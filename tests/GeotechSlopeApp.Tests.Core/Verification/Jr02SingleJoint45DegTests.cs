using System.IO;
using System.Linq;
using System.Text.Json;
using GeotechSlopeApp.Persistence;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Verification;

[TestFixture]
public class Jr02SingleJoint45DegTests
{
    [Test]
    public void Xfem_Deserializes45DegJointTwoBlocksAndTopLoad()
    {
        var path = Path.Combine(CaseDir(), "jr02_single_joint_45deg_compression.xfem");
        Assert.That(File.Exists(path), Is.True, path);
        var doc = JsonSerializer.Deserialize<XfemProjectPersistence.XfemDocument>(
            File.ReadAllText(path), XfemJsonOptions());
        Assert.That(doc, Is.Not.Null);
        Assert.That(doc!.Geometry!.Vertices, Has.Count.EqualTo(6));
        Assert.That(doc.Geometry.Surfaces, Has.Count.EqualTo(2));
        var joint = doc.Geometry.GeologicalFeatures![0];
        Assert.That(joint.EndCondition, Is.EqualTo("BothOpen"));
        Assert.That(joint.DipAngleDeg, Is.EqualTo(45).Within(1e-9));
        Assert.That(joint.KnKpa, Is.EqualTo(1e5).Within(1e-6));
        Assert.That(joint.Vertices, Has.Count.EqualTo(2));
        Assert.That(joint.Vertices![0].X, Is.EqualTo(0).Within(1e-12));
        Assert.That(joint.Vertices[0].Y, Is.EqualTo(0.5).Within(1e-12));
        Assert.That(joint.Vertices[1].X, Is.EqualTo(1).Within(1e-12));
        Assert.That(joint.Vertices[1].Y, Is.EqualTo(1.5).Within(1e-12));
        Assert.That(doc.Materials!.MaterialLibrary![0].UnitWeight, Is.EqualTo(0).Within(1e-12));
        Assert.That(doc.Loads![0].Magnitude, Is.EqualTo(10000).Within(1e-6));
        Assert.That(doc.FeAnalysis!.GravityEnabled, Is.False);
        Assert.That(doc.Mesh!.IsDiscretized, Is.True);
        Assert.That(doc.Mesh.DiscretizationNodes, Has.Count.EqualTo(153));
        Assert.That(doc.Mesh.Elements, Has.Count.EqualTo(256));
        Assert.That(doc.BoundaryConditions!.Rows, Has.Count.EqualTo(9));
        Assert.That(doc.BoundaryConditions.Rows![0].BCType, Is.EqualTo("Fixed"));
        Assert.That(doc.BoundaryConditions.Rows.Skip(1).All(r => r.BCType == "Roller Y"), Is.True);
        Assert.That(doc.Mesh.DiscretizationNodes!.Any(n =>
            Math.Abs(n.X - 0.5) < 1e-9 && Math.Abs(n.Y - 1.0) < 1e-9), Is.True);
    }

    private static string CaseDir()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CMakeLists.txt"))
                && Directory.Exists(Path.Combine(dir.FullName, "Verification")))
                return Path.Combine(dir.FullName, "Verification", "slope", "joint verification", "JR02_SingleJoint45DegCompression");
            dir = dir.Parent;
        }
        return Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..",
            "Verification", "slope", "joint verification", "JR02_SingleJoint45DegCompression"));
    }

    private static JsonSerializerOptions XfemJsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
