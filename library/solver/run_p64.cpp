/**
 * Program 6.4 — Plane strain slope stability (Mohr–Coulomb, 8-node quads, SRF trials).
 * Implementation lives in `slope_Q8_SRF.cpp` (`run_slope_q8_srf`).
 */
#include "run_programs.hpp"

namespace programs {

int run_p64(std::ifstream& dat, std::ofstream& res, const std::string& dataname) {
  return run_slope_q8_srf(dat, res, dataname);
}

} // namespace programs
