using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.FiniteElement;

namespace GeotechSlopeApp.Services.Wave3;

public static class FeMeshExportAdapter
{
    public static bool TryFromResults(
        IReadOnlyList<Node>? nodes,
        IReadOnlyList<ElementBase>? elements,
        out IReadOnlyList<Node> outNodes,
        out IReadOnlyList<ElementBase> outElements)
    {
        if (nodes is { Count: > 0 } && elements is { Count: > 0 })
        {
            outNodes = nodes;
            outElements = elements;
            return true;
        }

        outNodes = Array.Empty<Node>();
        outElements = Array.Empty<ElementBase>();
        return false;
    }

    public static bool TryFromMesh(
        IReadOnlyList<MeshDiscretizationNodeDisplay> nodes,
        IReadOnlyList<MeshInformationRow> rows,
        out IReadOnlyList<Node> outNodes,
        out IReadOnlyList<ElementBase> outElements)
    {
        if (nodes.Count == 0 || rows.Count == 0)
        {
            outNodes = Array.Empty<Node>();
            outElements = Array.Empty<ElementBase>();
            return false;
        }

        var feNodes = nodes.Select((n, i) => new Node
        {
            Id = n.NodeId > 0 ? n.NodeId : i + 1,
            X = n.Location.X,
            Y = n.Location.Y,
        }).ToList();

        var feElements = new List<ElementBase>(rows.Count);
        foreach (var r in rows)
        {
            var ids = new[] { r.Node1, r.Node2, r.Node3, r.Node4, r.Node5, r.Node6, r.Node7, r.Node8 }
                .Where(id => id > 0)
                .ToArray();
            if (ids.Length < 3) continue;
            feElements.Add(new LinearTriangleElement
            {
                Id = r.ElementId,
                NodeIds = ids,
            });
        }

        outNodes = feNodes;
        outElements = feElements;
        return feNodes.Count > 0 && feElements.Count > 0;
    }
}
