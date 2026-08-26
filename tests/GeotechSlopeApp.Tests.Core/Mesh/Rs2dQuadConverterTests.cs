using System;
using System.Collections.Generic;
using System.Windows;
using GeotechSlopeApp.Backend;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Mesh;

/// <summary>NUnit port of the TestRs2dQuadConverter console harness (scripts/TestRs2dQuadConverter).</summary>
[TestFixture]
public class Rs2dQuadConverterTests
{
    [Test]
    public void PairTrianglesToQuads_PerfectGrid_ProducesAllQuads()
    {
        var pts = new List<Point>();
        int Add(double x, double y) { pts.Add(new Point(x, y)); return pts.Count - 1; }
        int v00 = Add(0, 0), v10 = Add(0.5, 0), v20 = Add(1.0, 0);
        int v01 = Add(0, 0.5), v11 = Add(0.5, 0.5), v21 = Add(1.0, 0.5);
        int v02 = Add(0, 1.0), v12 = Add(0.5, 1.0), v22 = Add(1.0, 1.0);

        var tris = new List<Rs2dQuadConverter.TriRef>
        {
            new() { Region = 1, Etype = 1, A = v00, B = v10, C = v01 },
            new() { Region = 1, Etype = 1, A = v10, B = v11, C = v01 },
            new() { Region = 1, Etype = 1, A = v10, B = v20, C = v11 },
            new() { Region = 1, Etype = 1, A = v20, B = v21, C = v11 },
            new() { Region = 1, Etype = 1, A = v01, B = v11, C = v02 },
            new() { Region = 1, Etype = 1, A = v11, B = v12, C = v02 },
            new() { Region = 1, Etype = 1, A = v11, B = v21, C = v12 },
            new() { Region = 1, Etype = 1, A = v21, B = v22, C = v12 },
        };

        var result = Rs2dQuadConverter.PairTrianglesToQuads(pts, tris, Rs2dQuadConverter.Quality.Default);

        Assert.That(result.Quads.Count, Is.EqualTo(4));
        Assert.That(result.Unmatched.Count, Is.EqualTo(0));
    }

    [Test]
    public void PairTrianglesToQuads_SingleTrianglePair_ProducesOneQuad()
    {
        var pts = new List<Point> { new(0, 0), new(1, 0), new(1, 1), new(0, 1) };
        var tris = new List<Rs2dQuadConverter.TriRef>
        {
            new() { Region = 1, Etype = 1, A = 0, B = 1, C = 2 },
            new() { Region = 1, Etype = 1, A = 0, B = 2, C = 3 },
        };

        var result = Rs2dQuadConverter.PairTrianglesToQuads(pts, tris, Rs2dQuadConverter.Quality.Default);

        Assert.That(result.Quads.Count, Is.EqualTo(1));
        Assert.That(result.Unmatched.Count, Is.EqualTo(0));
    }

    [Test]
    public void PairTrianglesToQuads_LoneTriangle_IsUnmatched()
    {
        var pts = new List<Point> { new(0, 0), new(1, 0), new(0.5, 1) };
        var tris = new List<Rs2dQuadConverter.TriRef>
        {
            new() { Region = 1, Etype = 1, A = 0, B = 1, C = 2 },
        };

        var result = Rs2dQuadConverter.PairTrianglesToQuads(pts, tris, Rs2dQuadConverter.Quality.Default);

        Assert.That(result.Quads.Count, Is.EqualTo(0));
        Assert.That(result.Unmatched.Count, Is.EqualTo(1));
    }

    [Test]
    public void UpgradeQ4ToQ8_AddsMidsideNodesOnEdges()
    {
        var pts = new List<Point> { new(0, 0), new(1, 0), new(1, 1), new(0, 1) };
        var quads = new List<Rs2dQuadConverter.QuadResult>
        {
            new() { Q1 = 0, Q2 = 1, Q3 = 2, Q4 = 3, Etype = 1 },
        };

        var q8 = Rs2dQuadConverter.UpgradeQ4ToQ8(pts, quads);

        Assert.That(q8.Count, Is.EqualTo(1));
        Assert.That(pts.Count, Is.EqualTo(8));

        var rec = q8[0];
        AssertMid(pts, rec.n1, rec.n2, rec.n5);
        AssertMid(pts, rec.n2, rec.n3, rec.n6);
        AssertMid(pts, rec.n3, rec.n4, rec.n7);
        AssertMid(pts, rec.n4, rec.n1, rec.n8);
    }

    private static void AssertMid(List<Point> pts, int a, int b, int m)
    {
        var pa = pts[a]; var pb = pts[b]; var pm = pts[m];
        Assert.That(pm.X, Is.EqualTo((pa.X + pb.X) * 0.5).Within(1e-9));
        Assert.That(pm.Y, Is.EqualTo((pa.Y + pb.Y) * 0.5).Within(1e-9));
    }
}
