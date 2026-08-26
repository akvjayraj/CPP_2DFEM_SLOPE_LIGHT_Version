#ifndef MAIN_FMKDKE_HPP
#define MAIN_FMKDKE_HPP

#include <vector>

namespace fem {

void fmkdke(const std::vector<std::vector<double>>& km, const std::vector<std::vector<double>>& kp,
            const std::vector<std::vector<double>>& c, double theta,
            std::vector<std::vector<double>>& ke, std::vector<std::vector<double>>& kd);

} // namespace fem

#endif
