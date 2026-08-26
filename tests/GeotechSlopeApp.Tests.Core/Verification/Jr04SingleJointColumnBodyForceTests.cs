using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using GeotechSlopeApp.Persistence;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Verification;

[TestFixture]
public class Jr04SingleJointColumnBodyForceTests
{
    private const double RelTol = 0.02;
    private const double EquilTol = 0.03;

    [Test]
    public void Xfem_DeserializesRs3Case2Column()
    {
        var path = Path.Combine(CaseDir(), "jr04_single_joint_column_bodyforce.xfem");
        Assert.That(File.Exists(path), Is.True, path);
        var doc = JsonSerializer.Deserialize<XfemProjectPersistence.XfemDocument>(
            File.ReadAllText(path), XfemJsonOptions());
        Assert.That(doc, Is.Not.Null);
        Assert.That(doc!.Geometry!.GeologicalFeatures![0].Vertices![0].Y, Is.EqualTo(1.5).Within(1e-12));
        Assert.That(doc.Materials!.MaterialLibrary![0].UnitWeight, Is.EqualTo(Jr04DeckBuilder.GammaKpaPerM).Within(1));
        Assert.That(doc.Materials.MaterialLibrary[0].YoungsModulus, Is.EqualTo(Jr04DeckBuilder.EKpa).Within(1));
        Assert.That(doc.FeAnalysis!.GravityEnabled, Is.True);
        Assert.That(doc.FeAnalysis.LayersOverride, Is.EqualTo(1));
        Assert.That(doc.Loads == null || doc.Loads.Count == 0, Is.True);
        Assert.That(doc.Mesh!.DiscretizationNodes, Has.Count.EqualTo(225));
        Assert.That(doc.Mesh.Elements, Has.Count.EqualTo(384));
    }

    [Test]
    public void DeckBuilder_DebDasBodyForceFormula()
    {
        Assert.That(Jr04DeckBuilder.OverburdenAtJointKpa, Is.EqualTo(15000).Within(1e-9));
        Assert.That(Jr04DeckBuilder.AnalyticalClosureM(), Is.EqualTo(0.0015).Within(1e-15));
        Assert.That(Jr04DeckBuilder.AnalyticalUyDownM(0), Is.EqualTo(0).Within(1e-15));
        Assert.That(Jr04DeckBuilder.AnalyticalUyDownM(3), Is.EqualTo(0.024).Within(1e-15));
        var dat = Jr04DeckBuilder.BuildDat(Jr04DeckBuilder.Medium);
        Assert.That(dat, Does.Contain("10000"));
        Assert.That(dat, Does.Contain("80  5  1"));
    }

    [Test]
    public void WriteStandardDecks_CreatesVerificationFiles()
    {
        var decks = Path.Combine(CaseDir(), "decks");
        Jr04DeckBuilder.WriteStandardDecks(decks);
        Assert.That(File.Exists(Path.Combine(decks, "jr04_medium.dat")), Is.True);
        Assert.That(File.Exists(Path.Combine(decks, "jr04_coarse.dat")), Is.True);
        Assert.That(File.Exists(Path.Combine(decks, "jr04_fine.dat")), Is.True);
    }

    [Test]
    public void Rs3Case2_JointClosureAndOverburden()
    {
        var r = RunOrIgnore(Jr04DeckBuilder.Medium);
        double dcAn = Jr04DeckBuilder.AnalyticalClosureM();
        Assert.That(Math.Abs(r.AvgClosure - dcAn) / dcAn, Is.LessThan(RelTol), r.Diagnostics);
        Assert.That(Math.Abs(r.AvgSn - Jr04DeckBuilder.OverburdenAtJointKpa) / Jr04DeckBuilder.OverburdenAtJointKpa,
            Is.LessThan(RelTol), r.Diagnostics);
    }

