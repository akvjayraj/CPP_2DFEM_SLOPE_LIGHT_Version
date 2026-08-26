# Pure C++ FEM

Full C++ conversion of the Fortran finite element program. No Fortran compiler required.

## Structure

```
cpp/
├── library/
│   ├── geom/     # Geometry routines (formnf)
│   └── main/     # FEM routines (rod_km, fsparv, sparin, spabac, etc.)
├── programs/
│   └── p41.cpp   # Program 4.1 - axially loaded rods
├── CMakeLists.txt
└── build_cpp.bat
```

## Build

```powershell
cd cpp
.\build_cpp.bat
```

Or with CMake directly:
```powershell
cd cpp
mkdir build && cd build
cmake -G Ninja -DCMAKE_BUILD_TYPE=Release ..
cmake --build .
```

## Run

```powershell
copy ..\chap04\p41_1.dat cpp\build\
cd cpp\build
.\p41.exe p41_1
```

## Converted Routines (p41)

| Fortran | C++ |
|---------|-----|
| formnf | geom::formnf |
| rod_km | rod_km |
| num_to_g | num_to_g |
| fkdiag | fkdiag |
| fsparv | fsparv |
| sparin | sparin |
| spabac | spabac |

## Extending to More Programs

The full Fortran codebase has ~90 main routines and ~13 geom routines. To add more programs:

1. **Identify required routines** – Check the Fortran program's `USE main` and `USE geom` calls
2. **Convert** – Create .hpp/.cpp pairs in library/main/ or library/geom/
3. **Add to CMakeLists.txt** – Include new .cpp files in the library
4. **Create program** – Add programs/pxx.cpp following p41's pattern

Key routines for common programs:
- **p42–p47**: beam_km, beam_mm, cholin, chobk1, chobk2
- **p51–p57**: geom_rect, mesh_size, stiff4, deemat, beemat, shape_fun, shape_der, sample
- **p61+**: plasticity routines (invar, vmflow, vmdpl, etc.)
