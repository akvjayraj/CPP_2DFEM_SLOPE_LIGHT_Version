// Tiny stand-alone harness: parses a p610_t3_elastic JSON, runs the same Kirsch
// comparison logic the WPF VerificationViewModel runs, and prints a PASS/FAIL
// summary. Lets us validate the analytical pipeline without spinning up WPF.

using System.Globalization;
using System.Text.Json;
using GeotechSlopeApp.Analytical;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: KirschVerifyHarness <json_path> [tol_percent]");
    return 2;
}
string jsonPath = args[0];
double tolPct = args.Length > 1
    ? double.Parse(args[1], CultureInfo.InvariantCulture)
    : 5.0;

using var stream = File.OpenRead(jsonPath);
using var doc = JsonDocument.Parse(stream);
var root = doc.RootElement;

// ── meta block ──────────────────────────────────────────────────────────────
double E_kPa = 1e7, nu = 0.2, sigV = 100, K0 = 1, A = 1, Cx = 0, Cy = 0;
if (root.TryGetProperty("meta", out var meta))
{
    if (meta.TryGetProperty("material", out var mat))
    {
        if (mat.TryGetProperty("E_kPa", out var e)) E_kPa = e.GetDouble();
        if (mat.TryGetProperty("nu", out var n)) nu = n.GetDouble();
    }
    if (meta.TryGetProperty("loading", out var loa))
    {
        if (loa.TryGetProperty("sigma_v_kPa", out var s)) sigV = s.GetDouble();
        if (loa.TryGetProperty("K0", out var k)) K0 = k.GetDouble();
    }
    if (meta.TryGetProperty("tunnel", out var tun))
    {
        if (tun.TryGetProperty("a", out var a)) A = a.GetDouble();
        if (tun.TryGetProperty("cx", out var cx)) Cx = cx.GetDouble();
        if (tun.TryGetProperty("cy", out var cy)) Cy = cy.GetDouble();
    }
}
Console.WriteLine($"Prefilled from meta: E={E_kPa:G4} kPa, nu={nu}, sigV={sigV} kPa, K0={K0}, a={A:G6}, c=({Cx:G3},{Cy:G3})");

// ── nodes & last-stage displacements ────────────────────────────────────────
var nodes = new Dictionary<int, (double x, double y)>();
foreach (var n in root.GetProperty("nodes").EnumerateArray())
{
    nodes[n.GetProperty("id").GetInt32()] = (n.GetProperty("x").GetDouble(), n.GetProperty("y").GetDouble());
}

JsonElement lastStage = default; bool hasStages = false;
if (root.TryGetProperty("stages", out var stages) && stages.GetArrayLength() > 0)
{
    foreach (var st in stages.EnumerateArray()) { lastStage = st; hasStages = true; }
}
JsonElement nodeArrayForDisp = hasStages ? lastStage.GetProperty("nodes") : root.GetProperty("nodes");
var disp = new Dictionary<int, (double ux, double uy)>();
foreach (var n in nodeArrayForDisp.EnumerateArray())
{
    int id = n.GetProperty("id").GetInt32();
    double ux = n.TryGetProperty("ux", out var uxe) ? uxe.GetDouble() : 0;
    double uy = n.TryGetProperty("uy", out var uye) ? uye.GetDouble() : 0;
    disp[id] = (ux, uy);
}

// ── elements + element_stresses ─────────────────────────────────────────────
var elems = new List<(int id, int n1, int n2, int n3)>();
foreach (var e in root.GetProperty("elements").EnumerateArray())
{
    elems.Add((e.GetProperty("id").GetInt32(),
               e.GetProperty("n1").GetInt32(),
               e.GetProperty("n2").GetInt32(),
               e.GetProperty("n3").GetInt32()));
}

JsonElement esArr = hasStages && lastStage.TryGetProperty("element_stresses", out var es1)
    ? es1
    : root.GetProperty("element_stresses");
var stress = new Dictionary<int, (double sxx, double syy, double txy)>();
foreach (var s in esArr.EnumerateArray())
{
    stress[s.GetProperty("id").GetInt32()] = (
        s.GetProperty("sxx").GetDouble(),
        s.GetProperty("syy").GetDouble(),
        s.GetProperty("txy").GetDouble());
}

// ── replicate VerificationViewModel.BuildRows ───────────────────────────────
var inputs = new KirschCircularHole.Inputs(E_kPa, nu, -sigV, K0, A, Cx, Cy);

double[] thetas = { 0, 45, 90, 135, 180, 225, 270, 315 };
double[] rOverA = { 1.5, 2.0, 3.0, 5.0 };  // 1.1 picks the wall element on coarse meshes

var wall = new List<(int id, double dx, double dy, double r, double th)>();
foreach (var (id, p) in nodes)
{
    double dx = p.x - Cx, dy = p.y - Cy;
    double r = Math.Sqrt(dx * dx + dy * dy);
    if (r > 1e-12 && Math.Abs(r - A) / A < 0.05) wall.Add((id, dx, dy, r, Math.Atan2(dy, dx)));
}

var centroids = elems.Select(e => (
    cx: (nodes[e.n1].x + nodes[e.n2].x + nodes[e.n3].x) / 3.0,
    cy: (nodes[e.n1].y + nodes[e.n2].y + nodes[e.n3].y) / 3.0)).ToArray();

int total = 0, pass = 0;
void Add(string name, string desc, double an, double fem, string unit, double absTolForZero = 1e-6)
{
    double err;
    bool p;
    if (Math.Abs(an) < absTolForZero)
    {
        err = Math.Abs(fem) <= absTolForZero ? 0.0 : 100.0;
        p = Math.Abs(fem) <= absTolForZero;
    }
    else
    {
        err = (fem - an) / an * 100.0;
        p = Math.Abs(err) <= tolPct;
    }
    total++; if (p) pass++;
    Console.WriteLine($"  [{(p ? "PASS" : "FAIL")}] {name,-22} {desc,-44} an={an,12:G5}  fem={fem,12:G5} {unit,-3}  err={err,7:F2}%");
}

