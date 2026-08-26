#ifndef MAIN_CONTOUR_HPP
#define MAIN_CONTOUR_HPP

#include <vector>
#include <string>

namespace fem {

void contour(const std::vector<double>& loads, const std::vector<std::vector<double>>& g_coord,
             const std::vector<std::vector<int>>& g_num, int ned, const std::string& argv, int nlen, int ips);

} // namespace fem

#endif
