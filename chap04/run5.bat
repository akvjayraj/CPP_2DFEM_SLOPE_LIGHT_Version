echo off
set ED5=\\Mac\Home\Downloads
set G95=C:\g95
set FEL=%ED5%\source\library
set PATH=%G95%\bin
set LIBRARY_PATH=%G95%\lib; %G95%\lib\gcc-lib\i686-mingw32\4.0.4
g95 %1.f03 -o %1.exe %FEL%\arpack\arpacklib.a ^
-I %FEL%\main -I %FEL%\geom ^
%FEL%\main\mainlib.a %FEL%\geom\geomlib.a
%1 %2
del %1.exe
