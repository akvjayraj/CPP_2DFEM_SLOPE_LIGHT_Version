#include "vecmsh.hpp"
#include <fstream>

namespace fem {

void vecmsh(const std::vector<double>& loads, const std::vector<std::vector<int>>& nf,
            const std::vector<std::vector<double>>& g_coord, const std::vector<std::vector<int>>& g_num,
            const std::string& argv, int nlen, int ips, double ratmax) {
  (void)loads;
  (void)nf;
  (void)g_coord;
  (void)g_num;
  (void)ips;
  (void)ratmax;
  std::string fname = (nlen > 0 && nlen <= static_cast<int>(argv.size())) ? argv.substr(0, static_cast<size_t>(nlen)) : argv;
  fname += ".vec";
  std::ofstream out(fname);
  if (out) out << "%!PS-Adobe-1.0\nshowpage\n";
}

} // namespace fem
