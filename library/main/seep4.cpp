#include "seep4.hpp"
#include "determinant.hpp"
#include "invert.hpp"
#include "shape_der.hpp"
#include "shape_fun.hpp"
#include "sample.hpp"
#include <vector>

namespace fem {

void seep4(std::vector<std::vector<double>>& kp,
           const std::vector<std::vector<double>>& coord,
           const std::vector<std::vector<double>>& kay) {
  const int nod = 4, ndim = 2, nip = 4;
  if (kp.size() != static_cast<size_t>(nod))
    kp.resize(nod, std::vector<double>(nod, 0.0));
  else
    for (int i = 0; i < nod; ++i)
      for (int j = 0; j < nod; ++j) kp[i][j] = 0.0;

  std::vector<std::vector<double>> points(nip, std::vector<double>(ndim));
  std::vector<double> weights(nip);
  fem::sample("quadrilateral", points, &weights);
  std::vector<std::vector<double>> der(ndim, std::vector<double>(nod));
  std::vector<std::vector<double>> deriv(ndim, std::vector<double>(nod));
  std::vector<std::vector<double>> jac(ndim, std::vector<double>(ndim));
  std::vector<double> fun(nod);

  for (int ig = 0; ig < nip; ++ig) {
    fem::shape_der(der, points, ig + 1);
    fem::shape_fun(fun, points, ig + 1);
    for (int i = 0; i < ndim; ++i)
      for (int j = 0; j < ndim; ++j) {
        jac[i][j] = 0.0;
        for (int k = 0; k < nod; ++k) jac[i][j] += der[i][k] * coord[k][j];
      }
    double det = fem::determinant(jac);
    fem::invert(jac);
    for (int i = 0; i < ndim; ++i)
      for (int j = 0; j < nod; ++j) {
        deriv[i][j] = 0.0;
        for (int k = 0; k < ndim; ++k) deriv[i][j] += jac[i][k] * der[k][j];
      }
    for (int i = 0; i < nod; ++i)
      for (int j = 0; j < nod; ++j) {
        double sum = 0.0;
        for (int p = 0; p < ndim; ++p)
          for (int q = 0; q < ndim; ++q)
            sum += deriv[p][i] * kay[p][q] * deriv[q][j];
        kp[i][j] += sum * det * weights[ig];
      }
  }
}

} // namespace fem
