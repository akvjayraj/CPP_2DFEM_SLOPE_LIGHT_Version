// Trace exactly what the solver's std::ifstream-based parser sees, using the
// same load_dat_stripped logic (strip everything after '!').
#include <iostream>
#include <fstream>
#include <sstream>
#include <string>
int main(int argc, char* argv[]) {
    if (argc < 2) { std::cerr << "usage: trace_dat_parse <path>\n"; return 1; }
    std::ifstream src(argv[1]);
    if (!src) { std::cerr << "cannot open\n"; return 1; }
    std::stringstream out;
    std::string line;
    while (std::getline(src, line)) {
        auto bang = line.find('!');
        if (bang != std::string::npos) line.erase(bang);
        out << line << '\n';
    }
    int idx = 0;
    while (out) {
        std::string tok;
        out >> tok;
        if (!out) break;
        std::cout << idx++ << " : " << tok << "\n";
    }
    return 0;
}
