#ifndef MAIN_INVAR_HPP
#define MAIN_INVAR_HPP

#include <vector>

namespace fem {

/// Forms stress invariants in 2D (nst=4) or 3D (nst=6).
/// stress is sigma_xx, sigma_yy, tau_xy [, sigma_zz] or full 6 components.
/// Optional outputs: sigm (mean stress), dsbar (deviatoric invariant), theta (Lode angle).
void invar(const std::vector<double>& stress,
           double* sigm, double* dsbar, double* theta);

} // namespace fem

#endif
