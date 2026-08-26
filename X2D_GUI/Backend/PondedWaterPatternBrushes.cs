using System.Windows;
using System.Windows.Media;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Backend;

/// <summary>Builds canvas fill brushes for <see cref="PondedWaterDisplayPattern"/>.</summary>
public static class PondedWaterPatternBrushes
{
    private static readonly Color TealLight = Color.FromArgb(0x55, 0x14, 0xB8, 0xA6);
    private static readonly Color TealMid = Color.FromArgb(0x66, 0x0F, 0x76, 0x6E);
    private static readonly Color TealDeep = Color.FromArgb(0x88, 0x11, 0x5E, 0x59);
    private static readonly Color HatchStroke = Color.FromArgb(0xBB, 0x0F, 0x76, 0x6E);
    private static readonly Color WaveStroke = Color.FromArgb(0xAA, 0x0D, 0x94, 0x88);

    public static Brush Create(
        PondedWaterDisplayPattern pattern,
        double canvasTopY,
        double canvasBottomY)
    {
        Brush brush = pattern switch
        {
            PondedWaterDisplayPattern.Solid => CreateSolid(),
            PondedWaterDisplayPattern.DiagonalHatch => CreateDiagonalHatch(),
            PondedWaterDisplayPattern.CrossHatch => CreateCrossHatch(),
            PondedWaterDisplayPattern.Waves => CreateWaves(),
            PondedWaterDisplayPattern.Dots => CreateDots(),
            _ => CreateDepthGradient(canvasTopY, canvasBottomY),
        };
        if (brush.CanFreeze)
            brush.Freeze();
        return brush;
    }

    private static Brush CreateDepthGradient(double canvasTopY, double canvasBottomY)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, canvasTopY),
            EndPoint = new Point(0.5, canvasBottomY),
            MappingMode = BrushMappingMode.Absolute,
            SpreadMethod = GradientSpreadMethod.Pad
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x28, 0x0D, 0x94, 0x88), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, 0x0F, 0x76, 0x6E), 0.55));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x99, 0x11, 0x5E, 0x59), 1.0));
        return brush;
    }

    private static Brush CreateSolid() =>
        new SolidColorBrush(TealMid);

    private static Brush CreateDiagonalHatch()
    {
        const double tile = 10.0;
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Color.FromArgb(0x30, 0xCC, 0xFB, 0xF1)),
            null,
            new RectangleGeometry(new Rect(0, 0, tile, tile))));
        group.Children.Add(new GeometryDrawing(
            null,
            new Pen(new SolidColorBrush(HatchStroke), 1.2),
            new LineGeometry(new Point(0, tile), new Point(tile, 0))));
        return Tile(group, tile);
    }

    private static Brush CreateCrossHatch()
    {
        const double tile = 10.0;
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Color.FromArgb(0x28, 0xCC, 0xFB, 0xF1)),
            null,
            new RectangleGeometry(new Rect(0, 0, tile, tile))));
        var pen = new Pen(new SolidColorBrush(HatchStroke), 1.1);
        group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, tile), new Point(tile, 0))));
        group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 0), new Point(tile, tile))));
        return Tile(group, tile);
    }

    private static Brush CreateWaves()
    {
        const double tileW = 16.0;
        const double tileH = 8.0;
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Color.FromArgb(0x35, 0x99, 0xF6, 0xE4)),
            null,
            new RectangleGeometry(new Rect(0, 0, tileW, tileH))));

        var fig = new PathFigure { StartPoint = new Point(0, tileH * 0.55), IsClosed = false };
        fig.Segments.Add(new QuadraticBezierSegment(
            new Point(tileW * 0.25, tileH * 0.15),
            new Point(tileW * 0.5, tileH * 0.55),
            true));
        fig.Segments.Add(new QuadraticBezierSegment(
            new Point(tileW * 0.75, tileH * 0.95),
            new Point(tileW, tileH * 0.55),
            true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        group.Children.Add(new GeometryDrawing(
            null,
            new Pen(new SolidColorBrush(WaveStroke), 1.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
            geo));

        return new DrawingBrush(group)
        {
            Viewport = new Rect(0, 0, tileW, tileH),
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.Tile,
            Stretch = Stretch.None
        };
    }

    private static Brush CreateDots()
    {
        const double tile = 8.0;
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Color.FromArgb(0x2A, 0x99, 0xF6, 0xE4)),
            null,
            new RectangleGeometry(new Rect(0, 0, tile, tile))));
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(TealDeep),
            null,
            new EllipseGeometry(new Point(tile * 0.35, tile * 0.35), 1.4, 1.4)));
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(TealLight),
            null,
            new EllipseGeometry(new Point(tile * 0.75, tile * 0.7), 1.1, 1.1)));
        return Tile(group, tile);
    }

    private static DrawingBrush Tile(DrawingGroup group, double tile) =>
        new(group)
        {
            Viewport = new Rect(0, 0, tile, tile),
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.Tile,
            Stretch = Stretch.None
        };
}
