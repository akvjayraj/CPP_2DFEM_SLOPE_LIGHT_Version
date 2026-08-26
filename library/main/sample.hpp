#ifndef MAIN_SAMPLE_HPP
#define MAIN_SAMPLE_HPP

#include <string>
#include <vector>

namespace fem {

/// Returns local coordinates and weights of integration points.
/// s: nip x ndim. wt: optional, size nip.
void sample(const std::string& element, std::vector<std::vector<double>>& s,
            std::vector<double>* wt = nullptr);

} // namespace fem

#endif
