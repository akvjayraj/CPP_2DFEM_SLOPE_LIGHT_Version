/**
 * Program 6.4 — Plane strain slope stability, elastic–plastic Mohr–Coulomb,
 * 8-node quadrilaterals, viscoplastic strain method (Smith & Griffiths).
 */
#include "run_programs.hpp"
#include "../geom/geom.hpp"
#include "../main/main.hpp"
#include <algorithm>
#include <cmath>
#include <iomanip>
#include <iostream>
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

void add_km_BBDB(std::vector<std::vector<double>>& km,
                 const std::vector<std::vector<double>>& bee,
                 const std::vector<std::vector<double>>& dee, double scale) {
  constexpr int ndof = 16;
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

void accumulate_bload(const std::vector<double>& devp,
                      const std::vector<std::vector<double>>& bee,
                      double det, double w, std::vector<double>& bload) {
  constexpr int ndof = 16;
  constexpr int nst = 4;
  double fac = det * w;
  for (int j = 0; j < ndof; ++j) {
    double s = kZero;
    for (int a = 0; a < nst; ++a)
      s += devp[a] * bee[a][j];
    bload[j] += s * fac;
  }
}

} // namespace

int run_p64(std::ifstream& dat, std::ofstream& res, const std::string& dataname) {
  (void)dataname;
  const int ndim = 2, nodof = 2, nod = 8, ndof = 16, nip = 4, nst = 4, nprops = 6;

  double w1, s1, w2, h1, h2;
  int nx1, nx2, ny1, ny2, np_types;
  if (!(dat >> w1 >> s1 >> w2 >> h1 >> h2 >> nx1 >> nx2 >> ny1 >> ny2 >> np_types))
    return 1;

  int nye = ny1 + ny2;
  int nels = nx1 * nye + ny2 * nx2;
  int nn = (3 * nye + 2) * nx1 + 2 * nye + 1 + (3 * ny2 + 2) * nx2;

  std::vector<std::vector<double>> prop(nprops, std::vector<double>(np_types));
  for (int j = 0; j < np_types; ++j)
    for (int i = 0; i < nprops; ++i)
      dat >> prop[i][j];

  std::vector<int> etype(nels, 1);
  if (np_types > 1) {
    for (int i = 0; i < nels; ++i)
      dat >> etype[i];
  }

  std::vector<int> nf(nodof * nn, 0);
  geom::emb_2d_bc(nx1, nx2, ny1, ny2, nf);

  int neq = 0;
  for (int v : nf)
    neq = std::max(neq, v);

  std::vector<int> kdiag(neq, 0);
  std::vector<std::vector<double>> g_coord(ndim, std::vector<double>(nn));
  std::vector<std::vector<int>> g_num(nod, std::vector<int>(nels));
  std::vector<std::vector<int>> g_g(ndof, std::vector<int>(nels));
  std::vector<std::vector<double>> coord(ndim, std::vector<double>(nod));
  std::vector<int> num(nod);

  for (int iel = 0; iel < nels; ++iel) {
    geom::emb_2d_geom(iel + 1, nx1, nx2, ny1, ny2, w1, s1, w2, h1, h2, coord, num);
    std::vector<int> g(ndof);
    fem::num_to_g(num, nf, nodof, nn, g);
    for (int i = 0; i < nod; ++i)
      g_num[i][iel] = num[i];
    for (int i = 0; i < nod; ++i) {
      g_coord[0][num[i] - 1] = coord[0][i];
      g_coord[1][num[i] - 1] = coord[1][i];
    }
    for (int i = 0; i < ndof; ++i)
      g_g[i][iel] = g[i];
    fem::fkdiag(kdiag, g, neq);
  }

  int nlen = static_cast<int>(dataname.size());
  fem::mesh(g_coord, g_num, dataname, nlen, 12);

  for (int i = 1; i < neq; ++i)
    kdiag[i] += kdiag[i - 1];

  int kv_len = kdiag[neq - 1];
  std::vector<double> kv(kv_len, kZero);

  res << " There are" << neq << " equations and the skyline storage is" << kv_len << "\n";

  std::string element = "quadrilateral";
  std::vector<std::vector<double>> points(nip, std::vector<double>(ndim));
  std::vector<double> weights(nip);
  fem::sample(element, points, &weights);

  std::vector<std::vector<double>> evpt(
      nst, std::vector<double>(static_cast<size_t>(nip) * static_cast<size_t>(nels)));

  auto evpt_at = [&](int i, int gp, int el) -> double& {
    return evpt[i][static_cast<size_t>(gp) + static_cast<size_t>(nip) * static_cast<size_t>(el)];
  };

  std::vector<std::vector<double>> bee(nst, std::vector<double>(ndof));
  std::vector<std::vector<double>> dee(nst, std::vector<double>(nst));
  std::vector<double> fun(nod);
  std::vector<double> gravlo(neq + 1, kZero);

  for (int iel = 0; iel < nels; ++iel) {
    int et = etype[iel] - 1;
    fem::deemat(dee, prop[4][et], prop[5][et]);
    for (int i = 0; i < nod; ++i)
      num[i] = g_num[i][iel];
    for (int i = 0; i < nod; ++i) {
      coord[0][i] = g_coord[0][num[i] - 1];
      coord[1][i] = g_coord[1][num[i] - 1];
    }
    std::vector<int> g(ndof);
    for (int i = 0; i < ndof; ++i)
      g[i] = g_g[i][iel];

    std::vector<std::vector<double>> km(ndof, std::vector<double>(ndof, kZero));
    std::vector<double> eld(ndof, kZero);

    for (int gp = 0; gp < nip; ++gp) {
      fem::shape_fun(fun, points, gp + 1);
      double detj = kZero;
      fem::bee8(bee, coord, points[gp][0], points[gp][1], detj);
      add_km_BBDB(km, bee, dee, detj * weights[gp]);
      for (int m = 0; m < nod; ++m) {
        int iy = (m + 1) * nodof - 1;
        eld[iy] += fun[m] * detj * weights[gp];
      }
    }

    fem::fsparv(kv, km, g, kdiag);
    double gamma = prop[3][et];
    for (int i = 0; i < ndof; ++i)
      if (g[i] != 0)
        gravlo[g[i]] -= eld[i] * gamma;
  }

  fem::sparin(kv, kdiag);

  double tol = 0.0;
  int limit = 0, nsrf = 0;
  dat >> tol >> limit >> nsrf;
  std::vector<double> srf(nsrf);
  for (int i = 0; i < nsrf; ++i)
    dat >> srf[i];

  res << "\n    srf    max disp  iters\n";

  std::vector<double> loads(neq + 1, kZero);
  std::vector<double> bdylds(neq + 1, kZero);
  std::vector<double> oldis(neq + 1, kZero);
  std::vector<double> elastic(neq + 1, kZero);

  std::vector<double> eps(nst), sigma(nst), devp(nst), erate(nst), evp(nst);
  std::vector<double> eload(ndof), bload(ndof), eld(ndof);
  std::vector<std::vector<double>> m1(4, std::vector<double>(4));
  std::vector<std::vector<double>> m2(4, std::vector<double>(4));
  std::vector<std::vector<double>> m3(4, std::vector<double>(4));
  std::vector<std::vector<double>> flow(4, std::vector<double>(4));

  for (int iy = 0; iy < nsrf; ++iy) {
    double dt = kStartDt;
    for (int ip = 0; ip < np_types; ++ip) {
      double phi_deg = prop[0][ip];
      double tnph = std::tan(phi_deg * kPi / kD180);
      double phif_rad = std::atan(tnph / srf[iy]);
      double snph = std::sin(phif_rad);
      double e = prop[4][ip];
      double v = prop[5][ip];
      double ddt = kFour * (kOne + v) * (kOne - kTwo * v) /
                   (e * (kOne - kTwo * v + snph * snph));
      if (ddt < dt)
        dt = ddt;
    }

    int iters = 0;
    std::fill(bdylds.begin(), bdylds.end(), kZero);
    for (size_t k = 0; k < evpt.size(); ++k)
      std::fill(evpt[k].begin(), evpt[k].end(), kZero);
    std::fill(oldis.begin(), oldis.end(), kZero);

    for (;;) {
      double fmax = kZero;
      ++iters;
      for (int i = 0; i <= neq; ++i)
        loads[i] = gravlo[i] + bdylds[i];
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
        int et = etype[iel] - 1;
        double phi_deg = prop[0][et];
        double tnph = std::tan(phi_deg * kPi / kD180);
        double phif = std::atan(tnph / srf[iy]) * kD180 / kPi;
        double psi_deg = prop[2][et];
        double tnps = std::tan(psi_deg * kPi / kD180);
        double psif = std::atan(tnps / srf[iy]) * kD180 / kPi;
        double cf = prop[1][et] / srf[iy];
        double e = prop[4][et];
        double v = prop[5][et];
        fem::deemat(dee, e, v);

        for (int i = 0; i < nod; ++i)
          num[i] = g_num[i][iel];
        for (int i = 0; i < nod; ++i) {
          coord[0][i] = g_coord[0][num[i] - 1];
          coord[1][i] = g_coord[1][num[i] - 1];
        }
        std::vector<int> g(ndof);
        for (int i = 0; i < ndof; ++i)
          g[i] = g_g[i][iel];

        for (int i = 0; i < ndof; ++i) {
          int eq = g[i];
          eld[i] = (eq != 0) ? loads[eq] : kZero;
        }

        for (int gp = 0; gp < nip; ++gp) {
          double detj = kZero;
          fem::bee8(bee, coord, points[gp][0], points[gp][1], detj);

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
          fem::invar(sigma, &sigm, &dsbar, &lode_theta);
          double f = fem::mocouf(phif, cf, sigm, dsbar, lode_theta);
          if (f > fmax)
            fmax = f;

          if (converged || iters == limit) {
            devp = sigma;
          } else {
            if (f >= kZero) {
              double dq1 = 0.0, dq2 = 0.0, dq3 = 0.0;
              fem::mocouq(psif, dsbar, lode_theta, dq1, dq2, dq3);
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
            accumulate_bload(devp, bee, detj, weights[gp], bload);
        }

        for (int i = 0; i < ndof; ++i)
          if (g[i] != 0)
            bdylds[g[i]] += bload[i];
        bdylds[0] = kZero;
      }

      std::cout << "  srf" << std::fixed << std::setprecision(2) << srf[iy]
                << "  iteration" << iters << "  F_max" << std::setprecision(3) << fmax
                << std::endl;

      if (converged || iters == limit) {
        double maxabs = kZero;
        for (int i = 1; i <= neq; ++i)
          maxabs = std::max(maxabs, std::abs(loads[i]));
        res << std::fixed << std::setprecision(2) << std::setw(7) << srf[iy]
            << std::scientific << std::setprecision(4) << std::setw(12) << maxabs
            << std::fixed << std::setw(5) << iters << "\n";
        break;
      }
    }

    if (iters == limit)
      break;
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

  return 0;
}

} // namespace programs
