using System;
using System.Collections.Generic;
using System.Windows;

namespace GeotechSlopeApp.Backend;

/// <summary>Samples Jacobian determinants at Gauss points for T3/T6/Q4/Q8 elements.</summary>
public static class MeshJacobianValidator
{
    public sealed class Result
    {
        public double MinJacobian { get; init; }
        public int BadElementCount { get; init; }
        public int ElementsChecked { get; init; }
        public bool IsValid { get; init; }
        public string Message { get; init; } = "";
    }

    private static readonly double[] Gauss1D = { -0.577350269189626, 0.577350269189626 };

    public static Result Validate(
        IReadOnlyList<Point> nodes,
        IReadOnlyList<Rs2dQuadConverter.QuadResult> quads,
        IReadOnlyList<Rs2dQuadConverter.UnmatchedTri> unmatched,
        IReadOnlyList<(int n1, int n2, int n3, int n4, int n5, int n6, int n7, int n8)>? q8Elements = null)
    {
        double minJ = double.PositiveInfinity;
        int bad = 0;
        int checkedCount = 0;
        double charScale = CharacteristicLength(nodes);

        if (q8Elements != null)
        {
            foreach (var q in q8Elements)
            {
                checkedCount++;
                double elemMin = MinJacobianQ8(nodes, q);
                minJ = Math.Min(minJ, elemMin);
                if (elemMin <= charScale * charScale * 1e-10)
                    bad++;
            }
        }
        else
        {
            foreach (var q in quads)
            {
                checkedCount++;
                double elemMin = MinJacobianQ4(nodes, q.Q1, q.Q2, q.Q3, q.Q4);
                minJ = Math.Min(minJ, elemMin);
                if (elemMin <= charScale * charScale * 1e-10)
                    bad++;
            }
        }

        foreach (var t in unmatched)
        {
            checkedCount++;
            double elemMin = JacobianT3(nodes, t.A, t.B, t.C);
            minJ = Math.Min(minJ, elemMin);
            if (elemMin <= charScale * charScale * 1e-10)
                bad++;
        }

        if (checkedCount == 0)
            return new Result { IsValid = true, Message = "No elements to validate." };

        if (double.IsPositiveInfinity(minJ))
            minJ = 0;

        bool valid = bad == 0 && minJ > 0;
        string msg = valid
            ? $"All {checkedCount} element(s) have positive Jacobian (min det J = {minJ:E3})."
            : $"{bad} element(s) with non-positive/near-zero Jacobian (min det J = {minJ:E3}).";

        return new Result
        {
            MinJacobian = minJ,
            BadElementCount = bad,
            ElementsChecked = checkedCount,
            IsValid = valid,
            Message = msg,
        };
    }

    public static Result ValidateT3(
        IReadOnlyList<Point> nodes,
        IReadOnlyList<Rs2dQuadConverter.TriRef> triangles)
    {
        double minJ = double.PositiveInfinity;
        int bad = 0;
        double charScale = CharacteristicLength(nodes);

        foreach (var t in triangles)
        {
            double elemMin = JacobianT3(nodes, t.A, t.B, t.C);
            minJ = Math.Min(minJ, elemMin);
            if (elemMin <= charScale * charScale * 1e-10)
                bad++;
        }

        if (triangles.Count == 0)
            return new Result { IsValid = true, Message = "No elements to validate." };

        bool valid = bad == 0 && minJ > 0;
        return new Result
        {
            MinJacobian = minJ,
            BadElementCount = bad,
            ElementsChecked = triangles.Count,
            IsValid = valid,
            Message = valid
                ? $"All {triangles.Count} T3 element(s) have positive Jacobian (min det J = {minJ:E3})."
                : $"{bad} T3 element(s) with non-positive/near-zero Jacobian (min det J = {minJ:E3}).",
        };
    }

