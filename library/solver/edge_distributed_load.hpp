#pragma once

#include <cmath>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

namespace edge_distributed_load {

struct EdgeRow {
  int nA = 0;
  int nB = 0;
  int nC = 0;
  double qA = 0.0;
  double qB = 0.0;
};

struct Group {
  int kind = 0;
  double dir_x = 0.0;
  double dir_y = -1.0;
  std::vector<EdgeRow> edges;
};

struct Spec {
  std::vector<Group> groups;
  bool active() const {
    for (const auto& g : groups)
      if (!g.edges.empty()) return true;
    return false;
  }
};

inline std::string strip_comment(const std::string& raw) {
  std::string line = raw;
  auto bang = line.find('!');
  if (bang != std::string::npos) line = line.substr(bang + 1);
  while (!line.empty() && (line[0] == ' ' || line[0] == '\t')) line.erase(line.begin());
  while (!line.empty() && (line.back() == '\r' || line.back() == ' ' || line.back() == '\t'))
    line.pop_back();
  return line;
}

inline Spec parse_block_from_content(const std::string& content) {
  Spec spec;
  bool in_block = false;
  enum class Phase { kGroupsCount, kGroupHeader, kEdgeCount, kEdgeRows } phase = Phase::kGroupsCount;
  Group current{};
  int edges_remaining = 0;

  std::istringstream ss(content);
  std::string raw;
  while (std::getline(ss, raw)) {
    if (raw.find("<BEGIN:DistributedEdgeLoads>") != std::string::npos) {
      in_block = true;
      continue;
    }
    if (raw.find("<END:DistributedEdgeLoads>") != std::string::npos) break;
    if (!in_block) continue;

    std::string line = strip_comment(raw);
    if (line.empty()) continue;

    if (phase == Phase::kGroupsCount) {
      int n_groups = 0;
      std::istringstream ls(line);
      if (ls >> n_groups && n_groups > 0) {
        spec.groups.reserve(static_cast<size_t>(n_groups));
        phase = Phase::kGroupHeader;
      }
      continue;
    }

    if (phase == Phase::kGroupHeader) {
      std::istringstream ls(line);
      if (ls >> current.kind >> current.dir_x >> current.dir_y) {
        phase = Phase::kEdgeCount;
      }
      continue;
    }

    if (phase == Phase::kEdgeCount) {
      std::istringstream ls(line);
      if (ls >> edges_remaining) {
        current.edges.clear();
        spec.groups.push_back(current);
        phase = edges_remaining > 0 ? Phase::kEdgeRows : Phase::kGroupHeader;
      }
      continue;
    }

    EdgeRow row{};
    std::istringstream ls(line);
    std::vector<double> nums;
    double v = 0.0;
    while (ls >> v) nums.push_back(v);
    if (nums.size() == 4) {
      row.nA = static_cast<int>(nums[0]);
      row.nB = static_cast<int>(nums[1]);
      row.qA = nums[2];
      row.qB = nums[3];
    } else if (nums.size() >= 5) {
      row.nA = static_cast<int>(nums[0]);
      row.nB = static_cast<int>(nums[1]);
      row.nC = static_cast<int>(nums[2]);
      row.qA = nums[3];
      row.qB = nums[4];
    } else {
      continue;
    }

    spec.groups.back().edges.push_back(row);
    --edges_remaining;
    if (edges_remaining <= 0) phase = Phase::kGroupHeader;
  }
  return spec;
}

inline Spec parse_from_dat_path(const std::string& path) {
  std::ifstream f(path);
  if (!f) return Spec{};
  std::ostringstream content;
  content << f.rdbuf();
  return parse_block_from_content(content.str());
}

inline void assemble_edge_2node(
    int nA, int nB, double qA, double qB, int kind, double dir_x, double dir_y,
    const std::vector<std::vector<double>>& g_coord, const std::vector<int>& nf,
    int nodof, std::vector<double>& loads) {
  const int ia = nA - 1;
  const int ib = nB - 1;
  if (ia < 0 || ib < 0 || ia >= static_cast<int>(g_coord.size()) ||
      ib >= static_cast<int>(g_coord.size()))
    return;

  const double xa = g_coord[static_cast<size_t>(ia)][0];
  const double ya = g_coord[static_cast<size_t>(ia)][1];
  const double xb = g_coord[static_cast<size_t>(ib)][0];
  const double yb = g_coord[static_cast<size_t>(ib)][1];
  const double dx = xb - xa;
  const double dy = yb - ya;
  const double L = std::sqrt(dx * dx + dy * dy);
  if (L < 1e-20) return;

  double tx = 0.0;
  double ty = 0.0;
  if (kind == 1) {
    tx = dy / L;
    ty = -dx / L;
  } else {
    const double len = std::sqrt(dir_x * dir_x + dir_y * dir_y);
    if (len < 1e-20) return;
    tx = dir_x / len;
    ty = dir_y / len;
  }

  const double fA = L / 6.0 * (2.0 * qA + qB);
  const double fB = L / 6.0 * (qA + 2.0 * qB);

  const int eqAx = nf[0 + ia * nodof];
  const int eqAy = nf[1 + ia * nodof];
  const int eqBx = nf[0 + ib * nodof];
  const int eqBy = nf[1 + ib * nodof];

  if (eqAx > 0 && eqAx < static_cast<int>(loads.size())) loads[static_cast<size_t>(eqAx)] += fA * tx;
  if (eqAy > 0 && eqAy < static_cast<int>(loads.size())) loads[static_cast<size_t>(eqAy)] += fA * ty;
  if (eqBx > 0 && eqBx < static_cast<int>(loads.size())) loads[static_cast<size_t>(eqBx)] += fB * tx;
  if (eqBy > 0 && eqBy < static_cast<int>(loads.size())) loads[static_cast<size_t>(eqBy)] += fB * ty;
}

inline void assemble(const Spec& spec, int nn, int nodof,
                     const std::vector<std::vector<double>>& g_coord,
                     const std::vector<int>& nf, std::vector<double>& loads) {
  (void)nn;
  if (!spec.active()) return;
  if (loads.empty()) return;

  for (const auto& group : spec.groups) {
    for (const auto& edge : group.edges) {
      if (edge.nC > 0) {
        assemble_edge_2node(edge.nA, edge.nC, edge.qA, 0.5 * (edge.qA + edge.qB), group.kind,
                            group.dir_x, group.dir_y, g_coord, nf, nodof, loads);
        assemble_edge_2node(edge.nC, edge.nB, 0.5 * (edge.qA + edge.qB), edge.qB, group.kind,
                            group.dir_x, group.dir_y, g_coord, nf, nodof, loads);
      } else {
        assemble_edge_2node(edge.nA, edge.nB, edge.qA, edge.qB, group.kind, group.dir_x,
                            group.dir_y, g_coord, nf, nodof, loads);
      }
    }
  }
}

} // namespace edge_distributed_load
