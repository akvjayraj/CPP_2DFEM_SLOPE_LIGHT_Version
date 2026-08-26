#include "fsparv.hpp"
#include <cstdlib>
#include <iostream>

namespace fem {

static void check_ival(int ival, int nkv, int k, int iw) {
  if (ival < 1 || ival > nkv) {
    std::cerr << "fsparv: skyline index out of range ival=" << ival << " (1.." << nkv
              << ") k=" << k << " iw=" << iw << ". Mesh/BC data may be inconsistent.\n";
    std::exit(6);
  }
}

void fsparv(std::vector<double>& kv, const double km[2][2],
            const std::vector<int>& g, const std::vector<int>& kdiag) {
  const int nkv = static_cast<int>(kv.size());
  int idof = static_cast<int>(g.size());
  for (int i = 0; i < idof; ++i) {
    int k = g[i];
    if (k != 0) {
      for (int j = 0; j < idof; ++j) {
        if (g[j] != 0) {
          int iw = k - g[j];
          if (iw >= 0) {
            int ival = kdiag[k - 1] - iw;
            check_ival(ival, nkv, k, iw);
            kv[ival - 1] += km[i][j];
          }
        }
      }
    }
  }
}

void fsparv(std::vector<double>& kv,
            const std::vector<std::vector<double>>& km,
            const std::vector<int>& g, const std::vector<int>& kdiag) {
  const int nkv = static_cast<int>(kv.size());
  int idof = static_cast<int>(g.size());
  for (int i = 0; i < idof; ++i) {
    int k = g[i];
    if (k != 0) {
      for (int j = 0; j < idof; ++j) {
        if (g[j] != 0) {
          int iw = k - g[j];
          if (iw >= 0) {
            int ival = kdiag[k - 1] - iw;
            check_ival(ival, nkv, k, iw);
            kv[ival - 1] += km[i][j];
          }
        }
      }
    }
  }
}

} // namespace fem
