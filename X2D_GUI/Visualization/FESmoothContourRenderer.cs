using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GeotechSlopeApp.Models.FiniteElement;

namespace GeotechSlopeApp.Visualization;

/// <summary>
/// Rasterizes smooth, nodally interpolated contour bands on a regular pixel grid.
/// Element stresses are first averaged to nodes; values are then barycentrically
/// interpolated inside each triangle and mapped through the shared blue→red scale.
/// </summary>
public static class FESmoothContourRenderer
{
    public const int DefaultContourLevels = 20;

    private sealed class SpatialIndex
    {
        public required List<int>[] Buckets { get; init; }
        public required int GridCols { get; init; }
        public required int GridRows { get; init; }
        public required double WorldMinX { get; init; }
        public required double WorldMinY { get; init; }
        public required double CellW { get; init; }
        public required double CellH { get; init; }
    }

    private readonly struct ContourTriangle
    {
        public double X0 { get; init; }
        public double Y0 { get; init; }
        public double X1 { get; init; }
        public double Y1 { get; init; }
        public double X2 { get; init; }
        public double Y2 { get; init; }
        public double V0 { get; init; }
        public double V1 { get; init; }
        public double V2 { get; init; }
        public double MinX { get; init; }
        public double MinY { get; init; }
        public double MaxX { get; init; }
        public double MaxY { get; init; }
    }

