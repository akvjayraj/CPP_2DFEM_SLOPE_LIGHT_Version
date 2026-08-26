using System;
using System.Collections.Generic;
using System.Linq;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Program 6.4 parametric embankment Q8 mesh (Smith <c>emb_2d_geom</c> / <c>emb_2d_bc</c>).
/// Port of <c>tools/gen_p64_q8_srf.ps1</c>.
/// </summary>
public static class P64EmbankmentMeshBuilder
{
    public sealed class MeshResult
    {
        public int Nels { get; init; }
        public int Nn { get; init; }
        public IReadOnlyList<(int id, double x, double y)> Nodes { get; init; } = Array.Empty<(int, double, double)>();
        public IReadOnlyList<(int id, int[] conn, int etype)> Elements { get; init; } = Array.Empty<(int, int[], int)>();
        public IReadOnlyList<(int node, int fx, int fy)> BoundaryConditions { get; init; } = Array.Empty<(int, int, int)>();
    }

    /// <summary>
    /// Smith Example 2 layered foundation etype (port of <c>Get-Ex2ElementType</c> in
    /// <c>tools/gen_slope64_ex2.ps1</c>).
    /// </summary>
    public static int GetEx2ElementType(int iel, int nx1, int nx2, int ny1, int ny2)
    {
        int nt = nx1 * ny1;
        if (iel <= nt)
            return 1;
        int nxe = nx1 + nx2;
        int iq = (iel - nt - 1) / nxe + ny1 + 1;
        int fr = iq - ny1;
        return fr <= 3 ? 2 : 1;
    }

    public static MeshResult BuildQ8Mesh(
        int nx1, int nx2, int ny1, int ny2,
        double w1, double s1, double w2, double h1, double h2,
        bool assignEx2ElementTypes = false,
        Func<int, int>? assignElementType = null)
    {
        int nye = ny1 + ny2;
        int nels = nx1 * nye + ny2 * nx2;
        int nn = (3 * nye + 2) * nx1 + 2 * nye + 1 + (3 * ny2 + 2) * nx2;

        var elems = new List<int[]>(nels);
        for (int iel = 1; iel <= nels; iel++)
        {
            var g = Emb2dGeom(iel, nx1, nx2, ny1, ny2, w1, s1, w2, h1, h2);
            elems.Add((int[])g.Num.Clone());
        }

        var coordX = new double[nn];
        var coordY = new double[nn];
        ApplyAveragedEmbankmentNodeCoords(coordX, coordY, elems, nx1, nx2, ny1, ny2, w1, s1, w2, h1, h2, coordYOffset: 0);

        var nodes = new List<(int id, double x, double y)>(nn);
        for (int nid = 1; nid <= nn; nid++)
            nodes.Add((nid, coordX[nid - 1], coordY[nid - 1]));

        var elements = new List<(int id, int[] conn, int etype)>(nels);
        for (int i = 0; i < nels; i++)
        {
            int etype = assignElementType != null
                ? assignElementType(i + 1)
                : assignEx2ElementTypes
                    ? GetEx2ElementType(i + 1, nx1, nx2, ny1, ny2)
                    : 1;
            elements.Add((i + 1, elems[i], etype));
        }

        var nf = Emb2dBc(nx1, nx2, ny1, ny2, nn);
        var bcs = new List<(int node, int fx, int fy)>();
        for (int nid = 1; nid <= nn; nid++)
        {
            int ix = (nid - 1) * 2;
            int fx = nf[ix] == 0 ? 0 : 1;
            int fy = nf[ix + 1] == 0 ? 0 : 1;
            if (fx == 0 || fy == 0)
                bcs.Add((nid, fx, fy));
        }

        return new MeshResult
        {
            Nels = nels,
            Nn = nn,
            Nodes = nodes,
            Elements = elements,
            BoundaryConditions = bcs,
        };
    }

