#ifndef MAIN_INTERP_HPP
#define MAIN_INTERP_HPP

#include <vector>

namespace fem {

/// Forms load/time interpolation: al(j,k) = interpolated load at time step j for load curve k.
/// rt, rl define (time, load) points; al(1,k)=rl(0); then for j=2..nstep, t = rt[0] + (j-1)*dtim, linear interp.
void interp(int k, double dtim, const std::vector<double>& rt,
            const std::vector<double>& rl, std::vector<std::vector<double>>& al, int nstep);

} // namespace fem

#endif
