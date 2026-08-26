#pragma once

#include <string>
#include <vector>

namespace slope_element {

bool iequals(const std::string& a, const std::string& b);

int mesh_nodes_per_element(const std::string& mesh_type);
int mesh_ndof(const std::string& mesh_type);

struct GaussPt2 {
  double xi{0.0};
  double eta{0.0};
  double w{0.0};
};

std::vector<GaussPt2> element_gauss_rule(const std::string& mesh_type);

void shape_derivatives(const std::string& mesh_type, double xi, double eta,
                       std::vector<double>& N,
                       std::vector<double>& dN_dxi,
                       std::vector<double>& dN_deta);

bool build_bee_at_gp(const std::string& mesh_type,
                     const std::vector<std::vector<double>>& coord,
                     double xi, double eta,
                     std::vector<std::vector<double>>& bee,
                     std::vector<double>& N,
                     double& detJ);

/// Plane B (rows 0–2) plus hoop row εθ = uᵣ/r (S&G axisymmetric).
/// On success, sets radial_r = Σ Nᵢ xᵢ (clamped) for ×r integration weight.
bool build_bee_at_gp_axisym(const std::string& mesh_type,
                            const std::vector<std::vector<double>>& coord,
                            double xi, double eta,
                            std::vector<std::vector<double>>& bee,
                            std::vector<double>& N,
                            double& detJ,
                            double& radial_r);

/// r = Σ Nᵢ xᵢ at a Gauss point (x = radial). Clamped away from zero.
double radial_weight(const std::vector<double>& N,
                     const std::vector<std::vector<double>>& coord_node_major);

} // namespace slope_element
