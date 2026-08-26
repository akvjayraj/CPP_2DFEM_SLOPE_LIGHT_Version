using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Mesh;

/// <summary>
/// Pure-geometry portion of the TunnelWizardSmokeTest harness
/// (scripts/TunnelWizardSmokeTest). ViewModel/applier scenarios live in the ViewModels project.
/// </summary>
[TestFixture]
public class TunnelShapeBuilderTests
{
    private static double SignedArea(IReadOnlyList<Point> p)
    {
        double a = 0;
        for (int i = 0; i < p.Count - 1; i++)
            a += p[i].X * p[i + 1].Y - p[i + 1].X * p[i].Y;
        return 0.5 * a;
    }

    private static Point Centroid(IReadOnlyList<Point> p)
        => new(p.Take(p.Count - 1).Average(q => q.X), p.Take(p.Count - 1).Average(q => q.Y));

    private static (double xMin, double xMax, double yMin, double yMax) Bbox(IReadOnlyList<Point> p)
        => (p.Min(q => q.X), p.Max(q => q.X), p.Min(q => q.Y), p.Max(q => q.Y));

    [Test]
    public void Circle_HasExpectedCentroidAreaAndOrientation()
    {
        var poly = TunnelShapeBuilder.BuildPolygon(
            new TunnelShapeParams { Kind = TunnelShapeKind.Circle, Radius = 5.0, CenterX = 10, CenterY = -2 });

        Assert.That(poly.Count, Is.GreaterThan(8));
        Assert.That(Centroid(poly).X, Is.EqualTo(10.0).Within(1e-6));
        Assert.That(Centroid(poly).Y, Is.EqualTo(-2.0).Within(1e-6));
        Assert.That(Math.Abs(SignedArea(poly)), Is.EqualTo(Math.PI * 25.0).Within(0.5));
        Assert.That(SignedArea(poly), Is.GreaterThan(0), "expected CCW orientation");
        Assert.That(poly.Max(q => Math.Sqrt((q.X - 10) * (q.X - 10) + (q.Y + 2) * (q.Y + 2))),
            Is.EqualTo(5.0).Within(1e-6));
    }

    [Test]
    public void Rectangle_HasFourCornersAndCorrectArea()
    {
        var poly = TunnelShapeBuilder.BuildPolygon(
            new TunnelShapeParams { Kind = TunnelShapeKind.Rectangular, Width = 8.0, Height = 6.0 });

        Assert.That(poly.Count, Is.EqualTo(5), "4 corners + closing vertex");
        Assert.That(Math.Abs(SignedArea(poly)), Is.EqualTo(48.0).Within(1e-9));
        Assert.That(SignedArea(poly), Is.GreaterThan(0));
    }

    [Test]
    public void Horseshoe_HasExpectedBoundingBoxAndArea()
    {
        var poly = TunnelShapeBuilder.BuildPolygon(
            new TunnelShapeParams { Kind = TunnelShapeKind.Horseshoe, HorseshoeSpan = 10, HorseshoeHeight = 8 });

        Assert.That(poly.Count, Is.GreaterThan(10));
        Assert.That(SignedArea(poly), Is.GreaterThan(0));
        var (xMin, xMax, yMin, yMax) = Bbox(poly);
        Assert.That(xMax - xMin, Is.EqualTo(10.0).Within(1e-3));
        Assert.That(yMax - yMin, Is.EqualTo(8.0).Within(1e-3));
        double expectedArea = 3.0 * 10.0 + 0.5 * Math.PI * 25.0;
        Assert.That(Math.Abs(SignedArea(poly)), Is.EqualTo(expectedArea).Within(1.0));
    }

    [Test]
    public void NatmArch_HasExpectedBoundingBox()
    {
        var poly = TunnelShapeBuilder.BuildPolygon(new TunnelShapeParams
        {
            Kind = TunnelShapeKind.NatmArch,
            NatmSpan = 10, NatmHeight = 7, NatmCrownRadius = 6, NatmInvertRadius = 12,
        });

        Assert.That(poly.Count, Is.GreaterThan(12));
        Assert.That(SignedArea(poly), Is.GreaterThan(0));
        var (xMin, xMax, yMin, yMax) = Bbox(poly);
        Assert.That(xMax - xMin, Is.EqualTo(10.0).Within(1e-3));
        Assert.That(yMax - yMin, Is.EqualTo(7.0).Within(1e-3));
    }

    [Test]
    public void SuggestOuterDomain_ExtendsWellBeyondTunnel()
    {
        var poly = TunnelShapeBuilder.BuildPolygon(
            new TunnelShapeParams { Kind = TunnelShapeKind.Circle, Radius = 5.0 });
        var (xMin, xMax, yMin, yMax) = TunnelShapeBuilder.SuggestOuterDomain(poly);

        Assert.That((xMax - xMin) / 2.0, Is.GreaterThan(25.0));
        Assert.That((yMax - yMin) / 2.0, Is.GreaterThan(25.0));
    }
}
