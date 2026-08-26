#ifndef GEOM_FMGLEM_HPP
#define GEOM_FMGLEM_HPP

#include <vector>

namespace geom {

/// Returns g_num for the footing mesh.
void fmglem(int fnxe, int fnye, int lnxe, int lifts,
            std::vector<std::vector<int>>& g_num);

} // namespace geom

#endif
