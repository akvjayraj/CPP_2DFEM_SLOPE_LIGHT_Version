#include "mesh_ensi.hpp"
#include <fstream>

namespace fem {

void mesh_ensi(const std::string& argv, int nlen, const std::vector<std::vector<double>>& g_coord,
               const std::vector<std::vector<int>>& g_num, const std::string& element,
               const std::vector<int>& etype, const std::vector<std::vector<int>>& nf,
               const std::vector<double>& loads, int nstep, int npri, double dtim, bool solid) {
  (void)g_coord;
  (void)g_num;
  (void)element;
  (void)etype;
  (void)nf;
  (void)loads;
  (void)nstep;
  (void)npri;
  (void)dtim;
  (void)solid;
  std::string fname = (nlen > 0 && nlen <= static_cast<int>(argv.size())) ? argv.substr(0, static_cast<size_t>(nlen)) : argv;
  fname += ".ensi.case";
  std::ofstream out(fname);
  if (out) out << "# Ensight stub\n";
}

} // namespace fem
