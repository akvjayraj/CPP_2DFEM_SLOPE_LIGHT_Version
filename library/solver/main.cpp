/**
 * Unified FEM executable — geotechnical excavation, embankment / staged backfill, and earth pressure.
 *
 * Usage: fem_cpp [dataname]
 *
 * First token in .dat: analysis alias or program id (p64 … p613).
 */
#include <iostream>
#include <string>
#include "common.hpp"
#include "geotech_dat_io.h"
#include "run_programs.hpp"

static void print_usage(const char* exe) {
  std::cerr << "Usage: " << exe << " [dataname]\n"
            << "  First token in .dat: p64 | p65 | … | p613, or a semantic alias (see resolve_analysis_type).\n"
            << "  p64  — plane strain slope stability (Mohr–Coulomb, 8-node quads, viscoplastic).\n"
            << "  p65  — earth pressure / rigid wall (stub).\n"
            << "  p66–p68, p611–p613 — reserved Chapter 6 drivers (stubs).\n"
            << "  p69  — embankment or staged fill in layers (Mohr–Coulomb, 8-node quads).\n"
            << "  p610 — excavation in layers (stub).\n"
            << "Example: " << exe << " mydata\n";
}

int main(int argc, char* argv[]) {
  programs::ProgramContext ctx = programs::make_context_dataname_only(argc, argv);
  if (!ctx.ok()) {
    if (argc < 2)
      print_usage(argv[0]);
    return 1;
  }

  const std::string dat_path = ctx.dataname + ".dat";
  std::string first_token = geotech_io::read_program_token(dat_path);
  if (first_token.empty())
    first_token = programs::read_analysis_type(ctx.dat);

  if (first_token.empty() && geotech_io::detect_p64_parametric_deck(dat_path))
    ctx.program_name = "p64";
  else if (geotech_io::detect_p64_explicit_deck(dat_path))
    ctx.program_name = "p64";
  else if (first_token.empty()) {
    std::cerr << "Error: could not read program id from " << dat_path << "\n";
    return 1;
  } else {
    ctx.program_name = programs::resolve_analysis_type(first_token);
    if (ctx.program_name.empty())
      ctx.program_name = first_token;
  }

  const std::string& prog = ctx.program_name;
  int ret = 0;

  if (prog == "p64") ret = programs::run_p64(ctx.dat, ctx.res, ctx.dataname);
  else if (prog == "p65") ret = programs::run_p65(ctx.dat, ctx.res, ctx.dataname);
  else if (prog == "p66") ret = programs::run_p66(ctx.dat, ctx.res, ctx.dataname);
  else if (prog == "p67") ret = programs::run_p67(ctx.dat, ctx.res, ctx.dataname);
  else if (prog == "p68") ret = programs::run_p68(ctx.dat, ctx.res, ctx.dataname);
  else if (prog == "p69") ret = programs::run_p69(ctx.dat, ctx.res, ctx.dataname);
  else if (prog == "p610") ret = programs::run_p610(ctx.dat, ctx.res, ctx.dataname);
  else if (prog == "p611") ret = programs::run_p611(ctx.dat, ctx.res, ctx.dataname);
  else if (prog == "p612") ret = programs::run_p612(ctx.dat, ctx.res, ctx.dataname);
  else if (prog == "p613") ret = programs::run_p613(ctx.dat, ctx.res, ctx.dataname);
  else if (prog == "p72") ret = programs::run_p72(ctx.dat, ctx.res, ctx.dataname);
  else if (prog == "p73") ret = programs::run_p73(ctx.dat, ctx.res, ctx.dataname);
  else if (prog == "p74") ret = programs::run_p74(ctx.dat, ctx.res, ctx.dataname);
  else if (prog == "p84") ret = programs::run_p84(ctx.dat, ctx.res, ctx.dataname);
  else if (prog == "p88") ret = programs::run_p88(ctx.dat, ctx.res, ctx.dataname);
  else {
    ctx.res << "Unknown program or analysis type: " << prog << "\n";
    std::cerr << "Unknown program or analysis type: " << prog << " (first token in " << ctx.dataname << ".dat)\n"
              << "Use p64…p613, p72–p74, p84/p88 or a supported alias (see library/solver/common.cpp).\n";
    return 1;
  }

  std::cout << "Results written to " << ctx.dataname << ".res\n";
  return ret;
}
