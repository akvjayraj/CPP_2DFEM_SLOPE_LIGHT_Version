/**
 * Stub runners for geotechnical construction programs not yet fully ported.
 */
#include "run_programs.hpp"
#include <fstream>
#include <string>

namespace programs {

#define STUB(NAME) \
  int run_##NAME(std::ifstream&, std::ofstream& res, const std::string&) { \
    res << #NAME ": full implementation pending.\n"; \
    return 0; \
  }

STUB(p65) STUB(p66) STUB(p67) STUB(p68) STUB(p69) STUB(p610) STUB(p611) STUB(p612) STUB(p613)

#undef STUB

} // namespace programs
