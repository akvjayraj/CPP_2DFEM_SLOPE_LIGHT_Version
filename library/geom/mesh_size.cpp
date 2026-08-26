#include "mesh_size.hpp"

namespace geom {

void mesh_size(const std::string& element, int nod, int nxe, int nye,
               int& nels, int& nn, int nze) {
  if (element == "triangle") {
    nels = nxe * nye * 2;
    if (nod == 3) nn = (nxe + 1) * (nye + 1);
    else if (nod == 6) nn = (2 * nxe + 1) * (2 * nye + 1);
    else if (nod == 10) nn = (3 * nxe + 1) * (3 * nye + 1);
    else if (nod == 15) nn = (4 * nxe + 1) * (4 * nye + 1);
  } else if (element == "quadrilateral") {
    nels = nxe * nye;
    if (nod == 4) nn = (nxe + 1) * (nye + 1);
    else if (nod == 5) nn = (nxe + 1) * (nye + 1) + nxe * nye;
    else if (nod == 8) nn = (2 * nxe + 1) * (nye + 1) + (nxe + 1) * nye;
    else if (nod == 9) nn = (2 * nxe + 1) * (2 * nye + 1);
  } else if (element == "hexahedron" && nze >= 0) {
    nels = nxe * nye * nze;
    if (nod == 8) nn = (nxe + 1) * (nye + 1) * (nze + 1);
    else if (nod == 14) nn = 4 * nxe * nye * nze + 2 * (nxe * nye + nye * nze + nze * nxe) + nxe + nye + nze + 1;
    else if (nod == 20) nn = ((2 * nxe + 1) * (nze + 1) + (nxe + 1) * nze) * (nye + 1) + (nxe + 1) * (nze + 1) * nye;
  }
}

} // namespace geom