    public static Result ValidateT6(IReadOnlyList<Point> nodes, IReadOnlyList<Rs2dQuadConverter.TriRef> triangles)
    {
        double minJ = double.PositiveInfinity;
        int bad = 0;
        double charScale = CharacteristicLength(nodes);

        foreach (var t in triangles)
        {
            double elemMin = double.PositiveInfinity;
            foreach (var (r, s) in TriangleGaussPoints())
            {
                double j = JacobianT6At(nodes, t.A, t.B, t.C, r, s);
                elemMin = Math.Min(elemMin, j);
            }
            minJ = Math.Min(minJ, elemMin);
            if (elemMin <= charScale * charScale * 1e-10)
                bad++;
        }

        if (triangles.Count == 0)
            return new Result { IsValid = true, Message = "No elements to validate." };

        bool valid = bad == 0 && minJ > 0;
        return new Result
        {
            MinJacobian = minJ,
            BadElementCount = bad,
            ElementsChecked = triangles.Count,
            IsValid = valid,
            Message = valid
                ? $"All {triangles.Count} T6 element(s) have positive Jacobian (min det J = {minJ:E3})."
                : $"{bad} T6 element(s) with non-positive/near-zero Jacobian (min det J = {minJ:E3}).",
        };
    }

    private static double JacobianT3(IReadOnlyList<Point> nodes, int a, int b, int c)
    {
        var pa = nodes[a]; var pb = nodes[b]; var pc = nodes[c];
        return Math.Abs((pb.X - pa.X) * (pc.Y - pa.Y) - (pc.X - pa.X) * (pb.Y - pa.Y));
    }

    private static IEnumerable<(double r, double s)> TriangleGaussPoints()
    {
        yield return (1.0 / 6.0, 1.0 / 6.0);
        yield return (2.0 / 3.0, 1.0 / 6.0);
        yield return (1.0 / 6.0, 2.0 / 3.0);
    }

    private static double JacobianT6At(IReadOnlyList<Point> nodes, int a, int b, int c, double r, double s)
    {
        var p1 = nodes[a]; var p2 = nodes[b]; var p3 = nodes[c];
        var p4 = Mid(p1, p2); var p5 = Mid(p2, p3); var p6 = Mid(p3, p1);

        double L1 = 1.0 - r - s, L2 = r, L3 = s;
        double dL1dr = -1, dL2dr = 1, dL3dr = 0;
        double dL1ds = -1, dL2ds = 0, dL3ds = 1;

        double N1 = L1 * (2 * L1 - 1), N2 = L2 * (2 * L2 - 1), N3 = L3 * (2 * L3 - 1);
        double N4 = 4 * L1 * L2, N5 = 4 * L2 * L3, N6 = 4 * L3 * L1;

        double dN1dr = (4 * L1 - 1) * dL1dr;
        double dN2dr = (4 * L2 - 1) * dL2dr;
        double dN3dr = (4 * L3 - 1) * dL3dr;
        double dN4dr = 4 * (dL1dr * L2 + L1 * dL2dr);
        double dN5dr = 4 * (dL2dr * L3 + L2 * dL3dr);
        double dN6dr = 4 * (dL3dr * L1 + L3 * dL1dr);

        double dN1ds = (4 * L1 - 1) * dL1ds;
        double dN2ds = (4 * L2 - 1) * dL2ds;
        double dN3ds = (4 * L3 - 1) * dL3ds;
        double dN4ds = 4 * (dL1ds * L2 + L1 * dL2ds);
        double dN5ds = 4 * (dL2ds * L3 + L2 * dL3ds);
        double dN6ds = 4 * (dL3ds * L1 + L3 * dL1ds);

        double dxdr = dN1dr * p1.X + dN2dr * p2.X + dN3dr * p3.X + dN4dr * p4.X + dN5dr * p5.X + dN6dr * p6.X;
        double dydr = dN1dr * p1.Y + dN2dr * p2.Y + dN3dr * p3.Y + dN4dr * p4.Y + dN5dr * p5.Y + dN6dr * p6.Y;
        double dxds = dN1ds * p1.X + dN2ds * p2.X + dN3ds * p3.X + dN4ds * p4.X + dN5ds * p5.X + dN6ds * p6.X;
        double dyds = dN1ds * p1.Y + dN2ds * p2.Y + dN3ds * p3.Y + dN4ds * p4.Y + dN5ds * p5.Y + dN6ds * p6.Y;

        return Math.Abs(dxdr * dyds - dxds * dydr);
    }

