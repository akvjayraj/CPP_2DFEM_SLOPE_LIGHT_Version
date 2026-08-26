using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Builds RS2-style load-arrow overlays for auto-assigned ponded face pressures
/// (<see cref="PondedWaterEdgeLoadBuilder"/> / DistributedEdgeLoads).
/// Magnitude labels are thinned globally so dense meshes stay readable.
/// </summary>
public static class PondedFacePressureOverlayBuilder
{
    private const double ArrowLengthCanvas = 14.0;
    private const double BracketOffsetCanvas = 10.0;
    private const double LabelOffsetCanvas = 10.0;
    private const double HeadOutwardOffsetCanvas = 5.0;
    private const double CanvasEdgeMargin = 18.0;
    private const double LabelExtraMargin = 20.0;
    private const int MaxArrowsPerEdge = 5;
    /// <summary>Minimum canvas spacing between magnitude callouts.</summary>
    private const double MinLabelSpacingCanvas = 56.0;
    private const double LabelFontSize = 11.0;

    private sealed class EdgeGraphics
    {
        public required LineLoadOverlayDisplay Overlay { get; set; }
        public Point LabelAnchor { get; init; }
        public Vector Outward { get; init; }
        public double Magnitude { get; init; }
    }

    public static IReadOnlyList<LineLoadOverlayDisplay> Build(
        GroundwaterModel model,
        MeshViewModel? mesh,
        GeometryDefinitionViewModel geometry)
    {
        var groups = PondedWaterEdgeLoadBuilder.CollectGroups(model, mesh, geometry);
        if (groups.Count == 0 || mesh == null)
            return Array.Empty<LineLoadOverlayDisplay>();

        var graphics = new List<EdgeGraphics>();
        foreach (var group in groups)
        {
            foreach (var edge in group.Edges)
            {
                var built = BuildEdgeOverlay(edge, group.DirX, group.DirY, mesh, geometry);
                if (built != null)
                    graphics.Add(built);
            }
        }

        AttachThinnedLabels(graphics, geometry);
        return graphics.Select(g => g.Overlay).ToList();
    }

