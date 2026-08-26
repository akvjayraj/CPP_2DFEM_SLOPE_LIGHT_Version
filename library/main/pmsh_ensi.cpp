#include "pmsh_ensi.hpp"
#include <fstream>

namespace fem {

void pmsh_ensi(const std::string& argv, int nlen, const std::vector<std::vector<int>>& g_num,
               const std::vector<double>& loads, int nstep, int npri, double dtim) {
  (void)g_num;
  (void)loads;
  (void)nstep;
  (void)npri;
  (void)dtim;
  std::string fname = (nlen > 0 && nlen <= static_cast<int>(argv.size())) ? argv.substr(0, static_cast<size_t>(nlen)) : argv;
  fname += ".ensi.p";
  std::ofstream out(fname);
  if (out) out << "# Ensight pressure stub\n";
}

} // namespace fem
