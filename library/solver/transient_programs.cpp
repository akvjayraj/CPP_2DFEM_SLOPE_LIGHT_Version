/**
 * Transient consolidation programs ported from Smith & Griffiths Chapter 8:
 *   p84 — plane or axisymmetric θ-method, structured 4-node quad mesh (geom_rect).
 *   p88 — general 2D/3D θ-method consolidation on an explicit mesh.
 *
 * Each runner writes a Fortran-faithful {base}.res and a {base}.json with nodal
 * pressures (as total_head / pressure_head) for the GUI.
 */
#include "common.hpp"
#include "geotech_dat_io.h"
#include "run_programs.hpp"

#include "cross_product.hpp"
#include "determinant.hpp"
#include "fkdiag.hpp"
#include "fsparv.hpp"
#include "geom_rect.hpp"
#include "invert.hpp"
#include "linmul_sky.hpp"
#include "mesh_size.hpp"
#include "sample.hpp"
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

struct TransientMonitorSample {
  int step = 0;
  double time = 0.0;
  double pressure = 0.0;
};

struct TransientFrame {
  int step = 0;
  double time = 0.0;
  std::vector<double> pressures; // node 1..nn at indices 0..nn-1
};

bool want_spatial_frame(int j, int nstep, int npri, int ntime) {
  if (j == 0 || j == nstep) return true;
  if (ntime > 0 && j == ntime) return true;
  if (npri > 0 && (j / npri) * npri == j) return true;
  return false;
}

void capture_spatial_frame(std::vector<TransientFrame>& frames, int step, double time,
                           const std::vector<double>& loads, int nn) {
  if (!frames.empty() && frames.back().step == step) return;
  TransientFrame frame;
  frame.step = step;
  frame.time = time;
  frame.pressures.resize(static_cast<size_t>(nn));
  for (int i = 0; i < nn; ++i)
    frame.pressures[static_cast<size_t>(i)] = loads[static_cast<size_t>(i + 1)];
  frames.push_back(std::move(frame));
}

