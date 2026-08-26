using System;
using System.Collections.Generic;
using System.Windows;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Structured rectangular mesh of 3-node triangles matching <c>library/geom/geom_rect.cpp</c> (Program 5.1 style).
/// </summary>
public static class GeomRectTriangleMeshBuilder
{
    public static int ElementCount(int nxe, int nyePhysical) => nxe * nyePhysical * 2;

    public static int NodeCount(int nxe, int nyePhysical) => (nxe + 1) * (nyePhysical + 1);

    /// <summary>
    /// Global node numbers (1-based) for element <paramref name="iel"/> (1-based).
    /// </summary>
    public static (int n1, int n2, int n3) GetTriangleNodeIds(int iel, int nxe, int nyePhysical, char dir)
    {
        int nye = nyePhysical * 2;
        if (dir == 'x' || dir == 'r')
        {
            int jel = 2 * nxe * ((iel - 1) / (2 * nxe));
            // C++ (iel-jel)/2 can be 0 for iel==1; x_coords[ip-1] requires ip>=1 (1-based strip index).
            int ip = Math.Max(1, (iel - jel) / 2);
            int iq = 2 * ((iel - 1) / (2 * nxe) + 1) - 1 + ((iel / 2) * 2) / Math.Max(iel, 1);
            if (iq % 2 != 0)
            {
                int n1 = (nxe + 1) * (iq - 1) / 2 + ip;
                int n2 = n1 + 1;
                int n3 = (nxe + 1) * (iq + 1) / 2 + ip;
                return (n1, n2, n3);
            }
            else
            {
                int n1 = (nxe + 1) * iq / 2 + ip + 1;
                int n2 = n1 - 1;
                int n3 = (nxe + 1) * (iq - 2) / 2 + ip + 1;
                return (n1, n2, n3);
            }
        }

        if (dir == 'y')
        {
            int jel = (iel - 1) / nye;
            int ip = jel + 1;
            int iq = iel - nye * jel;
            if (iq % 2 != 0)
            {
                int n1 = (ip - 1) * (nye + 2) / 2 + (iq + 1) / 2;
                int n2 = n1 + (nye + 2) / 2;
                int n3 = n1 + 1;
                return (n1, n2, n3);
            }
            else
            {
                int n1 = ip * (nye + 2) / 2 + (iq + 2) / 2;
                int n2 = (ip - 1) * (nye + 2) / 2 + (iq + 1) / 2 + 1;
                int n3 = n1 - 1;
                return (n1, n2, n3);
            }
        }

        throw new ArgumentException($"dir must be x, y, or r; got '{dir}'", nameof(dir));
    }

    /// <summary>
    /// Builds axis-coordinate nodes and triangle connectivity (same topology as p51 <c>geom_rect</c>).
    /// </summary>
    public static GeomRectTriangleMeshResult Build(
        IReadOnlyList<double> xCoords,
        IReadOnlyList<double> yCoords,
        char dir)
    {
        if (xCoords.Count < 2 || yCoords.Count < 2)
            throw new ArgumentException("Need at least two x and y coordinate lines.");

        int nxe = xCoords.Count - 1;
        int nyePhysical = yCoords.Count - 1;
        int nn = NodeCount(nxe, nyePhysical);
        int nels = ElementCount(nxe, nyePhysical);

        var nodePoints = new Point[nn];
        for (int i = 0; i < nn; i++)
        {
            int iy = i / (nxe + 1);
            int ix = i % (nxe + 1);
            double x = ix < xCoords.Count ? xCoords[ix] : 0;
            double y = iy < yCoords.Count ? yCoords[iy] : 0;
            nodePoints[i] = new Point(x, y);
        }

        var triangles = new List<(int n1, int n2, int n3)>(nels);
        for (int iel = 1; iel <= nels; iel++)
            triangles.Add(GetTriangleNodeIds(iel, nxe, nyePhysical, dir));

        return new GeomRectTriangleMeshResult
        {
            Nxe = nxe,
            Nye = nyePhysical,
            NodePoints = nodePoints,
            Triangles = triangles
        };
    }
}

public sealed class GeomRectTriangleMeshResult
{
    public int Nxe { get; init; }
    public int Nye { get; init; }
    /// <summary>Index 0 = node 1, … index nn-1 = node nn.</summary>
    public Point[] NodePoints { get; init; } = Array.Empty<Point>();
    public List<(int n1, int n2, int n3)> Triangles { get; init; } = new();
}
