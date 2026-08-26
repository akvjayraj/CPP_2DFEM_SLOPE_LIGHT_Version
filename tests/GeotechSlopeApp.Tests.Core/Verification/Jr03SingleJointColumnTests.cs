using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using GeotechSlopeApp.Persistence;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Verification;

[TestFixture]
public class Jr03SingleJointColumnTests
{
    private const double RelTol = 0.01;
    private const double EquilTol = 0.001;

    [Test]
    public void Xfem_DeserializesRs3Case1Column()
    {
        var path = Path.Combine(CaseDir(), "jr03_single_joint_column_axial.xfem");
        Assert.That(File.Exists(path), Is.True, path);
        var doc = JsonSerializer.Deserialize<XfemProjectPersistence.XfemDocument>(
            File.ReadAllText(path), XfemJsonOptions());
        Assert.That(doc, Is.Not.Null);
        Assert.That(doc!.Geometry!.Vertices, Has.Count.EqualTo(6));
        Assert.That(doc.Geometry.Surfaces, Has.Count.EqualTo(2));
        var joint = doc.Geometry.GeologicalFeatures![0];
        Assert.That(joint.EndCondition, Is.EqualTo("BothOpen"));
        Assert.That(joint.KnKpa, Is.EqualTo(Jr03DeckBuilder.KnKpaPerM).Within(1e-6));
        Assert.That(joint.Vertices, Has.Count.EqualTo(2));
        Assert.That(joint.Vertices![0].Y, Is.EqualTo(Jr03DeckBuilder.JointYM).Within(1e-12));
        Assert.That(joint.Vertices[1].Y, Is.EqualTo(Jr03DeckBuilder.JointYM).Within(1e-12));
        Assert.That(doc.Materials!.MaterialLibrary![0].YoungsModulus, Is.EqualTo(Jr03DeckBuilder.EKpa).Within(1));
        Assert.That(doc.Materials.MaterialLibrary[0].PoissonsRatio, Is.EqualTo(Jr03DeckBuilder.Nu).Within(1e-12));
        Assert.That(doc.Materials.MaterialLibrary[0].UnitWeight, Is.EqualTo(0).Within(1e-12));
        Assert.That(doc.Loads![0].Magnitude, Is.EqualTo(1000).Within(1e-6));
        Assert.That(doc.FeAnalysis!.GravityEnabled, Is.False);
        Assert.That(doc.Mesh!.IsDiscretized, Is.True);
        Assert.That(doc.Mesh.DiscretizationNodes, Has.Count.EqualTo(225));
        Assert.That(doc.Mesh.Elements, Has.Count.EqualTo(384));
        Assert.That(doc.BoundaryConditions!.Rows, Has.Count.EqualTo(9));
        Assert.That(doc.BoundaryConditions.Rows![0].BCType, Is.EqualTo("Fixed"));
        Assert.That(doc.BoundaryConditions.Rows.Skip(1).All(r => r.BCType == "Roller Y"), Is.True);
    }

    [Test]
    public void DeckBuilder_WritesSplitJointAndDebDasDisplacement()
    {
        var dat = Jr03DeckBuilder.BuildDat(Jr03DeckBuilder.Medium, -1000);
        Assert.That(dat, Does.Contain("! ninterface 8"));
        Assert.That(dat, Does.Contain("both_open"));
        Assert.That(dat, Does.Contain("10000000"));
        Assert.That(Jr03DeckBuilder.AnalyticalClosureM(1000), Is.EqualTo(0.0001).Within(1e-15));
        Assert.That(Jr03DeckBuilder.AnalyticalTopUM(1000), Is.EqualTo(0.0016).Within(1e-15));
        Assert.That(Jr03DeckBuilder.AnalyticalUyDownM(0.75, 1000), Is.EqualTo(0.000375).Within(1e-15));
        Assert.That(Jr03DeckBuilder.AnalyticalUyDownM(1.5, 1000), Is.EqualTo(0.00085).Within(1e-15));
        Assert.That(Jr03DeckBuilder.AnalyticalUyDownM(1.499, 1000), Is.EqualTo(0.0007495).Within(1e-15));
    }

