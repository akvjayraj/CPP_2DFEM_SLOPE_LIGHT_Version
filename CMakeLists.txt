cmake_minimum_required(VERSION 3.10)
project(fem_cpp LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 11)

# Geom library (mirrors Fortran library/geom structure)
add_library(geom_cpp STATIC
  library/geom/formnf.cpp
  library/geom/mesh_size.cpp
  library/geom/bc_rect.cpp
  library/geom/geom_rect.cpp
  library/geom/emb_2d_bc.cpp
  library/geom/emb_2d_geom.cpp
  library/geom/emb_3d_bc.cpp
  library/geom/emb_3d_geom.cpp
  library/geom/hexahedron_xz.cpp
  library/geom/fmcoem.cpp
  library/geom/fmglem.cpp
  library/geom/geom_freesurf.cpp
)
target_include_directories(geom_cpp PUBLIC library/geom)

# Main library
add_library(main_cpp STATIC
  library/main/rod_km.cpp
  library/main/rod_mm.cpp
  library/main/pin_jointed.cpp
  library/main/glob_to_axial.cpp
  library/main/rigid_jointed.cpp
  library/main/num_to_g.cpp
  library/main/fkdiag.cpp
  library/main/fsparv.cpp
  library/main/sparin.cpp
  library/main/spabac.cpp
  library/main/beam_km.cpp
  library/main/beam_mm.cpp
  library/main/beam_gm.cpp
  library/main/checon.cpp
  library/main/linmul_sky.cpp
  library/main/stability.cpp
  library/main/fmplat.cpp
  library/main/hinge.cpp
  library/main/determinant.cpp
  library/main/invert.cpp
  library/main/cholin.cpp
  library/main/chobk1.cpp
  library/main/chobk2.cpp
  library/main/getname.cpp
  library/main/lnblnk.cpp
  library/main/rect_km.cpp
  library/main/stiff3.cpp
  library/main/stiff4.cpp
  library/main/stiff6.cpp
  library/main/deemat.cpp
  library/main/deemat_ti.cpp
  library/main/beemat.cpp
  library/main/bee8.cpp
  library/main/shape_fun.cpp
  library/main/shape_der.cpp
  library/main/sample.cpp
  library/main/cross_product.cpp
  library/main/seep4.cpp
  library/main/bandwidth.cpp
  library/main/bandred.cpp
  library/main/bisect.cpp
  library/main/sparin_gauss.cpp
  library/main/spabac_gauss.cpp
  library/main/invar.cpp
  library/main/interp.cpp
  library/main/formku.cpp
  library/main/formlump.cpp
  library/main/elmat.cpp
  library/main/ecmat.cpp
  library/main/norm.cpp
  library/main/formm.cpp
  library/main/mocouf.cpp
  library/main/mocouq.cpp
  library/main/vmdpl.cpp
  library/main/vmflow.cpp
  library/main/banmul.cpp
  library/main/formkb.cpp
  library/main/formtb.cpp
  library/main/gauss_band.cpp
  library/main/solve_band.cpp
  library/main/load_function.cpp
  library/main/formke.cpp
  library/main/formupv.cpp
  library/main/formkc.cpp
  library/main/comred.cpp
  library/main/comsub.cpp
  library/main/loc_to_glob.cpp
  library/main/glob_to_loc.cpp
  library/main/bantmul.cpp
  library/main/form_s.cpp
  library/main/mcdpl.cpp
  library/main/fmdsig.cpp
  library/main/fmrmat.cpp
  library/main/fmacat.cpp
  library/main/fmkdke.cpp
  library/main/formaa.cpp
  library/main/mesh.cpp
  library/main/dismsh.cpp
  library/main/contour.cpp
  library/main/beamdis.cpp
  library/main/exc_nods.cpp
  library/main/bmat_nonaxi.cpp
  library/main/stiff10.cpp
  library/main/stiff15.cpp
  library/main/mesh_ensi.cpp
  library/main/dismsh_ensi.cpp
  library/main/pmsh_ensi.cpp
  library/main/vecmsh.cpp
)
target_include_directories(main_cpp PUBLIC library/main)
target_link_libraries(main_cpp geom_cpp)

