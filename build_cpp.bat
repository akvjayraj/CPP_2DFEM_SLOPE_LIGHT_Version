@echo off
rem Pure C++ FEM build - no Fortran required

set "SRC=%~dp0"
pushd "%SRC%"

if not exist build mkdir build
cd build

cmake -G Ninja -DCMAKE_BUILD_TYPE=Release ..
if %ERRORLEVEL% equ 0 cmake --build .

popd
pause
