#include "getname.hpp"
#include "lnblnk.hpp"
#include <iostream>
#include <fstream>
#include <stdexcept>

namespace fem {

void getname(int argc, char* argv[], std::string& argv_out, int& nlen) {
  if (argc < 2) {
    std::cout << "Please enter the base name of data file: ";
    std::cin >> argv_out;
  } else {
    argv_out = argv[1];
  }
  nlen = lnblnk(argv_out);
  std::string dat_path = argv_out.substr(0, static_cast<size_t>(nlen)) + ".dat";
  std::ifstream f(dat_path);
  if (!f) {
    std::cerr << "Data file not found: " << dat_path << "\n";
    std::cerr << "Please create or check spelling.\n";
    throw std::runtime_error("Data file not found");
  }
}

} // namespace fem
