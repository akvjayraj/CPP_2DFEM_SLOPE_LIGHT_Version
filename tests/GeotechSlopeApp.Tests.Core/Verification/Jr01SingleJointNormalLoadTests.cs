using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using GeotechSlopeApp.Persistence;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Verification;

[TestFixture]
public class Jr01SingleJointNormalLoadTests
{
    private const double ClosureTol = 0.01;
    private const double KnTol = 0.01;
    private const double EquilTol = 0.001;

    [Test]
    public void Xfem_DeserializesTwoBlocksJointAndTopLoad()
    {
        var path = Path.Combine(CaseDir(), "jr01_single_joint_normal_compression.xfem");
        Assert.That(File.Exists(path), Is.True, path);
        var doc = JsonSerializer.Deserialize<XfemProjectPersistence.XfemDocument>(File.ReadAllText(path), XfemJsonOptions());
        Assert.That(doc, Is.Not.Null);
        Assert.That(doc!.Geometry, Is.Not.Null);
        Assert.That(doc.Geometry!.Vertices, Has.Count.EqualTo(6));
        Assert.That(doc.Geometry.Surfaces, Has.Count.EqualTo(2));
        Assert.That(doc.Geometry.GeologicalFeatures, Has.Count.EqualTo(1));
        var joint = doc.Geometry.GeologicalFeatures![0];
        Assert.That(joint.EndCondition, Is.EqualTo("BothOpen"));
        Assert.That(joint.KnKpa, Is.EqualTo(1e5).Within(1e-6));
        Assert.That(doc.Materials!.MaterialLibrary![0].UnitWeight, Is.EqualTo(0).Within(1e-12));
        Assert.That(doc.Materials.MaterialLibrary[0].YoungsModulus, Is.EqualTo(3e7).Within(1));
        Assert.That(doc.Loads, Has.Count.EqualTo(1));
        Assert.That(doc.Loads![0].Magnitude, Is.EqualTo(10000).Within(1e-6));
        Assert.That(doc.FeAnalysis!.GravityEnabled, Is.False);
        Assert.That(doc.FeAnalysis.LayersOverride, Is.EqualTo(0));
        Assert.That(doc.Mesh, Is.Not.Null);
        Assert.That(doc.Mesh!.IsDiscretized, Is.True);
        Assert.That(doc.Mesh.DiscretizationNodes, Has.Count.EqualTo(162));
        Assert.That(doc.Mesh.Elements, Has.Count.EqualTo(256));
        Assert.That(doc.BoundaryConditions, Is.Not.Null);
        Assert.That(doc.BoundaryConditions!.Rows, Has.Count.EqualTo(9));
        Assert.That(doc.BoundaryConditions.Rows![0].BCType, Is.EqualTo("Fixed"));
        Assert.That(doc.BoundaryConditions.Rows.Skip(1).All(r => r.BCType == "Roller Y"), Is.True);
    }

    [Test]
    public void DeckBuilder_WritesSplitJointAndIndependentAnalyticalClosure()
    {
        var dat = Jr01DeckBuilder.BuildDat(Jr01DeckBuilder.Medium, -1000);
        Assert.That(dat, Does.Contain("! ninterface 8"));
        Assert.That(dat, Does.Contain("both_open"));
        Assert.That(dat, Does.Contain("100000"));
        Assert.That(Jr01DeckBuilder.AnalyticalClosureM(1000), Is.EqualTo(0.010).Within(1e-15));
        Assert.That(Jr01DeckBuilder.AnalyticalClosureM(2000), Is.EqualTo(0.020).Within(1e-15));
        Assert.That(Jr01DeckBuilder.AnalyticalClosureM(5000), Is.EqualTo(0.050).Within(1e-15));
        Assert.That(Jr01DeckBuilder.AnalyticalClosureM(10000), Is.EqualTo(0.100).Within(1e-15));
    }

