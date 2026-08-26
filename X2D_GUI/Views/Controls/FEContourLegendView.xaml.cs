using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GeotechSlopeApp.Models.FiniteElement;
using GeotechSlopeApp.ViewModels;
using GeotechSlopeApp.Visualization;

namespace GeotechSlopeApp.Views.Controls;

public partial class FEContourLegendView : UserControl
{
    public FEContourLegendView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }
    
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ResultsViewModel viewModel)
        {
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
            UpdateLegend(viewModel);
        }
    }
    
    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResultsViewModel.SelectedOutputType) ||
            e.PropertyName == nameof(ResultsViewModel.ContourFillMode) ||
            e.PropertyName == nameof(ResultsViewModel.VisualizationMode) ||
            e.PropertyName == nameof(ResultsViewModel.FeNodalDisplacements) ||
            e.PropertyName == nameof(ResultsViewModel.FeElementStresses) ||
            e.PropertyName == nameof(ResultsViewModel.JointResults) ||
            e.PropertyName == nameof(ResultsViewModel.FENodes) ||
            e.PropertyName == nameof(ResultsViewModel.FEElements) ||
            e.PropertyName == nameof(ResultsViewModel.SelectedStageInfo) ||
            e.PropertyName == nameof(ResultsViewModel.SelectedStageResult))
        {
            if (sender is ResultsViewModel viewModel)
            {
                UpdateLegend(viewModel);
            }
        }
    }
    
    private void UpdateLegend(ResultsViewModel viewModel)
    {
        var nodes = viewModel.FENodes;
        var elements = viewModel.FEElements;
        var outputType = viewModel.SelectedOutputType;
        
        if (nodes == null || elements == null || nodes.Count == 0 || elements.Count == 0)
        {
            MinValueText.Text = "N/A";
            MaxValueText.Text = "N/A";
            return;
        }

        if (string.Equals(viewModel.VisualizationMode, ResultsViewModel.VisualizationModeContour1D, StringComparison.OrdinalIgnoreCase))
        {
            if (FEOneDContourCanvasView.TryGetScalarRange(viewModel, out double min1D, out double max1D))
            {
                var (unit1D, dMin, dMax) = GetUnitAndDisplayValues(outputType, min1D, max1D);
                MinValueText.Text = $"{dMin:E3} {unit1D}";
                MaxValueText.Text = $"{dMax:E3} {unit1D}";
            }
            else
            {
                MinValueText.Text = "N/A";
                MaxValueText.Text = "N/A";
            }
            return;
        }

        var activeElements = FEContourScaleHelper.GetContourElements(viewModel, outputType);
        if (activeElements.Count == 0)
        {
            MinValueText.Text = "N/A";
            MaxValueText.Text = "N/A";
            return;
        }

        var (minValue, maxValue) = FEContourScaleHelper.ComputeDisplayColorRange(viewModel, outputType, activeElements);
        var (unit, displayMin, displayMax) = GetUnitAndDisplayValues(outputType, minValue, maxValue);
        MinValueText.Text = $"{displayMin:E3} {unit}";
        MaxValueText.Text = $"{displayMax:E3} {unit}";
    }
    
    private static (string unit, double displayMin, double displayMax) GetUnitAndDisplayValues(string outputType, double minValue, double maxValue)
    {
        var unit = ResultsViewModel.GetScalarUnit(outputType);
        return (unit == "-" ? string.Empty : unit, minValue, maxValue);
    }
    
    private Dictionary<int, double>? GetNodeValues(
        IReadOnlyList<Node> nodes,
        IReadOnlyList<ElementBase> elements,
        string outputType,
        ResultsViewModel viewModel)
    {
        var nodeValues = new Dictionary<int, double>();

        if (ResultsViewModel.IsNodalScalarOutput(outputType))
        {
            // Key by FENodes list index (Element.NodeIds are 0-based indices).
            for (int i = 0; i < nodes.Count; i++)
                nodeValues[i] = viewModel.GetNodeScalarValue(nodes[i], outputType);
        }
        else if (ResultsViewModel.IsElementScalarOutput(outputType))
        {
            var removedIds = GetRemovedElementIds(viewModel);
            var activeElements = elements
                .Where(e => removedIds != null
                    ? !removedIds.Contains(e.Id)
                    : e.IsActive)
                .ToList();

            var nodeValueSums = new Dictionary<int, double>();
            var nodeCounts = new Dictionary<int, int>();

            foreach (var element in activeElements)
            {
                double elementValue = viewModel.GetElementScalarValue(element, outputType, nodes);
                foreach (var nodeId in element.NodeIds)
                {
                    if (nodeId < 0 || nodeId >= nodes.Count) continue;
                    nodeValueSums.TryAdd(nodeId, 0);
                    nodeCounts.TryAdd(nodeId, 0);
                    nodeValueSums[nodeId] += elementValue;
                    nodeCounts[nodeId]++;
                }
            }

            foreach (var nodeId in nodeValueSums.Keys)
                nodeValues[nodeId] = nodeValueSums[nodeId] / nodeCounts[nodeId];
        }

        return nodeValues;
    }

    /// <summary>Matches <see cref="FEContourCanvasView"/> staged excavation filter.</summary>
    private static HashSet<int>? GetRemovedElementIds(ResultsViewModel viewModel)
    {
        if (viewModel.SelectedStageResult != null && viewModel.StagedAnalysisResult != null)
        {
            var ids = new HashSet<int>();
            int sel = viewModel.SelectedStageResult.StageNumber;
            foreach (var sr in viewModel.StagedAnalysisResult.StageResults)
            {
                if (sr.StageNumber <= sel)
                {
                    foreach (var id in sr.RemovedElementIds)
                        ids.Add(id);
                }
            }
            return ids;
        }

        if (viewModel.StagedAnalysisResult?.StageResults?.Count > 0)
        {
            var ids = new HashSet<int>();
            foreach (var sr in viewModel.StagedAnalysisResult.StageResults)
            {
                foreach (var id in sr.RemovedElementIds)
                    ids.Add(id);
            }
            return ids;
        }

        return null;
    }
}
