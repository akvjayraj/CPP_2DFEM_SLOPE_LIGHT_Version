using System.Windows;
using GeotechSlopeApp.Backend;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Backend;

[TestFixture]
public class MeshJacobianValidatorTests
{
    [Test]
    public void ValidateT3_NonDegenerateTriangle_IsValidWithPositiveJacobian()
    {
        var nodes = new[] { new Point(0, 0), new Point(1, 0), new Point(0, 1) };
        var tris = new[] { new Rs2dQuadConverter.TriRef { A = 0, B = 1, C = 2 } };

        var result = MeshJacobianValidator.ValidateT3(nodes, tris);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.ElementsChecked, Is.EqualTo(1));
        Assert.That(result.BadElementCount, Is.EqualTo(0));
        Assert.That(result.MinJacobian, Is.GreaterThan(0));
    }

    [Test]
    public void ValidateT3_CollinearTriangle_IsInvalid()
    {
        var nodes = new[] { new Point(0, 0), new Point(1, 0), new Point(2, 0) };
        var tris = new[] { new Rs2dQuadConverter.TriRef { A = 0, B = 1, C = 2 } };

        var result = MeshJacobianValidator.ValidateT3(nodes, tris);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.BadElementCount, Is.EqualTo(1));
    }

    [Test]
    public void ValidateT3_NoElements_ReportsValidWithMessage()
    {
        var result = MeshJacobianValidator.ValidateT3(
            System.Array.Empty<Point>(),
            System.Array.Empty<Rs2dQuadConverter.TriRef>());

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Message, Does.Contain("No elements"));
    }

    [Test]
    public void Validate_UnitSquareQuad_IsValid()
    {
        var nodes = new[]
        {
            new Point(0, 0), new Point(1, 0), new Point(1, 1), new Point(0, 1),
        };
        var quads = new[] { new Rs2dQuadConverter.QuadResult { Q1 = 0, Q2 = 1, Q3 = 2, Q4 = 3 } };

        var result = MeshJacobianValidator.Validate(
            nodes, quads, System.Array.Empty<Rs2dQuadConverter.UnmatchedTri>());

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.ElementsChecked, Is.EqualTo(1));
        Assert.That(result.MinJacobian, Is.GreaterThan(0));
    }
}