    [Test]
    public void WriteStandardDecks_CreatesVerificationFiles()
    {
        var decks = Path.Combine(CaseDir(), "decks");
        Jr01DeckBuilder.WriteStandardDecks(decks);
        Assert.That(File.Exists(Path.Combine(decks, "jr01_medium_1mpa.dat")), Is.True);
        Assert.That(File.Exists(Path.Combine(decks, "jr01_coarse_10mpa.dat")), Is.True);
        Assert.That(File.Exists(Path.Combine(decks, "jr01_fine_10mpa.dat")), Is.True);
        Assert.That(File.Exists(Path.Combine(decks, "jr01_medium_unload_0mpa.dat")), Is.True);
        Assert.That(File.Exists(Path.Combine(decks, "jr01_medium_tension.dat")), Is.True);
    }

    [Test]
    public void SingleJointNormalLoad_1MPa() => AssertCompression(1000);

    [Test]
    public void SingleJointNormalLoad_2MPa() => AssertCompression(2000);

    [Test]
    public void SingleJointNormalLoad_5MPa() => AssertCompression(5000);

    [Test]
    public void SingleJointNormalLoad_10MPa() => AssertCompression(10000);

    [Test]
    public void SingleJointNormalLoad_LinearResponse()
    {
        var pts = new List<(double Sigma, double Closure)>();
        foreach (double sigma in new[] { 1000.0, 2000.0, 5000.0, 10000.0 })
        {
            var r = RunOrIgnore(Jr01DeckBuilder.Medium, -sigma);
            pts.Add((sigma, r.AvgClosure));
        }
        FitSigmaVsClosure(pts, out double knFit, out double r2);
        var diag = FormatPoints(pts) + $"\nk_fit={knFit:G6} kPa/m  R2={r2:F6}";
        Assert.That(Math.Abs(knFit - Jr01DeckBuilder.KnKpaPerM) / Jr01DeckBuilder.KnKpaPerM, Is.LessThan(KnTol), diag);
        Assert.That(r2, Is.GreaterThan(0.999), diag);
    }

    [Test]
    public void SingleJointNormalLoad_ForceEquilibrium()
    {
        var r = RunOrIgnore(Jr01DeckBuilder.Medium, -10000);
        double fApplied = 10000 * Jr01DeckBuilder.WidthM;
        double eps = Math.Abs(Math.Abs(r.ReactionFy) - fApplied) / fApplied;
        Assert.That(eps, Is.LessThan(EquilTol), r.Diagnostics + $"\nF_applied={fApplied}  F_reaction={r.ReactionFy}  eps_F={eps:P4}");
    }

    [Test]
    public void SingleJointNormalLoad_LoadUnload()
    {
        var load = new[] { 1000.0, 2000.0, 5000.0, 10000.0 };
        var unload = new[] { 10000.0, 5000.0, 2000.0, 1000.0, 0.0 };
        var loaded = new Dictionary<double, double>();
        foreach (var s in load)
            loaded[s] = RunOrIgnore(Jr01DeckBuilder.Medium, -s).AvgClosure;
        foreach (var s in unload)
        {
            var r = RunOrIgnore(Jr01DeckBuilder.Medium, -s, increments: s == 0 ? 1 : 5);
            if (s == 0)
            {
                Assert.That(Math.Abs(r.AvgClosure), Is.LessThan(1e-6), r.Diagnostics);
                continue;
            }
            double err = Math.Abs(r.AvgClosure - loaded[s]) / Math.Max(loaded[s], 1e-12);
            Assert.That(err, Is.LessThan(1e-6), r.Diagnostics + $"\nunload mismatch at {s} kPa");
        }
    }

