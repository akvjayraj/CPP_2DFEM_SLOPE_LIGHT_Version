#ifndef MAIN_LOAD_FUNCTION_HPP
#define MAIN_LOAD_FUNCTION_HPP

#include <vector>

namespace fem {

/// Forms load increment at each time step from load-time table lf(2, nlfp).
/// lf(1,:)=times, lf(2,:)=load values. al(i) = increment at step i.
void load_function(const std::vector<std::vector<double>>& lf, double dtim, std::vector<double>& al);

} // namespace fem

#endif