    /// <summary>
    /// Smith Program 6.4 embankment with 4-node bilinear quads (corner nodes only from <see cref="Emb2dGeom"/>).
    /// </summary>
    public static MeshResult BuildQ4Mesh(
        int nx1, int nx2, int ny1, int ny2,
        double w1, double s1, double w2, double h1, double h2,
        bool assignEx2ElementTypes = false)
    {
        var q8 = BuildQ8Mesh(nx1, nx2, ny1, ny2, w1, s1, w2, h1, h2, assignEx2ElementTypes);
        var q8NodeById = q8.Nodes.ToDictionary(n => n.id);

        var cornerIds = new HashSet<int>();
        var q4Elements = new List<(int id, int[] conn, int etype)>(q8.Elements.Count);
        foreach (var (id, conn8, etype) in q8.Elements)
        {
            if (conn8.Length < 8)
                throw new InvalidOperationException($"Q8 element {id} has fewer than 8 nodes.");
            var q4Conn = new[] { conn8[0], conn8[2], conn8[4], conn8[6] };
            foreach (int nid in q4Conn)
                cornerIds.Add(nid);
            q4Elements.Add((id, q4Conn, etype));
        }

        var sortedCornerIds = cornerIds.OrderBy(i => i).ToList();
        var idMap = sortedCornerIds.Select((oldId, idx) => (oldId, newId: idx + 1))
            .ToDictionary(t => t.oldId, t => t.newId);

        var nodes = sortedCornerIds
            .Select(oldId =>
            {
                var (_, x, y) = q8NodeById[oldId];
                return (idMap[oldId], x, y);
            })
            .ToList();

        var elements = q4Elements
            .Select(e => (e.id, e.conn.Select(c => idMap[c]).ToArray(), e.etype))
            .ToList();

        var bcs = q8.BoundaryConditions
            .Where(bc => cornerIds.Contains(bc.node))
            .Select(bc => (idMap[bc.node], bc.fx, bc.fy))
            .OrderBy(bc => bc.Item1)
            .ToList();

        return new MeshResult
        {
            Nels = q8.Nels,
            Nn = nodes.Count,
            Nodes = nodes,
            Elements = elements,
            BoundaryConditions = bcs,
        };
    }

    /// <summary>Preview element/node counts for embankment parametric mesh.</summary>
    public static (int nn, int nels) PreviewEmbankmentCounts(
        int nx1, int nx2, int ny1, int ny2, string meshKind)
    {
        int nye = ny1 + ny2;
        int nels = nx1 * nye + ny2 * nx2;
        if (string.Equals(meshKind, "Q4", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var mesh = BuildQ4Mesh(nx1, nx2, ny1, ny2, 20, 20, 20, 10, 5);
                return (mesh.Nn, mesh.Nels);
            }
            catch
            {
                return (0, nels);
            }
        }

        int nn = (3 * nye + 2) * nx1 + 2 * nye + 1 + (3 * ny2 + 2) * nx2;
        return (nn, nels);
    }

    /// <summary>
    /// Fortran <c>g_coord(:,num)=TRANSPOSE(coord)</c> using file connectivity (last element write wins).
    /// </summary>
    /// <summary>Element-local Smith coords aligned to file connectivity (8 points, local order).</summary>
    public static (double x, double y)[] GetElementPointsFromConnectivity(
        int iel,
        int[] conn,
        int nx1, int nx2, int ny1, int ny2,
        double w1, double s1, double w2, double h1, double h2,
        double coordYOffset = 0)
    {
        var pts = new (double x, double y)[8];
        if (conn == null || conn.Length < 8)
            return pts;

        var g = Emb2dGeom(iel, nx1, nx2, ny1, ny2, w1, s1, w2, h1, h2);
        for (int k = 0; k < 8; k++)
        {
            int j = Array.IndexOf(g.Num, conn[k]);
            pts[k] = j >= 0
                ? (g.CoordX[j], g.CoordY[j] + coordYOffset)
                : (0, 0);
        }
        return pts;
    }

    /// <summary>
    /// Resolve element-local emb_2d samples into global node coordinates (GUI / explicit .dat).
    /// </summary>
    public static void ApplyAveragedEmbankmentNodeCoords(
        double[] coordX,
        double[] coordY,
        IReadOnlyList<int[]> connectivity,
        int nx1, int nx2, int ny1, int ny2,
        double w1, double s1, double w2, double h1, double h2,
        double coordYOffset = 0)
    {
        if (coordX == null || coordY == null || coordX.Length == 0 || connectivity == null)
            return;

        var samples = new Dictionary<int, List<(double x, double y, bool isCorner)>>();
        for (int e = 0; e < connectivity.Count; e++)
        {
            var g = Emb2dGeom(e + 1, nx1, nx2, ny1, ny2, w1, s1, w2, h1, h2);
            var conn = connectivity[e];
            for (int k = 0; k < 8; k++)
            {
                int nid = conn[k];
                int j = Array.IndexOf(g.Num, nid);
                if (j < 0)
                    continue;
                if (!samples.TryGetValue(nid, out var list))
                {
                    list = new List<(double, double, bool)>();
                    samples[nid] = list;
                }
                bool isCorner = j is 0 or 2 or 4 or 6;
                list.Add((g.CoordX[j], g.CoordY[j] + coordYOffset, isCorner));
            }
        }

        foreach (var (nid, list) in samples)
        {
            int idx = nid - 1;
            if (idx < 0 || idx >= coordX.Length || idx >= coordY.Length)
                continue;
            var pick = PickDisplayCoord(list);
            coordX[idx] = pick.x;
            coordY[idx] = pick.y;
        }
    }

