/**
 * Common program utilities - shared by unified FEM executable.
 * I/O, argument parsing, and standard structural/flow analysis helpers.
 */
#ifndef PROGRAMS_COMMON_HPP
#define PROGRAMS_COMMON_HPP

#include <string>
#include <fstream>
#include <iostream>
#include <vector>

namespace programs {

/// Resolves data file base name from argv or prompts stdin. Returns empty on error.
std::string get_dataname(int argc, char* argv[]);

/// Parses argv for unified exe: argv[1]=program name (p41, p42, ...), argv[2]=optional dataname.
/// Returns true and sets program_name, dataname; if argc<2 returns false.
bool get_program_and_dataname(int argc, char* argv[], std::string& program_name, std::string& dataname);

/// Opens .dat file; tries current dir then (on Windows) exe directory. Returns true if open.
bool open_data_file(const std::string& dataname, std::ifstream& dat);

/// Opens .res file for writing. Returns true if open.
bool open_result_file(const std::string& dataname, std::ofstream& res);

/// Program context: holds program name, dataname and open streams for the duration of a run.
struct ProgramContext {
  std::string program_name;
  std::string dataname;
  std::ifstream dat;
  std::ofstream res;
  bool ok() const { return dat.is_open() && res.is_open(); }
};

/// Builds context for unified exe: program_name=argv[1], dataname=argv[2] or prompt. context.ok() false on failure.
ProgramContext make_context(int argc, char* argv[]);

/// Builds context using only dataname (program-independent mode).
/// argv[1] = dataname, or prompts if argc < 2. Opens .dat and .res; program_name is left empty (read from .dat next).
ProgramContext make_context_dataname_only(int argc, char* argv[]);

/// Reads first token from open .dat file (analysis type or program id).
std::string read_analysis_type(std::ifstream& dat);

/// Maps analysis type (or element+physics) to canonical program id.
/// Accepts: semantic names (e.g. rod_1d, pin_jointed, beam_static, steady_seepage_4node) and
/// legacy program ids (p41, p42, ... p118). Returns canonical id (e.g. "p41") or empty if unknown.
std::string resolve_analysis_type(const std::string& token);

/// Reads one token from dat. Returns true if it looks like an element name (non-numeric),
/// false if numeric. When false, token is the first data value (e.g. nels); caller should
/// use it (e.g. nels = stoi(token)) then read the rest. Supports optional "element" line in .dat.
bool read_optional_element_token(std::ifstream& dat, std::string& token);

// ---------- Standard structural/flow analysis helpers ----------

/// Reads restrained nodes from dat: nr lines, each k then nodof integers into nf.
/// nf layout: nf[j + (node-1)*nodof] for node 1..nn, dof 0..nodof-1.
void read_restrained_nodes(std::ifstream& dat, std::vector<int>& nf, int nr, int nodof);

/// Reads fixed_freedoms from dat and applies penalty to kv and sets loads.
/// If with_sense is false, each line is (node value); if true, (node sense value).
/// kdiag is 1-based cumulative (kdiag[i] = 1-based index of diagonal for equation i+1).
void read_and_apply_fixed_freedoms(std::ifstream& dat, const std::vector<int>& nf, int nodof,
  std::vector<double>& kv, const std::vector<int>& kdiag, std::vector<double>& loads,
  double penalty, bool with_sense);

/// Cumulates kdiag in place: kdiag[i] += kdiag[i-1] for i=1..neq-1. Call after fkdiag loop.
void cumulate_kdiag(std::vector<int>& kdiag, int neq);

/// Default penalty for fixed freedoms (1e20).
constexpr double default_penalty = 1.0e20;

// ---------- Formku / formlump / formtb / bandwidth / interp ----------
// Use library/main: fem::bandwidth(g), fem::formtb(pb, km, g), fem::interp(...).
// formku: fem::formku(ku, km, g) for vector km; fem::formku(ku, km[4][4], g) for 4x4 array.
// formlump: fem::formlump (diagonal-only) or fem::formlump_rowsum (row-sum, 1-based diag).

} // namespace programs

#endif
