using System;
using System.Collections.Generic;
using System.Linq;
using GeotechSlopeApp.Models.FiniteElement;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Visualization;

/// <summary>Shared contour color-scale logic for 2D FE fill and legend views.</summary>
public static class FEContourScaleHelper
{
    public const double GammaMaxPercentile = 98.0;

    public static HashSet<int>? GetRemovedElementIds(ResultsViewModel viewModel)
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

    public static List<ElementBase> GetActiveElements(ResultsViewModel viewModel)
    {
        var elements = viewModel.FEElements;
        if (elements == null || elements.Count == 0)
            return new List<ElementBase>();

        var removedIds = GetRemovedElementIds(viewModel);
        return elements
            .Where(e => removedIds != null
                ? !removedIds.Contains(e.Id)
                : e.IsActive)
            .ToList();
    }

    /// <summary>Active elements used for contour fill and color scale (γmax masks boundary artifacts).</summary>
    public static List<ElementBase> GetContourElements(ResultsViewModel viewModel, string outputType)
    {
        var active = GetActiveElements(viewModel);
        if (!ResultsViewModel.IsGammaMaxContourOutput(outputType))
            return active;

        var nodes = viewModel.FENodes;
        if (nodes == null || nodes.Count == 0)
            return active;

        var nodeById = BuildNodeLookup(nodes);
        var maskCtx = BuildGammaMaxMaskContext(viewModel.FEElements, nodes, nodeById);

        var result = new List<ElementBase>(active.Count);
        foreach (var element in active)
        {
            if (!ShouldMaskForGammaMax(viewModel, element, nodeById, maskCtx))
                result.Add(element);
        }

        return result;
    }

    public static (double Min, double Max) ComputeColorRange(
        ResultsViewModel viewModel,
        string outputType,
        IReadOnlyList<ElementBase>? activeElements = null)
    {
        if (ResultsViewModel.IsJointOutput(outputType))
            return ComputeJointColorRange(viewModel, outputType);

        var nodes = viewModel.FENodes;
        activeElements ??= GetContourElements(viewModel, outputType);

        if (nodes == null || nodes.Count == 0 || activeElements.Count == 0)
            return (0, 1);

        if (ResultsViewModel.IsElementScalarOutput(outputType))
        {
            var elementValues = activeElements
                .Select(e => viewModel.GetElementScalarValue(e, outputType, nodes))
                .ToList();

            if (ResultsViewModel.IsGammaMaxContourOutput(outputType))
                return ComputeGammaMaxColorRange(elementValues);

            double min = elementValues.Min();
            double max = elementValues.Max();
            if (Math.Abs(max - min) < 1e-10)
            {
                min -= 1e-6;
                max += 1e-6;
            }
            return (min, max);
        }

        // Key by FENodes list index — Element.NodeIds are 0-based indices, not solver Ids.
        var nodeValues = new Dictionary<int, double>(nodes.Count);
        for (int i = 0; i < nodes.Count; i++)
            nodeValues[i] = viewModel.GetNodeScalarValue(nodes[i], outputType);

        if (nodeValues.Count == 0)
            return (0, 1);

        double nMin = nodeValues.Values.Min();
        double nMax = nodeValues.Values.Max();
        if (Math.Abs(nMax - nMin) < 1e-10)
        {
            nMin -= 1e-6;
            nMax += 1e-6;
        }
        return (nMin, nMax);
    }

    /// <summary>Color scale matching the active contour fill style (nodal field for smooth bands).</summary>
    public static (double Min, double Max) ComputeDisplayColorRange(
        ResultsViewModel viewModel,
        string outputType,
        IReadOnlyList<ElementBase>? activeElements = null)
    {
        if (ResultsViewModel.IsJointOutput(outputType))
            return ComputeJointColorRange(viewModel, outputType);

        activeElements ??= GetContourElements(viewModel, outputType);
        var nodes = viewModel.FENodes;

        if (viewModel.UsesSmoothContourBands
            && nodes != null
            && nodes.Count > 0
            && activeElements.Count > 0)
        {
            var nodal = BuildNodalScalarField(viewModel, nodes, activeElements, outputType);
            if (nodal != null && nodal.Count > 0)
            {
                double min = nodal.Values.Min();
                double max = nodal.Values.Max();
                if (Math.Abs(max - min) < 1e-10)
                {
                    min -= 1e-6;
                    max += 1e-6;
                }
                return (min, max);
            }
        }

        return ComputeColorRange(viewModel, outputType, activeElements);
    }

    /// <summary>
    /// Manual legend range when both ends are finite and ordered; otherwise the existing auto scale
    /// (including the equal-value ±1e-6 pad).
    /// </summary>
    public static (double Min, double Max) ResolveManualOrAutoColorRange(
        ResultsViewModel viewModel,
        string outputType,
        double? manualMin,
        double? manualMax)
    {
        if (manualMin is { } min && manualMax is { } max
            && double.IsFinite(min) && double.IsFinite(max) && max > min)
            return (min, max);
        return ComputeDisplayColorRange(viewModel, outputType);
    }