    /// <summary>
    /// Mean of element-local samples. On the embankment left face (x≈0), prefer midside
    /// samples so crest corners (y = 0 Smith) do not collapse hundreds of ids to (0, crest).
    /// </summary>
    private static (double x, double y) PickDisplayCoord(List<(double x, double y, bool isCorner)> list)
    {
        if (list.Count == 0)
            return (0, 0);

        const double leftTol = 1e-9;
        static (double x, double y) Average(IEnumerable<(double x, double y, bool isCorner)> src)
        {
            double sx = 0, sy = 0;
            int n = 0;
            foreach (var p in src)
            {
                sx += p.x;
                sy += p.y;
                n++;
            }
            return n > 0 ? (sx / n, sy / n) : (0, 0);
        }

        static (double x, double y) PickFromSubset(List<(double x, double y, bool isCorner)> subset)
        {
            var mids = subset.Where(s => !s.isCorner).ToList();
            if (mids.Count > 0)
                return Average(mids);
            return Average(subset);
        }

        var onLeft = list.Where(s => Math.Abs(s.x) <= leftTol).ToList();
        if (onLeft.Count > 0)
            return PickFromSubset(onLeft);

        var mids = list.Where(s => !s.isCorner).ToList();
        if (mids.Count > 0)
            return Average(mids);
        return Average(list);
    }

    public static void FillGlobalCoordsFromConnectivity(
        int nels,
        double[] coordX,
        double[] coordY,
        IReadOnlyList<int[]> connectivity,
        int nx1, int nx2, int ny1, int ny2,
        double w1, double s1, double w2, double h1, double h2)
    {
        if (coordX == null || coordY == null || coordX.Length == 0 || connectivity == null)
            return;

        for (int e = 0; e < nels && e < connectivity.Count; e++)
        {
            var g = Emb2dGeom(e + 1, nx1, nx2, ny1, ny2, w1, s1, w2, h1, h2);
            var conn = connectivity[e];
            if (conn == null || conn.Length < 8)
                continue;

            for (int k = 0; k < 8; k++)
            {
                int nid = conn[k];
                int j = Array.IndexOf(g.Num, nid);
                if (j < 0)
                    continue;
                int idx = nid - 1;
                if (idx >= 0 && idx < coordX.Length && idx < coordY.Length)
                {
                    coordX[idx] = g.CoordX[j];
                    coordY[idx] = g.CoordY[j];
                }
            }
        }
    }

    private sealed class GeomResult
    {
        public int[] Num { get; } = new int[8];
        public double[] CoordX { get; } = new double[8];
        public double[] CoordY { get; } = new double[8];
    }

