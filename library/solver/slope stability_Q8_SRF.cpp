/**
 * Plane strain slope stability — elastic–plastic Mohr–Coulomb, 8-node quadrilaterals,
 * viscoplastic strain method with explicit strength-reduction-factor (SRF) trials.
 *
 * C++ port of `slope_Q8_SRF.f03` (Smith & Griffiths Program 6.4). Same .dat layout as `p64`.
 */
#include "run_programs.hpp"
#include "slope_constitutive.hpp"
#include "slope_element.hpp"
#include "../geom/geom.hpp"
#include "../geom/formnf.hpp"
#include "../io/geotech_dat_io.h"
#include "slope_pseudo_static.h"
#include "groundwater_pore_pressure.hpp"
#include "edge_distributed_load.hpp"
#include "nodal_point_load.hpp"
#include "joint_elements.hpp"
#include "../main/main.hpp"
#include <algorithm>
#include <cmath>
#include <cctype>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <string>
#include <vector>

namespace programs {

namespace {

constexpr double kPi = 3.14159265358979323846;
constexpr double kD180 = 180.0;
constexpr double kZero = 0.0;
constexpr double kOne = 1.0;
constexpr double kTwo = 2.0;
constexpr double kFour = 4.0;
constexpr double kStartDt = 1.0e15;

int clamp_elem_mtype(int etype_val, int np_types) {
  int et = etype_val - 1;
  if (np_types <= 0) return 0;
  if (et < 0) et = 0;
  if (et >= np_types) et = np_types - 1;
  return et;
}

void add_km_BBDB(std::vector<std::vector<double>>& km,
                 const std::vector<std::vector<double>>& bee,
                 const std::vector<std::vector<double>>& dee, double scale, int ndof) {
  constexpr int nst = 4;
  for (int i = 0; i < ndof; ++i)
    for (int j = 0; j < ndof; ++j) {
      double s = kZero;
      for (int a = 0; a < nst; ++a)
        for (int b = 0; b < nst; ++b)
          s += bee[a][i] * dee[a][b] * bee[b][j];
      km[i][j] += s * scale;
    }
}

void matmul_flow_sigma(const std::vector<std::vector<double>>& flow,
                       const std::vector<double>& sigma, std::vector<double>& erate) {
  erate.assign(4, 0.0);
  for (int i = 0; i < 4; ++i)
    for (int j = 0; j < 4; ++j)
      erate[i] += flow[i][j] * sigma[j];
}

void dee_times_vec(const std::vector<std::vector<double>>& dee,
                   const std::vector<double>& v, std::vector<double>& out) {
  out.assign(4, 0.0);
  for (int i = 0; i < 4; ++i)
    for (int j = 0; j < 4; ++j)
      out[i] += dee[i][j] * v[j];
}

/// Integration weight |detJ|×w×r — T3 meshes from the GUI may mix clockwise/counter-clockwise
/// node order; signed detJ would cancel lumped gravity at shared nodes (p610 uses |detJ|).
/// For plane strain, pass radial_r = 1. For axisymmetric (S&G), radial_r = Σ Nᵢ xᵢ.
double gp_weight(double detj, double w, double radial_r = 1.0) {
  return std::abs(detj) * w * radial_r;
}

void accumulate_bload(const std::vector<double>& devp,
                      const std::vector<std::vector<double>>& bee,
                      double det, double w, double radial_r,
                      std::vector<double>& bload, int ndof) {
  constexpr int nst = 4;
  double fac = gp_weight(det, w, radial_r);
  for (int j = 0; j < ndof; ++j) {
    double s = kZero;
    for (int a = 0; a < nst; ++a)
      s += devp[a] * bee[a][j];
    bload[j] += s * fac;
  }
}

std::vector<std::vector<double>> coord_node_major(
    const std::vector<std::vector<double>>& coord, int nod) {
  std::vector<std::vector<double>> cm(static_cast<size_t>(nod), std::vector<double>(2));
  for (int i = 0; i < nod; ++i) {
    cm[static_cast<size_t>(i)][0] = coord[0][static_cast<size_t>(i)];
    cm[static_cast<size_t>(i)][1] = coord[1][static_cast<size_t>(i)];
  }
  return cm;
}

bool eval_bee_at_gp(const std::string& mesh_type,
                    const std::vector<std::vector<double>>& coord,
                    const slope_element::GaussPt2& gp,
                    std::vector<std::vector<double>>& bee,
                    std::vector<double>& fun,
                    double& detj,
                    double& radial_r,
                    int nod,
                    bool axisymmetric) {
  const auto cm = coord_node_major(coord, nod);
  if (axisymmetric)
    return slope_element::build_bee_at_gp_axisym(mesh_type, cm, gp.xi, gp.eta, bee, fun, detj, radial_r);
  radial_r = 1.0;
  return slope_element::build_bee_at_gp(mesh_type, cm, gp.xi, gp.eta, bee, fun, detj);
}

bool resolve_axisymmetric_mode(const std::string& dat_path) {
#ifdef SLOPE_SRF_AXISYM
  (void)dat_path;
  return true;
#else
  const std::string t = geotech_io::read_analysis_option(dat_path, "analysis_type");
  if (slope_element::iequals(t, "axisymmetric") || slope_element::iequals(t, "axisym")) {
    std::cerr << "warning: analysis_type axisymmetric ignored by plane SRF executable; "
                 "use slope_stability_*_SRF_axisym\n";
  }
  return false;
#endif
}

struct SrfStageJson {
  int index = 0;
  double srf = 1.0;
  int iters = 0;
  std::vector<double> ux;
  std::vector<double> uy;
  /// Flat centroid stresses: sigma[comp * nels + el] = sxx,syy,txy,szz per element.
  std::vector<double> sigma;
  /// Flat centroid strains: strain[comp * nels + el] = exx,eyy,gamma_xy per element.
  std::vector<double> strain;
  /// Peak γmax = |ε₁−ε₂| over Gauss points per element (RS2D-style band localization).
  std::vector<double> gamma_max;
  std::vector<double> u_kPa;
  std::vector<double> h_total_m;
  std::vector<double> h_pressure_m;
  std::vector<double> sigma_eff;
  std::vector<double> yield_ratio;
  std::vector<double> plastic_strain;
};

void json_out_double(std::ostream& os, double v) {
  const auto prev_flags = os.flags();
  const auto prev_prec = os.precision();
  os << std::scientific << std::setprecision(17);
  if (std::isfinite(v))
    os << v;
  else
    os << 0.0;
  os.flags(prev_flags);
  os.precision(prev_prec);
}

void recover_element_stresses(
    const std::vector<double>& loads,
    const std::vector<std::vector<double>>& evpt,
    int nels, int nip, int nst, int ndof, int nod,
    const std::vector<std::vector<double>>& prop,
    const std::vector<int>& etype,
    const std::vector<std::vector<double>>& g_coord,
    const std::vector<std::vector<int>>& g_num,
    const std::vector<std::vector<int>>& g_g,
    const std::string& mesh_type,
    const std::vector<slope_element::GaussPt2>& gps,
    std::vector<double>& out_sigma,
    std::vector<double>& out_strain,
    std::vector<double>& out_gamma_max,
    const groundwater::Spec& gw,
    std::vector<double>& out_u,
    std::vector<double>& out_h_total,
    std::vector<double>& out_h_pressure,
    std::vector<double>& out_sigma_eff,
    std::vector<double>& out_yield_ratio,
    std::vector<double>& out_plastic_strain,
    bool axisymmetric) {
  auto evpt_at = [&](int i, int gp, int el) -> double {
    return evpt[i][static_cast<size_t>(gp) + static_cast<size_t>(nip) * static_cast<size_t>(el)];
  };
  out_sigma.assign(static_cast<size_t>(nst * nels), kZero);
  out_strain.assign(static_cast<size_t>(3 * nels), kZero);
  out_gamma_max.assign(static_cast<size_t>(nels), kZero);
  out_u.assign(static_cast<size_t>(nels), kZero);
  out_h_total.assign(static_cast<size_t>(nels), kZero);
  out_h_pressure.assign(static_cast<size_t>(nels), kZero);
  out_sigma_eff.assign(static_cast<size_t>(nst * nels), kZero);
  out_yield_ratio.assign(static_cast<size_t>(nels), kZero);
  out_plastic_strain.assign(static_cast<size_t>(nels), kZero);
  const double pi_loc = std::acos(-1.0);
  std::vector<std::vector<double>> bee(nst, std::vector<double>(ndof));
  std::vector<std::vector<double>> dee(nst, std::vector<double>(nst));
  std::vector<std::vector<double>> coord(2, std::vector<double>(nod));
  std::vector<int> num(nod);
  std::vector<double> eps(nst), sigma_gp(nst), eld(ndof);
  std::vector<double> fun(nod);

  for (int iel = 0; iel < nels; ++iel) {
    int et = clamp_elem_mtype(etype[iel], static_cast<int>(prop[0].size()));
    fem::deemat(dee, prop[4][static_cast<size_t>(et)], prop[5][static_cast<size_t>(et)]);
    for (int i = 0; i < nod; ++i)
      num[i] = g_num[i][iel];
    const int nn = static_cast<int>(g_coord[0].size());
    for (int i = 0; i < nod; ++i) {
      const int ni = num[i] - 1;
      if (ni < 0 || ni >= nn) continue;
      coord[0][i] = g_coord[0][static_cast<size_t>(ni)];
      coord[1][i] = g_coord[1][static_cast<size_t>(ni)];
    }
    std::vector<int> g(ndof);
    for (int i = 0; i < ndof; ++i)
      g[i] = g_g[i][iel];
    for (int i = 0; i < ndof; ++i) {
      int eq = g[i];
      eld[i] = (eq > 0 && eq < static_cast<int>(loads.size())) ? loads[eq] : kZero;
    }

    double avg_sxx = kZero, avg_syy = kZero, avg_txy = kZero, avg_szz = kZero;
    double avg_exx = kZero, avg_eyy = kZero, avg_gxy = kZero;
    double max_gmax = kZero;
    double avg_u = kZero;
    double cx = kZero, cy = kZero;
    for (int i = 0; i < nod; ++i) {
      cx += coord[0][i];
      cy += coord[1][i];
    }
    cx /= static_cast<double>(nod);
    cy /= static_cast<double>(nod);
    for (int gp = 0; gp < nip; ++gp) {
      double detj = kZero;
      double radial_r = 1.0;
      eval_bee_at_gp(mesh_type, coord, gps[static_cast<size_t>(gp)], bee, fun, detj, radial_r, nod, axisymmetric);
      (void)detj;
      (void)radial_r;
      double xg = kZero, yg = kZero;
      for (int m = 0; m < nod; ++m) {
        xg += fun[m] * coord[0][m];
        yg += fun[m] * coord[1][m];
      }
      for (int j = 0; j < nst; ++j) {
        eps[j] = kZero;
        for (int k = 0; k < ndof; ++k)
          eps[j] += bee[j][k] * eld[k];
        eps[j] -= evpt_at(j, gp, iel);
      }
      for (int j = 0; j < nst; ++j) {
        sigma_gp[j] = kZero;
        for (int k = 0; k < nst; ++k)
          sigma_gp[j] += dee[j][k] * eps[k];
      }
      // Ru uses σv — compute sigma_gp before pore_pressure_at so recovered u_kPa is nonzero.
      if (gw.active())
        avg_u += groundwater::pore_pressure_at(xg, yg, gw, sigma_gp.data());
      avg_exx += eps[0];
      avg_eyy += eps[1];
      avg_gxy += eps[2];
      {
        const double gmax_gp = std::sqrt((eps[0] - eps[1]) * (eps[0] - eps[1]) + eps[2] * eps[2]);
        if (gmax_gp > max_gmax)
          max_gmax = gmax_gp;
      }
      avg_sxx += sigma_gp[0];
      avg_syy += sigma_gp[1];
      avg_txy += sigma_gp[2];
      avg_szz += sigma_gp[3];
    }
    const double inv = kOne / static_cast<double>(nip);
    const int o = nst * iel;
    out_sigma[static_cast<size_t>(o + 0)] = avg_sxx * inv;
    out_sigma[static_cast<size_t>(o + 1)] = avg_syy * inv;
    out_sigma[static_cast<size_t>(o + 2)] = avg_txy * inv;
    out_sigma[static_cast<size_t>(o + 3)] = avg_szz * inv;
    const int so = 3 * iel;
    out_strain[static_cast<size_t>(so + 0)] = avg_exx * inv;
    out_strain[static_cast<size_t>(so + 1)] = avg_eyy * inv;
    out_strain[static_cast<size_t>(so + 2)] = avg_gxy * inv;
    out_gamma_max[static_cast<size_t>(iel)] = max_gmax;
    if (gw.active()) {
      out_u[static_cast<size_t>(iel)] = avg_u * inv;
      const double sigma_gp_avg[4] = {
        out_sigma[static_cast<size_t>(o + 0)],
        out_sigma[static_cast<size_t>(o + 1)],
        out_sigma[static_cast<size_t>(o + 2)],
        out_sigma[static_cast<size_t>(o + 3)]};
      out_h_pressure[static_cast<size_t>(iel)] =
          groundwater::pressure_head_at(cx, cy, gw, sigma_gp_avg);
      out_h_total[static_cast<size_t>(iel)] =
          groundwater::total_head_at(cx, cy, gw, sigma_gp_avg);
      double se[4] = {
        out_sigma[static_cast<size_t>(o + 0)],
        out_sigma[static_cast<size_t>(o + 1)],
        out_sigma[static_cast<size_t>(o + 2)],
        out_sigma[static_cast<size_t>(o + 3)]};
      groundwater::apply_effective_stress(se, out_u[static_cast<size_t>(iel)]);
      out_sigma_eff[static_cast<size_t>(o + 0)] = se[0];
      out_sigma_eff[static_cast<size_t>(o + 1)] = se[1];
      out_sigma_eff[static_cast<size_t>(o + 2)] = se[2];
      out_sigma_eff[static_cast<size_t>(o + 3)] = se[3];
    }

    // Mohr–Coulomb yield ratio on Terzaghi effective stress (when available).
    {
      double se[4] = {
        out_sigma[static_cast<size_t>(o + 0)],
        out_sigma[static_cast<size_t>(o + 1)],
        out_sigma[static_cast<size_t>(o + 2)],
        out_sigma[static_cast<size_t>(o + 3)]};
      if (gw.active())
        groundwater::apply_effective_stress(se, out_u[static_cast<size_t>(iel)]);
      const double sm = 0.5 * (se[0] + se[1]);
      const double rad = std::sqrt(0.25 * (se[0] - se[1]) * (se[0] - se[1]) + se[2] * se[2]);
      if (sm <= 0.0) {
        const double phi = prop[0][static_cast<size_t>(et)] * pi_loc / 180.0;
        const double c = prop[1][static_cast<size_t>(et)];
        const double allow = c * std::cos(phi) - sm * std::sin(phi);
        if (allow > 1e-9)
          out_yield_ratio[static_cast<size_t>(iel)] = rad / allow;
      }
    }

    double pn = kZero;
    for (int j = 0; j < nst; ++j) {
      double avg = kZero;
      for (int gp = 0; gp < nip; ++gp)
        avg += evpt_at(j, gp, iel);
      avg /= static_cast<double>(nip);
      pn += avg * avg;
    }
    out_plastic_strain[static_cast<size_t>(iel)] = std::sqrt(pn);
  }
}

void gather_nodal_disp(const std::vector<double>& loads, const std::vector<int>& nf,
                       int nn, int nodof, std::vector<double>& ux, std::vector<double>& uy) {
  ux.assign(static_cast<size_t>(nn), kZero);
  uy.assign(static_cast<size_t>(nn), kZero);
  for (int in = 0; in < nn; ++in) {
    int eqx = nf[in * nodof];
    int eqy = nf[in * nodof + 1];
    if (eqx > 0 && eqx < static_cast<int>(loads.size()))
      ux[static_cast<size_t>(in)] = loads[eqx];
    if (eqy > 0 && eqy < static_cast<int>(loads.size()))
      uy[static_cast<size_t>(in)] = loads[eqy];
  }
}

void write_p64_json(const std::string& base,
                    const std::vector<SrfStageJson>& stages,
                    const std::vector<std::vector<double>>& g_coord,
                    const std::vector<std::vector<int>>& g_num,
                    int nn, int nels, int nod, int nst,
                    bool has_pseudo_static = false,
                    double ps_k_h = 0.0,
                    double ps_k_v = 0.0,
                    const std::vector<double>& ps_fx = {},
                    const std::vector<double>& ps_fy = {},
                    const groundwater::Spec& gw = groundwater::Spec{}) {
  std::ofstream js(base + ".json");
  if (!js) return;
  js.setf(std::ios::showpoint);
  js << std::setprecision(17) << std::scientific;

  js << "{\n  \"solver\":\"p64\","
     << "\n  \"description\":\"Plane-strain slope stability (Q8, Mohr-Coulomb, SRF trials). "
        "stages[] = converged displacement and element_stresses per SRF.\","
     << "\n  \"node_count\":" << nn
     << ",\n  \"element_count\":" << nels
     << ",\n  \"n_stages\":" << stages.size() << ",\n";

  auto write_stage_nodes = [&](const SrfStageJson& st) {
    for (int i = 0; i < nn; ++i) {
      double ux = (i < static_cast<int>(st.ux.size())) ? st.ux[static_cast<size_t>(i)] : kZero;
      double uy = (i < static_cast<int>(st.uy.size())) ? st.uy[static_cast<size_t>(i)] : kZero;
      js << "          {\"id\":" << (i + 1) << ",\"x\":";
      json_out_double(js, g_coord[0][static_cast<size_t>(i)]);
      js << ",\"y\":";
      json_out_double(js, g_coord[1][static_cast<size_t>(i)]);
      js << ",\"ux\":";
      json_out_double(js, ux);
      js << ",\"uy\":";
      json_out_double(js, uy);
      js << "}";
      if (i < nn - 1) js << ",\n";
      else js << "\n";
    }
  };

  js << "  \"nodes\":[\n";
  for (int i = 0; i < nn; ++i) {
    js << "    {\"id\":" << (i + 1) << ",\"x\":";
    json_out_double(js, g_coord[0][static_cast<size_t>(i)]);
    js << ",\"y\":";
    json_out_double(js, g_coord[1][static_cast<size_t>(i)]);
    js << ",\"ux\":0,\"uy\":0}";
    if (i < nn - 1) js << ",\n";
    else js << "\n";
  }
  js << "  ],\n";

  js << "  \"stages\":[\n";
  for (size_t si = 0; si < stages.size(); ++si) {
    const SrfStageJson& st = stages[si];
    js << "    {\"index\":" << st.index
       << ",\"name\":\"srf_" << std::fixed << std::setprecision(2) << st.srf << "\""
       << ",\"subtitle\":\"iters=" << st.iters << ", SRF=" << std::fixed << std::setprecision(2)
       << st.srf << "\""
       << ",\"elements_solid\":" << nels
       << ",\n      \"nodes\":[\n";
    js << std::scientific << std::setprecision(17);
    write_stage_nodes(st);
    js << "      ]";
    const size_t nSig = static_cast<size_t>(nst) * static_cast<size_t>(nels);
    if (st.sigma.size() >= nSig) {
      js << ",\n      \"element_stresses\":[\n";
      for (int e = 0; e < nels; ++e) {
        const int o = nst * e;
        double sxx = st.sigma[static_cast<size_t>(o + 0)];
        double syy = st.sigma[static_cast<size_t>(o + 1)];
        double txy = st.sigma[static_cast<size_t>(o + 2)];
        double szz = st.sigma[static_cast<size_t>(o + 3)];
        double sm = 0.5 * (sxx + syy);
        double sd = 0.5 * (sxx - syy);
        double r = std::sqrt(sd * sd + txy * txy);
        double s1 = sm + r;
        double s2 = sm - r;
        double theta1_deg =
            0.5 * std::atan2(2.0 * txy, sxx - syy) * (kD180 / kPi);
        js << "        {\"id\":" << (e + 1) << ",\"sxx\":";
        json_out_double(js, sxx);
        js << ",\"syy\":";
        json_out_double(js, syy);
        js << ",\"txy\":";
        json_out_double(js, txy);
        js << ",\"szz\":";
        json_out_double(js, szz);
        js << ",\"s1\":";
        json_out_double(js, s1);
        js << ",\"s2\":";
        json_out_double(js, s2);
        js << ",\"theta1_deg\":";
        json_out_double(js, theta1_deg);
        if (st.strain.size() >= static_cast<size_t>(3 * (e + 1))) {
          const int so = 3 * e;
          js << ",\"exx\":";
          json_out_double(js, st.strain[static_cast<size_t>(so + 0)]);
          js << ",\"eyy\":";
          json_out_double(js, st.strain[static_cast<size_t>(so + 1)]);
          js << ",\"gamma_xy\":";
          json_out_double(js, st.strain[static_cast<size_t>(so + 2)]);
        }
        if (st.gamma_max.size() > static_cast<size_t>(e)) {
          js << ",\"gamma_max\":";
          json_out_double(js, st.gamma_max[static_cast<size_t>(e)]);
        }
        // Emit hydrostatic/Ru u and Terzaghi σ' whenever GW fields were recovered
        // (not gated by optional water-pressure-grid export flags).
        if (st.u_kPa.size() > static_cast<size_t>(e) && gw.active()) {
          js << ",\"u_kPa\":";
          json_out_double(js, st.u_kPa[static_cast<size_t>(e)]);
          if (st.sigma_eff.size() >= nSig) {
            js << ",\"sxx_eff_kPa\":";
            json_out_double(js, st.sigma_eff[static_cast<size_t>(o + 0)]);
            js << ",\"syy_eff_kPa\":";
            json_out_double(js, st.sigma_eff[static_cast<size_t>(o + 1)]);
            js << ",\"sxy_eff_kPa\":";
            json_out_double(js, st.sigma_eff[static_cast<size_t>(o + 2)]);
          }
        }
        if (st.h_pressure_m.size() > static_cast<size_t>(e) && gw.static_grid_pressure_head) {
          js << ",\"h_pressure_m\":";
          json_out_double(js, st.h_pressure_m[static_cast<size_t>(e)]);
        }
        if (st.h_total_m.size() > static_cast<size_t>(e) && gw.static_grid_total_head) {
          js << ",\"h_total_m\":";
          json_out_double(js, st.h_total_m[static_cast<size_t>(e)]);
        }
        if (st.yield_ratio.size() > static_cast<size_t>(e)) {
          js << ",\"yield_ratio\":";
          json_out_double(js, st.yield_ratio[static_cast<size_t>(e)]);
        }
        if (st.plastic_strain.size() > static_cast<size_t>(e)) {
          js << ",\"plastic_strain\":";
          json_out_double(js, st.plastic_strain[static_cast<size_t>(e)]);
        }
        js << "}";
        if (e < nels - 1) js << ",\n";
        else js << "\n";
      }
      js << "      ]";
    }
    js << "\n    }";
    if (si + 1 < stages.size()) js << ",\n";
    else js << "\n";
  }
  js << "  ],\n";

  if (has_pseudo_static && ps_fx.size() >= static_cast<size_t>(nn)
      && ps_fy.size() >= static_cast<size_t>(nn)) {
    js << "  \"pseudo_static\":{\"k_h\":";
    json_out_double(js, ps_k_h);
    js << ",\"k_v\":";
    json_out_double(js, ps_k_v);
    js << "},\n  \"pseudo_static_nodal_forces\":[\n";
    for (int i = 0; i < nn; ++i) {
      js << "    {\"id\":" << (i + 1) << ",\"fx\":";
      json_out_double(js, ps_fx[static_cast<size_t>(i)]);
      js << ",\"fy\":";
      json_out_double(js, ps_fy[static_cast<size_t>(i)]);
      js << "}";
      if (i < nn - 1) js << ",\n";
      else js << "\n";
    }
    js << "  ],\n";
  }

  js << "  \"elements\":[\n";
  for (int i = 0; i < nels; ++i) {
    js << "    {\"id\":" << (i + 1);
    for (int k = 0; k < nod; ++k)
      js << ",\"n" << (k + 1) << "\":" << g_num[static_cast<size_t>(k)][static_cast<size_t>(i)];
    js << ",\"active\":1}";
    if (i < nels - 1) js << ",\n";
    else js << "\n";
  }
  js << "  ]\n}\n";
}

bool iequals(const std::string& a, const std::string& b) {
  if (a.size() != b.size()) return false;
  for (size_t i = 0; i < a.size(); ++i)
    if (std::tolower(static_cast<unsigned char>(a[i])) !=
        std::tolower(static_cast<unsigned char>(b[i])))
      return false;
  return true;
}

bool read_srf_controls(std::istream& dat, const geotech_io::AnalysisOptions& opts,
                       double& tol, int& limit, std::vector<double>& srf) {
  if (!(dat >> tol >> limit))
    return false;
  srf.clear();
  if (iequals(opts.srf_mode, "single") && opts.srf_F > 0.0) {
    srf.push_back(opts.srf_F);
    return true;
  }
  int nsrf = 0;
  if (!(dat >> nsrf) || nsrf < 1)
    return false;
  srf.resize(static_cast<size_t>(nsrf));
  for (int i = 0; i < nsrf; ++i)
    if (!(dat >> srf[static_cast<size_t>(i)]))
      return false;
  return true;
}

} // namespace

int run_slope_q8_srf(std::ifstream& dat_in, std::ofstream& res, const std::string& dataname) {
  (void)dat_in;
  const int ndim = 2, nodof = 2, nst = 4, nprops = 6;

  const std::string dat_path = dataname + ".dat";
  geotech_io::AnalysisOptions opts = geotech_io::parse_analysis_options(dat_path);
  const bool axisymmetric = resolve_axisymmetric_mode(dat_path);
  std::string mesh_type = geotech_io::read_analysis_option(dat_path, "mesh_type");
  if (mesh_type.empty()) mesh_type = "Q8";
  for (char& c : mesh_type)
    c = static_cast<char>(std::toupper(static_cast<unsigned char>(c)));
  if (slope_element::iequals(mesh_type, "Q6")) mesh_type = "T6";

  const int nod = slope_element::mesh_nodes_per_element(mesh_type);
  const int ndof = slope_element::mesh_ndof(mesh_type);
  const int nip = static_cast<int>(slope_element::element_gauss_rule(mesh_type).size());
  const bool use_serendipity_q8 = slope_element::iequals(mesh_type, "Q8");
  if (!opts.has_pseudo_static) {
    double kh = 0.0, kv = 0.0;
    if (geotech_io::parse_pseudo_static_coefficients(dat_path, kh, kv)) {
      opts.k_h = kh;
      opts.k_v = kv;
      opts.has_pseudo_static = true;
    }
  }
  std::stringstream dat;
  if (!geotech_io::load_dat_stripped(dat_path, dat)) {
    std::cerr << "Cannot read " << dat_path << "\n";
    return 1;
  }

  bool use_parametric = geotech_io::body_looks_parametric(dat);
  if (opts.has_block && !opts.mesh_source.empty()) {
    if (iequals(opts.mesh_source, "explicit"))
      use_parametric = false;
    else if (iequals(opts.mesh_source, "parametric"))
      use_parametric = true;
  }
  if (!opts.has_block && !use_parametric)
    use_parametric = false;
  if (use_parametric && !use_serendipity_q8) {
    std::cerr << "Parametric embankment mesh requires mesh_type Q8 (got " << mesh_type << ")\n";
    return 1;
  }

  dat.clear();
  dat.seekg(0);
  std::string skip;
  if (dat >> skip) {
    if (!iequals(skip, "p64") && !iequals(skip, "slope_q8_srf") && !iequals(skip, "slope_Q8_SRF"))
      dat.seekg(0);
    else {
      char c = dat.peek();
      while (c == ' ' || c == '\t' || c == '\n') {
        dat.get();
        c = dat.peek();
      }
    }
  } else {
    dat.clear();
    dat.seekg(0);
  }

  int nels = 0, nn = 0, np_types = 0;
  std::vector<std::vector<double>> prop(nprops, std::vector<double>());
  std::vector<int> etype;
  std::vector<int> nf;
  std::vector<int> kdiag;
  std::vector<std::vector<double>> g_coord(ndim, std::vector<double>());
  std::vector<std::vector<int>> g_num(nod, std::vector<int>());
  std::vector<std::vector<int>> g_g(ndof, std::vector<int>());
  std::vector<std::vector<double>> coord(ndim, std::vector<double>(nod));
  std::vector<int> num(nod);
  int neq = 0;

  auto parse_emb_count = [&](const char* key) -> int {
    std::string v = geotech_io::read_analysis_option(dat_path, key);
    if (v.empty()) return 0;
    try {
      return std::stoi(v);
    } catch (...) {
      return 0;
    }
  };
  auto parse_emb_double = [&](const char* key, double def) -> double {
    std::string v = geotech_io::read_analysis_option(dat_path, key);
    if (v.empty()) return def;
    try {
      return std::stod(v);
    } catch (...) {
      return def;
    }
  };

  auto build_embankment_mesh = [&](int nx1, int nx2, int ny1, int ny2, double w1, double s1,
                                   double w2, double h1, double h2) {
    int nye = ny1 + ny2;
    nels = nx1 * nye + ny2 * nx2;
    nn = (3 * nye + 2) * nx1 + 2 * nye + 1 + (3 * ny2 + 2) * nx2;

    nf.assign(static_cast<size_t>(nodof * nn), 0);
    geom::emb_2d_bc(nx1, nx2, ny1, ny2, nf);
    neq = 0;
    for (int v : nf)
      neq = std::max(neq, v);

    kdiag.assign(static_cast<size_t>(neq), 0);
    g_coord[0].assign(static_cast<size_t>(nn), 0.0);
    g_coord[1].assign(static_cast<size_t>(nn), 0.0);
    g_num.assign(nod, std::vector<int>(static_cast<size_t>(nels)));
    g_g.assign(ndof, std::vector<int>(static_cast<size_t>(nels)));

    for (int iel = 0; iel < nels; ++iel) {
      geom::emb_2d_geom(iel + 1, nx1, nx2, ny1, ny2, w1, s1, w2, h1, h2, coord, num);
      std::vector<int> g(ndof);
      fem::num_to_g(num, nf, nodof, nn, g);
      for (int i = 0; i < nod; ++i)
        g_num[static_cast<size_t>(i)][static_cast<size_t>(iel)] = num[static_cast<size_t>(i)];
      for (int i = 0; i < nod; ++i) {
        g_coord[0][static_cast<size_t>(num[static_cast<size_t>(i)] - 1)] = coord[0][static_cast<size_t>(i)];
        g_coord[1][static_cast<size_t>(num[static_cast<size_t>(i)] - 1)] = coord[1][static_cast<size_t>(i)];
      }
      for (int i = 0; i < ndof; ++i)
        g_g[static_cast<size_t>(i)][static_cast<size_t>(iel)] = g[static_cast<size_t>(i)];
      fem::fkdiag(kdiag, g, neq);
    }
  };

  if (use_parametric) {
    double w1, s1, w2, h1, h2;
    int nx1, nx2, ny1, ny2;
    if (!(dat >> w1 >> s1 >> w2 >> h1 >> h2 >> nx1 >> nx2 >> ny1 >> ny2 >> np_types))
      return 1;

    int nye = ny1 + ny2;
    nels = nx1 * nye + ny2 * nx2;
    nn = (3 * nye + 2) * nx1 + 2 * nye + 1 + (3 * ny2 + 2) * nx2;

    prop.assign(nprops, std::vector<double>(static_cast<size_t>(np_types)));
    for (int j = 0; j < np_types; ++j)
      for (int i = 0; i < nprops; ++i)
        dat >> prop[i][j];

    etype.assign(static_cast<size_t>(nels), 1);
    if (np_types > 1) {
      for (int i = 0; i < nels; ++i)
        dat >> etype[static_cast<size_t>(i)];
    }

    build_embankment_mesh(nx1, nx2, ny1, ny2, w1, s1, w2, h1, h2);
  } else {
    if (!(dat >> nels >> nn >> np_types) || nels < 1 || nn < 1 || np_types < 1)
      return 1;

    prop.assign(nprops, std::vector<double>(static_cast<size_t>(np_types)));
    for (int j = 0; j < np_types; ++j)
      for (int i = 0; i < nprops; ++i)
        dat >> prop[i][j];

    etype.assign(static_cast<size_t>(nels), 1);
    if (np_types > 1) {
      for (int i = 0; i < nels; ++i)
        dat >> etype[static_cast<size_t>(i)];
    }

    const int enx1 = parse_emb_count("embankment_nx1");
    const int enx2 = parse_emb_count("embankment_nx2");
    const int eny1 = parse_emb_count("embankment_ny1");
    const int eny2 = parse_emb_count("embankment_ny2");

    if (enx1 > 0 && eny1 > 0) {
      const int nels_file = nels;
      const int nn_file = nn;
      const double w1 = parse_emb_double("embankment_w1", 20.0);
      const double s1 = parse_emb_double("embankment_s1", 20.0);
      const double w2 = parse_emb_double("embankment_w2", 20.0);
      const double h1 = parse_emb_double("embankment_h1", 10.0);
      const double h2 = parse_emb_double("embankment_h2", 5.0);
      build_embankment_mesh(enx1, enx2, eny1, eny2, w1, s1, w2, h1, h2);
      if (nels != nels_file || nn != nn_file) {
        std::cerr << "Explicit mesh size mismatch: file " << nels_file << "/" << nn_file
                  << " vs embankment " << nels << "/" << nn << "\n";
        return 1;
      }
      double sx = 0.0, sy = 0.0;
      for (int i = 0; i < nn_file; ++i)
        dat >> sx >> sy;
      int dummy = 0;
      for (int iel = 0; iel < nels_file; ++iel)
        for (int k = 0; k < nod; ++k)
          dat >> dummy;
      int nr = 0;
      dat >> nr;
      for (int i = 0; i < nr; ++i)
        dat >> dummy >> dummy >> dummy;
    } else {
      g_coord[0].assign(static_cast<size_t>(nn), 0.0);
      g_coord[1].assign(static_cast<size_t>(nn), 0.0);
      for (int i = 0; i < nn; ++i)
        dat >> g_coord[0][static_cast<size_t>(i)] >> g_coord[1][static_cast<size_t>(i)];

      g_num.assign(nod, std::vector<int>(static_cast<size_t>(nels)));
      for (int iel = 0; iel < nels; ++iel)
        for (int k = 0; k < nod; ++k)
          dat >> g_num[static_cast<size_t>(k)][static_cast<size_t>(iel)];

      std::vector<char> node_used(static_cast<size_t>(nn), 0);
      for (int iel = 0; iel < nels; ++iel)
        for (int k = 0; k < nod; ++k) {
          const int ni = g_num[static_cast<size_t>(k)][static_cast<size_t>(iel)] - 1;
          if (ni >= 0 && ni < nn)
            node_used[static_cast<size_t>(ni)] = 1;
        }

      nf.assign(static_cast<size_t>(nodof * nn), 1);
      int nr = 0;
      dat >> nr;
      for (int i = 0; i < nr; ++i) {
        int k = 0;
        dat >> k;
        for (int d = 0; d < nodof; ++d)
          dat >> nf[static_cast<size_t>(d + (k - 1) * nodof)];
      }

      int orphan_nodes = 0;
      for (int in = 0; in < nn; ++in) {
        if (node_used[static_cast<size_t>(in)]) continue;
        ++orphan_nodes;
        for (int d = 0; d < nodof; ++d)
          nf[static_cast<size_t>(d + in * nodof)] = 0;
      }
      if (orphan_nodes > 0)
        res << " Restrained " << orphan_nodes
            << " unused mesh node(s) with no element connectivity.\n";

      geom::formnf(nf, nodof, nn);
      for (int v : nf)
        neq = std::max(neq, v);

      kdiag.assign(static_cast<size_t>(neq), 0);
      g_g.assign(ndof, std::vector<int>(static_cast<size_t>(nels)));
      for (int iel = 0; iel < nels; ++iel) {
        for (int i = 0; i < nod; ++i)
          num[static_cast<size_t>(i)] = g_num[static_cast<size_t>(i)][static_cast<size_t>(iel)];
        std::vector<int> g(ndof);
        fem::num_to_g(num, nf, nodof, nn, g);
        for (int i = 0; i < ndof; ++i)
          g_g[static_cast<size_t>(i)][static_cast<size_t>(iel)] = g[static_cast<size_t>(i)];
        fem::fkdiag(kdiag, g, neq);
      }
    }
  }

  int nlen = static_cast<int>(dataname.size());
  fem::mesh(g_coord, g_num, dataname, nlen, 12);

  const joint_elements::Spec joint_spec = joint_elements::parse_from_dat_path(dat_path);
  std::vector<joint_elements::PlasticState> joint_state;
  if (joint_spec.active())
    joint_state.assign(joint_spec.interfaces.size(), {});
  auto joint_getx = [&](int i) { return joint_elements::node_x_p64(g_coord, i); };
  auto joint_gety = [&](int i) { return joint_elements::node_y_p64(g_coord, i); };
  joint_elements::apply_fkdiag(joint_spec, nf, nodof, nn, kdiag, neq, 1);

  for (int i = 1; i < neq; ++i)
    kdiag[i] += kdiag[i - 1];

  int kv_len = kdiag[neq - 1];
  std::vector<double> kv(kv_len, kZero);

  res << " There are" << neq << " equations and the skyline storage is" << kv_len << "\n";

  const std::vector<slope_element::GaussPt2> gps =
      slope_element::element_gauss_rule(mesh_type);
  res << " mesh_type " << mesh_type << "  nod=" << nod << "  nip=" << nip << "\n";
  if (axisymmetric)
    res << " analysis_type axisymmetric (S&G *r, e_theta=u/r)\n";

  groundwater::Spec gw = groundwater::parse_from_dat_path(dat_path);
  bool gw_effective_stress_yield = true;
  double gw_u_factor = 1.0;
  if (gw.active()) {
    std::string gs = geotech_io::read_analysis_option(dat_path, "groundwater_strength");
    for (char& c : gs)
      c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    gw_effective_stress_yield = (gs != "total");
    const std::string uf = geotech_io::read_analysis_option(dat_path, "groundwater_u_factor");
    if (!uf.empty()) {
      try {
        gw_u_factor = std::stod(uf);
      } catch (...) {
        gw_u_factor = 1.0;
      }
    }
    if (!gw_effective_stress_yield)
      gw_u_factor = 0.0;
  }

  std::vector<std::vector<double>> evpt(
      nst, std::vector<double>(static_cast<size_t>(nip) * static_cast<size_t>(nels)));

  auto evpt_at = [&](int i, int gp, int el) -> double& {
    return evpt[i][static_cast<size_t>(gp) + static_cast<size_t>(nip) * static_cast<size_t>(el)];
  };

  std::vector<std::vector<double>> bee(nst, std::vector<double>(ndof));
  std::vector<std::vector<double>> dee(nst, std::vector<double>(nst));
  std::vector<double> fun(nod);
  std::vector<double> gravlo(neq + 1, kZero);
  std::vector<double> ps_loads(neq + 1, kZero);
  const bool export_ps_forces = opts.has_pseudo_static
      && (opts.k_h != 0.0 || opts.k_v != 0.0);

  for (int iel = 0; iel < nels; ++iel) {
    int et = clamp_elem_mtype(etype[iel], np_types);
    fem::deemat(dee, prop[4][et], prop[5][et]);
    for (int i = 0; i < nod; ++i)
      num[i] = g_num[i][iel];
    for (int i = 0; i < nod; ++i) {
      const int ni = num[i] - 1;
      if (ni < 0 || ni >= nn) continue;
      coord[0][i] = g_coord[0][static_cast<size_t>(ni)];
      coord[1][i] = g_coord[1][static_cast<size_t>(ni)];
    }
    std::vector<int> g(ndof);
    for (int i = 0; i < ndof; ++i)
      g[i] = g_g[i][iel];

    std::vector<std::vector<double>> km(ndof, std::vector<double>(ndof, kZero));
    std::vector<double> eld(ndof, kZero);

    for (int gp = 0; gp < nip; ++gp) {
      double detj = kZero;
      double radial_r = 1.0;
      if (!eval_bee_at_gp(mesh_type, coord, gps[static_cast<size_t>(gp)], bee, fun, detj, radial_r, nod, axisymmetric))
        continue;
      add_km_BBDB(km, bee, dee, gp_weight(detj, gps[static_cast<size_t>(gp)].w, radial_r), ndof);
      for (int m = 0; m < nod; ++m) {
        const double wgp = fun[m] * gp_weight(detj, gps[static_cast<size_t>(gp)].w, radial_r);
        eld[m * nodof] += wgp;
        eld[m * nodof + 1] += wgp;
      }
    }

    fem::fsparv(kv, km, g, kdiag);
    double gamma = prop[3][et];
    const double k_h = opts.has_pseudo_static ? opts.k_h : 0.0;
    const double k_v = opts.has_pseudo_static ? opts.k_v : 0.0;
    if (gw.active()) {
      for (int gp = 0; gp < nip; ++gp) {
        double detj = kZero;
        double radial_r = 1.0;
        eval_bee_at_gp(mesh_type, coord, gps[static_cast<size_t>(gp)], bee, fun, detj, radial_r, nod, axisymmetric);
        double xg = kZero, yg = kZero;
        for (int m = 0; m < nod; ++m) {
          xg += fun[m] * coord[0][m];
          yg += fun[m] * coord[1][m];
        }
        const double g_use = groundwater::body_unit_weight(gamma, et, xg, yg, gw);
        const double wgp = gp_weight(detj, gps[static_cast<size_t>(gp)].w, radial_r);
        for (int m = 0; m < nod; ++m) {
          const int eqx = g[m * nodof];
          const int eqy = g[m * nodof + 1];
          if (eqy > 0 && eqy < static_cast<int>(gravlo.size()))
            gravlo[eqy] -= fun[m] * wgp * g_use;
          if (k_h != 0.0 || k_v != 0.0) {
            if (eqx > 0 && eqx < static_cast<int>(gravlo.size()))
              gravlo[eqx] += fun[m] * wgp * g_use * k_h;
            if (eqy > 0 && eqy < static_cast<int>(gravlo.size()))
              gravlo[eqy] -= fun[m] * wgp * g_use * k_v;
          }
          if (export_ps_forces) {
            if (eqx > 0 && eqx < static_cast<int>(ps_loads.size()))
              ps_loads[eqx] += fun[m] * wgp * g_use * k_h;
            if (eqy > 0 && eqy < static_cast<int>(ps_loads.size()))
              ps_loads[eqy] -= fun[m] * wgp * g_use * k_v;
          }
        }
      }
    } else {
      slope_pseudo_static::apply_element_pseudo_static(gravlo, g, nodof, eld, gamma, k_h, k_v);
      if (export_ps_forces)
        slope_pseudo_static::accumulate_element_pseudo_static_forces(
            ps_loads, g, nodof, eld, gamma, k_h, k_v);
    }
  }
  joint_elements::assemble(joint_spec, joint_getx, joint_gety, nf, nodof, nn, kv, kdiag, 1);

  std::vector<double> ps_fx;
  std::vector<double> ps_fy;
  if (export_ps_forces)
    slope_pseudo_static::gather_nodal_forces_from_global(ps_loads, nf, nn, nodof, ps_fx, ps_fy);

  const edge_distributed_load::Spec edge_spec =
      edge_distributed_load::parse_from_dat_path(dat_path);
  std::vector<double> edge_loads(neq + 1, kZero);
  if (edge_spec.active())
    edge_distributed_load::assemble(edge_spec, nn, nodof, g_coord, nf, edge_loads);

  const nodal_point_load::Spec nodal_spec =
      nodal_point_load::parse_from_dat_path(dat_path);
  std::vector<double> nodal_loads(neq + 1, kZero);
  if (nodal_spec.active())
    nodal_point_load::assemble(nodal_spec, nf, nodof, nodal_loads);

  fem::sparin(kv, kdiag);

  double tol = 0.0;
  int limit = 0;
  std::vector<double> srf;
  if (!read_srf_controls(dat, opts, tol, limit, srf)) {
    std::cerr << "Error reading SRF controls from " << dat_path << "\n";
    return 1;
  }
  if (opts.srf_mode.empty() && srf.size() > 1)
    opts.srf_mode = "multi";

  const std::string failure_opt = geotech_io::read_analysis_option(dat_path, "failure_criterion");
  const slope_constitutive::FailureMode failure_mode =
      slope_constitutive::parse_failure_mode(failure_opt);
  const slope_constitutive::MaterialLaw material_law = slope_constitutive::MaterialLaw::Isotropic;
  if (!failure_opt.empty())
    res << " Failure criterion from AnalysisOptions: " << failure_opt << "\n";
  if (opts.has_pseudo_static)
    res << " Pseudo-static coefficients: k_h=" << opts.k_h << "  k_v=" << opts.k_v << "\n";
  if (gw.active())
    res << groundwater::groundwater_log_line(gw);

  res << "\n    srf    max disp  iters\n";

  std::vector<double> loads(neq + 1, kZero);
  std::vector<double> bdylds(neq + 1, kZero);
  std::vector<double> oldis(neq + 1, kZero);
  std::vector<double> elastic(neq + 1, kZero);

  std::vector<double> eps(nst), sigma(nst), devp(nst), erate(nst), evp(nst);
  std::vector<double> bload(ndof), eld(ndof);
  std::vector<std::vector<double>> m1(4, std::vector<double>(4));
  std::vector<std::vector<double>> m2(4, std::vector<double>(4));
  std::vector<std::vector<double>> m3(4, std::vector<double>(4));
  std::vector<std::vector<double>> flow(4, std::vector<double>(4));
  std::vector<SrfStageJson> json_stages;

  auto push_srf_stage = [&](double srf_val, int iters_count) {
    SrfStageJson st;
    st.index = static_cast<int>(json_stages.size());
    st.srf = srf_val;
    st.iters = iters_count;
    gather_nodal_disp(loads, nf, nn, nodof, st.ux, st.uy);
    recover_element_stresses(loads, evpt, nels, nip, nst, ndof, nod, prop, etype,
                             g_coord, g_num, g_g, mesh_type, gps, st.sigma, st.strain,
                             st.gamma_max, gw, st.u_kPa, st.h_total_m, st.h_pressure_m, st.sigma_eff,
                             st.yield_ratio, st.plastic_strain, axisymmetric);
    json_stages.push_back(std::move(st));
  };

  const int nsrf = static_cast<int>(srf.size());
  for (int iy = 0; iy < nsrf; ++iy) {
    std::vector<std::vector<double>> trial_prop = prop;
    slope_constitutive::apply_strength_reduction(srf[iy], failure_mode, trial_prop);

    double dt = kStartDt;
    for (int ip = 0; ip < np_types; ++ip) {
      double phi_deg = trial_prop[0][ip];
      double phif_rad = phi_deg * kPi / kD180;
      double snph = std::sin(phif_rad);
      double e = trial_prop[4][ip];
      double v = trial_prop[5][ip];
      double ddt = kFour * (kOne + v) * (kOne - kTwo * v) /
                   (e * (kOne - kTwo * v + snph * snph));
      if (ddt < dt)
        dt = ddt;
    }

    int iters = 0;
    std::fill(bdylds.begin(), bdylds.end(), kZero);
    for (size_t k = 0; k < evpt.size(); ++k)
      std::fill(evpt[k].begin(), evpt[k].end(), kZero);
    for (auto& st : joint_state) st = {};
    std::fill(oldis.begin(), oldis.end(), kZero);

    for (;;) {
      double fmax = kZero;
      ++iters;
      for (int i = 0; i <= neq; ++i)
        loads[i] = gravlo[i] + edge_loads[i] + nodal_loads[i] + bdylds[i];
      fem::spabac(kv, loads, kdiag);
      loads[0] = kZero;

      if (iy == 0 && iters == 1) {
        for (int i = 0; i <= neq; ++i)
          elastic[i] = loads[i];
      }

      bool converged = fem::checon(loads, oldis, tol);
      if (iters == 1)
        converged = false;
      if (converged || iters == limit)
        std::fill(bdylds.begin(), bdylds.end(), kZero);

      for (int iel = 0; iel < nels; ++iel) {
        std::fill(bload.begin(), bload.end(), kZero);
        int et = clamp_elem_mtype(etype[iel], np_types);
        double phif = trial_prop[0][et];
        double psif = trial_prop[2][et];
        double cf = trial_prop[1][et];
        double e = trial_prop[4][et];
        double v = trial_prop[5][et];
        fem::deemat(dee, e, v);

        for (int i = 0; i < nod; ++i)
          num[i] = g_num[i][iel];
        for (int i = 0; i < nod; ++i) {
          const int ni = num[i] - 1;
          if (ni < 0 || ni >= nn) continue;
          coord[0][i] = g_coord[0][static_cast<size_t>(ni)];
          coord[1][i] = g_coord[1][static_cast<size_t>(ni)];
        }
        std::vector<int> g(ndof);
        for (int i = 0; i < ndof; ++i)
          g[i] = g_g[i][iel];

        for (int i = 0; i < ndof; ++i) {
          int eq = g[i];
          eld[i] = (eq > 0 && eq < static_cast<int>(loads.size())) ? loads[eq] : kZero;
        }

        for (int gp = 0; gp < nip; ++gp) {
          double detj = kZero;
          double radial_r = 1.0;
          eval_bee_at_gp(mesh_type, coord, gps[static_cast<size_t>(gp)], bee, fun, detj, radial_r, nod, axisymmetric);

          for (int j = 0; j < nst; ++j) {
            eps[j] = kZero;
            for (int k = 0; k < ndof; ++k)
              eps[j] += bee[j][k] * eld[k];
            eps[j] -= evpt_at(j, gp, iel);
          }

          for (int j = 0; j < nst; ++j) {
            sigma[j] = kZero;
            for (int k = 0; k < nst; ++k)
              sigma[j] += dee[j][k] * eps[k];
          }

          double sigm = 0.0, dsbar = 0.0, lode_theta = 0.0;
          double sigm_y = 0.0, dsbar_y = 0.0, lode_y = 0.0;
          if (gw.active() && gw_u_factor > 0.0) {
            double xg = kZero, yg = kZero;
            for (int m = 0; m < nod; ++m) {
              xg += fun[m] * coord[0][m];
              yg += fun[m] * coord[1][m];
            }
            const double u = groundwater::pore_pressure_at(xg, yg, gw, sigma.data());
            std::vector<double> sig_eff(sigma.begin(), sigma.end());
            groundwater::apply_effective_stress(sig_eff.data(), u, gw_u_factor);
            fem::invar(sig_eff, &sigm_y, &dsbar_y, &lode_y);
          } else {
            fem::invar(sigma, &sigm_y, &dsbar_y, &lode_y);
          }
          sigm = sigm_y;
          dsbar = dsbar_y;
          lode_theta = lode_y;
          double f = slope_constitutive::yield_value(
              failure_mode, material_law, phif, cf, sigm, dsbar, lode_theta);
          if (f > fmax)
            fmax = f;

          if (converged || iters == limit) {
            devp = sigma;
          } else {
            if (f >= kZero) {
              double dq1 = 0.0, dq2 = 0.0, dq3 = 0.0;
              slope_constitutive::plastic_potential_derivs(
                  failure_mode, material_law, psif, dsbar, lode_theta, dq1, dq2, dq3);
              fem::formm(sigma, m1, m2, m3);
              for (int r = 0; r < 4; ++r)
                for (int c = 0; c < 4; ++c)
                  flow[r][c] = f * (dq1 * m1[r][c] + dq2 * m2[r][c] + dq3 * m3[r][c]);
              matmul_flow_sigma(flow, sigma, erate);
              for (int j = 0; j < nst; ++j)
                evp[j] = erate[j] * dt;
              for (int j = 0; j < nst; ++j)
                evpt_at(j, gp, iel) += evp[j];
              dee_times_vec(dee, evp, devp);
            }
          }

          if (f >= kZero)
            accumulate_bload(devp, bee, detj, gps[static_cast<size_t>(gp)].w, radial_r, bload, ndof);
        }

        for (int i = 0; i < ndof; ++i)
          if (g[i] != 0)
            bdylds[g[i]] += bload[i];
        bdylds[0] = kZero;
      }
      joint_elements::accumulate_plastic(
          joint_spec, joint_state, joint_getx, joint_gety, nf, nodof, nn, loads, bdylds, dt, 1,
          converged || iters == limit);

      std::cout << "  srf" << std::fixed << std::setprecision(2) << srf[iy]
                << "  iteration" << iters << "  F_max" << std::setprecision(3) << fmax
                << std::endl;

      if (converged || iters == limit) {
        std::vector<double> stage_ux, stage_uy;
        gather_nodal_disp(loads, nf, nn, nodof, stage_ux, stage_uy);
        double maxabs = kZero;
        for (int in = 0; in < nn; ++in) {
          const double ux = stage_ux[static_cast<size_t>(in)];
          const double uy = stage_uy[static_cast<size_t>(in)];
          maxabs = std::max(maxabs, std::sqrt(ux * ux + uy * uy));
        }
        res << std::fixed << std::setprecision(4) << std::setw(8) << srf[iy]
            << std::scientific << std::setprecision(4) << std::setw(12) << maxabs
            << std::fixed << std::setw(5) << iters << "\n";

        push_srf_stage(srf[iy], iters);
        break;
      }
    }
  }

  std::vector<double> diff(neq + 1, kZero);
  for (int i = 1; i <= neq; ++i)
    diff[i] = loads[i] - elastic[i];

  std::vector<std::vector<int>> nf2(nodof, std::vector<int>(nn));
  for (int in = 0; in < nn; ++in) {
    nf2[0][in] = nf[in * nodof];
    nf2[1][in] = nf[in * nodof + 1];
  }

  fem::dismsh(diff, nf2, 0.1, g_coord, g_num, dataname, nlen, 13);
  fem::vecmsh(diff, nf2, g_coord, g_num, dataname, nlen, 14, 0.25);

  if (!json_stages.empty())
    write_p64_json(dataname, json_stages, g_coord, g_num, nn, nels, nod, nst,
                   export_ps_forces, opts.k_h, opts.k_v, ps_fx, ps_fy, gw);

  std::cout << "Results: " << dataname << ".res  and  " << dataname << ".json\n";

  return 0;
}

} // namespace programs
