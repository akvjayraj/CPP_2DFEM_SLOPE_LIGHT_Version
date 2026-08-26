using System;
using System.Collections.Generic;
using System.Linq;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace GeotechSlopeApp.Backend;

/// <summary>Builds Smith-style SRF vs d_max plots from p64 .res trial tables.</summary>
public static class SrfConvergencePlotBuilder
{
    public static PlotModel Build(
        IReadOnlyList<P64ResFileParser.SrfTrialRow> trials,
        int iterationLimit,
        double? estimatedFoS = null)
    {
        var ordered = trials.OrderBy(t => t.Srf).ToList();
        var displacements = ordered.Select(t => t.MaxDisplacement).Where(d => d > 0 && double.IsFinite(d)).ToList();

        var model = new PlotModel
        {
            Title = "SRF vs. d_max",
            PlotMargins = new OxyThickness(72, 4, 40, 50),
        };

        // Displacement (d_max) spans huge orders of magnitude once failed trials (near-infinite
        // iteration blow-up) are included alongside converged trials — a logarithmic axis with
        // scientific tick labels keeps both the converged knee and the failed tail readable on
        // the same plot instead of squashing everything near the origin.
        var xAxis = new LogarithmicAxis
        {
            Position = AxisPosition.Bottom,
            Title = "d_max (m)",
            Base = 10,
            StringFormat = "0.00E+0",
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot,
            MinimumPadding = 0.05,
            MaximumPadding = 0.05,
        };
        if (displacements.Count > 0)
        {
            xAxis.Minimum = displacements.Min() * 0.5;
            xAxis.Maximum = displacements.Max() * 2.0;
        }
        model.Axes.Add(xAxis);

        var yAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "SRF",
            IsAxisVisible = true,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot,
            StringFormat = "0.00E+0",
            MinimumPadding = 0.08,
            MaximumPadding = 0.08,
        };
        model.Axes.Add(yAxis);

        var lineSeries = new LineSeries
        {
            Title = "SRF trials",
            Color = OxyColor.FromRgb(30, 41, 59),
            MarkerType = MarkerType.Diamond,
            MarkerSize = 5,
            MarkerFill = OxyColor.FromRgb(30, 41, 59),
            MarkerStroke = OxyColors.White,
            MarkerStrokeThickness = 0.8,
            StrokeThickness = 1.5,
        };

        foreach (var t in ordered)
        {
            // Logarithmic X axis requires strictly positive, finite displacement values.
            if (t.MaxDisplacement > 0 && double.IsFinite(t.MaxDisplacement))
                lineSeries.Points.Add(new DataPoint(t.MaxDisplacement, t.Srf));
        }

        if (lineSeries.Points.Count > 0)
            model.Series.Add(lineSeries);

        if (estimatedFoS is > 0)
        {
            model.Annotations.Add(new LineAnnotation
            {
                Type = LineAnnotationType.Horizontal,
                Y = estimatedFoS.Value,
                Color = OxyColor.FromRgb(220, 38, 38),
                LineStyle = LineStyle.Solid,
                StrokeThickness = 1.5,
                Text = $"FS ≈ {estimatedFoS.Value:F2}",
                TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Right,
                TextVerticalAlignment = OxyPlot.VerticalAlignment.Bottom,
            });
        }

        return model;
    }
}
