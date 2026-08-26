#ifndef MAIN_SEEP4_HPP
#define MAIN_SEEP4_HPP

#include <vector>

namespace fem {

/// Forms the 4-node quadrilateral conductivity (Laplace) matrix for seepage.
/// kp = integral of B^T * kay * B * det(J) over element.
/// coord: nod x ndim (4 x 2), kay: ndim x ndim (2 x 2), kp: nod x nod (4 x 4).
void seep4(std::vector<std::vector<double>>& kp,
           const std::vector<std::vector<double>>& coord,
           const std::vector<std::vector<double>>& kay);

} // namespace fem

#endif
