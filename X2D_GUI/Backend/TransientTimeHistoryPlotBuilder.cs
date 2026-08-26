using System;
using System.Collections.Generic;
using System.Linq;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace GeotechSlopeApp.Backend;

/// <summary>One (time, value) sample for a transient probe chart.</summary>
public readonly record struct TransientTimeSeriesPoint(double Time, double Value);

/// <summary>Builds a professional nodal time-history chart for transient consolidation.</summary>
public static class TransientTimeHistoryPlotBuilder
{
    public static PlotModel Build(
        IReadOnlyList<TransientTimeSeriesPoint> series,
        int nodeId,
        string quantityLabel,
        string yAxisTitle,
        double? currentTime = null,
        string? title = null)
    {
        var model = new PlotModel
        {
            Title = title ?? $"{quantityLabel} — node {nodeId}",
            Subtitle = "θ-method consolidation time history",
            PlotMargins = new OxyThickness(64, 8, 28, 48),
            TitleFontSize = 13,
            SubtitleFontSize = 10,
            TitleColor = OxyColor.FromRgb(15, 23, 42),
            SubtitleColor = OxyColor.FromRgb(71, 85, 105),
        };

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Time",
            MajorGridlineStyle = LineStyle.Solid,
            MajorGridlineColor = OxyColor.FromRgb(226, 232, 240),
            MinorGridlineStyle = LineStyle.Dot,
            MinorGridlineColor = OxyColor.FromRgb(241, 245, 249),
            TicklineColor = OxyColor.FromRgb(148, 163, 184),
            TitleColor = OxyColor.FromRgb(51, 65, 85),
            TextColor = OxyColor.FromRgb(71, 85, 105),
            MinimumPadding = 0.02,
            MaximumPadding = 0.04,
        });

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = yAxisTitle,
            MajorGridlineStyle = LineStyle.Solid,
            MajorGridlineColor = OxyColor.FromRgb(226, 232, 240),
            MinorGridlineStyle = LineStyle.Dot,
            MinorGridlineColor = OxyColor.FromRgb(241, 245, 249),
            TicklineColor = OxyColor.FromRgb(148, 163, 184),
            TitleColor = OxyColor.FromRgb(51, 65, 85),
            TextColor = OxyColor.FromRgb(71, 85, 105),
            MinimumPadding = 0.08,
            MaximumPadding = 0.08,
        });

        var line = new LineSeries
        {
            Title = $"Node {nodeId}",
            Color = OxyColor.FromRgb(37, 99, 235),
            StrokeThickness = 2.2,
            MarkerType = MarkerType.Circle,
            MarkerSize = 3.2,
            MarkerFill = OxyColor.FromRgb(37, 99, 235),
            MarkerStroke = OxyColors.White,
            MarkerStrokeThickness = 0.8,
            TrackerFormatString = "Time: {2:0.####}\nValue: {4:0.####}",
        };

        foreach (var s in series.OrderBy(h => h.Time))
        {
            if (double.IsFinite(s.Time) && double.IsFinite(s.Value))
                line.Points.Add(new DataPoint(s.Time, s.Value));
        }

        model.Series.Add(line);

        if (currentTime is { } t && double.IsFinite(t) && line.Points.Count > 0)
        {
            model.Annotations.Add(new LineAnnotation
            {
                Type = LineAnnotationType.Vertical,
                X = t,
                Color = OxyColor.FromRgb(220, 38, 38),
                StrokeThickness = 1.6,
                LineStyle = LineStyle.Dash,
                Text = $"t = {t:G4}",
                TextColor = OxyColor.FromRgb(185, 28, 28),
                TextHorizontalAlignment = HorizontalAlignment.Left,
                TextVerticalAlignment = VerticalAlignment.Top,
                TextMargin = 4,
            });

            var nearest = series
                .OrderBy(h => Math.Abs(h.Time - t))
                .FirstOrDefault();
            if (series.Count > 0)
            {
                model.Annotations.Add(new PointAnnotation
                {
                    X = nearest.Time,
                    Y = nearest.Value,
                    Shape = MarkerType.Circle,
                    Size = 6,
                    Fill = OxyColor.FromRgb(220, 38, 38),
                    Stroke = OxyColors.White,
                    StrokeThickness = 1.2,
                });
            }
        }

        return model;
    }
}
