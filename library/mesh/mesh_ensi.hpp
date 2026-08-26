#ifndef MAIN_MESH_ENSI_HPP
#define MAIN_MESH_ENSI_HPP

#include <vector>
#include <string>

namespace fem {

void mesh_ensi(const std::string& argv, int nlen, const std::vector<std::vector<double>>& g_coord,
               const std::vector<std::vector<int>>& g_num, const std::string& element,
               const std::vector<int>& etype, const std::vector<std::vector<int>>& nf,
               const std::vector<double>& loads, int nstep, int npri, double dtim, bool solid);

} // namespace fem

#endif
