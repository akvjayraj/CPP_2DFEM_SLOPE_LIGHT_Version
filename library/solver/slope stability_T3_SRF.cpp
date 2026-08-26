/**
 * Plane-strain slope / strength-reduction driver for 3-node triangular (T3) meshes.
 *
 * Same solver core and .dat format as `p610_t3_elastoplastic` (Mohr–Coulomb, SRF via
 * `! srf_F` in AnalysisOptions, `MeshType T3` in the mesh block). Named alongside
 * `slope stability_Q8_SRF.cpp` (Q8 / Program 6.4 path).
 */
#include "p610_t3_entry.hpp"

int main(int argc, char* argv[]) {
  return p610_t3_run_entry(argc, argv, "slope_stability_T3_SRF");
}
