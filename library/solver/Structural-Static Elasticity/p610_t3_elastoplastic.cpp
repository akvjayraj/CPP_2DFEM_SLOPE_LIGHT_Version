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
    int nn, int nels, int nodof, int nst)
{
    if (F_trial <= 0.0) return;
    const double pi_loc = std::acos(-1.0);

    double max_yield = 0.0;
    double max_plastic = 0.0;
    for (int el = 0; el < nels; ++el) {
        if (!solid.empty() && !solid[el]) continue;
        // Element principal stresses from cartesian Smith-Griffiths layout
        // (sxx, syy, txy, szz). Plane stress / plane strain agnostic: we use
        // in-plane principals s1 >= s2 and ignore szz (Mohr-Coulomb in 2D).
        double sxx = tensor[0][0][el];
        double syy = tensor[1][0][el];
        double txy = tensor[2][0][el];
        double sm  = 0.5 * (sxx + syy);
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
    meta.srf_all_increments_converged  = all_increments_converged;
}

static void write_json(
    const std::string& base, int layer_count,
    const std::vector<double>& tot_d,
    const std::vector<StageJson>& stages,
    const std::vector<std::vector<double>>& g_coord,
    const std::vector<std::vector<int>>& g_num,
    const std::vector<int>& solid,
    const MetaBlock& meta,
    int nn, int nels, int nodof, int nod, int nst)
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
    js << "  \"description\":\"`stages[0]` = in-situ (u=0, geostatic stress at T3 centroids). "
          "`stages[1]..K` = after each excavation. Each entry has `nodes` (ux,uy) and `element_stresses` "
          "(sxx,syy,txy,szz plus in-plane principals s1>=s2 and angle theta1_deg measured CCW from +x to "
          "the s1 direction; Smith-Griffiths tension+ sign, same units as E/gamma in .dat). "
          "Top-level `nodes` = final cumulative U.\",\n";

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
            js << "\"srf\":{\"F_trial\":" << meta.srf_F_trial
               << ",\"fos\":"             << meta.srf_fos
               << ",\"phi_orig_deg\":"    << meta.srf_phi_orig
               << ",\"c_orig_kPa\":"      << meta.srf_c_orig
               << ",\"max_yield_ratio\":" << meta.srf_max_yield_ratio
               << ",\"max_disp\":"        << meta.srf_max_displacement
               << ",\"max_plastic_strain\":" << meta.srf_max_plastic_strain
               << ",\"max_iters_used\":"  << meta.srf_max_iters_used
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
               << ",\"x\":" << g_coord[i][0]
               << ",\"y\":" << g_coord[i][1]
               << ",\"ux\":" << d[0 + (i) * nodof]
               << ",\"uy\":" << d[1 + (i) * nodof]
               << "}";
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
               << ",\"x\":" << g_coord[i][0]
               << ",\"y\":" << g_coord[i][1]
               << ",\"ux\":" << st.d[0 + (i) * nodof]
               << ",\"uy\":" << st.d[1 + (i) * nodof]
               << "}";
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
                   << ",\"sxx\":" << sxx
                   << ",\"syy\":" << syy
                   << ",\"txy\":" << txy
                   << ",\"szz\":" << szz
                   << ",\"s1\":" << s1
                   << ",\"s2\":" << s2
                   << ",\"theta1_deg\":" << theta1_deg << "}";
                if (e < nels - 1) js << ",\n";
                else js << "\n";
            }
            js << "        ]";
        }
        js << "\n    }";
        if (si + 1 < stages.size()) js << ",\n";
        else js << "\n";
    }
    js << "  ],\n";

    js << "  \"elements\":[\n";
    for (int i = 0; i < nels; ++i) {
        js << "    {"
           << "\"id\":" << (i + 1);
        for (int k = 0; k < nod; ++k) js << ",\"n" << (k + 1) << "\":" << g_num[k][i];
        js << ",\"active\":" << solid[i] << "}";
        if (i < nels - 1) js << ",\n";
        else js << "\n";
    }
    js << "  ]\n}\n";
}

