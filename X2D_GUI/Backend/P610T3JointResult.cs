using System;
using System.Collections.Generic;
using GeotechSlopeApp.Models.FiniteElement;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// One Goodman interface result from p610 JSON <c>joints[]</c>.
/// <see cref="N1"/>–<see cref="N4"/> are 0-based indices into <see cref="P610T3RunResult.Nodes"/>.
/// <see cref="Un"/> is opening-positive; <see cref="SnKpa"/> is compression-positive.
/// </summary>
public sealed class P610T3JointResult
{
    public int N1 { get; init; }
    public int N2 { get; init; }
    public int N3 { get; init; }
    public int N4 { get; init; }
    public double Un { get; init; }
    public double Us { get; init; }
    public double Closure { get; init; }
    public double SnKpa { get; init; }
    public double TauKpa { get; init; }
    public double L { get; init; }
    public double Nx { get; init; }
    public double Ny { get; init; }
    public bool Slipped { get; init; }
}

/// <summary>Re-evaluate joint opening/slip/stress from a stage displacement field.</summary>
public static class P610T3JointKinematics
{
    public static IReadOnlyList<P610T3JointResult> RecomputeAll(
        IReadOnlyList<P610T3JointResult> topology,
        IReadOnlyList<Node> nodes,
        IReadOnlyList<(double ux, double uy)> displacements)
    {
        if (topology == null || topology.Count == 0)
            return Array.Empty<P610T3JointResult>();

        var list = new List<P610T3JointResult>(topology.Count);
        foreach (var src in topology)
            list.Add(Recompute(src, nodes, displacements));
        return list;
    }

    public static P610T3JointResult Recompute(
        P610T3JointResult src,
        IReadOnlyList<Node> nodes,
        IReadOnlyList<(double ux, double uy)> displacements)
    {
        var (x1, y1, x2, y2, L, sx, sy, nx, ny) = Geometry(src, nodes);
        var u1 = U(displacements, src.N1);
        var u2 = U(displacements, src.N2);
        var u3 = U(displacements, src.N3);
        var u4 = U(displacements, src.N4);
        double uxMinus = 0.5 * (u1.ux + u2.ux);
        double uyMinus = 0.5 * (u1.uy + u2.uy);
        double uxPlus = 0.5 * (u4.ux + u3.ux);
        double uyPlus = 0.5 * (u4.uy + u3.uy);
        double urelX = uxPlus - uxMinus;
        double urelY = uyPlus - uyMinus;
        double us = sx * urelX + sy * urelY;
        double un = nx * urelX + ny * urelY;
        double closure = -un;
        return new P610T3JointResult
        {
            N1 = src.N1,
            N2 = src.N2,
            N3 = src.N3,
            N4 = src.N4,
            Un = un,
            Us = us,
            Closure = closure,
            SnKpa = ScaleStress(src.SnKpa, src.Un, un),
            TauKpa = ScaleStress(src.TauKpa, src.Us, us),
            L = L,
            Nx = nx,
            Ny = ny,
            Slipped = src.Slipped && Math.Abs(us) >= Math.Abs(src.Us) - 1e-15,
        };
    }

    public static (double X1, double Y1, double X2, double Y2) SegmentEnds(
        P610T3JointResult joint,
        IReadOnlyList<Node> nodes)
    {
        var (x1, y1, x2, y2, _, _, _, _, _) = Geometry(joint, nodes);
        return (x1, y1, x2, y2);
    }

    private static (double x1, double y1, double x2, double y2, double L, double sx, double sy, double nx, double ny)
        Geometry(P610T3JointResult src, IReadOnlyList<Node> nodes)
    {
        var p1 = Coord(nodes, src.N1);
        var p2 = Coord(nodes, src.N2);
        var p3 = Coord(nodes, src.N3);
        var p4 = Coord(nodes, src.N4);
        double x1 = 0.5 * (p1.x + p4.x);
        double y1 = 0.5 * (p1.y + p4.y);
        double x2 = 0.5 * (p2.x + p3.x);
        double y2 = 0.5 * (p2.y + p3.y);
        double dx = x2 - x1;
        double dy = y2 - y1;
        double L = Math.Sqrt(dx * dx + dy * dy);
        double sx, sy;
        if (L > 1e-9)
        {
            sx = dx / L;
            sy = dy / L;
        }
        else if (Math.Abs(src.Nx) + Math.Abs(src.Ny) > 1e-12)
        {
            sx = -src.Ny;
            sy = src.Nx;
            L = src.L;
        }
        else
        {
            sx = 1.0;
            sy = 0.0;
        }

        return (x1, y1, x2, y2, L, sx, sy, -sy, sx);
    }

    private static (double x, double y) Coord(IReadOnlyList<Node> nodes, int index)
    {
        if (nodes == null || index < 0 || index >= nodes.Count)
            return (0, 0);
        return (nodes[index].X, nodes[index].Y);
    }

    private static (double ux, double uy) U(IReadOnlyList<(double ux, double uy)> disp, int index)
    {
        if (disp == null || index < 0 || index >= disp.Count)
            return (0, 0);
        return disp[index];
    }

    private static double ScaleStress(double templateStress, double templateRel, double rel)
    {
        if (Math.Abs(templateRel) < 1e-15)
            return 0;
        return templateStress * (rel / templateRel);
    }
}
