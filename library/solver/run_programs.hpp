/**
 * Geotechnical construction program runners (excavation, embankment / staged fill, earth pressure).
 * Used by the unified fem_cpp dispatcher (programs p64–p613).
 */
#ifndef PROGRAMS_RUN_PROGRAMS_HPP
#define PROGRAMS_RUN_PROGRAMS_HPP

#include <string>
#include <fstream>

namespace programs {

/// Q8 slope SRF driver (Fortran `slope_Q8_SRF.f03` / Program 6.4).
int run_slope_q8_srf(std::ifstream& dat, std::ofstream& res, const std::string& dataname);
int run_p64(std::ifstream& dat, std::ofstream& res, const std::string& dataname);
int run_p65(std::ifstream& dat, std::ofstream& res, const std::string& dataname);
int run_p66(std::ifstream& dat, std::ofstream& res, const std::string& dataname);
int run_p67(std::ifstream& dat, std::ofstream& res, const std::string& dataname);
int run_p68(std::ifstream& dat, std::ofstream& res, const std::string& dataname);
int run_p69(std::ifstream& dat, std::ofstream& res, const std::string& dataname);
int run_p610(std::ifstream& dat, std::ofstream& res, const std::string& dataname);
int run_p611(std::ifstream& dat, std::ofstream& res, const std::string& dataname);
int run_p612(std::ifstream& dat, std::ofstream& res, const std::string& dataname);
int run_p613(std::ifstream& dat, std::ofstream& res, const std::string& dataname);

/// Chapter 7 steady seepage (seepage_programs.cpp): p72 plane/axisymmetric Q4,
/// p73 free-surface iteration, p74 general mesh.
int run_p72(std::ifstream& dat, std::ofstream& res, const std::string& dataname);
int run_p73(std::ifstream& dat, std::ofstream& res, const std::string& dataname);
int run_p74(std::ifstream& dat, std::ofstream& res, const std::string& dataname);

/// Chapter 8 transient consolidation (transient_programs.cpp): p84 structured Q4,
/// p88 general mesh.
int run_p84(std::ifstream& dat, std::ofstream& res, const std::string& dataname);
int run_p88(std::ifstream& dat, std::ofstream& res, const std::string& dataname);

} // namespace programs

#endif
