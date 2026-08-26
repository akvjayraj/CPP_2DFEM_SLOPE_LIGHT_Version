# Solver architecture: how the C++ `.cpp` solvers are linked into `.exe`, and how the app routes to them

Mapped from `CMakeLists.txt`, `library/solver/main.cpp`, and the GUI router
`X2D_GUI/Backend/P610AnalysisOptions.cs`.

## Summary

- Every solver executable statically links the same 5 building-block libraries:
  `geom_cpp`, `main_cpp`, `geotech_dat_io`, `solver_common`, `slope_constitutive`.
- There are **two independent solver cores**:
  - **Core A** — `library/solver/p610_t3_elastoplastic.cpp` (staged-excavation elastoplastic).
    The `T3/T6` branded SRF exes are thin wrappers that recompile this core with
    `-DP610_T3_NO_MAIN`.
  - **Core B** — `library/solver/slope stability_Q8_SRF.cpp` + `slope_element.cpp`
    (Smith & Griffiths Program 6.4, viscoplastic SRF). `fem_cpp`, `p64`, and all `Q4/Q8`
    branded SRF exes use this core.
- `Q8` deliberately comes from **Core B**, never Core A (Core A's Q8 element is broken —
  returns zero stress / diverges; the shipped `slope_stability_Q8_SRF.exe` is built from
  Core B).
- A standard Mohr-Coulomb slope SRF run from **Run FE Analysis** is routed by the GUI through
  **`slope_stability_{Mesh}_SRF.exe`** (literature-optimal core per mesh), not `fem_cpp p64`:
  - **T3 / T6** → Core A (`p610_t3_elastoplastic`) + p610 `.dat` deck
  - **Q4 / Q8** → Core B (`slope stability_Q8_SRF` / `slope_element`) + explicit p64-layout `.dat` deck
    (`slope_stability_{Q4,Q8}_SRF.exe` in `library/solver/SRF/`)
- The unified `fem_cpp.exe → p64` path remains available for Script Editor decks that name
  `p64` explicitly, but it is not the default for GUI SRF runs.

## A. Build / link graph — `.cpp` -> static libs -> `.exe`

```mermaid
flowchart TB
  subgraph SRC["Source folders"]
    geomS["library/geom/*.cpp"]
    mainS["library/main/*.cpp<br/>(deemat, beemat, invar,<br/>mocouf, sparin, spabac...)"]
    ioS["library/io/geotech_dat_io.cpp"]
    commonS["library/solver/common.cpp"]
    constS["library/solver/slope_constitutive.cpp"]
    p610S["library/solver/<br/>p610_t3_elastoplastic.cpp<br/><b>(CORE A)</b>"]
    q8S["library/solver/<br/>'slope stability_Q8_SRF.cpp'<br/>+ slope_element.cpp<br/><b>(CORE B / Program 6.4)</b>"]
  end

  subgraph LIBS["Static libraries (shared by everything)"]
    geom["geom_cpp"]
    maincpp["main_cpp"]
    io["geotech_dat_io"]
    common["solver_common"]
    const["slope_constitutive"]
  end

  geomS --> geom
  mainS --> maincpp
  geom --> maincpp
  ioS --> io
  commonS --> common
  io --> common
  constS --> const
  maincpp --> const

  subgraph FAMA["EXE family A - p610 T3 core (-DP610_T3_NO_MAIN)"]
    eA1["p610_t3_elastoplastic.exe"]
    eA2["slope_stability_{T3,T6}_SRF.exe"]
    eA3["slope_stability_{T3,T6}_{crit}_SRF.exe"]
    eA4["p610_t3_{mohr_coulomb,drucker_prager,<br/>hoek_brown,gen_hoek_brown,plastic,nonlinear}.exe"]
  end

  subgraph FAMB["EXE family B - Program 6.4 core (slope_element)"]
    eB1["fem_cpp.exe  (unified driver)"]
    eB2["p64.exe"]
    eB3["slope_stability_{Q4,Q8}_SRF.exe<br/>slope_stability_{Q4,Q8}_{crit}_SRF.exe"]
  end

  p610S --> eA1 & eA2 & eA3 & eA4
  q8S --> eB1 & eB2 & eB3

  common --> eA1 & eA2 & eA3 & eA4 & eB1 & eB2 & eB3
  maincpp --> eA1 & eA2 & eA3 & eA4 & eB1 & eB2 & eB3
  geom --> eA1 & eA2 & eA3 & eA4 & eB1 & eB2 & eB3
  const --> eA1 & eA2 & eA3 & eA4 & eB1 & eB2 & eB3
  io --> eB1 & eB2 & eB3
```

## B. Runtime routing — how the app selects an `.exe`

```mermaid
flowchart TB
  gui["GUI: Run FE Analysis / SRF<br/>SrfLiteratureOptimalRouting + ResolveSlopeSrfRunner()"]
  dat[".dat AnalysisOptions<br/>(mesh_type, failure_criterion,<br/>solver_executable)"]
  gui --> dat

  dat --> q{"MC SRF + mesh type?"}

  q -- "T3 / T6" --> exA["slope_stability_{mesh}_SRF.exe"]
  exA --> coreA["CORE A / p610_t3_elastoplastic<br/>p610 .dat deck"]

  q -- "Q4 / Q8" --> exB["slope_stability_{mesh}_SRF.exe"]
  exB --> coreB["CORE B / slope_element<br/>explicit p64-layout .dat deck"]

  q -- "Script deck names p64" --> femcpp["fem_cpp.exe  &lt;base&gt;"]
  femcpp --> disp["main.cpp: token = p64"]
  disp --> runp64["run_p64() -> run_slope_q8_srf()"]
  runp64 --> coreB

  q -- "non-MC slope (DP/HB/GHB)" --> brandedCrit["slope_stability_{mesh}_{crit}_SRF.exe"]
  brandedCrit --> meshQ{"Q4 or Q8?"}
  meshQ -- yes --> coreB
  meshQ -- no --> coreA

  q -- "p610_t3 script deck" --> exP["p610_t3_elastoplastic.exe<br/>(or p610_t3_{crit}.exe)"]
  exP --> coreA
```

## C. Where things live

| Item | Path |
|---|---|
| Core A source | `library/solver/p610_t3_elastoplastic.cpp` |
| Core B source | `library/solver/slope stability_Q8_SRF.cpp`, `library/solver/slope_element.cpp` |
| Unified driver dispatch | `library/solver/main.cpp` (`p64`->`run_p64`->`run_slope_q8_srf`) |
| Failure criteria | `library/solver/slope_constitutive.cpp` |
| `.dat` parsing | `library/io/geotech_dat_io.cpp` |
| FE primitives (S&G) | `library/main/*.cpp` |
| Deployed exes | `library/solver/SRF/` (T3/T6/Q4/Q8 SRF), `library/solver/Q4-Q8 SRF/` (`fem_cpp`, `p64`), `library/solver/Structural-Static Elasticity/` (p610) |
| GUI routing | `X2D_GUI/Backend/P610AnalysisOptions.cs`, `SrfLiteratureOptimalRouting.cs` |

## Notes / caveats

- The branded `T3/T6` SRF exes and the Core B `Q4/Q8` path are **different numerical cores**. For the same Mohr-Coulomb slope, Core A and Core B can give slightly
  different Factor-of-Safety values (e.g. T6 ~1.40 via Core A's lumped gravity vs ~1.32
  via Core B's consistent gravity) because of differences in gravity load lumping and the
  viscoplastic convergence handling.
- Q4/Q8-branded SRF exes deploy to `library/solver/SRF/` alongside T3/T6.