void write_transient_json(const std::string& base, const char* solver,
                          int nn, int nels, int nod, int ndim,
                          const std::vector<std::vector<double>>& g_coord,
                          const std::vector<std::vector<int>>& g_num,
                          const std::vector<double>& loads,
                          int nstep, double dtim, double theta,
                          int npri, int nres, int ntime,
                          const std::vector<TransientMonitorSample>& history,
                          const std::vector<TransientFrame>& frames) {
  std::ofstream js(base + ".json");
  if (!js) return;
  js << "{\n  \"solver\":\"" << solver << "\","
     << "\n  \"analysis\":\"transient_consolidation\","
     << "\n  \"node_count\":" << nn
     << ",\n  \"element_count\":" << nels
     << ",\n  \"element_nodes\":" << nod
     << ",\n  \"ndim\":" << ndim
     << ",\n  \"nstep\":" << nstep
     << ",\n  \"dtim\":";
  json_num(js, dtim);
  js << ",\n  \"theta\":";
  json_num(js, theta);
  js << ",\n  \"npri\":" << npri
     << ",\n  \"nres\":" << nres
     << ",\n  \"ntime\":" << ntime
     << ",\n  \"monitor_node\":" << nres
     << ",\n  \"inflow\":0,\n  \"outflow\":0,\n  \"monitor_history\":[\n";
  for (size_t i = 0; i < history.size(); ++i) {
    const auto& s = history[i];
    js << "    {\"step\":" << s.step << ",\"time\":";
    json_num(js, s.time);
    js << ",\"pressure\":";
    json_num(js, s.pressure);
    js << "}" << (i + 1 < history.size() ? ",\n" : "\n");
  }
  js << "  ],\n  \"steps\":[\n";
  for (size_t f = 0; f < frames.size(); ++f) {
    const auto& frame = frames[f];
    js << "    {\"step\":" << frame.step << ",\"time\":";
    json_num(js, frame.time);
    js << ",\"pressures\":[";
    for (size_t i = 0; i < frame.pressures.size(); ++i) {
      json_num(js, frame.pressures[i]);
      if (i + 1 < frame.pressures.size()) js << ",";
    }
    js << "]}" << (f + 1 < frames.size() ? ",\n" : "\n");
  }
  // Backward-compatible final nodal snapshot (pressure aliased as head fields).
  js << "  ],\n  \"nodes\":[\n";
  for (int i = 0; i < nn; ++i) {
    const double x = g_coord[0][static_cast<size_t>(i)];
    const double y = g_coord[1][static_cast<size_t>(i)];
    const double p = loads[static_cast<size_t>(i + 1)];
    js << "    {\"id\":" << (i + 1) << ",\"x\":";
    json_num(js, x);
    js << ",\"y\":";
    json_num(js, y);
    js << ",\"total_head\":";
    json_num(js, p);
    js << ",\"pressure_head\":";
    json_num(js, p);
    js << ",\"flow_rate\":0}" << (i + 1 < nn ? ",\n" : "\n");
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

void assemble_transient_element(
    const std::string& element, int nod, int nip, int ndim, bool axisym,
    const std::vector<std::vector<double>>& points,
    const std::vector<double>& weights,
    const std::vector<std::vector<double>>& ecoord,
    const double* kay,
    std::vector<std::vector<double>>& kc,
    std::vector<std::vector<double>>& mm) {
  std::vector<std::vector<double>> der(ndim, std::vector<double>(nod));
  std::vector<std::vector<double>> deriv(ndim, std::vector<double>(nod));
  std::vector<std::vector<double>> jac(ndim, std::vector<double>(ndim));
  std::vector<std::vector<double>> ntn(nod, std::vector<double>(nod));
  std::vector<double> fun(nod);

  for (int i = 0; i < nod; ++i)
    for (int j = 0; j < nod; ++j) {
      kc[static_cast<size_t>(i)][static_cast<size_t>(j)] = 0.0;
      mm[static_cast<size_t>(i)][static_cast<size_t>(j)] = 0.0;
    }

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
    const double w = weights[static_cast<size_t>(ig - 1)] * det * gc;
    for (int i = 0; i < nod; ++i)
      for (int j = 0; j < nod; ++j) {
        double sum = 0.0;
        for (int p = 0; p < ndim; ++p)
          sum += deriv[static_cast<size_t>(p)][static_cast<size_t>(i)] * kay[p] *
                 deriv[static_cast<size_t>(p)][static_cast<size_t>(j)];
        kc[static_cast<size_t>(i)][static_cast<size_t>(j)] += sum * w;
      }
    fem::cross_product(fun, fun, ntn);
    for (int i = 0; i < nod; ++i)
      for (int j = 0; j < nod; ++j)
        mm[static_cast<size_t>(i)][static_cast<size_t>(j)] +=
            ntn[static_cast<size_t>(i)][static_cast<size_t>(j)] * w;
  }
}

} // namespace

