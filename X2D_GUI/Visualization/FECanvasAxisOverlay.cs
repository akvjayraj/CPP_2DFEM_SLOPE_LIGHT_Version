using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GeotechSlopeApp.Visualization;

/// <summary>
/// Screen-fixed X/Y rulers for FE Results canvases (contour / deformed / vector),
/// matching the 1–2–5×10ⁿ tick style used on Geometry/Mesh/Load canvases.
/// </summary>
public static class FECanvasAxisOverlay
{
    private const double MinTickPx = 48.0;
    private const double MaxTickPx = 160.0;
    private const double TargetTickPx = 72.0;
    private const double MajorTickLen = 6.0;
    private const double LabelFontSize = 10.0;

    private static readonly Brush AxisStroke = Freeze(new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)));
    private static readonly Brush TickStroke = Freeze(new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)));
    private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)));
    private static readonly Brush SymmetryStroke = Freeze(new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)));
    private static readonly Brush SymmetryLabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x6D, 0x28, 0xD9)));
    private static readonly DoubleCollection SymmetryDash = FreezeDash(new DoubleCollection { 8, 4 });

    public readonly record struct ViewTransform(
        double ModelScale,
        double OffsetX,
        double OffsetY,
        double Zoom,
        double PanX,
        double PanY);

    public static void Clear(Canvas? overlay) => overlay?.Children.Clear();

    /// <summary>
    /// Draws nice X/Y ticks for a rectangular plot area already laid out in canvas pixels
    /// (used by the 1D contour strip chart).
    /// </summary>
    public static void RedrawPlotFrame(
        Canvas canvas,
        double plotLeft,
        double plotTop,
        double plotWidth,
        double plotHeight,
        double xMin,
        double xMax,
        double yMin,
        double yMax,
        string? xAxisTitle = null)
    {
        if (canvas == null || plotWidth < 40 || plotHeight < 40) return;
        double xRange = xMax - xMin;
        double yRange = yMax - yMin;
        if (xRange <= 1e-12 || yRange <= 1e-12) return;

        double xBase = plotTop + plotHeight;
        canvas.Children.Add(new Line
        {
            X1 = plotLeft, Y1 = xBase, X2 = plotLeft + plotWidth, Y2 = xBase,
            Stroke = AxisStroke, StrokeThickness = 1
        });
        canvas.Children.Add(new Line
        {
            X1 = plotLeft, Y1 = plotTop, X2 = plotLeft, Y2 = xBase,
            Stroke = AxisStroke, StrokeThickness = 1
        });

        var xStep = NiceStep(xRange, plotWidth);
        var yStep = NiceStep(yRange, plotHeight);
        var xFmt = LabelFormat(xStep);
        var yFmt = LabelFormat(yStep);

        for (var x = AlignStart(xMin, xStep); x <= xMax + xStep * 1e-9; x += xStep)
        {
            double sx = plotLeft + (x - xMin) / xRange * plotWidth;
            if (sx < plotLeft - 1 || sx > plotLeft + plotWidth + 1) continue;
            canvas.Children.Add(new Line
            {
                X1 = sx, Y1 = xBase, X2 = sx, Y2 = xBase - MajorTickLen,
                Stroke = TickStroke, StrokeThickness = 1
            });
            var tb = new TextBlock
            {
                Text = FormatValue(x, xFmt),
                FontSize = LabelFontSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = LabelBrush
            };
            canvas.Children.Add(tb);
            Canvas.SetLeft(tb, sx + 1);
            Canvas.SetTop(tb, xBase + 2);
        }

        for (var y = AlignStart(yMin, yStep); y <= yMax + yStep * 1e-9; y += yStep)
        {
            double sy = plotTop + (1.0 - (y - yMin) / yRange) * plotHeight;
            if (sy < plotTop - 1 || sy > xBase + 1) continue;
            canvas.Children.Add(new Line
            {
                X1 = plotLeft, Y1 = sy, X2 = plotLeft + MajorTickLen, Y2 = sy,
                Stroke = TickStroke, StrokeThickness = 1
            });
            var tb = new TextBlock
            {
                Text = FormatValue(y, yFmt),
                FontSize = LabelFontSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = LabelBrush
            };
            canvas.Children.Add(tb);
            Canvas.SetLeft(tb, Math.Max(2, plotLeft - 42));
            Canvas.SetTop(tb, Math.Max(0, sy - 8));
        }

        if (!string.IsNullOrWhiteSpace(xAxisTitle))
        {
            var title = new TextBlock
            {
                Text = xAxisTitle,
                FontSize = 11,
                Foreground = LabelBrush
            };
            canvas.Children.Add(title);
            Canvas.SetLeft(title, plotLeft);
            Canvas.SetTop(title, xBase + 18);
        }
    }

    public static void Redraw(
        Canvas overlay,
        double viewportWidth,
        double viewportHeight,
        in ViewTransform t,
        bool showAxisOfSymmetry = false)
    {
        overlay.Children.Clear();
        if (viewportWidth < 48 || viewportHeight < 48
            || t.ModelScale <= 1e-12 || t.Zoom <= 1e-12)
            return;

        // Visible world from the four viewport corners (Y-up model space).
        var bl = ScreenToWorld(0, viewportHeight, t);
        var br = ScreenToWorld(viewportWidth, viewportHeight, t);
        var tl = ScreenToWorld(0, 0, t);
        var tr = ScreenToWorld(viewportWidth, 0, t);

        double xMin = Min4(bl.X, br.X, tl.X, tr.X);
        double xMax = Max4(bl.X, br.X, tl.X, tr.X);
        double yMin = Min4(bl.Y, br.Y, tl.Y, tr.Y);
        double yMax = Max4(bl.Y, br.Y, tl.Y, tr.Y);
        double xRange = xMax - xMin;
        double yRange = yMax - yMin;
        if (xRange <= 1e-12 || yRange <= 1e-12)
            return;

        // Frame lines (left + bottom), like Geometry / Mesh canvases.
        overlay.Children.Add(new Line
        {
            X1 = 0, Y1 = 0, X2 = 0, Y2 = viewportHeight,
            Stroke = AxisStroke, StrokeThickness = 1
        });
        overlay.Children.Add(new Line
        {
            X1 = 0, Y1 = viewportHeight, X2 = viewportWidth, Y2 = viewportHeight,
            Stroke = AxisStroke, StrokeThickness = 1
        });

        if (showAxisOfSymmetry)
            DrawAxisOfSymmetry(overlay, viewportWidth, viewportHeight, yMin, yMax, t);

        var xStep = NiceStep(xRange, viewportWidth);
        var yStep = NiceStep(yRange, viewportHeight);
        var xFmt = LabelFormat(xStep);
        var yFmt = LabelFormat(yStep);

        for (var x = AlignStart(xMin, xStep); x <= xMax + xStep * 1e-9; x += xStep)
        {
            // Place X ticks using mid-height screen X so pan/zoom stay consistent.
            double sx = WorldToScreen(x, 0.5 * (yMin + yMax), t).X;
            if (sx < 4 || sx > viewportWidth - 4) continue;

            overlay.Children.Add(new Line
            {
                X1 = sx, Y1 = viewportHeight,
                X2 = sx, Y2 = viewportHeight - MajorTickLen,
                Stroke = TickStroke, StrokeThickness = 1
            });

            var label = FormatValue(x, xFmt);
            var tb = new TextBlock
            {
                Text = label,
                FontSize = LabelFontSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = LabelBrush
            };
            overlay.Children.Add(tb);
            Canvas.SetLeft(tb, sx + 2);
            Canvas.SetTop(tb, Math.Max(0, viewportHeight - 18));
        }

        for (var y = AlignStart(yMin, yStep); y <= yMax + yStep * 1e-9; y += yStep)
        {
            double sy = WorldToScreen(0.5 * (xMin + xMax), y, t).Y;
            if (sy < 4 || sy > viewportHeight - 4) continue;

            overlay.Children.Add(new Line
            {
                X1 = 0, Y1 = sy,
                X2 = MajorTickLen, Y2 = sy,
                Stroke = TickStroke, StrokeThickness = 1
            });

            var label = FormatValue(y, yFmt);
            var tb = new TextBlock
            {
                Text = label,
                FontSize = LabelFontSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = LabelBrush
            };
            overlay.Children.Add(tb);
            Canvas.SetLeft(tb, 6);
            Canvas.SetTop(tb, Math.Max(0, sy - 10));
        }
    }

    /// <summary>Vertical dashed line at world x = 0 (radial / axis of symmetry).</summary>
    private static void DrawAxisOfSymmetry(
        Canvas overlay,
        double viewportWidth,
        double viewportHeight,
        double yMin,
        double yMax,
        in ViewTransform t)
    {
        var top = WorldToScreen(0, yMax, t);
        var bottom = WorldToScreen(0, yMin, t);
        // Skip if the axis is completely off-screen horizontally.
        if (top.X < -2 && bottom.X < -2) return;
        if (top.X > viewportWidth + 2 && bottom.X > viewportWidth + 2) return;

        overlay.Children.Add(new Line
        {
            X1 = top.X,
            Y1 = top.Y,
            X2 = bottom.X,
            Y2 = bottom.Y,
            Stroke = SymmetryStroke,
            StrokeThickness = 2,
            StrokeDashArray = SymmetryDash,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });

        var label = new TextBlock
        {
            Text = "Axis of symmetry",
            FontSize = LabelFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = SymmetryLabelBrush,
            Background = Brushes.White,
            Opacity = 0.92,
            Padding = new Thickness(2, 0, 2, 0)
        };
        overlay.Children.Add(label);
        Canvas.SetLeft(label, Math.Clamp(top.X + 6, 2, Math.Max(2, viewportWidth - 110)));
        Canvas.SetTop(label, Math.Clamp(Math.Min(top.Y, bottom.Y) + 4, 2, Math.Max(2, viewportHeight - 18)));
    }

    public static Point WorldToScreen(double worldX, double worldY, in ViewTransform t)
    {
        double cx = worldX * t.ModelScale + t.OffsetX;
        double cy = -worldY * t.ModelScale + t.OffsetY;
        return new Point(cx * t.Zoom + t.PanX, cy * t.Zoom + t.PanY);
    }

    public static Point ScreenToWorld(double screenX, double screenY, in ViewTransform t)
    {
        double cx = (screenX - t.PanX) / t.Zoom;
        double cy = (screenY - t.PanY) / t.Zoom;
        double wx = (cx - t.OffsetX) / t.ModelScale;
        double wy = (t.OffsetY - cy) / t.ModelScale;
        return new Point(wx, wy);
    }

    private static double NiceStep(double range, double canvasLength)
    {
        var rough = range * (TargetTickPx / Math.Max(canvasLength, 1.0));
        var step = NiceNumber(rough, round: true);
        if (step <= 0) step = NiceNumber(range / 5.0, round: true);

        double Pixels(double s) => s / range * canvasLength;
        var guard = 0;
        while (Pixels(step) < MinTickPx && guard++ < 24)
            step = BumpNice(step, +1);
        guard = 0;
        while (Pixels(step) > MaxTickPx && guard++ < 24)
        {
            var next = BumpNice(step, -1);
            if (next >= step - 1e-15 || next <= 0) break;
            if (range / next < 2.5) break;
            step = next;
        }
        return step;
    }

    private static double NiceNumber(double value, bool round)
    {
        if (!(value > 0) || double.IsNaN(value) || double.IsInfinity(value))
            return 1.0;
        var exp = Math.Floor(Math.Log10(value));
        var pow10 = Math.Pow(10, exp);
        var fraction = value / pow10;
        double niceFraction;
        if (round)
        {
            if (fraction < 1.5) niceFraction = 1;
            else if (fraction < 3.0) niceFraction = 2;
            else if (fraction < 7.0) niceFraction = 5;
            else niceFraction = 10;
        }
        else
        {
            if (fraction <= 1.0) niceFraction = 1;
            else if (fraction <= 2.0) niceFraction = 2;
            else if (fraction <= 5.0) niceFraction = 5;
            else niceFraction = 10;
        }
        return niceFraction * pow10;
    }

    private static double BumpNice(double step, int direction)
    {
        if (!(step > 0)) return 1.0;
        var exp = Math.Floor(Math.Log10(step));
        var pow10 = Math.Pow(10, exp);
        var fraction = step / pow10;
        int nice = fraction < 1.5 ? 1 : fraction < 3.5 ? 2 : fraction < 7.5 ? 5 : 10;
        if (direction >= 0)
        {
            if (nice < 2) nice = 2;
            else if (nice < 5) nice = 5;
            else { nice = 1; exp++; }
        }
        else
        {
            if (nice > 5) nice = 5;
            else if (nice > 2) nice = 2;
            else if (nice > 1) nice = 1;
            else { nice = 5; exp--; }
        }
        return nice * Math.Pow(10, exp);
    }

    private static double AlignStart(double visibleMin, double step)
        => Math.Floor(visibleMin / step + 1e-12) * step;

    private static string LabelFormat(double step)
    {
        var abs = Math.Abs(step);
        if (abs >= 1) return "0";
        if (abs >= 0.1) return "0.#";
        if (abs >= 0.01) return "0.##";
        return "0.###";
    }

    private static string FormatValue(double value, string format)
    {
        var rounded = Math.Abs(value) < 1e-9 ? 0 : value;
        return rounded.ToString(format, CultureInfo.CurrentCulture);
    }

    private static double Min4(double a, double b, double c, double d)
        => Math.Min(Math.Min(a, b), Math.Min(c, d));

    private static double Max4(double a, double b, double c, double d)
        => Math.Max(Math.Max(a, b), Math.Max(c, d));

    private static Brush Freeze(SolidColorBrush brush)
    {
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    private static DoubleCollection FreezeDash(DoubleCollection dash)
    {
        if (dash.CanFreeze) dash.Freeze();
        return dash;
    }
}
