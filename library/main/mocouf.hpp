#ifndef MAIN_MOCOUF_HPP
#define MAIN_MOCOUF_HPP

namespace fem {

/// Mohr-Coulomb yield function value (phi in degrees).
double mocouf(double phi, double c, double sigm, double dsbar, double theta);

} // namespace fem

#endif