// ---------------------------------------------------------------------------
// p84 — plane / axisymmetric transient consolidation, structured Q4
// ---------------------------------------------------------------------------
int run_p84(std::ifstream& dat_in, std::ofstream& res, const std::string& dataname) {
  (void)dat_in;
  static const char* aliases[] = {"p84", "transient_4node", "transient_plane"};
  std::stringstream dat;
  if (!open_deck(dataname, aliases, 3, dat)) return 1;

  const int ndim = 2, nod = 4, nip = 4;
  std::string type_2d_raw, dir_raw;
  int nxe = 0, nye = 0, np_types = 0;
  if (!(dat >> type_2d_raw >> dir_raw >> nxe >> nye >> np_types)) {
    std::cerr << "p84: malformed header (expected type_2d dir nxe nye np_types)\n";
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
        std::cerr << "p84: malformed property table\n";
        return 1;
      }
  std::vector<int> etype(static_cast<size_t>(nels), 1);
  if (np_types > 1)
    for (int e = 0; e < nels; ++e) dat >> etype[static_cast<size_t>(e)];

  std::vector<double> x_coords(static_cast<size_t>(nxe + 1)), y_coords(static_cast<size_t>(nye + 1));
  for (double& v : x_coords) dat >> v;
  for (double& v : y_coords) dat >> v;

  double dtim = 0.0, theta = 0.0;
  int nstep = 0, npri = 0, nres = 0, ntime = 0;
  if (!(dat >> dtim >> nstep >> theta >> npri >> nres >> ntime)) {
    std::cerr << "p84: malformed time-control line\n";
    return 1;
  }

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
  std::vector<double> bp(kv.size(), 0.0);
  res << " There are" << std::setw(5) << neq
      << " equations and the skyline storage is" << std::setw(5) << kv.size() << "\n";

  std::vector<std::vector<double>> points(nip, std::vector<double>(ndim));
  std::vector<double> weights(nip);
  fem::sample("quadrilateral", points, &weights);

  std::vector<std::vector<double>> ecoord(nod, std::vector<double>(ndim));
  std::vector<std::vector<double>> kc(nod, std::vector<double>(nod));
  std::vector<std::vector<double>> mm(nod, std::vector<double>(nod));

  for (int iel = 0; iel < nels; ++iel) {
    const int et = etype[static_cast<size_t>(iel)];
    double kay[2] = {prop[0][static_cast<size_t>(et - 1)], prop[1][static_cast<size_t>(et - 1)]};
    for (int i = 0; i < nod; ++i) {
      num[i] = g_num[static_cast<size_t>(i)][static_cast<size_t>(iel)];
      ecoord[static_cast<size_t>(i)][0] = g_coord[0][static_cast<size_t>(num[i] - 1)];
      ecoord[static_cast<size_t>(i)][1] = g_coord[1][static_cast<size_t>(num[i] - 1)];
    }
    assemble_transient_element("quadrilateral", nod, nip, ndim, axisym, points, weights,
                               ecoord, kay, kc, mm);
    fem::fsparv(kv, kc, num, kdiag);
    fem::fsparv(bp, mm, num, kdiag);
  }

  // kv <- θ Δt K; bp <- M + θ Δt K; kv <- M - (1-θ) Δt K  (= bp - kv/θ)
  for (double& v : kv) v *= theta * dtim;
  for (size_t i = 0; i < bp.size(); ++i) bp[i] += kv[i];
  if (std::abs(theta) > 1e-30) {
    for (size_t i = 0; i < kv.size(); ++i) kv[i] = bp[i] - kv[i] / theta;
  } else {
    for (size_t i = 0; i < kv.size(); ++i) kv[i] = bp[i];
  }

  std::vector<double> loads(static_cast<size_t>(neq + 1), 0.0);
  std::vector<double> newlo(static_cast<size_t>(neq + 1), 0.0);
  for (int i = 1; i <= nn; ++i) {
    if (!(dat >> loads[static_cast<size_t>(i)])) {
      std::cerr << "p84: malformed initial loads\n";
      return 1;
    }
  }

  int fixed_freedoms = 0;
  if (!(dat >> fixed_freedoms)) {
    std::cerr << "p84: missing fixed_freedoms\n";
    return 1;
  }
  std::vector<int> node(static_cast<size_t>(std::max(fixed_freedoms, 0)));
  std::vector<double> value(static_cast<size_t>(std::max(fixed_freedoms, 0)));
  std::vector<double> storbp(static_cast<size_t>(std::max(fixed_freedoms, 0)));
  for (int i = 0; i < fixed_freedoms; ++i) {
    if (!(dat >> node[static_cast<size_t>(i)] >> value[static_cast<size_t>(i)])) {
      std::cerr << "p84: malformed fixed freedoms\n";
      return 1;
    }
    const size_t diag = static_cast<size_t>(kdiag[static_cast<size_t>(node[static_cast<size_t>(i)] - 1)] - 1);
    bp[diag] += kPenalty;
    storbp[static_cast<size_t>(i)] = bp[diag];
  }

  fem::sparin(bp, kdiag);

  std::vector<TransientMonitorSample> history;
  std::vector<TransientFrame> frames;
  auto push_monitor = [&](int step, double time) {
    history.push_back(TransientMonitorSample{
        step, time, loads[static_cast<size_t>(std::max(1, std::min(nres, nn)))]});
  };

  res << "\n    Time      Pressure (node" << std::setw(3) << nres << ")\n";
  res << std::setw(12) << std::scientific << std::setprecision(4) << 0.0
      << std::setw(12) << loads[static_cast<size_t>(nres)] << "\n";
  push_monitor(0, 0.0);
  if (want_spatial_frame(0, nstep, npri, ntime))
    capture_spatial_frame(frames, 0, 0.0, loads, nn);

  for (int j = 1; j <= nstep; ++j) {
    const double time = j * dtim;
    fem::linmul_sky(kv, loads, newlo, kdiag);
    for (int i = 0; i < fixed_freedoms; ++i)
      newlo[static_cast<size_t>(node[static_cast<size_t>(i)])] =
          storbp[static_cast<size_t>(i)] * value[static_cast<size_t>(i)];
    fem::spabac(bp, newlo, kdiag);
    loads = newlo;
    if (j == ntime) {
      int nci = 0;
      dat >> nci; // consume contour intervals (contour file optional)
      (void)nci;
    }
    if (npri > 0 && j / npri * npri == j) {
      res << std::setw(12) << std::scientific << std::setprecision(4) << time
          << std::setw(12) << loads[static_cast<size_t>(nres)] << "\n";
      push_monitor(j, time);
    }
    if (want_spatial_frame(j, nstep, npri, ntime))
      capture_spatial_frame(frames, j, time, loads, nn);
  }
  if (history.empty() || history.back().step != nstep)
    push_monitor(nstep, nstep * dtim);

  write_transient_json(dataname, "p84", nn, nels, nod, ndim, g_coord, g_num, loads,
                       nstep, dtim, theta, npri, nres, ntime, history, frames);
  return 0;
}

