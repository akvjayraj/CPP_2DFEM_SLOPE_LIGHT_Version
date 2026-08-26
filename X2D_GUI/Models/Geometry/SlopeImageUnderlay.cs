using System;
using System.Windows;
using System.Windows.Media;

namespace GeotechSlopeApp.Models.Geometry;

/// <summary>How the slope image is mapped into model (axis) coordinates.</summary>
public enum SlopeImageCalibrationMode
{
    /// <summary>Two image pixels with known world coordinates define a similarity transform.</summary>
    TwoPoint = 0,

    /// <summary>Image fills an axis-aligned world rectangle (no rotation).</summary>
    AxisAlignedBounds = 1
}

/// <summary>
/// Georeferenced raster underlay for the Geometry Definition canvas. Digitize tools still create
/// ordinary vertices; the image is a visual guide only.
/// </summary>
public sealed class SlopeImageUnderlay
{
    public bool IsVisible { get; set; } = true;

    /// <summary>0 = transparent, 1 = opaque.</summary>
    public double Opacity { get; set; } = 0.65;

    /// <summary>Absolute path to the source or sidecar image on disk (runtime).</summary>
    public string SourcePath { get; set; } = "";

    public SlopeImageCalibrationMode Mode { get; set; } = SlopeImageCalibrationMode.TwoPoint;

    public int ImagePixelWidth { get; set; }
    public int ImagePixelHeight { get; set; }

    // --- Two-point calibration (image pixels, origin top-left, Y down) ---
    public Point Point1ImagePx { get; set; }
    public Point Point1World { get; set; }
    public Point Point2ImagePx { get; set; }
    public Point Point2World { get; set; }

    // --- Axis-aligned bounds (world, Y up) ---
    public double WorldMinX { get; set; }
    public double WorldMinY { get; set; }
    public double WorldMaxX { get; set; }
    public double WorldMaxY { get; set; }

    public bool HasValidImageSize => ImagePixelWidth > 0 && ImagePixelHeight > 0;

    public bool TryGetWorldBounds(out double minX, out double minY, out double maxX, out double maxY)
    {
        minX = minY = maxX = maxY = 0;
        if (!HasValidImageSize) return false;

        if (Mode == SlopeImageCalibrationMode.AxisAlignedBounds)
        {
            minX = Math.Min(WorldMinX, WorldMaxX);
            maxX = Math.Max(WorldMinX, WorldMaxX);
            minY = Math.Min(WorldMinY, WorldMaxY);
            maxY = Math.Max(WorldMinY, WorldMaxY);
            return maxX - minX > 1e-12 && maxY - minY > 1e-12;
        }

        if (!TryImageToWorld(0, 0, out var tl)
            || !TryImageToWorld(ImagePixelWidth, 0, out var tr)
            || !TryImageToWorld(0, ImagePixelHeight, out var bl)
            || !TryImageToWorld(ImagePixelWidth, ImagePixelHeight, out var br))
            return false;

        minX = Math.Min(Math.Min(tl.X, tr.X), Math.Min(bl.X, br.X));
        maxX = Math.Max(Math.Max(tl.X, tr.X), Math.Max(bl.X, br.X));
        minY = Math.Min(Math.Min(tl.Y, tr.Y), Math.Min(bl.Y, br.Y));
        maxY = Math.Max(Math.Max(tl.Y, tr.Y), Math.Max(bl.Y, br.Y));
        return maxX - minX > 1e-12 && maxY - minY > 1e-12;
    }

    /// <summary>Maps an image pixel (Y down) into world/axis coordinates (Y up).</summary>
    public bool TryImageToWorld(double imageX, double imageY, out Point world)
    {
        world = default;
        if (!HasValidImageSize) return false;

        if (Mode == SlopeImageCalibrationMode.AxisAlignedBounds)
        {
            if (!TryGetWorldBounds(out var minX, out var minY, out var maxX, out var maxY))
                return false;
            var u = imageX / ImagePixelWidth;
            var v = imageY / ImagePixelHeight;
            world = new Point(
                minX + u * (maxX - minX),
                maxY - v * (maxY - minY));
            return true;
        }

        if (!TryGetTwoPointSimilarity(out var scale, out var cos, out var sin, out var oImg, out var oWorld))
            return false;

        var dx = imageX - oImg.X;
        var dy = imageY - oImg.Y;
        // Image Y down → flip before rotating into world Y up.
        var ix = dx;
        var iy = -dy;
        world = new Point(
            oWorld.X + scale * (cos * ix - sin * iy),
            oWorld.Y + scale * (sin * ix + cos * iy));
        return true;
    }

    /// <summary>Inverse of <see cref="TryImageToWorld"/>.</summary>
    public bool TryWorldToImage(double worldX, double worldY, out Point imagePx)
    {
        imagePx = default;
        if (!HasValidImageSize) return false;

        if (Mode == SlopeImageCalibrationMode.AxisAlignedBounds)
        {
            if (!TryGetWorldBounds(out var minX, out var minY, out var maxX, out var maxY))
                return false;
            var u = (worldX - minX) / (maxX - minX);
            var v = (maxY - worldY) / (maxY - minY);
            imagePx = new Point(u * ImagePixelWidth, v * ImagePixelHeight);
            return true;
        }

        if (!TryGetTwoPointSimilarity(out var scale, out var cos, out var sin, out var oImg, out var oWorld))
            return false;
        if (scale < 1e-15) return false;

        var wx = (worldX - oWorld.X) / scale;
        var wy = (worldY - oWorld.Y) / scale;
        // Inverse rotation, then un-flip Y.
        var ix = cos * wx + sin * wy;
        var iy = -sin * wx + cos * wy;
        imagePx = new Point(oImg.X + ix, oImg.Y - iy);
        return true;
    }

