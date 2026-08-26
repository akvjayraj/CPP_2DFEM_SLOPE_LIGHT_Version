#include "dismsh_ensi.hpp"
#include <fstream>

namespace fem {

void dismsh_ensi(const std::string& argv, int nlen, const std::vector<std::vector<double>>& g_coord,
                 const std::vector<std::vector<int>>& g_num, const std::vector<double>& loads,
                 const std::vector<std::vector<int>>& nf, int nstep, int npri, double dtim) {
  (void)g_coord;
  (void)g_num;
  (void)loads;
  (void)nf;
  (void)nstep;
  (void)npri;
  (void)dtim;
  std::string fname = (nlen > 0 && nlen <= static_cast<int>(argv.size())) ? argv.substr(0, static_cast<size_t>(nlen)) : argv;
  fname += ".ensi.disp";
  std::ofstream out(fname);
  if (out) out << "# Ensight displacement stub\n";
}

} // namespace fem
