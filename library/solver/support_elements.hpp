#pragma once

#include <algorithm>
#include <cmath>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

namespace support_elements {

struct Bar {
  double hx = 0, hy = 0, tx = 0, ty = 0;
  double ea = 0;        // axial rigidity per metre of slope (kN)
  double prestress = 0; // tensile force per metre (kN)
  int install_stage = 1;
  bool exclude_from_srf = true;
};

struct Spec {
  bool include = false;
  std::vector<Bar> bars;

  bool active() const { return include && !bars.empty(); }
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

inline Spec parse_block_from_content(const std::string& content) {
  Spec spec;
  bool in_block = false;
  bool in_bars = false;
  for (const auto& raw : [&]() {
         std::vector<std::string> out;
         std::istringstream ss(content);
         std::string line;
         while (std::getline(ss, line)) out.push_back(line);
         return out;
       }()) {
    if (raw.find("<BEGIN:SupportElements>") != std::string::npos) {
      in_block = true;
      continue;
    }
    if (raw.find("<END:SupportElements>") != std::string::npos) break;
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
    if (line == "bars") {
      in_bars = true;
      continue;
    }
    if (!in_bars) continue;
    std::istringstream ls(line);
    Bar bar;
    if (ls >> bar.hx >> bar.hy >> bar.tx >> bar.ty >> bar.ea >> bar.prestress >> bar.install_stage) {
      int excl = 1;
      ls >> excl;
      bar.exclude_from_srf = excl != 0;
      spec.bars.push_back(bar);
    }
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

inline void fill_truss_km(double ea, double dx, double dy, std::vector<std::vector<double>>& km) {
  const double L = std::sqrt(dx * dx + dy * dy);
  for (int i = 0; i < 4; ++i)
    for (int j = 0; j < 4; ++j) km[i][j] = 0.0;
  if (!(L > 1e-9) || !(ea > 0.0)) return;
  const double c = dx / L;
  const double s = dy / L;
  const double k = ea / L;
  const double kcc = k * c * c, kss = k * s * s, kcs = k * c * s;
  km[0][0] = kcc;  km[0][1] = kcs;  km[0][2] = -kcc; km[0][3] = -kcs;
  km[1][0] = kcs;  km[1][1] = kss;  km[1][2] = -kcs; km[1][3] = -kss;
  km[2][0] = -kcc; km[2][1] = -kcs; km[2][2] = kcc;  km[2][3] = kcs;
  km[3][0] = -kcs; km[3][1] = -kss; km[3][2] = kcs;  km[3][3] = kss;
}

inline int nearest_node(const std::vector<std::vector<double>>& g_coord, double x, double y) {
  int best = 0;
  double best_d2 = 1.0e300;
  for (int i = 0; i < static_cast<int>(g_coord.size()); ++i) {
    if (g_coord[i].size() < 2) continue;
    const double dx = g_coord[i][0] - x;
    const double dy = g_coord[i][1] - y;
    const double d2 = dx * dx + dy * dy;
    if (d2 < best_d2) {
      best_d2 = d2;
      best = i;
    }
  }
  return best;
}

/// Assemble 2-node truss stiffness into the existing sparse kv (Smith fsparv layout).
/// Node IDs passed to num_to_g are 1-based.
template <typename NumToG, typename Fsparv>
inline int assemble_bars(
    const Spec& spec,
    const std::vector<std::vector<double>>& g_coord,
    const std::vector<int>& lnf,
    int nodof,
    int nn,
    std::vector<double>& kv,
    const std::vector<int>& kdiag,
    NumToG num_to_g,
    Fsparv fsparv,
    double snap_tol = 2.0)
{
  if (!spec.active()) return 0;
  int added = 0;
  std::vector<std::vector<double>> km(4, std::vector<double>(4, 0.0));
  std::vector<int> g(4, 0);
  for (const auto& bar : spec.bars) {
    const double dx = bar.tx - bar.hx;
    const double dy = bar.ty - bar.hy;
    const double L = std::sqrt(dx * dx + dy * dy);
    if (!(L > 1e-9) || !(bar.ea > 0.0)) continue;
    const int n0 = nearest_node(g_coord, bar.hx, bar.hy);
    const int n1 = nearest_node(g_coord, bar.tx, bar.ty);
    if (n0 == n1) continue;
    const double d0x = g_coord[n0][0] - bar.hx;
    const double d0y = g_coord[n0][1] - bar.hy;
    const double d1x = g_coord[n1][0] - bar.tx;
    const double d1y = g_coord[n1][1] - bar.ty;
    if (d0x * d0x + d0y * d0y > snap_tol * snap_tol) continue;
    if (d1x * d1x + d1y * d1y > snap_tol * snap_tol) continue;

    const double c = dx / L;
    const double s = dy / L;
    const double k = bar.ea / L;
    const double kcc = k * c * c, kss = k * s * s, kcs = k * c * s;
    for (int i = 0; i < 4; ++i)
      for (int j = 0; j < 4; ++j) km[i][j] = 0.0;
    km[0][0] = kcc;  km[0][1] = kcs;  km[0][2] = -kcc; km[0][3] = -kcs;
    km[1][0] = kcs;  km[1][1] = kss;  km[1][2] = -kcs; km[1][3] = -kss;
    km[2][0] = -kcc; km[2][1] = -kcs; km[2][2] = kcc;  km[2][3] = kcs;
    km[3][0] = -kcs; km[3][1] = -kss; km[3][2] = kcs;  km[3][3] = kss;

    std::vector<int> num = {n0 + 1, n1 + 1};
    num_to_g(num, lnf, nodof, nn, g);
    fsparv(kv, km, g, kdiag);
    ++added;
  }
  return added;
}

} // namespace support_elements
