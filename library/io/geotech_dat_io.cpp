#include "geotech_dat_io.h"

#include <cctype>
#include <cstdlib>
#include <fstream>
#include <sstream>

namespace geotech_io {

namespace {

std::string trim_ws(const std::string& s) {
  size_t b = 0, e = s.size();
  while (b < e && std::isspace(static_cast<unsigned char>(s[b]))) ++b;
  while (e > b && std::isspace(static_cast<unsigned char>(s[e - 1]))) --e;
  return s.substr(b, e - b);
}

bool iequals(const std::string& a, const std::string& b) {
  if (a.size() != b.size()) return false;
  for (size_t i = 0; i < a.size(); ++i)
    if (std::tolower(static_cast<unsigned char>(a[i])) !=
        std::tolower(static_cast<unsigned char>(b[i])))
      return false;
  return true;
}

} // namespace

bool load_dat_stripped(const std::string& path, std::stringstream& out) {
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

std::string read_analysis_option(const std::string& path, const std::string& key) {
  std::ifstream f(path);
  if (!f) return "";
  std::string line;
  bool in_block = false;
  while (std::getline(f, line)) {
    if (line.find("<BEGIN:AnalysisOptions>") != std::string::npos) {
      in_block = true;
      continue;
    }
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

AnalysisOptions parse_analysis_options(const std::string& path) {
  AnalysisOptions opt;
  std::ifstream f(path);
  if (!f) return opt;
  std::string line;
  bool in_block = false;
  while (std::getline(f, line)) {
    if (line.find("<BEGIN:AnalysisOptions>") != std::string::npos) {
      in_block = true;
      opt.has_block = true;
      continue;
    }
    if (line.find("<END:AnalysisOptions>") != std::string::npos) break;
    if (!in_block) continue;
    auto bang = line.find('!');
    if (bang == std::string::npos) continue;
    std::string body = trim_ws(line.substr(bang + 1));
    auto take_token = [&](const char* key, std::string& dest) {
      const std::string k(key);
      if (body.rfind(k, 0) != 0) return;
      dest = trim_ws(body.substr(k.size()));
      auto sp = dest.find_first_of(" \t");
      if (sp != std::string::npos) dest = dest.substr(0, sp);
    };
    take_token("mesh_source", opt.mesh_source);
    take_token("srf_mode", opt.srf_mode);
    std::string srf_s;
    take_token("srf_F", srf_s);
    if (!srf_s.empty()) {
      try {
        opt.srf_F = std::stod(srf_s);
      } catch (...) {
        opt.srf_F = 0.0;
      }
    }
    std::string kh_s, kv_s;
    take_token("k_h", kh_s);
    take_token("k_v", kv_s);
    if (!kh_s.empty()) {
      try {
        opt.k_h = std::stod(kh_s);
        opt.has_pseudo_static = true;
      } catch (...) {
        opt.k_h = 0.0;
      }
    }
    if (!kv_s.empty()) {
      try {
        opt.k_v = std::stod(kv_s);
        opt.has_pseudo_static = true;
      } catch (...) {
        opt.k_v = 0.0;
      }
    }
  }
  return opt;
}

bool parse_pseudo_static_coefficients(const std::string& path, double& k_h, double& k_v) {
  k_h = 0.0;
  k_v = 0.0;
  const AnalysisOptions opt = parse_analysis_options(path);
  if (opt.has_pseudo_static) {
    k_h = opt.k_h;
    k_v = opt.k_v;
    return true;
  }

  // Fallback: scan comment lines anywhere in the deck (! k_h 0.1 / ! k_v 0.05).
  const std::string kh_s = read_analysis_option(path, "k_h");
  const std::string kv_s = read_analysis_option(path, "k_v");
  bool found = false;
  if (!kh_s.empty()) {
    try {
      k_h = std::stod(kh_s);
      found = true;
    } catch (...) {
      k_h = 0.0;
    }
  }
  if (!kv_s.empty()) {
    try {
      k_v = std::stod(kv_s);
      found = true;
    } catch (...) {
      k_v = 0.0;
    }
  }
  if (found)
    return true;

  std::ifstream f(path);
  if (!f) return false;
  std::string line;
  while (std::getline(f, line)) {
    auto bang = line.find('!');
    if (bang == std::string::npos) continue;
    std::string body = trim_ws(line.substr(bang + 1));
    auto sp = body.find_first_of(" \t");
    if (sp == std::string::npos) continue;
    std::string key = body.substr(0, sp);
    std::string val = trim_ws(body.substr(sp + 1));
    auto val_sp = val.find_first_of(" \t");
    if (val_sp != std::string::npos) val = val.substr(0, val_sp);
    try {
      if (iequals(key, "k_h")) {
        k_h = std::stod(val);
        found = true;
      } else if (iequals(key, "k_v")) {
        k_v = std::stod(val);
        found = true;
      }
    } catch (...) {
    }
  }
  return found;
}

std::string read_program_token(const std::string& path) {
  std::ifstream f(path);
  if (!f) return "";
  std::string line;
  while (std::getline(f, line)) {
    auto bang = line.find('!');
    if (bang != std::string::npos) {
      std::string comment = trim_ws(line.substr(bang + 1));
      if (comment.size() > 8 && comment.compare(0, 8, "<solver:") == 0) {
        size_t end = comment.find('>');
        if (end != std::string::npos && end > 8)
          return comment.substr(8, end - 8);
      }
      continue;
    }
    line = trim_ws(line);
    if (line.empty()) continue;
    std::istringstream iss(line);
    std::string tok;
    if (iss >> tok) {
      char* end = nullptr;
      std::strtod(tok.c_str(), &end);
      if (end != tok.c_str() && *end == '\0') continue;
      return tok;
    }
  }
  const std::string exe = read_analysis_option(path, "solver_executable");
  if (!exe.empty()) return exe;
  return "";
}

static bool read_parametric_header(std::istream& in) {
  double a = 0, b = 0, c = 0, d = 0, e = 0;
  if (!(in >> a >> b >> c >> d >> e)) return false;
  int i1 = 0, i2 = 0, i3 = 0, i4 = 0;
  if (!(in >> i1 >> i2 >> i3 >> i4)) return false;
  return i1 > 0 && i2 >= 0 && i3 > 0 && i4 >= 1;
}

bool body_looks_parametric(const std::stringstream& body) {
  std::stringstream copy(body.str());
  if (read_parametric_header(copy)) return true;
  copy.clear();
  copy.str(body.str());
  std::string tok;
  if (copy >> tok) {
    if (iequals(tok, "p64") || iequals(tok, "slope_q8_srf") || iequals(tok, "slope_Q8_SRF"))
      return read_parametric_header(copy);
  }
  return false;
}

bool detect_p64_parametric_deck(const std::string& path) {
  std::stringstream body;
  if (!load_dat_stripped(path, body)) return false;
  const std::string tok = read_program_token(path);
  if (!tok.empty()) {
    if (iequals(tok, "p64") || iequals(tok, "slope_q8_srf") || iequals(tok, "slope_Q8_SRF"))
      return body_looks_parametric(body);
    return false;
  }
  return body_looks_parametric(body);
}

bool detect_p64_explicit_deck(const std::string& path) {
  const std::string exe = read_analysis_option(path, "solver_executable");
  const std::string mesh = read_analysis_option(path, "mesh_type");
  const bool tagged_p64 = iequals(exe, "p64") || iequals(exe, "slope_q8_srf") ||
                        iequals(exe, "slope_Q8_SRF") || iequals(exe, "slope_stability_p64");
  const bool tagged_mesh = iequals(mesh, "Q8") || iequals(mesh, "Q4")
                        || iequals(mesh, "T6") || iequals(mesh, "T3");
  if (!tagged_p64 && !tagged_mesh) return false;

  std::stringstream body;
  if (!load_dat_stripped(path, body)) return false;
  int nels = 0, nn = 0, np_types = 0;
  if (!(body >> nels >> nn >> np_types)) return false;
  return nels > 0 && nn > 0 && np_types >= 1;
}

} // namespace geotech_io
