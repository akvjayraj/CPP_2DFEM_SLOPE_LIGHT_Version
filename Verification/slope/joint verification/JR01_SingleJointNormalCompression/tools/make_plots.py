"""Build JR-01 CSVs and verification plots from solver JSON next to decks/."""
from __future__ import annotations

import csv
import json
import math
import pathlib

KN = 1.0e5
CASE = pathlib.Path(__file__).resolve().parents[1]
PLOTS = CASE / "plots"
DECKS = CASE / "decks"


def joint_stats(json_path: pathlib.Path) -> dict:
    data = json.loads(json_path.read_text(encoding="utf-8"))
    joints = data.get("joints") or []
    if not joints:
        raise SystemExit(f"{json_path} has no joints[] — rebuild p610 with JR-01 export")
    closures = [j["closure"] for j in joints]
    sns = [j["sn_kPa"] for j in joints]
    fy = float((data.get("reactions") or {}).get("fy") or 0.0)
    avg_c = sum(closures) / len(closures)
    avg_sn = sum(sns) / len(sns)
    return {
        "avg_closure": avg_c,
        "min_closure": min(closures),
        "max_closure": max(closures),
        "avg_sn": avg_sn,
        "fy": fy,
        "n": len(joints),
    }


def find_json(stem: str) -> pathlib.Path | None:
    for p in (DECKS / f"{stem}.json", CASE / f"{stem}.json"):
        if p.exists():
            return p
    return None


def write_csv(path: pathlib.Path, header: list[str], rows: list[list[object]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(header)
        w.writerows(rows)


def try_plot(xs, ys, path: pathlib.Path, xlabel: str, ylabel: str, title: str, ref=None) -> None:
    try:
        import matplotlib.pyplot as plt
    except ImportError:
        print("matplotlib not available; CSV only for", path.name)
        return
    fig, ax = plt.subplots(figsize=(6, 4))
    ax.plot(xs, ys, "o", label="FEM")
    if ref is not None:
        rx, ry, rlabel = ref
        ax.plot(rx, ry, "-", label=rlabel)
    ax.set_xlabel(xlabel)
    ax.set_ylabel(ylabel)
    ax.set_title(title)
    ax.grid(True, alpha=0.3)
    ax.legend()
    fig.tight_layout()
    fig.savefig(path, dpi=140)
    plt.close(fig)


def main() -> None:
    PLOTS.mkdir(parents=True, exist_ok=True)
    stresses = [1000, 2000, 5000, 10000]
    rows = []
    for s in stresses:
        jp = find_json(f"jr01_medium_{s // 1000}mpa")
        if jp is None:
            print("missing", s, "MPa JSON — run the decks first")
            continue
        st = joint_stats(jp)
        analytical = s / KN
        err = abs(st["avg_closure"] - analytical) / analytical * 100
        kn_num = st["avg_sn"] / st["avg_closure"] if abs(st["avg_closure"]) > 1e-15 else float("nan")
        f_app = s * 1.0
        eps_f = abs(abs(st["fy"]) - f_app) / f_app * 100 if f_app else float("nan")
        status = "PASS" if err < 1 and abs(kn_num - KN) / KN < 0.01 and eps_f < 0.1 else "FAIL"
        rows.append([s / 1000, st["avg_sn"], analytical, st["avg_closure"], st["min_closure"],
                     st["max_closure"], kn_num, err, eps_f, status])

    write_csv(PLOTS / "jr01_stress_closure.csv",
              ["sigma_MPa", "sn_num_kPa", "closure_analytical_m", "closure_avg_m",
               "closure_min_m", "closure_max_m", "kn_num_kPa_per_m", "error_pct",
               "equil_error_pct", "status"],
              rows)

    if rows:
        sig = [r[0] for r in rows]
        clo = [r[3] for r in rows]
        err = [r[7] for r in rows]
        write_csv(PLOTS / "jr01_error_vs_stress.csv", ["sigma_MPa", "error_pct"], list(zip(sig, err)))
        xs = [0, 0.12]
        try_plot(clo, [r[0] for r in rows], PLOTS / "plot1_stress_vs_closure.png",
                 "Joint closure δc (m)", "Applied stress σn (MPa)",
                 "JR-01 normal stress vs joint closure",
                 ref=(xs, [KN * x / 1000 for x in xs], "analytical σ = kn δ"))
        try_plot(sig, err, PLOTS / "plot2_error_vs_stress.png",
                 "Applied stress (MPa)", "Closure error (%)",
                 "JR-01 closure error vs applied stress")

    mesh_rows = []
    for name in ("coarse", "medium", "fine"):
        jp = find_json(f"jr01_{name}_10mpa")
        if jp is None:
            continue
        st = joint_stats(jp)
        analytical = 0.100
        err = abs(st["avg_closure"] - analytical) / analytical * 100
        mesh_rows.append([name, st["n"], analytical, st["avg_closure"], err])
    write_csv(PLOTS / "jr01_mesh_convergence.csv",
              ["mesh", "n_interfaces", "closure_analytical_m", "closure_num_m", "error_pct"],
              mesh_rows)
    if mesh_rows:
        try_plot(list(range(len(mesh_rows))), [r[3] for r in mesh_rows],
                 PLOTS / "plot3_mesh_convergence.png",
                 "Mesh (0=coarse, 1=medium, 2=fine)", "Joint closure (m)",
                 "JR-01 mesh convergence at 10 MPa",
                 ref=([-0.2, len(mesh_rows) - 0.8], [0.1, 0.1], "analytical 0.100 m"))

    seq = [("load", s) for s in (1, 2, 5, 10)] + [("unload", s) for s in (10, 5, 2, 1, 0)]
    lu = []
    for kind, s in seq:
        stem = "jr01_medium_unload_0mpa" if s == 0 else f"jr01_medium_{'unload_' if kind == 'unload' else ''}{s}mpa"
        if kind == "load":
            stem = f"jr01_medium_{s}mpa"
        jp = find_json(stem)
        if jp is None:
            continue
        st = joint_stats(jp)
        lu.append([kind, s, st["avg_closure"]])
    write_csv(PLOTS / "jr01_load_unload.csv", ["path", "sigma_MPa", "closure_m"], lu)
    if lu:
        try_plot([r[2] for r in lu], [r[1] for r in lu], PLOTS / "plot4_load_unload.png",
                 "Joint closure δc (m)", "Applied stress σn (MPa)",
                 "JR-01 loading / unloading")

    print("wrote CSVs/plots under", PLOTS)


if __name__ == "__main__":
    main()
