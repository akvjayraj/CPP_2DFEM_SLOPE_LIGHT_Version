/**
 * Steady seepage programs ported from Smith & Griffiths Chapter 7:
 *   p72 — plane or axisymmetric steady seepage, structured 4-node quad mesh (geom_rect).
 *   p73 — plane free-surface (unconfined) flow, iterative mesh adjustment (geom_freesurf + seep4).
 *   p74 — general two- (plane) or three-dimensional steady seepage, explicit mesh from .dat.
 *
 * Each runner writes a Fortran-faithful {base}.res and a {base}.json with nodal total
 * heads, pressure heads, net nodal flow rates and inflow/outflow sums for the GUI.
 */
#include "common.hpp"
#include "geotech_dat_io.h"
#include "run_programs.hpp"

#include "determinant.hpp"
#include "fkdiag.hpp"
#include "fsparv.hpp"
#include "geom_freesurf.hpp"
#include "geom_rect.hpp"
#include "invert.hpp"
#include "linmul_sky.hpp"
#include "mesh_size.hpp"
#include "checon.hpp"
#include "sample.hpp"
#include "seep4.hpp"
#include "shape_der.hpp"
#include "shape_fun.hpp"
#include "sparin.hpp"
#include "spabac.hpp"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <string>
#include <vector>

