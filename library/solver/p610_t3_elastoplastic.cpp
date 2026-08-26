/**
 * p610_t3 - Plane-strain elastic-plastic (Mohr-Coulomb) analysis using
 *           3-node constant-strain triangles (T3). Viscoplastic method.
 *
 * Mirrors p610 (8-node quad) logic but uses T3 elements:
 *   nod=3, ndof=6, nst=4 (plane strain), nip=1 (centroid Gauss point)
 *
 * Two analysis modes selected by `layers` in the solver-control line:
 *   layers >  0 : staged excavation (geostatic in-situ + per-layer cuts).
 *   layers == 0 : INFINITE-MEDIUM MOHR-COULOMB analysis. Starts from zero
 *                 stress / zero displacement, applies outer-boundary
 *                 tractions in `incs` equal increments, runs MC viscoplastic
 *                 iterations to convergence (mocouf yield + mocouq plastic
 *                 potential). The wall is left FREE; far-field stress lives
 *                 on the outer arc as a Neumann BC. Per-material `psi` is
 *                 independent of `phi`, so ψ=φ gives associated flow and
 *                 ψ=0 gives non-associated flow with no other change.
 *                 Canonical example: Verification/mc_tunnel/mc_tunnel.dat
 *
 * Input (.dat):
 *   Line 1 : nels  nn  np_types
 *   Block 2: 7 x np_types material properties (row-major)
 *            row order: phi  c  psi  gamma  E  v  K0
 *   Block 3: etype(1..nels)   [omitted when np_types==1]
 *   Block 4: nn rows  x y  (nodal coordinates)
 *   Block 5: nels rows  n1 n2 n3  (connectivity, 1-based)
 *   Line 6 : nr
 *            nr lines: node  fix_x  fix_y   (0=restrained, 1=free)
 *   Line 7 : nouts
 *            nouts output node numbers
 *   Line 8 : tol  limit  incs  layers
 *   When layers >  0  (staged excavation):
 *     Per layer:
 *       noexe
 *       noexe element numbers (1-based)
 *   When layers == 0  (infinite-medium MC):
 *     n_traction_groups
 *     Per group:
 *       mode  p0  p1  p2
 *         mode=0  direct:    sxx=p0, syy=p1, sxy=p2
 *         mode=1  far-field: syy=p0, sxx=p1*p0, sxy=0
 *       n_edges
 *       n_edges rows: n1 n2   (1-based, CCW so outward normal = right-hand)
 *
 * Output:
 *   <name>.res   - text log (stage-0 + per-layer iterations + output nodes,
 *                  or per-increment iterations + wall convergence in MC mode)
 *   <name>.json  - nodes, elements, stages[0]=zero/in-situ, stages[1..]=after
 *                  each cut OR after MC convergence. JSON `meta` block carries
 *                  tunnel geometry, material, far-field loading, and (for
 *                  layers==0) Mohr-Coulomb constants for downstream tools.
 */

#include <algorithm>
#include <array>
#include <cctype>
#include <cmath>
#include <cstdlib>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <map>
#include <set>
#include <sstream>
#include <string>
#include <utility>
#include <vector>

#include "checon.hpp"
#include "deemat.hpp"
#include "fkdiag.hpp"
#include "formm.hpp"
#include "fsparv.hpp"
#include "invar.hpp"
#include "mocouf.hpp"
#include "mocouq.hpp"
#include "num_to_g.hpp"
#include "spabac.hpp"
#include "sparin.hpp"
#include "formnf.hpp"
#include "p610_t3_entry.hpp"
#include "slope_constitutive.hpp"
#include "../io/geotech_dat_io.h"
#include "slope_pseudo_static.h"
#include "edge_distributed_load.hpp"
#include "nodal_point_load.hpp"
#include "groundwater_pore_pressure.hpp"
#include "support_elements.hpp"
#include "joint_elements.hpp"

using slope_constitutive::FailureMode;
using slope_constitutive::MaterialLaw;
using slope_constitutive::parse_failure_mode;
using slope_constitutive::parse_material_law;
using slope_constitutive::yield_value;
using slope_constitutive::plastic_potential_derivs;
using slope_constitutive::apply_strength_reduction;
using slope_constitutive::is_slope_stability_srf_executable;


static bool iequals_opt(const std::string& a, const std::string& b)
{
    if (a.size() != b.size()) return false;
    for (size_t i = 0; i < a.size(); ++i) {
        if (std::tolower(static_cast<unsigned char>(a[i])) !=
            std::tolower(static_cast<unsigned char>(b[i])))
            return false;
    }
    return true;
}

/// Mechanical axisymmetric (S&G): x=r, y=z, εθ=u_r/r, integrate × r.
static bool g_axisymmetric = false;

static bool resolve_axisymmetric_mode(const std::string& dat_path)
{
#ifdef SLOPE_SRF_AXISYM
    (void)dat_path;
    return true;
#else
    const std::string t = geotech_io::read_analysis_option(dat_path, "analysis_type");
    if (iequals_opt(t, "axisymmetric") || iequals_opt(t, "axisym")) {
        std::cerr << "warning: analysis_type axisymmetric ignored by plane SRF executable; "
                     "use slope_stability_*_SRF_axisym\n";
    }
    return false;
#endif
}

static double gp_int_weight(double detJ, double w, double radial_r)
{
    return std::abs(detJ) * w * radial_r;
}
static std::string g_solver_name = "p610_t3_elastoplastic";

// ============================================================================
//  T3 element: constant-strain triangle, plane strain (nst=4)
//  coord[3][2]: row=node, col={x,y}
//  bee [4][6]: row=strain, col=DOF (u1,v1,u2,v2,u3,v3)
//  area: element area (output)
// ============================================================================
static void bee3(std::vector<std::vector<double>>& bee,
                 const std::vector<std::vector<double>>& coord,
                 double& area)
{
    const double x1=coord[0][0], y1=coord[0][1];
    const double x2=coord[1][0], y2=coord[1][1];
    const double x3=coord[2][0], y3=coord[2][1];

    double det2 = (x2-x1)*(y3-y1) - (x3-x1)*(y2-y1);
    area = 0.5 * std::abs(det2);
    if (std::abs(det2) < 1e-20)
        det2 = (det2 >= 0.0) ? 1e-20 : -1e-20;

    // Global shape-function derivatives (standard area-coordinate formulas)
    const double b1=(y2-y3)/det2, b2=(y3-y1)/det2, b3=(y1-y2)/det2;
    const double c1=(x3-x2)/det2, c2=(x1-x3)/det2, c3=(x2-x1)/det2;

    for (int i=0;i<4;++i) for (int j=0;j<6;++j) bee[i][j]=0.0;
    bee[0][0]=b1; bee[0][2]=b2; bee[0][4]=b3;   // eps_x
    bee[1][1]=c1; bee[1][3]=c2; bee[1][5]=c3;   // eps_y
    bee[2][0]=c1; bee[2][1]=b1; bee[2][2]=c2;   // gamma_xy
    bee[2][3]=b2; bee[2][4]=c3; bee[2][5]=b3;
    // row 3 (eps_z) = 0 for plane strain
}

// ============================================================================
//  exc_nods for T3 (3-nod). Marks DOFs of orphaned excavated nodes as 0.
//  g_num[nod][nels] column-major, 1-based node indices.
//  nf[nodof*nn] flat column-major.
// ============================================================================
static void exc_nods_t3(int noexe,
                        const std::vector<int>& exele,   // 0-based
                        const std::vector<std::vector<int>>& g_num,
                        std::vector<int>& totex,
                        int& ntote,
                        std::vector<int>& nf,
                        int nodof, int nn)
{
    const int nod  = static_cast<int>(g_num.size());
    const int nels = static_cast<int>(g_num[0].size());
    ntote += noexe;
    for (int i=0;i<noexe;++i)
        totex[ntote-noexe+i] = exele[i];

    for (int i=0;i<noexe;++i) {
        int kel = exele[i];
        for (int k=0;k<nod;++k) {
            int ncheck = g_num[k][kel];
            bool nodex = false;
            for (int iel=0;iel<nels && !nodex;++iel) {
                bool modex = false;
                for (int jj=0;jj<ntote;++jj)
                    if (iel==totex[jj]) { modex=true; break; }
                if (modex) continue;
                for (int jj=0;jj<nod;++jj)
                    if (ncheck==g_num[jj][iel]) { nodex=true; break; }
            }
            if (!nodex && ncheck>0 && ncheck<=nn)
                for (int d=0;d<nodof;++d)
                    nf[d + (ncheck-1)*nodof] = 0;
        }
    }
}

// ============================================================================
//  Matrix helpers (avoid heavy dependencies)
// ============================================================================
static std::vector<double>
matvec(const std::vector<std::vector<double>>& A, const std::vector<double>& x)
{
    int m=(int)A.size(), n=(int)x.size();
    std::vector<double> y(m,0.0);
    for (int i=0;i<m;++i) for (int j=0;j<n;++j) y[i]+=A[i][j]*x[j];
    return y;
}

// y = A^T * x
static std::vector<double>
matvec_T(const std::vector<std::vector<double>>& A, const std::vector<double>& x)
{
    int cols=(int)A[0].size(), rows=(int)A.size();
    std::vector<double> y(cols,0.0);
    for (int j=0;j<rows;++j) for (int i=0;i<cols;++i) y[i]+=A[j][i]*x[j];
    return y;
}

static std::vector<std::vector<double>>
matmul(const std::vector<std::vector<double>>& A,
       const std::vector<std::vector<double>>& B)
{
    int m=(int)A.size(), p=(int)B.size(), n=(int)B[0].size();
    std::vector<std::vector<double>> C(m,std::vector<double>(n,0.0));
    for (int i=0;i<m;++i) for (int k=0;k<p;++k) if (A[i][k]!=0.0)
        for (int j=0;j<n;++j) C[i][j]+=A[i][k]*B[k][j];
    return C;
}

// km += bee^T * dee * bee * scale
static void accum_ke(std::vector<std::vector<double>>& km,
                     const std::vector<std::vector<double>>& bee,
                     const std::vector<std::vector<double>>& dee,
                     double scale)
{
    auto db = matmul(dee, bee);
    int ndof=(int)bee[0].size(), nst=(int)bee.size();
    for (int i=0;i<ndof;++i) for (int j=0;j<ndof;++j) {
        double s=0.0;
        for (int k=0;k<nst;++k) s+=bee[k][i]*db[k][j];
        km[i][j]+=s*scale;
    }
}

// ============================================================================
//  JSON output helper
//  `nodes`  : final cumulative displacements (same as last `stages` entry)
//  `stages` : one object per excavated layer, cumulative u at end of that stage
//             (separate from final `nodes` for staged plots)
// ============================================================================
struct StageJson {
    int    index{0};
    int    neq{0};
    int    elements_solid{0};
    int    removed{0};
    std::vector<double> d;     // nodof*nn, same layout as tot_d
    // nst(=4) values per element: sxx, syy, tau_xy, szz  (tension+ per Smith-Griffiths; compression negative)
    std::vector<double> sigma;
    std::vector<double> u_kPa;
    std::vector<double> h_total_m;
    std::vector<double> h_pressure_m;
    std::vector<double> yield_ratio;
    std::vector<double> plastic_strain;
    // Total strain from B·u (3 per element: exx, eyy, gamma_xy) + peak γmax over GPs.
    std::vector<double> strain;
    std::vector<double> gamma_max;
};

// Optional metadata block (see p610_t3_elastic.cpp for full description).
//
// `loading` and `mohr_coulomb` are populated only by the layers==0 (infinite-
// medium MC) path; in that mode `sigma_v_kPa` is in compression-positive
// convention (the .dat is tension-positive, so trac syy is negated on emit).
struct MetaBlock {
    bool   valid_tunnel  {false};
    bool   valid_material{false};
    bool   valid_loading {false};
    bool   valid_mc      {false};
    double cx{0.0}, cy{0.0}, a{0.0};
    double E_kPa{0.0}, nu{0.0};
    double sigma_v_kPa{0.0}, K0{1.0};
    double phi_deg{0.0}, c_kPa{0.0}, psi_deg{0.0}, gamma{0.0};

    // M2.2 Option B: strength-reduction-factor (SRF) state.
    // When the .dat AnalysisOptions block declares `! srf_F X.X`, the solver
    // scales every material's phi/c/psi by F before the in-situ + excavation
    // solve (phi -> atan(tan phi / F), c -> c / F, psi -> atan(tan psi / F)).
    // Post-solve diagnostics are written under `meta.srf` so the orchestrator
    // can sweep F across multiple .dat copies and locate the FE limit-load FoS
    // (the F at which max_yield_ratio first saturates and / or max_plastic_strain
    // first jumps super-linearly). A future bisection implementation will set
    // `fos` to the converged FoS rather than echoing F_trial.
    bool   valid_srf     {false};
    double srf_F_trial   {0.0};
    double srf_fos       {0.0};
    double srf_phi_orig  {0.0};
    double srf_c_orig    {0.0};
    double srf_max_yield_ratio  {0.0};
    double srf_max_displacement {0.0};
    double srf_max_plastic_strain {0.0};
    bool   srf_all_increments_converged {false};
    int    srf_max_iters_used  {0};
    int    srf_iteration_limit {0};
};

// Algebraic least-squares circle fit (mirrors the helper in p610_t3_elastic.cpp).
static bool fit_circle_lsq_t3(const std::vector<int>& nids,
                              const std::vector<std::vector<double>>& g_coord,
                              double& cx, double& cy, double& a, double& res_std)
{
    const int n = (int)nids.size();
    if (n < 3) return false;

    double Sx=0, Sy=0, Sxx=0, Syy=0, Sxy=0;
    double Sxxx=0, Syyy=0, Sxyy=0, Sxxy=0;
    for (int id : nids) {
        const double x = g_coord[id][0], y = g_coord[id][1];
        Sx += x; Sy += y;
        Sxx += x*x; Syy += y*y; Sxy += x*y;
        Sxxx += x*x*x; Syyy += y*y*y; Sxyy += x*y*y; Sxxy += x*x*y;
    }
    double M[3][3] = {{Sxx, Sxy, Sx},
                      {Sxy, Syy, Sy},
                      {Sx,  Sy,  (double)n}};
    double b[3] = { -(Sxxx + Sxyy),
                    -(Sxxy + Syyy),
                    -(Sxx  + Syy ) };

    auto det3 = [](const double m[3][3]) {
        return m[0][0]*(m[1][1]*m[2][2]-m[1][2]*m[2][1])
             - m[0][1]*(m[1][0]*m[2][2]-m[1][2]*m[2][0])
             + m[0][2]*(m[1][0]*m[2][1]-m[1][1]*m[2][0]);
    };
    const double det = det3(M);
    if (std::abs(det) < 1e-20) return false;

    auto solve_col = [&](int col) {
        double Mc[3][3] = {{M[0][0],M[0][1],M[0][2]},
                           {M[1][0],M[1][1],M[1][2]},
                           {M[2][0],M[2][1],M[2][2]}};
        Mc[0][col] = b[0]; Mc[1][col] = b[1]; Mc[2][col] = b[2];
        return det3(Mc) / det;
    };
    const double D = solve_col(0);
    const double E = solve_col(1);
    const double F = solve_col(2);

    cx = -0.5 * D;
    cy = -0.5 * E;
    const double r2 = cx*cx + cy*cy - F;
    if (r2 <= 0.0) return false;
    a = std::sqrt(r2);

    double mean_r = 0.0;
    std::vector<double> rs(n);
    for (int i = 0; i < n; ++i) {
        const double x = g_coord[nids[i]][0], y = g_coord[nids[i]][1];
        rs[i] = std::sqrt((x-cx)*(x-cx) + (y-cy)*(y-cy));
        mean_r += rs[i];
    }
    mean_r /= n;
    double var = 0.0;
    for (int i = 0; i < n; ++i) var += (rs[i]-mean_r)*(rs[i]-mean_r);
    res_std = std::sqrt(var / n);
    return true;
}

static bool circle_through_3_t3(double ax, double ay, double bx, double by,
                                double cxp, double cyp,
                                double& cx, double& cy, double& r)
{
    const double d = 2.0 * (ax*(by - cyp) + bx*(cyp - ay) + cxp*(ay - by));
    if (std::abs(d) < 1e-20) return false;
    const double a2 = ax*ax + ay*ay;
    const double b2 = bx*bx + by*by;
    const double c2 = cxp*cxp + cyp*cyp;
    cx = (a2*(by - cyp) + b2*(cyp - ay) + c2*(ay - by)) / d;
    cy = (a2*(cxp - bx) + b2*(ax - cxp) + c2*(bx - ax)) / d;
    r  = std::sqrt((ax - cx)*(ax - cx) + (ay - cy)*(ay - cy));
    return true;
}

// RANSAC tunnel circle detection (see p610_t3_elastic.cpp for full rationale).
static bool detect_tunnel_circle_t3(const std::vector<int>& seeds,
                                    const std::vector<std::vector<double>>& g_coord,
                                    double& out_cx, double& out_cy, double& out_a)
{
    const int n = (int)seeds.size();
    if (n < 3) return false;

    double xmin = 1e300, xmax = -1e300, ymin = 1e300, ymax = -1e300;
    for (const auto& p : g_coord) {
        if (p[0] < xmin) xmin = p[0]; if (p[0] > xmax) xmax = p[0];
        if (p[1] < ymin) ymin = p[1]; if (p[1] > ymax) ymax = p[1];
    }
    const double bx = xmax - xmin, by = ymax - ymin;
    const double bbox_max = std::max(bx, by);
    if (!(bbox_max > 0.0)) return false;
    const double tol_abs = 0.01 * bbox_max;
    const double max_r   = 0.6  * bbox_max;

    constexpr int MAX_SAMPLE = 80;
    std::vector<int> sample;
    if (n <= MAX_SAMPLE) {
        sample = seeds;
    } else {
        sample.reserve(MAX_SAMPLE);
        for (int i = 0; i < MAX_SAMPLE; ++i)
            sample.push_back(seeds[(int)((long long)i * n / MAX_SAMPLE)]);
    }
    const int N = (int)sample.size();

    constexpr int MIN_INLIERS = 4;

    int best_inliers = 0;
    double best_cx = 0.0, best_cy = 0.0, best_r = 1e30;

    for (int i = 0; i < N; ++i) {
        const double xi = g_coord[sample[i]][0], yi = g_coord[sample[i]][1];
        for (int j = i + 1; j < N; ++j) {
            const double xj = g_coord[sample[j]][0], yj = g_coord[sample[j]][1];
            for (int k = j + 1; k < N; ++k) {
                const double xk = g_coord[sample[k]][0], yk = g_coord[sample[k]][1];
                double cx, cy, r;
                if (!circle_through_3_t3(xi, yi, xj, yj, xk, yk, cx, cy, r)) continue;
                if (!(r > 0.0) || r > max_r) continue;

                int inliers = 0;
                for (int s : seeds) {
                    const double dx = g_coord[s][0] - cx, dy = g_coord[s][1] - cy;
                    if (std::abs(std::sqrt(dx*dx + dy*dy) - r) < tol_abs) ++inliers;
                }
                if (inliers > best_inliers ||
                    (inliers == best_inliers && r < best_r)) {
                    best_inliers = inliers;
                    best_cx = cx; best_cy = cy; best_r = r;
                }
            }
        }
    }
    if (best_inliers < MIN_INLIERS) return false;

    std::vector<int> inlier_ids;
    for (int s : seeds) {
        const double dx = g_coord[s][0] - best_cx, dy = g_coord[s][1] - best_cy;
        if (std::abs(std::sqrt(dx*dx + dy*dy) - best_r) < tol_abs) inlier_ids.push_back(s);
    }
    double cx, cy, r, res;
    if (fit_circle_lsq_t3(inlier_ids, g_coord, cx, cy, r, res) && r > 0.0 && res / r < 0.05) {
        out_cx = cx; out_cy = cy; out_a = r;
    } else {
        out_cx = best_cx; out_cy = best_cy; out_a = best_r;
    }
    return true;
}

