#include "common.hpp"
#include <cctype>
#ifdef _WIN32
#include <windows.h>
#endif

namespace programs {

std::string get_dataname(int argc, char* argv[]) {
  if (argc >= 2) return std::string(argv[1]);
  std::cout << "Enter data file base name: ";
  std::string d;
  std::cin >> d;
  return d;
}

bool open_data_file(const std::string& dataname, std::ifstream& dat) {
  std::string path = dataname + ".dat";
  dat.open(path);
#ifdef _WIN32
  if (!dat) {
    char exePath[MAX_PATH];
    if (GetModuleFileNameA(nullptr, exePath, MAX_PATH)) {
      std::string base_dir = exePath;
      size_t pos = base_dir.find_last_of("\\/");
      if (pos != std::string::npos) {
        base_dir = base_dir.substr(0, pos + 1);
        path = base_dir + dataname + ".dat";
        dat.open(path);
      }
    }
  }
#endif
  return dat.is_open();
}

bool open_result_file(const std::string& dataname, std::ofstream& res) {
  res.open(dataname + ".res");
  return res.is_open();
}

bool get_program_and_dataname(int argc, char* argv[], std::string& program_name, std::string& dataname) {
  if (argc < 2) return false;
  program_name = argv[1];
  if (argc >= 3)
    dataname = argv[2];
  else {
    std::cout << "Enter data file base name: ";
    std::cin >> dataname;
  }
  return true;
}

ProgramContext make_context(int argc, char* argv[]) {
  ProgramContext ctx;
  if (!get_program_and_dataname(argc, argv, ctx.program_name, ctx.dataname) || ctx.dataname.empty())
    return ctx;
  if (!open_data_file(ctx.dataname, ctx.dat)) {
    std::cerr << "Cannot open " << ctx.dataname << ".dat\n";
    return ctx;
  }
  if (!open_result_file(ctx.dataname, ctx.res)) {
    std::cerr << "Cannot create " << ctx.dataname << ".res\n";
    return ctx;
  }
  return ctx;
}

ProgramContext make_context_dataname_only(int argc, char* argv[]) {
  ProgramContext ctx;
  ctx.program_name.clear();
  if (argc >= 2)
    ctx.dataname = argv[1];
  else {
    std::cout << "Enter data file base name: ";
    std::cin >> ctx.dataname;
  }
  if (ctx.dataname.empty())
    return ctx;
  if (!open_data_file(ctx.dataname, ctx.dat)) {
    std::cerr << "Cannot open " << ctx.dataname << ".dat\n";
    return ctx;
  }
  if (!open_result_file(ctx.dataname, ctx.res)) {
    std::cerr << "Cannot create " << ctx.dataname << ".res\n";
    return ctx;
  }
  return ctx;
}

std::string read_analysis_type(std::ifstream& dat) {
  std::string prog;
  if (dat >> prog)
    return prog;
  return std::string();
}

std::string resolve_analysis_type(const std::string& token) {
  if (token.empty()) return std::string();

  // ----- Slope / embankment / cut stability (Mohr–Coulomb, viscoplastic) -----
  if (token == "slope_vp_8quad" || token == "slope_stability_p64" || token == "plane_strain_slope_mc") return "p64";

  // ----- Earth pressure (rigid wall; PFEM Program 6.5) -----
  if (token == "earth_pressure_mc" || token == "retaining_wall_mc" || token == "geotech_earth_pressure") return "p65";

  // ----- Embankment or staged fill / backfill in layers (PFEM Program 6.9) -----
  if (token == "embankment_layers_mc" || token == "staged_fill_mc" || token == "backfill_layers_mc") return "p69";

  // ----- Excavation in layers (PFEM Program 6.10) -----
  if (token == "excavation_layers_mc" || token == "staged_excavation_mc") return "p610";

  // ----- Excavation in layers, T3 triangular mesh -----
  if (token == "excavation_t3_mc" || token == "staged_excavation_t3_mc" || token == "p610_t3") return "p610_t3";

  // ----- Legacy ids: Chapter 6 geotechnical construction suite (p64–p613) -----
  if (token == "p64" || token == "p65" || token == "p66" || token == "p67" || token == "p68" ||
      token == "p69" || token == "p610" || token == "p611" || token == "p612" || token == "p613")
    return token;

  return std::string();
}

static bool is_numeric_token(const std::string& s) {
  if (s.empty()) return false;
  for (size_t i = 0; i < s.size(); ++i)
    if (!std::isdigit(static_cast<unsigned char>(s[i])) && s[i] != '-' && s[i] != '+') return false;
  return true;
}

bool read_optional_element_token(std::ifstream& dat, std::string& token) {
  token.clear();
  if (!(dat >> token)) return true;
  return !is_numeric_token(token);
}

// ---------- Standard structural/flow analysis helpers ----------

void read_restrained_nodes(std::ifstream& dat, std::vector<int>& nf, int nr, int nodof) {
  for (int i = 0; i < nr; ++i) {
    int k;
    dat >> k;
    for (int j = 0; j < nodof; ++j)
      dat >> nf[j + (k - 1) * nodof];
  }
}

void read_and_apply_fixed_freedoms(std::ifstream& dat, const std::vector<int>& nf, int nodof,
  std::vector<double>& kv, const std::vector<int>& kdiag, std::vector<double>& loads,
  double penalty, bool with_sense) {
  int fixed_freedoms;
  dat >> fixed_freedoms;
  if (fixed_freedoms == 0) return;
  for (int i = 0; i < fixed_freedoms; ++i) {
    int node, sense = 1;
    double value;
    if (with_sense)
      dat >> node >> sense >> value;
    else
      dat >> node >> value;
    int no = nf[(sense - 1) + (node - 1) * nodof];
    if (no == 0) continue;
    int diag = kdiag[no - 1] - 1;
    kv[diag] += penalty;
    loads[no] = kv[diag] * value;  // loads is 1-based (loads[1..neq])
  }
}

void cumulate_kdiag(std::vector<int>& kdiag, int neq) {
  for (int i = 1; i < neq; ++i)
    kdiag[i] += kdiag[i - 1];
}

} // namespace programs
