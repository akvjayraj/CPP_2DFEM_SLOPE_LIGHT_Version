#ifndef MAIN_LNBLNK_HPP
#define MAIN_LNBLNK_HPP

#include <string>

namespace fem {

/// Returns the index (1-based) of the last non-blank, non-tab, non-null
/// character in str. Returns 0 if all blank.
int lnblnk(const std::string& str);

} // namespace fem

#endif
