#include "mesh.hpp"
#include <fstream>
#include <cmath>
#include <algorithm>

namespace fem {

void mesh(const std::vector<std::vector<double>>& g_coord, const std::vector<std::vector<int>>& g_num,
          const std::string& argv, int nlen, int ips) {
  std::string fname = (nlen > 0 && nlen <= static_cast<int>(argv.size())) ? argv.substr(0, static_cast<size_t>(nlen)) : argv;
  fname += ".msh";
  std::ofstream out(fname);
  if (!out) return;
  int nn = static_cast<int>(g_coord[0].size());
  int nod = static_cast<int>(g_num.size());
  int nel = static_cast<int>(g_num[0].size());
  double xmin = g_coord[0][0], xmax = xmin, ymin = g_coord[1][0], ymax = ymin;
  for (int i = 1; i < nn; ++i) {
    xmin = std::min(xmin, g_coord[0][i]);
    xmax = std::max(xmax, g_coord[0][i]);
    ymin = std::min(ymin, g_coord[1][i]);
    ymax = std::max(ymax, g_coord[1][i]);
  }
  double width = xmax - xmin, height = ymax - ymin;
  double scale = 72.0, sxy, xo, yo;
  if (height >= (11.0/8.5)*width) {
    sxy = scale * 8.0 / height;
    xo = scale * 0.5 * (8.5 - 8.0*width/height);
    yo = scale * 1.5;
  } else {
    sxy = scale * 5.5 / width;
    xo = scale * 1.5;
    yo = scale * 0.5 * (11.0 - 5.5*height/width);
  }
  out << "%!PS-Adobe-1.0\n%%DocumentFonts: none\n%%Pages: 1\n%%EndComments\n";
  out << "/m {moveto} def\n/l {lineto} def\n/s {stroke} def\n/c {closepath} def\n%%EndProlog\n%%Page: 0 1\ngsave\n";
  out << xo << " " << yo << " translate\n0.5 setlinewidth\n";
  int nod_eff = (nod == 5) ? 4 : (nod == 9) ? 8 : (nod == 10) ? 9 : (nod == 15) ? 12 : nod;
  for (int i = 0; i < nel; ++i) {
    int ii = g_num[0][i];
    if (ii <= 0) continue;
    double x = sxy * (g_coord[0][ii-1] - xmin), y = sxy * (g_coord[1][ii-1] - ymin);
    out << x << " " << y << " m\n";
    for (int j = 1; j < nod_eff; ++j) {
      int jj = g_num[j][i];
      if (jj <= 0) continue;
      x = sxy * (g_coord[0][jj-1] - xmin); y = sxy * (g_coord[1][jj-1] - ymin);
      out << x << " " << y << " l\n";
    }
    out << "c s\n";
  }
  out << "grestore\nshowpage\n";
}

} // namespace fem
