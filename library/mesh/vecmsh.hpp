#ifndef MAIN_VECMSH_HPP
#define MAIN_VECMSH_HPP

#include <vector>
#include <string>

namespace fem {

void vecmsh(const std::vector<double>& loads, const std::vector<std::vector<int>>& nf,
            const std::vector<std::vector<double>>& g_coord, const std::vector<std::vector<int>>& g_num,
            const std::string& argv, int nlen, int ips, double ratmax);

} // namespace fem

#endif
