#include "slope_constitutive.hpp"
#include "../main/main.hpp"
#include <cmath>
#include <cctype>

namespace slope_constitutive {

bool iequals(const std::string& a, const std::string& b)
{
    if (a.size() != b.size()) return false;
    for (size_t i = 0; i < a.size(); ++i)
        if (std::tolower(static_cast<unsigned char>(a[i])) !=
            std::tolower(static_cast<unsigned char>(b[i])))
            return false;
    return true;
}

FailureMode parse_failure_mode(const std::string& token)
{
    if (iequals(token, "HoekBrown")) return FailureMode::HoekBrown;
    if (iequals(token, "GeneralizedHoekBrown")) return FailureMode::GeneralizedHoekBrown;
    if (iequals(token, "DruckerPrager")) return FailureMode::DruckerPrager;
    return FailureMode::MohrCoulomb;
}

MaterialLaw parse_material_law(const std::string& token)
{
    if (iequals(token, "TransverselyIsotropic")) return MaterialLaw::TransverselyIsotropic;
    if (iequals(token, "Orthotropic")) return MaterialLaw::Orthotropic;
    if (iequals(token, "DuncanChangHyperbolic")) return MaterialLaw::DuncanChangHyperbolic;
    if (iequals(token, "NonlinearIsotropic")) return MaterialLaw::NonlinearIsotropic;
    if (iequals(token, "ViscoElastic")) return MaterialLaw::ViscoElastic;
    if (iequals(token, "Plastic")) return MaterialLaw::Plastic;
    return MaterialLaw::Isotropic;
}

FailureMode effective_failure_mode(FailureMode mode, MaterialLaw law)
{
    if (law == MaterialLaw::Plastic && mode == FailureMode::MohrCoulomb)
        return FailureMode::DruckerPrager;
    return mode;
}

void dp_params_from_mohr_coulomb(double phi_deg, double c_kPa, double& alpha, double& k)
{
    const double pi = std::acos(-1.0);
    const double phir = phi_deg * pi / 180.0;
    const double s = std::sin(phir);
    const double c = std::cos(phir);
    const double den = std::sqrt(3.0) * (3.0 - s);
    alpha = (2.0 * s) / den;
    k = (6.0 * c_kPa * c) / den;
}

double yield_value(FailureMode mode, MaterialLaw law, double phi_deg, double c_kPa,
                   double sigm, double dsbar, double lode)
{
    mode = effective_failure_mode(mode, law);
    if (mode == FailureMode::DruckerPrager) {
        double alpha = 0.0, k = 0.0;
        dp_params_from_mohr_coulomb(phi_deg, c_kPa, alpha, k);
        return dsbar + alpha * sigm - k;
    }
    if (mode == FailureMode::HoekBrown || mode == FailureMode::GeneralizedHoekBrown) {
        const double sigma_ci = std::max(c_kPa, 1.0);
        const double mb = std::max(std::tan(phi_deg * std::acos(-1.0) / 180.0), 1e-6);
        const double s = 1.0;
        const double a = (mode == FailureMode::GeneralizedHoekBrown) ? 0.6 : 0.5;
        const double p_comp = std::max(-sigm, 0.0);
        const double q_allow = sigma_ci * std::pow(mb * (p_comp / sigma_ci) + s, a);
        (void)lode;
        return dsbar - q_allow;
    }
    return fem::mocouf(phi_deg, c_kPa, sigm, dsbar, lode);
}

void plastic_potential_derivs(FailureMode mode, MaterialLaw law, double psi_deg,
                              double dsbar, double lode, double& dq1, double& dq2, double& dq3)
{
    mode = effective_failure_mode(mode, law);
    if (mode == FailureMode::DruckerPrager) {
        double alpha_g = 0.0, k_dummy = 0.0;
        dp_params_from_mohr_coulomb(psi_deg, 0.0, alpha_g, k_dummy);
        dq1 = alpha_g;
        dq2 = (std::abs(dsbar) > 1e-12) ? (1.0 / dsbar) : 0.0;
        dq3 = 0.0;
        return;
    }
    if (mode == FailureMode::HoekBrown || mode == FailureMode::GeneralizedHoekBrown) {
        double alpha_g = 0.0, k_dummy = 0.0;
        dp_params_from_mohr_coulomb(psi_deg, 0.0, alpha_g, k_dummy);
        dq1 = alpha_g;
        dq2 = (std::abs(dsbar) > 1e-12) ? (1.0 / dsbar) : 0.0;
        dq3 = 0.0;
        (void)lode;
        return;
    }
    fem::mocouq(psi_deg, dsbar, lode, dq1, dq2, dq3);
}

void apply_strength_reduction(double F, FailureMode mode,
                            std::vector<std::vector<double>>& prop)
{
    if (F <= 0.0 || F >= 1.0e6 || prop.empty() || prop[0].empty())
        return;

    const double pi = std::acos(-1.0);
    const int np_types = static_cast<int>(prop[0].size());

    for (int m = 0; m < np_types; ++m) {
        const double phi_r = prop[0][m] * pi / 180.0;
        const double psi_r = (prop.size() > 2) ? prop[2][m] * pi / 180.0 : 0.0;
        prop[0][m] = std::atan(std::tan(phi_r) / F) * 180.0 / pi;
        if (prop.size() > 2)
            prop[2][m] = std::atan(std::tan(psi_r) / F) * 180.0 / pi;
        prop[1][m] = prop[1][m] / F;
        (void)mode; // DP/HB use reduced phi,c proxies at yield evaluation
    }
}

bool is_slope_stability_srf_executable(const std::string& token)
{
    const std::string pre = "slope_stability_";
    if (token.compare(0, pre.size(), pre) != 0) return false;
    const std::string suf_axisym = "_SRF_axisym";
    const std::string suf = "_SRF";
    if (token.size() > pre.size() + suf_axisym.size()
        && token.compare(token.size() - suf_axisym.size(), suf_axisym.size(), suf_axisym) == 0)
        return true;
    if (token.size() > pre.size() + suf.size()
        && token.compare(token.size() - suf.size(), suf.size(), suf) == 0)
        return true;
    return false;
}

} // namespace slope_constitutive
