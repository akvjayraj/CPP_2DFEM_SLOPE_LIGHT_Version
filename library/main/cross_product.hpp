#ifndef MAIN_CROSS_PRODUCT_HPP
#define MAIN_CROSS_PRODUCT_HPP

#include <vector>

namespace fem {

/// Forms outer product ntn(i,j) = a(i)*b(j). Used for mass matrix N^T*N.
void cross_product(const std::vector<double>& a, const std::vector<double>& b,
                   std::vector<std::vector<double>>& ntn);

} // namespace fem

#endif
