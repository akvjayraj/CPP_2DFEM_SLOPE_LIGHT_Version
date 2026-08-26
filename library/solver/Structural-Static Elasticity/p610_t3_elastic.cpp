/**
 * p610_t3_elastic  -  Plane-strain linear-elastic analysis using T3 elements.
 *
 * Single direct stiffness solve (no plasticity, no VP iterations).
 * Far-field stresses are applied as consistent Neumann traction BCs on
 * specified boundary edges — the standard FEM technique for approximating
 * an infinite elastic domain with a finite annular mesh.
 *
 * Verification target: Kirsch (1898) cylindrical-hole solution.
 *   u_r(r) = p*a^2 / (2*G*r)      (isotropic far-field p, plane-strain)
 *   G = E / (2*(1+v))
 *
 * Input (.dat):
 *   Line 1 : nels  nn  np_types
 *   Block 2: 7 x np_types material properties (row-major)
 *            order: phi  c  psi  gamma  E  v  K0   (only E=prop[4], v=prop[5] used)
 *   Block 3: etype(1..nels)   [omitted when np_types==1]
 *   Block 4: nn rows  x  y   (nodal coordinates)
 *   Block 5: nels rows  n1 n2 n3   (connectivity, 1-based)
 *   Line 6 : nr
 *            nr lines: node  fix_x  fix_y   (0=restrained, 1=free)
 *   Line 7 : nouts
 *            nouts output node numbers
 *   Line 8 : n_traction_groups
 *   Per group:
 *     mode  p0  p1  p2
 *       mode=0  direct:      sxx=p0, syy=p1, sxy=p2
 *       mode=1  far-field:   sxx=K0*sigma_v, syy=sigma_v, sxy=0
 *                            (p0=sigma_v, p1=K0, p2 ignored)
 *     n_edges
 *     n_edges rows: n1 n2   (1-based; CCW boundary → outward normal = right-hand of edge)
 *
 * Output:
 *   <name>.res   text log (freedoms, node displacements)
 *   <name>.json  stages[0]=reference zero state, stages[1]=elastic solution
 *                (same JSON schema as p610_t3 — compatible with visualize.html)
 */

#include <algorithm>
#include <array>
#include <cctype>
#include <cmath>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <map>
#include <set>
#include <sstream>
#include <string>
#include <utility>
#include <vector>

#include "deemat.hpp"
#include "fkdiag.hpp"
#include "fsparv.hpp"
#include "num_to_g.hpp"
#include "spabac.hpp"
#include "sparin.hpp"
#include "formnf.hpp"

// ============================================================================
//  T3 B-matrix (identical to p610_t3)
// ============================================================================
static void bee3(std::vector<std::vector<double>>& bee,
                 const std::vector<std::vector<double>>& coord,
                 double& area)
{
    const double x1=coord[0][0], y1=coord[0][1];
    const double x2=coord[1][0], y2=coord[1][1];
    const double x3=coord[2][0], y3=coord[2][1];

    double det2 = (x2-x1)*(y3-y1) - (x3-x1)*(y2-y1);
    area = 0.5*std::abs(det2);
    if (std::abs(det2) < 1e-20)
        det2 = (det2 >= 0.0) ? 1e-20 : -1e-20;

    const double b1=(y2-y3)/det2, b2=(y3-y1)/det2, b3=(y1-y2)/det2;
    const double c1=(x3-x2)/det2, c2=(x1-x3)/det2, c3=(x2-x1)/det2;

    for (int i=0;i<4;++i) for (int j=0;j<6;++j) bee[i][j]=0.0;
    bee[0][0]=b1; bee[0][2]=b2; bee[0][4]=b3;
    bee[1][1]=c1; bee[1][3]=c2; bee[1][5]=c3;
    bee[2][0]=c1; bee[2][1]=b1; bee[2][2]=c2;
    bee[2][3]=b2; bee[2][4]=c3; bee[2][5]=b3;
}

// ============================================================================
//  Matrix helpers
// ============================================================================
static std::vector<double>
matvec(const std::vector<std::vector<double>>& A, const std::vector<double>& x)
{
    int m=(int)A.size();
    std::vector<double> y(m,0.0);
    for (int i=0;i<m;++i) for (int j=0;j<(int)x.size();++j) y[i]+=A[i][j]*x[j];
    return y;
}

