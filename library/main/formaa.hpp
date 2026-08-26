#ifndef MAIN_FORMAA_HPP
#define MAIN_FORMAA_HPP

#include <vector>

namespace fem {

void formaa(const std::vector<double>& vmfl, const std::vector<std::vector<double>>& rmat,
            std::vector<std::vector<double>>& daatd);

} // namespace fem

#endif
