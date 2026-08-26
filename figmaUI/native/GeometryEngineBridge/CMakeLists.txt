cmake_minimum_required(VERSION 3.16)
project(GeometryEngineBridge LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

find_package(CGAL REQUIRED)
find_package(OpenCASCADE CONFIG REQUIRED)

add_library(GeometryEngineBridge SHARED bridge.cpp)

target_link_libraries(GeometryEngineBridge PRIVATE
  CGAL::CGAL
  TKernel
  TKMath
  TKG2d
  TKGeom2d
)

if(MSVC)
  target_compile_definitions(GeometryEngineBridge PRIVATE GEOMETRY_ENGINE_EXPORTS)
endif()
