#include "lnblnk.hpp"

namespace fem {

int lnblnk(const std::string& str) {
  for (int j = static_cast<int>(str.size()) - 1; j >= 0; --j) {
    char c = str[static_cast<size_t>(j)];
    if (c != ' ' && c != '\0' && c != '\t') {
      return j + 1;  // 1-based
    }
  }
  return 0;
}

} // namespace fem
