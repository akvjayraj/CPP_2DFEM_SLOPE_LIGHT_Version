#pragma once

#include <algorithm>
#include <cmath>
#include <cctype>
#include <fstream>
#include <limits>
#include <sstream>
#include <string>
#include <tuple>
#include <utility>
#include <vector>

namespace groundwater {

enum class Method { Hydrostatic, Ru, Grid, Bbar };

enum class GridValueType { PorePressure, PressureHead, TotalHead };

struct Spec {
  Method method = Method::Hydrostatic;
  int nosurf = 0;
  double gam_w = 9.81;
  double Ru = 0.0;
  /// Skempton B̄: u = B̄·σv (same stress-ratio form as Ru).
  double Bbar = 0.0;
  std::vector<std::pair<double, double>> surf; // axis Y-up: (x, y_wt)
  /// Optional per-material saturated unit weights (0-based etype index). Empty = use dry γ only.
  std::vector<double> gamma_sat;
  GridValueType grid_type = GridValueType::PorePressure;
  int ngrid = 0;
  /// Scatter points (x, y, value) for water-pressure grid input.
  std::vector<std::tuple<double, double, double>> grid;
  bool static_water_surfaces = true;
  bool static_ru = true;
  bool static_bbar = true;
  bool static_grid_total_head = true;
  bool static_grid_pressure_head = true;
  bool static_grid_pore_pressure = true;
  /// When true, Ru uses u = Ru·γ·depth (axis Y-up) instead of Ru·σv.
  /// Used during gravity turn-on so α·u can ramp once like hydrostatic
  /// (σv-based Ru already scales with the load fraction and would double-count).
  bool ru_use_overburden = false;
  double ru_overburden_gamma = 0.0;
  double ru_overburden_y_surface = 0.0;

  bool has_grid() const {
    if (grid.empty() || ngrid <= 0) return false;
    switch (grid_type) {
      case GridValueType::TotalHead: return static_grid_total_head;
      case GridValueType::PressureHead: return static_grid_pressure_head;
      case GridValueType::PorePressure: return static_grid_pore_pressure;
    }
    return false;
  }

  bool is_ratio_method() const {
    return method == Method::Ru || method == Method::Bbar;
  }

  double ratio_coeff() const {
    return method == Method::Bbar ? Bbar : Ru;
  }

  bool ratio_static_enabled() const {
    return method == Method::Bbar ? static_bbar : static_ru;
  }

