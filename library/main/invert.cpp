#include "invert.hpp"

namespace fem {

void invert(std::vector<std::vector<double>>& matrix) {
  int ndim = static_cast<int>(matrix.size());
  if (ndim == 2) {
    double det = matrix[0][0] * matrix[1][1] - matrix[0][1] * matrix[1][0];
    double j11 = matrix[0][0];
    matrix[0][0] = matrix[1][1];
    matrix[1][1] = j11;
    matrix[0][1] = -matrix[0][1];
    matrix[1][0] = -matrix[1][0];
    for (int i = 0; i < 2; ++i)
      for (int j = 0; j < 2; ++j)
        matrix[i][j] /= det;
  } else if (ndim == 3) {
    double det = matrix[0][0] * (matrix[1][1] * matrix[2][2] - matrix[2][1] * matrix[1][2]);
    det -= matrix[0][1] * (matrix[1][0] * matrix[2][2] - matrix[2][0] * matrix[1][2]);
    det += matrix[0][2] * (matrix[1][0] * matrix[2][1] - matrix[2][0] * matrix[1][1]);
    double j11 = matrix[1][1] * matrix[2][2] - matrix[2][1] * matrix[1][2];
    double j21 = -matrix[1][0] * matrix[2][2] + matrix[2][0] * matrix[1][2];
    double j31 = matrix[1][0] * matrix[2][1] - matrix[2][0] * matrix[1][1];
    double j12 = -matrix[0][1] * matrix[2][2] + matrix[2][1] * matrix[0][2];
    double j22 = matrix[0][0] * matrix[2][2] - matrix[2][0] * matrix[0][2];
    double j32 = -matrix[0][0] * matrix[2][1] + matrix[2][0] * matrix[0][1];
    double j13 = matrix[0][1] * matrix[1][2] - matrix[1][1] * matrix[0][2];
    double j23 = -matrix[0][0] * matrix[1][2] + matrix[1][0] * matrix[0][2];
    double j33 = matrix[0][0] * matrix[1][1] - matrix[1][0] * matrix[0][1];
    matrix[0][0] = j11; matrix[0][1] = j12; matrix[0][2] = j13;
    matrix[1][0] = j21; matrix[1][1] = j22; matrix[1][2] = j23;
    matrix[2][0] = j31; matrix[2][1] = j32; matrix[2][2] = j33;
    for (int i = 0; i < 3; ++i)
      for (int j = 0; j < 3; ++j)
        matrix[i][j] /= det;
  }
}

} // namespace fem
