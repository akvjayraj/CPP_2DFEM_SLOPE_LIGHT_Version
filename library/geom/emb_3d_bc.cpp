#include "emb_3d_bc.hpp"

namespace geom {

void emb_3d_bc(int ifix, int nx1, int nx2, int ny1, int ny2, int nze,
               std::vector<int>& nf) {
  const int nodof = 3;
  int nye = ny1 + ny2;
  int nm = 0;
  int ic = 0;
  auto at = [&](int row, int col) -> int& { return nf[col * nodof + row]; };

  for (int i = 0; i < 2 * nye; ++i) {
    at(0, nm) = at(1, nm) = 0;
    if (ifix == 1) at(1, nm) = ++ic;
    ++nm;
  }
  at(0, nm) = at(1, nm) = at(2, nm) = 0;
  ++nm;

  for (int j = 0; j < nx1; ++j) {
    for (int i = 0; i < nye; ++i) {
      at(0, nm) = at(1, nm) = at(2, nm) = 0;
      if (ifix == 1) { at(0, nm) = ++ic; at(1, nm) = ++ic; }
      ++nm;
    }
    at(0, nm) = at(1, nm) = at(2, nm) = 0;
    ++nm;
    for (int i = 0; i < 2 * nye; ++i) {
      at(0, nm) = at(1, nm) = at(2, nm) = 0;
      if (ifix == 1) { at(0, nm) = ++ic; at(1, nm) = ++ic; }
      ++nm;
    }
    at(0, nm) = at(1, nm) = at(2, nm) = 0;
    ++nm;
  }

  for (int j = 0; j < nx2; ++j) {
    for (int i = 0; i < ny2; ++i) {
      at(0, nm) = at(1, nm) = at(2, nm) = 0;
      if (ifix == 1) { at(0, nm) = ++ic; at(1, nm) = ++ic; }
      ++nm;
    }
    at(0, nm) = at(1, nm) = at(2, nm) = 0;
    ++nm;
    for (int i = 0; i < 2 * ny2; ++i) {
      at(0, nm) = at(1, nm) = at(2, nm) = 0;
      if (ifix == 1) {
        if (j < nx2 - 1) at(0, nm) = ++ic;
        at(1, nm) = ++ic;
      }
      ++nm;
    }
    at(0, nm) = at(1, nm) = at(2, nm) = 0;
    ++nm;
  }

  for (int i = 0; i < nye; ++i) {
    at(0, nm) = 0;
    at(2, nm) = ++ic;
    at(1, nm) = ++ic;
    ++nm;
  }
  at(0, nm) = at(1, nm) = at(2, nm) = 0;
  ++nm;

  for (int j = 0; j < nx1; ++j) {
    for (int i = 0; i < nye; ++i) {
      at(0, nm) = ++ic;
      at(2, nm) = ++ic;
      at(1, nm) = ++ic;
      ++nm;
    }
    at(0, nm) = at(1, nm) = at(2, nm) = 0;
    ++nm;
  }

  for (int j = 0; j < nx2; ++j) {
    for (int i = 0; i < ny2; ++i) {
      at(0, nm) = (j == nx2 - 1) ? 0 : ++ic;
      at(2, nm) = ++ic;
      at(1, nm) = ++ic;
      ++nm;
    }
    at(0, nm) = at(1, nm) = at(2, nm) = 0;
    ++nm;
  }

  for (int k = 0; k < nze - 1; ++k) {
    for (int i = 0; i < 2 * nye; ++i) {
      at(0, nm) = 0;
      at(2, nm) = ++ic;
      at(1, nm) = ++ic;
      ++nm;
    }
    at(0, nm) = at(1, nm) = at(2, nm) = 0;
    ++nm;

    for (int j = 0; j < nx1; ++j) {
      for (int i = 0; i < nye; ++i) {
        at(0, nm) = ++ic;
        at(2, nm) = ++ic;
        at(1, nm) = ++ic;
        ++nm;
      }
      at(0, nm) = at(1, nm) = at(2, nm) = 0;
      ++nm;
      for (int i = 0; i < 2 * nye; ++i) {
        at(0, nm) = ++ic;
        at(2, nm) = ++ic;
        at(1, nm) = ++ic;
        ++nm;
      }
      at(0, nm) = at(1, nm) = at(2, nm) = 0;
      ++nm;
    }

    for (int j = 0; j < nx2; ++j) {
      for (int i = 0; i < ny2; ++i) {
        at(0, nm) = ++ic;
        at(2, nm) = ++ic;
        at(1, nm) = ++ic;
        ++nm;
      }
      at(0, nm) = at(1, nm) = at(2, nm) = 0;
      ++nm;
      for (int i = 0; i < 2 * ny2; ++i) {
        at(0, nm) = (j == nx2 - 1) ? 0 : ++ic;
        at(2, nm) = ++ic;
        at(1, nm) = ++ic;
        ++nm;
      }
      at(0, nm) = at(1, nm) = at(2, nm) = 0;
      ++nm;
    }

    for (int i = 0; i < nye; ++i) {
      at(0, nm) = 0;
      at(2, nm) = ++ic;
      at(1, nm) = ++ic;
      ++nm;
    }
    at(0, nm) = at(1, nm) = at(2, nm) = 0;
    ++nm;

    for (int j = 0; j < nx1; ++j) {
      for (int i = 0; i < nye; ++i) {
        at(0, nm) = ++ic;
        at(2, nm) = ++ic;
        at(1, nm) = ++ic;
        ++nm;
      }
      at(0, nm) = at(1, nm) = at(2, nm) = 0;
      ++nm;
    }

    for (int j = 0; j < nx2; ++j) {
      for (int i = 0; i < ny2; ++i) {
        at(0, nm) = (j == nx2 - 1) ? 0 : ++ic;
        at(2, nm) = ++ic;
        at(1, nm) = ++ic;
        ++nm;
      }
      at(0, nm) = at(1, nm) = at(2, nm) = 0;
      ++nm;
    }
  }

  for (int i = 0; i < 2 * nye; ++i) {
    at(0, nm) = at(1, nm) = at(2, nm) = 0;
    if (ifix == 1 || ifix == 2) at(1, nm) = ++ic;
    ++nm;
  }
  at(0, nm) = at(1, nm) = at(2, nm) = 0;
  ++nm;

  for (int j = 0; j < nx1; ++j) {
    for (int i = 0; i < nye; ++i) {
      at(0, nm) = at(1, nm) = at(2, nm) = 0;
      if (ifix == 1 || ifix == 2) { at(0, nm) = ++ic; at(1, nm) = ++ic; }
      ++nm;
    }
    at(0, nm) = at(1, nm) = at(2, nm) = 0;
    ++nm;
    for (int i = 0; i < 2 * nye; ++i) {
      at(0, nm) = at(1, nm) = at(2, nm) = 0;
      if (ifix == 1 || ifix == 2) { at(0, nm) = ++ic; at(1, nm) = ++ic; }
      ++nm;
    }
    at(0, nm) = at(1, nm) = at(2, nm) = 0;
    ++nm;
  }

  for (int j = 0; j < nx2; ++j) {
    for (int i = 0; i < ny2; ++i) {
      at(0, nm) = at(1, nm) = at(2, nm) = 0;
      if (ifix == 1 || ifix == 2) { at(0, nm) = ++ic; at(1, nm) = ++ic; }
      ++nm;
    }
    at(0, nm) = at(1, nm) = at(2, nm) = 0;
    ++nm;
    for (int i = 0; i < 2 * ny2; ++i) {
      at(0, nm) = at(1, nm) = at(2, nm) = 0;
      if (ifix == 1 || ifix == 2) {
        if (j < nx2 - 1) at(0, nm) = ++ic;
        at(1, nm) = ++ic;
      }
      ++nm;
    }
    at(0, nm) = at(1, nm) = at(2, nm) = 0;
    ++nm;
  }
}

} // namespace geom