static void copy_stress_to_flat(const std::vector<std::vector<std::vector<double>>>& tensor,
                                int nst, int nels, std::vector<double>& out)
{
    out.resize(nst * nels);
    for (int el = 0; el < nels; ++el)
        for (int s = 0; s < nst; ++s) out[s + el * nst] = tensor[s][0][el];
}

static void element_centroid_xy(
    const std::vector<std::vector<double>>& g_coord,
    const std::vector<int>& num,
    int nod,
    double& xg,
    double& yg)
{
    xg = 0.0;
    yg = 0.0;
    for (int k = 0; k < nod; ++k) {
        const int ni = num[k] - 1;
        xg += g_coord[ni][0];
        yg += g_coord[ni][1];
    }
    const double inv = 1.0 / static_cast<double>(std::max(1, nod));
    xg *= inv;
    yg *= inv;
}

static double yield_from_stress_gw(
    const std::vector<double>& stress,
    const groundwater::Spec& gw,
    double gw_u_factor,
    double xg,
    double yg,
    FailureMode failure_mode,
    MaterialLaw material_law,
    double phi,
    double c,
    double& sigm,
    double& dsbar,
    double& lode,
    std::vector<double>* yield_stress = nullptr)
{
    std::vector<double> sig_eff = stress;
    double sigm_y = 0.0, dsbar_y = 0.0, lode_y = 0.0;
    if (gw.active() && gw_u_factor > 0.0) {
        const double u = groundwater::pore_pressure_at(xg, yg, gw, stress.data());
        groundwater::apply_effective_stress(sig_eff.data(), u, gw_u_factor);
    }
    fem::invar(sig_eff, &sigm_y, &dsbar_y, &lode_y);
    if (yield_stress != nullptr) *yield_stress = sig_eff;
    sigm = sigm_y;
    dsbar = dsbar_y;
    lode = lode_y;
    return yield_value(failure_mode, material_law, phi, c, sigm, dsbar, lode);
}

/// Viscoplastic plastic-strain increment for one CST state.
/// Yield, potential derivatives, and formm all use the same stress state that
/// enters the yield function (Terzaghi effective when GW is active). Tiny mean
/// stress is clamped so formm's 1/(9σm) term stays finite — never skip the
/// plastic correction when f≥0 (the old |σm|<ε guard left surface GPs elastic
/// while still yielding).
static bool viscoplastic_strain_increment(
    const std::vector<double>& stress_total,
    const groundwater::Spec& gw,
    double gw_u_factor,
    double xg,
    double yg,
    FailureMode failure_mode,
    MaterialLaw material_law,
    double phi_deg,
    double c,
    double psi_deg,
    double dt,
    const std::vector<std::vector<double>>& /*dee*/,
    std::vector<std::vector<double>>& m1,
    std::vector<std::vector<double>>& m2,
    std::vector<std::vector<double>>& m3,
    std::vector<std::vector<double>>& flow,
    std::vector<double>& evp_out,
    double& f_out)
{
    const int nst = static_cast<int>(stress_total.size());
    std::vector<double> yield_stress;
    double sigm_y = 0.0, dsbar_y = 0.0, lode_y = 0.0;
    f_out = yield_from_stress_gw(
        stress_total, gw, gw_u_factor, xg, yg,
        failure_mode, material_law, phi_deg, c, sigm_y, dsbar_y, lode_y,
        &yield_stress);
    if (f_out < 0.0) {
        evp_out.assign(static_cast<size_t>(nst), 0.0);
        return false;
    }

    double dq1 = 0.0, dq2 = 0.0, dq3 = 0.0;
    plastic_potential_derivs(
        failure_mode, material_law, psi_deg, dsbar_y, lode_y, dq1, dq2, dq3);

    std::vector<double> formm_stress = yield_stress;
    if (std::abs(sigm_y) < 1.0e-3) {
        // Embed slightly into compression so formm stays regular.
        formm_stress[0] -= 1.0e-3;
        formm_stress[1] -= 1.0e-3;
        if (nst > 3) formm_stress[3] -= 1.0e-3;
    }
    fem::formm(formm_stress, m1, m2, m3);
    for (int a = 0; a < nst; ++a)
        for (int b = 0; b < nst; ++b)
            flow[a][b] = f_out * (m1[a][b] * dq1 + m2[a][b] * dq2 + m3[a][b] * dq3);

    auto erate = matvec(flow, formm_stress);
    evp_out.resize(static_cast<size_t>(nst));
    for (int s = 0; s < nst; ++s)
        evp_out[static_cast<size_t>(s)] = erate[static_cast<size_t>(s)] * dt;
    return true;
}

// M2.2 Option B helper. Walks the final stage's stress / displacement / plastic
// strain tensors and stores the SRF diagnostics in `meta` so write_json can
// emit them under `meta.srf`. Called by both the layers==0 and layers>=1 paths
// just before write_json. Mirrors the formulas in
//   tools/build_all_slope_smoke_packages.ps1
//   X2D_GUI/ViewModels/VerificationViewModel.cs (TryAutoRunSlopeStabilityComparison)
// so the orchestrator, GUI, and solver agree on what the numbers mean.
//
// `prop` here is the REDUCED prop (i.e. phi/c already divided by F_trial); the
// yield-ratio uses this reduced strength because the corresponding stress field
// is what the solver actually carried in `tensor`.
static void populate_srf_diagnostics(
    MetaBlock& meta,
    double F_trial,
    double phi_orig_deg,
    double c_orig_kPa,
    int max_iters_used,
    int iteration_limit,
    bool all_increments_converged,
    const std::vector<std::vector<std::vector<double>>>& tensor,
    const std::vector<std::vector<std::vector<double>>>& evpt,
    const std::vector<double>& tot_d,
    const std::vector<std::vector<double>>& prop,
    const std::vector<int>& etype,
    const std::vector<int>& solid,
    int nn, int nels, int nodof, int nst,
    const groundwater::Spec& gw,
    const std::vector<std::vector<double>>& g_coord,
    const std::vector<std::vector<int>>& g_num,
    int nod,
    double gw_u_factor)
{
    if (F_trial <= 0.0) return;
    const double pi_loc = std::acos(-1.0);

    double max_yield = 0.0;
    double max_plastic = 0.0;
    for (int el = 0; el < nels; ++el) {
        if (!solid.empty() && !solid[el]) continue;
        // Element principal stresses from cartesian Smith-Griffiths layout
        // (sxx, syy, txy, szz). Use Terzaghi effective stress when GW is active.
        std::vector<double> sig = {
            tensor[0][0][el], tensor[1][0][el], tensor[2][0][el],
            nst > 3 ? tensor[3][0][el] : 0.0};
        if (gw.active() && gw_u_factor > 0.0 && !g_coord.empty() && !g_num.empty()) {
            double xg = 0.0, yg = 0.0;
            std::vector<int> num(static_cast<size_t>(nod), 0);
            for (int k = 0; k < nod; ++k) num[static_cast<size_t>(k)] = g_num[k][el];
            element_centroid_xy(g_coord, num, nod, xg, yg);
            const double u = groundwater::pore_pressure_at(xg, yg, gw, sig.data());
            groundwater::apply_effective_stress(sig.data(), u, gw_u_factor);
        }
        double sxx = sig[0];
        double syy = sig[1];
        double txy = sig[2];
        double sm  = 0.5 * (sxx + syy);
        // Crest CST tensile mean stress is not a bulk SRF mechanism.
        if (sm > 0.0) continue;
        double rad = std::sqrt(0.25 * (sxx - syy) * (sxx - syy) + txy * txy);
        // s1 = sm + rad, s2 = sm - rad, sd = (s1 - s2) / 2 = rad
        int m = etype.empty() ? 0 : etype[el];
        double phi = prop[0][m] * pi_loc / 180.0;
        double c   = prop[1][m];
        double allow = c * std::cos(phi) - sm * std::sin(phi);
        if (allow > 1e-9) {
            double r = rad / allow;
            if (r > max_yield) max_yield = r;
        }

        // Plastic strain norm = sqrt(sum_s evpt[s][0][el]^2). evpt has nst=4
        // components matching the stress tensor layout (gamma_xy is engineering
        // shear strain, so squared without doubling).
        double pn = 0.0;
        for (int s = 0; s < nst; ++s) {
            double v = evpt[s][0][el];
            pn += v * v;
        }
        pn = std::sqrt(pn);
        if (pn > max_plastic) max_plastic = pn;
    }

    double max_disp = 0.0;
    for (int i = 0; i < nn; ++i) {
        double ux = tot_d[0 + i * nodof];
        double uy = tot_d[1 + i * nodof];
        double r  = std::sqrt(ux * ux + uy * uy);
        if (r > max_disp) max_disp = r;
    }

    meta.valid_srf                     = true;
    meta.srf_F_trial                   = F_trial;
    // Single-shot SRF: `fos` echoes F_trial. A future bisection wrapper will
    // overwrite this with the converged FoS while keeping F_trial as the last
    // tried reduction factor.
    meta.srf_fos                       = F_trial;
    meta.srf_phi_orig                  = phi_orig_deg;
    meta.srf_c_orig                    = c_orig_kPa;
    meta.srf_max_yield_ratio           = max_yield;
    meta.srf_max_displacement          = max_disp;
    meta.srf_max_plastic_strain        = max_plastic;
    meta.srf_max_iters_used            = max_iters_used;
    meta.srf_iteration_limit           = iteration_limit;
    // Belt-and-suspenders for GUI bracket search: a trial that finishes under
    // `limit` with bulk Mohr–Coulomb overstress is not a stable FoS state.
    // Threshold is deliberately high so near-critical but converged trials
    // (yield ratio often ~2–10) are not misclassified.
    bool conv = all_increments_converged;
    if (max_yield > 25.0)
        conv = false;
    meta.srf_all_increments_converged  = conv;
}

static void json_out_double(std::ostream& os, double v)
{
    if (std::isfinite(v))
        os << v;
    else
        os << 0.0;
}

static void populate_stage_gw_fields(
    StageJson& stage,
    const groundwater::Spec& gw,
    const std::vector<std::vector<double>>& g_coord,
    const std::vector<std::vector<int>>& g_num,
    int nod,
    int nels,
    int nst)
{
    stage.u_kPa.assign(static_cast<size_t>(nels), 0.0);
    stage.h_total_m.assign(static_cast<size_t>(nels), 0.0);
    stage.h_pressure_m.assign(static_cast<size_t>(nels), 0.0);
    if (!gw.active()) return;
    const size_t nSig = static_cast<size_t>(nst) * static_cast<size_t>(nels);
    if (stage.sigma.size() < nSig) return;
    std::vector<int> num(static_cast<size_t>(nod));
    for (int e = 0; e < nels; ++e) {
        for (int k = 0; k < nod; ++k) num[static_cast<size_t>(k)] = g_num[k][e];
        double xg = 0.0, yg = 0.0;
        element_centroid_xy(g_coord, num, nod, xg, yg);
        const int o = nst * e;
        const double sigma[4] = {
            stage.sigma[static_cast<size_t>(o + 0)],
            stage.sigma[static_cast<size_t>(o + 1)],
            stage.sigma[static_cast<size_t>(o + 2)],
            stage.sigma[static_cast<size_t>(o + 3)]};
        stage.u_kPa[static_cast<size_t>(e)] = groundwater::pore_pressure_at(xg, yg, gw, sigma);
        stage.h_pressure_m[static_cast<size_t>(e)] = groundwater::pressure_head_at(xg, yg, gw, sigma);
        stage.h_total_m[static_cast<size_t>(e)] = groundwater::total_head_at(xg, yg, gw, sigma);
    }
}

/// Per-element Mohr–Coulomb yield ratio and plastic-strain norm for Results contours.
static void populate_stage_yield_fields(
    StageJson& stage,
    const std::vector<std::vector<std::vector<double>>>& tensor,
    const std::vector<std::vector<std::vector<double>>>& evpt,
    const std::vector<std::vector<double>>& prop,
    const std::vector<int>& etype,
    const std::vector<int>& solid,
    int nels, int nst, int nod,
    const groundwater::Spec& gw,
    const std::vector<std::vector<double>>& g_coord,
    const std::vector<std::vector<int>>& g_num,
    double gw_u_factor)
{
    stage.yield_ratio.assign(static_cast<size_t>(nels), 0.0);
    stage.plastic_strain.assign(static_cast<size_t>(nels), 0.0);
    const double pi_loc = std::acos(-1.0);
    for (int el = 0; el < nels; ++el) {
        if (!solid.empty() && !solid[static_cast<size_t>(el)]) continue;
        std::vector<double> sig = {
            tensor[0][0][static_cast<size_t>(el)],
            tensor[1][0][static_cast<size_t>(el)],
            tensor[2][0][static_cast<size_t>(el)],
            nst > 3 ? tensor[3][0][static_cast<size_t>(el)] : 0.0};
        if (gw.active() && gw_u_factor > 0.0 && !g_coord.empty() && !g_num.empty()) {
            double xg = 0.0, yg = 0.0;
            std::vector<int> num(static_cast<size_t>(nod), 0);
            for (int k = 0; k < nod; ++k) num[static_cast<size_t>(k)] = g_num[k][el];
            element_centroid_xy(g_coord, num, nod, xg, yg);
            const double u = groundwater::pore_pressure_at(xg, yg, gw, sig.data());
            groundwater::apply_effective_stress(sig.data(), u, gw_u_factor);
        }
        const double sxx = sig[0];
        const double syy = sig[1];
        const double txy = sig[2];
        const double sm  = 0.5 * (sxx + syy);
        const double rad = std::sqrt(0.25 * (sxx - syy) * (sxx - syy) + txy * txy);
        if (sm <= 0.0) {
            const int m = etype.empty() ? 0 : etype[static_cast<size_t>(el)];
            const double phi = prop[0][static_cast<size_t>(m)] * pi_loc / 180.0;
            const double c   = prop[1][static_cast<size_t>(m)];
            const double allow = c * std::cos(phi) - sm * std::sin(phi);
            if (allow > 1e-9)
                stage.yield_ratio[static_cast<size_t>(el)] = rad / allow;
        }
        double pn = 0.0;
        for (int s = 0; s < nst; ++s) {
            const double v = evpt[static_cast<size_t>(s)][0][static_cast<size_t>(el)];
            pn += v * v;
        }
        stage.plastic_strain[static_cast<size_t>(el)] = std::sqrt(pn);
    }
}

static void write_joints_array(
    std::ostream& js,
    const joint_elements::Spec& spec,
    const std::vector<std::vector<double>>& g_coord,
    const std::vector<double>& tot_d,
    int nodof, int nn, int current_stage)
{
    auto getx = [&](int i) { return joint_elements::node_x_p610(g_coord, i); };
    auto gety = [&](int i) { return joint_elements::node_y_p610(g_coord, i); };
    std::vector<joint_elements::Eval> jres;
    joint_elements::evaluate(spec, getx, gety, tot_d, nodof, nn, jres, current_stage);
    js << "\"joints\":[\n";
    for (size_t ji = 0; ji < jres.size(); ++ji) {
        const auto& j = jres[ji];
        js << "    {\"n1\":" << j.n1 << ",\"n2\":" << j.n2
           << ",\"n3\":" << j.n3 << ",\"n4\":" << j.n4
           << ",\"un\":";
        json_out_double(js, j.un);
        js << ",\"us\":";
        json_out_double(js, j.us);
        js << ",\"closure\":";
        json_out_double(js, j.closure);
        js << ",\"sn_kPa\":";
        json_out_double(js, j.sn_kPa);
        js << ",\"tau_kPa\":";
        json_out_double(js, j.tau_kPa);
        js << ",\"L\":";
        json_out_double(js, j.L);
        js << ",\"nx\":";
        json_out_double(js, j.nx);
        js << ",\"ny\":";
        json_out_double(js, j.ny);
        js << ",\"ux_minus\":";
        json_out_double(js, j.ux_minus);
        js << ",\"uy_minus\":";
        json_out_double(js, j.uy_minus);
        js << ",\"ux_plus\":";
        json_out_double(js, j.ux_plus);
        js << ",\"uy_plus\":";
        json_out_double(js, j.uy_plus);
        js << ",\"slipped\":" << (j.slipped ? "true" : "false") << "}";
        if (ji + 1 < jres.size()) js << ",\n";
        else js << "\n";
    }
    js << "  ]";
}