Console.WriteLine();
Console.WriteLine("--- wall checks ---");
foreach (var thDeg in thetas)
{
    double tt = thDeg * Math.PI / 180.0;
    (int id, double dx, double dy, double r, double th)? best = null;
    double bestSlip = double.PositiveInfinity;
    foreach (var w in wall)
    {
        double diff = w.th - tt;
        while (diff > Math.PI) diff -= 2 * Math.PI;
        while (diff <= -Math.PI) diff += 2 * Math.PI;
        double slip = Math.Abs(diff);
        if (slip < bestSlip) { bestSlip = slip; best = w; }
    }
    if (best is null || bestSlip * 180 / Math.PI > 15) continue;
    var b = best.Value;
    var (uxF, uyF) = disp[b.id];
    double rxHat = b.dx / b.r, ryHat = b.dy / b.r;
    double urFem = -(uxF * rxHat + uyF * ryHat);
    double urAn = KirschCircularHole.UrInward(inputs, b.th);
    string ang = $"th={thDeg:F0} (act {b.th * 180 / Math.PI:F1})";
    Add("Kirsch wall u_r", ang, urAn, urFem, "m", 1e-9);

    double sampleR = A * 0.99;
    double tx = Cx + sampleR * Math.Cos(b.th);
    double ty = Cy + sampleR * Math.Sin(b.th);
    int eIdx = -1; double bestD2 = double.PositiveInfinity;
    for (int i = 0; i < centroids.Length; i++)
    {
        double dx = centroids[i].cx - tx, dy = centroids[i].cy - ty;
        double d2 = dx * dx + dy * dy;
        if (d2 < bestD2) { bestD2 = d2; eIdx = i; }
    }
    if (eIdx < 0 || !stress.TryGetValue(elems[eIdx].id, out var sT)) continue;
    double cxC = centroids[eIdx].cx, cyC = centroids[eIdx].cy;
    double rActual = Math.Sqrt((cxC - Cx) * (cxC - Cx) + (cyC - Cy) * (cyC - Cy));
    double thActual = Math.Atan2(cyC - Cy, cxC - Cx);
    var (_, stF, _) = KirschCircularHole.PolarFromCartesian(sT.sxx, sT.syy, sT.txy, thActual);
    double stAn = KirschCircularHole.WallSigTheta(inputs, b.th);
    string ang2 = ang + $" centroid r/a={rActual / A:F3}";
    Add("Kirsch wall s_tt", ang2, stAn, stF, "kPa");
}

Console.WriteLine();
Console.WriteLine("--- springline radial profile (theta = 0) ---");
foreach (var k in rOverA)
{
    double r = k * A;
    double tx = Cx + r, ty = Cy;
    int eIdx = -1; double bestD2 = double.PositiveInfinity;
    for (int i = 0; i < centroids.Length; i++)
    {
        double dx = centroids[i].cx - tx, dy = centroids[i].cy - ty;
        double d2 = dx * dx + dy * dy;
        if (d2 < bestD2) { bestD2 = d2; eIdx = i; }
    }
    double dist = Math.Sqrt(bestD2);
    if (eIdx < 0 || dist > 0.5 * A) continue;
    if (!stress.TryGetValue(elems[eIdx].id, out var sT)) continue;
    double rAct = Math.Sqrt(Math.Pow(centroids[eIdx].cx - Cx, 2) + Math.Pow(centroids[eIdx].cy - Cy, 2));
    double thAct = Math.Atan2(centroids[eIdx].cy - Cy, centroids[eIdx].cx - Cx);
    var (srF, stF, _) = KirschCircularHole.PolarFromCartesian(sT.sxx, sT.syy, sT.txy, thAct);
    var (srAn, stAn) = KirschCircularHole.StressPolar(inputs, rAct, thAct);
    string desc = $"req r/a={k:F1}, act r/a={rAct / A:F3}";
    Add("Kirsch ray s_rr", desc, srAn, srF, "kPa", Math.Max(1.0, 0.05 * Math.Abs(sigV)));
    Add("Kirsch ray s_tt", desc, stAn, stF, "kPa");
}

Console.WriteLine();
Console.WriteLine("--- far-field ---");
double rOuter = 0;
foreach (var (_, p) in nodes)
{
    double dx = p.x - Cx, dy = p.y - Cy;
    double r = Math.Sqrt(dx * dx + dy * dy);
    if (r > rOuter) rOuter = r;
}
if (rOuter > 1.5 * A)
{
    double rFar = 0.85 * rOuter;
    double sxxSum = 0, syySum = 0; int n = 0;
    for (int i = 0; i < elems.Count; i++)
    {
        double r = Math.Sqrt(Math.Pow(centroids[i].cx - Cx, 2) + Math.Pow(centroids[i].cy - Cy, 2));
        if (r < rFar) continue;
        if (!stress.TryGetValue(elems[i].id, out var sT)) continue;
        sxxSum += sT.sxx; syySum += sT.syy; n++;
    }
    if (n > 0)
    {
        Add("Kirsch far-field", $"sxx avg ({n} elem)", -K0 * sigV, sxxSum / n, "kPa");
        Add("Kirsch far-field", $"syy avg ({n} elem)", -sigV, syySum / n, "kPa");
    }
}

Console.WriteLine();
Console.WriteLine($"Summary: {pass} / {total} checks passed (tol = {tolPct} %).");
return pass == total ? 0 : 1;