// ---------------------------------------------------------------------------
// p88 — general 2D/3D transient consolidation, explicit mesh
// ---------------------------------------------------------------------------
int run_p88(std::ifstream& dat_in, std::ofstream& res, const std::string& dataname) {
  (void)dat_in;
  static const char* aliases[] = {"p88", "consolidation_general", "consolidation_general_2d"};
  std::stringstream dat;
  if (!open_deck(dataname, aliases, 3, dat)) return 1;

  std::string element_raw;
  int nels = 0, nn = 0, nip = 0, nod = 0, ndim = 0, np_types = 0;
  // Fortran: element, nels, nn, nip, nod, ndim, np_types
  if (!(dat >> element_raw >> nels >> nn >> nip >> nod >> ndim >> np_types)) {
    std::cerr << "p88: malformed header (expected element nels nn nip nod ndim np_types)\n";
    return 1;
  }
  const std::string element = strip_quotes(element_raw);
  const int neq = nn;

  std::vector<std::vector<double>> prop(ndim, std::vector<double>(static_cast<size_t>(np_types)));
  for (int j = 0; j < np_types; ++j)
    for (int i = 0; i < ndim; ++i)
      if (!(dat >> prop[static_cast<size_t>(i)][static_cast<size_t>(j)])) {
        std::cerr << "p88: malformed property table\n";
        return 1;
      }
  std::vector<int> etype(static_cast<size_t>(nels), 1);
  if (np_types > 1)
    for (int e = 0; e < nels; ++e) dat >> etype[static_cast<size_t>(e)];

  std::vector<std::vector<double>> g_coord(ndim, std::vector<double>(static_cast<size_t>(nn)));
  for (int i = 0; i < nn; ++i)
    for (int d = 0; d < ndim; ++d)
      if (!(dat >> g_coord[static_cast<size_t>(d)][static_cast<size_t>(i)])) {
        std::cerr << "p88: malformed coordinates\n";
        return 1;
      }

  std::vector<std::vector<int>> g_num(nod, std::vector<int>(static_cast<size_t>(nels)));
  for (int e = 0; e < nels; ++e)
    for (int i = 0; i < nod; ++i)
      if (!(dat >> g_num[static_cast<size_t>(i)][static_cast<size_t>(e)])) {
        std::cerr << "p88: malformed connectivity\n";
        return 1;
      }

  double dtim = 0.0, theta = 0.0;
  int nstep = 0, npri = 0, nres = 0, ntime = 0, nci = 0;
  if (!(dat >> dtim >> nstep >> theta >> npri >> nres)) {
    std::cerr << "p88: malformed time-control line\n";
    return 1;
  }
  if (ndim == 2 && nod == 4) {
    if (!(dat >> ntime >> nci)) {
      std::cerr << "p88: missing ntime nci for 2D Q4\n";
      return 1;
    }
  }

  std::vector<int> num(nod);
  std::vector<int> kdiag(static_cast<size_t>(neq), 0);
  for (int iel = 0; iel < nels; ++iel) {
    for (int i = 0; i < nod; ++i)
      num[i] = g_num[static_cast<size_t>(i)][static_cast<size_t>(iel)];
    fem::fkdiag(kdiag, num, neq);
  }
  programs::cumulate_kdiag(kdiag, neq);

  std::vector<double> kv(static_cast<size_t>(kdiag[static_cast<size_t>(neq - 1)]), 0.0);
  std::vector<double> bp(kv.size(), 0.0);
  res << " There are" << std::setw(5) << neq
      << "  equations and the skyline storage is " << std::setw(5) << kv.size() << "\n";

  std::vector<std::vector<double>> points(nip, std::vector<double>(ndim));
  std::vector<double> weights(nip);
  fem::sample(element, points, &weights);

  std::vector<std::vector<double>> ecoord(nod, std::vector<double>(ndim));
  std::vector<std::vector<double>> kc(nod, std::vector<double>(nod));
  std::vector<std::vector<double>> mm(nod, std::vector<double>(nod));
  std::vector<double> kay_vec(static_cast<size_t>(ndim));

  for (int iel = 0; iel < nels; ++iel) {
    const int et = etype[static_cast<size_t>(iel)];
    for (int d = 0; d < ndim; ++d)
      kay_vec[static_cast<size_t>(d)] = prop[static_cast<size_t>(d)][static_cast<size_t>(et - 1)];
    for (int i = 0; i < nod; ++i) {
      num[i] = g_num[static_cast<size_t>(i)][static_cast<size_t>(iel)];
      for (int d = 0; d < ndim; ++d)
        ecoord[static_cast<size_t>(i)][static_cast<size_t>(d)] =
            g_coord[static_cast<size_t>(d)][static_cast<size_t>(num[i] - 1)];
    }
    assemble_transient_element(element, nod, nip, ndim, false, points, weights,
                               ecoord, kay_vec.data(), kc, mm);
    fem::fsparv(kv, kc, num, kdiag);
    fem::fsparv(bp, mm, num, kdiag);
  }

  for (double& v : kv) v *= theta * dtim;
  for (size_t i = 0; i < bp.size(); ++i) bp[i] += kv[i];
  if (std::abs(theta) > 1e-30) {
    for (size_t i = 0; i < kv.size(); ++i) kv[i] = bp[i] - kv[i] / theta;
  } else {
    for (size_t i = 0; i < kv.size(); ++i) kv[i] = bp[i];
  }

  std::vector<double> loads(static_cast<size_t>(neq + 1), 0.0);
  std::vector<double> newlo(static_cast<size_t>(neq + 1), 0.0);
  for (int i = 1; i <= nn; ++i) {
    if (!(dat >> loads[static_cast<size_t>(i)])) {
      std::cerr << "p88: malformed initial loads\n";
      return 1;
    }
  }

  int fixed_freedoms = 0;
  if (!(dat >> fixed_freedoms)) {
    std::cerr << "p88: missing fixed_freedoms\n";
    return 1;
  }
  std::vector<int> node(static_cast<size_t>(std::max(fixed_freedoms, 0)));
  std::vector<double> value(static_cast<size_t>(std::max(fixed_freedoms, 0)));
  std::vector<double> storbp(static_cast<size_t>(std::max(fixed_freedoms, 0)));
  for (int i = 0; i < fixed_freedoms; ++i) {
    if (!(dat >> node[static_cast<size_t>(i)] >> value[static_cast<size_t>(i)])) {
      std::cerr << "p88: malformed fixed freedoms\n";
      return 1;
    }
    const size_t diag = static_cast<size_t>(kdiag[static_cast<size_t>(node[static_cast<size_t>(i)] - 1)] - 1);
    bp[diag] += kPenalty;
    storbp[static_cast<size_t>(i)] = bp[diag];
  }

  fem::sparin(bp, kdiag);

  std::vector<TransientMonitorSample> history;
  std::vector<TransientFrame> frames;
  auto push_monitor = [&](int step, double time) {
    history.push_back(TransientMonitorSample{
        step, time, loads[static_cast<size_t>(std::max(1, std::min(nres, nn)))]});
  };

  res << "\n    Time      Pressure (node" << std::setw(3) << nres << ")\n";
  res << std::setw(12) << std::scientific << std::setprecision(4) << 0.0
      << std::setw(12) << loads[static_cast<size_t>(nres)] << "\n";
  push_monitor(0, 0.0);
  if (want_spatial_frame(0, nstep, npri, ntime))
    capture_spatial_frame(frames, 0, 0.0, loads, nn);

  for (int j = 1; j <= nstep; ++j) {
    const double time = j * dtim;
    fem::linmul_sky(kv, loads, newlo, kdiag);
    for (int i = 0; i < fixed_freedoms; ++i)
      newlo[static_cast<size_t>(node[static_cast<size_t>(i)])] =
          storbp[static_cast<size_t>(i)] * value[static_cast<size_t>(i)];
    fem::spabac(bp, newlo, kdiag);
    loads = newlo;
    (void)nci;
    if (npri > 0 && j / npri * npri == j) {
      res << std::setw(12) << std::scientific << std::setprecision(4) << time
          << std::setw(12) << loads[static_cast<size_t>(nres)] << "\n";
      push_monitor(j, time);
    }
    if (want_spatial_frame(j, nstep, npri, ntime))
      capture_spatial_frame(frames, j, time, loads, nn);
  }
  if (history.empty() || history.back().step != nstep)
    push_monitor(nstep, nstep * dtim);

  // GUI expects 2D x/y; for 3D decks emit x and z as y for display.
  if (ndim == 3) {
    std::vector<std::vector<double>> g2(2, std::vector<double>(static_cast<size_t>(nn)));
    for (int i = 0; i < nn; ++i) {
      g2[0][static_cast<size_t>(i)] = g_coord[0][static_cast<size_t>(i)];
      g2[1][static_cast<size_t>(i)] = g_coord[2][static_cast<size_t>(i)];
    }
    write_transient_json(dataname, "p88", nn, nels, nod, 2, g2, g_num, loads,
                         nstep, dtim, theta, npri, nres, ntime, history, frames);
  } else {
    write_transient_json(dataname, "p88", nn, nels, nod, ndim, g_coord, g_num, loads,
                         nstep, dtim, theta, npri, nres, ntime, history, frames);
  }
  return 0;
}

} // namespace programs
