"""Write JR-01 p610 T3 decks (mirrors tests/.../Jr01DeckBuilder.cs)."""
from __future__ import annotations

import pathlib

KN = 1.0e5
KS = 1.0e5
E = 3.0e7
NU = 0.25


def fmt(v: float) -> str:
    return f"{v:.10g}"


def build_dat(nx: int, ny_quads: int, syy: float, increments: int = 5, mesh_name: str = "") -> str:
    ny_l = ny_quads // 2
    ny_u = ny_quads - ny_l
    nxl = nx + 1
    n_lower = nxl * (ny_l + 1)
    nn = n_lower + nxl * (ny_u + 1)
    nels = nx * ny_l * 2 + nx * ny_u * 2

    def node_l(ix: int, iy: int) -> int:
        return iy * nxl + ix + 1

    def node_u(ix: int, iy: int) -> int:
        return n_lower + iy * nxl + ix + 1

    lines = [
        "! <solver:p610_t3>",
        f"! JR-01 single joint normal compression  mesh={mesh_name}  syy={fmt(syy)} kPa",
        "! <BEGIN:AnalysisOptions>",
        "! failure_criterion Elastic",
        "! material_type Isotropic",
        "! mesh_type T3",
        "! solver_executable p610_t3_elastoplastic",
        "! <END:AnalysisOptions>",
        f"{nels}  {nn}  1",
        f"89  1.0e10  0  0  {fmt(E)}  {fmt(NU)}  0",
    ]
    for iy in range(ny_l + 1):
        y = iy / ny_l
        for ix in range(nx + 1):
            lines.append(f"{fmt(ix / nx)}  {fmt(y)}")
    for iy in range(ny_u + 1):
        y = 1.0 + iy / ny_u
        for ix in range(nx + 1):
            lines.append(f"{fmt(ix / nx)}  {fmt(y)}")

    def quad(n00, n10, n11, n01):
        lines.append(f"{n00}  {n10}  {n11}")
        lines.append(f"{n00}  {n11}  {n01}")

    for iy in range(ny_l):
        for ix in range(nx):
            quad(node_l(ix, iy), node_l(ix + 1, iy), node_l(ix + 1, iy + 1), node_l(ix, iy + 1))
    for iy in range(ny_u):
        for ix in range(nx):
            quad(node_u(ix, iy), node_u(ix + 1, iy), node_u(ix + 1, iy + 1), node_u(ix, iy + 1))

    lines.append(f"{nxl}")
    for ix in range(nx + 1):
        nid = node_l(ix, 0)
        lines.append(f"{nid}  0  0" if ix == 0 else f"{nid}  1  0")

    watch = [
        node_l(0, 0), node_l(nx, 0), node_l(0, ny_l), node_l(nx, ny_l),
        node_u(0, 0), node_u(nx, 0), node_u(0, ny_u), node_u(nx, ny_u),
    ]
    lines.append(str(len(watch)))
    lines.append("  ".join(str(w) for w in watch))
    lines.append(f"0.0001  80  {max(1, increments)}  0")
    if abs(syy) < 1e-15:
        lines.append("0")
    else:
        lines.append("1")
        lines.append(f"0  0  {fmt(syy)}  0")
        lines.append(str(nx))
        for ix in range(nx - 1, -1, -1):
            lines.append(f"{node_u(ix + 1, ny_u)}  {node_u(ix, ny_u)}")

    lines += [
        "! <BEGIN:Joints>",
        "! include true",
        "! njoint 1",
        f"! ninterface {nx}",
        '! joint weakplane "JR-01 horizontal joint"',
        f"!   kn {fmt(KN)}",
        f"!   ks {fmt(KS)}",
        "!   cohesion 0",
        "!   friction 0",
        "!   tension 0",
        "!   ends both_open",
        "!   install_stage 1",
        "!   nvert 2",
        "!     0  1",
        "!     1  1",
        "! interfaces",
    ]
    for ix in range(nx):
        n1, n2 = node_l(ix, ny_l), node_l(ix + 1, ny_l)
        n3, n4 = node_u(ix + 1, 0), node_u(ix, 0)
        lines.append(f"!   {n1} {n2} {n3} {n4}  {fmt(KN)} {fmt(KS)} 0 0 0  both_open 1 0 0")
    lines.append("! <END:Joints>")
    return "\n".join(lines) + "\n"


def main() -> None:
    root = pathlib.Path(__file__).resolve().parents[1] / "decks"
    root.mkdir(parents=True, exist_ok=True)
    cases = [
        ("jr01_medium_1mpa.dat", 8, 16, -1000, 5, "medium"),
        ("jr01_medium_2mpa.dat", 8, 16, -2000, 5, "medium"),
        ("jr01_medium_5mpa.dat", 8, 16, -5000, 5, "medium"),
        ("jr01_medium_10mpa.dat", 8, 16, -10000, 5, "medium"),
        ("jr01_coarse_10mpa.dat", 4, 8, -10000, 5, "coarse"),
        ("jr01_fine_10mpa.dat", 16, 32, -10000, 5, "fine"),
        ("jr01_medium_unload_1mpa.dat", 8, 16, -1000, 5, "medium"),
        ("jr01_medium_unload_2mpa.dat", 8, 16, -2000, 5, "medium"),
        ("jr01_medium_unload_5mpa.dat", 8, 16, -5000, 5, "medium"),
        ("jr01_medium_unload_10mpa.dat", 8, 16, -10000, 5, "medium"),
        ("jr01_medium_unload_0mpa.dat", 8, 16, 0, 1, "medium"),
        ("jr01_medium_tension.dat", 8, 16, 100, 5, "medium"),
    ]
    for name, nx, ny, syy, inc, mesh in cases:
        (root / name).write_text(build_dat(nx, ny, syy, inc, mesh), encoding="utf-8")
        print("wrote", root / name)


if __name__ == "__main__":
    main()
