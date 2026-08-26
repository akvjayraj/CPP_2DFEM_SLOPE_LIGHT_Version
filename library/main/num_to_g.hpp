#ifndef MAIN_NUM_TO_G_HPP
#define MAIN_NUM_TO_G_HPP

#include <vector>

namespace fem {

/// Finds the g (steering) vector from node numbers and nf.
/// num: node numbers (1-based), nf: column-major (nodof x nn)
void num_to_g(const std::vector<int>& num, const std::vector<int>& nf,
              int nodof, int nn, std::vector<int>& g);

} // namespace fem

#endif
