#ifndef MAIN_MESH_HPP
#define MAIN_MESH_HPP

#include <vector>
#include <string>

namespace fem {

/// Writes PostScript .msh file for undeformed mesh. g_coord(2,nn), g_num(nod,nel).
void mesh(const std::vector<std::vector<double>>& g_coord, const std::vector<std::vector<int>>& g_num,
          const std::string& argv, int nlen, int ips);

} // namespace fem

#endif
