#ifndef MAIN_BEAM_GM_HPP
#define MAIN_BEAM_GM_HPP

namespace fem {

/// Forms the beam geometric matrix for stability analysis (4x4).
void beam_gm(double gm[4][4], double ell);

} // namespace fem

#endif