set(SOLVER_DIR ${CMAKE_SOURCE_DIR}/library/solver)
set(SOLVER_SRF_DIR "${SOLVER_DIR}/SRF")
set(SOLVER_Q4Q8_DIR "${SOLVER_DIR}/Q4-Q8 SRF")
set(SOLVER_STRUCTURAL_DIR "${SOLVER_DIR}/Structural-Static Elasticity")

function(deploy_solver_to_srf_folder target)
  add_custom_command(TARGET ${target} POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E make_directory "${SOLVER_SRF_DIR}"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
      $<TARGET_FILE:${target}>
      "${SOLVER_SRF_DIR}/$<TARGET_FILE_NAME:${target}>"
    COMMENT "Sync $<TARGET_FILE_NAME:${target}> to library/solver/SRF"
  )
endfunction()

function(deploy_solver_to_q4q8_folder target)
  add_custom_command(TARGET ${target} POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E make_directory "${SOLVER_Q4Q8_DIR}"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
      $<TARGET_FILE:${target}>
      "${SOLVER_Q4Q8_DIR}/$<TARGET_FILE_NAME:${target}>"
    COMMENT "Sync $<TARGET_FILE_NAME:${target}> to library/solver/Q4-Q8 SRF"
  )
endfunction()

function(deploy_solver_to_structural_folder target)
  add_custom_command(TARGET ${target} POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E make_directory "${SOLVER_STRUCTURAL_DIR}"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
      $<TARGET_FILE:${target}>
      "${SOLVER_STRUCTURAL_DIR}/$<TARGET_FILE_NAME:${target}>"
    COMMENT "Sync $<TARGET_FILE_NAME:${target}> to library/solver/Structural-Static Elasticity"
  )
endfunction()

add_library(geotech_dat_io STATIC
  library/io/geotech_dat_io.cpp
)
target_include_directories(geotech_dat_io PUBLIC library/io)

add_library(solver_common STATIC ${SOLVER_DIR}/common.cpp)
target_include_directories(solver_common PUBLIC ${SOLVER_DIR})
target_link_libraries(solver_common PUBLIC geotech_dat_io)

add_library(slope_constitutive STATIC ${SOLVER_DIR}/slope_constitutive.cpp)
target_include_directories(slope_constitutive PUBLIC ${SOLVER_DIR} library/main)
target_link_libraries(slope_constitutive PUBLIC main_cpp)

# 2D mesh generation (C++ core + DLL for X2D_GUI P/Invoke)
set(MESH2D_DIR ${CMAKE_SOURCE_DIR}/library/mesh)
add_library(mesh2d_algo STATIC
  ${MESH2D_DIR}/bowyer_watson.cpp
  ${MESH2D_DIR}/dismsh_ensi.cpp
  ${MESH2D_DIR}/dismsh.cpp
  ${MESH2D_DIR}/geom_rect_triangle.cpp
  ${MESH2D_DIR}/graded_surface_mesh.cpp
  ${MESH2D_DIR}/mesh_ensi.cpp
  ${MESH2D_DIR}/mesh_size.cpp
  ${MESH2D_DIR}/mesh.cpp
  ${MESH2D_DIR}/pmsh_ensi.cpp
  ${MESH2D_DIR}/polygon_ear.cpp
  ${MESH2D_DIR}/surface_mesh_hierarchy.cpp
  ${MESH2D_DIR}/t6_upgrade.cpp
  ${MESH2D_DIR}/tunnel_mesh.cpp
  ${MESH2D_DIR}/vecmsh.cpp
)
target_include_directories(mesh2d_algo PUBLIC ${MESH2D_DIR})
set_target_properties(mesh2d_algo PROPERTIES POSITION_INDEPENDENT_CODE ON)