static void write_json(
    const std::string& base, int layer_count,
    const std::vector<double>& tot_d,
    const std::vector<StageJson>& stages,
    const std::vector<std::vector<double>>& g_coord,
    const std::vector<std::vector<int>>& g_num,
    const std::vector<int>& solid,
    const MetaBlock& meta,
    int nn, int nels, int nodof, int nod, int nst,
    bool has_pseudo_static = false,
    double ps_k_h = 0.0,
    double ps_k_v = 0.0,
    const std::vector<double>& ps_fx = {},
    const std::vector<double>& ps_fy = {},
    bool groundwater_active = false,
    const groundwater::Spec& gw = groundwater::Spec{},
    const joint_elements::Spec* joint_spec = nullptr,
    bool have_reactions = false,
    double reaction_fx = 0.0,
    double reaction_fy = 0.0)
{
    std::ofstream js(base + ".json");
    if (!js) return;
    js.setf(std::ios::showpoint);
    js << std::setprecision(17) << std::scientific;
    js << "{\n"
       << "  \"solver\":\"" << g_solver_name << "\","
       << " \"cumulative_meters_per_stage\":true,\n  \"layers\":" << layer_count
       << ",\n  \"n_stages\":" << stages.size()
       << ",\n  \"node_count\":" << nn << ",\n  \"element_count\":" << nels << ",\n";
    if (groundwater_active) {
        js << "  \"description\":\"`stages[0]` = in-situ geostatic stress at T3 centroids with hydrostatic pore pressure "
              "(Terzaghi effective stress in yield). `stages[1]..K` = after each excavation. Each entry has `nodes` "
              "(ux,uy) and `element_stresses` (sxx,syy,txy,szz plus in-plane principals s1>=s2 and angle theta1_deg "
              "measured CCW from +x to the s1 direction; Smith-Griffiths tension+ sign, same units as E/gamma in .dat). "
              "Top-level `nodes` = final cumulative U.\",\n";
    } else {
        js << "  \"description\":\"`stages[0]` = in-situ (u=0, geostatic stress at T3 centroids). "
              "`stages[1]..K` = after each excavation. Each entry has `nodes` (ux,uy) and `element_stresses` "
              "(sxx,syy,txy,szz plus in-plane principals s1>=s2 and angle theta1_deg measured CCW from +x to "
              "the s1 direction; Smith-Griffiths tension+ sign, same units as E/gamma in .dat). "
              "Top-level `nodes` = final cumulative U.\",\n";
    }

    if (meta.valid_tunnel || meta.valid_material ||
        meta.valid_loading || meta.valid_mc || meta.valid_srf) {
        js << "  \"meta\":{";
        bool first = true;
        auto sep = [&](){ if (!first) js << ","; first = false; };
        if (meta.valid_tunnel) {
            sep();
            js << "\"tunnel\":{\"cx\":" << meta.cx
               << ",\"cy\":" << meta.cy
               << ",\"a\":"  << meta.a << "}";
        }
        if (meta.valid_material) {
            sep();
            js << "\"material\":{\"E_kPa\":" << meta.E_kPa
               << ",\"nu\":"                << meta.nu << "}";
        }
        if (meta.valid_loading) {
            sep();
            js << "\"loading\":{\"sigma_v_kPa\":" << meta.sigma_v_kPa
               << ",\"K0\":"                     << meta.K0
               << ",\"convention\":\"compression_positive\"}";
        }
        if (meta.valid_mc) {
            sep();
            js << "\"mohr_coulomb\":{\"phi_deg\":" << meta.phi_deg
               << ",\"c_kPa\":"                   << meta.c_kPa
               << ",\"psi_deg\":"                 << meta.psi_deg
               << ",\"gamma\":"                   << meta.gamma << "}";
        }
        if (meta.valid_srf) {
            sep();
            js << "\"srf\":{\"F_trial\":";
            json_out_double(js, meta.srf_F_trial);
            js << ",\"fos\":";
            json_out_double(js, meta.srf_fos);
            js << ",\"phi_orig_deg\":";
            json_out_double(js, meta.srf_phi_orig);
            js << ",\"c_orig_kPa\":";
            json_out_double(js, meta.srf_c_orig);
            js << ",\"max_yield_ratio\":";
            json_out_double(js, meta.srf_max_yield_ratio);
            js << ",\"max_disp\":";
            json_out_double(js, meta.srf_max_displacement);
            js << ",\"max_plastic_strain\":";
            json_out_double(js, meta.srf_max_plastic_strain);
            js << ",\"max_iters_used\":"  << meta.srf_max_iters_used
               << ",\"iteration_limit\":" << meta.srf_iteration_limit
               << ",\"all_increments_converged\":" << (meta.srf_all_increments_converged ? "true" : "false")
               << "}";
        }
        js << "},\n";
    }

    auto write_nodes = [&](const std::vector<double>& d) {
        for (int i = 0; i < nn; ++i) {
            js << "        {"
               << "\"id\":" << (i + 1)
               << ",\"x\":";
            json_out_double(js, g_coord[i][0]);
            js << ",\"y\":";
            json_out_double(js, g_coord[i][1]);
            js << ",\"ux\":";
            json_out_double(js, d[0 + (i) * nodof]);
            js << ",\"uy\":";
            json_out_double(js, d[1 + (i) * nodof]);
            js << "}";
            if (i < nn - 1) js << ",\n";
            else js << "\n";
        }
    };

    // Top-level `nodes` = final (backward compatible for tools that do not use `stages`)
    js << "  \"nodes\":[\n";
    write_nodes(tot_d);
    js << "  ],\n";

    // Per-excavation cumulative displacement field
    js << "  \"stages\":[\n";
    for (size_t si = 0; si < stages.size(); ++si) {
        const StageJson& st = stages[si];
        js << "    {"
           << "\"index\":" << st.index
           << ",\"name\":";
        if (st.index == 0) js << "\"in_situ_pre_excavation\"";
        else js << "\"after_excavation_" << st.index << "\"";
        js << ",\"neq\":" << st.neq
           << ",\"elements_solid\":" << st.elements_solid
           << ",\"removed\":" << st.removed
           << ",\n        \"nodes\":[\n";
        for (int i = 0; i < nn; ++i) {
            js << "        {"
               << "\"id\":" << (i + 1)
               << ",\"x\":";
            json_out_double(js, g_coord[i][0]);
            js << ",\"y\":";
            json_out_double(js, g_coord[i][1]);
            js << ",\"ux\":";
            json_out_double(js, st.d[0 + (i) * nodof]);
            js << ",\"uy\":";
            json_out_double(js, st.d[1 + (i) * nodof]);
            js << "}";
            if (i < nn - 1) js << ",\n";
            else js << "\n";
        }
        js << "        ]";
        // centroid stresses, one T3 = one value (constant strain)
        const size_t nSig = (size_t)nst * (size_t)nels;
        if (st.sigma.size() >= nSig) {
            js << ",\n        \"element_stresses\":[\n";
            for (int e = 0; e < nels; ++e) {
                int o = nst * e;
                double sxx = st.sigma[o + 0];
                double syy = st.sigma[o + 1];
                double txy = st.sigma[o + 2];
                double szz = st.sigma[o + 3];
                // In-plane principal stresses (plane strain). s1 >= s2 (tension+ convention).
                double sm = 0.5 * (sxx + syy);
                double sd = 0.5 * (sxx - syy);
                double r  = std::sqrt(sd * sd + txy * txy);
                double s1 = sm + r;
                double s2 = sm - r;
                // Angle of s1 from +x axis (CCW, degrees). atan2(2*txy, sxx-syy)/2.
                double theta1_deg = 0.5 * std::atan2(2.0 * txy, sxx - syy) * (180.0 / 3.14159265358979323846);
                js << "        {"
                   << "\"id\":" << (e + 1)
                   << ",\"sxx\":";
                json_out_double(js, sxx);
                js << ",\"syy\":";
                json_out_double(js, syy);
                js << ",\"txy\":";
                json_out_double(js, txy);
                js << ",\"szz\":";
                json_out_double(js, szz);
                js << ",\"s1\":";
                json_out_double(js, s1);
                js << ",\"s2\":";
                json_out_double(js, s2);
                js << ",\"theta1_deg\":";
                json_out_double(js, theta1_deg);
                // Always emit hydrostatic/Ru pore pressure + Terzaghi σ' when GW is active
                // (not gated by optional water-pressure-grid export flags).
                if (groundwater_active && st.u_kPa.size() > static_cast<size_t>(e)) {
                    const double u = st.u_kPa[static_cast<size_t>(e)];
                    js << ",\"u_kPa\":";
                    json_out_double(js, u);
                    double se[4] = {sxx, syy, txy, szz};
                    groundwater::apply_effective_stress(se, u);
                    js << ",\"sxx_eff_kPa\":";
                    json_out_double(js, se[0]);
                    js << ",\"syy_eff_kPa\":";
                    json_out_double(js, se[1]);
                    js << ",\"sxy_eff_kPa\":";
                    json_out_double(js, se[2]);
                }
                if (gw.static_grid_pressure_head && st.h_pressure_m.size() > static_cast<size_t>(e)) {
                    js << ",\"h_pressure_m\":";
                    json_out_double(js, st.h_pressure_m[static_cast<size_t>(e)]);
                }
                if (gw.static_grid_total_head && st.h_total_m.size() > static_cast<size_t>(e)) {
                    js << ",\"h_total_m\":";
                    json_out_double(js, st.h_total_m[static_cast<size_t>(e)]);
                }
                if (st.yield_ratio.size() > static_cast<size_t>(e)) {
                    js << ",\"yield_ratio\":";
                    json_out_double(js, st.yield_ratio[static_cast<size_t>(e)]);
                }
                if (st.plastic_strain.size() > static_cast<size_t>(e)) {
                    js << ",\"plastic_strain\":";
                    json_out_double(js, st.plastic_strain[static_cast<size_t>(e)]);
                }
                if (st.strain.size() >= static_cast<size_t>(3 * (e + 1))) {
                    const size_t so = static_cast<size_t>(3 * e);
                    js << ",\"exx\":";
                    json_out_double(js, st.strain[so + 0]);
                    js << ",\"eyy\":";
                    json_out_double(js, st.strain[so + 1]);
                    js << ",\"gamma_xy\":";
                    json_out_double(js, st.strain[so + 2]);
                }
                if (st.gamma_max.size() > static_cast<size_t>(e)) {
                    js << ",\"gamma_max\":";
                    json_out_double(js, st.gamma_max[static_cast<size_t>(e)]);
                }
                js << "}";
                if (e < nels - 1) js << ",\n";
                else js << "\n";
            }
            js << "        ]";
        }
        if (joint_spec && joint_spec->active()) {
            js << ",\n        ";
            write_joints_array(js, *joint_spec, g_coord, st.d, nodof, nn, std::max(1, st.index));
        }
        js << "\n    }";
        if (si + 1 < stages.size()) js << ",\n";
        else js << "\n";
    }
    js << "  ],\n";

    if (has_pseudo_static && ps_fx.size() >= static_cast<size_t>(nn)
        && ps_fy.size() >= static_cast<size_t>(nn)) {
        js << "  \"pseudo_static\":{\"k_h\":";
        json_out_double(js, ps_k_h);
        js << ",\"k_v\":";
        json_out_double(js, ps_k_v);
        js << "},\n  \"pseudo_static_nodal_forces\":[\n";
        for (int i = 0; i < nn; ++i) {
            js << "    {\"id\":" << (i + 1) << ",\"fx\":";
            json_out_double(js, ps_fx[static_cast<size_t>(i)]);
            js << ",\"fy\":";
            json_out_double(js, ps_fy[static_cast<size_t>(i)]);
            js << "}";
            if (i < nn - 1) js << ",\n";
            else js << "\n";
        }
        js << "  ],\n";
    }

    js << "  \"elements\":[\n";
    for (int i = 0; i < nels; ++i) {
        js << "    {"
           << "\"id\":" << (i + 1);
        for (int k = 0; k < nod; ++k) js << ",\"n" << (k + 1) << "\":" << g_num[k][i];
        js << ",\"active\":" << solid[i] << "}";
        if (i < nels - 1) js << ",\n";
        else js << "\n";
    }
    js << "  ]";

    if (joint_spec && joint_spec->active()) {
        js << ",\n  ";
        write_joints_array(js, *joint_spec, g_coord, tot_d, nodof, nn, 1);
    }

    if (have_reactions) {
        js << ",\n  \"reactions\":{\"fx\":";
        json_out_double(js, reaction_fx);
        js << ",\"fy\":";
        json_out_double(js, reaction_fy);
        js << "}";
    }

    js << "\n}\n";
}

// ============================================================================
//  Field Stress — Constant / Gravity (replaces legacy K0·γ·depth loop)
//
//  .dat block format (inserted between Materials and Mesh):
//    ! <BEGIN:FieldStress>
//    ! mode_global  (0=Constant, 1=Gravity)
//    1
//    ! Constant: sigma1 sigma3 sigmaZ angle_deg
    //    ! Gravity : Kx Kz y_surface use_ratio   (y_surface>1e29 → auto-detect)
//    0.5  0.5  1.0e30  0
//    ! n_overrides
//    0
//    ! per override: mat_idx(1-based)  mode  p0 p1 p2 p3
//    ! <END:FieldStress>
//
//  Backward compat: block absent → legacy gravity (K0 from prop[6][m]).
// ============================================================================
struct FieldStressSpec {
    enum class Mode { Gravity, Constant } mode = Mode::Gravity;
    double s1 = 0.0, s3 = 0.0, sZ = 0.0, angle_deg = 0.0; // Constant
    double Kx = 0.5, Kz = 0.5, y_surface = 1.0e30;         // Gravity
    bool   use_ratio = false;                                // Gravity effective-stress ratio
};

struct FieldStress {
    FieldStressSpec global;
    std::vector<std::pair<int,FieldStressSpec>> overrides; // 1-based mat-idx → spec
    bool is_legacy = true; // true when no FieldStress block found → use prop[6][m] as K0

    const FieldStressSpec& for_material(int m_zero) const {
        int m1 = m_zero + 1;
        for (const auto& ov : overrides)
            if (ov.first == m1) return ov.second;
        return global;
    }
};

static FieldStressSpec parse_fs_spec(std::stringstream& ss, int mode_flag)
{
    FieldStressSpec spec;
    spec.mode = (mode_flag == 0) ? FieldStressSpec::Mode::Constant
                                 : FieldStressSpec::Mode::Gravity;
    double p0 = 0.5, p1 = 0.5, p2 = 1.0e30, p3 = 0.0;
    ss >> p0 >> p1 >> p2 >> p3;
    if (spec.mode == FieldStressSpec::Mode::Constant) {
        spec.s1 = p0; spec.s3 = p1; spec.sZ = p2; spec.angle_deg = p3;
    } else {
        spec.Kx = p0; spec.Kz = p1; spec.y_surface = p2;
        spec.use_ratio = (p3 != 0.0);
    }
    return spec;
}

// Reads the ! <BEGIN:FieldStress> block directly from the raw .dat file
// (bypassing load_dat_stripped so markers are visible).
static FieldStress read_field_stress_block(const std::string& path)
{
    FieldStress fs;
    std::ifstream f(path);
    if (!f) return fs;

    std::string line;
    bool in_block = false;
    std::stringstream block;

    while (std::getline(f, line)) {
        const char* p = line.c_str();
        while (*p == ' ' || *p == '\t') ++p;
        if (std::strstr(p, "<BEGIN:FieldStress>")) { in_block = true;  continue; }
        if (std::strstr(p, "<END:FieldStress>"))   { break; }
        if (!in_block) continue;
        auto bang = line.find('!');
        if (bang != std::string::npos) line.erase(bang);
        block << line << '\n';
    }

    if (block.str().find_first_not_of(" \t\n\r") == std::string::npos)
        return fs; // no block → legacy fallback (is_legacy stays true)

    int mode_global = 1;
    block >> mode_global;
    fs.global = parse_fs_spec(block, mode_global);

    int n_overrides = 0;
    block >> n_overrides;
    for (int i = 0; i < n_overrides; ++i) {
        int mat_idx = 1, ov_mode = 1;
        block >> mat_idx >> ov_mode;
        fs.overrides.push_back({mat_idx, parse_fs_spec(block, ov_mode)});
    }

    fs.is_legacy = false;
    return fs;
}

// ============================================================================
//  main
// ============================================================================
//  Load .dat file, stripping `!`-prefixed comments so the wizard-emitted
//  `! <BEGIN:X>` / `! <END:X>` sentinel lines never poison the iostream
//  formatted-extraction state. Returns true on success.
// ============================================================================
static bool load_dat_stripped(const std::string& path, std::stringstream& out)
{
    std::ifstream src(path);
    if (!src) return false;
    std::string line;
    while (std::getline(src, line)) {
        auto bang = line.find('!');
        if (bang != std::string::npos) line.erase(bang);
        out << line << '\n';
    }
    return true;
}

static std::string trim_ws(const std::string& s)
{
    size_t b = 0, e = s.size();
    while (b < e && std::isspace(static_cast<unsigned char>(s[b]))) ++b;
    while (e > b && std::isspace(static_cast<unsigned char>(s[e - 1]))) --e;
    return s.substr(b, e - b);
}

static bool iequals(const std::string& a, const std::string& b)
{
    if (a.size() != b.size()) return false;
    for (size_t i = 0; i < a.size(); ++i)
        if (std::tolower(static_cast<unsigned char>(a[i])) != std::tolower(static_cast<unsigned char>(b[i])))
            return false;
    return true;
}

static std::string read_analysis_option(const std::string& path, const std::string& key)
{
    std::ifstream f(path);
    if (!f) return "";
    std::string line;
    bool in_block = false;
    while (std::getline(f, line)) {
        if (line.find("<BEGIN:AnalysisOptions>") != std::string::npos) { in_block = true; continue; }
        if (line.find("<END:AnalysisOptions>") != std::string::npos) break;
        if (!in_block) continue;
        auto bang = line.find('!');
        if (bang == std::string::npos) continue;
        std::string body = trim_ws(line.substr(bang + 1));
        if (body.rfind(key, 0) == 0) {
            std::string v = trim_ws(body.substr(key.size()));
            auto sp = v.find_first_of(" \t");
            if (sp != std::string::npos) v = v.substr(0, sp);
            return v;
        }
    }
    return "";
}

static int mesh_nodes_per_element(const std::string& mesh_type)
{
    if (iequals(mesh_type, "T6")) return 6;
    if (iequals(mesh_type, "Q4")) return 4;
    if (iequals(mesh_type, "Q8")) return 8;
    return 3; // T3 default
}

static int mesh_vertex_count(const std::string& mesh_type)
{
    if (iequals(mesh_type, "Q4") || iequals(mesh_type, "Q8")) return 4;
    return 3;
}

/// Corner-vertex edge topology for the free-surface envelope (ignores midsides).
static void mesh_corner_edges(const std::string& mesh_type,
                              std::vector<std::pair<int, int>>& edges)
{
    edges.clear();
    if (iequals(mesh_type, "Q4") || iequals(mesh_type, "Q8")) {
        edges.push_back({0, 1});
        edges.push_back({1, 2});
        edges.push_back({2, 3});
        edges.push_back({3, 0});
        return;
    }
    // T3 / T6
    edges.push_back({0, 1});
    edges.push_back({1, 2});
    edges.push_back({2, 0});
}

/// Piecewise-linear mesh free-surface: upper envelope of boundary nodes,
/// sorted by x with linear interpolation (no gaps that fall back to ymax).
struct MeshFreeSurface {
    std::vector<std::pair<double, double>> pts; // ascending x, upper y
    double y_fallback{0.0};

    double y_at(double x) const
    {
        if (pts.empty()) return y_fallback;
        if (x <= pts.front().first) return pts.front().second;
        if (x >= pts.back().first) return pts.back().second;
        // binary search for right segment
        size_t lo = 0, hi = pts.size() - 1;
        while (hi - lo > 1) {
            const size_t mid = lo + (hi - lo) / 2;
            if (pts[mid].first <= x) lo = mid;
            else hi = mid;
        }
        const double x0 = pts[lo].first, y0 = pts[lo].second;
        const double x1 = pts[hi].first, y1 = pts[hi].second;
        if (std::abs(x1 - x0) < 1.0e-14) return std::max(y0, y1);
        const double t = (x - x0) / (x1 - x0);
        return y0 + t * (y1 - y0);
    }
};

