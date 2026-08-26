#include "contour.hpp"
#include <fstream>
#include <cmath>
#include <algorithm>

namespace fem {

void contour(const std::vector<double>& loads, const std::vector<std::vector<double>>& g_coord,
             const std::vector<std::vector<int>>& g_num, int ned, const std::string& argv, int nlen, int ips) {
  (void)ned;
  (void)ips;
  std::string fname = (nlen > 0 && nlen <= static_cast<int>(argv.size())) ? argv.substr(0, static_cast<size_t>(nlen)) : argv;
  fname += ".con";
  std::ofstream out(fname);
  if (!out) return;
  int nn = static_cast<int>(g_coord[0].size());
  double xmin = g_coord[0][0], xmax = xmin, ymin = g_coord[1][0], ymax = ymin;
  for (int i = 1; i < nn; ++i) {
    xmin = std::min(xmin, g_coord[0][i]);
    xmax = std::max(xmax, g_coord[0][i]);
    ymin = std::min(ymin, g_coord[1][i]);
    ymax = std::max(ymax, g_coord[1][i]);
  }
  out << "%!PS-Adobe-1.0\n%%Contour output (minimal)\nshowpage\n";
}

} // namespace fem
