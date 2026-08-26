#ifndef MAIN_ROD_MM_HPP
#define MAIN_ROD_MM_HPP

namespace fem {

/// Forms the consistent "mass" (capacitance) matrix for a 2-node rod element.
/// Used in 1D consolidation/diffusion: mm = (ell/6) * [2 1; 1 2]
void rod_mm(double mm[2][2], double ell);

} // namespace fem

#endif
