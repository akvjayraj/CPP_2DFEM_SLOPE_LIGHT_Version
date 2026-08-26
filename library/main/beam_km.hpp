#ifndef MAIN_BEAM_KM_HPP
#define MAIN_BEAM_KM_HPP

namespace fem {

/// Forms the stiffness matrix of a beam element (bending only), 4x4.
void beam_km(double km[4][4], double ei, double ell);

} // namespace fem

#endif
