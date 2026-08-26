@echo off
set "SRC=%~dp0"
pushd "%SRC%"

if not exist "build\p41_1.dat" (
  echo Copying p41_1.dat from chap04...
  copy "..\chap04\p41_1.dat" "build\"
)

if not exist "build\p41.exe" (
  echo p41.exe not found. Run build_cpp.bat first.
  popd
  pause
  exit /b 1
)

cd build
p41.exe p41_1
echo.
dir p41_1.res 2>nul || echo p41_1.res not found
popd
pause
