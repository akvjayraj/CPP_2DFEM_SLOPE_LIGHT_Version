/**
 * Legacy Q4-branded slope SRF source (Core A p610 wrapper).
 *
 * Shipped Q4 SRF executables are built from Core B via CMake-generated
 * slope_coreb_srf_main.cpp + slope stability_Q8_SRF.cpp (see CMakeLists.txt).
 * This file is retained for reference only.
 */
#include "p610_t3_entry.hpp"

int main(int argc, char* argv[]) {
  return p610_t3_run_entry(argc, argv, "slope_stability_Q4_SRF");
}