    [Test]
    public void WriteStandardDecks_CreatesVerificationFiles()
    {
        var decks = Path.Combine(CaseDir(), "decks");
        Jr03DeckBuilder.WriteStandardDecks(decks);
        Assert.That(File.Exists(Path.Combine(decks, "jr03_medium_1mpa.dat")), Is.True);
        Assert.That(File.Exists(Path.Combine(decks, "jr03_medium_2mpa.dat")), Is.True);
        Assert.That(File.Exists(Path.Combine(decks, "jr03_coarse_1mpa.dat")), Is.True);
        Assert.That(File.Exists(Path.Combine(decks, "jr03_fine_1mpa.dat")), Is.True);
    }

    [Test]
    public void Rs3Case1_1MPa_JointClosure()
    {
        var r = RunOrIgnore(Jr03DeckBuilder.Medium, -1000);
        double an = Jr03DeckBuilder.AnalyticalClosureM(1000);
        double err = Math.Abs(r.AvgClosure - an) / an;
        Assert.That(err, Is.LessThan(RelTol), r.Diagnostics + $"\nδ_num={r.AvgClosure} δ_an={an}");
        Assert.That(Math.Abs(r.AvgSn - 1000) / 1000, Is.LessThan(RelTol), r.Diagnostics);
    }

    [Test]
    public void Rs3Case1_1MPa_DisplacementProfile()
    {
        var r = RunOrIgnore(Jr03DeckBuilder.Medium, -1000);
        foreach (double y in new[] { 0.0, 0.75, 2.25, 3.0 })
        {
            double num = r.UyDownAt(0.5, y);
            double an = Jr03DeckBuilder.AnalyticalUyDownM(y, 1000);
            double err = an < 1e-12 ? Math.Abs(num) : Math.Abs(num - an) / an;
            Assert.That(err, Is.LessThan(RelTol),
                r.Diagnostics + $"\ny={y} u_num={num} u_an={an} err={err:P4}");
        }
        double uMinus = r.JointUyDownMinus;
        double uPlus = r.JointUyDownPlus;
        double anMinus = 1000 * Jr03DeckBuilder.JointYM / Jr03DeckBuilder.EKpa;
        double anPlus = anMinus + Jr03DeckBuilder.AnalyticalClosureM(1000);
        Assert.That(Math.Abs(uMinus - anMinus) / anMinus, Is.LessThan(RelTol),
            r.Diagnostics + $"\njoint- u_num={uMinus} u_an={anMinus}");
        Assert.That(Math.Abs(uPlus - anPlus) / anPlus, Is.LessThan(RelTol),
            r.Diagnostics + $"\njoint+ u_num={uPlus} u_an={anPlus}");
    }

    [Test]
    public void Rs3Case1_1MPa_ForceEquilibrium()
    {
        var r = RunOrIgnore(Jr03DeckBuilder.Medium, -1000);
        double fApplied = 1000 * Jr03DeckBuilder.WidthM;
        double eps = Math.Abs(Math.Abs(r.ReactionFy) - fApplied) / fApplied;
        Assert.That(eps, Is.LessThan(EquilTol), r.Diagnostics + $"\nF_applied={fApplied} F_r={r.ReactionFy}");
    }

    [Test]
    public void Rs3Case1_MeshConvergence_1MPa()
    {
        const double sigma = 1000;
        double dcAn = Jr03DeckBuilder.AnalyticalClosureM(sigma);
        double uTopAn = Jr03DeckBuilder.AnalyticalTopUM(sigma);
        foreach (var mesh in new[] { Jr03DeckBuilder.Coarse, Jr03DeckBuilder.Medium, Jr03DeckBuilder.Fine })
        {
            var r = RunOrIgnore(mesh, -sigma);
            Assert.That(Math.Abs(r.AvgClosure - dcAn) / dcAn, Is.LessThan(RelTol),
                r.Diagnostics + $"\nmesh={mesh.Name} closure");
            double uTop = r.UyDownAt(0.5, Jr03DeckBuilder.HeightM);
            Assert.That(Math.Abs(uTop - uTopAn) / uTopAn, Is.LessThan(RelTol),
                r.Diagnostics + $"\nmesh={mesh.Name} u_top={uTop}");
        }
    }

    [Test]
    public void Rs3Case1_Linearity_1And2MPa()
    {
        var a = RunOrIgnore(Jr03DeckBuilder.Medium, -1000);
        var b = RunOrIgnore(Jr03DeckBuilder.Medium, -2000);
        Assert.That(b.AvgClosure / a.AvgClosure, Is.EqualTo(2.0).Within(0.01), a.Diagnostics + b.Diagnostics);
        Assert.That(b.UyDownAt(0.5, 3.0) / a.UyDownAt(0.5, 3.0), Is.EqualTo(2.0).Within(0.01));
    }