static MeshFreeSurface build_mesh_free_surface(
    const std::vector<std::vector<double>>& g_coord,
    const std::vector<std::vector<int>>& g_num,
    int nels,
    int nod,
    const std::string& mesh_type,
    double y_fallback)
{
    MeshFreeSurface fs;
    fs.y_fallback = y_fallback;
    std::vector<std::pair<int, int>> local_edges;
    mesh_corner_edges(mesh_type, local_edges);
    const int nvert = mesh_vertex_count(mesh_type);

    std::map<std::pair<int, int>, int> edge_count;
    std::map<std::pair<int, int>, int> edge_owner; // one adjacent element
    for (int iel = 0; iel < nels; ++iel) {
        for (const auto& le : local_edges) {
            if (le.first >= nvert || le.second >= nvert) continue;
            if (le.first >= nod || le.second >= nod) continue;
            const int n1 = g_num[le.first][iel];
            const int n2 = g_num[le.second][iel];
            if (n1 <= 0 || n2 <= 0) continue;
            const int a = std::min(n1, n2);
            const int b = std::max(n1, n2);
            const auto key = std::make_pair(a, b);
            edge_count[key] += 1;
            edge_owner[key] = iel;
        }
    }
    // Keep only upward-facing boundary edges (midpoint above element centroid)
    // so base nodes cannot punch holes in the free-surface envelope.
    std::map<double, double> xmax_y;
    for (const auto& kv : edge_count) {
        if (kv.second != 1) continue;
        const int n1 = kv.first.first;
        const int n2 = kv.first.second;
        const int i0 = n1 - 1, i1 = n2 - 1;
        if (i0 < 0 || i1 < 0) continue;
        const double x0 = g_coord[static_cast<size_t>(i0)][0];
        const double y0 = g_coord[static_cast<size_t>(i0)][1];
        const double x1 = g_coord[static_cast<size_t>(i1)][0];
        const double y1 = g_coord[static_cast<size_t>(i1)][1];
        const int iel = edge_owner[kv.first];
        double cx = 0.0, cy = 0.0;
        for (int k = 0; k < nvert && k < nod; ++k) {
            const int ni = g_num[k][iel] - 1;
            cx += g_coord[static_cast<size_t>(ni)][0];
            cy += g_coord[static_cast<size_t>(ni)][1];
        }
        cx /= static_cast<double>(std::max(1, nvert));
        cy /= static_cast<double>(std::max(1, nvert));
        const double my = 0.5 * (y0 + y1);
        if (my + 1.0e-9 < cy) continue; // base / downward-facing
        for (const auto& pt : {std::make_pair(x0, y0), std::make_pair(x1, y1)}) {
            auto it = xmax_y.find(pt.first);
            if (it == xmax_y.end() || pt.second > it->second)
                xmax_y[pt.first] = pt.second;
        }
    }
    // Merge nearly-coincident x (mesh noise) keeping max y.
    fs.pts.reserve(xmax_y.size());
    for (const auto& kv : xmax_y) {
        if (!fs.pts.empty() && std::abs(fs.pts.back().first - kv.first) < 1.0e-9) {
            if (kv.second > fs.pts.back().second) fs.pts.back().second = kv.second;
        } else {
            fs.pts.push_back(kv);
        }
    }
    return fs;
}

struct GaussPt2 {
    double xi{0.0};
    double eta{0.0};
    double w{0.0};
};

static std::vector<GaussPt2> element_gauss_rule(const std::string& mesh_type)
{
    std::vector<GaussPt2> gps;
    if (iequals(mesh_type, "T6")) {
        gps.push_back({1.0 / 6.0, 1.0 / 6.0, 1.0 / 6.0});
        gps.push_back({2.0 / 3.0, 1.0 / 6.0, 1.0 / 6.0});
        gps.push_back({1.0 / 6.0, 2.0 / 3.0, 1.0 / 6.0});
        return gps;
    }
    if (iequals(mesh_type, "Q4") || iequals(mesh_type, "Q8")) {
        const double a = 1.0 / std::sqrt(3.0);
        gps.push_back({-a, -a, 1.0});
        gps.push_back({ a, -a, 1.0});
        gps.push_back({ a,  a, 1.0});
        gps.push_back({-a,  a, 1.0});
        return gps;
    }
    gps.push_back({1.0 / 3.0, 1.0 / 3.0, 0.5}); // T3
    return gps;
}

static GaussPt2 representative_gp(const std::string& mesh_type)
{
    if (iequals(mesh_type, "Q4") || iequals(mesh_type, "Q8")) return {0.0, 0.0, 4.0};
    return {1.0 / 3.0, 1.0 / 3.0, 0.5};
}

static void shape_derivatives(const std::string& mesh_type, double xi, double eta,
                              std::vector<double>& N,
                              std::vector<double>& dN_dxi,
                              std::vector<double>& dN_deta)
{
    const int nod = mesh_nodes_per_element(mesh_type);
    N.assign(nod, 0.0);
    dN_dxi.assign(nod, 0.0);
    dN_deta.assign(nod, 0.0);

    if (iequals(mesh_type, "T6")) {
        const double L1 = 1.0 - xi - eta;
        const double L2 = xi;
        const double L3 = eta;
        N[0] = L1 * (2.0 * L1 - 1.0);
        N[1] = L2 * (2.0 * L2 - 1.0);
        N[2] = L3 * (2.0 * L3 - 1.0);
        N[3] = 4.0 * L1 * L2;
        N[4] = 4.0 * L2 * L3;
        N[5] = 4.0 * L3 * L1;

        dN_dxi[0] = -(4.0 * L1 - 1.0);
        dN_deta[0] = -(4.0 * L1 - 1.0);
        dN_dxi[1] =  4.0 * L2 - 1.0;
        dN_deta[1] = 0.0;
        dN_dxi[2] = 0.0;
        dN_deta[2] = 4.0 * L3 - 1.0;
        dN_dxi[3] = 4.0 * (L1 - L2);
        dN_deta[3] = -4.0 * L2;
        dN_dxi[4] = 4.0 * L3;
        dN_deta[4] = 4.0 * L2;
        dN_dxi[5] = -4.0 * L3;
        dN_deta[5] = 4.0 * (L1 - L3);
        return;
    }
    if (iequals(mesh_type, "Q4")) {
        N[0] = 0.25 * (1.0 - xi) * (1.0 - eta);
        N[1] = 0.25 * (1.0 + xi) * (1.0 - eta);
        N[2] = 0.25 * (1.0 + xi) * (1.0 + eta);
        N[3] = 0.25 * (1.0 - xi) * (1.0 + eta);
        dN_dxi[0] = -0.25 * (1.0 - eta);
        dN_deta[0] = -0.25 * (1.0 - xi);
        dN_dxi[1] =  0.25 * (1.0 - eta);
        dN_deta[1] = -0.25 * (1.0 + xi);
        dN_dxi[2] =  0.25 * (1.0 + eta);
        dN_deta[2] =  0.25 * (1.0 + xi);
        dN_dxi[3] = -0.25 * (1.0 + eta);
        dN_deta[3] =  0.25 * (1.0 - xi);
        return;
    }
    if (iequals(mesh_type, "Q8")) {
        N[0] = -0.25 * (1.0 - xi) * (1.0 - eta) * (1.0 + xi + eta);
        N[1] = -0.25 * (1.0 + xi) * (1.0 - eta) * (1.0 - xi + eta);
        N[2] = -0.25 * (1.0 + xi) * (1.0 + eta) * (1.0 - xi - eta);
        N[3] = -0.25 * (1.0 - xi) * (1.0 + eta) * (1.0 + xi - eta);
        N[4] =  0.50 * (1.0 - xi * xi) * (1.0 - eta);
        N[5] =  0.50 * (1.0 + xi) * (1.0 - eta * eta);
        N[6] =  0.50 * (1.0 - xi * xi) * (1.0 + eta);
        N[7] =  0.50 * (1.0 - xi) * (1.0 - eta * eta);

        dN_dxi[0] =  0.25 * (1.0 - eta) * (2.0 * xi + eta);
        dN_deta[0] = 0.25 * (1.0 - xi) * (xi + 2.0 * eta);
        dN_dxi[1] =  0.25 * (1.0 - eta) * (2.0 * xi - eta);
        dN_deta[1] = 0.25 * (1.0 + xi) * (2.0 * eta - xi);
        dN_dxi[2] =  0.25 * (1.0 + eta) * (2.0 * xi + eta);
        dN_deta[2] = 0.25 * (1.0 + xi) * (xi + 2.0 * eta);
        dN_dxi[3] =  0.25 * (1.0 + eta) * (2.0 * xi - eta);
        dN_deta[3] = 0.25 * (1.0 - xi) * (2.0 * eta - xi);
        dN_dxi[4] = -xi * (1.0 - eta);
        dN_deta[4] = -0.50 * (1.0 - xi * xi);
        dN_dxi[5] =  0.50 * (1.0 - eta * eta);
        dN_deta[5] = -(1.0 + xi) * eta;
        dN_dxi[6] = -xi * (1.0 + eta);
        dN_deta[6] =  0.50 * (1.0 - xi * xi);
        dN_dxi[7] = -0.50 * (1.0 - eta * eta);
        dN_deta[7] = -(1.0 - xi) * eta;
        return;
    }

    // T3 default
    N[0] = 1.0 - xi - eta;
    N[1] = xi;
    N[2] = eta;
    dN_dxi[0] = -1.0; dN_deta[0] = -1.0;
    dN_dxi[1] =  1.0; dN_deta[1] =  0.0;
    dN_dxi[2] =  0.0; dN_deta[2] =  1.0;
}

static bool build_bee_at_gp(const std::string& mesh_type,
                            const std::vector<std::vector<double>>& coord,
                            double xi, double eta,
                            std::vector<std::vector<double>>& bee,
                            std::vector<double>& N,
                            double& detJ,
                            double& radial_r)
{
    const int nod = (int)coord.size();
    std::vector<double> dN_dxi, dN_deta;
    shape_derivatives(mesh_type, xi, eta, N, dN_dxi, dN_deta);

    double j11 = 0.0, j12 = 0.0, j21 = 0.0, j22 = 0.0;
    for (int a = 0; a < nod; ++a) {
        const double x = coord[a][0], y = coord[a][1];
        j11 += dN_dxi[a]  * x;
        j12 += dN_deta[a] * x;
        j21 += dN_dxi[a]  * y;
        j22 += dN_deta[a] * y;
    }
    detJ = j11 * j22 - j12 * j21;
    if (std::abs(detJ) < 1e-16) return false;
    // Chain rule: [dN/dxi; dN/deta] = A * [dN/dx; dN/dy] with
    //   A = [[dx/dxi, dy/dxi], [dx/deta, dy/deta]] = [[j11, j21], [j12, j22]].
    // Hence [dN/dx; dN/dy] = A^-1 * [dN/dxi; dN/deta], i.e.
    //   dN/dx = ( j22 * dN/dxi - j21 * dN/deta) / detJ
    //   dN/dy = (-j12 * dN/dxi + j11 * dN/deta) / detJ
    // Earlier the code applied inv(J) (transpose of A^-1) directly to
    // [dN/dxi; dN/deta], which silently swapped the off-diagonal entries
    // and broke the constant-stress patch test for any non-axis-aligned
    // (i.e. asymmetric Jacobian) triangle - including the 2-element split
    // square in nafems_le1 / le1_min_repro.
    const double inv11 =  j22 / detJ;
    const double inv12 = -j21 / detJ;
    const double inv21 = -j12 / detJ;
    const double inv22 =  j11 / detJ;

    const int ndof = 2 * nod;
    for (int i = 0; i < 4; ++i) for (int j = 0; j < ndof; ++j) bee[i][j] = 0.0;
    for (int a = 0; a < nod; ++a) {
        const double dNdx = inv11 * dN_dxi[a] + inv12 * dN_deta[a];
        const double dNdy = inv21 * dN_dxi[a] + inv22 * dN_deta[a];
        const int ux = 2 * a, uy = ux + 1;
        bee[0][ux] = dNdx;
        bee[1][uy] = dNdy;
        bee[2][ux] = dNdy;
        bee[2][uy] = dNdx;
    }
    radial_r = 1.0;
    if (g_axisymmetric) {
        double r = 0.0;
        for (int a = 0; a < nod; ++a)
            r += N[static_cast<size_t>(a)] * coord[static_cast<size_t>(a)][0];
        constexpr double kMinR = 1.0e-12;
        if (!(r > kMinR)) r = kMinR;
        radial_r = r;
        const double inv_r = 1.0 / radial_r;
        for (int a = 0; a < nod; ++a) {
            const int ux = 2 * a;
            bee[3][ux] = N[static_cast<size_t>(a)] * inv_r;
        }
    }
    return true;
}

/// Sum Bᵀσ on restrained DOFs (nf==0). Used for JR-01 force-equilibrium checks.
static void recover_support_reactions(
    const std::vector<std::vector<std::vector<double>>>& tensor,
    const std::vector<std::vector<double>>& g_coord,
    const std::vector<std::vector<int>>& g_num,
    const std::vector<int>& nf,
    int nn, int nels, int nod, int nodof, int nst, int ndof,
    const std::string& mesh_type,
    double& rfx, double& rfy)
{
    rfx = 0.0;
    rfy = 0.0;
    if (nn <= 0 || nels <= 0 || nod <= 0 || nodof <= 0) return;
    std::vector<std::vector<double>> bee(nst, std::vector<double>(ndof, 0.0));
    std::vector<std::vector<double>> coord(nod, std::vector<double>(2, 0.0));
    const auto gps = element_gauss_rule(mesh_type);
    const auto gp_rep = representative_gp(mesh_type);
    for (int iel = 0; iel < nels; ++iel) {
        for (int k = 0; k < nod; ++k) coord[k] = g_coord[g_num[k][iel] - 1];
        std::vector<double> Nrep;
        double detJrep = 0.0, radial_r = 1.0;
        if (!build_bee_at_gp(mesh_type, coord, gp_rep.xi, gp_rep.eta, bee, Nrep, detJrep, radial_r))
            continue;
        double fac = 0.0;
        for (const auto& gp : gps) {
            std::vector<double> Ng;
            double detJ = 0.0;
            radial_r = 1.0;
            if (!build_bee_at_gp(mesh_type, coord, gp.xi, gp.eta, bee, Ng, detJ, radial_r)) continue;
            fac += gp_int_weight(detJ, gp.w, radial_r);
        }
        build_bee_at_gp(mesh_type, coord, gp_rep.xi, gp_rep.eta, bee, Nrep, detJrep, radial_r);
        std::vector<double> stress(nst, 0.0);
        for (int s = 0; s < nst; ++s)
            if (tensor[s][0].size() > static_cast<size_t>(iel))
                stress[s] = tensor[s][0][iel];
        auto eload = matvec_T(bee, stress);
        for (int k = 0; k < nod; ++k) {
            const int nid = g_num[k][iel] - 1;
            if (nid < 0 || nid >= nn) continue;
            const int eqx = nf[0 + nid * nodof];
            const int eqy = nf[1 + nid * nodof];
            if (eqx == 0) rfx += eload[2 * k] * fac;
            if (eqy == 0) rfy += eload[2 * k + 1] * fac;
        }
    }
}

/// Total element strains from nodal displacements: ε = B·u (engineering γxy).
/// Averaged over the element Gauss rule; gamma_max is the peak GP value of
/// √((εxx−εyy)² + γxy²) for RS2-style maximum shear strain contours.
static void populate_stage_strain_fields(
    StageJson& stage,
    const std::string& mesh_type,
    const std::vector<std::vector<double>>& g_coord,
    const std::vector<std::vector<int>>& g_num,
    const std::vector<int>& solid,
    int nn, int nels, int nod, int nodof, int nst)
{
    stage.strain.assign(static_cast<size_t>(3 * nels), 0.0);
    stage.gamma_max.assign(static_cast<size_t>(nels), 0.0);
    if (stage.d.size() < static_cast<size_t>(nodof * nn) || nels <= 0 || nod <= 0)
        return;

    const auto gps = element_gauss_rule(mesh_type);
    if (gps.empty()) return;

    const int ndof = nod * nodof;
    std::vector<std::vector<double>> bee(4, std::vector<double>(ndof, 0.0));
    std::vector<std::vector<double>> coord(static_cast<size_t>(nod), std::vector<double>(2, 0.0));
    std::vector<double> eld(static_cast<size_t>(ndof), 0.0);

    for (int iel = 0; iel < nels; ++iel) {
        if (!solid.empty() && solid[static_cast<size_t>(iel)] == 0) continue;

        for (int k = 0; k < nod; ++k) {
            const int nid = g_num[k][iel];
            if (nid < 1 || nid > nn) continue;
            coord[static_cast<size_t>(k)][0] = g_coord[static_cast<size_t>(nid - 1)][0];
            coord[static_cast<size_t>(k)][1] = g_coord[static_cast<size_t>(nid - 1)][1];
            eld[static_cast<size_t>(k * nodof + 0)] =
                stage.d[static_cast<size_t>((nid - 1) * nodof + 0)];
            eld[static_cast<size_t>(k * nodof + 1)] =
                stage.d[static_cast<size_t>((nid - 1) * nodof + 1)];
        }

        double avg_exx = 0.0, avg_eyy = 0.0, avg_gxy = 0.0;
        double max_gmax = 0.0;
        int n_ok = 0;
        for (const auto& gp : gps) {
            std::vector<double> Ng;
            double detJ = 0.0;
            double radial_r = 1.0;
            if (!build_bee_at_gp(mesh_type, coord, gp.xi, gp.eta, bee, Ng, detJ, radial_r))
                continue;
            double exx = 0.0, eyy = 0.0, gxy = 0.0;
            for (int j = 0; j < ndof; ++j) {
                exx += bee[0][static_cast<size_t>(j)] * eld[static_cast<size_t>(j)];
                eyy += bee[1][static_cast<size_t>(j)] * eld[static_cast<size_t>(j)];
                gxy += bee[2][static_cast<size_t>(j)] * eld[static_cast<size_t>(j)];
            }
            (void)nst;
            avg_exx += exx;
            avg_eyy += eyy;
            avg_gxy += gxy;
            const double gmax_gp = std::sqrt((exx - eyy) * (exx - eyy) + gxy * gxy);
            if (gmax_gp > max_gmax) max_gmax = gmax_gp;
            ++n_ok;
        }
        if (n_ok <= 0) continue;
        const double inv = 1.0 / static_cast<double>(n_ok);
        const size_t so = static_cast<size_t>(3 * iel);
        stage.strain[so + 0] = avg_exx * inv;
        stage.strain[so + 1] = avg_eyy * inv;
        stage.strain[so + 2] = avg_gxy * inv;
        stage.gamma_max[static_cast<size_t>(iel)] = max_gmax;
    }
}

