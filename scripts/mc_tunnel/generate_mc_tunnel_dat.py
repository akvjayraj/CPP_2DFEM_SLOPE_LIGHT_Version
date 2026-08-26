"""
Quarter-annular radial T3 mesh for the Mohr-Coulomb tunnel verification.

Problem (Smith & Griffiths Chapter 6 style):
  Cylindrical hole radius a=1 m in an infinite ideal-elastic-perfectly-plastic
  Mohr-Coulomb medium, hydrostatic compressive stress sigma_0 = 30 MPa.
  External boundary at R=21 m (>>plastic zone radius ~4.2 m).
  Plane strain, quarter symmetry (first quadrant).

Two cases written:
  mc_tunnel_assoc.dat    psi = phi = 30 deg  (associated flow)
  mc_tunnel_nonassoc.dat psi = 0  deg        (non-associated, no dilation)

Material:
  phi  = 30 deg
  c    = 1000 kPa  (1 MPa)
  E    = 2 000 000 kPa  (2 GPa)
  nu   = 0.25
  K0   = 0  (unused for layers=0 mode)

Analytical plastic-zone radius (Duncan Fama 1993, Eq. 5.28):
  k_phi = (1+sin30)/(1-sin30) = 3
  c_cot = c/tan(phi) = 1000/tan(30) ~ 1732 kPa
  R_p/a = [ (2*(p0*(k_phi-1)+c_cot)) / ((k_phi+1)*c_cot) ]^(1/(k_phi-1))
        = [ (2*(30000*2+1732)) / (4*1732) ]^0.5
        = [ 123464/6928 ]^0.5  ~  sqrt(17.82)  ~  4.22  m
"""
import math
import os

# ── Mesh parameters ─────────────────────────────────────────────────────────
NR = 15          # radial rings  (15+1=16 radial node layers)
NA = 15          # angular arcs  (15+1=16 angular node layers, 0..90 deg)
a  = 1.0         # inner radius (m)
R  = 21.0        # outer radius (m)

# ── Material ─────────────────────────────────────────────────────────────────
E     = 2_000_000.0   # kPa
nu    = 0.25
c     = 1_000.0       # kPa
phi   = 30.0          # deg
gamma = 0.0           # kN/m³ (unused in layers=0 mode)
K0    = 0.0           # unused

# ── Far-field stress (compression = negative in tension-positive convention) ─
sigma0_kPa = -30_000.0   # = -30 MPa

# ── Solver control ───────────────────────────────────────────────────────────
tol   = 1e-5
limit = 500
incs  = 10

# ────────────────────────────────────────────────────────────────────────────
# Derived mesh quantities
# ────────────────────────────────────────────────────────────────────────────
nn   = (NR + 1) * (NA + 1)
nels = 2 * NR * NA

# Logarithmic radial spacing
r_vals = [a * (R / a) ** (i / NR) for i in range(NR + 1)]

# Uniform angular spacing 0..90 deg
theta_vals = [j * (math.pi / 2.0) / NA for j in range(NA + 1)]

def nid(i, j):          # 1-based node id
    return i * (NA + 1) + j + 1

# ── Coordinates ─────────────────────────────────────────────────────────────
coords = []
for i in range(NR + 1):
    r = r_vals[i]
    for j in range(NA + 1):
        theta = theta_vals[j]
        coords.append((r * math.cos(theta), r * math.sin(theta)))

# ── Connectivity (2 CCW T3 per quad) ────────────────────────────────────────
# Quad corners:
#   n1=nid(i,j)   n2=nid(i+1,j)   n3=nid(i+1,j+1)   n4=nid(i,j+1)
# Tri-1: n1 n2 n3  (outer-radial half)
# Tri-2: n1 n3 n4  (inner-angular half)
connectivity = []
for i in range(NR):
    for j in range(NA):
        n1 = nid(i,   j)
        n2 = nid(i+1, j)
        n3 = nid(i+1, j+1)
        n4 = nid(i,   j+1)
        connectivity.append((n1, n2, n3))
        connectivity.append((n1, n3, n4))

assert len(connectivity) == nels

# ── Boundary conditions ──────────────────────────────────────────────────────
# y=0 axis  (j=0, all i): fix uy   → fmt: node  1  0
# x=0 axis  (j=NA, all i): fix ux  → fmt: node  0  1
bcs = []
for i in range(NR + 1):
    bcs.append((nid(i, 0),  1, 0))   # fix y on y=0 axis
    bcs.append((nid(i, NA), 0, 1))   # fix x on x=0 axis
nr_bc = len(bcs)