static std::vector<std::vector<double>>
matmul(const std::vector<std::vector<double>>& A,
       const std::vector<std::vector<double>>& B)
{
    int m=(int)A.size(), p=(int)B.size(), n=(int)B[0].size();
    std::vector<std::vector<double>> C(m, std::vector<double>(n,0.0));
    for (int i=0;i<m;++i) for (int k=0;k<p;++k) if (A[i][k]!=0.0)
        for (int j=0;j<n;++j) C[i][j]+=A[i][k]*B[k][j];
    return C;
}

static void accum_ke(std::vector<std::vector<double>>& km,
                     const std::vector<std::vector<double>>& bee,
                     const std::vector<std::vector<double>>& dee,
                     double area)
{
    auto db = matmul(dee, bee);
    int ndof=(int)bee[0].size(), nst=(int)bee.size();
    for (int i=0;i<ndof;++i) for (int j=0;j<ndof;++j) {
        double s=0.0;
        for (int k=0;k<nst;++k) s+=bee[k][i]*db[k][j];
        km[i][j]+=s*area;
    }
}

// ============================================================================
//  JSON output (same schema as p610_t3 — stages[0]=zero, stages[1]=result)
// ============================================================================
struct StageJson {
    int index{0}, neq{0}, elements_solid{0}, removed{0};
    std::vector<double> d;
    std::vector<double> sigma;
};

// Optional metadata block: tunnel geometry, far-field loading, isotropic material.
// Sign convention for `loading`: compression-positive (Smith-Griffiths .dat is
// tension-positive, so values are negated when copying from traction groups).
struct MetaBlock {
    bool   valid_tunnel  {false};
    bool   valid_loading {false};
    bool   valid_material{false};
    double cx{0.0}, cy{0.0}, a{0.0};
    double sigma_v_kPa{0.0}, K0{1.0};
    double E_kPa{0.0}, nu{0.0};
};