// ============================================================================
//  Field Stress — Constant / Gravity (replaces legacy K0·γ·depth loop)
//
//  .dat block format (inserted between Materials and Mesh):
//    ! <BEGIN:FieldStress>
//    ! mode_global  (0=Constant, 1=Gravity)
//    1
//    ! Constant: sigma1 sigma3 sigmaZ angle_deg
//    ! Gravity : Kx Kz y_surface use_ratio   (y_surface>1e29 → auto=max-y)
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
                            double& detJ)
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
    return true;
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

enum class FailureMode {
    MohrCoulomb,
    DruckerPrager,
    HoekBrown,
    GeneralizedHoekBrown
};

enum class MaterialLaw {
    Isotropic,
    TransverselyIsotropic,
    Orthotropic,
    DuncanChangHyperbolic,
    NonlinearIsotropic,
    ViscoElastic,
    Plastic
};

static FailureMode parse_failure_mode(const std::string& token)
{
    if (iequals(token, "HoekBrown")) return FailureMode::HoekBrown;
    if (iequals(token, "GeneralizedHoekBrown")) return FailureMode::GeneralizedHoekBrown;
    if (iequals(token, "DruckerPrager")) return FailureMode::DruckerPrager;
    return FailureMode::MohrCoulomb;
}

static MaterialLaw parse_material_law(const std::string& token)
{
    if (iequals(token, "TransverselyIsotropic")) return MaterialLaw::TransverselyIsotropic;
    if (iequals(token, "Orthotropic")) return MaterialLaw::Orthotropic;
    if (iequals(token, "DuncanChangHyperbolic")) return MaterialLaw::DuncanChangHyperbolic;
    if (iequals(token, "NonlinearIsotropic")) return MaterialLaw::NonlinearIsotropic;
    if (iequals(token, "ViscoElastic")) return MaterialLaw::ViscoElastic;
    if (iequals(token, "Plastic")) return MaterialLaw::Plastic;
    return MaterialLaw::Isotropic;
}

