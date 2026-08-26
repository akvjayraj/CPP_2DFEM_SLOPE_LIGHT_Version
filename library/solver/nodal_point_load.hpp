#pragma once

#include <cmath>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

namespace nodal_point_load {

struct Row {
  int node = 0;
  double fx = 0.0;
  double fy = 0.0;
};

struct Spec {
  std::vector<Row> rows;
  bool active() const { return !rows.empty(); }
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
  bool expect_count = true;
  int rows_remaining = 0;

  std::istringstream ss(content);
  std::string raw;
  while (std::getline(ss, raw)) {
    if (raw.find("<BEGIN:NodalPointLoads>") != std::string::npos) {
      in_block = true;
      continue;
    }
    if (raw.find("<END:NodalPointLoads>") != std::string::npos) break;
    if (!in_block) continue;

    std::string line = strip_comment(raw);
    if (line.empty()) continue;

    if (expect_count) {
      std::istringstream ls(line);
      if (ls >> rows_remaining) expect_count = false;
      continue;
    }

    Row row{};
    std::istringstream ls(line);
    if (ls >> row.node >> row.fx >> row.fy) {
      spec.rows.push_back(row);
      --rows_remaining;
      if (rows_remaining <= 0) expect_count = true;
    }
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

inline void assemble(const Spec& spec, const std::vector<int>& nf, int nodof,
                     std::vector<double>& loads) {
  if (!spec.active() || loads.empty()) return;

  for (const auto& row : spec.rows) {
    const int idx = row.node - 1;
    if (idx < 0) continue;
    const int eqx = nf[0 + idx * nodof];
    const int eqy = nf[1 + idx * nodof];
    if (eqx > 0 && eqx < static_cast<int>(loads.size())) loads[static_cast<size_t>(eqx)] += row.fx;
    if (eqy > 0 && eqy < static_cast<int>(loads.size())) loads[static_cast<size_t>(eqy)] += row.fy;
  }
}

} // namespace nodal_point_load