    private sealed class ColumnRun
    {
        public double AvgClosure;
        public double AvgSn;
        public double ReactionFy;
        public double JointUyDownMinus;
        public double JointUyDownPlus;
        public List<(double X, double Y, double Uy)> Nodes = new();
        public string Diagnostics = "";

        public double UyDownAt(double x, double y)
        {
            var hits = Nodes.Where(n => Math.Abs(n.X - x) < 1e-6 && Math.Abs(n.Y - y) < 1e-6).ToList();
            if (hits.Count == 0)
            {
                var nearest = Nodes.OrderBy(n => Math.Abs(n.X - x) + Math.Abs(n.Y - y)).First();
                return -nearest.Uy;
            }
            if (Math.Abs(y - Jr03DeckBuilder.JointYM) < 1e-9 && hits.Count >= 2)
                return -hits.Average(h => h.Uy);
            return -hits[0].Uy;
        }
    }

    private static ColumnRun RunOrIgnore(Jr03DeckBuilder.MeshSpec mesh, double syyKpa, int increments = 5)
    {
        var exe = FindP610();
        if (exe == null)
            Assert.Ignore("p610_t3_elastoplastic.exe not found. Build the C++ solver to run JR-03.");

        var work = Path.Combine(Path.GetTempPath(), "JR03_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, "jr03.dat"), Jr03DeckBuilder.BuildDat(mesh, syyKpa, increments));
        var psi = new ProcessStartInfo(exe, "jr03")
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
        var jsonPath = Path.Combine(work, "jr03.json");
        if (!File.Exists(jsonPath))
            Assert.Fail($"solver produced no JSON (exit {p.ExitCode})\n{p.StandardError.ReadToEnd()}\n{p.StandardOutput.ReadToEnd()}");

        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var root = doc.RootElement;
        if (!root.TryGetProperty("joints", out var joints) || joints.GetArrayLength() == 0)
            Assert.Fail("JSON missing joints[]");

        double sumC = 0, sumSn = 0, sumUyMinus = 0, sumUyPlus = 0;
        int n = 0;
        var sb = new StringBuilder();
        sb.AppendLine($"mesh={mesh.Name} syy={syyKpa} kPa");
        foreach (var j in joints.EnumerateArray())
        {
            sumC += j.GetProperty("closure").GetDouble();
            sumSn += j.GetProperty("sn_kPa").GetDouble();
            sumUyMinus += j.GetProperty("uy_minus").GetDouble();
            sumUyPlus += j.GetProperty("uy_plus").GetDouble();
            n++;
        }
        double fy = 0;
        if (root.TryGetProperty("reactions", out var reac))
            fy = reac.GetProperty("fy").GetDouble();

        var nodes = new List<(double X, double Y, double Uy)>();
        if (root.TryGetProperty("nodes", out var nodeArr))
        {
            foreach (var nd in nodeArr.EnumerateArray())
                nodes.Add((nd.GetProperty("x").GetDouble(), nd.GetProperty("y").GetDouble(), nd.GetProperty("uy").GetDouble()));
        }

        sb.AppendLine($"closure={sumC / n} sn={sumSn / n} Fy={fy} u-_joint={-sumUyMinus / n} u+_joint={-sumUyPlus / n}");
        try { Directory.Delete(work, true); } catch { /* best-effort */ }
        return new ColumnRun
        {
            AvgClosure = sumC / n,
            AvgSn = sumSn / n,
            ReactionFy = fy,
            JointUyDownMinus = -sumUyMinus / n,
            JointUyDownPlus = -sumUyPlus / n,
            Nodes = nodes,
            Diagnostics = sb.ToString()
        };
    }

    private static string? FindP610()
    {
        var root = FindRepoRoot();
        var names = new[]
        {
            Path.Combine(root, "library", "solver", "Structural-Static Elasticity", "p610_t3_elastoplastic.exe"),
            Path.Combine(root, "library", "solver", "p610_t3_elastoplastic.exe"),
            Path.Combine(root, "build", "Release", "p610_t3_elastoplastic.exe"),
            Path.Combine(root, "build", "Debug", "p610_t3_elastoplastic.exe")
        };
        return names.FirstOrDefault(File.Exists);
    }

    private static string CaseDir()
        => Path.Combine(FindRepoRoot(), "Verification", "slope", "joint verification", "JR03_SingleJointColumnAxial");

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
