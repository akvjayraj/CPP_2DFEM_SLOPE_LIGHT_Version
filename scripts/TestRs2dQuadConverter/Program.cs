using System;
using System.Collections.Generic;
using System.Windows;
using GeotechSlopeApp.Backend;

class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        int failures = 0;
        failures += Test_PerfectGrid_ProducesAllQuads() ? 0 : 1;
        failures += Test_SingleTrianglePair_ProducesOneQuad() ? 0 : 1;
        failures += Test_BoundaryTriangles_AreUnmatched() ? 0 : 1;
        failures += Test_Q8Upgrade_AddsMidsideNodes() ? 0 : 1;
        Console.WriteLine($"Tests done — {failures} failure(s).");
        return failures == 0 ? 0 : 1;
    }

    static bool Test_PerfectGrid_ProducesAllQuads()
    {
        // 2x2 unit square subdivided into 8 right triangles (4 quads).
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
        bool pass = result.Quads.Count == 4 && result.Unmatched.Count == 0;
        Console.WriteLine($"[Test_PerfectGrid_ProducesAllQuads] quads={result.Quads.Count}, unmatched={result.Unmatched.Count} → {(pass ? "PASS" : "FAIL")}");
        return pass;
    }

    static bool Test_SingleTrianglePair_ProducesOneQuad()
    {
        var pts = new List<Point>
        {
            new(0, 0), new(1, 0), new(1, 1), new(0, 1)
        };
        var tris = new List<Rs2dQuadConverter.TriRef>
        {
            new() { Region = 1, Etype = 1, A = 0, B = 1, C = 2 },
            new() { Region = 1, Etype = 1, A = 0, B = 2, C = 3 },
        };
        var result = Rs2dQuadConverter.PairTrianglesToQuads(pts, tris, Rs2dQuadConverter.Quality.Default);
        bool pass = result.Quads.Count == 1 && result.Unmatched.Count == 0;
        Console.WriteLine($"[Test_SingleTrianglePair_ProducesOneQuad] quads={result.Quads.Count}, unmatched={result.Unmatched.Count} → {(pass ? "PASS" : "FAIL")}");
        return pass;
    }

    static bool Test_BoundaryTriangles_AreUnmatched()
    {
        var pts = new List<Point>
        {
            new(0, 0), new(1, 0), new(0.5, 1)
        };
        var tris = new List<Rs2dQuadConverter.TriRef>
        {
            new() { Region = 1, Etype = 1, A = 0, B = 1, C = 2 },
        };
        var result = Rs2dQuadConverter.PairTrianglesToQuads(pts, tris, Rs2dQuadConverter.Quality.Default);
        bool pass = result.Quads.Count == 0 && result.Unmatched.Count == 1;
        Console.WriteLine($"[Test_BoundaryTriangles_AreUnmatched] quads={result.Quads.Count}, unmatched={result.Unmatched.Count} → {(pass ? "PASS" : "FAIL")}");
        return pass;
    }

    static bool Test_Q8Upgrade_AddsMidsideNodes()
    {
        var pts = new List<Point>
        {
            new(0, 0), new(1, 0), new(1, 1), new(0, 1)
        };
        var quads = new List<Rs2dQuadConverter.QuadResult>
        {
            new() { Q1 = 0, Q2 = 1, Q3 = 2, Q4 = 3, Etype = 1 }
        };
        var q8 = Rs2dQuadConverter.UpgradeQ4ToQ8(pts, quads);
        bool pass = q8.Count == 1 && pts.Count == 8;
        if (pass)
        {
            var rec = q8[0];
            // Midside nodes should sit on the edges
            pass &= MidApprox(pts, rec.n1, rec.n2, rec.n5);
            pass &= MidApprox(pts, rec.n2, rec.n3, rec.n6);
            pass &= MidApprox(pts, rec.n3, rec.n4, rec.n7);
            pass &= MidApprox(pts, rec.n4, rec.n1, rec.n8);
        }
        Console.WriteLine($"[Test_Q8Upgrade_AddsMidsideNodes] pts={pts.Count}, q8={q8.Count} → {(pass ? "PASS" : "FAIL")}");
        return pass;
    }

    static bool MidApprox(List<Point> pts, int a, int b, int m)
    {
        var pa = pts[a]; var pb = pts[b]; var pm = pts[m];
        double mx = (pa.X + pb.X) * 0.5, my = (pa.Y + pb.Y) * 0.5;
        return Math.Abs(pm.X - mx) < 1e-9 && Math.Abs(pm.Y - my) < 1e-9;
    }
}