    public static Image? BuildContourImage(
        IReadOnlyList<Node> nodes,
        IReadOnlyList<ElementBase> activeElements,
        IReadOnlyDictionary<int, double> nodeValues,
        double minValue,
        double maxValue,
        double scale,
        Point offset,
        int pixelWidth,
        int pixelHeight,
        bool smithQ8,
        int contourLevels = DefaultContourLevels)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0 || activeElements.Count == 0)
            return null;

        var triangles = BuildTriangles(nodes, activeElements, nodeValues, smithQ8);
        if (triangles.Count == 0)
            return null;

        var spatialIndex = BuildSpatialIndex(triangles, pixelWidth, pixelHeight, scale, offset);

        int stride = pixelWidth * 4;
        var pixels = new byte[stride * pixelHeight];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
            pixels[i + 1] = 255;
            pixels[i + 2] = 255;
            pixels[i + 3] = 255;
        }

        for (int py = 0; py < pixelHeight; py++)
        {
            double canvasY = py + 0.5;
            double worldY = (offset.Y - canvasY) / scale;
            int rowOffset = py * stride;

            for (int px = 0; px < pixelWidth; px++)
            {
                double canvasX = px + 0.5;
                double worldX = (canvasX - offset.X) / scale;

                if (!TrySampleValue(worldX, worldY, triangles, spatialIndex, out double value))
                {
                    continue;
                }

                Color color = QuantizeBandColor(value, minValue, maxValue, contourLevels);
                int idx = rowOffset + px * 4;
                pixels[idx] = color.B;
                pixels[idx + 1] = color.G;
                pixels[idx + 2] = color.R;
                pixels[idx + 3] = 255;
            }
        }

        var bitmap = new WriteableBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, pixelWidth, pixelHeight), pixels, stride, 0);

        return new Image
        {
            Source = bitmap,
            Width = pixelWidth,
            Height = pixelHeight,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        };
    }

    private static Color QuantizeBandColor(double value, double minValue, double maxValue, int contourLevels)
    {
        if (contourLevels < 2)
            return FEContourColorMap.GetColorForValue(value, minValue, maxValue);

        if (Math.Abs(maxValue - minValue) < 1e-10)
            return FEContourColorMap.GetColorForValue(value, minValue, maxValue);

        double normalized = (value - minValue) / (maxValue - minValue);
        normalized = Math.Max(0, Math.Min(1, normalized));
        int band = Math.Min(contourLevels - 1, (int)(normalized * contourLevels));
        double bandCenter = (band + 0.5) / contourLevels;
        return FEContourColorMap.GetColorForNormalizedValue(bandCenter);
    }

    private static List<ContourTriangle> BuildTriangles(
        IReadOnlyList<Node> nodes,
        IReadOnlyList<ElementBase> activeElements,
        IReadOnlyDictionary<int, double> nodeValues,
        bool smithQ8)
    {
        var triangles = new List<ContourTriangle>(activeElements.Count * 2);

        foreach (var element in activeElements)
        {
            var corners = FEMeshOutlineHelper.GetContourCornerNodeIndices(element, smithQ8);
            if (corners.Count == 3)
            {
                TryAddTriangle(nodes, nodeValues, element, corners, 0, 1, 2, triangles);
            }
            else if (corners.Count >= 4)
            {
                TryAddTriangle(nodes, nodeValues, element, corners, 0, 1, 2, triangles);
                TryAddTriangle(nodes, nodeValues, element, corners, 0, 2, 3, triangles);
            }
        }

        return triangles;
    }

    private static void TryAddTriangle(
        IReadOnlyList<Node> nodes,
        IReadOnlyDictionary<int, double> nodeValues,
        ElementBase element,
        IReadOnlyList<int> cornerLocals,
        int a,
        int b,
        int c,
        List<ContourTriangle> triangles)
    {
        if (!TryGetCorner(nodes, nodeValues, element, cornerLocals, a, out var n0, out var v0)) return;
        if (!TryGetCorner(nodes, nodeValues, element, cornerLocals, b, out var n1, out var v1)) return;
        if (!TryGetCorner(nodes, nodeValues, element, cornerLocals, c, out var n2, out var v2)) return;

        triangles.Add(new ContourTriangle
        {
            X0 = n0.X, Y0 = n0.Y, V0 = v0,
            X1 = n1.X, Y1 = n1.Y, V1 = v1,
            X2 = n2.X, Y2 = n2.Y, V2 = v2,
            MinX = Math.Min(n0.X, Math.Min(n1.X, n2.X)),
            MinY = Math.Min(n0.Y, Math.Min(n1.Y, n2.Y)),
            MaxX = Math.Max(n0.X, Math.Max(n1.X, n2.X)),
            MaxY = Math.Max(n0.Y, Math.Max(n1.Y, n2.Y))
        });
    }

    private static bool TryGetCorner(
        IReadOnlyList<Node> nodes,
        IReadOnlyDictionary<int, double> nodeValues,
        ElementBase element,
        IReadOnlyList<int> cornerLocals,
        int cornerIndex,
        out Node node,
        out double value)
    {
        node = null!;
        value = 0;
        if (cornerIndex < 0 || cornerIndex >= cornerLocals.Count) return false;

        int localIdx = cornerLocals[cornerIndex];
        if (localIdx < 0 || localIdx >= element.NodeIds.Length) return false;

        int nodeId = element.NodeIds[localIdx];
        if (nodeId < 0 || nodeId >= nodes.Count) return false;

        node = nodes[nodeId];
        if (!nodeValues.TryGetValue(nodeId, out value))
            value = 0;
        return true;
    }

    private static SpatialIndex BuildSpatialIndex(
        IReadOnlyList<ContourTriangle> triangles,
        int pixelWidth,
        int pixelHeight,
        double scale,
        Point offset)
    {
        const int targetCells = 64;
        int gridCols = Math.Clamp((int)Math.Sqrt(triangles.Count * 2), 8, targetCells);
        int gridRows = Math.Clamp(gridCols * pixelHeight / Math.Max(pixelWidth, 1), 8, targetCells);

        var buckets = new List<int>[gridCols * gridRows];
        for (int i = 0; i < buckets.Length; i++)
            buckets[i] = new List<int>();

        double worldMinX = (0 - offset.X) / scale;
        double worldMaxX = (pixelWidth - offset.X) / scale;
        double worldMinY = (offset.Y - pixelHeight) / scale;
        double worldMaxY = offset.Y / scale;
        double cellW = (worldMaxX - worldMinX) / gridCols;
        double cellH = (worldMaxY - worldMinY) / gridRows;
        if (cellW <= 0) cellW = 1;
        if (cellH <= 0) cellH = 1;

        for (int ti = 0; ti < triangles.Count; ti++)
        {
            var tri = triangles[ti];
            int c0 = Math.Clamp((int)((tri.MinX - worldMinX) / cellW), 0, gridCols - 1);
            int c1 = Math.Clamp((int)((tri.MaxX - worldMinX) / cellW), 0, gridCols - 1);
            int r0 = Math.Clamp((int)((tri.MinY - worldMinY) / cellH), 0, gridRows - 1);
            int r1 = Math.Clamp((int)((tri.MaxY - worldMinY) / cellH), 0, gridRows - 1);

            for (int row = r0; row <= r1; row++)
            {
                for (int col = c0; col <= c1; col++)
                    buckets[row * gridCols + col].Add(ti);
            }
        }

        return new SpatialIndex
        {
            Buckets = buckets,
            GridCols = gridCols,
            GridRows = gridRows,
            WorldMinX = worldMinX,
            WorldMinY = worldMinY,
            CellW = cellW,
            CellH = cellH
        };
    }

    private static bool TrySampleValue(
        double worldX,
        double worldY,
        IReadOnlyList<ContourTriangle> triangles,
        SpatialIndex index,
        out double value)
    {
        value = 0;
        int col = Math.Clamp((int)((worldX - index.WorldMinX) / index.CellW), 0, index.GridCols - 1);
        int row = Math.Clamp((int)((worldY - index.WorldMinY) / index.CellH), 0, index.GridRows - 1);

        var candidates = index.Buckets[row * index.GridCols + col];
        foreach (int ti in candidates)
        {
            var tri = triangles[ti];
            if (worldX < tri.MinX || worldX > tri.MaxX || worldY < tri.MinY || worldY > tri.MaxY)
                continue;

            if (TryBarycentricSample(worldX, worldY, tri, out value))
                return true;
        }

        return false;
    }

    private static bool TryBarycentricSample(double px, double py, ContourTriangle tri, out double value)
    {
        value = 0;
        var (inside, l0, l1, l2) = BarycentricCoords(
            px, py,
            tri.X0, tri.Y0, tri.X1, tri.Y1, tri.X2, tri.Y2);
        if (!inside) return false;

        value = l0 * tri.V0 + l1 * tri.V1 + l2 * tri.V2;
        return true;
    }

    private static (bool inside, double l0, double l1, double l2) BarycentricCoords(
        double px, double py,
        double x0, double y0, double x1, double y1, double x2, double y2)
    {
        double denom = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2);
        if (Math.Abs(denom) < 1e-20) return (false, 0, 0, 0);
        double l0 = ((y1 - y2) * (px - x2) + (x2 - x1) * (py - y2)) / denom;
        double l1 = ((y2 - y0) * (px - x2) + (x0 - x2) * (py - y2)) / denom;
        double l2 = 1.0 - l0 - l1;
        return (l0 >= -1e-6 && l1 >= -1e-6 && l2 >= -1e-6, l0, l1, l2);
    }
}
