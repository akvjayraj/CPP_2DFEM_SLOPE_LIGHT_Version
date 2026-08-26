#ifndef MAIN_DISMSH_ENSI_HPP
#define MAIN_DISMSH_ENSI_HPP

#include <vector>
#include <string>

namespace fem {

void dismsh_ensi(const std::string& argv, int nlen, const std::vector<std::vector<double>>& g_coord,
                 const std::vector<std::vector<int>>& g_num, const std::vector<double>& loads,
                 const std::vector<std::vector<int>>& nf, int nstep, int npri, double dtim);

} // namespace fem

#endif