  bool active() const {
    if (is_ratio_method() && ratio_coeff() > 0.0 && ratio_static_enabled()) return true;
    if (has_grid()) return true;
    if (method == Method::Grid && has_grid()) return true;
    if (static_water_surfaces && nosurf > 0 && !surf.empty()) return true;
    return false;
  }
};

inline bool parse_option_line(const std::string& line, const std::string& key, std::string& val) {
  const std::string t = [&]() {
    std::string s = line;
    auto b = s.find('!');
    if (b != std::string::npos) s = s.substr(0, b);
    size_t a = 0;
    while (a < s.size() && (s[a] == ' ' || s[a] == '\t')) ++a;
    size_t z = s.size();
    while (z > a && (s[z - 1] == ' ' || s[z - 1] == '\t' || s[z - 1] == '\r')) --z;
    return s.substr(a, z - a);
  }();
  const std::string prefix = key + " ";
  if (t.size() <= prefix.size()) return false;
  for (size_t i = 0; i < prefix.size(); ++i)
    if (std::tolower(static_cast<unsigned char>(t[i])) !=
        std::tolower(static_cast<unsigned char>(prefix[i])))
      return false;
  val = t.substr(prefix.size());
  while (!val.empty() && (val.back() == ' ' || val.back() == '\t' || val.back() == '\r')) val.pop_back();
  return true;
}

inline Method parse_method_token(const std::string& val) {
  std::string lower = val;
  for (char& c : lower)
    c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
  if (lower == "ru") return Method::Ru;
  if (lower == "bbar" || lower == "b-bar" || lower == "b_bar") return Method::Bbar;
  if (lower == "grid" || lower == "pore_pressure_grid" || lower == "water_pressure_grid")
    return Method::Grid;
  return Method::Hydrostatic;
}

inline GridValueType parse_grid_type_token(const std::string& val) {
  std::string lower = val;
  for (char& c : lower)
    c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
  if (lower == "total_head" || lower == "totalhead") return GridValueType::TotalHead;
  if (lower == "pressure_head" || lower == "pressurehead") return GridValueType::PressureHead;
  return GridValueType::PorePressure;
}

inline bool parse_bool_token(const std::string& val) {
  std::string lower = val;
  for (char& c : lower)
    c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
  return !(lower == "false" || lower == "0" || lower == "no");
}

inline Spec parse_block_from_content(const std::string& content) {
  Spec spec;
  bool in_block = false;
  bool in_surf = false;
  bool in_grid = false;
  bool include = true;
  for (const auto& raw : [&]() {
         std::vector<std::string> out;
         std::istringstream ss(content);
         std::string line;
         while (std::getline(ss, line)) out.push_back(line);
         return out;
       }()) {
    if (raw.find("<BEGIN:Groundwater>") != std::string::npos) {
      in_block = true;
      continue;
    }
    if (raw.find("<END:Groundwater>") != std::string::npos) break;
    if (!in_block) continue;
    std::string line = raw;
    auto b = line.find('!');
    if (b != std::string::npos) line = line.substr(b + 1);
    while (!line.empty() && (line[0] == ' ' || line[0] == '\t')) line.erase(line.begin());
    std::string val;
    if (parse_option_line(line, "include", val)) {
      std::string lower = val;
      for (char& c : lower)
        c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
      include = !(lower == "false" || lower == "0" || lower == "no");
    }
    if (parse_option_line(line, "method", val))
      spec.method = parse_method_token(val);
    if (parse_option_line(line, "Ru", val) || parse_option_line(line, "ru", val)) {
      try { spec.Ru = std::stod(val); } catch (...) {}
    }
    if (parse_option_line(line, "Bbar", val) || parse_option_line(line, "bbar", val)
        || parse_option_line(line, "B-bar", val)) {
      try { spec.Bbar = std::stod(val); } catch (...) {}
    }
    if (parse_option_line(line, "gamma_w", val))
      spec.gam_w = std::stod(val);
    if (parse_option_line(line, "gamma_sat", val)) {
      spec.gamma_sat.clear();
      std::istringstream ls(val);
      double g = 0.0;
      while (ls >> g) spec.gamma_sat.push_back(g);
    }
    if (parse_option_line(line, "static_water_surfaces", val))
      spec.static_water_surfaces = parse_bool_token(val);
    if (parse_option_line(line, "static_ru", val))
      spec.static_ru = parse_bool_token(val);
    if (parse_option_line(line, "static_bbar", val))
      spec.static_bbar = parse_bool_token(val);
    if (parse_option_line(line, "static_grid_total_head", val))
      spec.static_grid_total_head = parse_bool_token(val);
    if (parse_option_line(line, "static_grid_pressure_head", val))
      spec.static_grid_pressure_head = parse_bool_token(val);
    if (parse_option_line(line, "static_grid_pore_pressure", val))
      spec.static_grid_pore_pressure = parse_bool_token(val);
    if (parse_option_line(line, "nosurf", val))
      spec.nosurf = std::stoi(val);
    if (parse_option_line(line, "grid_type", val))
      spec.grid_type = parse_grid_type_token(val);
    if (parse_option_line(line, "ngrid", val)) {
      try { spec.ngrid = std::stoi(val); } catch (...) { spec.ngrid = 0; }
    }
    if (line == "surf") {
      in_surf = true;
      in_grid = false;
      continue;
    }
    if (line == "grid") {
      in_grid = true;
      in_surf = false;
      continue;
    }
    if (in_surf && spec.surf.size() < static_cast<size_t>(spec.nosurf)) {
      std::istringstream ls(line);
      double x = 0, y = 0;
      if (ls >> x >> y) spec.surf.emplace_back(x, y);
    }
    if (in_grid && spec.grid.size() < static_cast<size_t>(std::max(spec.ngrid, 0))) {
      std::istringstream ls(line);
      double x = 0, y = 0, v = 0;
      if (ls >> x >> y >> v) spec.grid.emplace_back(x, y, v);
    }
  }
  if (spec.nosurf == 0) spec.nosurf = static_cast<int>(spec.surf.size());
  if (spec.ngrid == 0) spec.ngrid = static_cast<int>(spec.grid.size());
  if (!include) {
    spec = {};
    return spec;
  }
  return spec;
}

/// SLOPE64 / p64 explicit numeric tail: ... tol limit nsrf F... nosurf x y ... gam_w
inline Spec parse_numeric_tail_from_content(const std::string& content) {
  Spec spec;
  std::vector<std::string> tokens;
  std::istringstream lines(content);
  std::string line;
  while (std::getline(lines, line)) {
    const auto bang = line.find('!');
    if (bang != std::string::npos) line = line.substr(0, bang);
    std::istringstream ls(line);
    std::string tok;
    while (ls >> tok) tokens.push_back(tok);
  }
  if (tokens.size() < 3) return spec;

  int nosurf = 0;
  try {
    nosurf = std::stoi(tokens[tokens.size() - 2]);
    spec.gam_w = std::stod(tokens[tokens.size() - 1]);
  } catch (...) {
    return {};
  }
  if (nosurf <= 0) return spec;

  const size_t need = static_cast<size_t>(nosurf) * 2 + 2;
  if (tokens.size() < need) return spec;
  const size_t start = tokens.size() - need;
  size_t p = start + 1;
  spec.surf.clear();
  try {
    for (int i = 0; i < nosurf; ++i) {
      const double x = std::stod(tokens[p++]);
      const double y = std::stod(tokens[p++]);
      spec.surf.emplace_back(x, y);
    }
  } catch (...) {
    return {};
  }
  spec.nosurf = nosurf;
  return spec;
}

inline Spec parse_from_dat_path(const std::string& dat_path) {
  std::ifstream in(dat_path);
  if (!in) return {};
  std::ostringstream oss;
  oss << in.rdbuf();
  const std::string content = oss.str();
  Spec spec = parse_block_from_content(content);
  // An explicit block is authoritative, including `include false`. Falling
  // through to the legacy numeric-tail parser can reinterpret unrelated final
  // solver tokens as a one-point groundwater surface.
  if (content.find("<BEGIN:Groundwater>") != std::string::npos) return spec;
  return parse_numeric_tail_from_content(content);
}

inline bool try_read_numeric_tail(std::istream& dat, Spec& spec) {
  std::streampos mark = dat.tellg();
  int nosurf = 0;
  if (!(dat >> nosurf)) {
    dat.clear();
    dat.seekg(mark);
    return false;
  }
  spec.nosurf = nosurf;
  spec.surf.clear();
  for (int i = 0; i < nosurf; ++i) {
    double x = 0, y = 0;
    if (!(dat >> x >> y)) {
      dat.clear();
      dat.seekg(mark);
      spec = {};
      return false;
    }
    spec.surf.emplace_back(x, y);
  }
  if (!(dat >> spec.gam_w)) {
    dat.clear();
    dat.seekg(mark);
    spec = {};
    return false;
  }
  return true;
}

/// Smith FEM: compression negative; vertical total compression magnitude.
inline double vertical_total_stress_from_smith_stress(const double sigma[]) {
  return std::max(0.0, -sigma[1]);
}

inline double pore_pressure_ru(const double sigma[], double Ru) {
  return Ru * vertical_total_stress_from_smith_stress(sigma);
}

/// Phreatic elevation at x (axis Y-up), linear between surf points sorted by x.
inline double water_table_y_at_x(double x, const Spec& spec) {
  if (spec.nosurf <= 0 || spec.surf.empty()) return 1.0e30;
  const auto& s = spec.surf;
  if (s.size() == 1) return s[0].second;
  if (x <= s.front().first) return s.front().second;
  if (x >= s.back().first) return s.back().second;
  for (size_t i = 1; i < s.size(); ++i) {
    const double x0 = s[i - 1].first, y0 = s[i - 1].second;
    const double x1 = s[i].first, y1 = s[i].second;
    if (x >= x0 && x <= x1) {
      if (std::abs(x1 - x0) < 1.0e-14) return 0.5 * (y0 + y1);
      const double t = (x - x0) / (x1 - x0);
      return y0 + t * (y1 - y0);
    }
  }
  return s.back().second;
}

/// Depth below WT (axis Y-up): h = y_wt - y when y < y_wt.
inline double depth_below_wt(double x, double y, const Spec& spec) {
  if (spec.nosurf <= 0 || spec.surf.empty()) return 0.0;
  const double ywt = water_table_y_at_x(x, spec);
  if (y >= ywt) return 0.0;
  return ywt - y;
}

/// Inverse-distance-weighted sample of the water-pressure grid (returns raw grid value).
inline double sample_grid_value(double x, double y, const Spec& spec) {
  if (spec.grid.empty()) return 0.0;
  double wsum = 0.0;
  double vsum = 0.0;
  for (const auto& pt : spec.grid) {
    const double px = std::get<0>(pt);
    const double py = std::get<1>(pt);
    const double pv = std::get<2>(pt);
    const double dx = x - px;
    const double dy = y - py;
    const double r2 = dx * dx + dy * dy;
    if (r2 < 1.0e-24) return pv;
    const double w = 1.0 / r2;
    wsum += w;
    vsum += w * pv;
  }
  if (wsum <= 0.0) return 0.0;
  return vsum / wsum;
}

inline double pore_pressure_from_grid(double x, double y, const Spec& spec) {
  const double v = sample_grid_value(x, y, spec);
  switch (spec.grid_type) {
    case GridValueType::PorePressure:
      return std::max(0.0, v);
    case GridValueType::PressureHead:
      return std::max(0.0, spec.gam_w * v);
    case GridValueType::TotalHead:
      return std::max(0.0, spec.gam_w * (v - y));
  }
  return 0.0;
}

inline double pore_pressure_at(double x, double y, const Spec& spec,
                               const double sigma[] = nullptr,
                               double gamma = 0.0, double y_surface = 0.0) {
  if (!spec.active()) return 0.0;
  if (spec.is_ratio_method()) {
    const double coeff = spec.ratio_coeff();
    if (spec.ru_use_overburden && spec.ru_overburden_gamma > 0.0) {
      const double depth = std::max(0.0, spec.ru_overburden_y_surface - y);
      return coeff * spec.ru_overburden_gamma * depth;
    }
    if (sigma != nullptr)
      return pore_pressure_ru(sigma, coeff);
    if (gamma > 0.0) {
      const double depth = std::max(0.0, y_surface - y);
      const double sv = gamma * depth;
      return coeff * sv;
    }
    return 0.0;
  }
  // Grid overrides hydrostatic when present (RS2 static water-pressure grid).
  if (spec.has_grid())
    return pore_pressure_from_grid(x, y, spec);
  const double h = depth_below_wt(x, y, spec);
  return spec.gam_w * h;
}

inline double pressure_head_at(double x, double y, const Spec& spec,
                               const double sigma[] = nullptr,
                               double gamma = 0.0, double y_surface = 0.0) {
  if (spec.gam_w <= 0.0) return 0.0;
  return pore_pressure_at(x, y, spec, sigma, gamma, y_surface) / spec.gam_w;
}

inline double total_head_at(double x, double y, const Spec& spec,
                            const double sigma[] = nullptr,
                            double gamma = 0.0, double y_surface = 0.0) {
  return y + pressure_head_at(x, y, spec, sigma, gamma, y_surface);
}

/// Buoyant γ′ = γ − γw below WT. Kept for reference; body forces should use
/// body_unit_weight (γsat below WT) when pore pressure is applied separately (RS2-style).
inline double effective_unit_weight(double gamma, double x, double y, const Spec& spec) {
  if (!spec.active()) return gamma;
  if (spec.is_ratio_method()) return gamma;
  const double h = depth_below_wt(x, y, spec);
  if (h <= 0.0) return gamma;
  return std::max(0.0, gamma - spec.gam_w);
}

/// Total-stress body-force unit weight: γsat below the phreatic surface when provided,
/// otherwise dry/bulk γ. Does not subtract γw (pore pressure is applied via Terzaghi).
inline double body_unit_weight(double gamma, int etype0, double x, double y, const Spec& spec) {
  if (!spec.active() || spec.is_ratio_method())
    return gamma;
  if (etype0 < 0 || static_cast<size_t>(etype0) >= spec.gamma_sat.size())
    return gamma;
  const double gsat = spec.gamma_sat[static_cast<size_t>(etype0)];
  if (!(gsat > 0.0))
    return gamma;
  // Prefer WT depth when a phreatic surface exists; if only a grid is active, treat
  // positive pore pressure as "saturated" for body-force selection.
  if (spec.nosurf > 0 && !spec.surf.empty()) {
    if (depth_below_wt(x, y, spec) > 0.0) return gsat;
    return gamma;
  }
  if (spec.has_grid() && pore_pressure_from_grid(x, y, spec) > 0.0)
    return gsat;
  return gamma;
}

/// Positive vertical overburden magnitude from free surface down to (x, y) (Y-up).
/// Uses dry γ above the WT and γsat below when gamma_sat is provided (all element types).
inline double vertical_overburden_magnitude(
    double gamma, int etype0, double x, double y, double y_surface, const Spec& spec)
{
  const double depth = std::max(0.0, y_surface - y);
  if (depth <= 0.0) return 0.0;

  double g_sat = gamma;
  if (etype0 >= 0 && static_cast<size_t>(etype0) < spec.gamma_sat.size()
      && spec.gamma_sat[static_cast<size_t>(etype0)] > 0.0)
    g_sat = spec.gamma_sat[static_cast<size_t>(etype0)];

  if (!spec.active() || spec.is_ratio_method()
      || spec.nosurf <= 0 || spec.surf.empty())
    return gamma * depth;

  const double ywt = water_table_y_at_x(x, spec);
  if (y >= ywt)
    return gamma * depth; // centroid above WT
  if (y_surface <= ywt)
    return g_sat * depth; // WT at/above free surface
  const double h_dry = std::max(0.0, y_surface - ywt);
  const double h_sat = std::max(0.0, ywt - y);
  return gamma * h_dry + g_sat * h_sat;
}

/// Apply Terzaghi effective stress on normal components (Smith FEM: compression negative).
/// Pore pressure u > 0 reduces compressive magnitude: σ' = σ + α·u on σxx, σyy, σzz.
inline void apply_effective_stress(double sigma[], double u, double factor = 1.0) {
  const double du = factor * u;
  sigma[0] += du;
  sigma[1] += du;
  sigma[3] += du;
}

inline std::string groundwater_log_line(const Spec& spec) {
  if (!spec.active()) return "";
  if (spec.method == Method::Ru)
    return " Groundwater: method=Ru  Ru=" + std::to_string(spec.Ru) + "\n";
  if (spec.method == Method::Bbar)
    return " Groundwater: method=B-bar  Bbar=" + std::to_string(spec.Bbar) + "\n";
  if (spec.has_grid())
    return " Groundwater: method=grid  ngrid=" + std::to_string(spec.ngrid) +
           "  gam_w=" + std::to_string(spec.gam_w) + " kN/m3\n";
  std::string line = " Groundwater: method=hydrostatic  nosurf=" + std::to_string(spec.nosurf) +
                     "  gam_w=" + std::to_string(spec.gam_w) + " kN/m3";
  if (!spec.gamma_sat.empty())
    line += "  gamma_sat[" + std::to_string(spec.gamma_sat.size()) + "]";
  return line + "\n";
}

} // namespace groundwater