// ----------------------------------------------------------------------------
//  Algebraic least-squares circle fit. Solves the linear system from the
//  expansion (x-cx)^2 + (y-cy)^2 = a^2  ->  D*x + E*y + F = -(x^2 + y^2)
//  with cx = -D/2, cy = -E/2, a = sqrt(D^2/4 + E^2/4 - F).
//  `nids` are 0-based node indices into `g_coord`. Returns false if the system
//  is degenerate or yields a non-physical radius.
// ----------------------------------------------------------------------------
static bool fit_circle_lsq(const std::vector<int>& nids,
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

// Geometric circle through 3 points (returns false if collinear).
static bool circle_through_3(double ax, double ay, double bx, double by,
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

// ----------------------------------------------------------------------------
//  Tunnel circle detection.
//
//  Strategy: RANSAC-style enumeration of 3-tuples of seed boundary nodes.
//  Each triple defines a candidate circle; we count seeds within a small
//  ABSOLUTE inlier band scaled to the FULL MESH bounding box (a relative-only
//  tolerance scales with radius, so large bogus circles trivially "swallow"
//  all seeds and dominate the score; the seed-only bbox is too small when
//  seeds form a short arc on a much larger circle).
//
//  Candidate circles are rejected when their radius exceeds 0.6 × max mesh
//  extent (tunnel can't span the whole mesh).
//
//  Among surviving circles the one with the most inliers wins, breaking ties
//  by smaller radius (tunnel = innermost hole). A final algebraic LSQ refines
//  the inlier set, so symmetry-cut nodes mixed into the seed set don't bias
//  the result — they simply don't end up as inliers.
// ----------------------------------------------------------------------------
static bool detect_tunnel_circle(const std::vector<int>& seeds,
                                 const std::vector<std::vector<double>>& g_coord,
                                 double& out_cx, double& out_cy, double& out_a)
{
    const int n = (int)seeds.size();
    if (n < 3) return false;

    // Full-mesh bounding box → length scale for absolute inlier tolerance.
    double xmin = 1e300, xmax = -1e300, ymin = 1e300, ymax = -1e300;
    for (const auto& p : g_coord) {
        if (p[0] < xmin) xmin = p[0]; if (p[0] > xmax) xmax = p[0];
        if (p[1] < ymin) ymin = p[1]; if (p[1] > ymax) ymax = p[1];
    }
    const double bx = xmax - xmin, by = ymax - ymin;
    const double bbox_max  = std::max(bx, by);
    if (!(bbox_max > 0.0)) return false;
    const double tol_abs   = 0.01 * bbox_max;
    const double max_r     = 0.6  * bbox_max;

    // Cap triple-enumeration to keep cost bounded on large meshes.
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
                if (!circle_through_3(xi, yi, xj, yj, xk, yk, cx, cy, r)) continue;
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
    if (fit_circle_lsq(inlier_ids, g_coord, cx, cy, r, res) && r > 0.0 && res / r < 0.05) {
        out_cx = cx; out_cy = cy; out_a = r;
    } else {
        out_cx = best_cx; out_cy = best_cy; out_a = best_r;
    }
    return true;
}

static void copy_stress_flat(const std::vector<std::vector<std::vector<double>>>& tensor,
                              int nst, int nels, std::vector<double>& out)
{
    out.resize(nst*nels);
    for (int el=0;el<nels;++el)
        for (int s=0;s<nst;++s) out[s+el*nst]=tensor[s][0][el];
}

// Executable identity. Mesh-specific variants (p610_t6_elastic, p610_q4_elastic,
// p610_q8_elastic) reuse this core via p610_elastic_run_entry and override this name.
static std::string g_solver_name = "p610_t3_elastic";

static void write_json(
    const std::string& base,
    const std::vector<double>& tot_d,
    const std::vector<StageJson>& stages,
    const std::vector<std::vector<double>>& g_coord,
    const std::vector<std::vector<int>>& g_num,
    const MetaBlock& meta,
    int nn, int nels, int nodof, int nod, int nst)
{
    std::ofstream js(base+".json");
    if (!js) return;
    js.setf(std::ios::showpoint);
    js << std::setprecision(17) << std::scientific;

    js << "{\n  \"solver\":\"" << g_solver_name << "\","
       << " \"cumulative_meters_per_stage\":true,\n  \"layers\":1"
       << ",\n  \"n_stages\":" << stages.size()
       << ",\n  \"node_count\":" << nn
       << ",\n  \"element_count\":" << nels << ",\n";
    js << "  \"description\":\"Linear-elastic T3 plane-strain. "
          "stages[0]=zero reference; stages[1]=elastic solution. "
          "element_stresses: sxx,syy,txy,szz (tension+).\",\n";

    if (meta.valid_tunnel || meta.valid_loading || meta.valid_material) {
        js << "  \"meta\":{";
        bool first = true;
        auto sep = [&](){ if (!first) js << ","; first = false; };
        if (meta.valid_tunnel) {
            sep();
            js << "\"tunnel\":{\"cx\":" << meta.cx
               << ",\"cy\":" << meta.cy
               << ",\"a\":"  << meta.a << "}";
        }
        if (meta.valid_loading) {
            sep();
            js << "\"loading\":{\"sigma_v_kPa\":" << meta.sigma_v_kPa
               << ",\"K0\":"                     << meta.K0
               << ",\"convention\":\"compression_positive\"}";
        }
        if (meta.valid_material) {
            sep();
            js << "\"material\":{\"E_kPa\":" << meta.E_kPa
               << ",\"nu\":"                << meta.nu << "}";
        }
        js << "},\n";
    }

    auto write_nodes = [&](const std::vector<double>& d) {
        for (int i=0;i<nn;++i) {
            js << "        {\"id\":"<<(i+1)
               <<",\"x\":"<<g_coord[i][0]
               <<",\"y\":"<<g_coord[i][1]
               <<",\"ux\":"<<d[0+i*nodof]
               <<",\"uy\":"<<d[1+i*nodof]<<"}";
            if (i<nn-1) js<<",\n"; else js<<"\n";
        }
    };

    js << "  \"nodes\":[\n"; write_nodes(tot_d); js << "  ],\n";

    js << "  \"stages\":[\n";
    for (size_t si=0;si<stages.size();++si) {
        const StageJson& st=stages[si];
        js << "    {\"index\":"<<st.index<<",\"name\":";
        if (st.index==0) js<<"\"reference_zero_state\"";
        else             js<<"\"elastic_solution\"";
        js <<",\"neq\":"<<st.neq
           <<",\"elements_solid\":"<<st.elements_solid
           <<",\"removed\":"<<st.removed
           <<",\n        \"nodes\":[\n";
        write_nodes(st.d);
        js << "        ]";
        if (!st.sigma.empty()) {
            js << ",\n        \"element_stresses\":[\n";
            for (int e=0;e<nels;++e) {
                int o=nst*e;
                js << "        {\"id\":"<<(e+1)
                   <<",\"sxx\":"<<st.sigma[o+0]
                   <<",\"syy\":"<<st.sigma[o+1]
                   <<",\"txy\":"<<st.sigma[o+2]
                   <<",\"szz\":"<<st.sigma[o+3]<<"}";
                if (e<nels-1) js<<",\n"; else js<<"\n";
            }
            js << "        ]";
        }
        js << "\n    }";
        if (si+1<stages.size()) js<<",\n"; else js<<"\n";
    }
    js << "  ],\n";

    js << "  \"elements\":[\n";
    for (int i=0;i<nels;++i) {
        js<<"    {\"id\":"<<(i+1);
        for (int k=0;k<nod;++k) js<<",\"n"<<(k+1)<<"\":" <<g_num[k][i];
        js<<",\"active\":1}";
        if (i<nels-1) js<<",\n"; else js<<"\n";
    }
    js << "  ]\n}\n";
}

// ============================================================================
//  Strip !-prefixed comment lines (identical logic to p610_t3)
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
    return 3;
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
    gps.push_back({1.0 / 3.0, 1.0 / 3.0, 0.5});
    return gps;
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
        dN_dxi[0] = -(4.0 * L1 - 1.0); dN_deta[0] = -(4.0 * L1 - 1.0);
        dN_dxi[1] =  4.0 * L2 - 1.0;   dN_deta[1] = 0.0;
        dN_dxi[2] = 0.0;               dN_deta[2] = 4.0 * L3 - 1.0;
        dN_dxi[3] = 4.0 * (L1 - L2);   dN_deta[3] = -4.0 * L2;
        dN_dxi[4] = 4.0 * L3;          dN_deta[4] = 4.0 * L2;
        dN_dxi[5] = -4.0 * L3;         dN_deta[5] = 4.0 * (L1 - L3);
        return;
    }
    if (iequals(mesh_type, "Q4")) {
        N[0] = 0.25 * (1.0 - xi) * (1.0 - eta);
        N[1] = 0.25 * (1.0 + xi) * (1.0 - eta);
        N[2] = 0.25 * (1.0 + xi) * (1.0 + eta);
        N[3] = 0.25 * (1.0 - xi) * (1.0 + eta);
        dN_dxi[0] = -0.25 * (1.0 - eta); dN_deta[0] = -0.25 * (1.0 - xi);
        dN_dxi[1] =  0.25 * (1.0 - eta); dN_deta[1] = -0.25 * (1.0 + xi);
        dN_dxi[2] =  0.25 * (1.0 + eta); dN_deta[2] =  0.25 * (1.0 + xi);
        dN_dxi[3] = -0.25 * (1.0 + eta); dN_deta[3] =  0.25 * (1.0 - xi);
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
        dN_dxi[4] = -xi * (1.0 - eta);      dN_deta[4] = -0.50 * (1.0 - xi * xi);
        dN_dxi[5] =  0.50 * (1.0 - eta * eta); dN_deta[5] = -(1.0 + xi) * eta;
        dN_dxi[6] = -xi * (1.0 + eta);      dN_deta[6] =  0.50 * (1.0 - xi * xi);
        dN_dxi[7] = -0.50 * (1.0 - eta * eta); dN_deta[7] = -(1.0 - xi) * eta;
        return;
    }

    N[0] = 1.0 - xi - eta; N[1] = xi; N[2] = eta;
    dN_dxi[0] = -1.0; dN_deta[0] = -1.0;
    dN_dxi[1] = 1.0;  dN_deta[1] = 0.0;
    dN_dxi[2] = 0.0;  dN_deta[2] = 1.0;
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
        j11 += dN_dxi[a] * x;   j12 += dN_deta[a] * x;
        j21 += dN_dxi[a] * y;   j22 += dN_deta[a] * y;
    }
    detJ = j11 * j22 - j12 * j21;
    if (std::abs(detJ) < 1e-16) return false;
    // See p610_t3_elastoplastic.cpp / build_bee_at_gp for the chain-rule
    // derivation: A = [[j11, j21], [j12, j22]]; [dN/dx; dN/dy] = A^-1 *
    // [dN/dxi; dN/deta]. Earlier this used inv(J) instead of A^-1,
    // silently swapping the off-diagonal terms - undetectable on
    // axis-aligned single-element patch tests but visible in any sheared
    // triangle.
    const double inv11 =  j22 / detJ, inv12 = -j21 / detJ;
    const double inv21 = -j12 / detJ, inv22 =  j11 / detJ;
    const int ndof = 2 * nod;
    for (int i = 0; i < 4; ++i) for (int j = 0; j < ndof; ++j) bee[i][j] = 0.0;
    for (int a = 0; a < nod; ++a) {
        const double dNdx = inv11 * dN_dxi[a] + inv12 * dN_deta[a];
        const double dNdy = inv21 * dN_dxi[a] + inv22 * dN_deta[a];
        const int ux = 2 * a, uy = ux + 1;
        bee[0][ux] = dNdx; bee[1][uy] = dNdy;
        bee[2][ux] = dNdy; bee[2][uy] = dNdx;
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
    for (int i = 0; i < nv; ++i)
        edges.push_back({el_nodes[i], el_nodes[(i + 1) % nv]});
    return edges;
}

