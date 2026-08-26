#include "sample.hpp"
#include <cmath>
#include <stdexcept>

namespace fem {

void sample(const std::string& element, std::vector<std::vector<double>>& s,
            std::vector<double>* wt) {
  int nip = static_cast<int>(s.size());
  double root3 = 1.0 / std::sqrt(3.0);

  if (element == "line") {
    if (nip == 1) {
      s[0][0] = 0.0;
      if (wt) (*wt)[0] = 2.0;
    } else if (nip == 2) {
      s[0][0] = -root3; s[1][0] = root3;
      if (wt) { (*wt)[0] = 1.0; (*wt)[1] = 1.0; }
    } else if (nip == 3) {
      s[0][0] = -0.774596669241484;
      s[1][0] = 0.0;
      s[2][0] = 0.774596669241484;
      if (wt) { (*wt)[0] = 0.555555555555556; (*wt)[1] = 0.888888888888889; (*wt)[2] = 0.555555555555556; }
    } else {
      throw std::runtime_error("Wrong number of integrating points for a line");
    }
  } else if (element == "quadrilateral") {
    if (nip == 1) {
      s[0][0] = 0.0; s[0][1] = 0.0;
      if (wt) (*wt)[0] = 4.0;
    } else if (nip == 4) {
      s[0][0] = -root3; s[0][1] = root3;
      s[1][0] = root3;  s[1][1] = root3;
      s[2][0] = -root3; s[2][1] = -root3;
      s[3][0] = root3;  s[3][1] = -root3;
      if (wt) for (int i = 0; i < 4; ++i) (*wt)[i] = 1.0;
    } else if (nip == 9) {
      double r15 = 0.2 * std::sqrt(15.0);
      for (int i = 0; i < 3; ++i) {
        s[i][0] = -r15; s[i+3][0] = 0.0; s[i+6][0] = r15;
        s[i*3][1] = r15; s[i*3+1][1] = 0.0; s[i*3+2][1] = -r15;
      }
      if (wt) {
        double v[] = {5.0/9.0*5.0/9.0, 8.0/9.0*5.0/9.0, 5.0/9.0*5.0/9.0,
                      5.0/9.0*8.0/9.0, 8.0/9.0*8.0/9.0, 5.0/9.0*8.0/9.0,
                      5.0/9.0*5.0/9.0, 8.0/9.0*5.0/9.0, 5.0/9.0*5.0/9.0};
        for (int i = 0; i < 9; ++i) (*wt)[i] = v[i];
      }
    } else if (nip == 16) {
      double g4[] = {-0.861136311594053, -0.339981043584856, 0.339981043584856, 0.861136311594053};
      double w4[] = {0.347854845137454, 0.652145154862546, 0.652145154862546, 0.347854845137454};
      for (int i = 0; i < 4; ++i)
        for (int j = 0; j < 4; ++j) {
          s[i*4+j][0] = g4[i];
          s[i*4+j][1] = g4[j];
          if (wt) (*wt)[i*4+j] = w4[i] * w4[j];
        }
    } else {
      throw std::runtime_error("Wrong number of integrating points for a quadrilateral");
    }
  } else if (element == "triangle") {
    if (nip == 1) {
      s[0][0] = 1.0/3.0; s[0][1] = 1.0/3.0;
      if (wt) (*wt)[0] = 0.5;
    } else if (nip == 3) {
      s[0][0] = 0.5; s[0][1] = 0.5;
      s[1][0] = 0.5; s[1][1] = 0.0;
      s[2][0] = 0.0; s[2][1] = 0.5;
      if (wt) { (*wt)[0] = (*wt)[1] = (*wt)[2] = 0.5/3.0; }
    } else {
      throw std::runtime_error("Wrong number of integrating points for a triangle");
    }
  } else if (element == "hexahedron") {
    if (nip == 1) {
      s[0][0] = s[0][1] = s[0][2] = 0.0;
      if (wt) (*wt)[0] = 8.0;
    } else if (nip == 8) {
      s[0][0] = s[0][1] = s[0][2] = root3;
      s[1][0] = s[1][1] = root3;  s[1][2] = -root3;
      s[2][0] = root3;  s[2][1] = -root3; s[2][2] = root3;
      s[3][0] = root3;  s[3][1] = s[3][2] = -root3;
      s[4][0] = -root3; s[4][1] = s[4][2] = root3;
      s[5][0] = s[5][1] = -root3; s[5][2] = root3;
      s[6][0] = -root3; s[6][1] = root3;  s[6][2] = -root3;
      s[7][0] = s[7][1] = s[7][2] = -root3;
      if (wt) for (int i = 0; i < 8; ++i) (*wt)[i] = 1.0;
    } else {
      throw std::runtime_error("Wrong number of integrating points for a hexahedron");
    }
  } else {
    throw std::runtime_error("Not a valid element type");
  }
}

} // namespace fem
