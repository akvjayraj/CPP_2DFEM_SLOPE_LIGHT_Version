/**
 * Geotechnical construction program runners (excavation, embankment / staged fill, earth pressure).
 * Used by the unified fem_cpp dispatcher (programs p64–p613).
 */
#ifndef PROGRAMS_RUN_PROGRAMS_HPP
#define PROGRAMS_RUN_PROGRAMS_HPP

#include <string>
#include <fstream>

namespace programs {

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

} // namespace programs

#endif
