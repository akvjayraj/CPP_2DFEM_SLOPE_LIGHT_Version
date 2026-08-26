#include "beamdis.hpp"
#include <fstream>
#include <cmath>

namespace fem {

void beamdis(const std::vector<double>& loads, const std::vector<std::vector<int>>& nf,
             double ratmax, int interp, int nels, const std::vector<double>& ell,
             const std::string& argv, int nlen, int ips) {
  (void)ratmax;
  (void)interp;
  (void)ips;
  std::string fname = (nlen > 0 && nlen <= static_cast<int>(argv.size())) ? argv.substr(0, static_cast<size_t>(nlen)) : argv;
  fname += ".dis";
  std::ofstream out(fname);
  if (!out) return;
  out << "%!PS-Adobe-1.0\n%%Beam displacement (minimal)\n";
  double x = 0.0;
  for (int i = 0; i < nels && i < static_cast<int>(ell.size()); ++i) x += ell[i];
  out << "showpage\n";
}

} // namespace fem
