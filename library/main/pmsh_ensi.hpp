#ifndef MAIN_PMSH_ENSI_HPP
#define MAIN_PMSH_ENSI_HPP

#include <vector>
#include <string>

namespace fem {

void pmsh_ensi(const std::string& argv, int nlen, const std::vector<std::vector<int>>& g_num,
               const std::vector<double>& loads, int nstep, int npri, double dtim);

} // namespace fem

#endif
