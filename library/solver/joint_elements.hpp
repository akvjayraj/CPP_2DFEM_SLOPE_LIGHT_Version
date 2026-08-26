#pragma once

#include "../main/fkdiag.hpp"
#include "../main/fsparv.hpp"
#include "../main/num_to_g.hpp"

#include <algorithm>
#include <cmath>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

namespace joint_elements {

struct Interface {
  int n1 = 0, n2 = 0, n3 = 0, n4 = 0; // 1-based: minus-a, minus-b, plus-b, plus-a
  double kn = 0, ks = 0;
  double c = 0, phi_deg = 0, tension = 0;
  double cres = 0, phires = 0;
  int install_stage = 1;
  int ends = 0; // 0 both_closed, 1 both_open, 2 first_closed, 3 first_open
};

struct Spec {
  bool include = false;
  std::vector<Interface> interfaces;

  bool active() const { return include && !interfaces.empty(); }
};

struct PlasticState {
  bool slipped = false;
  double evp_s[2] = {0, 0};
  double evp_n[2] = {0, 0};
};

inline bool parse_option_line(const std::string& line, const std::string& key, std::string& val) {
  std::string s = line;
  auto b = s.find('!');
  if (b != std::string::npos) s = s.substr(0, b);
  size_t a = 0;
  while (a < s.size() && (s[a] == ' ' || s[a] == '\t')) ++a;
  size_t z = s.size();
  while (z > a && (s[z - 1] == ' ' || s[z - 1] == '\t' || s[z - 1] == '\r')) --z;
  s = s.substr(a, z - a);
  const std::string prefix = key + " ";
  if (s.size() <= prefix.size()) return false;
  for (size_t i = 0; i < prefix.size(); ++i)
    if (std::tolower(static_cast<unsigned char>(s[i])) !=
        std::tolower(static_cast<unsigned char>(prefix[i])))
      return false;
  val = s.substr(prefix.size());
  return true;
}

inline int parse_ends_token(const std::string& tok) {
  std::string t = tok;
  for (char& c : t) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
  if (t == "both_open" || t == "1") return 1;
  if (t == "first_closed_last_open" || t == "2") return 2;
  if (t == "first_open_last_closed" || t == "3") return 3;
  return 0;
}

inline Spec parse_block_from_content(const std::string& content) {
  Spec spec;
  bool in_block = false;
  bool in_ifaces = false;
  for (const auto& raw : [&]() {
         std::vector<std::string> out;
         std::istringstream ss(content);
         std::string line;
         while (std::getline(ss, line)) out.push_back(line);
         return out;
       }()) {
    if (raw.find("<BEGIN:Joints>") != std::string::npos) {
      in_block = true;
      continue;
    }
    if (raw.find("<END:Joints>") != std::string::npos) break;
    if (!in_block) continue;
    std::string line = raw;
    auto bang = line.find('!');
    if (bang != std::string::npos) line = line.substr(bang + 1);
    while (!line.empty() && (line[0] == ' ' || line[0] == '\t')) line.erase(line.begin());
    std::string val;
    if (parse_option_line(line, "include", val)) {
      std::string lower = val;
      for (char& c : lower) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
      spec.include = !(lower == "false" || lower == "0" || lower == "no");
    }
    if (line == "interfaces") {
      in_ifaces = true;
      continue;
    }
    if (!in_ifaces) continue;
    std::istringstream ls(line);
    Interface it;
    std::string ends_tok;
    if (!(ls >> it.n1 >> it.n2 >> it.n3 >> it.n4 >> it.kn >> it.ks >> it.c >> it.phi_deg >>
          it.tension >> ends_tok >> it.install_stage))
      continue;
    it.ends = parse_ends_token(ends_tok);
    if (!(ls >> it.cres >> it.phires)) {
      it.cres = it.c;
      it.phires = it.phi_deg;
    }
    if (it.n1 > 0 && it.n2 > 0 && it.n3 > 0 && it.n4 > 0)
      spec.interfaces.push_back(it);
  }
  return spec;
}

inline Spec parse_from_dat_path(const std::string& dat_path) {
  std::ifstream in(dat_path);
  if (!in) return {};
  std::ostringstream oss;
  oss << in.rdbuf();
  return parse_block_from_content(oss.str());
}

inline bool is_installed(const Interface& it, int current_stage) {
  return current_stage >= std::max(1, it.install_stage);
}

inline bool nodes_in_range(const Interface& it, int nn) {
  return it.n1 >= 1 && it.n1 <= nn && it.n2 >= 1 && it.n2 <= nn && it.n3 >= 1 && it.n3 <= nn &&
         it.n4 >= 1 && it.n4 <= nn;
}

inline double node_x_p610(const std::vector<std::vector<double>>& g_coord, int node0) {
  return g_coord[static_cast<size_t>(node0)][0];
}
inline double node_y_p610(const std::vector<std::vector<double>>& g_coord, int node0) {
  return g_coord[static_cast<size_t>(node0)][1];
}
inline double node_x_p64(const std::vector<std::vector<double>>& g_coord, int node0) {
  return g_coord[0][static_cast<size_t>(node0)];
}
inline double node_y_p64(const std::vector<std::vector<double>>& g_coord, int node0) {
  return g_coord[1][static_cast<size_t>(node0)];
}

/// 8x8 Goodman interface stiffness in global ux,uy. Thickness 0; kn,ks in kPa/m.
inline void fill_interface_km(double x1, double y1, double x2, double y2, double kn, double ks,
                              std::vector<std::vector<double>>& km) {
  for (int i = 0; i < 8; ++i)
    for (int j = 0; j < 8; ++j) km[i][j] = 0.0;
  const double dx = x2 - x1, dy = y2 - y1;
  const double L = std::sqrt(dx * dx + dy * dy);
  if (!(L > 1e-9) || !(kn > 0.0 || ks > 0.0)) return;
  const double sx = dx / L, sy = dy / L;
  const double nx = -sy, ny = sx;
  const double xi[2] = {-1.0 / std::sqrt(3.0), 1.0 / std::sqrt(3.0)};
  const double w = 1.0;
  double B[2][8];
  for (int gp = 0; gp < 2; ++gp) {
    const double Na = 0.5 * (1.0 - xi[gp]);
    const double Nb = 0.5 * (1.0 + xi[gp]);
    const double coef[4] = {-Na, -Nb, Nb, Na};
    for (int i = 0; i < 8; ++i) B[0][i] = B[1][i] = 0.0;
    for (int a = 0; a < 4; ++a) {
      B[0][2 * a] = coef[a] * sx;
      B[0][2 * a + 1] = coef[a] * sy;
      B[1][2 * a] = coef[a] * nx;
      B[1][2 * a + 1] = coef[a] * ny;
    }
    const double fac = w * (L * 0.5);
    for (int i = 0; i < 8; ++i)
      for (int j = 0; j < 8; ++j)
        km[i][j] += fac * (B[0][i] * ks * B[0][j] + B[1][i] * kn * B[1][j]);
  }
}

template <typename GetX, typename GetY>
inline void interface_geometry(const Interface& it, GetX getx, GetY gety, double& x1, double& y1,
                               double& x2, double& y2, double& L, double& sx, double& sy,
                               double& nx, double& ny) {
  x1 = 0.5 * (getx(it.n1 - 1) + getx(it.n4 - 1));
  y1 = 0.5 * (gety(it.n1 - 1) + gety(it.n4 - 1));
  x2 = 0.5 * (getx(it.n2 - 1) + getx(it.n3 - 1));
  y2 = 0.5 * (gety(it.n2 - 1) + gety(it.n3 - 1));
  const double dx = x2 - x1, dy = y2 - y1;
  L = std::sqrt(dx * dx + dy * dy);
  if (L > 1e-9) {
    sx = dx / L;
    sy = dy / L;
  } else {
    sx = 1.0;
    sy = 0.0;
  }
  nx = -sy;
  ny = sx;
}

template <typename GetX, typename GetY>
inline int assemble(
    const Spec& spec, GetX getx, GetY gety, const std::vector<int>& nf, int nodof, int nn,
    std::vector<double>& kv, const std::vector<int>& kdiag, int current_stage) {
  if (!spec.active()) return 0;
  int added = 0;
  std::vector<std::vector<double>> km(8, std::vector<double>(8, 0.0));
  std::vector<int> g(8, 0);
  for (const auto& it : spec.interfaces) {
    if (!is_installed(it, current_stage) || !nodes_in_range(it, nn)) continue;
    double x1, y1, x2, y2, L, sx, sy, nx, ny;
    interface_geometry(it, getx, gety, x1, y1, x2, y2, L, sx, sy, nx, ny);
    if (!(L > 1e-9)) continue;
    fill_interface_km(x1, y1, x2, y2, it.kn, it.ks, km);
    std::vector<int> num{it.n1, it.n2, it.n3, it.n4};
    fem::num_to_g(num, nf, nodof, nn, g);
    fem::fsparv(kv, km, g, kdiag);
    ++added;
  }
  return added;
}

inline void apply_fkdiag(const Spec& spec, const std::vector<int>& nf, int nodof, int nn,
                         std::vector<int>& kdiag, int neq, int current_stage) {
  if (!spec.active()) return;
  std::vector<int> g(8, 0);
  for (const auto& it : spec.interfaces) {
    if (!is_installed(it, current_stage) || !nodes_in_range(it, nn)) continue;
    std::vector<int> num{it.n1, it.n2, it.n3, it.n4};
    fem::num_to_g(num, nf, nodof, nn, g);
    fem::fkdiag(kdiag, g, neq);
  }
}

inline void project_mc(double us, double un, double kn, double ks, double c, double phi_deg,
                       double tension, bool& slipped, double& tau, double& sn_comp) {
  const double tanp = std::tan(phi_deg * 3.14159265358979323846 / 180.0);
  sn_comp = -kn * un; // compression positive
  tau = ks * us;
  if (sn_comp < -tension) {
    sn_comp = -tension;
    tau = 0.0;
    slipped = true;
    return;
  }
  const double tmax = c + std::max(sn_comp, 0.0) * tanp;
  if (std::abs(tau) > tmax && tmax >= 0.0) {
    tau = (tau >= 0.0 ? 1.0 : -1.0) * tmax;
    slipped = true;
  }
}

/// Explicit VP-style plastic increment: add B^T D Δu_rel^p into bdylds.
template <typename GetX, typename GetY>
inline void accumulate_plastic(
    const Spec& spec, std::vector<PlasticState>& state, GetX getx, GetY gety,
    const std::vector<int>& nf, int nodof, int nn, const std::vector<double>& loads,
    std::vector<double>& bdylds, double dt, int current_stage, bool final_pass) {
  if (!spec.active()) return;
  if (state.size() < spec.interfaces.size()) state.resize(spec.interfaces.size());
  std::vector<int> g(8, 0);
  const double xi[2] = {-1.0 / std::sqrt(3.0), 1.0 / std::sqrt(3.0)};
  for (size_t ii = 0; ii < spec.interfaces.size(); ++ii) {
    const auto& it = spec.interfaces[ii];
    if (!is_installed(it, current_stage) || !nodes_in_range(it, nn)) continue;
    double x1, y1, x2, y2, L, sx, sy, nx, ny;
    interface_geometry(it, getx, gety, x1, y1, x2, y2, L, sx, sy, nx, ny);
    if (!(L > 1e-9)) continue;
    std::vector<int> num{it.n1, it.n2, it.n3, it.n4};
    fem::num_to_g(num, nf, nodof, nn, g);
    double u[8] = {0, 0, 0, 0, 0, 0, 0, 0};
    for (int a = 0; a < 8; ++a)
      if (g[a] > 0 && g[a] < static_cast<int>(loads.size())) u[a] = loads[g[a]];

    auto& st = state[ii];
    const double c_use = st.slipped ? it.cres : it.c;
    const double phi_use = st.slipped ? it.phires : it.phi_deg;
    for (int gp = 0; gp < 2; ++gp) {
      const double Na = 0.5 * (1.0 - xi[gp]);
      const double Nb = 0.5 * (1.0 + xi[gp]);
      const double coef[4] = {-Na, -Nb, Nb, Na};
      double urel_x = 0, urel_y = 0;
      for (int a = 0; a < 4; ++a) {
        urel_x += coef[a] * u[2 * a];
        urel_y += coef[a] * u[2 * a + 1];
      }
      double us = sx * urel_x + sy * urel_y - st.evp_s[gp];
      double un = nx * urel_x + ny * urel_y - st.evp_n[gp];
      double tau = 0, sn = 0;
      bool slip = st.slipped;
      project_mc(us, un, it.kn, it.ks, c_use, phi_use, it.tension, slip, tau, sn);
      const double tau_el = it.ks * us;
      const double sn_el = -it.kn * un;
      if (!final_pass) {
        const double dtau = tau_el - tau;
        const double dsn = sn_el - sn;
        if (std::abs(dtau) > 1e-12 && it.ks > 0.0) st.evp_s[gp] += (dtau / it.ks) * std::min(dt, 1.0);
        if (std::abs(dsn) > 1e-12 && it.kn > 0.0) st.evp_n[gp] += (-dsn / it.kn) * std::min(dt, 1.0);
        st.slipped = slip || st.slipped;
      }
      const double fac = (L * 0.5);
      const double fs = it.ks * st.evp_s[gp] * fac;
      const double fn = it.kn * st.evp_n[gp] * fac;
      for (int a = 0; a < 4; ++a) {
        const int eqx = g[2 * a];
        const int eqy = g[2 * a + 1];
        const double fx = coef[a] * (fs * sx + fn * nx);
        const double fy = coef[a] * (fs * sy + fn * ny);
        if (eqx > 0 && eqx < static_cast<int>(bdylds.size())) bdylds[eqx] += fx;
        if (eqy > 0 && eqy < static_cast<int>(bdylds.size())) bdylds[eqy] += fy;
      }
    }
  }
  if (!bdylds.empty()) bdylds[0] = 0.0;
}

/// Mid-edge joint result from the full nodal displacement vector tot_d[dof + node*nodof].
/// un > 0 is opening; closure = -un; sn_kPa is compression-positive (-kn*un before cutoff).
struct Eval {
  int n1 = 0, n2 = 0, n3 = 0, n4 = 0;
  double L = 0, sx = 0, sy = 0, nx = 0, ny = 0;
  double ux_minus = 0, uy_minus = 0, ux_plus = 0, uy_plus = 0;
  double un = 0, us = 0, closure = 0, sn_kPa = 0, tau_kPa = 0;
  bool slipped = false;
};

template <typename GetX, typename GetY>
inline void evaluate(
    const Spec& spec, GetX getx, GetY gety, const std::vector<double>& tot_d, int nodof, int nn,
    std::vector<Eval>& out, int current_stage = 1) {
  out.clear();
  if (!spec.active()) return;
  const auto u_at = [&](int node1, int dof) -> double {
    const int i = node1 - 1;
    if (i < 0 || nodof <= 0) return 0.0;
    const size_t idx = static_cast<size_t>(dof + i * nodof);
    return idx < tot_d.size() ? tot_d[idx] : 0.0;
  };
  for (const auto& it : spec.interfaces) {
    if (!is_installed(it, current_stage) || !nodes_in_range(it, nn)) continue;
    Eval ev;
    ev.n1 = it.n1;
    ev.n2 = it.n2;
    ev.n3 = it.n3;
    ev.n4 = it.n4;
    double x1 = 0, y1 = 0, x2 = 0, y2 = 0;
    interface_geometry(it, getx, gety, x1, y1, x2, y2, ev.L, ev.sx, ev.sy, ev.nx, ev.ny);
    ev.ux_minus = 0.5 * (u_at(it.n1, 0) + u_at(it.n2, 0));
    ev.uy_minus = 0.5 * (u_at(it.n1, 1) + u_at(it.n2, 1));
    ev.ux_plus = 0.5 * (u_at(it.n4, 0) + u_at(it.n3, 0));
    ev.uy_plus = 0.5 * (u_at(it.n4, 1) + u_at(it.n3, 1));
    const double urel_x = ev.ux_plus - ev.ux_minus;
    const double urel_y = ev.uy_plus - ev.uy_minus;
    ev.us = ev.sx * urel_x + ev.sy * urel_y;
    ev.un = ev.nx * urel_x + ev.ny * urel_y;
    ev.closure = -ev.un;
    bool slip = false;
    project_mc(ev.us, ev.un, it.kn, it.ks, it.c, it.phi_deg, it.tension, slip, ev.tau_kPa, ev.sn_kPa);
    ev.slipped = slip;
    out.push_back(ev);
  }
}

} // namespace joint_elements
