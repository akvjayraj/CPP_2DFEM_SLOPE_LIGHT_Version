#ifndef GEOM_FMCOEM_HPP
#define GEOM_FMCOEM_HPP

#include <vector>

namespace geom {

/// Returns g_coord for the mesh (footing mesh coordinates).
void fmcoem(const std::vector<std::vector<int>>& g_num,
            std::vector<std::vector<double>>& g_coord,
            const std::vector<double>& fwidth, const std::vector<double>& fdepth,
            const std::vector<double>& width, const std::vector<double>& depth,
            int lnxe, int lifts, int fnxe, int fnye, int itype);

} // namespace geom

#endif