# ── Output nodes ────────────────────────────────────────────────────────────
# Tunnel wall (i=0, j=0..NA) for radial displacement check
output_nodes = [nid(0, j) for j in range(NA + 1)]
# Also add a few radial stations along theta=0 line (j=0, i=0..NR//2)
for i in range(1, NR // 2 + 1):
    n = nid(i, 0)
    if n not in output_nodes:
        output_nodes.append(n)
nouts = len(output_nodes)

# ── Outer-boundary traction edges (i=NR, j=0..NA-1) ─────────────────────────
# Edge direction j → j+1 is CCW; right-hand outward normal → radially outward ✓
outer_edges = [(nid(NR, j), nid(NR, j + 1)) for j in range(NA)]

# ────────────────────────────────────────────────────────────────────────────
def write_dat(path, psi_deg):
    with open(path, "w") as f:
        def w(*args, comment=""):
            line = "  ".join(str(a) for a in args)
            if comment:
                line += f"  ! {comment}"
            f.write(line + "\n")

        f.write(f"! Mohr-Coulomb tunnel verification — psi={psi_deg} deg\n")
        f.write(f"! a={a} m, R={R} m, sigma0={abs(sigma0_kPa)} kPa, phi={phi} deg, c={c} kPa\n")
        f.write(f"! NR={NR}, NA={NA}, nn={nn}, nels={nels}\n")
        f.write("!\n")

        # Line 1: nels nn np_types
        w(nels, nn, 1, comment="nels  nn  np_types")

        # Block 2: material properties  phi c psi gamma E v K0
        w(phi, c, psi_deg, gamma, E, nu, K0,
          comment="phi  c  psi  gamma  E  v  K0")

        # Block 4: nodal coordinates
        f.write("! --- nodal coordinates (nn rows: x  y) ---\n")
        for (x, y) in coords:
            f.write(f"  {x:.10f}  {y:.10f}\n")

        # Block 5: connectivity (nels rows: n1 n2 n3)
        f.write("! --- connectivity (nels rows: n1 n2 n3, 1-based CCW) ---\n")
        for (n1, n2, n3) in connectivity:
            f.write(f"  {n1}  {n2}  {n3}\n")

        # Line 6: boundary conditions
        w(nr_bc, comment="nr (number of BC lines)")
        for (node, fx, fy) in bcs:
            f.write(f"  {node}  {fx}  {fy}\n")

        # Line 7: output nodes
        w(nouts, comment="nouts")
        f.write("  " + "  ".join(str(n) for n in output_nodes) + "\n")

        # Solver control: layers=0 → infinite-medium traction-loaded mode
        w(tol, limit, incs, 0, comment="tol  limit  incs  layers=0")

        # Traction groups
        w(1, comment="n_traction_groups")
        f.write(f"  0  {sigma0_kPa:.1f}  {sigma0_kPa:.1f}  0.0"
                f"  ! mode=0 sxx syy sxy (tension+, hydrostatic compression)\n")
        w(len(outer_edges), comment="n_edges on outer boundary")
        for (n1, n2) in outer_edges:
            f.write(f"  {n1}  {n2}\n")

    print(f"Written: {path}  ({nels} elements, {nn} nodes)")

# ── Generate both files ──────────────────────────────────────────────────────
script_dir = os.path.dirname(os.path.abspath(__file__))
verif_dir  = os.path.join(script_dir, "..", "..", "Verification")

write_dat(os.path.join(verif_dir, "mc_tunnel_assoc.dat"),    psi_deg=phi)   # associated
write_dat(os.path.join(verif_dir, "mc_tunnel_nonassoc.dat"), psi_deg=0.0)   # non-associated

# ── Print analytical reference ───────────────────────────────────────────────
p0 = abs(sigma0_kPa)
k_phi = (1 + math.sin(math.radians(phi))) / (1 - math.sin(math.radians(phi)))
c_cot = c / math.tan(math.radians(phi))
R_p = a * ((2 * (p0*(k_phi-1) + c_cot)) / ((k_phi+1) * c_cot)) ** (1/(k_phi-1))
G   = E / (2*(1+nu))
# Wall displacement (associated, Duncan Fama 1993):
#   u_r(a) = (a/(2G)) * [ (1-2nu)*(p0-c*cot(phi)) + 2*(p0+c*cot(phi)/k_phi)*(R_p/a)^2 / (k_phi+1) ... ]
# Simplified Salencon/Carranza-Torres formula:
#   u_r(a) = (a*R_p^2) / (2*G*a) * ( (1-nu) - nu*(k_phi-1)*... )
# Use the simple closed-form (Carranza-Torres 1998, Eq. 14):
C_p   = (2*(p0*(k_phi-1) + c_cot)) / ((k_phi+1)*c_cot)
u_r_a = (a / (2*G)) * (1-nu) * (p0*(k_phi-1) + c_cot*(k_phi+1)/2) * (R_p/a)**2 / ((k_phi-1)/2)
print(f"\nAnalytical reference (associated flow, Duncan Fama 1993):")
print(f"  k_phi      = {k_phi:.4f}")
print(f"  c*cot(phi) = {c_cot:.1f} kPa")
print(f"  R_p/a      = {R_p:.4f}  →  R_p = {R_p:.3f} m")
print(f"  u_r(a) ≈ {u_r_a:.6f} m  (inward,  associated, rough estimate)")
print(f"  G          = {G:.0f} kPa")
print(f"  Outer boundary R/R_p = {R/R_p:.1f}  (infinite-medium approximation quality)")
