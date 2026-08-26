#ifndef MAIN_EXC_NODS_HPP
#define MAIN_EXC_NODS_HPP

#include <vector>

namespace fem {

void exc_nods(int noexe, const std::vector<int>& exele, const std::vector<std::vector<int>>& g_num,
              std::vector<int>& totex, int& ntote, std::vector<std::vector<int>>& nf);

} // namespace fem

#endif
