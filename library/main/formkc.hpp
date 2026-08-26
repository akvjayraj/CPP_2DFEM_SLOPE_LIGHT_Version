#ifndef MAIN_FORMKC_HPP
#define MAIN_FORMKC_HPP

#include <vector>
#include <complex>

namespace fem {

/// Assembles complex band matrix bk from real km and cm (bk = km + i*cm). Skyline-style indexing ival = n*(icd-1)+g(i).
void formkc(std::vector<std::complex<double>>& bk, const std::vector<std::vector<double>>& km,
            const std::vector<std::vector<double>>& cm, const std::vector<int>& g, int n);

} // namespace fem

#endif
