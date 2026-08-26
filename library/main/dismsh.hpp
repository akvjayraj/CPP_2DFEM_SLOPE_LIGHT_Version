#ifndef MAIN_DISMSH_HPP
#define MAIN_DISMSH_HPP

#include <vector>
#include <string>

namespace fem {

void dismsh(const std::vector<double>& loads, const std::vector<std::vector<int>>& nf,
            double ratmax, const std::vector<std::vector<double>>& g_coord,
            const std::vector<std::vector<int>>& g_num, const std::string& argv, int nlen, int ips);

} // namespace fem

#endif
