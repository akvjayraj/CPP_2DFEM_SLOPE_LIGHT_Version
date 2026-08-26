#include "deemat_ti.hpp"
#include <cmath>

namespace fem {

void deemat_ti(std::vector<std::vector<double>>& dee,
               double E1, double E2, double nu12, double nu23, double G12) {
  const int n = static_cast<int>(dee.size());
  if (n != 3 && n != 4) return;
  if (static_cast<int>(dee[0].size()) != n) return;
  double zero = 0.0;
  for (int i = 0; i < n; ++i)
    for (int j = 0; j < n; ++j)
      dee[i][j] = zero;

  /* Plane strain: eps_z = 0. Transversely isotropic compliance (axis z):
   * eps_x = s11*sx + s12*sy + s13*sz, etc.
   * s11 = 1/E1, s12 = -nu12/E1, s13 = -nu23/E2
   * s22 = 1/E1, s23 = -nu23/E2, s33 = 1/E2
   * s44 = 1/G12 (for tau_xy -> gamma_xy)
   * From eps_z = 0: sz = -((s13*sx + s23*sy) / s33) = (nu23*E2/E1)*(sx+sy) / (1 - nu23*...) 
   * Actually: s31*sx + s32*sy + s33*sz = 0 => sz = -(s31*sx + s32*sy)/s33 = nu23*(sx+sy) (for s31=s32=-nu23/E2, s33=1/E2).
   * So sz = nu23*E2*(sx+sy) / E2 * (1/(1-nu23))? No. s31 = -nu23/E2, s32 = -nu23/E2, s33 = 1/E2.
   * So -nu23/E2*(sx+sy) + 1/E2*sz = 0 => sz = nu23*(sx+sy).
   * Substituting into eps_x = s11*sx + s12*sy + s13*sz = (1/E1)*sx - (nu12/E1)*sy - (nu23/E2)*nu23*(sx+sy)
   * = sx*(1/E1 - nu23^2/E2) - sy*(nu12/E1 + nu23^2/E2). So we get the 2x2 reduced compliance for (sx,sy).
   * Then invert to get 2x2 stiffness; tau_xy = G12*gamma_xy gives the 3rd diagonal; and row/col 3 for sz.
   */
  double s11 = 1.0 / E1;
  double s12 = -nu12 / E1;
  double s13 = -nu23 / E2;
  double s33 = 1.0 / E2;
  /* Reduced 2x2 for (sx,sy) with eps_z=0: eps_x = a11*sx + a12*sy, eps_y = a21*sx + a22*sy
   * where a11 = s11 + s13*nu23 = 1/E1 - nu23^2/E2, a12 = s12 + s13*nu23 = -nu12/E1 - nu23^2/E2,
   * a21 = a12 (symmetry), a22 = a11.
   */
  double a11 = s11 + s13 * nu23;
  double a12 = s12 + s13 * nu23;
  double det2 = a11 * a11 - a12 * a12;
  if (std::abs(det2) < 1e-30) return;
  double inv_det = 1.0 / det2;
  double D11 = a11 * inv_det;
  double D12 = -a12 * inv_det;
  dee[0][0] = D11;
  dee[0][1] = D12;
  dee[1][0] = D12;
  dee[1][1] = D11;
  dee[2][2] = G12;
  /* sz = nu23*(sx+sy) => sig_z = nu23*(sig_x+sig_y) from the 0 eps_z condition. So row 3: sz = nu23*sx + nu23*sy => dee[3][0]=nu23*D11+nu23*D12? No. We have (sx,sy,txy,sz) = D*(ex,ey,gxy,0). So sx = D11*ex+D12*ey, sy = D12*ex+D11*ey, txy = G12*gxy, sz = dee[3][0]*ex+dee[3][1]*ey+dee[3][2]*gxy. And sz = nu23*(sx+sy) = nu23*(D11*ex+D12*ey+D12*ex+D11*ey) = nu23*( (D11+D12)*ex + (D11+D12)*ey ) = nu23*(D11+D12)*(ex+ey). So dee[3][0] = dee[3][1] = nu23*(D11+D12), dee[3][2] = 0. And dee[0][3], dee[1][3], dee[2][3] for symmetry: dee[3][0]=dee[0][3] etc. So dee[0][3]=dee[3][0]=nu23*(D11+D12), dee[1][3]=dee[3][1]=nu23*(D11+D12), dee[2][3]=dee[3][2]=0. And dee[3][3]: when ez is nonzero, we need consistency; for plane strain ez=0 so column 3 is often set to zero or to the value that would give sz from ez only. Standard is dee[3][3] = E2 + nu23*nu23*(D11+D12)*2 or similar so that the 4x4 is symmetric and consistent. Actually the 4th row and column: for strain (0,0,0,ez), stress (0,0,0,sz), we need sz = E2*ez (approximately) for the out-of-plane direction, so dee[3][3] = E2. But then the matrix is not symmetric unless we set dee[0][3]=dee[3][0] etc. We have dee[3][0]=dee[0][3]=nu23*(D11+D12). So the 4x4 is symmetric. dee[3][3]: when only ez is applied, sz = dee[3][3]*ez. From the compliance, eps_z = s31*sx+s32*sy+s33*sz. If only sz is applied, eps_z = s33*sz so sz = E2*eps_z. So dee[3][3] = E2. But then we need to check symmetry: the contribution from (ex,ey,0,0) to sz is dee[3][0]*ex+dee[3][1]*ey. So we're good. */
  double d31 = nu23 * (D11 + D12);
  dee[3][0] = dee[0][3] = d31;
  dee[3][1] = dee[1][3] = d31;
  if (n >= 4) {
    dee[3][0] = dee[0][3] = d31;
    dee[3][1] = dee[1][3] = d31;
    dee[3][2] = dee[2][3] = 0.0;
    dee[3][3] = E2;
  }
}

} // namespace fem