add_library(mesh2d_native SHARED ${MESH2D_DIR}/mesh2d_c_api.cpp)
target_include_directories(mesh2d_native PUBLIC ${MESH2D_DIR})
target_link_libraries(mesh2d_native PRIVATE mesh2d_algo)
set_target_properties(mesh2d_native PROPERTIES OUTPUT_NAME mesh2d_native)

# Unified geotechnical driver (Chapter 6: p64–p613). Wire new runners in main.cpp + run_stubs / run_p64.
add_executable(fem_cpp
  ${SOLVER_DIR}/main.cpp
  "${SOLVER_DIR}/slope stability_Q8_SRF.cpp"
  ${SOLVER_DIR}/slope_element.cpp
  ${SOLVER_DIR}/run_p64.cpp
  "${SOLVER_DIR}/Embankment in layers_Q8.cpp"
  ${SOLVER_DIR}/run_stubs.cpp
  ${SOLVER_DIR}/seepage_programs.cpp
  ${SOLVER_DIR}/transient_programs.cpp
)
target_include_directories(fem_cpp PRIVATE ${SOLVER_DIR})
target_link_libraries(fem_cpp PRIVATE solver_common geotech_dat_io main_cpp geom_cpp slope_constitutive)
add_custom_command(TARGET fem_cpp POST_BUILD
  COMMAND ${CMAKE_COMMAND} -E make_directory "${SOLVER_Q4Q8_DIR}"
  COMMAND ${CMAKE_COMMAND} -E copy_if_different
    $<TARGET_FILE:fem_cpp>
    "${SOLVER_Q4Q8_DIR}/fem_cpp.exe"
  COMMENT "Sync fem_cpp.exe to library/solver/Q4-Q8 SRF"
)

