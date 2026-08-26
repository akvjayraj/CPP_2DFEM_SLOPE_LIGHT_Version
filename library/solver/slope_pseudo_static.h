#ifndef SLOPE_PSEUDO_STATIC_HPP
#define SLOPE_PSEUDO_STATIC_HPP

#include <vector>

namespace slope_pseudo_static {

/// Apply lumped pseudo-static body forces to the global gravity load vector.
/// +x horizontal inertia (k_h * weight); vertical gravity scaled by (1 - k_v).
inline void apply_element_pseudo_static(
    std::vector<double>& gravlo,
    const std::vector<int>& g,
    int nodof,
    const std::vector<double>& eld_lumped,
    double gamma,
    double k_h,
    double k_v) {
  const int ndof = static_cast<int>(g.size());
  if (k_h == 0.0 && k_v == 0.0) {
    for (int i = 0; i < ndof; ++i)
      if (g[i] > 0 && g[i] < static_cast<int>(gravlo.size()) && (i % nodof) == 1)
        gravlo[g[i]] -= eld_lumped[i] * gamma;
    return;
  }

  for (int i = 0; i < ndof; ++i) {
    if (g[i] <= 0) continue;
    if (g[i] >= static_cast<int>(gravlo.size())) continue;
    const int comp = i % nodof;
    if (comp == 0)
      gravlo[g[i]] += eld_lumped[i] * gamma * k_h;
    else if (comp == 1)
      gravlo[g[i]] -= eld_lumped[i] * gamma * (1.0 - k_v);
  }
}

/// Accumulate pseudo-static-only nodal forces (k_h horizontal +x; vertical −k_v×weight).
inline void accumulate_element_pseudo_static_forces(
    std::vector<double>& ps_global,
    const std::vector<int>& g,
    int nodof,
    const std::vector<double>& eld_lumped,
    double gamma,
    double k_h,
    double k_v) {
  if (k_h == 0.0 && k_v == 0.0) return;
  const int ndof = static_cast<int>(g.size());
  for (int i = 0; i < ndof; ++i) {
    if (g[i] <= 0) continue;
    if (g[i] >= static_cast<int>(ps_global.size())) continue;
    const int comp = i % nodof;
    if (comp == 0)
      ps_global[g[i]] += eld_lumped[i] * gamma * k_h;
    else if (comp == 1)
      ps_global[g[i]] -= eld_lumped[i] * gamma * k_v;
  }
}

/// Map 1-based global force vector to per-node fx/fy using nf (column-major nodof×nn).
inline void gather_nodal_forces_from_global(
    const std::vector<double>& global,
    const std::vector<int>& nf,
    int nn,
    int nodof,
    std::vector<double>& fx,
    std::vector<double>& fy) {
  fx.assign(static_cast<size_t>(nn), 0.0);
  fy.assign(static_cast<size_t>(nn), 0.0);
  const size_t nf_need = static_cast<size_t>(nn) * static_cast<size_t>(nodof);
  if (nf.size() < nf_need || global.size() < 2) return;
  for (int in = 0; in < nn; ++in) {
    const size_t base = static_cast<size_t>(in) * static_cast<size_t>(nodof);
    const int eqx = nf[base];
    const int eqy = nf[base + 1];
    if (eqx > 0 && eqx < static_cast<int>(global.size()))
      fx[static_cast<size_t>(in)] = global[eqx];
    if (eqy > 0 && eqy < static_cast<int>(global.size()))
      fy[static_cast<size_t>(in)] = global[eqy];
  }
}

} // namespace slope_pseudo_static

#endif
