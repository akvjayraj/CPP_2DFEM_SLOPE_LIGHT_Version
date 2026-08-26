#include "dismsh.hpp"
#include <fstream>
#include <cmath>
#include <algorithm>

namespace fem {

void dismsh(const std::vector<double>& loads, const std::vector<std::vector<int>>& nf,
            double ratmax, const std::vector<std::vector<double>>& g_coord,
            const std::vector<std::vector<int>>& g_num, const std::string& argv, int nlen, int ips) {
  std::string fname = (nlen > 0 && nlen <= static_cast<int>(argv.size())) ? argv.substr(0, static_cast<size_t>(nlen)) : argv;
  fname += ".dis";
  std::ofstream out(fname);
  if (!out) return;
  int nn = static_cast<int>(nf[0].size());
  double xmin = g_coord[0][0], xmax = xmin, ymin = g_coord[1][0], ymax = ymin;
  for (int i = 1; i < nn; ++i) {
    xmin = std::min(xmin, g_coord[0][i]);
    xmax = std::max(xmax, g_coord[0][i]);
    ymin = std::min(ymin, g_coord[1][i]);
    ymax = std::max(ymax, g_coord[1][i]);
  }
  double width = xmax - xmin, height = ymax - ymin;
  double dmax = ratmax * (height > width ? height : width);
  double vmax = 0.0;
  for (int i = 0; i < nn; ++i)
    for (int j = 0; j < 2; ++j)
      if (nf[j][i] > 0 && nf[j][i] <= static_cast<int>(loads.size()))
        vmax = std::max(vmax, std::abs(loads[nf[j][i]-1]));
  double dismag = (vmax > 1e-30) ? dmax / vmax : 1.0;
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
  int nod = static_cast<int>(g_num.size()), nel = static_cast<int>(g_num[0].size());
  int nod_eff = (nod == 5) ? 4 : (nod == 9) ? 8 : nod;
  for (int i = 0; i < nel; ++i) {
    for (int j = 0; j < nod_eff; ++j) {
      int n = g_num[j][i];
      if (n <= 0) continue;
      double x = g_coord[0][n-1] + (nf[0][n-1] > 0 ? dismag * loads[nf[0][n-1]-1] : 0.0);
      double y = g_coord[1][n-1] + (nf[1][n-1] > 0 ? dismag * loads[nf[1][n-1]-1] : 0.0);
      if (j == 0) out << sxy*(x-xmin) << " " << sxy*(y-ymin) << " m\n";
      else out << sxy*(x-xmin) << " " << sxy*(y-ymin) << " l\n";
    }
    out << "c s\n";
  }
  out << "grestore\nshowpage\n";
}

} // namespace fem
