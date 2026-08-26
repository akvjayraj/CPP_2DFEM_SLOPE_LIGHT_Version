#ifndef MAIN_BEAMDIS_HPP
#define MAIN_BEAMDIS_HPP

#include <vector>
#include <string>

namespace fem {

void beamdis(const std::vector<double>& loads, const std::vector<std::vector<int>>& nf,
             double ratmax, int interp, int nels, const std::vector<double>& ell,
             const std::string& argv, int nlen, int ips);

} // namespace fem

#endif