namespace programs {

namespace {

const double kPenalty = 1.0e20;

std::string strip_quotes(const std::string& s) {
  std::string r = s;
  while (!r.empty() && (r.front() == '\'' || r.front() == '"')) r.erase(r.begin());
  while (!r.empty() && (r.back() == '\'' || r.back() == '"')) r.pop_back();
  return r;
}

bool iequals_str(const std::string& a, const std::string& b) {
  if (a.size() != b.size()) return false;
  for (size_t i = 0; i < a.size(); ++i)
    if (std::tolower(static_cast<unsigned char>(a[i])) !=
        std::tolower(static_cast<unsigned char>(b[i])))
      return false;
  return true;
}

/// Loads the comment-stripped deck and consumes the leading solver token when present.
bool open_deck(const std::string& dataname, const char* const* aliases, int n_aliases,
               std::stringstream& dat) {
  if (!geotech_io::load_dat_stripped(dataname + ".dat", dat)) {
    std::cerr << "Cannot read " << dataname << ".dat\n";
    return false;
  }
  std::string first;
  std::streampos start = dat.tellg();
  if (dat >> first) {
    bool is_alias = false;
    for (int i = 0; i < n_aliases; ++i)
      if (iequals_str(first, aliases[i])) { is_alias = true; break; }
    if (!is_alias) {
      dat.clear();
      dat.seekg(start);
    }
  } else {
    dat.clear();
    dat.seekg(start);
  }
  return true;
}

void json_num(std::ostream& os, double v) {
  if (!std::isfinite(v)) { os << 0.0; return; }
  char buf[40];
  std::snprintf(buf, sizeof(buf), "%.17g", v);
  os << buf;
}

/// Writes {base}.json with nodal heads / flow rates plus element connectivity.
/// g_coord is ndim x nn (row 0 = x); loads / disps are 1-based (size nn+1).
void write_seepage_json(const std::string& base, const char* solver,
                        int nn, int nels, int nod, int ndim,
                        const std::vector<std::vector<double>>& g_coord,
                        const std::vector<std::vector<int>>& g_num,
                        const std::vector<double>& loads,
                        const std::vector<double>& disps,
                        double inflow, double outflow,
                        int iterations, bool converged) {
  std::ofstream js(base + ".json");
  if (!js) return;
  js << "{\n  \"solver\":\"" << solver << "\","
     << "\n  \"analysis\":\"steady_seepage\","
     << "\n  \"node_count\":" << nn
     << ",\n  \"element_count\":" << nels
     << ",\n  \"element_nodes\":" << nod
     << ",\n  \"ndim\":" << ndim;
  if (iterations > 0)
    js << ",\n  \"iterations\":" << iterations
       << ",\n  \"converged\":" << (converged ? "true" : "false");
  js << ",\n  \"inflow\":";
  json_num(js, inflow);
  js << ",\n  \"outflow\":";
  json_num(js, outflow);
  js << ",\n  \"nodes\":[\n";
  for (int i = 0; i < nn; ++i) {
    const double x = g_coord[0][static_cast<size_t>(i)];
    // For 2D decks row 1 is the elevation used for pressure head; 3D decks use the last row (z).
    const double y = g_coord[static_cast<size_t>(ndim - 1)][static_cast<size_t>(i)];
    const double h = loads[static_cast<size_t>(i + 1)];
    js << "    {\"id\":" << (i + 1) << ",\"x\":";
    json_num(js, x);
    js << ",\"y\":";
    json_num(js, ndim == 2 ? g_coord[1][static_cast<size_t>(i)] : y);
    js << ",\"total_head\":";
    json_num(js, h);
    js << ",\"pressure_head\":";
    json_num(js, h - y);
    js << ",\"flow_rate\":";
    json_num(js, disps[static_cast<size_t>(i + 1)]);
    js << "}" << (i + 1 < nn ? ",\n" : "\n");
  }
  js << "  ],\n  \"elements\":[\n";
  for (int e = 0; e < nels; ++e) {
    js << "    {\"id\":" << (e + 1) << ",\"nodes\":[";
    for (int i = 0; i < nod; ++i) {
      js << g_num[static_cast<size_t>(i)][static_cast<size_t>(e)];
      if (i + 1 < nod) js << ",";
    }
    js << "]}" << (e + 1 < nels ? ",\n" : "\n");
  }
  js << "  ]\n}\n";
}

void write_heads_res(std::ofstream& res, int nn,
                     const std::vector<double>& loads,
                     const std::vector<double>& disps) {
  res << "\n  Node Total Head  Flow rate\n";
  for (int k = 1; k <= nn; ++k) {
    res << std::setw(5) << k
        << std::setw(12) << std::scientific << std::setprecision(4)
        << loads[static_cast<size_t>(k)]
        << std::setw(12) << disps[static_cast<size_t>(k)] << "\n";
  }
}

void sum_flows(const std::vector<double>& disps, int nn, double& inflow, double& outflow) {
  inflow = 0.0;
  outflow = 0.0;
  for (int k = 1; k <= nn; ++k) {
    const double q = disps[static_cast<size_t>(k)];
    if (q > 0.0) inflow += q;
    else if (q < 0.0) outflow += q;
  }
}

/// Reads loaded nodes + fixed freedoms and applies them (shared by p72 / p74).
/// Returns false on malformed input.
bool read_sources_and_fixed_heads(std::stringstream& dat, std::vector<double>& kv,
                                  const std::vector<int>& kdiag, std::vector<double>& loads) {
  int loaded_nodes = 0;
  if (!(dat >> loaded_nodes)) return false;
  for (int i = 0; i < loaded_nodes; ++i) {
    int k;
    double v;
    if (!(dat >> k >> v)) return false;
    loads[static_cast<size_t>(k)] = v;
  }
  int fixed_freedoms = 0;
  if (!(dat >> fixed_freedoms)) return false;
  for (int i = 0; i < fixed_freedoms; ++i) {
    int node;
    double value;
    if (!(dat >> node >> value)) return false;
    const size_t diag = static_cast<size_t>(kdiag[static_cast<size_t>(node - 1)] - 1);
    kv[diag] += kPenalty;
    loads[static_cast<size_t>(node)] = kv[diag] * value;
  }
  return true;
}

} // namespace

// ---------------------------------------------------------------------------
// p72 — plane / axisymmetric steady seepage, structured Q4 mesh
// ---------------------------------------------------------------------------
int run_p72(std::ifstream& dat_in, std::ofstream& res, const std::string& dataname) {
  (void)dat_in;
  static const char* aliases[] = {"p72", "seepage_2d", "steady_seepage_4node", "seepage_plane_axisym"};
  std::stringstream dat;
  if (!open_deck(dataname, aliases, 4, dat)) return 1;

  const int ndim = 2, nod = 4, nip = 4;
  std::string type_2d_raw, dir_raw;
  int nxe = 0, nye = 0, np_types = 0;
  if (!(dat >> type_2d_raw >> dir_raw >> nxe >> nye >> np_types)) {
    std::cerr << "p72: malformed header (expected type_2d dir nxe nye np_types)\n";
    return 1;
  }
  const std::string type_2d = strip_quotes(type_2d_raw);
  const std::string dir_s = strip_quotes(dir_raw);
  const char dir = dir_s.empty() ? 'x' : dir_s[0];
  const bool axisym = iequals_str(type_2d, "axisymmetric");

  int nels = 0, nn = 0;
  geom::mesh_size("quadrilateral", nod, nxe, nye, nels, nn);
  const int neq = nn;

  std::vector<std::vector<double>> prop(ndim, std::vector<double>(static_cast<size_t>(np_types)));
  for (int j = 0; j < np_types; ++j)
    for (int i = 0; i < ndim; ++i)
      if (!(dat >> prop[static_cast<size_t>(i)][static_cast<size_t>(j)])) {
        std::cerr << "p72: malformed permeability table\n";
        return 1;
      }
  std::vector<int> etype(static_cast<size_t>(nels), 1);
  if (np_types > 1)
    for (int e = 0; e < nels; ++e) dat >> etype[static_cast<size_t>(e)];

  std::vector<double> x_coords(static_cast<size_t>(nxe + 1)), y_coords(static_cast<size_t>(nye + 1));
  for (double& v : x_coords) dat >> v;
  for (double& v : y_coords) dat >> v;

  std::vector<std::vector<double>> g_coord(ndim, std::vector<double>(static_cast<size_t>(nn)));
  std::vector<std::vector<int>> g_num(nod, std::vector<int>(static_cast<size_t>(nels)));
  std::vector<std::vector<double>> coord(ndim, std::vector<double>(nod));
  std::vector<int> num(nod);
  std::vector<int> kdiag(static_cast<size_t>(neq), 0);

  for (int iel = 1; iel <= nels; ++iel) {
    geom::geom_rect("quadrilateral", iel, x_coords, y_coords, coord, num, dir);
    for (int i = 0; i < nod; ++i) {
      g_num[static_cast<size_t>(i)][static_cast<size_t>(iel - 1)] = num[i];
      g_coord[0][static_cast<size_t>(num[i] - 1)] = coord[0][static_cast<size_t>(i)];
      g_coord[1][static_cast<size_t>(num[i] - 1)] = coord[1][static_cast<size_t>(i)];
    }
    fem::fkdiag(kdiag, num, neq);
  }
  programs::cumulate_kdiag(kdiag, neq);

  std::vector<double> kv(static_cast<size_t>(kdiag[static_cast<size_t>(neq - 1)]), 0.0);
  std::vector<double> kvh(kv.size(), 0.0);
  res << "There are" << std::setw(5) << neq
      << " equations and the skyline storage is" << std::setw(6) << kv.size() << "\n";

  std::vector<std::vector<double>> points(nip, std::vector<double>(ndim));
  std::vector<double> weights(nip);
  fem::sample("quadrilateral", points, &weights);

  std::vector<std::vector<double>> der(ndim, std::vector<double>(nod));
  std::vector<std::vector<double>> deriv(ndim, std::vector<double>(nod));
  std::vector<std::vector<double>> jac(ndim, std::vector<double>(ndim));
  std::vector<std::vector<double>> ecoord(nod, std::vector<double>(ndim));
  std::vector<std::vector<double>> kp(nod, std::vector<double>(nod));
  std::vector<double> fun(nod);

  for (int iel = 0; iel < nels; ++iel) {
    const int et = etype[static_cast<size_t>(iel)];
    double kay[2] = {prop[0][static_cast<size_t>(et - 1)], prop[1][static_cast<size_t>(et - 1)]};
    for (int i = 0; i < nod; ++i) {
      num[i] = g_num[static_cast<size_t>(i)][static_cast<size_t>(iel)];
      ecoord[static_cast<size_t>(i)][0] = g_coord[0][static_cast<size_t>(num[i] - 1)];
      ecoord[static_cast<size_t>(i)][1] = g_coord[1][static_cast<size_t>(num[i] - 1)];
    }
    for (int i = 0; i < nod; ++i)
      for (int j = 0; j < nod; ++j) kp[static_cast<size_t>(i)][static_cast<size_t>(j)] = 0.0;

    for (int ig = 1; ig <= nip; ++ig) {
      fem::shape_der(der, points, ig);
      fem::shape_fun(fun, points, ig);
      for (int i = 0; i < ndim; ++i)
        for (int j = 0; j < ndim; ++j) {
          jac[static_cast<size_t>(i)][static_cast<size_t>(j)] = 0.0;
          for (int k = 0; k < nod; ++k)
            jac[static_cast<size_t>(i)][static_cast<size_t>(j)] +=
                der[static_cast<size_t>(i)][static_cast<size_t>(k)] *
                ecoord[static_cast<size_t>(k)][static_cast<size_t>(j)];
        }
      const double det = fem::determinant(jac);
      fem::invert(jac);
      for (int i = 0; i < ndim; ++i)
        for (int j = 0; j < nod; ++j) {
          deriv[static_cast<size_t>(i)][static_cast<size_t>(j)] = 0.0;
          for (int k = 0; k < ndim; ++k)
            deriv[static_cast<size_t>(i)][static_cast<size_t>(j)] +=
                jac[static_cast<size_t>(i)][static_cast<size_t>(k)] *
                der[static_cast<size_t>(k)][static_cast<size_t>(j)];
        }
      double gc = 1.0;
      if (axisym) {
        gc = 0.0;
        for (int k = 0; k < nod; ++k)
          gc += fun[static_cast<size_t>(k)] * ecoord[static_cast<size_t>(k)][0];
      }
      for (int i = 0; i < nod; ++i)
        for (int j = 0; j < nod; ++j) {
          double sum = 0.0;
          for (int p = 0; p < ndim; ++p)
            sum += deriv[static_cast<size_t>(p)][static_cast<size_t>(i)] * kay[p] *
                   deriv[static_cast<size_t>(p)][static_cast<size_t>(j)];
          kp[static_cast<size_t>(i)][static_cast<size_t>(j)] +=
              sum * det * weights[static_cast<size_t>(ig - 1)] * gc;
        }
    }
    fem::fsparv(kv, kp, num, kdiag);
  }
  kvh = kv;

  std::vector<double> loads(static_cast<size_t>(neq + 1), 0.0);
  if (!read_sources_and_fixed_heads(dat, kv, kdiag, loads)) {
    std::cerr << "p72: malformed boundary condition block\n";
    return 1;
  }

  fem::sparin(kv, kdiag);
  fem::spabac(kv, loads, kdiag);

  std::vector<double> disps(static_cast<size_t>(neq + 1), 0.0);
  fem::linmul_sky(kvh, loads, disps, kdiag);

  write_heads_res(res, nn, loads, disps);
  double inflow = 0.0, outflow = 0.0;
  sum_flows(disps, nn, inflow, outflow);
  res << "\n       Inflow      Outflow\n     "
      << std::setw(12) << std::scientific << std::setprecision(4) << inflow
      << std::setw(12) << outflow << "\n";

  write_seepage_json(dataname, "p72", nn, nels, nod, ndim, g_coord, g_num,
                     loads, disps, inflow, outflow, 0, true);
  return 0;
}

// ---------------------------------------------------------------------------
// p73 — plane free-surface (unconfined) flow, iterative mesh adjustment
// ---------------------------------------------------------------------------
int run_p73(std::ifstream& dat_in, std::ofstream& res, const std::string& dataname) {
  (void)dat_in;
  static const char* aliases[] = {"p73", "seepage_free_surface", "flow_free_surface"};
  std::stringstream dat;
  if (!open_deck(dataname, aliases, 3, dat)) return 1;

  const int ndim = 2, nod = 4;
  const double pi = std::acos(-1.0);
  int nxe = 0, nye = 0, limit = 0, np_types = 0;
  int physical_orientation = 1;  // +1: upstream max-x; -1: upstream min-x.
  double physical_x_origin = 0.0;
  double tol = 0.0;
  std::string header_line;
  std::getline(dat >> std::ws, header_line);
  std::stringstream header(header_line);
  if (!(header >> nxe >> nye >> tol >> limit >> np_types)) {
    std::cerr << "p73: malformed header (expected nxe nye tol limit np_types)\n";
    return 1;
  }
  // Extended GUI deck: optional physical orientation and x origin. Legacy S&G decks
  // contain only the first five values and retain their original max-x upstream convention.
  if (!(header >> physical_orientation)) physical_orientation = 1;
  if (!(header >> physical_x_origin)) physical_x_origin = 0.0;
  physical_orientation = physical_orientation < 0 ? -1 : 1;
  const int nels = nxe * nye;
  const int nn = (nxe + 1) * (nels / nxe + 1);
  const int neq = nn;

  std::vector<std::vector<double>> prop(ndim, std::vector<double>(static_cast<size_t>(np_types)));
  for (int j = 0; j < np_types; ++j)
    for (int i = 0; i < ndim; ++i) dat >> prop[static_cast<size_t>(i)][static_cast<size_t>(j)];
  std::vector<int> etype(static_cast<size_t>(nels), 1);
  if (np_types > 1)
    for (int e = 0; e < nels; ++e) dat >> etype[static_cast<size_t>(e)];

  std::vector<double> bottom_width(static_cast<size_t>(nxe + 1));
  std::vector<double> top_width(static_cast<size_t>(nxe + 1));
  for (double& v : bottom_width) dat >> v;
  for (double& v : top_width) dat >> v;
  double initial_height = 0.0;
  dat >> initial_height;

  std::vector<double> surf(static_cast<size_t>(nxe + 1), initial_height);
  std::vector<double> angs(static_cast<size_t>(nxe + 1));
  for (int i = 0; i <= nxe; ++i)
    angs[static_cast<size_t>(i)] =
        std::atan(surf[static_cast<size_t>(i)] /
                  (top_width[static_cast<size_t>(i)] - bottom_width[static_cast<size_t>(i)])) *
        180.0 / pi;

  double hup = 0.0, hdown = 0.0;
  int fixed_up = 0, fixed_down = 0;
  dat >> hup >> fixed_up;
  std::vector<int> node_up(static_cast<size_t>(fixed_up));
  for (int& v : node_up) dat >> v;
  dat >> hdown >> fixed_down;
  std::vector<int> node_down(static_cast<size_t>(fixed_down));
  for (int& v : node_down) dat >> v;
  const int fixed_seep = nels / nxe - fixed_down;
  std::vector<int> node_seep(static_cast<size_t>(fixed_seep));
  for (int i = 1; i <= fixed_seep; ++i)
    node_seep[static_cast<size_t>(i - 1)] = i * (nxe + 1) + 1;

  std::vector<std::vector<double>> coord(ndim, std::vector<double>(nod));
  std::vector<int> num(nod);
  std::vector<int> kdiag(static_cast<size_t>(neq), 0);
  for (int iel = 1; iel <= nels; ++iel) {
    geom::geom_freesurf(iel, nxe, fixed_seep, fixed_down, hdown, bottom_width, angs, surf,
                        coord, num);
    fem::fkdiag(kdiag, num, neq);
  }
  programs::cumulate_kdiag(kdiag, neq);

  std::vector<double> kv(static_cast<size_t>(kdiag[static_cast<size_t>(neq - 1)]), 0.0);
  std::vector<double> kvh(kv.size(), 0.0);
  res << "There are " << std::setw(5) << neq
      << "  equations and the skyline storage is " << std::setw(6) << kv.size() << "\n";

  std::vector<std::vector<double>> g_coord(ndim, std::vector<double>(static_cast<size_t>(nn)));
  std::vector<std::vector<int>> g_num(nod, std::vector<int>(static_cast<size_t>(nels)));
  std::vector<std::vector<double>> ecoord(nod, std::vector<double>(ndim));
  std::vector<std::vector<double>> kp(nod, std::vector<double>(nod));
  std::vector<std::vector<double>> kay(ndim, std::vector<double>(ndim));
  std::vector<double> loads(static_cast<size_t>(neq + 1), 0.0);
  std::vector<double> oldpot(static_cast<size_t>(neq + 1), 0.0);

  int iters = 0;
  bool converged = false;
  for (;;) {
    ++iters;
    for (double& v : kv) v = 0.0;
    for (int iel = 1; iel <= nels; ++iel) {
      const int et = etype[static_cast<size_t>(iel - 1)];
      kay[0][0] = prop[0][static_cast<size_t>(et - 1)];
      kay[1][1] = prop[1][static_cast<size_t>(et - 1)];
      kay[0][1] = kay[1][0] = 0.0;
      geom::geom_freesurf(iel, nxe, fixed_seep, fixed_down, hdown, bottom_width, angs, surf,
                          coord, num);
      for (int i = 0; i < nod; ++i) {
        g_num[static_cast<size_t>(i)][static_cast<size_t>(iel - 1)] = num[i];
        g_coord[0][static_cast<size_t>(num[i] - 1)] = coord[0][static_cast<size_t>(i)];
        g_coord[1][static_cast<size_t>(num[i] - 1)] = coord[1][static_cast<size_t>(i)];
        ecoord[static_cast<size_t>(i)][0] = coord[0][static_cast<size_t>(i)];
        ecoord[static_cast<size_t>(i)][1] = coord[1][static_cast<size_t>(i)];
      }
      fem::seep4(kp, ecoord, kay);
      fem::fsparv(kv, kp, num, kdiag);
    }
    kvh = kv;

    for (double& v : loads) v = 0.0;
    for (int i = 0; i < fixed_up; ++i) {
      const int nd = node_up[static_cast<size_t>(i)];
      const size_t diag = static_cast<size_t>(kdiag[static_cast<size_t>(nd - 1)] - 1);
      kv[diag] += kPenalty;
      loads[static_cast<size_t>(nd)] = kv[diag] * hup;
    }
    for (int i = 0; i < fixed_down; ++i) {
      const int nd = node_down[static_cast<size_t>(i)];
      const size_t diag = static_cast<size_t>(kdiag[static_cast<size_t>(nd - 1)] - 1);
      kv[diag] += kPenalty;
      loads[static_cast<size_t>(nd)] = kv[diag] * hdown;
    }
    for (int i = 1; i <= fixed_seep; ++i) {
      const int nd = node_seep[static_cast<size_t>(i - 1)];
      const size_t diag = static_cast<size_t>(kdiag[static_cast<size_t>(nd - 1)] - 1);
      kv[diag] += kPenalty;
      loads[static_cast<size_t>(nd)] =
          kv[diag] * (hdown + (surf[0] - hdown) * (fixed_seep + 1 - i) / (fixed_seep + 1));
    }

    fem::sparin(kv, kdiag);
    fem::spabac(kv, loads, kdiag);
    for (int i = 1; i <= nxe; ++i)
      surf[static_cast<size_t>(i - 1)] = loads[static_cast<size_t>(i)];

    converged = fem::checon(loads, oldpot, tol);
    if (converged || iters == limit) break;
  }

  std::vector<double> disps(static_cast<size_t>(neq + 1), 0.0);
  fem::linmul_sky(kvh, loads, disps, kdiag);

  write_heads_res(res, nn, loads, disps);
  double inflow = 0.0, outflow = 0.0;
  sum_flows(disps, nn, inflow, outflow);
  // Fortran p73 prints (negative sum, positive sum) under "Inflow Outflow"; kept verbatim.
  res << "\n       Inflow      Outflow\n     "
      << std::setw(12) << std::scientific << std::setprecision(4) << outflow
      << std::setw(12) << inflow << "\n";
  res << "\n Converged in" << std::setw(3) << iters << " iterations\n";

  // p73's free-surface geometry is formulated with downstream on the internal left.
  // When the user places Upstream on physical min-x, the GUI mirrors materials into
  // this canonical system; map solved coordinates back to the user's physical model.
  const double domain_width = std::max(
      *std::max_element(bottom_width.begin(), bottom_width.end()),
      *std::max_element(top_width.begin(), top_width.end()));
  for (double& x : g_coord[0]) {
    x = physical_orientation < 0
        ? physical_x_origin + domain_width - x
        : physical_x_origin + x;
  }

  write_seepage_json(dataname, "p73", nn, nels, nod, ndim, g_coord, g_num,
                     loads, disps, inflow, outflow, iters, converged);
  return 0;
}

// ---------------------------------------------------------------------------
// p74 — general 2D (plane) or 3D steady seepage, explicit mesh
// ---------------------------------------------------------------------------
int run_p74(std::ifstream& dat_in, std::ofstream& res, const std::string& dataname) {
  (void)dat_in;
  static const char* aliases[] = {"p74", "seepage_general", "seepage_general_2d", "seepage_2d_3d"};
  std::stringstream dat;
  if (!open_deck(dataname, aliases, 4, dat)) return 1;

  std::string element_raw;
  int nod = 0, nels = 0, nn = 0, nip = 0, ndim = 0, np_types = 0;
  if (!(dat >> element_raw >> nod >> nels >> nn >> nip >> ndim >> np_types)) {
    std::cerr << "p74: malformed header (expected element nod nels nn nip ndim np_types)\n";
    return 1;
  }
  const std::string element = strip_quotes(element_raw);
  const int neq = nn;

  std::vector<std::vector<double>> prop(ndim, std::vector<double>(static_cast<size_t>(np_types)));
  for (int j = 0; j < np_types; ++j)
    for (int i = 0; i < ndim; ++i) dat >> prop[static_cast<size_t>(i)][static_cast<size_t>(j)];
  std::vector<int> etype(static_cast<size_t>(nels), 1);
  if (np_types > 1)
    for (int e = 0; e < nels; ++e) dat >> etype[static_cast<size_t>(e)];

  std::vector<std::vector<double>> g_coord(ndim, std::vector<double>(static_cast<size_t>(nn)));
  for (int n = 0; n < nn; ++n)
    for (int i = 0; i < ndim; ++i)
      if (!(dat >> g_coord[static_cast<size_t>(i)][static_cast<size_t>(n)])) {
        std::cerr << "p74: malformed nodal coordinate table\n";
        return 1;
      }
  std::vector<std::vector<int>> g_num(nod, std::vector<int>(static_cast<size_t>(nels)));
  for (int e = 0; e < nels; ++e)
    for (int i = 0; i < nod; ++i)
      if (!(dat >> g_num[static_cast<size_t>(i)][static_cast<size_t>(e)])) {
        std::cerr << "p74: malformed element connectivity table\n";
        return 1;
      }

  std::vector<int> kdiag(static_cast<size_t>(neq), 0);
  std::vector<int> num(nod);
  for (int e = 0; e < nels; ++e) {
    for (int i = 0; i < nod; ++i) num[i] = g_num[static_cast<size_t>(i)][static_cast<size_t>(e)];
    fem::fkdiag(kdiag, num, neq);
  }
  programs::cumulate_kdiag(kdiag, neq);

  std::vector<double> kv(static_cast<size_t>(kdiag[static_cast<size_t>(neq - 1)]), 0.0);
  std::vector<double> kvh(kv.size(), 0.0);
  res << " There are" << std::setw(5) << neq
      << " equations and the skyline storage is" << std::setw(6) << kv.size() << "\n";

  std::vector<std::vector<double>> points(nip, std::vector<double>(ndim));
  std::vector<double> weights(nip);
  fem::sample(element, points, &weights);

  std::vector<std::vector<double>> der(ndim, std::vector<double>(nod));
  std::vector<std::vector<double>> deriv(ndim, std::vector<double>(nod));
  std::vector<std::vector<double>> jac(ndim, std::vector<double>(ndim));
  std::vector<std::vector<double>> ecoord(nod, std::vector<double>(ndim));
  std::vector<std::vector<double>> kp(nod, std::vector<double>(nod));

  for (int e = 0; e < nels; ++e) {
    const int et = etype[static_cast<size_t>(e)];
    for (int i = 0; i < nod; ++i) {
      num[i] = g_num[static_cast<size_t>(i)][static_cast<size_t>(e)];
      for (int j = 0; j < ndim; ++j)
        ecoord[static_cast<size_t>(i)][static_cast<size_t>(j)] =
            g_coord[static_cast<size_t>(j)][static_cast<size_t>(num[i] - 1)];
    }
    for (int i = 0; i < nod; ++i)
      for (int j = 0; j < nod; ++j) kp[static_cast<size_t>(i)][static_cast<size_t>(j)] = 0.0;

    for (int ig = 1; ig <= nip; ++ig) {
      fem::shape_der(der, points, ig);
      for (int i = 0; i < ndim; ++i)
        for (int j = 0; j < ndim; ++j) {
          jac[static_cast<size_t>(i)][static_cast<size_t>(j)] = 0.0;
          for (int k = 0; k < nod; ++k)
            jac[static_cast<size_t>(i)][static_cast<size_t>(j)] +=
                der[static_cast<size_t>(i)][static_cast<size_t>(k)] *
                ecoord[static_cast<size_t>(k)][static_cast<size_t>(j)];
        }
      const double det = fem::determinant(jac);
      fem::invert(jac);
      for (int i = 0; i < ndim; ++i)
        for (int j = 0; j < nod; ++j) {
          deriv[static_cast<size_t>(i)][static_cast<size_t>(j)] = 0.0;
          for (int k = 0; k < ndim; ++k)
            deriv[static_cast<size_t>(i)][static_cast<size_t>(j)] +=
                jac[static_cast<size_t>(i)][static_cast<size_t>(k)] *
                der[static_cast<size_t>(k)][static_cast<size_t>(j)];
        }
      for (int i = 0; i < nod; ++i)
        for (int j = 0; j < nod; ++j) {
          double sum = 0.0;
          for (int p = 0; p < ndim; ++p)
            sum += deriv[static_cast<size_t>(p)][static_cast<size_t>(i)] *
                   prop[static_cast<size_t>(p)][static_cast<size_t>(et - 1)] *
                   deriv[static_cast<size_t>(p)][static_cast<size_t>(j)];
          kp[static_cast<size_t>(i)][static_cast<size_t>(j)] +=
              sum * det * weights[static_cast<size_t>(ig - 1)];
        }
    }
    fem::fsparv(kv, kp, num, kdiag);
  }
  kvh = kv;

  std::vector<double> loads(static_cast<size_t>(neq + 1), 0.0);
  if (!read_sources_and_fixed_heads(dat, kv, kdiag, loads)) {
    std::cerr << "p74: malformed boundary condition block\n";
    return 1;
  }

  fem::sparin(kv, kdiag);
  fem::spabac(kv, loads, kdiag);

  std::vector<double> disps(static_cast<size_t>(neq + 1), 0.0);
  fem::linmul_sky(kvh, loads, disps, kdiag);

  write_heads_res(res, nn, loads, disps);
  double inflow = 0.0, outflow = 0.0;
  sum_flows(disps, nn, inflow, outflow);
  res << "\n       Inflow      Outflow\n     "
      << std::setw(12) << std::scientific << std::setprecision(4) << inflow
      << std::setw(12) << outflow << "\n";

  write_seepage_json(dataname, "p74", nn, nels, nod, ndim, g_coord, g_num,
                     loads, disps, inflow, outflow, 0, true);
  return 0;
}

} // namespace programs
