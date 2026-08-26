#ifndef MAIN_ROD_KM_HPP
#define MAIN_ROD_KM_HPP

namespace fem {

/// Forms the 2x2 stiffness matrix of a 1D rod element.
/// km = (EA/L) * [1 -1; -1 1]
void rod_km(double km[2][2], double ea, double length);

} // namespace fem

#endif
