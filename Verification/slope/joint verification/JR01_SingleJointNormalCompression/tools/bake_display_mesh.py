"""Bake the JR-01 medium T3 mesh and base BCs into the .xfem for Mesh / BC canvases."""
from __future__ import annotations

import json
from pathlib import Path

NX = 8
NY_L = 8
NY_U = 8
NXL = NX + 1
N_LOWER = NXL * (NY_L + 1)


def node_l(ix: int, iy: int) -> int:
    return iy * NXL + ix + 1


def node_u(ix: int, iy: int) -> int:
    return N_LOWER + iy * NXL + ix + 1


def coord(ix: int, iy: int, y0: float, ny: int) -> tuple[float, float]:
    return ix / NX, y0 + iy / ny


def main() -> None:
    case = Path(__file__).resolve().parents[1]
    path = case / "jr01_single_joint_normal_compression.xfem"
    doc = json.loads(path.read_text(encoding="utf-8"))

    nodes: list[dict[str, float]] = []
    for iy in range(NY_L + 1):
        for ix in range(NXL):
            x, y = coord(ix, iy, 0.0, NY_L)
            nodes.append({"x": x, "y": y})
    for iy in range(NY_U + 1):
        for ix in range(NXL):
            x, y = coord(ix, iy, 1.0, NY_U)
            nodes.append({"x": x, "y": y})

    elements: list[dict] = []
    rows: list[dict] = []
    eid = 0

    def add_tri(n1: int, n2: int, n3: int) -> None:
        nonlocal eid
        eid += 1
        p1, p2, p3 = nodes[n1 - 1], nodes[n2 - 1], nodes[n3 - 1]
        cx = (p1["x"] + p2["x"] + p3["x"]) / 3.0
        cy = (p1["y"] + p2["y"] + p3["y"]) / 3.0
        elements.append(
            {
                "displayId": eid,
                "isActive": True,
                "isHighlighted": False,
                "isQuadraticTriangle": False,
                "isQuadrilateral": False,
                "isQuadraticQuad": False,
                "isUnmatchedTriangle": False,
                "centerX": cx,
                "centerY": cy,
                "nodePoints": [p1, p2, p3],
            }
        )
        rows.append(
            {
                "elementId": eid,
                "node1": n1,
                "node2": n2,
                "node3": n3,
                "node4": 0,
                "node5": 0,
                "node6": 0,
                "node7": 0,
                "node8": 0,
                "etype": 1,
                "n1X": p1["x"],
                "n1Y": p1["y"],
                "n2X": p2["x"],
                "n2Y": p2["y"],
                "n3X": p3["x"],
                "n3Y": p3["y"],
                "n4X": 0,
                "n4Y": 0,
                "n5X": 0,
                "n5Y": 0,
                "n6X": 0,
                "n6Y": 0,
                "n7X": 0,
                "n7Y": 0,
                "n8X": 0,
                "n8Y": 0,
                "kind": "T3",
                "status": "Active",
                "smithEmb2dLocalOrder": False,
            }
        )

    def quad_pair(n00: int, n10: int, n11: int, n01: int) -> None:
        add_tri(n00, n10, n11)
        add_tri(n00, n11, n01)

    for iy in range(NY_L):
        for ix in range(NX):
            quad_pair(node_l(ix, iy), node_l(ix + 1, iy), node_l(ix + 1, iy + 1), node_l(ix, iy + 1))
    for iy in range(NY_U):
        for ix in range(NX):
            quad_pair(node_u(ix, iy), node_u(ix + 1, iy), node_u(ix + 1, iy + 1), node_u(ix, iy + 1))

    mesh = doc["mesh"]
    mesh["isDiscretized"] = True
    mesh["showMeshElements"] = True
    mesh["showMeshNodes"] = True
    mesh["discretizationNodes"] = nodes
    mesh["elements"] = elements
    mesh["informationRows"] = rows
    mesh["surfaceMeshStatus"] = (
        "JR-01 medium T3 (8×16, 256 triangles). Remesh before Run FE to split the joint."
    )

    bc_rows = [
        {
            "nodeId": 1,
            "boundaryName": "Node 1",
            "bcType": "Fixed",
            "analysisCategory": "Structural",
            "degreeOfFreedom": 0,
            "value": "0",
            "description": "Both DOFs restrained at (0,0)",
        }
    ]
    for nid in range(2, NXL + 1):
        bc_rows.append(
            {
                "nodeId": nid,
                "boundaryName": f"Node {nid}",
                "bcType": "Roller Y",
                "analysisCategory": "Structural",
                "degreeOfFreedom": 1,
                "value": "0",
                "description": "y restrained (roller on x-axis plane)",
            }
        )
    doc["boundaryConditions"]["rows"] = bc_rows
    doc["boundaryConditions"]["statusMessage"] = (
        "Base uy=0; (0,0) fixed. Shown on the Boundary Conditions canvas."
    )

    path.write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {len(nodes)} nodes, {len(elements)} T3s, {len(bc_rows)} BCs -> {path}")


if __name__ == "__main__":
    main()