    [Test]
    public void SingleJointNormalLoad_MeshConvergence()
    {
        const double sigma = 10000;
        double analytical = Jr01DeckBuilder.AnalyticalClosureM(sigma);
        foreach (var mesh in new[] { Jr01DeckBuilder.Coarse, Jr01DeckBuilder.Medium, Jr01DeckBuilder.Fine })
        {
            var r = RunOrIgnore(mesh, -sigma);
            double err = Math.Abs(r.AvgClosure - analytical) / analytical;
            Assert.That(err, Is.LessThan(ClosureTol), r.Diagnostics + $"\nmesh={mesh.Name} err={err:P4}");
        }
    }

    [Test]
    public void SingleJointNormalLoad_TensionCutoff_OpensWithoutTension()
    {
        var r = RunOrIgnore(Jr01DeckBuilder.Medium, 100);
        Assert.That(r.AvgUn, Is.GreaterThan(0), r.Diagnostics);
        Assert.That(Math.Abs(r.AvgSn), Is.LessThan(1.0), r.Diagnostics);
        Assert.That(r.AnySlipped, Is.True, r.Diagnostics);
    }

    private static void AssertCompression(double sigmaKpa)
    {
        var r = RunOrIgnore(Jr01DeckBuilder.Medium, -sigmaKpa);
        double analytical = Jr01DeckBuilder.AnalyticalClosureM(sigmaKpa);
        double err = Math.Abs(r.AvgClosure - analytical) / analytical;
        double knNum = r.AvgClosure > 1e-15 ? r.AvgSn / r.AvgClosure : double.NaN;
        Assert.That(err, Is.LessThan(ClosureTol), r.Diagnostics + $"\nδ_num={r.AvgClosure} δ_an={analytical} err={err:P4}");
        Assert.That(Math.Abs(knNum - Jr01DeckBuilder.KnKpaPerM) / Jr01DeckBuilder.KnKpaPerM, Is.LessThan(KnTol),
            r.Diagnostics + $"\nkn_num={knNum}");
    }

    private sealed class JointRun
    {
        public double AvgClosure;
        public double MinClosure;
        public double MaxClosure;
        public double AvgSn;
        public double AvgUn;
        public double ReactionFy;
        public bool AnySlipped;
        public string Diagnostics = "";
    }

