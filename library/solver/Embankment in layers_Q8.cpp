/**
 * Program 6.9 — Plane strain elastic–plastic (Mohr–Coulomb) embankment in layers on a
 * foundation, 8-node quadrilaterals, viscoplastic strain method (Smith & Griffiths).
 * Port of chap06 / library/solver p69 (slope.f03).
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

inline double& tensor_at(std::vector<std::vector<double>>& tensor, int st, int gp,
                         int el, int nip, int nels) {
  return tensor[st][static_cast<size_t>(gp) + static_cast<size_t>(nip) * static_cast<size_t>(el)];
}

inline double tensor_at_c(const std::vector<std::vector<double>>& tensor, int st, int gp,
                          int el, int nip, int /*nels*/) {
  return tensor[st][static_cast<size_t>(gp) + static_cast<size_t>(nip) * static_cast<size_t>(el)];
}

} // namespace

int run_p69(std::ifstream& dat, std::ofstream& res, const std::string& dataname) {
  const int ndim = 2, nodof = 2, nod = 8, ndof = 16, nip = 4, nst = 4;

  int fnxe = 0, fnye = 0, nn = 0, incs = 0, limit = 0, lifts = 0, enxe = 0, enye = 0,
      itype = 0;
  double tol = 0.0, k0 = 0.0;
  double e_f = 0.0, v_f = 0.0, c_f = 0.0, phi_f = 0.0, psi_f = 0.0, gama_f = 0.0;
  double e_e = 0.0, v_e = 0.0, c_e = 0.0, phi_e = 0.0, psi_e = 0.0, gama_e = 0.0;

  if (!(dat >> fnxe >> fnye >> nn >> incs >> limit >> tol >> lifts >> enxe >> enye >>
        itype >> k0 >> e_f >> v_f >> c_f >> phi_f >> psi_f >> gama_f >> e_e >> v_e >>
        c_e >> phi_e >> psi_e >> gama_e))
    return 1;

  int k_tri = 0;
  for (int i = 1; i <= enye - 1; ++i)
    k_tri += i;
  const int nels = fnxe * fnye + (enxe * enye - k_tri);

  std::vector<double> fwidth(static_cast<size_t>(fnxe + 1));
  std::vector<double> fdepth(static_cast<size_t>(fnye + 1));
  std::vector<double> ewidth(static_cast<size_t>(enxe + 1));
  std::vector<double> edepth(static_cast<size_t>(enye + 1));
  for (double& x : fwidth)
    dat >> x;
  for (double& x : fdepth)
    dat >> x;
  for (double& x : ewidth)
    dat >> x;
  for (double& x : edepth)
    dat >> x;

  std::vector<int> nf(static_cast<size_t>(nodof * nn), 1);
  int nr = 0;
  dat >> nr;
  for (int i = 0; i < nr; ++i) {
    int node_k = 0, nfx = 0, nfy = 0;
    dat >> node_k >> nfx >> nfy;
    nf[(node_k - 1) * nodof] = nfx;
    nf[(node_k - 1) * nodof + 1] = nfy;
  }
  geom::formnf(nf, nodof, nn);

  int neq_full = 0;
  for (int v : nf)
    neq_full = std::max(neq_full, v);

  res << " The final number of elements is:" << nels << "\n";
  res << " The final number of freedoms is:" << neq_full << "\n";

  std::vector<int> etype(static_cast<size_t>(nels), 1);
  for (int i = fnxe * fnye; i < nels; ++i)
    etype[static_cast<size_t>(i)] = 2;

  std::vector<std::vector<int>> g_num(nod, std::vector<int>(nels));
  geom::fmglem(fnxe, fnye, enxe, lifts, g_num);

  std::vector<std::vector<double>> g_coord(ndim, std::vector<double>(static_cast<size_t>(nn)));
  geom::fmcoem(g_num, g_coord, fwidth, fdepth, ewidth, edepth, enxe, lifts, fnxe, fnye,
               itype);

  std::vector<double> totd(static_cast<size_t>(neq_full + 1), kZero);

  std::vector<std::vector<double>> tensor(
      nst, std::vector<double>(static_cast<size_t>(nip) * static_cast<size_t>(nels), kZero));

  std::vector<std::vector<int>> g_g(ndof, std::vector<int>(nels));
  std::vector<int> num(nod);
  for (int iel = 0; iel < nels; ++iel) {
    for (int i = 0; i < nod; ++i)
      num[i] = g_num[i][iel];
    std::vector<int> g(ndof);
    fem::num_to_g(num, nf, nodof, nn, g);
    for (int i = 0; i < ndof; ++i)
      g_g[i][iel] = g[i];
  }

  std::string element = "quadrilateral";
  std::vector<std::vector<double>> points(nip, std::vector<double>(ndim));
  std::vector<double> weights(nip);
  fem::sample(element, points, &weights);

  const double pi = std::acos(-kOne);

  int oldele = 0;
  int oldnn = 0;

  for (int lift = 0; lift < lifts; ++lift) {
    res << "\n Lift number" << (lift + 1) << "\n";

    int newele = 0;
    if (lift == 0) {
      newele = fnxe * fnye;
      oldele = newele;
    } else {
      newele = enxe - (lift - 1);
      oldele += newele;
    }

    int lnn = 0;
    if (lift == 0) {
      lnn = (fnxe * 2 + 1) * (fnye + 1) + (fnxe + 1) * fnye;
      oldnn = lnn;
    } else {
      lnn = oldnn + (enxe - (lift - 1)) * 2 + 1 + (enxe - (lift - 1) + 1);
      oldnn = lnn;
    }

    std::vector<int> lnf(static_cast<size_t>(nodof * lnn));
    for (int j = 0; j < lnn; ++j) {
      lnf[static_cast<size_t>(j * nodof)] = nf[static_cast<size_t>(j * nodof)];
      lnf[static_cast<size_t>(j * nodof + 1)] = nf[static_cast<size_t>(j * nodof + 1)];
    }

    int neq = 0;
    for (int v : lnf)
      neq = std::max(neq, v);

    std::vector<int> kdiag(static_cast<size_t>(neq), 0);
    for (int iel = 0; iel < oldele; ++iel) {
      std::vector<int> g(ndof);
      for (int i = 0; i < ndof; ++i)
        g[i] = g_g[i][iel];
      fem::fkdiag(kdiag, g, neq);
    }
    for (int i = 1; i < neq; ++i)
      kdiag[static_cast<size_t>(i)] += kdiag[static_cast<size_t>(i - 1)];

    res << " There are" << neq << " freedoms\n";
    res << " There are" << oldele << " elements after " << newele << " were added\n";

    int kv_len = kdiag[static_cast<size_t>(neq - 1)];
    std::vector<double> kv(static_cast<size_t>(kv_len), kZero);
    std::vector<double> loads(static_cast<size_t>(neq + 1), kZero);
    std::vector<double> bdylds(static_cast<size_t>(neq + 1), kZero);
    std::vector<double> oldis(static_cast<size_t>(neq + 1), kZero);
    std::vector<double> gravlo(static_cast<size_t>(neq + 1), kZero);

    std::vector<std::vector<double>> coord(ndim, std::vector<double>(nod));
    std::vector<std::vector<double>> bee(nst, std::vector<double>(ndof));
    std::vector<std::vector<double>> dee(nst, std::vector<double>(nst));
    std::vector<double> fun(nod);

    for (int iel = 0; iel < oldele; ++iel) {
      double gamma = gama_f;
      double e = e_f;
      double v = v_f;
      if (etype[static_cast<size_t>(iel)] == 2) {
        gamma = gama_e;
        e = e_e;
        v = v_e;
      }
      if (iel < oldele - newele)
        gamma = kZero;

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
      fem::deemat(dee, e, v);

      for (int gp = 0; gp < nip; ++gp) {
        fem::shape_fun(fun, points, gp + 1);
        double gc0 = kZero, gc1 = kZero;
        for (int n = 0; n < nod; ++n) {
          gc0 += fun[n] * coord[0][n];
          gc1 += fun[n] * coord[1][n];
        }
        if (lift == 0) {
          tensor_at(tensor, 1, gp, iel, nip, nels) =
              -kOne * (fdepth[static_cast<size_t>(fnye)] - gc1) * gamma;
          tensor_at(tensor, 0, gp, iel, nip, nels) =
              k0 * tensor_at_c(tensor, 1, gp, iel, nip, nels);
          tensor_at(tensor, 3, gp, iel, nip, nels) =
              tensor_at_c(tensor, 0, gp, iel, nip, nels);
          tensor_at(tensor, 2, gp, iel, nip, nels) = kZero;
        }

        double detj = kZero;
        fem::bee8(bee, coord, points[gp][0], points[gp][1], detj);
        add_km_BBDB(km, bee, dee, detj * weights[gp]);
        for (int m = 0; m < nod; ++m) {
          int iy = (m + 1) * nodof - 1;
          eld[iy] += fun[m] * detj * weights[gp];
        }
      }

      fem::fsparv(kv, km, g, kdiag);
      if (gamma != kZero) {
        for (int i = 0; i < ndof; ++i)
          if (g[i] != 0)
            gravlo[g[i]] -= eld[i] * gamma;
      }
      gravlo[0] = kZero;
    }

    fem::sparin(kv, kdiag);
    for (int i = 1; i <= neq; ++i)
      gravlo[static_cast<size_t>(i)] /= static_cast<double>(incs);

    std::vector<std::vector<double>> evpt(
        nst, std::vector<double>(static_cast<size_t>(nip) * static_cast<size_t>(oldele),
                                 kZero));

    auto evpt_at = [&](int st, int gp, int el) -> double& {
      return evpt[st][static_cast<size_t>(gp) +
                     static_cast<size_t>(nip) * static_cast<size_t>(el)];
    };

    std::vector<double> eps(nst), sigma(nst), devp(nst), erate(nst), evp(nst);
    std::vector<double> bload(ndof), eld_dof(ndof);
    std::vector<std::vector<double>> m1(4, std::vector<double>(4));
    std::vector<std::vector<double>> m2(4, std::vector<double>(4));
    std::vector<std::vector<double>> m3(4, std::vector<double>(4));
    std::vector<std::vector<double>> flow(4, std::vector<double>(4));

    for (int inc = 0; inc < incs; ++inc) {
      int iters = 0;
      std::fill(oldis.begin(), oldis.end(), kZero);
      std::fill(bdylds.begin(), bdylds.end(), kZero);
      for (size_t row = 0; row < evpt.size(); ++row)
        std::fill(evpt[row].begin(), evpt[row].end(), kZero);

      for (;;) {
        ++iters;
        std::cout << "  lift" << std::setw(3) << (lift + 1) << "  increment" << std::setw(3)
                  << (inc + 1) << "  iteration" << std::setw(4) << iters << std::endl;

        std::fill(loads.begin(), loads.end(), kZero);
        for (int i = 1; i <= neq; ++i)
          loads[static_cast<size_t>(i)] =
              gravlo[static_cast<size_t>(i)] + bdylds[static_cast<size_t>(i)];
        fem::spabac(kv, loads, kdiag);
        loads[0] = kZero;

        bool converged = fem::checon(loads, oldis, tol);
        if (iters == 1)
          converged = false;
        if (converged || iters == limit)
          std::fill(bdylds.begin(), bdylds.end(), kZero);

        for (int iel = 0; iel < oldele; ++iel) {
          double phi = phi_f, c = c_f, e = e_f, v = v_f, psi = psi_f;
          if (etype[static_cast<size_t>(iel)] == 2) {
            phi = phi_e;
            c = c_e;
            e = e_e;
            v = v_e;
            psi = psi_e;
          }
          double snph = std::sin(phi * pi / kD180);
          double dt = kFour * (kOne + v) * (kOne - kTwo * v) / (e * (kOne - kTwo * v + snph * snph));
          fem::deemat(dee, e, v);
          std::fill(bload.begin(), bload.end(), kZero);

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
            eld_dof[i] = (eq != 0) ? loads[static_cast<size_t>(eq)] : kZero;
          }

          for (int gp = 0; gp < nip; ++gp) {
            double detj = kZero;
            fem::bee8(bee, coord, points[gp][0], points[gp][1], detj);

            for (int j = 0; j < nst; ++j) {
              eps[j] = kZero;
              for (int k = 0; k < ndof; ++k)
                eps[j] += bee[j][k] * eld_dof[k];
              eps[j] -= evpt_at(j, gp, iel);
            }

            for (int j = 0; j < nst; ++j) {
              sigma[j] = kZero;
              for (int k = 0; k < nst; ++k)
                sigma[j] += dee[j][k] * eps[k];
            }

            std::vector<double> stress(nst);
            if (lift == 0) {
              for (int j = 0; j < nst; ++j)
                stress[j] = tensor_at_c(tensor, j, gp, iel, nip, nels);
            } else {
              for (int j = 0; j < nst; ++j)
                stress[j] = tensor_at_c(tensor, j, gp, iel, nip, nels) + sigma[j];
            }

            double sigm = 0.0, dsbar = 0.0, lode_theta = 0.0;
            fem::invar(stress, &sigm, &dsbar, &lode_theta);
            double f = fem::mocouf(phi, c, sigm, dsbar, lode_theta);

            if (converged || iters == limit) {
              devp = stress;
            } else {
              if (f >= kZero) {
                double dq1 = 0.0, dq2 = 0.0, dq3 = 0.0;
                fem::mocouq(psi, dsbar, lode_theta, dq1, dq2, dq3);
                fem::formm(stress, m1, m2, m3);
                for (int r = 0; r < 4; ++r)
                  for (int c = 0; c < 4; ++c)
                    flow[r][c] = f * (dq1 * m1[r][c] + dq2 * m2[r][c] + dq3 * m3[r][c]);
                matmul_flow_sigma(flow, stress, erate);
                for (int jj = 0; jj < nst; ++jj) {
                  evp[jj] = erate[jj] * dt;
                  evpt_at(jj, gp, iel) += evp[jj];
                }
                dee_times_vec(dee, evp, devp);
              }
            }

            if (f >= kZero)
              accumulate_bload(devp, bee, detj, weights[gp], bload);

            if (converged || iters == limit) {
              if (lift != 0) {
                for (int j = 0; j < nst; ++j)
                  tensor_at(tensor, j, gp, iel, nip, nels) = stress[j];
              }
            }
          }

          for (int i = 0; i < ndof; ++i)
            if (g[i] != 0)
              bdylds[g[i]] += bload[i];
          bdylds[0] = kZero;
        }

        if (converged || iters == limit) {
          if (lift != 0) {
            for (int i = 1; i <= neq; ++i)
              totd[static_cast<size_t>(i)] += loads[static_cast<size_t>(i)];
          }
          res << " Increment" << (inc + 1) << " took " << iters << " iterations to converge\n";
          if (inc == incs - 1 || iters == limit) {
            double maxabs = kZero;
            for (int i = 1; i <= neq; ++i)
              maxabs = std::max(maxabs, std::abs(loads[static_cast<size_t>(i)]));
            res << " Max displacement is" << std::scientific << std::setprecision(4) << maxabs
                << "\n";
          }
          if (iters == limit) {
            int nlen = static_cast<int>(dataname.size());
            std::vector<std::vector<int>> lnf2(nodof, std::vector<int>(lnn));
            for (int j = 0; j < lnn; ++j) {
              lnf2[0][j] = lnf[static_cast<size_t>(j * nodof)];
              lnf2[1][j] = lnf[static_cast<size_t>(j * nodof + 1)];
            }
            fem::dismsh(loads, lnf2, 0.05, g_coord, g_num, dataname, nlen, 13);
            fem::vecmsh(loads, lnf2, g_coord, g_num, dataname, nlen, 14, 0.1);
            return 0;
          }
          break;
        }
      }
    }
  }

  return 0;
}

} // namespace programs
