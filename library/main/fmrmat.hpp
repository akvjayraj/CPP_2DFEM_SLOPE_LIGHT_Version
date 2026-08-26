#ifndef MAIN_FMRMAT_HPP
#define MAIN_FMRMAT_HPP

#include <vector>

namespace fem {

void fmrmat(const std::vector<double>& vmfl, double dsbar, double dlam,
            const std::vector<std::vector<double>>& dee, std::vector<std::vector<double>>& rmat);

} // namespace fem

#endif
