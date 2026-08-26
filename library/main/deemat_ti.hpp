#ifndef MAIN_DEEMAT_TI_HPP
#define MAIN_DEEMAT_TI_HPP

#include <vector>

namespace fem {

/// Plane strain (4x4) elastic matrix for transversely isotropic material.
/// Isotropy plane = x-y; symmetry axis = z.
/// E1 = Young's modulus in plane (x,y), E2 = Young's modulus along z,
/// nu12 = Poisson in plane, nu23 = Poisson for strain in z due to stress in plane,
/// G12 = shear modulus in plane.
void deemat_ti(std::vector<std::vector<double>>& dee,
               double E1, double E2, double nu12, double nu23, double G12);

} // namespace fem

#endif