    /// <summary>
    /// Matrix mapping image pixel space (0..W, 0..H, Y down) into model-canvas pixels so the
    /// underlay pan/zooms with <c>ScaledGeometryLayer</c>.
    /// </summary>
    public bool TryBuildCanvasMatrix(Func<Point, Point> axisToCanvas, out Matrix matrix)
    {
        matrix = Matrix.Identity;
        if (axisToCanvas == null || !HasValidImageSize) return false;

        if (Mode == SlopeImageCalibrationMode.AxisAlignedBounds)
        {
            if (!TryGetWorldBounds(out var minX, out var minY, out var maxX, out var maxY))
                return false;
            var topLeft = axisToCanvas(new Point(minX, maxY));
            var bottomRight = axisToCanvas(new Point(maxX, minY));
            var w = bottomRight.X - topLeft.X;
            var h = bottomRight.Y - topLeft.Y;
            if (Math.Abs(w) < 1e-9 || Math.Abs(h) < 1e-9) return false;
            matrix = new Matrix(
                w / ImagePixelWidth, 0,
                0, h / ImagePixelHeight,
                topLeft.X, topLeft.Y);
            return true;
        }

        if (!TryImageToWorld(Point1ImagePx.X, Point1ImagePx.Y, out var w1)
            || !TryImageToWorld(Point2ImagePx.X, Point2ImagePx.Y, out var w2))
            return false;

        var c1 = axisToCanvas(w1);
        var c2 = axisToCanvas(w2);
        var du = Point2ImagePx.X - Point1ImagePx.X;
        var dv = Point2ImagePx.Y - Point1ImagePx.Y;
        var lenI = Math.Sqrt(du * du + dv * dv);
        var dcx = c2.X - c1.X;
        var dcy = c2.Y - c1.Y;
        var lenC = Math.Sqrt(dcx * dcx + dcy * dcy);
        if (lenI < 1e-9 || lenC < 1e-9) return false;

        var s = lenC / lenI;
        var angI = Math.Atan2(dv, du);
        var angC = Math.Atan2(dcy, dcx);
        var ang = angC - angI;
        var cos = Math.Cos(ang);
        var sin = Math.Sin(ang);

        // Column-major WPF Matrix: maps image px → canvas px via similarity about Point1.
        var m11 = s * cos;
        var m12 = s * sin;
        var m21 = -s * sin;
        var m22 = s * cos;
        var ox = c1.X - (m11 * Point1ImagePx.X + m21 * Point1ImagePx.Y);
        var oy = c1.Y - (m12 * Point1ImagePx.X + m22 * Point1ImagePx.Y);
        matrix = new Matrix(m11, m12, m21, m22, ox, oy);
        return true;
    }

    public SlopeImageUnderlay Clone() => new()
    {
        IsVisible = IsVisible,
        Opacity = Opacity,
        SourcePath = SourcePath,
        Mode = Mode,
        ImagePixelWidth = ImagePixelWidth,
        ImagePixelHeight = ImagePixelHeight,
        Point1ImagePx = Point1ImagePx,
        Point1World = Point1World,
        Point2ImagePx = Point2ImagePx,
        Point2World = Point2World,
        WorldMinX = WorldMinX,
        WorldMinY = WorldMinY,
        WorldMaxX = WorldMaxX,
        WorldMaxY = WorldMaxY
    };

    private bool TryGetTwoPointSimilarity(
        out double scale, out double cos, out double sin, out Point oImg, out Point oWorld)
    {
        scale = 0;
        cos = 1;
        sin = 0;
        oImg = Point1ImagePx;
        oWorld = Point1World;

        var du = Point2ImagePx.X - Point1ImagePx.X;
        var dv = Point2ImagePx.Y - Point1ImagePx.Y;
        var lenI = Math.Sqrt(du * du + dv * dv);
        if (lenI < 1e-9) return false;

        var dx = Point2World.X - Point1World.X;
        var dy = Point2World.Y - Point1World.Y;
        var lenW = Math.Sqrt(dx * dx + dy * dy);
        if (lenW < 1e-15) return false;

        scale = lenW / lenI;
        // Image direction with Y flipped to match world Y-up basis.
        var ix = du;
        var iy = -dv;
        var lenFlip = Math.Sqrt(ix * ix + iy * iy);
        if (lenFlip < 1e-9) return false;
        ix /= lenFlip;
        iy /= lenFlip;
        var wx = dx / lenW;
        var wy = dy / lenW;
        // Rotation taking unit image (flipped) vector to unit world vector.
        cos = ix * wx + iy * wy;
        sin = ix * wy - iy * wx;
        return true;
    }
}