    [Test]
    public void Rs3Case2_DisplacementProfile()
    {
        var r = RunOrIgnore(Jr04DeckBuilder.Medium);
        foreach (double y in new[] { 0.0, 0.75, 2.25, 3.0 })
        {
            double num = r.UyDownAt(0.5, y);
            double an = Jr04DeckBuilder.AnalyticalUyDownM(y);
            double err = an < 1e-12 ? Math.Abs(num) : Math.Abs(num - an) / an;
            Assert.That(err, Is.LessThan(RelTol), r.Diagnostics + $"\ny={y} u_num={num} u_an={an}");
        }
    }

    [Test]
    public void Rs3Case2_WeightEquilibrium()
    {
        var r = RunOrIgnore(Jr04DeckBuilder.Medium);
        double w = Jr04DeckBuilder.GammaKpaPerM * Jr04DeckBuilder.HeightM * Jr04DeckBuilder.WidthM;
        double eps = Math.Abs(Math.Abs(r.ReactionFy) - w) / w;
        Assert.That(eps, Is.LessThan(EquilTol), r.Diagnostics + $"\nW={w} Fy={r.ReactionFy}");
    }

    private sealed class ColumnRun
    {
        public double AvgClosure;
        public double AvgSn;
        public double ReactionFy;
        public List<(double X, double Y, double Uy)> Nodes = new();
        public string Diagnostics = "";

        public double UyDownAt(double x, double y)
        {
            var hits = Nodes.Where(n => Math.Abs(n.X - x) < 1e-6 && Math.Abs(n.Y - y) < 1e-6).ToList();
            if (hits.Count == 0)
                return -Nodes.OrderBy(n => Math.Abs(n.X - x) + Math.Abs(n.Y - y)).First().Uy;
            return -hits.Average(h => h.Uy);
        }
    }

    private static ColumnRun RunOrIgnore(Jr03DeckBuilder.MeshSpec mesh)
    {
        var exe = Jr03Solver.FindP610();
        if (exe == null)
            Assert.Ignore("p610_t3_elastoplastic.exe not found.");

        var work = Path.Combine(Path.GetTempPath(), "JR04_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, "jr04.dat"), Jr04DeckBuilder.BuildDat(mesh));
        using var doc = Jr03Solver.RunJson(exe, work, "jr04");
        var root = doc.RootElement;
        if (!root.TryGetProperty("joints", out var joints) || joints.GetArrayLength() == 0)
            Assert.Fail("JSON missing joints[]");

        double sumC = 0, sumSn = 0;
        int n = 0;
        foreach (var j in joints.EnumerateArray())
        {
            sumC += j.GetProperty("closure").GetDouble();
            sumSn += j.GetProperty("sn_kPa").GetDouble();
            n++;
        }
        double fy = root.TryGetProperty("reactions", out var reac) ? reac.GetProperty("fy").GetDouble() : 0;
        var nodes = new List<(double X, double Y, double Uy)>();
        if (root.TryGetProperty("nodes", out var nodeArr))
        {
            foreach (var nd in nodeArr.EnumerateArray())
                nodes.Add((nd.GetProperty("x").GetDouble(), nd.GetProperty("y").GetDouble(), nd.GetProperty("uy").GetDouble()));
        }
        var sb = new StringBuilder();
        sb.AppendLine($"mesh={mesh.Name} closure={sumC / n} sn={sumSn / n} Fy={fy}");
        try { Directory.Delete(work, true); } catch { /* best-effort */ }
        return new ColumnRun
        {
            AvgClosure = sumC / n,
            AvgSn = sumSn / n,
            ReactionFy = fy,
            Nodes = nodes,
            Diagnostics = sb.ToString()
        };
    }

    private static string CaseDir()
        => Path.Combine(Jr03Solver.FindRepoRoot(), "Verification", "slope", "joint verification", "JR04_SingleJointColumnBodyForce");

    private static JsonSerializerOptions XfemJsonOptions() => Jr03Solver.XfemJsonOptions();
}
