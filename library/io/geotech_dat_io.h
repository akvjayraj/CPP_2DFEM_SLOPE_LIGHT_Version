#ifndef GEOTECH_DAT_IO_HPP
#define GEOTECH_DAT_IO_HPP

#include <sstream>
#include <string>
#include <vector>

namespace geotech_io {

struct AnalysisOptions {
  std::string mesh_source;   // "parametric" | "explicit" | empty → auto
  std::string srf_mode;      // "multi" | "single" | empty → multi
  double srf_F = 0.0;
  double k_h = 0.0;
  double k_v = 0.0;
  bool has_block = false;
  bool has_pseudo_static = false;
};

/// Fills k_h/k_v from AnalysisOptions block and legacy SLOPE64-style quoted lines.
bool parse_pseudo_static_coefficients(const std::string& path, double& k_h, double& k_v);

bool load_dat_stripped(const std::string& path, std::stringstream& out);

std::string read_analysis_option(const std::string& path, const std::string& key);

AnalysisOptions parse_analysis_options(const std::string& path);

/// First program token: <solver:tag>, first non-comment token, or solver_executable option.
std::string read_program_token(const std::string& path);

/// After stripping, true if body starts with five floats (embankment geometry line).
bool body_looks_parametric(const std::stringstream& body);

/// Legacy Fortran p64.dat: no program id, first line is w1 s1 w2 h1 h2.
bool detect_p64_parametric_deck(const std::string& path);

/// Generalized p64 Q8 deck: explicit mesh header + AnalysisOptions with mesh_type Q8 / solver_executable p64.
bool detect_p64_explicit_deck(const std::string& path);

} // namespace geotech_io

#endif
