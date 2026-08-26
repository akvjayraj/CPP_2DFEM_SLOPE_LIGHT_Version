#ifndef MAIN_VMFLOW_HPP
#define MAIN_VMFLOW_HPP

#include <vector>

namespace fem {

/// Von Mises flow vector (2D plane strain, nst=4).
void vmflow(const std::vector<double>& stress, double dsbar, std::vector<double>& vmfl);

} // namespace fem

#endif