// ============================================================================
//  Traction BC group
// ============================================================================
struct TractionGroup {
    double sxx{0.0}, syy{0.0}, sxy{0.0};
    std::vector<std::pair<int,int>> edges; // 1-based node pairs
};

// ============================================================================
//  entry point (shared by p610_t3_elastic and the mesh-specific variant exes)
// ============================================================================
int p610_elastic_run_entry(int argc, char* argv[], const char* solver_name)
{
    g_solver_name = solver_name ? solver_name : "p610_t3_elastic";

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

    std::stringstream dat;
    if (!load_dat_stripped(dat_path, dat)) {
        std::cerr<<"Cannot open "<<dat_path<<"\n"; return 1;
    }
    std::ofstream res(base+".res");
    if (!res) { std::cerr<<"Cannot create "<<base<<".res\n"; return 1; }

    const int nod   = mesh_nodes_per_element(mesh_type);
    const int nodof = 2;
    const int ndof  = nod*nodof; // 6
    const int nst   = 4;         // sxx, syy, txy, szz
    const int nip   = 1;
    const int nprops= 7;

    // --- problem size -------------------------------------------------------
    int nels_raw, nn, np_types;
    dat >> nels_raw >> nn >> np_types;

    // prop[p][m]: phi c psi gamma E v K0  — only E=prop[4], v=prop[5] used
    std::vector<std::vector<double>> prop(nprops, std::vector<double>(np_types));
    for (int p=0;p<nprops;++p) for (int m=0;m<np_types;++m) dat>>prop[p][m];

    std::vector<int> etype_raw(nels_raw, 0);
    if (np_types>1)
        for (int i=0;i<nels_raw;++i) { dat>>etype_raw[i]; etype_raw[i]--; }

    std::vector<std::vector<double>> g_coord(nn, std::vector<double>(2));
    for (int i=0;i<nn;++i) dat>>g_coord[i][0]>>g_coord[i][1];

    int nels = nels_raw;
    std::vector<int> etype = etype_raw;
    if (np_types <= 1) etype.assign(nels, 0);
    std::vector<std::vector<int>> g_num(nod, std::vector<int>(nels));
    for (int i=0;i<nels;++i)
        for (int k=0;k<nod;++k)
            dat>>g_num[k][i];

    // boundary conditions: 0=fixed, 1=free
    std::vector<int> nf(nodof*nn, 1);
    int nr; dat>>nr;
    for (int i=0;i<nr;++i) {
        int k; dat>>k;
        for (int d=0;d<nodof;++d) dat>>nf[d+(k-1)*nodof];
    }

    int nouts; dat>>nouts;
    std::vector<int> no(nouts);
    for (int i=0;i<nouts;++i) dat>>no[i];

    // --- traction BC groups ------------------------------------------------
    int n_trac_groups; dat>>n_trac_groups;
    std::vector<TractionGroup> trac_groups(n_trac_groups);
    for (int g=0;g<n_trac_groups;++g) {
        int mode; double p0, p1, p2;
        dat >> mode >> p0 >> p1 >> p2;
        if (mode == 0) {
            trac_groups[g].sxx = p0;
            trac_groups[g].syy = p1;
            trac_groups[g].sxy = p2;
        } else {
            // mode 1: far-field  sxx = K0 * sigma_v,  syy = sigma_v
            trac_groups[g].syy = p0;
            trac_groups[g].sxx = p1 * p0;
            trac_groups[g].sxy = 0.0;
        }
        int n_edges; dat>>n_edges;
        trac_groups[g].edges.resize(n_edges);
        for (int e=0;e<n_edges;++e) {
            int n1, n2; dat>>n1>>n2;
            trac_groups[g].edges[e] = {n1, n2};
        }
    }

    // --- DOF numbering ------------------------------------------------------
    std::vector<int> lnf = nf;
    geom::formnf(lnf, nodof, nn);
    int neq=0;
    for (int v : lnf) if (v>neq) neq=v;

    res <<" Elements: "<<nels<<"\n";
    res <<" Freedoms: "<<neq<<"\n";
    res <<" Traction groups: "<<n_trac_groups<<"\n";

    // --- skyline profile ----------------------------------------------------
    std::vector<int> kdiag(neq, 0);
    std::vector<int> gv(ndof);
    for (int iel=0;iel<nels;++iel) {
        std::vector<int> num(nod);
        for (int k=0;k<nod;++k) num[k]=g_num[k][iel];
        fem::num_to_g(num, lnf, nodof, nn, gv);
        fem::fkdiag(kdiag, gv, neq);
    }
    for (int i=1;i<neq;++i) kdiag[i]+=kdiag[i-1];

    // --- assemble global stiffness ------------------------------------------
    std::vector<double> kv(kdiag[neq-1], 0.0);

    std::vector<std::vector<double>>
        bee(nst, std::vector<double>(ndof)),
        dee(nst, std::vector<double>(nst)),
        km (ndof, std::vector<double>(ndof));
    std::vector<std::vector<double>> coord(nod, std::vector<double>(2));
    const auto gps = element_gauss_rule(mesh_type);
    const GaussPt2 gp_rep = (iequals(mesh_type, "Q4") || iequals(mesh_type, "Q8"))
        ? GaussPt2{0.0, 0.0, 4.0}
        : GaussPt2{1.0 / 3.0, 1.0 / 3.0, 0.5};

    for (int iel=0;iel<nels;++iel) {
        int m = etype[iel];
        double e = prop[4][m], v = prop[5][m];

        for (int k=0;k<nod;++k) coord[k]=g_coord[g_num[k][iel]-1];
        fem::deemat(dee, e, v);

        for (int i=0;i<ndof;++i) for (int j=0;j<ndof;++j) km[i][j]=0.0;
        for (const auto& gp : gps) {
            std::vector<double> N;
            double detJ = 0.0;
            if (!build_bee_at_gp(mesh_type, coord, gp.xi, gp.eta, bee, N, detJ)) continue;
            accum_ke(km, bee, dee, detJ * gp.w);
        }

        std::vector<int> num(nod);
        for (int k=0;k<nod;++k) num[k]=g_num[k][iel];
        fem::num_to_g(num, lnf, nodof, nn, gv);
        fem::fsparv(kv, km, gv, kdiag);
    }

    fem::sparin(kv, kdiag);

    // --- assemble traction load vector -------------------------------------
    // For a T3 edge (linear interpolation), consistent load with uniform traction:
    //   f_i = f_j = t * L/2
    // Outward normal for CCW mesh, edge i→j: n = (yj-yi, xi-xj) / L
    std::vector<double> loads(neq+1, 0.0); // 1-based

    for (const auto& tg : trac_groups) {
        for (const auto& edge : tg.edges) {
            int ni = edge.first  - 1; // 0-based
            int nj = edge.second - 1;
            double xi = g_coord[ni][0], yi = g_coord[ni][1];
            double xj = g_coord[nj][0], yj = g_coord[nj][1];
            double dx = xj - xi, dy = yj - yi;
            double L  = std::sqrt(dx*dx + dy*dy);
            if (L < 1e-20) continue;

            // Outward unit normal (right-hand of CCW edge direction)
            double nx =  dy / L;
            double ny = -dx / L;

            // Traction: t = sigma * n
            double tx = tg.sxx*nx + tg.sxy*ny;
            double ty = tg.sxy*nx + tg.syy*ny;

            // Consistent nodal forces (linear edge → equal halves)
            double fxi = tx*L*0.5, fyi = ty*L*0.5;
            double fxj = tx*L*0.5, fyj = ty*L*0.5;

            int eqi_x = lnf[0 + ni*nodof];
            int eqi_y = lnf[1 + ni*nodof];
            int eqj_x = lnf[0 + nj*nodof];
            int eqj_y = lnf[1 + nj*nodof];

            if (eqi_x > 0) loads[eqi_x] += fxi;
            if (eqi_y > 0) loads[eqi_y] += fyi;
            if (eqj_x > 0) loads[eqj_x] += fxj;
            if (eqj_y > 0) loads[eqj_y] += fyj;
        }
    }

    // --- solve K u = f -------------------------------------------------------
    fem::spabac(kv, loads, kdiag);

    // Scatter solution to nodal displacement vector
    std::vector<double> tot_d(nodof*nn, 0.0);
    for (int j=0;j<nn;++j)
        for (int d=0;d<nodof;++d) {
            int eq = lnf[d + j*nodof];
            if (eq > 0) tot_d[d + j*nodof] = loads[eq];
        }

    // --- recover element stresses --------------------------------------------
    std::vector<std::vector<std::vector<double>>>
        tensor(nst, std::vector<std::vector<double>>(nip, std::vector<double>(nels, 0.0)));

    for (int iel=0;iel<nels;++iel) {
        int m = etype[iel];
        double e = prop[4][m], v = prop[5][m];

        for (int k=0;k<nod;++k) coord[k]=g_coord[g_num[k][iel]-1];
        std::vector<double> Nrep;
        double detJrep = 0.0;
        if (!build_bee_at_gp(mesh_type, coord, gp_rep.xi, gp_rep.eta, bee, Nrep, detJrep))
            continue;
        fem::deemat(dee, e, v);

        std::vector<int> num(nod);
        for (int k=0;k<nod;++k) num[k]=g_num[k][iel];
        fem::num_to_g(num, lnf, nodof, nn, gv);

        std::vector<double> eld(ndof, 0.0);
        for (int j=0;j<ndof;++j) {
            int node_local = j / nodof;
            int dof_local  = j % nodof;
            eld[j] = tot_d[dof_local + (g_num[node_local][iel]-1)*nodof];
        }

        auto eps   = matvec(bee, eld);
        auto sigma = matvec(dee, eps);

        // plane-strain szz = v*(sxx + syy) (from DEE formulation)
        // deemat already encodes plane-strain, but DEE row 3 = 0, so szz is not
        // in the solved system.  Recover it from the constraint:
        //   eps_z = 0  →  szz = v*(sxx + syy)
        // NOTE: sigma[3] from matvec(dee,eps) will be 0 because deemat row 3 = 0.
        sigma[3] = prop[5][m] * (sigma[0] + sigma[1]);

        for (int s=0;s<nst;++s) tensor[s][0][iel]=sigma[s];
    }

    // --- output .res --------------------------------------------------------
    res << "\n Elastic solution:\n";
    res << "  Node    x-disp              y-disp\n";
    for (int i=0;i<nouts;++i)
        res << "  " << no[i]
            << "  " << tot_d[0+(no[i]-1)*nodof]
            << "  " << tot_d[1+(no[i]-1)*nodof] << "\n";

    // Analytical Kirsch reference (isotropic, single material)
    {
        double E0 = prop[4][0], v0 = prop[5][0];
        double G  = E0 / (2.0*(1.0+v0));
        // Detect isotropic traction (sxx ≈ syy, sxy ≈ 0) from first group
        if (!trac_groups.empty()) {
            const auto& tg0 = trac_groups[0];
            if (std::abs(tg0.sxx - tg0.syy) < 1e-6*std::abs(tg0.syy+1e-10)
                && std::abs(tg0.sxy) < 1e-10) {
                double p  = tg0.syy;
                // find node closest to origin (tunnel centre assumed at 0,0)
                double r_min = 1e30;
                for (int i=0;i<nn;++i) {
                    double r = std::sqrt(g_coord[i][0]*g_coord[i][0] + g_coord[i][1]*g_coord[i][1]);
                    if (r < r_min) r_min = r;
                }
                // Plane-strain Kirsch: u_r(r) = (p/2G)*[(1-2v)*r + a^2/r]
                // At r=a: u_r(a) = p*a*(1-v)/G
                double ur_kirsch = p * r_min * (1.0 - v0) / G;
                res << "\n Kirsch plane-strain (isotropic p=" << p
                    << " kPa, a=" << r_min << " m, E=" << E0
                    << " kPa, v=" << v0 << ", G=" << G << " kPa):\n";
                res << "  u_r(a) = p*a*(1-v)/G = " << ur_kirsch << " m\n";
                // FEM average radial displacement at wall nodes
                double ur_fem_sum = 0.0; int nr_wall = 0;
                for (int i=0;i<nn;++i) {
                    double r = std::sqrt(g_coord[i][0]*g_coord[i][0] + g_coord[i][1]*g_coord[i][1]);
                    if (std::abs(r - r_min) < 1e-4*r_min + 1e-6) {
                        double ux = tot_d[0+i*nodof], uy = tot_d[1+i*nodof];
                        double rx = g_coord[i][0]/r, ry = g_coord[i][1]/r;
                        ur_fem_sum += ux*rx + uy*ry;
                        ++nr_wall;
                    }
                }
                if (nr_wall > 0) {
                    double ur_fem = ur_fem_sum / nr_wall;
                    double err_pct = std::abs(ur_fem - ur_kirsch) / (std::abs(ur_kirsch)+1e-20) * 100.0;
                    res << "  FEM u_r(a) avg (" << nr_wall << " wall nodes) = " << ur_fem << " m\n";
                    res << "  Error vs Kirsch = " << err_pct << " %\n";
                }
            }
        }
    }

    // --- write JSON ---------------------------------------------------------
    std::vector<StageJson> stage_records;
    {
        StageJson s0; s0.index=0; s0.neq=neq; s0.elements_solid=nels; s0.removed=0;
        s0.d.assign(nodof*nn, 0.0);
        std::vector<std::vector<std::vector<double>>>
            zero_tensor(nst, std::vector<std::vector<double>>(nip, std::vector<double>(nels,0.0)));
        copy_stress_flat(zero_tensor, nst, nels, s0.sigma);
        stage_records.push_back(std::move(s0));
    }
    {
        StageJson s1; s1.index=1; s1.neq=neq; s1.elements_solid=nels; s1.removed=0;
        s1.d = tot_d;
        copy_stress_flat(tensor, nst, nels, s1.sigma);
        stage_records.push_back(std::move(s1));
    }

    // --- assemble optional meta block --------------------------------------
    MetaBlock meta;
    {
        // Material: only emit when all materials agree (or single-material case).
        bool same_mat = true;
        for (int m = 1; m < np_types && same_mat; ++m) {
            if (prop[4][m] != prop[4][0] || prop[5][m] != prop[5][0]) same_mat = false;
        }
        if (same_mat) {
            meta.E_kPa        = prop[4][0];
            meta.nu           = prop[5][0];
            meta.valid_material = true;
        }

        // Loading: take the first traction group as the far-field reference.
        // .dat is tension-positive; flip sign so downstream consumers see
        // compression-positive σv.
        if (!trac_groups.empty()) {
            const auto& tg = trac_groups[0];
            meta.sigma_v_kPa = -tg.syy;
            meta.K0 = (std::abs(tg.syy) > 1e-12) ? (tg.sxx / tg.syy) : 1.0;
            meta.valid_loading = true;
        }

        // Tunnel geometry: boundary edges that are
        //   (a) not loaded by any traction group, AND
        //   (b) whose endpoints have ALL DOFs free
        // Condition (b) excludes symmetry-cut / roller nodes that share
        // boundary edges with the tunnel arc but don't actually lie on it,
        // which would otherwise contaminate the circle fit on quarter meshes.
        auto edge_key = [](int u, int v) {
            return std::pair<int,int>(std::min(u,v), std::max(u,v));
        };
        std::map<std::pair<int,int>, int> edge_count;
        for (int iel = 0; iel < nels; ++iel) {
            std::vector<int> en(nod);
            for (int k = 0; k < nod; ++k) en[k] = g_num[k][iel];
            for (const auto& e : element_boundary_segments(mesh_type, en))
                ++edge_count[edge_key(e.first, e.second)];
        }
        std::set<std::pair<int,int>> trac_edge_set;
        for (const auto& tg : trac_groups)
            for (const auto& e : tg.edges)
                trac_edge_set.insert(edge_key(e.first, e.second));

        auto is_fully_free = [&](int nid_1based) {
            for (int d = 0; d < nodof; ++d)
                if (nf[d + (nid_1based - 1) * nodof] == 0) return false;
            return true;
        };
        std::set<int> free_nodes; // 1-based ids
        for (const auto& kv : edge_count) {
            if (kv.second == 1 && !trac_edge_set.count(kv.first)) {
                if (is_fully_free(kv.first.first))  free_nodes.insert(kv.first.first);
                if (is_fully_free(kv.first.second)) free_nodes.insert(kv.first.second);
            }
        }
        std::vector<int> seeds; // 0-based
        seeds.reserve(free_nodes.size());
        for (int nid : free_nodes) seeds.push_back(nid - 1);

        double cx, cy, a;
        if (detect_tunnel_circle(seeds, g_coord, cx, cy, a)) {
            meta.cx = cx; meta.cy = cy; meta.a = a;
            meta.valid_tunnel = true;
            res << "\n Tunnel auto-detected: centre = (" << cx << ", " << cy
                << ") m, radius a = " << a << " m  ("
                << seeds.size() << " seed boundary nodes)\n";
        }
    }

    write_json(base, tot_d, stage_records, g_coord, g_num, meta, nn, nels, nodof, nod, nst);

    res << "\n Done.\n";
    std::cout << "Results: " << base << ".res  and  " << base << ".json\n";
    return 0;
}

#ifndef P610_ELASTIC_NO_MAIN
int main(int argc, char* argv[])
{
    return p610_elastic_run_entry(argc, argv, "p610_t3_elastic");
}
#endif
