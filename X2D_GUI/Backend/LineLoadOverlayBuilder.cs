using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>Builds RS2D-style line load overlay geometry from a <see cref="LoadModel"/> row.</summary>
public static class LineLoadOverlayBuilder
{
    private const double ArrowLengthCanvas = 14.0;
    private const double BracketOffsetCanvas = 10.0;
    private const double LabelOffsetCanvas = 8.0;
    private const double HeadOutwardOffsetCanvas = 5.0;
    private const double CanvasEdgeMargin = 18.0;
    private const double LabelExtraMargin = 20.0;
    private const int FallbackStationCount = 8;

    public static LineLoadOverlayDisplay? Build(
        LoadModel load,
        MeshViewModel? mesh,
        GeometryDefinitionViewModel geometry,
        ProblemDimension dimension)
    {
        if (!load.IsDistributedLineLoad
            || !LoadPlacementCoords.TryResolveLineCanvas(load, geometry, out var startCanvas, out var endCanvas))
            return null;

        var isTriangular = load.Type == LoadType.Triangular;
        var stations = CollectStationCanvasPoints(load, mesh, geometry, dimension, startCanvas, endCanvas);
        if (stations.Count < 2)
            return null;

        var edgePoints = new PointCollection(stations);
        var dir = ResolveCanvasDirection(load, geometry, mesh, stations[0], stations[^1], dimension);
        if (dir.Length < 1e-6)
            return null;

        dir.Normalize();
        var outward = new Vector(-dir.X, -dir.Y);
        double maxMag = Math.Max(Math.Abs(load.Magnitude), Math.Abs(load.EndMagnitude));
        if (maxMag < 1e-9)
            maxMag = 1.0;

        double fitScale = ComputeOverlayFitScale(stations, outward, geometry, isTriangular);
        double headOffset = HeadOutwardOffsetCanvas * fitScale;
        double baseArrowLen = ArrowLengthCanvas * fitScale;
        double bracketOffset = BracketOffsetCanvas * fitScale;
        double labelOffset = LabelOffsetCanvas * fitScale;
        double headSize = Math.Max(2.5, 5.0 * fitScale);
        double minArrowLen = Math.Max(1.5, 2.0 * fitScale);

        var arrows = new List<LineLoadArrowDisplay>(stations.Count);
        Point? firstTail = null;
        Point? lastTail = null;

        for (int i = 0; i < stations.Count; i++)
        {
            var contact = stations[i];
            double t = stations.Count == 1 ? 0.5 : (double)i / (stations.Count - 1);
            double mag = isTriangular
                ? load.Magnitude + t * (load.EndMagnitude - load.Magnitude)
                : load.Magnitude;
            double arrowLen = isTriangular
                ? baseArrowLen * Math.Abs(mag) / maxMag
                : baseArrowLen;
            if (arrowLen < minArrowLen)
                arrowLen = minArrowLen;

            // Head sits just outside the loaded edge; tail and bracket are further out.
            var head = new Point(
                contact.X + outward.X * headOffset,
                contact.Y + outward.Y * headOffset);
            var tail = new Point(head.X + outward.X * arrowLen, head.Y + outward.Y * arrowLen);
            var headDir = new Vector(head.X - tail.X, head.Y - tail.Y);
            if (headDir.Length > 1e-6)
                headDir.Normalize();
            else
                headDir = dir;
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
                ArrowHeadPoints = new PointCollection
                {
                    head,
                    headLeft,
                    headRight,
                },
            });
            firstTail ??= tail;
            lastTail = tail;
        }

        if (firstTail == null || lastTail == null)
            return null;

        Point bracketStart;
        Point bracketEnd;
        if (isTriangular)
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

        double edgeLength = (stations[^1] - stations[0]).Length;
        var labels = BuildLabels(
            load,
            isTriangular,
            stations,
            outward,
            labelOffset,
            fitScale,
            edgeLength,
            bracketStart,
            bracketEnd,
            arrows,
            geometry);

        return new LineLoadOverlayDisplay
        {
            EdgePoints = edgePoints,
            BracketStart = bracketStart,
            BracketEnd = bracketEnd,
            Arrows = arrows,
            Labels = labels,
        };
    }

    private static List<LineLoadLabelDisplay> BuildLabels(
        LoadModel load,
        bool isTriangular,
        IReadOnlyList<Point> stations,
        Vector outward,
        double labelOffset,
        double fitScale,
        double edgeLength,
        Point bracketStart,
        Point bracketEnd,
        IReadOnlyList<LineLoadArrowDisplay> arrows,
        GeometryDefinitionViewModel geometry)
    {
        var labels = new List<LineLoadLabelDisplay>(2);
        bool showStartEnd = isTriangular
            && Math.Abs(load.Magnitude - load.EndMagnitude) > 1e-6;
        int labelCount = showStartEnd ? 2 : 1;
        double fontSize = LoadOverlayShared.ComputeLabelFontSize(edgeLength, labelCount, fitScale);

        var tangent = stations[^1] - stations[0];
        if (tangent.Length < 1e-6)
            tangent = new Vector(1, 0);
        tangent.Normalize();
        double tangentPad = Math.Max(4.0, fontSize * 0.45);

        if (showStartEnd)
        {
            var startAnchor = new Point(
                bracketStart.X + outward.X * labelOffset - tangent.X * tangentPad,
                bracketStart.Y + outward.Y * labelOffset - tangent.Y * tangentPad);
            var endAnchor = new Point(
                bracketEnd.X + outward.X * labelOffset + tangent.X * tangentPad,
                bracketEnd.Y + outward.Y * labelOffset + tangent.Y * tangentPad);
            string startText = LoadOverlayShared.FormatMagnitude(load.Magnitude);
            string endText = LoadOverlayShared.FormatMagnitude(load.EndMagnitude);
            labels.Add(LoadOverlayShared.CreateLabel(startAnchor, startText, fontSize));
            labels.Add(LoadOverlayShared.CreateLabel(endAnchor, endText, fontSize));
        }
        else
        {
            Point anchor;
            if (arrows.Count > 0)
            {
                var midArrow = arrows[arrows.Count / 2];
                anchor = new Point(
                    (midArrow.TailX + bracketStart.X + bracketEnd.X) / 3.0,
                    (midArrow.TailY + bracketStart.Y + bracketEnd.Y) / 3.0);
            }
            else
            {
                anchor = new Point(
                    (bracketStart.X + bracketEnd.X) * 0.5,
                    (bracketStart.Y + bracketEnd.Y) * 0.5);
            }

            anchor = new Point(
                anchor.X + outward.X * labelOffset,
                anchor.Y + outward.Y * labelOffset);
            labels.Add(LoadOverlayShared.CreateLabel(anchor, LoadOverlayShared.FormatMagnitude(load.Magnitude), fontSize));
        }

        LoadOverlayShared.ClampLabelsInsideCanvas(labels, geometry);
        return labels;
    }

    private static double ComputeOverlayFitScale(
        IReadOnlyList<Point> stations,
        Vector outward,
        GeometryDefinitionViewModel geometry,
        bool isTriangular)
    {
        double requiredExtent = HeadOutwardOffsetCanvas
            + ArrowLengthCanvas
            + (isTriangular ? 0.0 : BracketOffsetCanvas)
            + LabelOffsetCanvas
            + LabelExtraMargin
            + LoadOverlayShared.MinLabelFontSize * 1.2;

        double minClearance = double.PositiveInfinity;
        foreach (var contact in stations)
        {
            double clearance = ComputeOutwardClearance(
                contact,
                outward,
                geometry.CanvasWidth,
                geometry.CanvasHeight,
                CanvasEdgeMargin);
            if (clearance < minClearance)
                minClearance = clearance;
        }

        if (!double.IsFinite(minClearance) || minClearance >= requiredExtent)
            return 1.0;

        return Math.Clamp(minClearance / requiredExtent, 0.04, 1.0);
    }

    private static double ComputeOutwardClearance(
        Point point,
        Vector outward,
        double canvasWidth,
        double canvasHeight,
        double margin)
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

    private static List<Point> CollectStationCanvasPoints(
        LoadModel load,
        MeshViewModel? mesh,
        GeometryDefinitionViewModel geometry,
        ProblemDimension dimension,
        Point startCanvas,
        Point endCanvas)
    {
        if (dimension == ProblemDimension.OneD)
            return Collect1DStationCanvasPoints(load, mesh, geometry, startCanvas, endCanvas);

        if (mesh is { IsDiscretized: true } && mesh.DiscretizationNodes.Count >= 2)
        {
            var fromMesh = Collect2DMeshStationCanvasPoints(load, mesh, geometry, startCanvas, endCanvas);
            if (fromMesh.Count >= 2)
                return fromMesh;
        }

        return CollectChordStationCanvasPoints(startCanvas, endCanvas);
    }

    private static List<Point> Collect1DStationCanvasPoints(
        LoadModel load,
        MeshViewModel? mesh,
        GeometryDefinitionViewModel geometry,
        Point startCanvas,
        Point endCanvas)
    {
        var stations = new List<Point>();
        if (mesh is not { IsDiscretized: true } || mesh.DiscretizationNodes.Count == 0)
            return CollectChordStationCanvasPoints(startCanvas, endCanvas);

        var startAxis = geometry.ConvertCanvasToAxis(startCanvas);
        var endAxis = geometry.ConvertCanvasToAxis(endCanvas);
        double sStart = geometry.GetPathDistanceAtPoint(startAxis.X, startAxis.Y);
        double sEnd = geometry.GetPathDistanceAtPoint(endAxis.X, endAxis.Y);
        double s0 = Math.Min(sStart, sEnd);
        double s1 = Math.Max(sStart, sEnd);

        var bounds = geometry.GetSegmentBounds();
        if (bounds.Count < 2)
            return CollectChordStationCanvasPoints(startCanvas, endCanvas);

        var nodePathPositions = new List<double>(mesh.DiscretizationNodes.Count);
        if (bounds.Count == mesh.DiscretizationNodes.Count)
        {
            nodePathPositions.AddRange(bounds);
        }
        else
        {
            double total = bounds[^1];
            int n = mesh.DiscretizationNodes.Count;
            for (int i = 0; i < n; i++)
                nodePathPositions.Add(total * i / Math.Max(1, n - 1));
        }

        for (int i = 0; i < mesh.DiscretizationNodes.Count; i++)
        {
            double s = nodePathPositions[Math.Min(i, nodePathPositions.Count - 1)];
            if (s < s0 - 1e-8 || s > s1 + 1e-8)
                continue;
            var n = mesh.DiscretizationNodes[i];
            stations.Add(geometry.ConvertAxisToCanvas(new Point(n.Location.X, n.Location.Y)));
        }

        return stations.Count >= 2 ? stations : CollectChordStationCanvasPoints(startCanvas, endCanvas);
    }

    private static List<Point> Collect2DMeshStationCanvasPoints(
        LoadModel load,
        MeshViewModel mesh,
        GeometryDefinitionViewModel geometry,
        Point startCanvas,
        Point endCanvas)
    {
        var startAxis = geometry.ConvertCanvasToAxis(startCanvas);
        var endAxis = geometry.ConvertCanvasToAxis(endCanvas);
        double segDx = endAxis.X - startAxis.X;
        double segDy = endAxis.Y - startAxis.Y;
        double segLenSq = segDx * segDx + segDy * segDy;
        if (segLenSq < 1e-12)
            return new List<Point>();

        double tolerance = EstimateAxisTolerance(mesh);
        var hits = new List<(double T, Point Canvas)>();

        foreach (var node in mesh.DiscretizationNodes)
        {
            double px = node.Location.X - startAxis.X;
            double py = node.Location.Y - startAxis.Y;
            double t = (px * segDx + py * segDy) / segLenSq;
            if (t < -0.05 || t > 1.05)
                continue;

            double closestX = startAxis.X + t * segDx;
            double closestY = startAxis.Y + t * segDy;
            double perp = Math.Sqrt(
                (node.Location.X - closestX) * (node.Location.X - closestX)
                + (node.Location.Y - closestY) * (node.Location.Y - closestY));
            if (perp > tolerance)
                continue;

            hits.Add((Math.Clamp(t, 0, 1), geometry.ConvertAxisToCanvas(new Point(node.Location.X, node.Location.Y))));
        }

        return hits
            .OrderBy(h => h.T)
            .Select(h => h.Canvas)
            .DistinctBy(p => $"{Math.Round(p.X, 2)}:{Math.Round(p.Y, 2)}")
            .ToList();
    }

    private static double EstimateAxisTolerance(MeshViewModel mesh)
    {
        var heights = new List<double>();
        foreach (var row in mesh.MeshInformationRows)
        {
            if (row.Node1 <= 0) continue;
            var nodes = mesh.DiscretizationNodes;
            if (TryGetAxisY(nodes, row.Node1, out var y1)
                && TryGetAxisY(nodes, row.Node2, out var y2))
            {
                heights.Add(Math.Abs(y2 - y1));
                if (row.Node3 > 0 && TryGetAxisY(nodes, row.Node3, out var y3))
                {
                    heights.Add(Math.Abs(y3 - y1));
                    heights.Add(Math.Abs(y3 - y2));
                }
            }
        }

        if (heights.Count == 0)
            return 0.5;

        heights.Sort();
        return Math.Max(0.05, heights[heights.Count / 2] * 0.75);
    }

    private static bool TryGetAxisY(IReadOnlyList<MeshDiscretizationNodeDisplay> nodes, int nodeId, out double y)
    {
        y = 0;
        int idx = nodeId - 1;
        if (idx < 0 || idx >= nodes.Count)
            return false;
        y = nodes[idx].Location.Y;
        return true;
    }

    private static List<Point> CollectChordStationCanvasPoints(Point start, Point end)
    {
        var stations = new List<Point>(FallbackStationCount);
        for (int i = 0; i < FallbackStationCount; i++)
        {
            double t = FallbackStationCount == 1 ? 0.5 : (double)i / (FallbackStationCount - 1);
            stations.Add(new Point(
                start.X + t * (end.X - start.X),
                start.Y + t * (end.Y - start.Y)));
        }
        return stations;
    }

    private static Vector ResolveCanvasDirection(
        LoadModel load,
        GeometryDefinitionViewModel geometry,
        MeshViewModel? mesh,
        Point firstStation,
        Point lastStation,
        ProblemDimension dimension)
    {
        var refCanvas = firstStation;
        var edgeMid = new Point(
            (firstStation.X + lastStation.X) * 0.5,
            (firstStation.Y + lastStation.Y) * 0.5);
        Vector canvasDir;

        if (load.Direction == LoadDirection.Axial && dimension != ProblemDimension.OneD)
        {
            var tangent = new Vector(lastStation.X - firstStation.X, lastStation.Y - firstStation.Y);
            if (tangent.Length < 1e-6)
                tangent = new Vector(1, 0);
            tangent.Normalize();
            var normalA = new Vector(-tangent.Y, tangent.X);
            var normalB = new Vector(tangent.Y, -tangent.X);
            canvasDir = PickInwardNormal(normalA, normalB, edgeMid, mesh, geometry);
            if (load.Magnitude < 0)
                canvasDir = new Vector(-canvasDir.X, -canvasDir.Y);
            return canvasDir;
        }

        Vector axisDir = load.Direction switch
        {
            LoadDirection.Y => new Vector(0, load.Magnitude >= 0 ? 1 : -1),
            LoadDirection.X => new Vector(load.Magnitude >= 0 ? 1 : -1, 0),
            _ => new Vector(lastStation.X - firstStation.X, lastStation.Y - firstStation.Y),
        };

        if (load.Direction == LoadDirection.Axial && axisDir.Length < 1e-6)
            axisDir = new Vector(1, 0);

        if (load.Direction == LoadDirection.Axial && load.Magnitude < 0)
            axisDir = new Vector(-axisDir.X, -axisDir.Y);

        var refAxis = geometry.ConvertCanvasToAxis(refCanvas);
        var axisTarget = new Point(refAxis.X + axisDir.X, refAxis.Y + axisDir.Y);
        var canvasTarget = geometry.ConvertAxisToCanvas(axisTarget);
        canvasDir = new Vector(canvasTarget.X - refCanvas.X, canvasTarget.Y - refCanvas.Y);

        if (dimension != ProblemDimension.OneD)
            canvasDir = EnsureTowardSoil(canvasDir, edgeMid, mesh, geometry);

        return canvasDir;
    }

    private static Vector PickInwardNormal(
        Vector normalA,
        Vector normalB,
        Point edgeMid,
        MeshViewModel? mesh,
        GeometryDefinitionViewModel geometry)
    {
        if (TryGetMeshCentroidCanvas(mesh, geometry, out var centroid))
        {
            var toInterior = new Vector(centroid.X - edgeMid.X, centroid.Y - edgeMid.Y);
            if (toInterior.Length < 1e-6)
                return normalA;
            return Vector.Multiply(normalA, toInterior) >= Vector.Multiply(normalB, toInterior)
                ? normalA
                : normalB;
        }

        // Typical 2D slope: soil mass sits below the crest on canvas (larger Y).
        return normalA.Y > normalB.Y ? normalA : normalB;
    }

    private static Vector EnsureTowardSoil(
        Vector canvasDir,
        Point edgeMid,
        MeshViewModel? mesh,
        GeometryDefinitionViewModel geometry)
    {
        if (canvasDir.Length < 1e-6)
            return canvasDir;
        if (!TryGetMeshCentroidCanvas(mesh, geometry, out var centroid))
            return canvasDir;

        var toInterior = new Vector(centroid.X - edgeMid.X, centroid.Y - edgeMid.Y);
        if (toInterior.Length < 1e-6)
            return canvasDir;

        canvasDir.Normalize();
        if (Vector.Multiply(canvasDir, toInterior) < 0)
            canvasDir = new Vector(-canvasDir.X, -canvasDir.Y);
        return canvasDir;
    }

    private static bool TryGetMeshCentroidCanvas(
        MeshViewModel? mesh,
        GeometryDefinitionViewModel geometry,
        out Point centroid)
    {
        centroid = default;
        if (mesh is not { IsDiscretized: true } || mesh.DiscretizationNodes.Count == 0)
            return false;

        double sumX = 0;
        double sumY = 0;
        foreach (var node in mesh.DiscretizationNodes)
        {
            var canvas = geometry.ConvertAxisToCanvas(new Point(node.Location.X, node.Location.Y));
            sumX += canvas.X;
            sumY += canvas.Y;
        }

        int count = mesh.DiscretizationNodes.Count;
        centroid = new Point(sumX / count, sumY / count);
        return true;
    }
}
