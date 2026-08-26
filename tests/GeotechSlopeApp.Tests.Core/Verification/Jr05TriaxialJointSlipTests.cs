using System.IO;
using System.Text;
using System.Text.Json;
using GeotechSlopeApp.Persistence;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Verification;

[TestFixture]
public class Jr05TriaxialJointSlipTests
{
    private const double RelTol = 0.02;

    [Test]
    public void Xfem_DeserializesRs3Case4_45Deg()
    {
        var path = Path.Combine(CaseDir(), "jr05_triaxial_joint_slip.xfem");
        Assert.That(File.Exists(path), Is.True, path);
        var doc = JsonSerializer.Deserialize<XfemProjectPersistence.XfemDocument>(
            File.ReadAllText(path), Jr03Solver.XfemJsonOptions());
        Assert.That(doc, Is.Not.Null);
        var joint = doc!.Geometry!.GeologicalFeatures![0];
        Assert.That(joint.DipAngleDeg, Is.EqualTo(45).Within(1e-9));
        Assert.That(joint.KnKpa, Is.EqualTo(Jr05DeckBuilder.KnKpaPerM).Within(1));
        Assert.That(joint.JointCohesionKpa.GetValueOrDefault(), Is.EqualTo(Jr05DeckBuilder.Case1CohesionKpa).Within(1));
        Assert.That(joint.JointFrictionDeg.GetValueOrDefault(), Is.EqualTo(30).Within(1e-9));
        Assert.That(doc.Loads, Has.Count.EqualTo(3));
        Assert.That(doc.Mesh!.DiscretizationNodes, Has.Count.EqualTo(225));
        Assert.That(doc.Mesh.Elements, Has.Count.EqualTo(384));
    }

    [Test]
    public void JaegerFormula_MatchesRs2Table27()
    {
        double s145 = Jr05DeckBuilder.AnalyticalSigma1Kpa(
            Jr05DeckBuilder.Case1Sigma3Kpa, Jr05DeckBuilder.Case1CohesionKpa, 45);
        Assert.That(s145, Is.EqualTo(183313).Within(50));
        var (sn, tau) = Jr05DeckBuilder.CauchyOnJoint(s145, Jr05DeckBuilder.Case1Sigma3Kpa, 45);
        double mc = Jr05DeckBuilder.Case1CohesionKpa + sn * Math.Tan(30 * Math.PI / 180.0);
        Assert.That(tau, Is.EqualTo(mc).Within(1.0));
    }

    [Test]
    public void WriteStandardDecks_CreatesAngleSeries()
    {
        var decks = Path.Combine(CaseDir(), "decks");
        Jr05DeckBuilder.WriteStandardDecks(decks);
        Assert.That(File.Exists(Path.Combine(decks, "jr05_c1_45deg.dat")), Is.True);
        Assert.That(File.Exists(Path.Combine(decks, "jr05_c2_60deg.dat")), Is.True);
    }

    [Test]
    public void Rs3Case4_Case1_45Deg_OnYieldSurface() => AssertOnYield(
        45, Jr05DeckBuilder.Case1Sigma3Kpa, Jr05DeckBuilder.Case1CohesionKpa);

    [Test]
    public void Rs3Case4_Case2_45Deg_OnYieldSurface() => AssertOnYield(
        45, Jr05DeckBuilder.Case2Sigma3Kpa, Jr05DeckBuilder.Case2CohesionKpa);

    [Test]
    public void Rs3Case4_Case1_60Deg_OnYieldSurface() => AssertOnYield(
        60, Jr05DeckBuilder.Case1Sigma3Kpa, Jr05DeckBuilder.Case1CohesionKpa);

    private static void AssertOnYield(double beta, double s3, double c)
    {
        double s1 = Jr05DeckBuilder.AnalyticalSigma1Kpa(s3, c, beta);
        var (snAn, tauAn) = Jr05DeckBuilder.CauchyOnJoint(s1, s3, beta);
        var r = RunOrIgnore(beta, s3, c, s1);
        Assert.That(Math.Abs(r.AvgSn - snAn) / snAn, Is.LessThan(RelTol), r.Diagnostics);
        Assert.That(Math.Abs(Math.Abs(r.AvgTau) - tauAn) / tauAn, Is.LessThan(RelTol), r.Diagnostics);
        double mc = c + r.AvgSn * Math.Tan(Jr05DeckBuilder.PhiDeg * Math.PI / 180.0);
        Assert.That(Math.Abs(Math.Abs(r.AvgTau) - mc) / mc, Is.LessThan(RelTol), r.Diagnostics);
    }

    private sealed class JointRun
    {
        public double AvgSn;
        public double AvgTau;
        public string Diagnostics = "";
    }

    private static JointRun RunOrIgnore(double beta, double s3, double c, double s1)
    {
        var exe = Jr03Solver.FindP610();
        if (exe == null)
            Assert.Ignore("p610_t3_elastoplastic.exe not found.");

        var work = Path.Combine(Path.GetTempPath(), "JR05_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        File.WriteAllText(
            Path.Combine(work, "jr05.dat"),
            Jr05DeckBuilder.BuildDat(Jr05DeckBuilder.Medium, beta, s3, c, s1));
        using var doc = Jr03Solver.RunJson(exe, work, "jr05");
        var root = doc.RootElement;
        if (!root.TryGetProperty("joints", out var joints) || joints.GetArrayLength() == 0)
            Assert.Fail("JSON missing joints[]");

        double sumSn = 0, sumTau = 0;
        int n = 0;
        var sb = new StringBuilder();
        sb.AppendLine($"beta={beta} s3={s3} c={c} s1={s1}");
        foreach (var j in joints.EnumerateArray())
        {
            sumSn += j.GetProperty("sn_kPa").GetDouble();
            sumTau += j.GetProperty("tau_kPa").GetDouble();
            n++;
        }
        sb.AppendLine($"sn={sumSn / n} tau={sumTau / n}");
        try { Directory.Delete(work, true); } catch { /* best-effort */ }
        return new JointRun { AvgSn = sumSn / n, AvgTau = sumTau / n, Diagnostics = sb.ToString() };
    }

    private static string CaseDir()
        => Path.Combine(Jr03Solver.FindRepoRoot(), "Verification", "slope", "joint verification", "JR05_TriaxialJointSlip");
}