    private static GeomResult Emb2dGeom(
        int iel, int nx1, int nx2, int ny1, int ny2,
        double w1, double s1, double w2, double h1, double h2)
    {
        int nxe = nx1 + nx2;
        int nye = ny1 + ny2;
        int nt = nx1 * ny1;
        int nc = (3 * nye + 2) * nx1 + 2 * ny1;
        double facx = s1 / ny1;
        double facy = h1 / ny1;
        double facb = (w1 + s1) / nx1;
        double facs = ny2 != 0 ? h2 / ny2 : 0.0;
        double frh = nx2 != 0 ? w2 / nx2 : 0.0;

        int iq, ip;
        if (iel <= nt)
        {
            iq = (iel - 1) / nx1 + 1;
            ip = iel - (iq - 1) * nx1;
        }
        else
        {
            iq = (iel - nt - 1) / nxe + ny1 + 1;
            ip = iel - nt - (iq - ny1 - 1) * nxe;
        }

        var g = new GeomResult();

        if (ip <= nx1)
        {
            g.Num[0] = (ip - 1) * (3 * nye + 2) + 2 * iq + 1;
            g.Num[1] = g.Num[0] - 1;
            g.Num[2] = g.Num[0] - 2;
            g.Num[3] = (ip - 1) * (3 * nye + 2) + 2 * nye + iq + 1;
            g.Num[4] = ip * (3 * nye + 2) + 2 * iq - 1;
            g.Num[5] = g.Num[4] + 1;
            g.Num[6] = g.Num[4] + 2;
            g.Num[7] = g.Num[3] + 1;

            if (iq <= ny1)
            {
                g.CoordX[0] = (ip - 1) * (w1 + iq * facx) / nx1;
                g.CoordX[2] = (ip - 1) * (w1 + (iq - 1) * facx) / nx1;
                g.CoordX[4] = ip * (w1 + (iq - 1) * facx) / nx1;
                g.CoordX[6] = ip * (w1 + iq * facx) / nx1;
                g.CoordY[0] = -iq * facy;
                g.CoordY[2] = -(iq - 1) * facy;
                g.CoordY[4] = -(iq - 1) * facy;
                g.CoordY[6] = -iq * facy;
            }
            else
            {
                g.CoordX[0] = (ip - 1) * facb;
                g.CoordX[2] = (ip - 1) * facb;
                g.CoordX[4] = ip * facb;
                g.CoordX[6] = ip * facb;
                g.CoordY[0] = -h1 - (iq - ny1) * facs;
                g.CoordY[2] = -h1 - (iq - ny1 - 1) * facs;
                g.CoordY[4] = -h1 - (iq - ny1 - 1) * facs;
                g.CoordY[6] = -h1 - (iq - ny1) * facs;
            }
        }
        else
        {
            g.Num[0] = nc + (ip - nx1 - 1) * (3 * ny2 + 2) + 2 * (iq - ny1) + 1;
            g.Num[1] = g.Num[0] - 1;
            g.Num[2] = g.Num[0] - 2;
            g.Num[3] = nc + (ip - nx1 - 1) * (3 * ny2 + 2) + 2 * ny2 + iq - ny1 + 1;
            g.Num[4] = nc + (ip - nx1) * (3 * ny2 + 2) + 2 * (iq - ny1) - 1;
            g.Num[5] = g.Num[4] + 1;
            g.Num[6] = g.Num[4] + 2;
            g.Num[7] = g.Num[3] + 1;

            g.CoordX[0] = w1 + s1 + (ip - nx1 - 1) * frh;
            g.CoordX[2] = g.CoordX[0];
            g.CoordX[4] = w1 + s1 + (ip - nx1) * frh;
            g.CoordX[6] = g.CoordX[4];
            g.CoordY[0] = -h1 - (iq - ny1) * facs;
            g.CoordY[2] = -h1 - (iq - ny1 - 1) * facs;
            g.CoordY[4] = -h1 - (iq - ny1 - 1) * facs;
            g.CoordY[6] = -h1 - (iq - ny1) * facs;
        }

        // Midside nodes at local indices 1,3,5,7 (see library/geom/emb_2d_geom.cpp).
        g.CoordX[1] = 0.5 * (g.CoordX[0] + g.CoordX[2]);
        g.CoordY[1] = 0.5 * (g.CoordY[0] + g.CoordY[2]);
        g.CoordX[3] = 0.5 * (g.CoordX[2] + g.CoordX[4]);
        g.CoordY[3] = 0.5 * (g.CoordY[2] + g.CoordY[4]);
        g.CoordX[5] = 0.5 * (g.CoordX[4] + g.CoordX[6]);
        g.CoordY[5] = 0.5 * (g.CoordY[4] + g.CoordY[6]);
        g.CoordX[7] = 0.5 * (g.CoordX[6] + g.CoordX[0]);
        g.CoordY[7] = 0.5 * (g.CoordY[6] + g.CoordY[0]);

        return g;
    }

    private static int[] Emb2dBc(int nx1, int nx2, int ny1, int ny2, int nn)
    {
        const int nodof = 2;
        var nf = new int[nodof * nn];
        int nye = ny1 + ny2;
        int nm = 0;
        int ic = 0;

        void SetNf(int x, int y)
        {
            nf[0 + nm * nodof] = x;
            nf[1 + nm * nodof] = y;
            nm++;
        }

        for (int i = 0; i < 2 * nye; i++)
            SetNf(0, ++ic);
        SetNf(0, 0);

        for (int j = 0; j < nx1 - 1; j++)
        {
            for (int i = 0; i < nye; i++)
                SetNf(++ic, ++ic);
            SetNf(0, 0);
            for (int i = 0; i < 2 * nye; i++)
                SetNf(++ic, ++ic);
            SetNf(0, 0);
        }

        for (int i = 0; i < nye; i++)
            SetNf(++ic, ++ic);
        SetNf(0, 0);

        for (int i = 0; i < 2 * ny1; i++)
            SetNf(++ic, ++ic);

        if (nx2 == 0)
        {
            for (int i = 0; i < 2 * ny2; i++)
                SetNf(0, ++ic);
            SetNf(0, 0);
        }
        else
        {
            for (int i = 0; i < 2 * ny2; i++)
                SetNf(++ic, ++ic);
            SetNf(0, 0);
            for (int jj = 0; jj < nx2; jj++)
            {
                for (int i = 0; i < ny2; i++)
                    SetNf(++ic, ++ic);
                SetNf(0, 0);
                for (int i = 0; i < 2 * ny2; i++)
                {
                    int fixX = jj == nx2 - 1 ? 0 : ++ic;
                    SetNf(fixX, ++ic);
                }
                SetNf(0, 0);
            }
        }

        if (nm != nn)
            throw new InvalidOperationException($"emb_2d_bc node count mismatch: built {nm} expected {nn}");

        return nf;
    }
}
