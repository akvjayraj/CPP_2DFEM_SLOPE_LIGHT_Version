#include "emb_2d_bc.hpp"

namespace geom {

void emb_2d_bc(int nx1, int nx2, int ny1, int ny2, std::vector<int>& nf) {
  const int nodof = 2;
  int nye = ny1 + ny2;
  int nm = 0;
  int ic = 0;
  auto at = [&](int row, int col) -> int& { return nf[col * nodof + row]; };

  for (int i = 0; i < 2 * nye; ++i) {
    at(0, nm) = 0;
    at(1, nm) = ++ic;
    ++nm;
  }
  at(0, nm) = 0;
  at(1, nm) = 0;
  ++nm;

  for (int j = 0; j < nx1 - 1; ++j) {
    for (int i = 0; i < nye; ++i) {
      at(0, nm) = ++ic;
      at(1, nm) = ++ic;
      ++nm;
    }
    at(0, nm) = 0;
    at(1, nm) = 0;
    ++nm;
    for (int i = 0; i < 2 * nye; ++i) {
      at(0, nm) = ++ic;
      at(1, nm) = ++ic;
      ++nm;
    }
    at(0, nm) = 0;
    at(1, nm) = 0;
    ++nm;
  }

  for (int i = 0; i < nye; ++i) {
    at(0, nm) = ++ic;
    at(1, nm) = ++ic;
    ++nm;
  }
  at(0, nm) = 0;
  at(1, nm) = 0;
  ++nm;

  for (int i = 0; i < 2 * ny1; ++i) {
    at(0, nm) = ++ic;
    at(1, nm) = ++ic;
    ++nm;
  }

  if (nx2 == 0) {
    for (int i = 0; i < 2 * ny2; ++i) {
      at(0, nm) = 0;
      at(1, nm) = ++ic;
      ++nm;
    }
    at(0, nm) = 0;
    at(1, nm) = 0;
    ++nm;
  } else {
    for (int i = 0; i < 2 * ny2; ++i) {
      at(0, nm) = ++ic;
      at(1, nm) = ++ic;
      ++nm;
    }
    at(0, nm) = 0;
    at(1, nm) = 0;
    ++nm;
    for (int jj = 0; jj < nx2; ++jj) {
      for (int i = 0; i < ny2; ++i) {
        at(0, nm) = ++ic;
        at(1, nm) = ++ic;
        ++nm;
      }
      at(0, nm) = 0;
      at(1, nm) = 0;
      ++nm;
      for (int i = 0; i < 2 * ny2; ++i) {
        at(0, nm) = (jj == nx2 - 1) ? 0 : ++ic;
        at(1, nm) = ++ic;
        ++nm;
      }
      at(0, nm) = 0;
      at(1, nm) = 0;
      ++nm;
    }
  }
}

} // namespace geom
