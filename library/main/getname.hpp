#ifndef MAIN_GETNAME_HPP
#define MAIN_GETNAME_HPP

#include <string>

namespace fem {

/// Reads the base name of the data file from argv or prompts user.
/// Returns the name and its length. Throws if .dat file not found.
void getname(int argc, char* argv[], std::string& argv_out, int& nlen);

} // namespace fem

#endif
