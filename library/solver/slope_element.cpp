#include "slope_element.hpp"

#include <cmath>
#include <cctype>

namespace slope_element {

bool iequals(const std::string& a, const std::string& b) {
  if (a.size() != b.size()) return false;
  for (size_t i = 0; i < a.size(); ++i)
    if (std::tolower(static_cast<unsigned char>(a[i])) !=
        std::tolower(static_cast<unsigned char>(b[i])))
      return false;
  return true;
}

int mesh_nodes_per_element(const std::string& mesh_type) {
  if (iequals(mesh_type, "T6")) return 6;
  if (iequals(mesh_type, "Q4")) return 4;
  if (iequals(mesh_type, "Q8")) return 8;
  return 3;
}

int mesh_ndof(const std::string& mesh_type) {
  return 2 * mesh_nodes_per_element(mesh_type);
}

std::vector<GaussPt2> element_gauss_rule(const std::string& mesh_type) {
  std::vector<GaussPt2> gps;
  if (iequals(mesh_type, "T6")) {
    gps.push_back({1.0 / 6.0, 1.0 / 6.0, 1.0 / 6.0});
    gps.push_back({2.0 / 3.0, 1.0 / 6.0, 1.0 / 6.0});
    gps.push_back({1.0 / 6.0, 2.0 / 3.0, 1.0 / 6.0});
    return gps;
  }
  if (iequals(mesh_type, "Q4") || iequals(mesh_type, "Q8")) {
    const double a = 1.0 / std::sqrt(3.0);
    gps.push_back({-a, -a, 1.0});
    gps.push_back({ a, -a, 1.0});
    gps.push_back({ a,  a, 1.0});
    gps.push_back({-a,  a, 1.0});
    return gps;
  }
  gps.push_back({1.0 / 3.0, 1.0 / 3.0, 0.5});
  return gps;
}

void shape_derivatives(const std::string& mesh_type, double xi, double eta,
                       std::vector<double>& N,
                       std::vector<double>& dN_dxi,
                       std::vector<double>& dN_deta) {
  const int nod = mesh_nodes_per_element(mesh_type);
  N.assign(nod, 0.0);
  dN_dxi.assign(nod, 0.0);
  dN_deta.assign(nod, 0.0);

  if (iequals(mesh_type, "T6")) {
    const double L1 = 1.0 - xi - eta;
    const double L2 = xi;
    const double L3 = eta;
    N[0] = L1 * (2.0 * L1 - 1.0);
    N[1] = L2 * (2.0 * L2 - 1.0);
    N[2] = L3 * (2.0 * L3 - 1.0);
    N[3] = 4.0 * L1 * L2;
    N[4] = 4.0 * L2 * L3;
    N[5] = 4.0 * L3 * L1;
    dN_dxi[0] = -(4.0 * L1 - 1.0);
    dN_deta[0] = -(4.0 * L1 - 1.0);
    dN_dxi[1] =  4.0 * L2 - 1.0;
    dN_deta[1] = 0.0;
    dN_dxi[2] = 0.0;
    dN_deta[2] = 4.0 * L3 - 1.0;
    dN_dxi[3] = 4.0 * (L1 - L2);
    dN_deta[3] = -4.0 * L2;
    dN_dxi[4] = 4.0 * L3;
    dN_deta[4] = 4.0 * L2;
    dN_dxi[5] = -4.0 * L3;
    dN_deta[5] = 4.0 * (L1 - L3);
    return;
  }
  if (iequals(mesh_type, "Q4")) {
    N[0] = 0.25 * (1.0 - xi) * (1.0 - eta);
    N[1] = 0.25 * (1.0 + xi) * (1.0 - eta);
    N[2] = 0.25 * (1.0 + xi) * (1.0 + eta);
    N[3] = 0.25 * (1.0 - xi) * (1.0 + eta);
    dN_dxi[0] = -0.25 * (1.0 - eta);
    dN_deta[0] = -0.25 * (1.0 - xi);
    dN_dxi[1] =  0.25 * (1.0 - eta);
    dN_deta[1] = -0.25 * (1.0 + xi);
    dN_dxi[2] =  0.25 * (1.0 + eta);
    dN_deta[2] =  0.25 * (1.0 + xi);
    dN_dxi[3] = -0.25 * (1.0 + eta);
    dN_deta[3] =  0.25 * (1.0 - xi);
    return;
  }
  if (iequals(mesh_type, "Q8")) {
    N[0] = -0.25 * (1.0 - xi) * (1.0 - eta) * (1.0 + xi + eta);
    N[1] = -0.25 * (1.0 + xi) * (1.0 - eta) * (1.0 - xi + eta);
    N[2] = -0.25 * (1.0 + xi) * (1.0 + eta) * (1.0 - xi - eta);
    N[3] = -0.25 * (1.0 - xi) * (1.0 + eta) * (1.0 + xi - eta);
    N[4] =  0.50 * (1.0 - xi * xi) * (1.0 - eta);
    N[5] =  0.50 * (1.0 + xi) * (1.0 - eta * eta);
    N[6] =  0.50 * (1.0 - xi * xi) * (1.0 + eta);
    N[7] =  0.50 * (1.0 - xi) * (1.0 - eta * eta);
    dN_dxi[0] =  0.25 * (1.0 - eta) * (2.0 * xi + eta);
    dN_deta[0] = 0.25 * (1.0 - xi) * (xi + 2.0 * eta);
    dN_dxi[1] =  0.25 * (1.0 - eta) * (2.0 * xi - eta);
    dN_deta[1] = 0.25 * (1.0 + xi) * (2.0 * eta - xi);
    dN_dxi[2] =  0.25 * (1.0 + eta) * (2.0 * xi + eta);
    dN_deta[2] = 0.25 * (1.0 + xi) * (xi + 2.0 * eta);
    dN_dxi[3] =  0.25 * (1.0 + eta) * (2.0 * xi - eta);
    dN_deta[3] = 0.25 * (1.0 - xi) * (2.0 * eta - xi);
    dN_dxi[4] = -xi * (1.0 - eta);
    dN_deta[4] = -0.50 * (1.0 - xi * xi);
    dN_dxi[5] =  0.50 * (1.0 - eta * eta);
    dN_deta[5] = -(1.0 + xi) * eta;
    dN_dxi[6] = -xi * (1.0 + eta);
    dN_deta[6] =  0.50 * (1.0 - xi * xi);
    dN_dxi[7] = -0.50 * (1.0 - eta * eta);
    dN_deta[7] = -(1.0 - xi) * eta;
    return;
  }

  N[0] = 1.0 - xi - eta;
  N[1] = xi;
  N[2] = eta;
  dN_dxi[0] = -1.0; dN_deta[0] = -1.0;
  dN_dxi[1] =  1.0; dN_deta[1] =  0.0;
  dN_dxi[2] =  0.0; dN_deta[2] =  1.0;
}

bool build_bee_at_gp(const std::string& mesh_type,
                     const std::vector<std::vector<double>>& coord,
                     double xi, double eta,
                     std::vector<std::vector<double>>& bee,
                     std::vector<double>& N,
                     double& detJ) {
  const int nod = static_cast<int>(coord.size());
  std::vector<double> dN_dxi, dN_deta;
  shape_derivatives(mesh_type, xi, eta, N, dN_dxi, dN_deta);

  double j11 = 0.0, j12 = 0.0, j21 = 0.0, j22 = 0.0;
  for (int a = 0; a < nod; ++a) {
    const double x = coord[a][0], y = coord[a][1];
    j11 += dN_dxi[a]  * x;
    j12 += dN_deta[a] * x;
    j21 += dN_dxi[a]  * y;
    j22 += dN_deta[a] * y;
  }
  detJ = j11 * j22 - j12 * j21;
  if (std::abs(detJ) < 1e-16) return false;

  const double inv11 =  j22 / detJ;
  const double inv12 = -j21 / detJ;
  const double inv21 = -j12 / detJ;
  const double inv22 =  j11 / detJ;

  const int ndof = 2 * nod;
  for (int i = 0; i < 4; ++i)
    for (int j = 0; j < ndof; ++j)
      bee[i][j] = 0.0;
  for (int a = 0; a < nod; ++a) {
    const double dNdx = inv11 * dN_dxi[a] + inv12 * dN_deta[a];
    const double dNdy = inv21 * dN_dxi[a] + inv22 * dN_deta[a];
    const int ux = 2 * a, uy = ux + 1;
    bee[0][ux] = dNdx;
    bee[1][uy] = dNdy;
    bee[2][ux] = dNdy;
    bee[2][uy] = dNdx;
  }
  return true;
}

double radial_weight(const std::vector<double>& N,
                     const std::vector<std::vector<double>>& coord_node_major) {
  const int nod = static_cast<int>(N.size());
  double r = 0.0;
  for (int a = 0; a < nod && a < static_cast<int>(coord_node_major.size()); ++a)
    r += N[static_cast<size_t>(a)] * coord_node_major[static_cast<size_t>(a)][0];
  constexpr double kMinR = 1.0e-12;
  if (!(r > kMinR))
    r = kMinR;
  return r;
}

bool build_bee_at_gp_axisym(const std::string& mesh_type,
                            const std::vector<std::vector<double>>& coord,
                            double xi, double eta,
                            std::vector<std::vector<double>>& bee,
                            std::vector<double>& N,
                            double& detJ,
                            double& radial_r) {
  if (!build_bee_at_gp(mesh_type, coord, xi, eta, bee, N, detJ))
    return false;
  radial_r = radial_weight(N, coord);
  const int nod = static_cast<int>(coord.size());
  const double inv_r = 1.0 / radial_r;
  for (int a = 0; a < nod; ++a) {
    const int ux = 2 * a;
    bee[3][ux] = N[static_cast<size_t>(a)] * inv_r;
  }
  return true;
}

} // namespace slope_element