    private static double MinJacobianQ4(IReadOnlyList<Point> nodes, int n1, int n2, int n3, int n4)
    {
        var p1 = nodes[n1]; var p2 = nodes[n2]; var p3 = nodes[n3]; var p4 = nodes[n4];
        double minJ = double.PositiveInfinity;
        foreach (var xi in Gauss1D)
        foreach (var eta in Gauss1D)
        {
            double j = JacobianQ4At(p1, p2, p3, p4, xi, eta);
            minJ = Math.Min(minJ, j);
        }
        return minJ;
    }

    private static double JacobianQ4At(Point p1, Point p2, Point p3, Point p4, double xi, double eta)
    {
        double dN1 = -0.25 * (1 - eta);
        double dN2 = 0.25 * (1 - eta);
        double dN3 = 0.25 * (1 + eta);
        double dN4 = -0.25 * (1 + eta);

        double dM1 = -0.25 * (1 - xi);
        double dM2 = -0.25 * (1 + xi);
        double dM3 = 0.25 * (1 + xi);
        double dM4 = 0.25 * (1 - xi);

        double dxdx = dN1 * p1.X + dN2 * p2.X + dN3 * p3.X + dN4 * p4.X;
        double dydx = dN1 * p1.Y + dN2 * p2.Y + dN3 * p3.Y + dN4 * p4.Y;
        double dxde = dM1 * p1.X + dM2 * p2.X + dM3 * p3.X + dM4 * p4.X;
        double dyde = dM1 * p1.Y + dM2 * p2.Y + dM3 * p3.Y + dM4 * p4.Y;

        return Math.Abs(dxdx * dyde - dxde * dydx);
    }

    private static double MinJacobianQ8(
        IReadOnlyList<Point> nodes,
        (int n1, int n2, int n3, int n4, int n5, int n6, int n7, int n8) q)
    {
        var p = new[]
        {
            nodes[q.n1], nodes[q.n2], nodes[q.n3], nodes[q.n4],
            nodes[q.n5], nodes[q.n6], nodes[q.n7], nodes[q.n8],
        };
        double minJ = double.PositiveInfinity;
        foreach (var xi in Gauss1D)
        foreach (var eta in Gauss1D)
        {
            double j = JacobianQ8At(p, xi, eta);
            minJ = Math.Min(minJ, j);
        }
        return minJ;
    }

    private static double JacobianQ8At(Point[] p, double xi, double eta)
    {
        static double dNdXi(int i, double x, double e)
        {
            return i switch
            {
                0 => 0.25 * (1 - e) * (2 * x + e),
                1 => 0.25 * (1 - e) * (2 * x - e),
                2 => 0.25 * (1 + e) * (2 * x + e),
                3 => 0.25 * (1 + e) * (2 * x - e),
                4 => -x * (1 - e),
                5 => 0.5 * (1 - e * e),
                6 => -x * (1 + e),
                7 => -0.5 * (1 - e * e),
                _ => 0,
            };
        }

        static double dNdEta(int i, double x, double e)
        {
            return i switch
            {
                0 => 0.25 * (1 - x) * (x + 2 * e),
                1 => 0.25 * (1 + x) * (2 * e - x),
                2 => 0.25 * (1 + x) * (2 * e + x),
                3 => 0.25 * (1 - x) * (x - 2 * e),
                4 => -0.5 * (1 - x * x),
                5 => -(1 + x) * e,
                6 => 0.5 * (1 - x * x),
                7 => (x - 1) * e,
                _ => 0,
            };
        }

        double dxdx = 0, dydx = 0, dxde = 0, dyde = 0;
        for (int i = 0; i < 8; i++)
        {
            dxdx += dNdXi(i, xi, eta) * p[i].X;
            dydx += dNdXi(i, xi, eta) * p[i].Y;
            dxde += dNdEta(i, xi, eta) * p[i].X;
            dyde += dNdEta(i, xi, eta) * p[i].Y;
        }
        return Math.Abs(dxdx * dyde - dxde * dydx);
    }

    private static Point Mid(Point a, Point b) => new((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);

    private static double CharacteristicLength(IReadOnlyList<Point> nodes)
    {
        if (nodes.Count == 0) return 1.0;
        double minX = nodes[0].X, maxX = nodes[0].X, minY = nodes[0].Y, maxY = nodes[0].Y;
        foreach (var p in nodes)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }
        return Math.Max(maxX - minX, maxY - minY);
    }
}