    private static JointRun RunOrIgnore(Jr01DeckBuilder.MeshSpec mesh, double syyKpa, int increments = 5)
    {
        var exe = FindP610();
        if (exe == null)
            Assert.Ignore("p610_t3_elastoplastic.exe not found. Build the C++ solver to run JR-01.");

        var work = Path.Combine(Path.GetTempPath(), "JR01_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var dat = Jr01DeckBuilder.BuildDat(mesh, syyKpa, increments);
        File.WriteAllText(Path.Combine(work, "jr01.dat"), dat);
        var psi = new ProcessStartInfo(exe, "jr01")
        {
            WorkingDirectory = work,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("failed to start solver");
        if (!p.WaitForExit(180_000))
        {
            try { p.Kill(true); } catch { /* ignore */ }
            Assert.Fail("p610 timed out");
        }
        var jsonPath = Path.Combine(work, "jr01.json");
        if (!File.Exists(jsonPath))
            Assert.Fail($"solver produced no JSON (exit {p.ExitCode})\n{p.StandardError.ReadToEnd()}\n{p.StandardOutput.ReadToEnd()}");

        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var root = doc.RootElement;
        if (!root.TryGetProperty("joints", out var joints) || joints.GetArrayLength() == 0)
            Assert.Fail("JSON missing joints[] — rebuild p610_t3_elastoplastic with JR-01 export.");

        double sumC = 0, minC = double.MaxValue, maxC = double.MinValue, sumSn = 0, sumUn = 0;
        bool slipped = false;
        var sb = new StringBuilder();
        sb.AppendLine($"mesh={mesh.Name} syy={syyKpa} kPa");
        int n = 0;
        foreach (var j in joints.EnumerateArray())
        {
            double c = j.GetProperty("closure").GetDouble();
            double sn = j.GetProperty("sn_kPa").GetDouble();
            double un = j.GetProperty("un").GetDouble();
            sumC += c; minC = Math.Min(minC, c); maxC = Math.Max(maxC, c);
            sumSn += sn; sumUn += un;
            slipped |= j.TryGetProperty("slipped", out var sl) && sl.GetBoolean();
            n++;
            sb.AppendLine(
                $"  iface n={j.GetProperty("n1").GetInt32()}-{j.GetProperty("n2").GetInt32()}-{j.GetProperty("n3").GetInt32()}-{j.GetProperty("n4").GetInt32()}" +
                $"  n=({j.GetProperty("nx").GetDouble():G4},{j.GetProperty("ny").GetDouble():G4})" +
                $"  u-=({j.GetProperty("ux_minus").GetDouble():G4},{j.GetProperty("uy_minus").GetDouble():G4})" +
                $"  u+=({j.GetProperty("ux_plus").GetDouble():G4},{j.GetProperty("uy_plus").GetDouble():G4})" +
                $"  un={un:G6} closure={c:G6} sn={sn:G6} L={j.GetProperty("L").GetDouble():G4}");
        }
        double fy = 0;
        if (root.TryGetProperty("reactions", out var reac))
            fy = reac.GetProperty("fy").GetDouble();
        sb.AppendLine($"F_reaction_fy={fy}");
        try { Directory.Delete(work, true); } catch { /* best-effort */ }
        return new JointRun
        {
            AvgClosure = sumC / n,
            MinClosure = minC,
            MaxClosure = maxC,
            AvgSn = sumSn / n,
            AvgUn = sumUn / n,
            ReactionFy = fy,
            AnySlipped = slipped,
            Diagnostics = sb.ToString()
        };
    }

    private static void FitSigmaVsClosure(IReadOnlyList<(double Sigma, double Closure)> pts, out double knFit, out double r2)
    {
        double mx = pts.Average(p => p.Closure);
        double my = pts.Average(p => p.Sigma);
        double num = 0, den = 0, ssTot = 0, ssRes = 0;
        foreach (var (sigma, closure) in pts)
        {
            num += (closure - mx) * (sigma - my);
            den += (closure - mx) * (closure - mx);
        }
        knFit = den > 0 ? num / den : 0;
        double intercept = my - knFit * mx;
        foreach (var (sigma, closure) in pts)
        {
            ssTot += (sigma - my) * (sigma - my);
            double pred = intercept + knFit * closure;
            ssRes += (sigma - pred) * (sigma - pred);
        }
        r2 = ssTot > 0 ? 1.0 - ssRes / ssTot : 1.0;
    }

    private static string FormatPoints(IEnumerable<(double Sigma, double Closure)> pts)
        => string.Join("\n", pts.Select(p => $"  σ={p.Sigma}  δ={p.Closure.ToString("G8", CultureInfo.InvariantCulture)}"));

    private static string? FindP610()
    {
        var root = FindRepoRoot();
        var names = new[]
        {
            Path.Combine(root, "library", "solver", "Structural-Static Elasticity", "p610_t3_elastoplastic.exe"),
            Path.Combine(root, "library", "solver", "p610_t3_elastoplastic.exe"),
            Path.Combine(root, "build", "Release", "p610_t3_elastoplastic.exe"),
            Path.Combine(root, "build", "Debug", "p610_t3_elastoplastic.exe"),
            Path.Combine(root, "build_verify_gw", "Release", "p610_t3_elastoplastic.exe"),
            Path.Combine(root, "build_verify_gw", "Debug", "p610_t3_elastoplastic.exe")
        };
        return names.FirstOrDefault(File.Exists);
    }

    private static string CaseDir()
        => Path.Combine(FindRepoRoot(), "Verification", "slope", "joint verification", "JR01_SingleJointNormalCompression");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CMakeLists.txt"))
                && Directory.Exists(Path.Combine(dir.FullName, "Verification")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", ".."));
    }

    private static JsonSerializerOptions XfemJsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