# Thin geotechnical executables (excavation / fill / earth pressure suite) — sources under programs/
# Each program's source is optional; the target is only declared when the .cpp
# is actually present. Lets the build proceed when only some chapters have been
# dropped into programs/ (e.g. fresh worktrees).
set(GEOTECH_EXE p64 p65 p66 p67 p68 p69 p610 p611 p612 p613)
foreach(name IN LISTS GEOTECH_EXE)
  if(NOT EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/programs/${name}.cpp")
    continue()
  endif()
  add_executable(${name} programs/${name}.cpp)
  target_include_directories(${name} PRIVATE ${SOLVER_DIR} library/main library/geom)
  if("${name}" STREQUAL "p64")
    target_sources(${name} PRIVATE "${SOLVER_DIR}/slope stability_Q8_SRF.cpp" ${SOLVER_DIR}/slope_element.cpp ${SOLVER_DIR}/run_p64.cpp)
    target_link_libraries(${name} PRIVATE solver_common main_cpp geom_cpp slope_constitutive)
    deploy_solver_to_q4q8_folder(${name})
  endif()
endforeach()

# p610_t3_elastoplastic (elasto-plastic T3: staged excavation OR infinite-medium
# Mohr-Coulomb). Target name mirrors the source basename, and the runtime output
# is placed next to the source so Script Editor execution can run
# library/solver/p610_t3_elastoplastic.exe directly. The in-file routing tag
# `! <solver:p610_t3>` is unchanged and still uniquely identifies these scripts
# in the GUI; only the on-disk exe name differs from the tag.
add_executable(p610_t3_elastoplastic ${SOLVER_DIR}/p610_t3_elastoplastic.cpp)
target_include_directories(p610_t3_elastoplastic PRIVATE ${SOLVER_DIR} library/main library/geom)
target_link_libraries(p610_t3_elastoplastic PRIVATE solver_common main_cpp geom_cpp slope_constitutive)
# Note: Debug-config builds intentionally fall through to the default
# ${CMAKE_BINARY_DIR}/Debug output so they do NOT overwrite the production
# release exe that the GUI / tools/build_*_smoke_packages.ps1 scripts pick up
# from library/solver. This lets us run a step-able Debug build at
# build/Debug/p610_t3_elastoplastic.exe (Phase A of the verification roadmap).
set_target_properties(p610_t3_elastoplastic PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY_RELEASE "${SOLVER_DIR}"
  RUNTIME_OUTPUT_DIRECTORY_RELWITHDEBINFO "${SOLVER_DIR}"
  RUNTIME_OUTPUT_DIRECTORY_MINSIZEREL "${SOLVER_DIR}"
)
deploy_solver_to_structural_folder(p610_t3_elastoplastic)

# Slope / SRF branded drivers (T3, T6, Q4): thin wrappers + shared core (same pattern as
# P610_VARIANT_EXES). Source filenames mirror `slope stability_Q8_SRF.cpp` (spaces).
# CMake target names stay identifier-safe (no spaces). Same .dat as p610_t3_elastoplastic.
add_executable(slope_stability_T3_SRF
  "${SOLVER_DIR}/slope stability_T3_SRF.cpp"
  ${SOLVER_DIR}/p610_t3_elastoplastic.cpp
)
target_include_directories(slope_stability_T3_SRF PRIVATE ${SOLVER_DIR} library/main library/geom)
target_compile_definitions(slope_stability_T3_SRF PRIVATE P610_T3_NO_MAIN)
target_link_libraries(slope_stability_T3_SRF PRIVATE solver_common main_cpp geom_cpp slope_constitutive)
set_target_properties(slope_stability_T3_SRF PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY "${SOLVER_DIR}"
  RUNTIME_OUTPUT_DIRECTORY_DEBUG "${SOLVER_DIR}"
  RUNTIME_OUTPUT_DIRECTORY_RELEASE "${SOLVER_DIR}"
  RUNTIME_OUTPUT_DIRECTORY_RELWITHDEBINFO "${SOLVER_DIR}"
  RUNTIME_OUTPUT_DIRECTORY_MINSIZEREL "${SOLVER_DIR}"
)
deploy_solver_to_srf_folder(slope_stability_T3_SRF)

add_executable(slope_stability_T6_SRF
  "${SOLVER_DIR}/slope stability_T6_SRF.cpp"
  ${SOLVER_DIR}/p610_t3_elastoplastic.cpp
)
target_include_directories(slope_stability_T6_SRF PRIVATE ${SOLVER_DIR} library/main library/geom)
target_compile_definitions(slope_stability_T6_SRF PRIVATE P610_T3_NO_MAIN)
target_link_libraries(slope_stability_T6_SRF PRIVATE solver_common main_cpp geom_cpp slope_constitutive)
set_target_properties(slope_stability_T6_SRF PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY "${SOLVER_DIR}"
  RUNTIME_OUTPUT_DIRECTORY_DEBUG "${SOLVER_DIR}"
  RUNTIME_OUTPUT_DIRECTORY_RELEASE "${SOLVER_DIR}"
  RUNTIME_OUTPUT_DIRECTORY_RELWITHDEBINFO "${SOLVER_DIR}"
  RUNTIME_OUTPUT_DIRECTORY_MINSIZEREL "${SOLVER_DIR}"
)
deploy_solver_to_srf_folder(slope_stability_T6_SRF)

# p610_t3_elastic (linear-elastic T3, far-field traction BCs, Kirsch verification)
add_executable(p610_t3_elastic ${SOLVER_DIR}/p610_t3_elastic.cpp)
target_include_directories(p610_t3_elastic PRIVATE ${SOLVER_DIR} library/main library/geom)
target_link_libraries(p610_t3_elastic PRIVATE main_cpp geom_cpp)
set_target_properties(p610_t3_elastic PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY "${SOLVER_DIR}"
  RUNTIME_OUTPUT_DIRECTORY_DEBUG "${SOLVER_DIR}"
  RUNTIME_OUTPUT_DIRECTORY_RELEASE "${SOLVER_DIR}"
  RUNTIME_OUTPUT_DIRECTORY_RELWITHDEBINFO "${SOLVER_DIR}"
  RUNTIME_OUTPUT_DIRECTORY_MINSIZEREL "${SOLVER_DIR}"
)

# p610_t3 criterion/material specializations (currently share the same core
# source implementation but expose distinct executable names for explicit
# routing from GUI and Script Editor .dat AnalysisOptions).
set(P610_VARIANT_EXES
  p610_t3_mohr_coulomb
  p610_t3_hoek_brown
  p610_t3_drucker_prager
  p610_t3_generalized_hoek_brown
  p610_t3_plastic
  p610_t3_nonlinear
)
foreach(name IN LISTS P610_VARIANT_EXES)
  add_executable(${name}
    ${SOLVER_DIR}/${name}.cpp
    ${SOLVER_DIR}/p610_t3_elastoplastic.cpp
  )
  target_include_directories(${name} PRIVATE ${SOLVER_DIR} library/main library/geom)
  target_compile_definitions(${name} PRIVATE P610_T3_NO_MAIN)
  target_link_libraries(${name} PRIVATE solver_common main_cpp geom_cpp slope_constitutive)
  set_target_properties(${name} PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY "${SOLVER_DIR}"
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${SOLVER_DIR}"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${SOLVER_DIR}"
    RUNTIME_OUTPUT_DIRECTORY_RELWITHDEBINFO "${SOLVER_DIR}"
    RUNTIME_OUTPUT_DIRECTORY_MINSIZEREL "${SOLVER_DIR}"
  )
  deploy_solver_to_structural_folder(${name})
endforeach()

# Branded slope SRF executables: mesh x failure criterion (thin p610 wrappers for T3/T6; Core B for Q4/Q8).
set(SLOPE_SRF_MESHES T3 T6)
set(SLOPE_SRF_CRITERIA mohr_coulomb drucker_prager hoek_brown generalized_hoek_brown)
set(SLOPE_SRF_RUNTIME_PROPS
  RUNTIME_OUTPUT_DIRECTORY "${SOLVER_DIR}"
  RUNTIME_OUTPUT_DIRECTORY_DEBUG "${SOLVER_DIR}"
  RUNTIME_OUTPUT_DIRECTORY_RELEASE "${SOLVER_DIR}"
  RUNTIME_OUTPUT_DIRECTORY_RELWITHDEBINFO "${SOLVER_DIR}"
  RUNTIME_OUTPUT_DIRECTORY_MINSIZEREL "${SOLVER_DIR}"
)

file(MAKE_DIRECTORY "${CMAKE_BINARY_DIR}/slope_srf_wrappers")

set(SLOPE_Q4Q8_RUNTIME_PROPS
  RUNTIME_OUTPUT_DIRECTORY "${SOLVER_Q4Q8_DIR}"
  RUNTIME_OUTPUT_DIRECTORY_DEBUG "${SOLVER_Q4Q8_DIR}"
  RUNTIME_OUTPUT_DIRECTORY_RELEASE "${SOLVER_Q4Q8_DIR}"
  RUNTIME_OUTPUT_DIRECTORY_RELWITHDEBINFO "${SOLVER_Q4Q8_DIR}"
  RUNTIME_OUTPUT_DIRECTORY_MINSIZEREL "${SOLVER_Q4Q8_DIR}"
)

foreach(mesh IN LISTS SLOPE_SRF_MESHES)
  foreach(crit IN LISTS SLOPE_SRF_CRITERIA)
    set(exe_name "slope_stability_${mesh}_${crit}_SRF")
    set(wrapper_cpp "${CMAKE_BINARY_DIR}/slope_srf_wrappers/${exe_name}.cpp")
    file(WRITE "${wrapper_cpp}"
"#include \"p610_t3_entry.hpp\"\n"
"int main(int argc, char* argv[]) {\n"
"  return p610_t3_run_entry(argc, argv, \"${exe_name}\");\n"
"}\n")
    add_executable(${exe_name}
      "${wrapper_cpp}"
      ${SOLVER_DIR}/p610_t3_elastoplastic.cpp
    )
    target_include_directories(${exe_name} PRIVATE ${SOLVER_DIR} library/main library/geom)
    target_compile_definitions(${exe_name} PRIVATE P610_T3_NO_MAIN)
    target_link_libraries(${exe_name} PRIVATE solver_common main_cpp geom_cpp slope_constitutive)
    set_target_properties(${exe_name} PROPERTIES ${SLOPE_SRF_RUNTIME_PROPS})
    deploy_solver_to_srf_folder(${exe_name})
  endforeach()
endforeach()

set(SLOPE_COREB_SRF_MAIN "${CMAKE_BINARY_DIR}/slope_srf_wrappers/slope_coreb_srf_main.cpp")
file(WRITE "${SLOPE_COREB_SRF_MAIN}"
"#include \"run_programs.hpp\"\n"
"#include <fstream>\n"
"#include <iostream>\n"
"#include <string>\n"
"int main(int argc, char* argv[]) {\n"
"  std::string base;\n"
"  if (argc >= 2) base = argv[1];\n"
"  else { std::cout << \"Enter data file base name: \"; std::cin >> base; }\n"
"  std::ifstream dat(base + \".dat\");\n"
"  std::ofstream res(base + \".res\");\n"
"  if (!dat || !res) return 1;\n"
"  return programs::run_slope_q8_srf(dat, res, base);\n"
"}\n")

foreach(crit IN LISTS SLOPE_SRF_CRITERIA)
  set(q4_exe "slope_stability_Q4_${crit}_SRF")
  add_executable(${q4_exe}
    "${SLOPE_COREB_SRF_MAIN}"
    "${SOLVER_DIR}/slope stability_Q8_SRF.cpp"
    ${SOLVER_DIR}/slope_element.cpp
  )
  target_include_directories(${q4_exe} PRIVATE ${SOLVER_DIR} library/main library/geom library/io)
  target_link_libraries(${q4_exe} PRIVATE solver_common geotech_dat_io main_cpp geom_cpp slope_constitutive)
  set_target_properties(${q4_exe} PROPERTIES ${SLOPE_SRF_RUNTIME_PROPS})
  deploy_solver_to_srf_folder(${q4_exe})
endforeach()

# Legacy Q4 MC executable name (same implementation as slope_stability_Q4_mohr_coulomb_SRF).
add_executable(slope_stability_Q4_SRF
  "${SLOPE_COREB_SRF_MAIN}"
  "${SOLVER_DIR}/slope stability_Q8_SRF.cpp"
  ${SOLVER_DIR}/slope_element.cpp
)
target_include_directories(slope_stability_Q4_SRF PRIVATE ${SOLVER_DIR} library/main library/geom library/io)
target_link_libraries(slope_stability_Q4_SRF PRIVATE solver_common geotech_dat_io main_cpp geom_cpp slope_constitutive)
set_target_properties(slope_stability_Q4_SRF PROPERTIES ${SLOPE_SRF_RUNTIME_PROPS})
deploy_solver_to_srf_folder(slope_stability_Q4_SRF)

foreach(crit IN LISTS SLOPE_SRF_CRITERIA)
  set(q8_exe "slope_stability_Q8_${crit}_SRF")
  add_executable(${q8_exe}
    "${SLOPE_COREB_SRF_MAIN}"
    "${SOLVER_DIR}/slope stability_Q8_SRF.cpp"
    ${SOLVER_DIR}/slope_element.cpp
  )
  target_include_directories(${q8_exe} PRIVATE ${SOLVER_DIR} library/main library/geom library/io)
  target_link_libraries(${q8_exe} PRIVATE solver_common geotech_dat_io main_cpp geom_cpp slope_constitutive)
  set_target_properties(${q8_exe} PROPERTIES ${SLOPE_SRF_RUNTIME_PROPS})
  deploy_solver_to_srf_folder(${q8_exe})
endforeach()

# Legacy Q8 MC executable name (same implementation as slope_stability_Q8_mohr_coulomb_SRF).
add_executable(slope_stability_Q8_SRF
  "${SLOPE_COREB_SRF_MAIN}"
  "${SOLVER_DIR}/slope stability_Q8_SRF.cpp"
  ${SOLVER_DIR}/slope_element.cpp
)
target_include_directories(slope_stability_Q8_SRF PRIVATE ${SOLVER_DIR} library/main library/geom library/io)
target_link_libraries(slope_stability_Q8_SRF PRIVATE solver_common geotech_dat_io main_cpp geom_cpp slope_constitutive)
set_target_properties(slope_stability_Q8_SRF PROPERTIES ${SLOPE_SRF_RUNTIME_PROPS})
deploy_solver_to_srf_folder(slope_stability_Q8_SRF)

# ---------------------------------------------------------------------------
# Axisymmetric mechanical SRF (`*_SRF_axisym`): same sources + SLOPE_SRF_AXISYM=1
# ---------------------------------------------------------------------------
add_executable(slope_stability_T3_SRF_axisym
  "${SOLVER_DIR}/slope stability_T3_SRF_axisym.cpp"
  ${SOLVER_DIR}/p610_t3_elastoplastic.cpp
)
target_include_directories(slope_stability_T3_SRF_axisym PRIVATE ${SOLVER_DIR} library/main library/geom)
target_compile_definitions(slope_stability_T3_SRF_axisym PRIVATE P610_T3_NO_MAIN SLOPE_SRF_AXISYM=1)
target_link_libraries(slope_stability_T3_SRF_axisym PRIVATE solver_common main_cpp geom_cpp slope_constitutive)
set_target_properties(slope_stability_T3_SRF_axisym PROPERTIES ${SLOPE_SRF_RUNTIME_PROPS})
deploy_solver_to_srf_folder(slope_stability_T3_SRF_axisym)

add_executable(slope_stability_T6_SRF_axisym
  "${SOLVER_DIR}/slope stability_T6_SRF_axisym.cpp"
  ${SOLVER_DIR}/p610_t3_elastoplastic.cpp
)
target_include_directories(slope_stability_T6_SRF_axisym PRIVATE ${SOLVER_DIR} library/main library/geom)
target_compile_definitions(slope_stability_T6_SRF_axisym PRIVATE P610_T3_NO_MAIN SLOPE_SRF_AXISYM=1)
target_link_libraries(slope_stability_T6_SRF_axisym PRIVATE solver_common main_cpp geom_cpp slope_constitutive)
set_target_properties(slope_stability_T6_SRF_axisym PROPERTIES ${SLOPE_SRF_RUNTIME_PROPS})
deploy_solver_to_srf_folder(slope_stability_T6_SRF_axisym)

foreach(mesh IN LISTS SLOPE_SRF_MESHES)
  foreach(crit IN LISTS SLOPE_SRF_CRITERIA)
    set(exe_name "slope_stability_${mesh}_${crit}_SRF_axisym")
    set(wrapper_cpp "${CMAKE_BINARY_DIR}/slope_srf_wrappers/${exe_name}.cpp")
    file(WRITE "${wrapper_cpp}"
"#include \"p610_t3_entry.hpp\"\n"
"int main(int argc, char* argv[]) {\n"
"  return p610_t3_run_entry(argc, argv, \"${exe_name}\");\n"
"}\n")
    add_executable(${exe_name}
      "${wrapper_cpp}"
      ${SOLVER_DIR}/p610_t3_elastoplastic.cpp
    )
    target_include_directories(${exe_name} PRIVATE ${SOLVER_DIR} library/main library/geom)
    target_compile_definitions(${exe_name} PRIVATE P610_T3_NO_MAIN SLOPE_SRF_AXISYM=1)
    target_link_libraries(${exe_name} PRIVATE solver_common main_cpp geom_cpp slope_constitutive)
    set_target_properties(${exe_name} PROPERTIES ${SLOPE_SRF_RUNTIME_PROPS})
    deploy_solver_to_srf_folder(${exe_name})
  endforeach()
endforeach()

foreach(crit IN LISTS SLOPE_SRF_CRITERIA)
  set(q4_exe "slope_stability_Q4_${crit}_SRF_axisym")
  add_executable(${q4_exe}
    "${SLOPE_COREB_SRF_MAIN}"
    "${SOLVER_DIR}/slope stability_Q8_SRF.cpp"
    ${SOLVER_DIR}/slope_element.cpp
  )
  target_include_directories(${q4_exe} PRIVATE ${SOLVER_DIR} library/main library/geom library/io)
  target_compile_definitions(${q4_exe} PRIVATE SLOPE_SRF_AXISYM=1)
  target_link_libraries(${q4_exe} PRIVATE solver_common geotech_dat_io main_cpp geom_cpp slope_constitutive)
  set_target_properties(${q4_exe} PROPERTIES ${SLOPE_SRF_RUNTIME_PROPS})
  deploy_solver_to_srf_folder(${q4_exe})
endforeach()

add_executable(slope_stability_Q4_SRF_axisym
  "${SLOPE_COREB_SRF_MAIN}"
  "${SOLVER_DIR}/slope stability_Q8_SRF.cpp"
  ${SOLVER_DIR}/slope_element.cpp
)
target_include_directories(slope_stability_Q4_SRF_axisym PRIVATE ${SOLVER_DIR} library/main library/geom library/io)
target_compile_definitions(slope_stability_Q4_SRF_axisym PRIVATE SLOPE_SRF_AXISYM=1)
target_link_libraries(slope_stability_Q4_SRF_axisym PRIVATE solver_common geotech_dat_io main_cpp geom_cpp slope_constitutive)
set_target_properties(slope_stability_Q4_SRF_axisym PROPERTIES ${SLOPE_SRF_RUNTIME_PROPS})
deploy_solver_to_srf_folder(slope_stability_Q4_SRF_axisym)

foreach(crit IN LISTS SLOPE_SRF_CRITERIA)
  set(q8_exe "slope_stability_Q8_${crit}_SRF_axisym")
  add_executable(${q8_exe}
    "${SLOPE_COREB_SRF_MAIN}"
    "${SOLVER_DIR}/slope stability_Q8_SRF.cpp"
    ${SOLVER_DIR}/slope_element.cpp
  )
  target_include_directories(${q8_exe} PRIVATE ${SOLVER_DIR} library/main library/geom library/io)
  target_compile_definitions(${q8_exe} PRIVATE SLOPE_SRF_AXISYM=1)
  target_link_libraries(${q8_exe} PRIVATE solver_common geotech_dat_io main_cpp geom_cpp slope_constitutive)
  set_target_properties(${q8_exe} PROPERTIES ${SLOPE_SRF_RUNTIME_PROPS})
  deploy_solver_to_srf_folder(${q8_exe})
endforeach()

add_executable(slope_stability_Q8_SRF_axisym
  "${SLOPE_COREB_SRF_MAIN}"
  "${SOLVER_DIR}/slope stability_Q8_SRF.cpp"
  ${SOLVER_DIR}/slope_element.cpp
)
target_include_directories(slope_stability_Q8_SRF_axisym PRIVATE ${SOLVER_DIR} library/main library/geom library/io)
target_compile_definitions(slope_stability_Q8_SRF_axisym PRIVATE SLOPE_SRF_AXISYM=1)
target_link_libraries(slope_stability_Q8_SRF_axisym PRIVATE solver_common geotech_dat_io main_cpp geom_cpp slope_constitutive)
set_target_properties(slope_stability_Q8_SRF_axisym PROPERTIES ${SLOPE_SRF_RUNTIME_PROPS})
deploy_solver_to_srf_folder(slope_stability_Q8_SRF_axisym)
