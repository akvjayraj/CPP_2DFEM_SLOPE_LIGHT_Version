#include "num_to_g.hpp"

namespace fem {

void num_to_g(const std::vector<int>& num, const std::vector<int>& nf,
              int nodof, int nn, std::vector<int>& g) {
  int nod = static_cast<int>(num.size());
  for (int i = 0; i < nod; ++i) {
    int node_num = num[i];  // 1-based node number
    for (int d = 0; d < nodof; ++d) {
      g[i * nodof + d] = nf[d + (node_num - 1) * nodof];
    }
  }
}

} // namespace fem