static std::vector<std::pair<int,int>> element_boundary_segments(const std::string& mesh_type,
                                                                 const std::vector<int>& el_nodes)
{
    std::vector<std::pair<int,int>> edges;
    if (iequals(mesh_type, "T6") && el_nodes.size() >= 6) {
        edges.push_back({el_nodes[0], el_nodes[3]});
        edges.push_back({el_nodes[3], el_nodes[1]});
        edges.push_back({el_nodes[1], el_nodes[4]});
        edges.push_back({el_nodes[4], el_nodes[2]});
        edges.push_back({el_nodes[2], el_nodes[5]});
        edges.push_back({el_nodes[5], el_nodes[0]});
        return edges;
    }
    if (iequals(mesh_type, "Q8") && el_nodes.size() >= 8) {
        edges.push_back({el_nodes[0], el_nodes[4]});
        edges.push_back({el_nodes[4], el_nodes[1]});
        edges.push_back({el_nodes[1], el_nodes[5]});
        edges.push_back({el_nodes[5], el_nodes[2]});
        edges.push_back({el_nodes[2], el_nodes[6]});
        edges.push_back({el_nodes[6], el_nodes[3]});
        edges.push_back({el_nodes[3], el_nodes[7]});
        edges.push_back({el_nodes[7], el_nodes[0]});
        return edges;
    }
    const int nv = mesh_vertex_count(mesh_type);
    for (int i = 0; i < nv; ++i) {
        int j = (i + 1) % nv;
        edges.push_back({el_nodes[i], el_nodes[j]});
    }
    return edges;
}

static void effective_elastic_constants(MaterialLaw law, double e0, double nu0, double sigm, double dsbar, double& e_eff, double& nu_eff)
{
    e_eff = e0;
    nu_eff = nu0;
    const double p_comp = std::max(-sigm, 0.0);
    switch (law) {
        case MaterialLaw::TransverselyIsotropic:
            e_eff = e0 * 0.85;
            nu_eff = std::min(0.49, std::max(0.05, nu0 * 0.95));
            break;
        case MaterialLaw::Orthotropic:
            e_eff = e0 * 0.75;
            nu_eff = std::min(0.49, std::max(0.05, nu0 * 0.90));
            break;
        case MaterialLaw::DuncanChangHyperbolic:
            e_eff = e0 / (1.0 + 0.15 * p_comp / (std::abs(e0) + 1.0));
            break;
        case MaterialLaw::NonlinearIsotropic:
            e_eff = e0 / (1.0 + 0.05 * dsbar / (std::abs(e0) + 1.0));
            break;
        case MaterialLaw::ViscoElastic:
            e_eff = e0 * 1.05;
            nu_eff = std::min(0.49, std::max(0.05, nu0 * 0.98));
            break;
        case MaterialLaw::Plastic:
            e_eff = e0 * 0.90;
            break;
        case MaterialLaw::Isotropic:
        default:
            break;
    }
    if (!std::isfinite(e_eff) || e_eff < 1.0e3) e_eff = 1.0e3;
    if (!std::isfinite(nu_eff)) nu_eff = 0.30;
    nu_eff = std::min(0.49, std::max(-0.49, nu_eff));
}

/// True when AnalysisOptions `solver_executable` names any p610 T3 route that shares the
/// elastoplastic core (same TU as `p610_t3_elastoplastic`). Used so `slope_stability_T3_SRF.exe`
/// (entry with g_solver_name == p610_t3_elastoplastic) accepts .dat files emitted for other
/// p610_t3_* executable names.
static bool p610_t3_dat_solver_executable_routes_to_elastoplastic_core(const std::string& token)
{
    return iequals(token, "p610_t3_elastoplastic")
        || iequals(token, "p610_t3_elastic")
        || iequals(token, "p610_t3_mohr_coulomb")
        || iequals(token, "p610_t3_hoek_brown")
        || iequals(token, "p610_t3_drucker_prager")
        || iequals(token, "p610_t3_generalized_hoek_brown")
        || iequals(token, "p610_t3_plastic")
        || iequals(token, "p610_t3_nonlinear")
        || iequals(token, "slope_stability_T3_SRF")
        || iequals(token, "slope_stability_T6_SRF")
        || iequals(token, "slope_stability_Q4_SRF")
        || is_slope_stability_srf_executable(token);
}