    private static EdgeGraphics? BuildEdgeOverlay(
        DistributedEdgeLoadDatWriter.EdgeLoadRow edge,
        double dirX,
        double dirY,
        MeshViewModel mesh,
        GeometryDefinitionViewModel geometry)
    {
        if (!TryNode(mesh, edge.NodeA, out var aAxis) || !TryNode(mesh, edge.NodeB, out var bAxis))
            return null;

        var a = geometry.ConvertAxisToCanvas(aAxis);
        var b = geometry.ConvertAxisToCanvas(bAxis);
        Point? midCanvas = null;
        if (edge.NodeC > 0 && TryNode(mesh, edge.NodeC, out var cAxis))
            midCanvas = geometry.ConvertAxisToCanvas(cAxis);

        // Prefer geometric midpoint of corners for direction probe (stable for curved Q8 edges).
        var dirProbeAxis = new Point(0.5 * (aAxis.X + bAxis.X), 0.5 * (aAxis.Y + bAxis.Y));
        var tipAxis = new Point(dirProbeAxis.X + dirX, dirProbeAxis.Y + dirY);
        var mid = geometry.ConvertAxisToCanvas(dirProbeAxis);
        var tip = geometry.ConvertAxisToCanvas(tipAxis);
        var loadDir = new Vector(tip.X - mid.X, tip.Y - mid.Y);
        if (loadDir.Length < 1e-9)
            return null;
        loadDir.Normalize();
        // Tail sits outside the face; head at the contact — arrow points into the soil.
        var outward = new Vector(-loadDir.X, -loadDir.Y);

        var stations = SampleStations(a, b, midCanvas, MaxArrowsPerEdge);
        if (stations.Count < 2)
            return null;

        double maxMag = Math.Max(Math.Abs(edge.QA), Math.Abs(edge.QB));
        if (maxMag < 1e-9)
            maxMag = 1.0;

        double fitScale = ComputeOverlayFitScale(stations, outward, geometry);
        double headOffset = HeadOutwardOffsetCanvas * fitScale;
        double baseArrowLen = ArrowLengthCanvas * fitScale;
        double bracketOffset = BracketOffsetCanvas * fitScale;
        double headSize = Math.Max(2.5, 5.0 * fitScale);
        double minArrowLen = Math.Max(1.5, 2.0 * fitScale);

        var arrows = new List<LineLoadArrowDisplay>(stations.Count);
        Point? firstTail = null;
        Point? lastTail = null;
        bool triangular = Math.Abs(edge.QA - edge.QB) > 1e-6;
        double edgeMag = triangular
            ? 0.5 * (edge.QA + edge.QB)
            : 0.5 * (edge.QA + edge.QB);

        for (int i = 0; i < stations.Count; i++)
        {
            var contact = stations[i];
            double t = stations.Count == 1 ? 0.5 : (double)i / (stations.Count - 1);
            double mag = triangular
                ? edge.QA + t * (edge.QB - edge.QA)
                : edgeMag;
            double arrowLen = triangular
                ? baseArrowLen * Math.Abs(mag) / maxMag
                : baseArrowLen;
            if (arrowLen < minArrowLen)
                arrowLen = minArrowLen;

            var head = new Point(
                contact.X + outward.X * headOffset,
                contact.Y + outward.Y * headOffset);
            var tail = new Point(head.X + outward.X * arrowLen, head.Y + outward.Y * arrowLen);
            var headDir = new Vector(head.X - tail.X, head.Y - tail.Y);
            if (headDir.Length > 1e-6)
                headDir.Normalize();
            else
                headDir = loadDir;
            var perp = new Vector(-headDir.Y, headDir.X);
            var headLeft = new Point(
                head.X - headDir.X * headSize + perp.X * headSize * 0.45,
                head.Y - headDir.Y * headSize + perp.Y * headSize * 0.45);
            var headRight = new Point(
                head.X - headDir.X * headSize - perp.X * headSize * 0.45,
                head.Y - headDir.Y * headSize - perp.Y * headSize * 0.45);

            arrows.Add(new LineLoadArrowDisplay
            {
                HeadX = head.X,
                HeadY = head.Y,
                TailX = tail.X,
                TailY = tail.Y,
                HeadLeftX = headLeft.X,
                HeadLeftY = headLeft.Y,
                HeadRightX = headRight.X,
                HeadRightY = headRight.Y,
                ArrowHeadPoints = new PointCollection { head, headLeft, headRight },
            });
            firstTail ??= tail;
            lastTail = tail;
        }

        if (firstTail == null || lastTail == null)
            return null;

        Point bracketStart;
        Point bracketEnd;
        if (triangular)
        {
            bracketStart = firstTail.Value;
            bracketEnd = lastTail.Value;
        }
        else
        {
            var bracketOffsetVec = new Vector(outward.X * bracketOffset, outward.Y * bracketOffset);
            bracketStart = new Point(firstTail.Value.X + bracketOffsetVec.X, firstTail.Value.Y + bracketOffsetVec.Y);
            bracketEnd = new Point(lastTail.Value.X + bracketOffsetVec.X, lastTail.Value.Y + bracketOffsetVec.Y);
        }

        var labelAnchor = new Point(
            (bracketStart.X + bracketEnd.X) * 0.5,
            (bracketStart.Y + bracketEnd.Y) * 0.5);

        return new EdgeGraphics
        {
            Overlay = new LineLoadOverlayDisplay
            {
                EdgePoints = new PointCollection(stations),
                BracketStart = bracketStart,
                BracketEnd = bracketEnd,
                Arrows = arrows,
                Labels = Array.Empty<LineLoadLabelDisplay>(),
            },
            LabelAnchor = labelAnchor,
            Outward = outward,
            Magnitude = edgeMag,
        };
    }

    /// <summary>
    /// Place a few readable magnitude callouts: extrema first, then fill by spacing.
    /// </summary>
    private static void AttachThinnedLabels(
        IReadOnlyList<EdgeGraphics> graphics,
        GeometryDefinitionViewModel geometry)
    {
        if (graphics.Count == 0) return;

        double labelOffset = LabelOffsetCanvas;
        var candidates = graphics
            .Where(g => Math.Abs(g.Magnitude) > 1e-9)
            .Select(g =>
            {
                var anchor = new Point(
                    g.LabelAnchor.X + g.Outward.X * labelOffset,
                    g.LabelAnchor.Y + g.Outward.Y * labelOffset);
                return (G: g, Anchor: anchor);
            })
            .ToList();
        if (candidates.Count == 0) return;

        var selected = new List<(EdgeGraphics G, Point Anchor)>();
        double minDistSq = MinLabelSpacingCanvas * MinLabelSpacingCanvas;

        // Always keep the highest and lowest face pressures when distinct.
        var byMag = candidates.OrderByDescending(c => c.G.Magnitude).ToList();
        TryAdd(selected, byMag[0], minDistSq);
        if (byMag.Count > 1)
            TryAdd(selected, byMag[^1], minDistSq);

        // Walk along the free surface left→right so crest / face / toe get spaced labels.
        foreach (var c in candidates.OrderBy(c => c.Anchor.X).ThenBy(c => c.Anchor.Y))
            TryAdd(selected, c, minDistSq);

        // Cap so a huge mesh never floods the canvas (still keeps extrema + spaced fillers).
        const int maxLabels = 12;
        if (selected.Count > maxLabels)
        {
            selected = selected
                .OrderByDescending(s => Math.Abs(s.G.Magnitude))
                .Take(maxLabels)
                .ToList();
        }

        foreach (var (g, anchor) in selected)
        {
            var label = LoadOverlayShared.CreateLabel(
                anchor,
                LoadOverlayShared.FormatMagnitude(g.Magnitude),
                LabelFontSize);
            var list = new List<LineLoadLabelDisplay> { label };
            LoadOverlayShared.ClampLabelsInsideCanvas(list, geometry);
            g.Overlay = new LineLoadOverlayDisplay
            {
                EdgePoints = g.Overlay.EdgePoints,
                BracketStart = g.Overlay.BracketStart,
                BracketEnd = g.Overlay.BracketEnd,
                Arrows = g.Overlay.Arrows,
                Labels = list,
            };
        }
    }