static void dp_params_from_mohr_coulomb(double phi_deg, double c_kPa, double& alpha, double& k)
{
    const double pi = std::acos(-1.0);
    const double phir = phi_deg * pi / 180.0;
    const double s = std::sin(phir);
    const double c = std::cos(phir);
    const double den = std::sqrt(3.0) * (3.0 - s);
    alpha = (2.0 * s) / den;
    k = (6.0 * c_kPa * c) / den;
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

static FailureMode effective_failure_mode(FailureMode mode, MaterialLaw law)
{
    if (law == MaterialLaw::Plastic && mode == FailureMode::MohrCoulomb)
        return FailureMode::DruckerPrager;
    return mode;
}

static double yield_value(FailureMode mode, MaterialLaw law, double phi_deg, double c_kPa, double sigm, double dsbar, double lode)
{
    mode = effective_failure_mode(mode, law);
    if (mode == FailureMode::DruckerPrager) {
        double alpha = 0.0, k = 0.0;
        dp_params_from_mohr_coulomb(phi_deg, c_kPa, alpha, k);
        return dsbar + alpha * sigm - k;
    }
    if (mode == FailureMode::HoekBrown || mode == FailureMode::GeneralizedHoekBrown) {
        // Minimal confinement-dependent Hoek-Brown style envelope in p-q space.
        // With only legacy 7-property input rows available, map:
        //   sigma_ci <- max(c, 1 kPa), mb <- tan(phi), s <- 1, a <- 0.5 (HB) / 0.6 (GHB)
        // Compression is negative in this code's stress sign convention.
        const double sigma_ci = std::max(c_kPa, 1.0);
        const double mb = std::max(std::tan(phi_deg * std::acos(-1.0) / 180.0), 1e-6);
        const double s = 1.0;
        const double a = (mode == FailureMode::GeneralizedHoekBrown) ? 0.6 : 0.5;
        const double p_comp = std::max(-sigm, 0.0);
        const double q_allow = sigma_ci * std::pow(mb * (p_comp / sigma_ci) + s, a);
        (void)lode;
        return dsbar - q_allow;
    }
    return fem::mocouf(phi_deg, c_kPa, sigm, dsbar, lode);
}

static void plastic_potential_derivs(FailureMode mode, MaterialLaw law, double psi_deg, double dsbar, double lode, double& dq1, double& dq2, double& dq3)
{
    mode = effective_failure_mode(mode, law);
    if (mode == FailureMode::DruckerPrager) {
        double alpha_g = 0.0, k_dummy = 0.0;
        dp_params_from_mohr_coulomb(psi_deg, 0.0, alpha_g, k_dummy);
        dq1 = alpha_g;
        dq2 = (std::abs(dsbar) > 1e-12) ? (1.0 / dsbar) : 0.0;
        dq3 = 0.0;
        return;
    }
    if (mode == FailureMode::HoekBrown || mode == FailureMode::GeneralizedHoekBrown) {
        // Approximate non-associated flow in p-q form using dilation as DP-like alpha_g.
        double alpha_g = 0.0, k_dummy = 0.0;
        dp_params_from_mohr_coulomb(psi_deg, 0.0, alpha_g, k_dummy);
        dq1 = alpha_g;
        dq2 = (std::abs(dsbar) > 1e-12) ? (1.0 / dsbar) : 0.0;
        dq3 = 0.0;
        (void)lode;
        return;
    }
    fem::mocouq(psi_deg, dsbar, lode, dq1, dq2, dq3);
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
        std::cerr << "AnalysisOptions solver_executable mismatch: file requests '" << solver_opt
                  << "' but executable is '" << g_solver_name << "'.\n";
        return 2;
    }
    const std::string mesh_opt = read_analysis_option(dat_path, "mesh_type");
    const std::string mesh_type = mesh_opt.empty() ? "T3" : mesh_opt;
    const std::string failure_opt = read_analysis_option(dat_path, "failure_criterion");
    const FailureMode failure_mode = parse_failure_mode(failure_opt);
    const std::string material_opt = read_analysis_option(dat_path, "material_type");
    const MaterialLaw material_law = parse_material_law(material_opt);

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
        const double pi_srf = std::acos(-1.0);
        for (int m = 0; m < np_types; ++m) {
            const double phi_r = prop[0][m] * pi_srf / 180.0;
            const double psi_r = prop[2][m] * pi_srf / 180.0;
            // tan(phi_eff) = tan(phi) / F   (F >= 1 reduces strength)
            prop[0][m] = std::atan(std::tan(phi_r) / srf_F_trial) * 180.0 / pi_srf;
            prop[2][m] = std::atan(std::tan(psi_r) / srf_F_trial) * 180.0 / pi_srf;
            // c_eff = c / F
            prop[1][m] = prop[1][m] / srf_F_trial;
        }
        std::cout << " M2.2 SRF mode: F_trial = " << srf_F_trial
                  << "; reduced phi_orig = " << srf_phi_orig << " deg, c_orig = "
                  << srf_c_orig << " kPa across " << np_types << " material(s).\n";
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

    // Initial geostatic stresses at centroid of each element.
    //   tensor[s][i][el]: s=stress component (0=sxx,1=syy,2=txy,3=szz),
    //                     i=Gauss pt (always 0 for T3), el=element.
    // Sign convention: tension-positive (compression-negative).
    //
    // Mode is selected by the optional ! <BEGIN:FieldStress> block in the .dat.
    // When the block is absent the solver falls back to the legacy per-material
    // K0·γ·depth gravity loop so existing .dat files continue to work unchanged.
    FieldStress fs = read_field_stress_block(dat_path);

    // y_top: free-surface elevation (auto-detected as max mesh y).
    double y_top = g_coord[0][1];
    for (int i = 1; i < nn; ++i) if (g_coord[i][1] > y_top) y_top = g_coord[i][1];

    std::vector<std::vector<std::vector<double>>>
        tensor(nst, std::vector<std::vector<double>>(nip, std::vector<double>(nels,0.0)));
    for (int iel = 0; iel < nels; ++iel) {
        int m = etype[iel];
        const FieldStressSpec& spec = fs.for_material(m);
        const int nvert = mesh_vertex_count(mesh_type);
        double yc = 0.0;
        for (int k = 0; k < nvert; ++k) yc += g_coord[g_num[k][iel]-1][1];
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
            const double gamma = prop[3][m];
            const double Kx    = fs.is_legacy ? prop[6][m] : spec.Kx;
            const double Kz    = fs.is_legacy ? prop[6][m] : spec.Kz;
            const double surf  = (spec.y_surface > 1.0e29) ? y_top : spec.y_surface;
            const double sv    = -gamma * (surf - yc);
            sxx = Kx * sv;
            syy = sv;
            szz = Kz * sv;
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
                if (!build_bee_at_gp(mesh_type, coord, gp.xi, gp.eta, bee, N, detJ)) continue;
                accum_ke(km, bee, dee, detJ * gp.w);
            }

            std::vector<int> num(nod);
            for (int k = 0; k < nod; ++k) num[k] = g_num[k][iel];
            fem::num_to_g(num, lnf, nodof, nn, g);
            fem::fsparv(kv_im, km, g, kdiag_im);
        }
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

            int iters_im = 0;
            bool conv_im = false;
            while (true) {
                ++iters_im;
                for (int i = 0; i <= neq_in_situ; ++i)
                    loads_im[i] = trac_loads[i] + bdylds_im[i];
                fem::spabac(kv_im, loads_im, kdiag_im);
                conv_im = fem::checon(loads_im, oldis_im, tol);
                if (iters_im == 1) conv_im = false;

                if (conv_im || iters_im == limit) {
                    for (int j = 0; j < nn; ++j)
                        for (int d = 0; d < nodof; ++d) {
                            int eq = lnf[d + j*nodof];
                            if (eq > 0) tot_d[d + j*nodof] += loads_im[eq];
                        }
                }

                std::fill(bdylds_im.begin(), bdylds_im.end(), 0.0);
                for (int iel = 0; iel < nels; ++iel) {
                    int m = etype[iel];
                    double phi_d = prop[0][m], c_d = prop[1][m], psi_d = prop[2][m];
                    double e_v   = prop[4][m], nu_v = prop[5][m];

                    for (int k = 0; k < nod; ++k) coord[k] = g_coord[g_num[k][iel]-1];
                    std::vector<double> Nrep;
                    double detJrep = 0.0;
                    if (!build_bee_at_gp(mesh_type, coord, gp_rep.xi, gp_rep.eta, bee, Nrep, detJrep))
                        continue;
                    double fac = 0.0;
                    for (const auto& gp : gps) {
                        std::vector<double> Ng;
                        double detJ = 0.0;
                        if (!build_bee_at_gp(mesh_type, coord, gp.xi, gp.eta, bee, Ng, detJ)) continue;
                        fac += detJ * gp.w;
                    }
                    build_bee_at_gp(mesh_type, coord, gp_rep.xi, gp_rep.eta, bee, Nrep, detJrep);
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

                    double sigm = 0, dsbar = 0, lode = 0;
                    fem::invar(stress, &sigm, &dsbar, &lode);
                    double f = yield_value(failure_mode, material_law, phi_d, c_d, sigm, dsbar, lode);

                    std::vector<double> devp(nst, 0.0);
                    if (conv_im || iters_im == limit) {
                        devp = stress;
                    } else if (f >= 0.0) {
                        double dq1 = 0, dq2 = 0, dq3 = 0;
                        plastic_potential_derivs(failure_mode, material_law, psi_d, dsbar, lode, dq1, dq2, dq3);
                        fem::formm(stress, m1, m2, m3);
                        for (int a = 0; a < nst; ++a) for (int b = 0; b < nst; ++b)
                            flow[a][b] = f*(m1[a][b]*dq1 + m2[a][b]*dq2 + m3[a][b]*dq3);
                        auto erate = matvec(flow, stress);
                        std::vector<double> evp(nst);
                        for (int s = 0; s < nst; ++s) { evp[s] = erate[s]*dt_im; evpt[s][0][iel] += evp[s]; }
                        devp = matvec(dee, evp);
                    }

                    if (f >= 0.0 || conv_im || iters_im == limit) {
                        auto eload = matvec_T(bee, devp);
                        for (int j = 0; j < ndof; ++j)
                            if (g[j] > 0) bdylds_im[g[j]] += eload[j] * fac;
                    }
                    if (conv_im || iters_im == limit)
                        for (int s = 0; s < nst; ++s) tensor[s][0][iel] = stress[s];
                }
                bdylds_im[0] = 0.0;
                if (conv_im || iters_im == limit) break;
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
        {
            StageJson s0; s0.index=0; s0.neq=neq_in_situ; s0.elements_solid=nels; s0.removed=0;
            s0.d.assign(nodof*nn, 0.0);
            std::vector<std::vector<std::vector<double>>>
                zt(nst, std::vector<std::vector<double>>(nip, std::vector<double>(nels,0.0)));
            copy_stress_to_flat(zt, nst, nels, s0.sigma);
            sr_im.push_back(std::move(s0));
        }
        {
            StageJson s1; s1.index=1; s1.neq=neq_in_situ; s1.elements_solid=nels; s1.removed=0;
            s1.d = tot_d;
            copy_stress_to_flat(tensor, nst, nels, s1.sigma);
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
                                 nn, nels, nodof, nst);
        write_json(base, 0, tot_d, sr_im, g_coord, g_num, solid_im, meta_im, nn, nels, nodof, nod, nst);
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
        stage_records.push_back(std::move(s0));
    }
    res << "\n Stage 0  (in-situ: u=0, geostatic σ at T3 centroids; full tensors in " << base
        << ".json  \"stages\"[0])\n";
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
        if (exele.empty()) {
            res << " Warning: excavation list maps to no internal T3 elements for layer " << (ii + 1) << "\n";
            continue;
        }

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
                stage_records.push_back(std::move(st));
            }
            continue;
        }

        // --- kdiag (skyline profile) ----------------------------------------
        std::vector<int> kdiag(neq, 0);
        for (int iel=0;iel<nels;++iel) {
            std::vector<int> num(nod);
            for (int k=0;k<nod;++k) num[k]=g_num[k][iel];
            fem::num_to_g(num, lnf, nodof, nn, g);
            fem::fkdiag(kdiag, g, neq);
        }
        for (int i=1;i<neq;++i) kdiag[i]+=kdiag[i-1];

        // --- excavation loads (in-situ stress release + self-weight) ---------
        // SRF slope decks use an empty first layer as a gravity-equilibration
        // step. With no excavated elements there is no stress-release load, so
        // assemble self-weight over all currently solid elements; otherwise
        // every SRF trial solves K u = 0 and exports umax = 0.
        std::vector<double> exc_loads(neq+1, 0.0); // 1-based
        {
            std::vector<int> load_elements;
            if (noexe == 0 && srf_F_trial > 0.0) {
                load_elements.reserve(nels);
                for (int iel = 0; iel < nels; ++iel)
                    if (solid[iel]) load_elements.push_back(iel);
            } else {
                load_elements = exele;
            }

            for (int kel : load_elements) {
                int m=etype[kel];
                double gamma=prop[3][m];

                for (int k=0;k<nod;++k) coord[k]=g_coord[g_num[k][kel]-1];
                std::vector<double> Nrep;
                double detJrep = 0.0;
                if (!build_bee_at_gp(mesh_type, coord, gp_rep.xi, gp_rep.eta, bee, Nrep, detJrep))
                    continue;
                double fac = 0.0;
                for (const auto& gp : gps) {
                    std::vector<double> Ng;
                    double detJ = 0.0;
                    if (!build_bee_at_gp(mesh_type, coord, gp.xi, gp.eta, bee, Ng, detJ)) continue;
                    fac += detJ * gp.w;
                }
                build_bee_at_gp(mesh_type, coord, gp_rep.xi, gp_rep.eta, bee, Nrep, detJrep);

                std::vector<int> num(nod);
                for (int k=0;k<nod;++k) num[k]=g_num[k][kel];
                fem::num_to_g(num, lnf, nodof, nn, g);

                std::vector<double> stress(nst);
                for (int s=0;s<nst;++s) stress[s]=tensor[s][0][kel];

                // bload = bee^T * stress * area  (stress-release force).
                // Gravity equilibration has no excavated stress release.
                auto bload = (noexe == 0 && srf_F_trial > 0.0)
                    ? std::vector<double>(ndof, 0.0)
                    : matvec_T(bee, stress);
                for (double& v:bload) v*=fac;

                // body-force in y for self-weight at centroid
                std::vector<double> eld(ndof, 0.0);
                for (const auto& gp : gps) {
                    std::vector<double> Ng;
                    double detJ = 0.0;
                    if (!build_bee_at_gp(mesh_type, coord, gp.xi, gp.eta, bee, Ng, detJ)) continue;
                    for (int k = 0; k < nod; ++k)
                        eld[k * nodof + 1] += Ng[k] * detJ * gp.w;
                }

                for (int j=0;j<ndof;++j)
                    if (g[j]>0) exc_loads[g[j]] += eld[j]*gamma + bload[j];
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
                if (!build_bee_at_gp(mesh_type, coord, gp.xi, gp.eta, bee, Ng, detJ)) continue;
                accum_ke(km, bee, dee, detJ * gp.w);
            }

            std::vector<int> num(nod);
            for (int k=0;k<nod;++k) num[k]=g_num[k][iel];
            fem::num_to_g(num, lnf, nodof, nn, g);
            fem::fsparv(kv, km, g, kdiag);
        }

        fem::sparin(kv, kdiag);
        for (int i=1;i<=neq;++i) exc_loads[i]/=incs;

        // =====================================================================
        //  INCREMENT LOOP
        // =====================================================================
        std::vector<double> loads(neq+1,0.0), oldis(neq+1,0.0), bdylds(neq+1,0.0);

        for (int iy=1;iy<=incs;++iy) {
            int iters=0;
            std::fill(oldis.begin(),oldis.end(),0.0);
            std::fill(bdylds.begin(),bdylds.end(),0.0);
            // reset viscoplastic strains for this increment
            for (int s=0;s<nst;++s)
                for (int el=0;el<nels;++el)
                    evpt[s][0][el]=0.0;

            bool converged=false;

            // -----------------------------------------------------------------
            //  ITERATION LOOP
            // -----------------------------------------------------------------
            while (true) {
                ++iters;
                std::cout<<"  exc "<<(ii+1)<<"  inc "<<iy<<"  iter "<<iters<<"\n";

                for (int i=0;i<=neq;++i) loads[i]=exc_loads[i]+bdylds[i];
                fem::spabac(kv, loads, kdiag);

                converged = fem::checon(loads, oldis, tol);
                if (iters==1) converged=false;

                if (converged||iters==limit) {
                    for (int j=0;j<nn;++j)
                        for (int d=0;d<nodof;++d) {
                            int eq=lnf[d+j*nodof];
                            if (eq>0) tot_d[d+j*nodof]+=loads[eq];
                        }
                }

                std::fill(bdylds.begin(),bdylds.end(),0.0);

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
                    if (!build_bee_at_gp(mesh_type, coord, gp_rep.xi, gp_rep.eta, bee, Nrep, detJrep))
                        continue;
                    double fac = 0.0;
                    for (const auto& gp : gps) {
                        std::vector<double> Ng;
                        double detJ = 0.0;
                        if (!build_bee_at_gp(mesh_type, coord, gp.xi, gp.eta, bee, Ng, detJ)) continue;
                        fac += detJ * gp.w;
                    }
                    build_bee_at_gp(mesh_type, coord, gp_rep.xi, gp_rep.eta, bee, Nrep, detJrep);
                    double e_eff = e, nu_eff = v;
                    effective_elastic_constants(material_law, e, v, 0.0, 0.0, e_eff, nu_eff);
                    fem::deemat(dee, e_eff, nu_eff);

                    std::vector<int> num(nod);
                    for (int k=0;k<nod;++k) num[k]=g_num[k][iel];
                    fem::num_to_g(num, lnf, nodof, nn, g);

                    std::vector<double> eld(ndof,0.0);
                    for (int j=0;j<ndof;++j) if (g[j]>0) eld[j]=loads[g[j]];

                    // strain increment
                    auto eps = matvec(bee, eld);
                    for (int s=0;s<nst;++s) eps[s]-=evpt[s][0][iel];

                    // trial stress = stored + D*(eps - evpt)
                    auto stress = matvec(dee, eps);
                    for (int s=0;s<nst;++s) stress[s]+=tensor[s][0][iel];

                    double sigm=0.0, dsbar=0.0, lode_theta=0.0;
                    fem::invar(stress, &sigm, &dsbar, &lode_theta);
                    double f = yield_value(failure_mode, material_law, phi, c, sigm, dsbar, lode_theta);

                    std::vector<double> devp(nst, 0.0);
                    if (converged||iters==limit) {
                        devp=stress;
                    } else {
                        if (f>=0.0) {
                            double dq1=0.0, dq2=0.0, dq3=0.0;
                            plastic_potential_derivs(failure_mode, material_law, psi, dsbar, lode_theta, dq1, dq2, dq3);
                            fem::formm(stress, m1, m2, m3);
                            for (int a=0;a<nst;++a) for (int b=0;b<nst;++b)
                                flow[a][b]=f*(m1[a][b]*dq1+m2[a][b]*dq2+m3[a][b]*dq3);
                            auto erate=matvec(flow, stress);
                            std::vector<double> evp(nst);
                            for (int s=0;s<nst;++s) evp[s]=erate[s]*dt;
                            for (int s=0;s<nst;++s) evpt[s][0][iel]+=evp[s];
                            devp=matvec(dee, evp);
                        }
                    }

                    if (f>=0.0||converged||iters==limit) {
                        auto eload=matvec_T(bee, devp);
                        for (int j=0;j<ndof;++j)
                            if (g[j]>0) bdylds[g[j]]+=eload[j]*fac;
                    }

                    if (converged||iters==limit)
                        for (int s=0;s<nst;++s) tensor[s][0][iel]=stress[s];
                }
                bdylds[0]=0.0;
                if (converged||iters==limit) break;
            } // iteration loop

            if (iters > srf_max_iters) srf_max_iters = iters;
            if (iters == limit) srf_all_conv = false;
            res<<" Inc "<<iy<<" took "<<iters<<" iterations\n";

            if (iy==incs||iters==limit) {
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
                             nn, nels, nodof, nst);

    write_json(base, layers, tot_d, stage_records, g_coord, g_num, solid, meta,
               nn, nels, nodof, nod, nst);

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
