/**
 * Axisymmetric continuum SRF driver for 3-node triangular (T3) meshes (S&G ×r, εθ=u/r).
 *
 * Same .dat format as plane `slope stability_T3_SRF.cpp`; compile with SLOPE_SRF_AXISYM=1.
 */
#include "p610_t3_entry.hpp"

int main(int argc, char* argv[]) {
  return p610_t3_run_entry(argc, argv, "slope_stability_T3_SRF_axisym");
}