    private static void TryAdd(
        List<(EdgeGraphics G, Point Anchor)> selected,
        (EdgeGraphics G, Point Anchor) candidate,
        double minDistSq)
    {
        foreach (var s in selected)
        {
            double dx = s.Anchor.X - candidate.Anchor.X;
            double dy = s.Anchor.Y - candidate.Anchor.Y;
            if (dx * dx + dy * dy < minDistSq)
                return;
        }
        selected.Add(candidate);
    }

    private static List<Point> SampleStations(Point a, Point b, Point? mid, int maxCount)
    {
        // Quadratic edge: place arrows at corners + midside (T6/Q8 DistributedEdgeLoads NodeC).
        if (mid is { } m)
            return new List<Point> { a, m, b };

        var list = new List<Point>();
        var span = b - a;
        double len = span.Length;
        if (len < 1e-6)
            return list;
        int n = Math.Clamp((int)Math.Round(len / 22.0) + 1, 2, maxCount);
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / (n - 1);
            list.Add(new Point(a.X + span.X * t, a.Y + span.Y * t));
        }
        return list;
    }

    private static double ComputeOverlayFitScale(
        IReadOnlyList<Point> stations,
        Vector outward,
        GeometryDefinitionViewModel geometry)
    {
        double requiredExtent = HeadOutwardOffsetCanvas
            + ArrowLengthCanvas
            + BracketOffsetCanvas
            + LabelOffsetCanvas
            + LabelExtraMargin
            + LoadOverlayShared.MinLabelFontSize * 1.2;

        double minClearance = double.PositiveInfinity;
        foreach (var contact in stations)
        {
            double clearance = ComputeOutwardClearance(
                contact, outward, geometry.CanvasWidth, geometry.CanvasHeight, CanvasEdgeMargin);
            if (clearance < minClearance)
                minClearance = clearance;
        }

        if (!double.IsFinite(minClearance) || minClearance >= requiredExtent)
            return 1.0;
        return Math.Clamp(minClearance / requiredExtent, 0.04, 1.0);
    }

    private static double ComputeOutwardClearance(
        Point point, Vector outward, double canvasWidth, double canvasHeight, double margin)
    {
        double minDistance = double.PositiveInfinity;
        if (outward.X < -1e-9)
            minDistance = Math.Min(minDistance, (point.X - margin) / -outward.X);
        if (outward.X > 1e-9)
            minDistance = Math.Min(minDistance, (canvasWidth - margin - point.X) / outward.X);
        if (outward.Y < -1e-9)
            minDistance = Math.Min(minDistance, (point.Y - margin) / -outward.Y);
        if (outward.Y > 1e-9)
            minDistance = Math.Min(minDistance, (canvasHeight - margin - point.Y) / outward.Y);
        return double.IsPositiveInfinity(minDistance) ? double.PositiveInfinity : Math.Max(0, minDistance);
    }

    private static bool TryNode(MeshViewModel mesh, int nodeId, out Point p)
    {
        p = default;
        foreach (var n in mesh.DiscretizationNodes)
        {
            if (n.NodeId == nodeId)
            {
                p = n.Location;
                return true;
            }
        }

        int idx = nodeId - 1;
        if (idx >= 0 && idx < mesh.DiscretizationNodes.Count)
        {
            p = mesh.DiscretizationNodes[idx].Location;
            return true;
        }

        return false;
    }
}
