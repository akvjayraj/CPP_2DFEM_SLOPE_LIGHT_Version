#pragma once

#include <string>
#include <vector>

namespace slope_constitutive {

enum class FailureMode {
    MohrCoulomb,
    DruckerPrager,
    HoekBrown,
    GeneralizedHoekBrown
};

enum class MaterialLaw {
    Isotropic,
    TransverselyIsotropic,
    Orthotropic,
    DuncanChangHyperbolic,
    NonlinearIsotropic,
    ViscoElastic,
    Plastic
};

bool iequals(const std::string& a, const std::string& b);

FailureMode parse_failure_mode(const std::string& token);
MaterialLaw parse_material_law(const std::string& token);
FailureMode effective_failure_mode(FailureMode mode, MaterialLaw law);

void dp_params_from_mohr_coulomb(double phi_deg, double c_kPa, double& alpha, double& k);

double yield_value(FailureMode mode, MaterialLaw law, double phi_deg, double c_kPa,
                   double sigm, double dsbar, double lode);

void plastic_potential_derivs(FailureMode mode, MaterialLaw law, double psi_deg,
                              double dsbar, double lode, double& dq1, double& dq2, double& dq3);

/// Reduce strength parameters for SRF trial F (F >= 1). prop rows: phi,c,psi,...
void apply_strength_reduction(double F, FailureMode mode,
                            std::vector<std::vector<double>>& prop);

bool is_slope_stability_srf_executable(const std::string& token);

} // namespace slope_constitutive
