#ifndef MAIN_BEAM_MM_HPP
#define MAIN_BEAM_MM_HPP

namespace fem {

/// Forms the consistent mass matrix of a beam element, 4x4.
void beam_mm(double mm[4][4], double fs, double ell);

} // namespace fem

#endif
