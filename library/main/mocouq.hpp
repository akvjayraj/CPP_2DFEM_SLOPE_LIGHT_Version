#ifndef MAIN_MOCOUQ_HPP
#define MAIN_MOCOUQ_HPP

namespace fem {

/// Mohr-Coulomb potential derivatives w.r.t. invariants (psi in degrees).
void mocouq(double psi, double dsbar, double theta, double& dq1, double& dq2, double& dq3);

} // namespace fem

#endif