int p610_t3_run_entry(int argc, char* argv[], const char* solver_name)
{
    g_solver_name = solver_name ? solver_name : "p610_t3_elastoplastic";
    std::string base;
    if (argc>=2) base=argv[1];
    else { std::cout<<"Enter data file base name: "; std::cin>>base; }

    const std::string dat_path = base + ".dat";
    const std::string solver_opt = read_analysis_option(dat_path, "solver_executable");
    if (!solver_opt.empty() && !iequals(solver_opt, g_solver_name)) {
        const bool elastoplastic_core_alias_ok =
            (iequals(g_solver_name, "p610_t3_elastoplastic")
                && p610_t3_dat_solver_executable_routes_to_elastoplastic_core(solver_opt))
            || (is_slope_stability_srf_executable(g_solver_name)
                && (iequals(solver_opt, g_solver_name)
                    || iequals(solver_opt, "SRF")
                    || p610_t3_dat_solver_executable_routes_to_elastoplastic_core(solver_opt)));
        if (!elastoplastic_core_alias_ok) {
            std::cerr << "AnalysisOptions solver_executable mismatch: file requests '" << solver_opt
                      << "' but executable is '" << g_solver_name << "'.\n";
            return 2;
        }
    }
    const std::string mesh_opt = read_analysis_option(dat_path, "mesh_type");
    g_axisymmetric = resolve_axisymmetric_mode(dat_path);
    if (g_axisymmetric)
        std::cout << " analysis_type: axisymmetric (S&G *r, e_theta=u/r)\n";
    std::string mesh_type = mesh_opt.empty() ? "T3" : mesh_opt;
    // Q6 is not a distinct serendipity formulation here; treat as quadratic 6-node triangles (T6).
    if (iequals(mesh_type, "Q6")) {
        std::cout << " Note: mesh_type Q6 mapped to T6 (quadratic 6-node triangles).\n";
        mesh_type = "T6";
    }
    const std::string failure_opt = read_analysis_option(dat_path, "failure_criterion");
    const FailureMode failure_mode = parse_failure_mode(failure_opt);
    const std::string material_opt = read_analysis_option(dat_path, "material_type");
    const MaterialLaw material_law = parse_material_law(material_opt);

    geotech_io::AnalysisOptions geo_opts = geotech_io::parse_analysis_options(dat_path);
    if (!geo_opts.has_pseudo_static) {
        double kh = 0.0, kv = 0.0;
        if (geotech_io::parse_pseudo_static_coefficients(dat_path, kh, kv)) {
            geo_opts.k_h = kh;
            geo_opts.k_v = kv;
            geo_opts.has_pseudo_static = true;
        }
    }
    const double ps_k_h = geo_opts.has_pseudo_static ? geo_opts.k_h : 0.0;
    const double ps_k_v = geo_opts.has_pseudo_static ? geo_opts.k_v : 0.0;
    const bool export_ps_forces = geo_opts.has_pseudo_static
        && (ps_k_h != 0.0 || ps_k_v != 0.0);

    std::stringstream dat;
    if (!load_dat_stripped(dat_path, dat)) {
        std::cerr<<"Cannot open "<<dat_path<<"\n"; return 1;
    }
    std::ofstream res(base+".res");
    if (!res) { std::cerr<<"Cannot create "<<base<<".res\n"; return 1; }
    if (!failure_opt.empty()) {
        res << " Failure criterion from AnalysisOptions: " << failure_opt << "\n";
        if (!(iequals(failure_opt, "MohrCoulomb") || iequals(failure_opt, "DruckerPrager")
            || iequals(failure_opt, "HoekBrown") || iequals(failure_opt, "GeneralizedHoekBrown")
            || iequals(failure_opt, "ElastoPlastic") || iequals(failure_opt, "Elastic")))
            res << " Note: unsupported criterion token for dedicated model; defaulting to Mohr-Coulomb constitutive core.\n";
    }
    if (!material_opt.empty())
        res << " Material type from AnalysisOptions: " << material_opt << "\n";
    if (g_axisymmetric)
        res << " analysis_type axisymmetric (S&G *r, e_theta=u/r)\n";
    if (geo_opts.has_pseudo_static)
        res << " Pseudo-static coefficients: k_h=" << ps_k_h << "  k_v=" << ps_k_v << "\n";

    // Plane-strain constants
    const int nod   = mesh_nodes_per_element(mesh_type);
    const int nodof = 2;
    const int ndof  = nod*nodof;
    const int nst   = 4;            // sigma_x, sigma_y, tau_xy, sigma_z
    const int nip   = 1;            // constitutive state stored at representative gp
    const int nprops= 7;

    // Centroid integration: weight * |J| = 0.5 * 2*Area = Area
    const double gp_w = 0.5;

    // --- read problem size ---------------------------------------------------
    int nels_raw, nn, np_types;
    dat >> nels_raw >> nn >> np_types;

    std::vector<double> ps_nodal_fx(static_cast<size_t>(nn), 0.0);
    std::vector<double> ps_nodal_fy(static_cast<size_t>(nn), 0.0);
    bool have_ps_nodal = false;

    // prop[p][m] : property p of material m
    std::vector<std::vector<double>> prop(nprops, std::vector<double>(np_types));
    for (int p=0;p<nprops;++p) for (int m=0;m<np_types;++m) dat>>prop[p][m];

    // M2.2 Option B: strength-reduction-factor (SRF) shim. When the .dat
    // declares `! srf_F X.X` (X >= 1), we reduce phi/c/psi for every material
    // BEFORE any solve uses them so the in-situ stress, yield surface and
    // post-stage diagnostics all reflect the reduced strengths. Save the pre-
    // reduction phi/c so the meta block can record what got divided out.
    double srf_F_trial = 0.0;
    {
        const std::string srf_opt = read_analysis_option(dat_path, "srf_F");
        if (!srf_opt.empty()) {
            try { srf_F_trial = std::stod(srf_opt); }
            catch (...) { srf_F_trial = 0.0; }
        }
    }
    const double srf_phi_orig = (np_types > 0) ? prop[0][0] : 0.0;
    const double srf_c_orig   = (np_types > 0) ? prop[1][0] : 0.0;
    if (srf_F_trial > 0.0 && srf_F_trial < 1.0e6) {
        apply_strength_reduction(srf_F_trial, failure_mode, prop);
        std::cout << " M2.2 SRF mode: F_trial = " << srf_F_trial
                  << "; reduced phi_orig = " << srf_phi_orig << " deg, c_orig = "
                  << srf_c_orig << " kPa across " << np_types << " material(s).\n";
    }

    // Gravity-only SRF in-situ path (AnalysisOptions `! srf_insitu_mode`):
    //   gravity (default) — clear Stage-0 K0 and ramp self-weight from σ=0 (Program 6.4)
    //   k0                — keep Stage-0 K0 (+ Ru/WT effective stress) and run a plastic
    //                       equilibrium check under reduced strength (no gravity re-turn-on)
    bool srf_insitu_k0 = false;
    {
        const std::string insitu_opt = read_analysis_option(dat_path, "srf_insitu_mode");
        srf_insitu_k0 = iequals(insitu_opt, "k0") || iequals(insitu_opt, "keep_k0");
        if (srf_insitu_k0)
            std::cout << " SRF in-situ mode: k0 (retain Stage-0 geostatic / Ru field)\n";
        else if (!insitu_opt.empty())
            std::cout << " SRF in-situ mode: gravity (Program 6.4 turn-on from σ=0)\n";
    }

    for (int m = 0; m < np_types; ++m) {
        if (!std::isfinite(prop[4][m]) || prop[4][m] < 1.0e3) prop[4][m] = 1.0e3;
        if (!std::isfinite(prop[5][m])) prop[5][m] = 0.30;
        prop[5][m] = std::min(0.49, std::max(-0.49, prop[5][m]));
    }

    // element material type (0-based)
    std::vector<int> etype_raw(nels_raw, 0);
    if (np_types>1)
        for (int i=0;i<nels_raw;++i) { dat>>etype_raw[i]; etype_raw[i]--; }

    // nodal coordinates g_coord[nn][2]
    std::vector<std::vector<double>> g_coord(nn, std::vector<double>(2));
    for (int i=0;i<nn;++i) dat>>g_coord[i][0]>>g_coord[i][1];

    int nels = nels_raw;
    std::vector<int> etype = etype_raw;
    if (np_types <= 1) etype.assign(nels, 0);
    std::vector<std::vector<int>> g_num(nod, std::vector<int>(nels));
    for (int i=0;i<nels;++i)
        for (int k=0;k<nod;++k)
            dat>>g_num[k][i];

    // boundary conditions: nf[nodof*nn] flat column-major, 0=fixed 1=free
    std::vector<int> nf(nodof*nn, 1);
    int nr; dat>>nr;
    for (int i=0;i<nr;++i) {
        int k; dat>>k;
        for (int d=0;d<nodof;++d) dat>>nf[d+(k-1)*nodof];
    }

    // lnf is the working copy of nf (modified per excavation stage)
    std::vector<int> lnf = nf;
    geom::formnf(lnf, nodof, nn);
    int neq=0;
    for (int v : lnf) if (v>neq) neq=v;
    const int neq_in_situ = neq;

    res<<" Initial elements: "<<nels<<"\n";
    res<<" Initial freedoms: "<<neq<<"\n";

    groundwater::Spec gw = groundwater::parse_from_dat_path(dat_path);
    const support_elements::Spec support_spec = support_elements::parse_from_dat_path(dat_path);
    const joint_elements::Spec joint_spec = joint_elements::parse_from_dat_path(dat_path);
    std::vector<joint_elements::PlasticState> joint_state;
    if (joint_spec.active())
        joint_state.assign(joint_spec.interfaces.size(), {});
    auto joint_getx = [&](int i) { return joint_elements::node_x_p610(g_coord, i); };
    auto joint_gety = [&](int i) { return joint_elements::node_y_p610(g_coord, i); };
    bool gw_effective_stress_yield = true;
    double gw_u_factor = 1.0;
    if (gw.active()) {
        std::string gs = read_analysis_option(dat_path, "groundwater_strength");
        for (char& c : gs)
            c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
        gw_effective_stress_yield = (gs != "total");
        const std::string uf = read_analysis_option(dat_path, "groundwater_u_factor");
        if (!uf.empty()) {
            try { gw_u_factor = std::stod(uf); }
            catch (...) { gw_u_factor = 1.0; }
        }
        if (!gw_effective_stress_yield)
            gw_u_factor = 0.0;
        res << groundwater::groundwater_log_line(gw);
    }

    // Initial geostatic stresses at centroid of each element.
    //   tensor[s][i][el]: s=stress component (0=sxx,1=syy,2=txy,3=szz),
    //                     i=Gauss pt (always 0 for T3), el=element.
    // Sign convention: tension-positive (compression-negative).
    //
    // Mode is selected by the optional ! <BEGIN:FieldStress> block in the .dat.
    // When the block is absent the solver falls back to the legacy per-material
    // K0·γ·depth gravity loop so existing .dat files continue to work unchanged.
    FieldStress fs = read_field_stress_block(dat_path);

    // Free-surface elevation for legacy K0·γ·(depth).
    // Smith/SLOPE64: y-down (ymax≈0 crest, ymin<0 base).
    // Engineering / SLOPE64-manual y-up: ymin≥0, free surface at ymax.
    // Legacy GUI axis (crest at ymin): set FieldStress y_surface explicitly.
    double y_min = g_coord[0][1], y_max = g_coord[0][1];
    for (int i = 1; i < nn; ++i) {
        if (g_coord[i][1] < y_min) y_min = g_coord[i][1];
        if (g_coord[i][1] > y_max) y_max = g_coord[i][1];
    }
    // Auto free surface: mesh upper envelope (not domain ymax). Crest-at-ymin
    // GUI meshes must set FieldStress y_surface explicitly.
    const MeshFreeSurface mesh_free_surface =
        build_mesh_free_surface(g_coord, g_num, nels, nod, mesh_type, y_max);
    const double y_surface_auto = y_max;
    const bool surface_at_max_y = true;
    res << " Mesh free-surface envelope: " << mesh_free_surface.pts.size()
        << " points (Stage-0 K0 uses y_surf(x), not ymax).\n";

    std::vector<std::vector<std::vector<double>>>
        tensor(nst, std::vector<std::vector<double>>(nip, std::vector<double>(nels,0.0)));
    for (int iel = 0; iel < nels; ++iel) {
        int m = etype[iel];
        const FieldStressSpec& spec = fs.for_material(m);
        const int nvert = mesh_vertex_count(mesh_type);
        double xc = 0.0, yc = 0.0;
        for (int k = 0; k < nvert; ++k) {
            xc += g_coord[g_num[k][iel]-1][0];
            yc += g_coord[g_num[k][iel]-1][1];
        }
        xc /= std::max(1, nvert);
        yc /= std::max(1, nvert);

        double sxx, syy, szz, txy;
        if (spec.mode == FieldStressSpec::Mode::Constant) {
            const double ar = spec.angle_deg * 3.14159265358979323846 / 180.0;
            const double c2 = std::cos(2.0*ar);
            const double s2 = std::sin(2.0*ar);
            const double sm = 0.5*(spec.s1 + spec.s3);
            const double sd = 0.5*(spec.s1 - spec.s3);
            sxx = sm + sd*c2;
            syy = sm - sd*c2;
            txy = sd*s2;
            szz = spec.sZ;
        } else {
            // Gravity mode.  Legacy fallback uses per-material K0 (prop[6][m])
            // for both horizontal axes; explicit block uses spec.Kx / spec.Kz.
            // Overburden uses γ above WT and γsat below when gamma_sat is in the GW block
            // (applies for T3/T6/Q4/Q8 via mesh_type).
            //
            // When pore pressure is active (hydrostatic, grid, or Ru), K0 must be
            // applied to Terzaghi *effective* stress. Applying K0 to total σv then
            // subtracting u for yield drives K0 < Ka on the effective stress path
            // and makes the "at-rest" field already past active failure.
            // For Ru this is critical: with Kx≈Ru≈0.5, total-K0 + Terzaghi yield
            // zeros σ′xx and collapses SRF FoS (RS2 Ex14 / Spencer–Baker).
            const double gamma = prop[3][m];
            const double Kx    = fs.is_legacy ? prop[6][m] : spec.Kx;
            const double Kz    = fs.is_legacy ? prop[6][m] : spec.Kz;
            const double surf  = (spec.y_surface > 1.0e29)
                ? mesh_free_surface.y_at(xc)
                : spec.y_surface;
            (void)y_surface_auto;
            double depth = surface_at_max_y ? (surf - yc) : (yc - surf);
            if (depth < 0.0) depth = 0.0;
            // Crest/near-surface elements with σ≡0 sit on the MC apex (f=c·cosφ>0).
            // formm() divides by mean stress — use a tiny embedment so in-situ σ is non-singular.
            if (depth < 1.0e-4) depth = 1.0e-4;
            double sv_total_mag = groundwater::vertical_overburden_magnitude(
                gamma, m, xc, yc, surf, gw);
            if (sv_total_mag < gamma * 1.0e-4)
                sv_total_mag = gamma * 1.0e-4;

            double u = 0.0;
            const bool gw_eff_k0 = gw.active() && gw_u_factor > 0.0;
            if (gw_eff_k0) {
                // Bootstrap u from overburden: hydrostatic/grid are stress-independent;
                // Ru uses u = Ru·σv with σv = overburden (dummy σyy = −sv_total_mag).
                double dummy[4] = {0.0, -sv_total_mag, 0.0, 0.0};
                u = groundwater::pore_pressure_at(xc, yc, gw, dummy) * gw_u_factor;
            }
            double sv_eff_mag = sv_total_mag - u;
            if (sv_eff_mag < gamma * 1.0e-4)
                sv_eff_mag = gamma * 1.0e-4;
            const double sv_eff = -sv_eff_mag;
            const double sxx_eff = Kx * sv_eff;
            const double syy_eff = sv_eff;
            const double szz_eff = Kz * sv_eff;
            // Store TOTAL stress: σ = σ' − u  (apply_effective_stress uses σ' = σ + u).
            sxx = sxx_eff - u;
            syy = syy_eff - u;
            szz = szz_eff - u;
            txy = 0.0;
        }
        tensor[0][0][iel] = sxx;
        tensor[1][0][iel] = syy;
        tensor[2][0][iel] = txy;
        tensor[3][0][iel] = szz;
    }

    // Accumulated total displacements: flat [nodof*nn], column-major
    std::vector<double> tot_d(nodof*nn, 0.0);

    std::vector<int> solid(nels, 1);        // 1=solid, 0=excavated
    std::vector<int> totex(nels, 0);
    int ntote = 0;

    // evpt[s][i][el]: viscoplastic strain accumulator
    std::vector<std::vector<std::vector<double>>>
        evpt(nst, std::vector<std::vector<double>>(nip, std::vector<double>(nels,0.0)));

    // solver control
    int nouts; dat>>nouts;
    std::vector<int> no(nouts);
    for (int i=0;i<nouts;++i) dat>>no[i];
    double tol; int limit, incs, layers;
    dat>>tol>>limit>>incs>>layers;

    // reusable work arrays
    std::vector<std::vector<double>>
        bee(nst, std::vector<double>(ndof)),
        dee(nst, std::vector<double>(nst)),
        km (ndof, std::vector<double>(ndof)),
        m1 (nst, std::vector<double>(nst)),
        m2 (nst, std::vector<double>(nst)),
        m3 (nst, std::vector<double>(nst)),
        flow(nst, std::vector<double>(nst));
    std::vector<int> g(ndof);
    std::vector<std::vector<double>> coord(nod, std::vector<double>(2));
    const auto gps = element_gauss_rule(mesh_type);
    const auto gp_rep = representative_gp(mesh_type);

    // =========================================================================
    //  LAYERS==0 : Infinite-medium traction-loaded elastoplastic solve
    //
    //  .dat format (after "tol limit incs 0"):
    //    n_traction_groups
    //    Per group:
    //      mode p0 p1 p2
    //        mode=0  direct:    sxx=p0, syy=p1, sxy=p2
    //        mode=1  far-field: syy=p0, sxx=p1*p0, sxy=0
    //      n_edges
    //      n_edges rows: n1 n2   (1-based, CCW so outward normal = right-hand of edge)
    //
    //  Starts from zero initial stress and zero displacement.
    //  Applies the total traction in `incs` equal increments, running VP
    //  iterations (same algorithm as the excavation loop) to convergence.
    // =========================================================================
    if (layers == 0) {
        // --- read traction groups -------------------------------------------
        struct TrGrp {
            double sxx{0}, syy{0}, sxy{0};
            std::vector<std::pair<int,int>> edges;
        };
        int n_trac = 0; dat >> n_trac;
        std::vector<TrGrp> trac_groups(n_trac);
        for (int gg = 0; gg < n_trac; ++gg) {
            int mode; double p0, p1, p2;
            dat >> mode >> p0 >> p1 >> p2;
            if (mode == 0) {
                trac_groups[gg].sxx = p0;
                trac_groups[gg].syy = p1;
                trac_groups[gg].sxy = p2;
            } else {
                trac_groups[gg].syy = p0;
                trac_groups[gg].sxx = p1 * p0;
                trac_groups[gg].sxy = 0.0;
            }
            int ne; dat >> ne;
            trac_groups[gg].edges.resize(ne);
            for (int e = 0; e < ne; ++e) {
                int n1, n2; dat >> n1 >> n2;
                trac_groups[gg].edges[e] = {n1, n2};
            }
        }

        res << "\n Infinite-medium traction-loaded elastoplastic analysis\n";
        res << " Elements: " << nels << "   Freedoms: " << neq_in_situ
            << "   Traction groups: " << n_trac << "\n";

        // Start from zero stress everywhere (no in-situ gravity field)
        for (int s = 0; s < nst; ++s)
            for (int el = 0; el < nels; ++el)
                tensor[s][0][el] = evpt[s][0][el] = 0.0;

        // --- skyline profile ------------------------------------------------
        std::vector<int> kdiag_im(neq_in_situ, 0);
        for (int iel = 0; iel < nels; ++iel) {
            std::vector<int> num(nod);
            for (int k = 0; k < nod; ++k) num[k] = g_num[k][iel];
            fem::num_to_g(num, lnf, nodof, nn, g);
            fem::fkdiag(kdiag_im, g, neq_in_situ);
        }
        if (support_spec.active()) {
            std::vector<int> gbar(4, 0);
            for (const auto& bar : support_spec.bars) {
                const int n0 = support_elements::nearest_node(g_coord, bar.hx, bar.hy);
                const int n1 = support_elements::nearest_node(g_coord, bar.tx, bar.ty);
                if (n0 == n1) continue;
                std::vector<int> nbar{n0 + 1, n1 + 1};
                fem::num_to_g(nbar, lnf, nodof, nn, gbar);
                fem::fkdiag(kdiag_im, gbar, neq_in_situ);
            }
        }
        joint_elements::apply_fkdiag(joint_spec, lnf, nodof, nn, kdiag_im, neq_in_situ, 1);
        for (int i = 1; i < neq_in_situ; ++i) kdiag_im[i] += kdiag_im[i-1];

        // --- assemble elastic stiffness + VP time step ----------------------
        std::vector<double> kv_im(kdiag_im[neq_in_situ-1], 0.0);
        double dt_im = 1.0e15;
        const double pi_im = std::acos(-1.0);
        for (int iel = 0; iel < nels; ++iel) {
            int m = etype[iel];
            double phi_r = prop[0][m] * pi_im / 180.0;
            double e_v   = prop[4][m], nu_v = prop[5][m];
            double snph  = std::sin(phi_r);
            double ddt   = 4.0*(1.0+nu_v)*(1.0-2.0*nu_v) / (e_v*(1.0-2.0*nu_v+snph*snph));
            if (ddt < dt_im) dt_im = ddt;

            for (int k = 0; k < nod; ++k) coord[k] = g_coord[g_num[k][iel]-1];
            double e_eff = e_v, nu_eff = nu_v;
            effective_elastic_constants(material_law, e_v, nu_v, 0.0, 0.0, e_eff, nu_eff);
            fem::deemat(dee, e_eff, nu_eff);
            for (int i = 0; i < ndof; ++i) for (int j = 0; j < ndof; ++j) km[i][j] = 0.0;
            for (const auto& gp : gps) {
                std::vector<double> N;
                double detJ = 0.0;
                double radial_r = 1.0;
                if (!build_bee_at_gp(mesh_type, coord, gp.xi, gp.eta, bee, N, detJ, radial_r)) continue;
                accum_ke(km, bee, dee, gp_int_weight(detJ, gp.w, radial_r));
            }

            std::vector<int> num(nod);
            for (int k = 0; k < nod; ++k) num[k] = g_num[k][iel];
            fem::num_to_g(num, lnf, nodof, nn, g);
            fem::fsparv(kv_im, km, g, kdiag_im);
        }
        if (support_spec.active()) {
            std::vector<std::vector<double>> kbar(4, std::vector<double>(4, 0.0));
            std::vector<int> gbar(4, 0);
            for (const auto& bar : support_spec.bars) {
                const double dx = bar.tx - bar.hx;
                const double dy = bar.ty - bar.hy;
                const int n0 = support_elements::nearest_node(g_coord, bar.hx, bar.hy);
                const int n1 = support_elements::nearest_node(g_coord, bar.tx, bar.ty);
                if (n0 == n1) continue;
                support_elements::fill_truss_km(bar.ea, dx, dy, kbar);
                std::vector<int> nbar{n0 + 1, n1 + 1};
                fem::num_to_g(nbar, lnf, nodof, nn, gbar);
                fem::fsparv(kv_im, kbar, gbar, kdiag_im);
            }
        }
        joint_elements::assemble(joint_spec, joint_getx, joint_gety, lnf, nodof, nn, kv_im, kdiag_im, 1);
        fem::sparin(kv_im, kdiag_im);

        // --- assemble traction load vector ----------------------------------
        std::vector<double> trac_loads(neq_in_situ + 1, 0.0);
        for (const auto& tg : trac_groups) {
            for (const auto& edge : tg.edges) {
                int ni = edge.first - 1, nj = edge.second - 1;
                double xi = g_coord[ni][0], yi = g_coord[ni][1];
                double xj = g_coord[nj][0], yj = g_coord[nj][1];
                double dx = xj - xi, dy = yj - yi;
                double L  = std::sqrt(dx*dx + dy*dy);
                if (L < 1e-20) continue;
                double nx = dy / L, ny = -dx / L;           // right-hand outward normal
                double tx = tg.sxx*nx + tg.sxy*ny;
                double ty = tg.sxy*nx + tg.syy*ny;
                int eqi_x = lnf[0 + ni*nodof], eqi_y = lnf[1 + ni*nodof];
                int eqj_x = lnf[0 + nj*nodof], eqj_y = lnf[1 + nj*nodof];
                if (eqi_x > 0) trac_loads[eqi_x] += tx * L * 0.5;
                if (eqi_y > 0) trac_loads[eqi_y] += ty * L * 0.5;
                if (eqj_x > 0) trac_loads[eqj_x] += tx * L * 0.5;
                if (eqj_y > 0) trac_loads[eqj_y] += ty * L * 0.5;
            }
        }
        for (int i = 1; i <= neq_in_situ; ++i) trac_loads[i] /= incs;

        // --- increment + VP iteration loop ----------------------------------
        std::vector<double> loads_im(neq_in_situ+1, 0.0),
                            oldis_im(neq_in_situ+1, 0.0),
                            bdylds_im(neq_in_situ+1, 0.0);

        // M2.2 Option B: track per-increment iteration counts so meta.srf can
        // surface "max_iters_used" and "all_increments_converged" without the
        // orchestrator having to re-parse the .res file.
        int srf_max_iters_im = 0;
        bool srf_all_conv_im = true;

        for (int iy = 1; iy <= incs; ++iy) {
            std::fill(oldis_im.begin(), oldis_im.end(), 0.0);
            std::fill(bdylds_im.begin(), bdylds_im.end(), 0.0);
            for (int s = 0; s < nst; ++s)
                for (int el = 0; el < nels; ++el) evpt[s][0][el] = 0.0;
            for (auto& st : joint_state) st = {};

            int iters_im = 0;
            bool conv_im = false;
            bool force_final_im = false;
            const double f_yield_tol_im = 1.0;
            while (true) {
                ++iters_im;
                for (int i = 0; i <= neq_in_situ; ++i)
                    loads_im[i] = trac_loads[i] + bdylds_im[i];
                fem::spabac(kv_im, loads_im, kdiag_im);
                conv_im = fem::checon(loads_im, oldis_im, tol) || force_final_im;
                if (iters_im == 1) conv_im = false;

                if (conv_im || iters_im == limit) {
                    for (int j = 0; j < nn; ++j)
                        for (int d = 0; d < nodof; ++d) {
                            int eq = lnf[d + j*nodof];
                            if (eq > 0) tot_d[d + j*nodof] += loads_im[eq];
                        }
                }

                // Reset bodyloads only on the final pass (reactions). During
                // plastic iterations they accumulate Bᵀ D Δεvp as in Program 6.4.
                if (conv_im || iters_im == limit) std::fill(bdylds_im.begin(), bdylds_im.end(), 0.0);
                double fmax_im = 0.0;
                for (int iel = 0; iel < nels; ++iel) {
                    int m = etype[iel];
                    double phi_d = prop[0][m], c_d = prop[1][m], psi_d = prop[2][m];
                    double e_v   = prop[4][m], nu_v = prop[5][m];

                    for (int k = 0; k < nod; ++k) coord[k] = g_coord[g_num[k][iel]-1];
                    std::vector<double> Nrep;
                    double detJrep = 0.0;
                    double radial_r = 1.0;
                    if (!build_bee_at_gp(mesh_type, coord, gp_rep.xi, gp_rep.eta, bee, Nrep, detJrep, radial_r))
                        continue;
                    double fac = 0.0;
                    for (const auto& gp : gps) {
                        std::vector<double> Ng;
                        double detJ = 0.0;
                        double radial_r = 1.0;
                        if (!build_bee_at_gp(mesh_type, coord, gp.xi, gp.eta, bee, Ng, detJ, radial_r)) continue;
                        fac += gp_int_weight(detJ, gp.w, radial_r);
                    }
                    build_bee_at_gp(mesh_type, coord, gp_rep.xi, gp_rep.eta, bee, Nrep, detJrep, radial_r);
                    double e_eff = e_v, nu_eff = nu_v;
                    effective_elastic_constants(material_law, e_v, nu_v, 0.0, 0.0, e_eff, nu_eff);
                    fem::deemat(dee, e_eff, nu_eff);

                    std::vector<int> num(nod);
                    for (int k = 0; k < nod; ++k) num[k] = g_num[k][iel];
                    fem::num_to_g(num, lnf, nodof, nn, g);

                    std::vector<double> eld(ndof, 0.0);
                    for (int j = 0; j < ndof; ++j) if (g[j] > 0) eld[j] = loads_im[g[j]];

                    auto eps   = matvec(bee, eld);
                    for (int s = 0; s < nst; ++s) eps[s] -= evpt[s][0][iel];
                    auto stress = matvec(dee, eps);
                    for (int s = 0; s < nst; ++s) stress[s] += tensor[s][0][iel];

                    double xg = 0.0, yg = 0.0;
                    element_centroid_xy(g_coord, num, nod, xg, yg);
                    double f = 0.0;
                    std::vector<double> devp(nst, 0.0);
                    if (conv_im || iters_im == limit) {
                        double sigm = 0, dsbar = 0, lode = 0;
                        f = yield_from_stress_gw(
                            stress, gw, gw_u_factor, xg, yg,
                            failure_mode, material_law, phi_d, c_d, sigm, dsbar, lode);
                        devp = stress;
                    } else {
                        std::vector<double> evp;
                        const bool yielded = viscoplastic_strain_increment(
                            stress, gw, gw_u_factor, xg, yg,
                            failure_mode, material_law, phi_d, c_d, psi_d, dt_im,
                            dee, m1, m2, m3, flow, evp, f);
                        if (yielded) {
                            for (int s = 0; s < nst; ++s) evpt[s][0][iel] += evp[s];
                            devp = matvec(dee, evp);
                        }
                    }

                    if (f > fmax_im) fmax_im = f;
                    if (f >= 0.0 || conv_im || iters_im == limit) {
                        auto eload = matvec_T(bee, devp);
                        for (int j = 0; j < ndof; ++j)
                            if (g[j] > 0) bdylds_im[g[j]] += eload[j] * fac;
                    }
                    if (conv_im || iters_im == limit)
                        for (int s = 0; s < nst; ++s) tensor[s][0][iel] = stress[s];
                }
                joint_elements::accumulate_plastic(
                    joint_spec, joint_state, joint_getx, joint_gety, lnf, nodof, nn, loads_im,
                    bdylds_im, dt_im, 1, conv_im || iters_im == limit);
                bdylds_im[0] = 0.0;
                if (conv_im || iters_im == limit) break;
                if (iters_im >= 3 && fmax_im < f_yield_tol_im) force_final_im = true;
            }
            if (iters_im > srf_max_iters_im) srf_max_iters_im = iters_im;
            if (iters_im == limit) srf_all_conv_im = false;
            res << " Inc " << iy << " took " << iters_im << " iterations\n";
            if (iy == incs || iters_im == limit) {
                res << "  Node    x-disp               y-disp\n";
                for (int i = 0; i < nouts; ++i)
                    res << "  " << no[i]
                        << "  " << tot_d[0+(no[i]-1)*nodof]
                        << "  " << tot_d[1+(no[i]-1)*nodof] << "\n";
                // Print radial displacement at tunnel wall nodes (all nodes at r≈a)
                double a_min = 1e30;
                for (int i = 0; i < nn; ++i) {
                    double r = std::sqrt(g_coord[i][0]*g_coord[i][0]+g_coord[i][1]*g_coord[i][1]);
                    if (r < a_min) a_min = r;
                }
                double ur_sum = 0; int nr_wall = 0;
                for (int i = 0; i < nn; ++i) {
                    double xn = g_coord[i][0], yn = g_coord[i][1];
                    double r = std::sqrt(xn*xn + yn*yn);
                    if (std::abs(r - a_min) < 1e-4*a_min + 1e-8) {
                        double ux = tot_d[0+i*nodof], uy = tot_d[1+i*nodof];
                        ur_sum += -(ux*xn/r + uy*yn/r); // inward radial
                        ++nr_wall;
                    }
                }
                if (nr_wall > 0)
                    res << "\n Average inward tunnel-wall displacement u_r(a) = "
                        << ur_sum/nr_wall << " m  (" << nr_wall << " wall nodes)\n";
                break;
            }
        }

        // --- JSON output (two stages: zero reference + result) --------------
        std::vector<StageJson> sr_im;
        std::vector<int> solid_zero(nels, 1);
        {
            StageJson s0; s0.index=0; s0.neq=neq_in_situ; s0.elements_solid=nels; s0.removed=0;
            s0.d.assign(nodof*nn, 0.0);
            std::vector<std::vector<std::vector<double>>>
                zt(nst, std::vector<std::vector<double>>(nip, std::vector<double>(nels,0.0)));
            copy_stress_to_flat(zt, nst, nels, s0.sigma);
            populate_stage_gw_fields(s0, gw, g_coord, g_num, nod, nels, nst);
            populate_stage_yield_fields(s0, zt, evpt, prop, etype, solid_zero,
                                        nels, nst, nod, gw, g_coord, g_num, gw_u_factor);
            populate_stage_strain_fields(s0, mesh_type, g_coord, g_num, solid_zero,
                                         nn, nels, nod, nodof, nst);
            sr_im.push_back(std::move(s0));
        }
        {
            StageJson s1; s1.index=1; s1.neq=neq_in_situ; s1.elements_solid=nels; s1.removed=0;
            s1.d = tot_d;
            copy_stress_to_flat(tensor, nst, nels, s1.sigma);
            populate_stage_gw_fields(s1, gw, g_coord, g_num, nod, nels, nst);
            populate_stage_yield_fields(s1, tensor, evpt, prop, etype, solid_zero,
                                        nels, nst, nod, gw, g_coord, g_num, gw_u_factor);
            populate_stage_strain_fields(s1, mesh_type, g_coord, g_num, solid_zero,
                                         nn, nels, nod, nodof, nst);
            sr_im.push_back(std::move(s1));
        }

        // Build meta block for tunnel auto-detection
        MetaBlock meta_im;
        {
            // Material + Mohr-Coulomb consts: emit only when uniform (or single
            // material). Compares all 6 of the MC-relevant prop rows so that an
            // accidental per-material override (different psi, etc.) does not
            // get smuggled into the meta block as a single representative value.
            bool same = true;
            for (int m = 1; m < np_types && same; ++m)
                for (int p_i = 0; p_i < 6 && same; ++p_i)
                    if (prop[p_i][m] != prop[p_i][0]) same = false;
            if (same) {
                meta_im.E_kPa  = prop[4][0]; meta_im.nu = prop[5][0];
                meta_im.valid_material = true;
                meta_im.phi_deg = prop[0][0];
                meta_im.c_kPa   = prop[1][0];
                meta_im.psi_deg = prop[2][0];
                meta_im.gamma   = prop[3][0];
                meta_im.valid_mc = true;
            }

            // Far-field loading: take the first traction group as representative.
            // .dat is tension-positive, so flip syy to expose compression-positive
            // sigma_v to downstream consumers (matches p610_t3_elastic convention).
            if (!trac_groups.empty()) {
                const auto& tg = trac_groups[0];
                meta_im.sigma_v_kPa = -tg.syy;
                meta_im.K0 = (std::abs(tg.syy) > 1e-12) ? (tg.sxx / tg.syy) : 1.0;
                meta_im.valid_loading = true;
            }

            // Seed nodes: free boundary not covered by traction edges
            auto edge_key=[](int u,int v){ return std::pair<int,int>(std::min(u,v),std::max(u,v)); };
            std::map<std::pair<int,int>,int> ecnt;
            for (int iel=0;iel<nels;++iel) {
                std::vector<int> en(nod);
                for (int k = 0; k < nod; ++k) en[k] = g_num[k][iel];
                for (const auto& e : element_boundary_segments(mesh_type, en))
                    ++ecnt[edge_key(e.first, e.second)];
            }
            std::set<std::pair<int,int>> trac_set;
            for (const auto& tg:trac_groups)
                for (const auto& e:tg.edges) trac_set.insert(edge_key(e.first,e.second));
            auto fully_free=[&](int nid1){
                for(int d=0;d<nodof;++d) if(nf[d+(nid1-1)*nodof]==0) return false; return true;
            };
            std::set<int> free_nodes;
            for (const auto& kv:ecnt)
                if (kv.second==1 && !trac_set.count(kv.first)) {
                    if (fully_free(kv.first.first))  free_nodes.insert(kv.first.first);
                    if (fully_free(kv.first.second)) free_nodes.insert(kv.first.second);
                }
            std::vector<int> seeds;
            for (int nid:free_nodes) seeds.push_back(nid-1);
            double cx,cy,ar;
            if (detect_tunnel_circle_t3(seeds,g_coord,cx,cy,ar)) {
                meta_im.cx=cx; meta_im.cy=cy; meta_im.a=ar; meta_im.valid_tunnel=true;
                res<<"\n Tunnel auto-detected: centre=("<<cx<<","<<cy<<") m, a="<<ar<<" m\n";
            }
        }

        std::vector<int> solid_im(nels, 1);
        populate_srf_diagnostics(meta_im, srf_F_trial, srf_phi_orig, srf_c_orig,
                                 srf_max_iters_im, limit, srf_all_conv_im,
                                 tensor, evpt, tot_d, prop, etype, solid_im,
                                 nn, nels, nodof, nst,
                                 gw, g_coord, g_num, nod, gw_u_factor);
        double reaction_fx = 0.0, reaction_fy = 0.0;
        recover_support_reactions(tensor, g_coord, g_num, lnf, nn, nels, nod, nodof, nst, ndof,
                                 mesh_type, reaction_fx, reaction_fy);
        write_json(base, 0, tot_d, sr_im, g_coord, g_num, solid_im, meta_im, nn, nels, nodof, nod, nst,
                   have_ps_nodal, ps_k_h, ps_k_v, ps_nodal_fx, ps_nodal_fy, gw.active(), gw,
                   &joint_spec, true, reaction_fx, reaction_fy);
        res << "\n Done.\n";
        std::cout << "Results: " << base << ".res  and  " << base << ".json\n";
        return 0;
    }
    // =========================================================================
    //  END layers==0 path
    // =========================================================================

    // Cumulative u after each excavation (for `stages` in JSON, separate plots)
    std::vector<StageJson> stage_records;
    {
        StageJson s0;
        s0.index = 0;
        s0.neq = neq_in_situ;
        s0.elements_solid = nels;
        s0.removed = 0;
        s0.d.assign(nodof * nn, 0.0);
        copy_stress_to_flat(tensor, nst, nels, s0.sigma);
        populate_stage_gw_fields(s0, gw, g_coord, g_num, nod, nels, nst);
        {
            std::vector<int> solid0(nels, 1);
            populate_stage_yield_fields(s0, tensor, evpt, prop, etype, solid0,
                                        nels, nst, nod, gw, g_coord, g_num, gw_u_factor);
            populate_stage_strain_fields(s0, mesh_type, g_coord, g_num, solid0,
                                         nn, nels, nod, nodof, nst);
        }
        stage_records.push_back(std::move(s0));
    }
    if (gw.active()) {
        if (gw.method == groundwater::Method::Ru)
            res << "\n Stage 0  (in-situ: Ru pore pressure, Terzaghi effective stress in yield; geostatic σ at T3 centroids; full tensors in "
                << base << ".json  \"stages\"[0])\n";
        else if (gw.method == groundwater::Method::Bbar)
            res << "\n Stage 0  (in-situ: B-bar pore pressure, Terzaghi effective stress in yield; geostatic σ at T3 centroids; full tensors in "
                << base << ".json  \"stages\"[0])\n";
        else
            res << "\n Stage 0  (in-situ: hydrostatic pore pressure, Terzaghi effective stress in yield; geostatic σ at T3 centroids; full tensors in "
                << base << ".json  \"stages\"[0])\n";
    } else {
        res << "\n Stage 0  (in-situ: u=0, geostatic σ at T3 centroids; full tensors in " << base
            << ".json  \"stages\"[0])\n";
    }
    res << "  Freedoms: " << neq_in_situ << "   All elements solid. Removed: 0\n";

    // M2.2 Option B: track per-layer/per-increment iteration counts so the
    // SRF diagnostics in `meta.srf` know whether ANY excavation increment hit
    // the iteration limit (= "diverged" in the soft sense).
    int srf_max_iters = 0;
    bool srf_all_conv = true;

    // =========================================================================
    //  LAYER LOOP
    // =========================================================================
    for (int ii=0;ii<layers;++ii) {
        res<<"\n Excavation number "<<(ii+1)<<"\n";

        int noexe; dat>>noexe;
        std::vector<int> exele(noexe);
        for (int i=0;i<noexe;++i) { dat>>exele[i]; exele[i]--; } // 0-based
        const bool gravity_only_layer = (noexe == 0);

        if (!gravity_only_layer && exele.empty()) {
            res << " Warning: excavation list maps to no internal T3 elements for layer " << (ii + 1) << "\n";
            continue;
        }

        if (!gravity_only_layer) {
            // Mark excavated elements inactive, but KEEP tensor/evpt populated with
            // the pre-excavation values — the release load below needs ∫Bᵀσ₀ dV from
            // each excavated element. The iteration loop already skips !solid[iel]
            // when forming the plastic residual, so leaving σ₀ in `tensor` here is
            // safe (it never reaches formm/mocouf for excavated elements).
            for (int i=0;i<noexe;++i) {
                int kel = exele[i];
                solid[kel] = 0;
            }
            exc_nods_t3(noexe, exele, g_num, totex, ntote, nf, nodof, nn);
        } else {
            if (srf_insitu_k0) {
                res << " In-situ K0 retained (srf_insitu_mode k0); plastic equilibrium under reduced strength (no gravity turn-on)\n";
            } else {
                res << " Gravity equilibration on full mesh (SLOPE64-style, no excavation)\n";
                // Clear Stage-0 K0: FE gravity turn-on from σ=0 (Program 6.4). Pore
                // pressure (hydrostatic/grid/Ru) is ramped with the load fraction.
                for (int iel = 0; iel < nels; ++iel)
                    for (int s = 0; s < nst; ++s) tensor[s][0][iel] = 0.0;
            }
            std::vector<char> node_used(static_cast<size_t>(nn), 0);
            for (int iel = 0; iel < nels; ++iel) {
                if (!solid[iel]) continue;
                for (int k = 0; k < nod; ++k) {
                    int nid = g_num[k][iel];
                    if (nid > 0 && nid <= nn) node_used[static_cast<size_t>(nid - 1)] = 1;
                }
            }
            for (int i = 0; i < nn; ++i) {
                if (node_used[static_cast<size_t>(i)]) continue;
                for (int d = 0; d < nodof; ++d)
                    nf[d + i * nodof] = 0;
            }
        }

        lnf = nf;
        geom::formnf(lnf, nodof, nn);
        neq=0; for (int v:lnf) if (v>neq) neq=v;

        res<<" Freedoms: "<<neq<<"\n";
        res<<" Elements remaining: "<<(nels-ntote)<<" (removed "<<noexe<<")\n";

        if (neq <= 0) {
            res<<" (no active freedoms left; skipping solve for this layer)\n";
            {
                StageJson st;
                st.index = ii + 1;
                st.neq = 0;
                st.elements_solid = nels - ntote;
                st.removed = noexe;
                st.d = tot_d;
                copy_stress_to_flat(tensor, nst, nels, st.sigma);
                populate_stage_gw_fields(st, gw, g_coord, g_num, nod, nels, nst);
                populate_stage_yield_fields(st, tensor, evpt, prop, etype, solid,
                                            nels, nst, nod, gw, g_coord, g_num, gw_u_factor);
                populate_stage_strain_fields(st, mesh_type, g_coord, g_num, solid,
                                             nn, nels, nod, nodof, nst);
                stage_records.push_back(std::move(st));
            }
            continue;
        }

        const edge_distributed_load::Spec edge_spec =
            edge_distributed_load::parse_from_dat_path(dat_path);
        std::vector<double> edge_loads_layer(neq + 1, 0.0);
        if (edge_spec.active())
            edge_distributed_load::assemble(edge_spec, nn, nodof, g_coord, lnf, edge_loads_layer);

        const nodal_point_load::Spec nodal_spec =
            nodal_point_load::parse_from_dat_path(dat_path);
        std::vector<double> nodal_loads_layer(neq + 1, 0.0);
        if (nodal_spec.active())
            nodal_point_load::assemble(nodal_spec, lnf, nodof, nodal_loads_layer);

        // --- kdiag (skyline profile) ----------------------------------------
        std::vector<int> kdiag(neq, 0);
        for (int iel=0;iel<nels;++iel) {
            std::vector<int> num(nod);
            for (int k=0;k<nod;++k) num[k]=g_num[k][iel];
            fem::num_to_g(num, lnf, nodof, nn, g);
            fem::fkdiag(kdiag, g, neq);
        }
        if (support_spec.active()) {
            std::vector<int> gbar(4, 0);
            for (const auto& bar : support_spec.bars) {
                const int n0 = support_elements::nearest_node(g_coord, bar.hx, bar.hy);
                const int n1 = support_elements::nearest_node(g_coord, bar.tx, bar.ty);
                if (n0 == n1) continue;
                std::vector<int> nbar{n0 + 1, n1 + 1};
                fem::num_to_g(nbar, lnf, nodof, nn, gbar);
                fem::fkdiag(kdiag, gbar, neq);
            }
        }
        joint_elements::apply_fkdiag(joint_spec, lnf, nodof, nn, kdiag, neq, ii + 1);
        for (int i=1;i<neq;++i) kdiag[i]+=kdiag[i-1];

        // --- layer loads: excavation (stress release + weight) or gravity-only residual ---
        std::vector<double> exc_loads(neq+1, 0.0); // 1-based
        if (gravity_only_layer && srf_insitu_k0) {
            // Keep Stage-0 K0 (+ Ru/WT). Apply gravity body force minus ∫Bᵀσ₀ so the
            // net load is the residual imbalance — under reduced strength this residual
            // + viscoplastic flow detects SRF collapse (zero-load K0 checks never fail).
            res << " In-situ K0 residual load: gravity − ∫Bᵀσ₀ (srf_insitu_mode k0)\n";
            for (int iel=0; iel<nels; ++iel) {
                if (!solid[iel]) continue;
                int m = etype[iel];
                double gamma = prop[3][m];
                for (int k=0;k<nod;++k) coord[k]=g_coord[g_num[k][iel]-1];
                double cx = 0.0, cy = 0.0;
                for (int k = 0; k < nod; ++k) {
                    cx += coord[k][0];
                    cy += coord[k][1];
                }
                cx /= static_cast<double>(nod);
                cy /= static_cast<double>(nod);
                const double g_use = groundwater::body_unit_weight(gamma, m, cx, cy, gw);
                std::vector<int> num(nod);
                for (int k=0;k<nod;++k) num[k]=g_num[k][iel];
                fem::num_to_g(num, lnf, nodof, nn, g);
                std::vector<double> eld(ndof, 0.0);
                double fac = 0.0;
                for (const auto& gp : gps) {
                    std::vector<double> Ng;
                    double detJ = 0.0;
                    double radial_r = 1.0;
                    if (!build_bee_at_gp(mesh_type, coord, gp.xi, gp.eta, bee, Ng, detJ, radial_r)) continue;
                    const double integration_weight = gp_int_weight(detJ, gp.w, radial_r);
                    fac += integration_weight;
                    for (int k = 0; k < nod; ++k) {
                        const double wgp = Ng[k] * integration_weight;
                        eld[k * nodof] += wgp;
                        eld[k * nodof + 1] += wgp;
                    }
                }
                slope_pseudo_static::apply_element_pseudo_static(
                    exc_loads, g, nodof, eld, g_use, ps_k_h, ps_k_v);
                std::vector<double> Nrep;
                double detJrep = 0.0;
                double radial_r = 1.0;
                if (!build_bee_at_gp(mesh_type, coord, gp_rep.xi, gp_rep.eta, bee, Nrep, detJrep, radial_r))
                    continue;
                std::vector<double> stress(nst);
                for (int s = 0; s < nst; ++s) stress[s] = tensor[s][0][iel];
                auto bload = matvec_T(bee, stress);
                for (double& v : bload) v *= fac;
                for (int j = 0; j < ndof; ++j)
                    if (g[j] > 0) exc_loads[g[j]] -= bload[j];
            }
        } else if (gravity_only_layer) {
            std::vector<double> ps_global(neq + 1, 0.0);
            for (int iel=0; iel<nels; ++iel) {
                if (!solid[iel]) continue;
                int m = etype[iel];
                double gamma = prop[3][m];
                for (int k=0;k<nod;++k) coord[k]=g_coord[g_num[k][iel]-1];
                double cx = 0.0, cy = 0.0;
                for (int k = 0; k < nod; ++k) {
                    cx += coord[k][0];
                    cy += coord[k][1];
                }
                cx /= static_cast<double>(nod);
                cy /= static_cast<double>(nod);
                const double g_use = groundwater::body_unit_weight(gamma, m, cx, cy, gw);
                std::vector<int> num(nod);
                for (int k=0;k<nod;++k) num[k]=g_num[k][iel];
                fem::num_to_g(num, lnf, nodof, nn, g);
                std::vector<double> eld(ndof, 0.0);
                for (const auto& gp : gps) {
                    std::vector<double> Ng;
                    double detJ = 0.0;
                    double radial_r = 1.0;
                    if (!build_bee_at_gp(mesh_type, coord, gp.xi, gp.eta, bee, Ng, detJ, radial_r)) continue;
                    const double integration_weight = gp_int_weight(detJ, gp.w, radial_r);
                    for (int k = 0; k < nod; ++k) {
                        const double wgp = Ng[k] * integration_weight;
                        eld[k * nodof] += wgp;
                        eld[k * nodof + 1] += wgp;
                    }
                }
                slope_pseudo_static::apply_element_pseudo_static(
                    exc_loads, g, nodof, eld, g_use, ps_k_h, ps_k_v);
                if (export_ps_forces)
                    slope_pseudo_static::accumulate_element_pseudo_static_forces(
                        ps_global, g, nodof, eld, g_use, ps_k_h, ps_k_v);
            }
            if (export_ps_forces) {
                slope_pseudo_static::gather_nodal_forces_from_global(
                    ps_global, lnf, nn, nodof, ps_nodal_fx, ps_nodal_fy);
                have_ps_nodal = true;
            }
        } else {
            for (int ei=0;ei<noexe;++ei) {
                int kel=exele[ei];
                int m=etype[kel];
                double gamma=prop[3][m];

                for (int k=0;k<nod;++k) coord[k]=g_coord[g_num[k][kel]-1];
                double cx = 0.0, cy = 0.0;
                for (int k = 0; k < nod; ++k) {
                    cx += coord[k][0];
                    cy += coord[k][1];
                }
                cx /= static_cast<double>(nod);
                cy /= static_cast<double>(nod);
                const double g_use = groundwater::body_unit_weight(gamma, m, cx, cy, gw);
                std::vector<double> Nrep;
                double detJrep = 0.0;
                double radial_r = 1.0;
                if (!build_bee_at_gp(mesh_type, coord, gp_rep.xi, gp_rep.eta, bee, Nrep, detJrep, radial_r))
                    continue;
                double fac = 0.0;
                for (const auto& gp : gps) {
                    std::vector<double> Ng;
                    double detJ = 0.0;
                    double radial_r = 1.0;
                    if (!build_bee_at_gp(mesh_type, coord, gp.xi, gp.eta, bee, Ng, detJ, radial_r)) continue;
                    fac += gp_int_weight(detJ, gp.w, radial_r);
                }
                build_bee_at_gp(mesh_type, coord, gp_rep.xi, gp_rep.eta, bee, Nrep, detJrep, radial_r);

                std::vector<int> num(nod);
                for (int k=0;k<nod;++k) num[k]=g_num[k][kel];
                fem::num_to_g(num, lnf, nodof, nn, g);

                std::vector<double> stress(nst);
                for (int s=0;s<nst;++s) stress[s]=tensor[s][0][kel];

                // bload = bee^T * stress * area  (stress-release force)
                auto bload = matvec_T(bee, stress);
                for (double& v:bload) v*=fac;

                // body-force in y for self-weight at centroid
                std::vector<double> eld(ndof, 0.0);
                for (const auto& gp : gps) {
                    std::vector<double> Ng;
                    double detJ = 0.0;
                    double radial_r = 1.0;
                    if (!build_bee_at_gp(mesh_type, coord, gp.xi, gp.eta, bee, Ng, detJ, radial_r)) continue;
                    for (int k = 0; k < nod; ++k)
                        eld[k * nodof + 1] += Ng[k] * gp_int_weight(detJ, gp.w, radial_r);
                }

                for (int j=0;j<ndof;++j)
                    if (g[j]>0) exc_loads[g[j]] += eld[j]*g_use + bload[j];
            }
        }

        // Now that the release load has been assembled from the pre-excavation
        // stress, wipe the stored tensor / viscoplastic strain on excavated
        // elements so stage snapshots and any accidental reuse see 0.
        for (int ei=0;ei<noexe;++ei) {
            int kel = exele[ei];
            for (int s=0;s<nst;++s) {
                tensor[s][0][kel] = 0.0;
                evpt[s][0][kel]   = 0.0;
            }
        }

        // --- assemble global stiffness ---------------------------------------
        std::vector<double> kv(kdiag[neq-1], 0.0);
        double dt = 1.0e15;
        const double pi = std::acos(-1.0);

        for (int iel=0;iel<nels;++iel) {
            double e=0.0, v=0.0;
            if (solid[iel]) {
                int m=etype[iel];
                double phi=prop[0][m];
                e=prop[4][m]; v=prop[5][m];
                double snph=std::sin(phi*pi/180.0);
                double ddt=4.0*(1.0+v)*(1.0-2.0*v)/(e*(1.0-2.0*v+snph*snph));
                if (ddt<dt) dt=ddt;
            }
            for (int k=0;k<nod;++k) coord[k]=g_coord[g_num[k][iel]-1];
            double e_eff = e, nu_eff = v;
            effective_elastic_constants(material_law, e, v, 0.0, 0.0, e_eff, nu_eff);
            fem::deemat(dee, e_eff, nu_eff);
            for (int i=0;i<ndof;++i) for (int j=0;j<ndof;++j) km[i][j]=0.0;
            for (const auto& gp : gps) {
                std::vector<double> Ng;
                double detJ = 0.0;
                double radial_r = 1.0;
                if (!build_bee_at_gp(mesh_type, coord, gp.xi, gp.eta, bee, Ng, detJ, radial_r)) continue;
                accum_ke(km, bee, dee, gp_int_weight(detJ, gp.w, radial_r));
            }

            std::vector<int> num(nod);
            for (int k=0;k<nod;++k) num[k]=g_num[k][iel];
            fem::num_to_g(num, lnf, nodof, nn, g);
            fem::fsparv(kv, km, g, kdiag);
        }
        if (support_spec.active()) {
            std::vector<std::vector<double>> kbar(4, std::vector<double>(4, 0.0));
            std::vector<int> gbar(4, 0);
            for (const auto& bar : support_spec.bars) {
                const double dx = bar.tx - bar.hx;
                const double dy = bar.ty - bar.hy;
                const int n0 = support_elements::nearest_node(g_coord, bar.hx, bar.hy);
                const int n1 = support_elements::nearest_node(g_coord, bar.tx, bar.ty);
                if (n0 == n1) continue;
                support_elements::fill_truss_km(bar.ea, dx, dy, kbar);
                std::vector<int> nbar{n0 + 1, n1 + 1};
                fem::num_to_g(nbar, lnf, nodof, nn, gbar);
                fem::fsparv(kv, kbar, gbar, kdiag);
            }
        }
        joint_elements::assemble(joint_spec, joint_getx, joint_gety, lnf, nodof, nn, kv, kdiag, ii + 1);

        fem::sparin(kv, kdiag);
        // Gravity turn-on from σ=0 must ramp self-weight. CST T3 needs a finer
        // ramp than classical p610 excavation `incs` (often 20) or the VP loop
        // locks onto a small residual yield and never meets checon. With
        // hydrostatic/grid pore pressure, u follows the same fraction.
        // Example 16 (layered WT + 0.5 m weak seam) needs >=100 gravity steps
        // for dry F≈1 and wet near-critical trials to settle.
        // K0 in-situ mode: residual gravity−∫Bᵀσ₀ with the same gravity increment floor
        // as turn-on (CST / Ru near-critical needs a fine residual ramp).
        const int solve_increments = gravity_only_layer
            ? std::max(incs, 100)
            : std::max(1, incs);
        for (int i=1;i<=neq;++i) exc_loads[i]/=solve_increments;
        for (int i=1;i<=neq;++i) exc_loads[i]+=edge_loads_layer[i]/solve_increments;
        for (int i=1;i<=neq;++i) exc_loads[i]+=nodal_loads_layer[i]/solve_increments;

        // =====================================================================
        //  INCREMENT LOOP
        // =====================================================================
        std::vector<double> loads(neq+1,0.0), oldis(neq+1,0.0), bdylds(neq+1,0.0);

        for (int iy=1;iy<=solve_increments;++iy) {
            int iters=0;
            std::fill(oldis.begin(),oldis.end(),0.0);
            std::fill(bdylds.begin(),bdylds.end(),0.0);
            // reset viscoplastic strains for this increment
            for (int s=0;s<nst;++s)
                for (int el=0;el<nels;++el)
                    evpt[s][0][el]=0.0;
            for (auto& st : joint_state) st = {};

            bool converged=false;
            bool force_final_pass=false;
            const double gravity_fraction =
                (gravity_only_layer && srf_insitu_k0)
                    ? 1.0
                    : static_cast<double>(iy) / static_cast<double>(solve_increments);
            // Ramp pore-pressure contribution with gravity load for all GW methods
            // (hydrostatic, grid, and Ru) so early increments stay stable.
            // K0 in-situ: Stage-0 already used full u — keep gw_u_factor at 1×.
            const double gw_step_factor =
                gravity_only_layer
                    ? gw_u_factor * gravity_fraction
                    : gw_u_factor;
            // Yield residual (kPa) below which CST T3 viscoplastic creep is
            // treated as numerical noise (~5% of the weakest c·cosφ).
            const double pi_tol = std::acos(-1.0);
            double f_strength = 1.0;
            for (int m = 0; m < np_types; ++m) {
                const double cs = prop[1][m] * std::cos(prop[0][m] * pi_tol / 180.0);
                if (m == 0 || cs < f_strength) f_strength = std::max(cs, 1.0);
            }
            const double f_yield_tol = 0.05 * f_strength;
            // -----------------------------------------------------------------
            //  ITERATION LOOP  (Smith Program 6.4 viscoplastic strain method)
            // -----------------------------------------------------------------
            while (true) {
                ++iters;
                std::cout<<"  exc "<<(ii+1)<<"  inc "<<iy<<"  iter "<<iters<<"\n";

                for (int i=0;i<=neq;++i) loads[i]=exc_loads[i]+bdylds[i];
                fem::spabac(kv, loads, kdiag);

                converged = fem::checon(loads, oldis, tol) || force_final_pass;
                if (iters==1) converged=false;

                if (converged||iters==limit) {
                    for (int j=0;j<nn;++j)
                        for (int d=0;d<nodof;++d) {
                            int eq=lnf[d+j*nodof];
                            if (eq>0) tot_d[d+j*nodof]+=loads[eq];
                        }
                }

                // Program 6.4: accumulate B^T D Δεvp across iterations; reset only
                // on the final pass where bodyloads become B^T σ for reactions.
                if (converged||iters==limit) std::fill(bdylds.begin(),bdylds.end(),0.0);

                double fmax = 0.0;
                int iel_fmax = -1;
                for (int iel=0;iel<nels;++iel) {
                    // Excavated elements contribute no stress release or plasticity
                    // to the residual. Skipping them avoids passing zero trial stress
                    // into formm() which computes `1 / (9 * sigm)` and yields Inf.
                    if (!solid[iel]) continue;

                    int m=etype[iel];
                    double phi=prop[0][m], c=prop[1][m], psi=prop[2][m];
                    double e=prop[4][m], v=prop[5][m];

                    for (int k=0;k<nod;++k) coord[k]=g_coord[g_num[k][iel]-1];
                    std::vector<double> Nrep;
                    double detJrep = 0.0;
                    double radial_r = 1.0;
                    if (!build_bee_at_gp(mesh_type, coord, gp_rep.xi, gp_rep.eta, bee, Nrep, detJrep, radial_r))
                        continue;
                    double fac = 0.0;
                    for (const auto& gp : gps) {
                        std::vector<double> Ng;
                        double detJ = 0.0;
                        double radial_r = 1.0;
                        if (!build_bee_at_gp(mesh_type, coord, gp.xi, gp.eta, bee, Ng, detJ, radial_r)) continue;
                        fac += gp_int_weight(detJ, gp.w, radial_r);
                    }
                    build_bee_at_gp(mesh_type, coord, gp_rep.xi, gp_rep.eta, bee, Nrep, detJrep, radial_r);
                    double e_eff = e, nu_eff = v;
                    effective_elastic_constants(material_law, e, v, 0.0, 0.0, e_eff, nu_eff);
                    fem::deemat(dee, e_eff, nu_eff);

                    std::vector<int> num(nod);
                    for (int k=0;k<nod;++k) num[k]=g_num[k][iel];
                    fem::num_to_g(num, lnf, nodof, nn, g);

                    std::vector<double> eld(ndof,0.0);
                    for (int j=0;j<ndof;++j) if (g[j]>0) eld[j]=loads[g[j]];

                    auto eps = matvec(bee, eld);
                    for (int s=0;s<nst;++s) eps[s]-=evpt[s][0][iel];

                    auto stress = matvec(dee, eps);
                    for (int s=0;s<nst;++s) stress[s]+=tensor[s][0][iel];

                    double xg = 0.0, yg = 0.0;
                    element_centroid_xy(g_coord, num, nod, xg, yg);
                    double f = 0.0;
                    double sigm_y = 0.0, dsbar_y = 0.0, lode_y = 0.0;
                    std::vector<double> devp(nst, 0.0);
                    if (converged||iters==limit) {
                        f = yield_from_stress_gw(
                            stress, gw, gw_step_factor, xg, yg,
                            failure_mode, material_law, phi, c, sigm_y, dsbar_y, lode_y);
                        devp=stress;
                    } else {
                        // Skip new VP at tensile effective mean stress (crest CST
                        // noise) for non-SRF / gravity runs. Under SRF, collapse
                        // can leave most GPs tensile after strength reduction;
                        // skipping VP there lets checon "converge" with no failure.
                        f = yield_from_stress_gw(
                            stress, gw, gw_step_factor, xg, yg,
                            failure_mode, material_law, phi, c, sigm_y, dsbar_y, lode_y);
                        const bool allow_tensile_vp = srf_F_trial > 0.0;
                        if ((sigm_y <= 0.0 || allow_tensile_vp) && f >= 0.0) {
                            std::vector<double> evp;
                            const bool yielded = viscoplastic_strain_increment(
                                stress, gw, gw_step_factor, xg, yg,
                                failure_mode, material_law, phi, c, psi, dt,
                                dee, m1, m2, m3, flow, evp, f);
                            if (yielded) {
                                for (int s=0;s<nst;++s) evpt[s][0][iel]+=evp[s];
                                devp=matvec(dee, evp);
                            }
                        }
                    }
                    // fmax for secondary convergence: ignore tensile GPs unless SRF
                    // (same collapse / false-pass pathology as the VP skip above).
                    if ((sigm_y <= 0.0 || srf_F_trial > 0.0) && f > fmax) {
                        fmax = f;
                        iel_fmax = iel;
                    }

                    if (f>=0.0||converged||iters==limit) {
                        auto eload=matvec_T(bee, devp);
                        for (int j=0;j<ndof;++j)
                            if (g[j]>0) bdylds[g[j]]+=eload[j]*fac;
                    }

                    if (converged||iters==limit)
                        for (int s=0;s<nst;++s) tensor[s][0][iel]=stress[s];
                }
                joint_elements::accumulate_plastic(
                    joint_spec, joint_state, joint_getx, joint_gety, lnf, nodof, nn, loads,
                    bdylds, dt, ii + 1, converged || iters == limit);
                bdylds[0]=0.0;
                if (iters > 1 && (iters % 25) == 0) {
                    std::cout<<"    fmax "<<fmax;
                    if (iel_fmax >= 0) {
                        const int m = etype[iel_fmax];
                        std::cout<<"  iel "<<(iel_fmax+1)<<" mat "<<(m+1)
                                 <<" c "<<prop[1][m]<<" phi "<<prop[0][m];
                    }
                    std::cout<<"\n";
                }
                if (converged||iters==limit) break;
                // Secondary convergence on residual yield. Disabled under active
                // groundwater (weak-seam effective yield) and under SRF (collapse
                // must hit the iteration limit so the bracket can detect failure).
                if (!gw.active() && srf_F_trial <= 0.0 && iters >= 3 && fmax < f_yield_tol)
                    force_final_pass = true;
            } // iteration loop

            if (iters > srf_max_iters) srf_max_iters = iters;
            if (iters == limit) srf_all_conv = false;
            res<<" Inc "<<iy<<" took "<<iters<<" iterations\n";

            if (iy==solve_increments||iters==limit) {
                res<<"  Node    x-disp         y-disp\n";
                for (int i=0;i<nouts;++i)
                    res<<"  "<<no[i]
                       <<"  "<<tot_d[0+(no[i]-1)*nodof]
                       <<"  "<<tot_d[1+(no[i]-1)*nodof]<<"\n";
                break;
            }
        } // increment loop

        {
            StageJson st;
            st.index = ii + 1;
            st.neq = neq;
            st.elements_solid = nels - ntote;
            st.removed = noexe;
            st.d = tot_d;
            copy_stress_to_flat(tensor, nst, nels, st.sigma);
            populate_stage_gw_fields(st, gw, g_coord, g_num, nod, nels, nst);
            populate_stage_yield_fields(st, tensor, evpt, prop, etype, solid,
                                        nels, nst, nod, gw, g_coord, g_num, gw_u_factor);
            populate_stage_strain_fields(st, mesh_type, g_coord, g_num, solid,
                                         nn, nels, nod, nodof, nst);
            stage_records.push_back(std::move(st));
        }
        res << "  (Stage " << (ii + 1) << " — σ and u written to " << base << ".json  \"stages\"["
            << (ii + 1) << "].)\n";

    } // layer loop

    // --- assemble optional meta block --------------------------------------
    MetaBlock meta;
    {
        bool same_mat = true;
        for (int m = 1; m < np_types && same_mat; ++m) {
            if (prop[4][m] != prop[4][0] || prop[5][m] != prop[5][0]) same_mat = false;
        }
        if (same_mat) {
            meta.E_kPa          = prop[4][0];
            meta.nu             = prop[5][0];
            meta.valid_material = true;
        }

        // Tunnel = the void boundary that emerges between solid and excavated
        // elements after all layers are stripped. Restrict seed nodes to those
        // with ALL DOFs free (i.e. not on a symmetry/roller boundary), which
        // keeps quarter-symmetric meshes from contaminating the fit with the
        // axis-line corner of the arc.
        auto edge_key = [](int u, int v) {
            return std::pair<int,int>(std::min(u,v), std::max(u,v));
        };
        std::map<std::pair<int,int>, int> shared_count;
        for (int iel = 0; iel < nels; ++iel) {
            if (solid[iel]) continue;
            std::vector<int> en(nod);
            for (int k = 0; k < nod; ++k) en[k] = g_num[k][iel];
            for (const auto& e : element_boundary_segments(mesh_type, en))
                ++shared_count[edge_key(e.first, e.second)];
        }
        std::map<std::pair<int,int>, int> solid_count;
        for (int iel = 0; iel < nels; ++iel) {
            if (!solid[iel]) continue;
            std::vector<int> en(nod);
            for (int k = 0; k < nod; ++k) en[k] = g_num[k][iel];
            for (const auto& e : element_boundary_segments(mesh_type, en))
                ++solid_count[edge_key(e.first, e.second)];
        }
        // After excavation the original `nf` may have had some excavated-only
        // nodes zeroed out, so use the original BC pattern stored at the start
        // of solve.  Re-derive a "fully free" predicate from the current nf
        // (any node with at least one zero DOF is treated as restrained).
        auto is_fully_free = [&](int nid_1based) {
            for (int d = 0; d < nodof; ++d)
                if (nf[d + (nid_1based - 1) * nodof] == 0) return false;
            return true;
        };
        std::set<int> wall_nodes; // 1-based
        for (const auto& kv : shared_count) {
            const auto& k = kv.first;
            if (solid_count.count(k)) {
                if (is_fully_free(k.first))  wall_nodes.insert(k.first);
                if (is_fully_free(k.second)) wall_nodes.insert(k.second);
            }
        }
        std::vector<int> seeds; // 0-based
        seeds.reserve(wall_nodes.size());
        for (int nid : wall_nodes) seeds.push_back(nid - 1);

        double cx, cy, a;
        if (detect_tunnel_circle_t3(seeds, g_coord, cx, cy, a)) {
            meta.cx = cx; meta.cy = cy; meta.a = a;
            meta.valid_tunnel = true;
            res << "\n Tunnel auto-detected: centre = (" << cx << ", " << cy
                << ") m, radius a = " << a << " m  ("
                << seeds.size() << " seed wall nodes)\n";
        }
    }

    populate_srf_diagnostics(meta, srf_F_trial, srf_phi_orig, srf_c_orig,
                             srf_max_iters, limit, srf_all_conv,
                             tensor, evpt, tot_d, prop, etype, solid,
                             nn, nels, nodof, nst,
                             gw, g_coord, g_num, nod, gw_u_factor);

    double reaction_fx = 0.0, reaction_fy = 0.0;
    recover_support_reactions(tensor, g_coord, g_num, lnf, nn, nels, nod, nodof, nst, ndof,
                             mesh_type, reaction_fx, reaction_fy);
    write_json(base, layers, tot_d, stage_records, g_coord, g_num, solid, meta,
               nn, nels, nodof, nod, nst,
               have_ps_nodal, ps_k_h, ps_k_v, ps_nodal_fx, ps_nodal_fy, gw.active(), gw,
               &joint_spec, true, reaction_fx, reaction_fy);

    res<<"\n Done.\n";
    std::cout<<"Results: "<<base<<".res  and  "<<base<<".json\n";
    return 0;
}

#ifndef P610_T3_NO_MAIN
int main(int argc, char* argv[])
{
    return p610_t3_run_entry(argc, argv, "p610_t3_elastoplastic");
}
#endif