    public static (double Min, double Max) ComputeJointColorRange(ResultsViewModel viewModel, string outputType)
    {
        var joints = viewModel.JointResults;
        if (joints == null || joints.Count == 0)
            return (0, 1);

        double min = viewModel.GetJointScalarValue(joints[0], outputType);
        double max = min;
        for (int i = 1; i < joints.Count; i++)
        {
            double v = viewModel.GetJointScalarValue(joints[i], outputType);
            if (v < min) min = v;
            if (v > max) max = v;
        }

        if (Math.Abs(max - min) < 1e-10)
        {
            min -= 1e-6;
            max += 1e-6;
        }

        return (min, max);
    }

    public static (double Min, double Max) ComputeGammaMaxColorRange(IReadOnlyList<double> elementValues)
    {
        if (elementValues.Count == 0)
            return (0, 1e-6);

        double max = Percentile(elementValues, GammaMaxPercentile);
        if (max <= 0)
            max = elementValues.Max();
        if (max <= 0)
            max = 1e-6;

        return (0, max);
    }

    public static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
            return 0;

        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 1)
            return sorted[0];

        double rank = percentile / 100.0 * (sorted.Count - 1);
        int lo = (int)Math.Floor(rank);
        int hi = (int)Math.Ceiling(rank);
        if (lo == hi)
            return sorted[lo];

        double t = rank - lo;
        return sorted[lo] * (1 - t) + sorted[hi] * t;
    }

    private readonly struct GammaMaxMaskContext
    {
        public double YMin { get; init; }
        public double BottomBand { get; init; }
    }

    private static GammaMaxMaskContext BuildGammaMaxMaskContext(
        IReadOnlyList<ElementBase>? elements,
        IReadOnlyList<Node> nodes,
        IReadOnlyDictionary<int, Node> nodeById)
    {
        double ymin = double.PositiveInfinity;
        foreach (var node in nodes)
            ymin = Math.Min(ymin, node.Y);

        if (!double.IsFinite(ymin))
            ymin = 0.0;

        return new GammaMaxMaskContext
        {
            YMin = ymin,
            BottomBand = EstimateTypicalElementHeight(elements, nodeById) * 1.05,
        };
    }

    private static bool ShouldMaskForGammaMax(
        ResultsViewModel viewModel,
        ElementBase element,
        IReadOnlyDictionary<int, Node> nodeById,
        GammaMaxMaskContext ctx)
    {
        if (viewModel.IsElementFullyFixed(element))
            return true;

        return GetElementCentroidY(element, nodeById) <= ctx.YMin + ctx.BottomBand;
    }

    private static Dictionary<int, Node> BuildNodeLookup(IReadOnlyList<Node> nodes)
    {
        var map = new Dictionary<int, Node>(nodes.Count);
        foreach (var node in nodes)
            map[node.Id] = node;
        return map;
    }

    private static double EstimateTypicalElementHeight(
        IReadOnlyList<ElementBase>? elements,
        IReadOnlyDictionary<int, Node> nodeById)
    {
        if (elements == null || elements.Count == 0)
            return 1.0;

        var heights = new List<double>(elements.Count);
        foreach (var element in elements)
        {
            if (element.NodeIds is not { Length: > 0 })
                continue;

            double minY = double.PositiveInfinity;
            double maxY = double.NegativeInfinity;
            foreach (var nodeId in element.NodeIds)
            {
                if (!nodeById.TryGetValue(nodeId, out var node))
                    continue;
                minY = Math.Min(minY, node.Y);
                maxY = Math.Max(maxY, node.Y);
            }

            if (double.IsFinite(minY) && double.IsFinite(maxY))
                heights.Add(maxY - minY);
        }

        if (heights.Count == 0)
            return 1.0;

        heights.Sort();
        return heights[heights.Count / 2];
    }

    private static double GetElementCentroidY(
        ElementBase element,
        IReadOnlyDictionary<int, Node> nodeById)
    {
        if (element.NodeIds is not { Length: > 0 })
            return 0.0;

        double sumY = 0.0;
        int count = 0;
        foreach (var nodeId in element.NodeIds)
        {
            if (!nodeById.TryGetValue(nodeId, out var node))
                continue;
            sumY += node.Y;
            count++;
        }

        return count > 0 ? sumY / count : 0.0;
    }

    /// <summary>
    /// Nodal scalar field for smooth contour interpolation. Nodal outputs use solver values;
    /// element outputs are averaged to adjacent nodes.
    /// </summary>
    public static Dictionary<int, double>? BuildNodalScalarField(
        ResultsViewModel viewModel,
        IReadOnlyList<Node> nodes,
        IReadOnlyList<ElementBase> activeElements,
        string outputType)
    {
        if (nodes.Count == 0)
            return null;

        var nodeValues = new Dictionary<int, double>();

        if (ResultsViewModel.IsNodalScalarOutput(outputType))
        {
            // Key by list index: Element.NodeIds / contour lookups use 0-based FENodes
            // indices, while Node.Id remains the solver's 1-based id.
            for (int i = 0; i < nodes.Count; i++)
                nodeValues[i] = viewModel.GetNodeScalarValue(nodes[i], outputType);
        }
        else if (ResultsViewModel.IsElementScalarOutput(outputType))
        {
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

        return nodeValues.Count > 0 ? nodeValues : null;
    }
}
