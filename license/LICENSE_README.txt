GeotechSlopeApp - Locked License Package
========================================

Built: 2026-06-25 16:53:30 (local time)
Licensee: Evaluation User
Valid until: 2026-07-25T23:59:59Z (UTC)

Run the application
-------------------
1. Open this folder in Explorer.
2. Double-click GeotechSlopeApp.exe.
3. Sign in when prompted (see LICENSE_CREDENTIALS.txt).

Layout
------
GeotechSlopeApp.exe          Main application (self-contained .NET 8, win-x64)
library\solver\              FEM executables used by FE Analysis and Script Editor

Solver binaries included
------------------------
  - fem_cpp.exe
  - mesh2d_native.dll
  - p41.exe
  - p51.exe
  - p610_t3_elastic.exe
  - p610_t3_elastoplastic.exe
  - slope_stability_Q4_SRF.exe
  - slope_stability_Q8_SRF.exe
  - slope_stability_T3_SRF.exe
  - slope_stability_T6_SRF.exe

Notes
-----
- Do not move solver executables out of library\solver; the app resolves them relative to GeotechSlopeApp.exe.
- The license checks UTC expiry and resists casual system-clock rollback via HKCU\Software\GeotechSlopeApp\License.
- After expiry, request a renewed build from the application owner.
