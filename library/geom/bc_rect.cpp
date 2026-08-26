#include "bc_rect.hpp"

namespace geom {

void bc_rect(int nxe, int nye, std::vector<int>& nf, char dir) {
  const int nodof = 2;
  int nm = 0;
  int ic = 0;
  auto at = [&](int row, int col) -> int& { return nf[col * nodof + row]; };

  if (dir == 'y') {
    for (int j = 0; j < 2 * nye; ++j) {
      at(0, nm) = 0;
      at(1, nm) = ++ic;
      ++nm;
    }
    at(0, nm) = 0;
    at(1, nm) = 0;
    ++nm;

    for (int i = 0; i < nxe - 1; ++i) {
      for (int j = 0; j < nye; ++j) {
        at(0, nm) = ++ic;
        at(1, nm) = ++ic;
        ++nm;
      }
      at(0, nm) = 0;
      at(1, nm) = 0;
      ++nm;

      for (int j = 0; j < 2 * nye; ++j) {
        at(0, nm) = ++ic;
        at(1, nm) = ++ic;
        ++nm;
      }
      at(0, nm) = 0;
      at(1, nm) = 0;
      ++nm;
    }

    for (int j = 0; j < nye; ++j) {
      at(0, nm) = ++ic;
      at(1, nm) = ++ic;
      ++nm;
    }
    at(0, nm) = 0;
    at(1, nm) = 0;
    ++nm;

    for (int j = 0; j < 2 * nye; ++j) {
      at(0, nm) = 0;
      at(1, nm) = ++ic;
      ++nm;
    }
    at(0, nm) = 0;
    at(1, nm) = 0;
    ++nm;
  } else {
    for (int j = 0; j < nye; ++j) {
      at(0, nm) = 0;
      at(1, nm) = ++ic;
      ++nm;
      for (int i = 0; i < 2 * nxe - 1; ++i) {
        at(0, nm) = ++ic;
        at(1, nm) = ++ic;
        ++nm;
      }
      at(0, nm) = 0;
      at(1, nm) = ++ic;
      ++nm;

      at(0, nm) = 0;
      at(1, nm) = ++ic;
      ++nm;
      for (int i = 0; i < nxe - 1; ++i) {
        at(0, nm) = ++ic;
        at(1, nm) = ++ic;
        ++nm;
      }
      at(0, nm) = 0;
      at(1, nm) = ++ic;
      ++nm;
    }

    for (int i = 0; i < 2 * nxe + 1; ++i) {
      at(0, nm) = 0;
      at(1, nm) = 0;
      ++nm;
    }
  }
}

} // namespace geom
