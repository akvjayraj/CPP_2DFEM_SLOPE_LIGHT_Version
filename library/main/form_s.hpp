#ifndef MAIN_FORM_S_HPP
#define MAIN_FORM_S_HPP

#include <vector>

namespace fem {

/// Forms s vector and omega, gamma for BiCGSTAB(ell). gg is (ell+1) x (ell+1).
void form_s(const std::vector<std::vector<double>>& gg, int ell, double kappa,
            double& omega, std::vector<double>& gamma, std::vector<double>& s);

} // namespace fem

#endif
