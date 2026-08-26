namespace GeotechSlopeApp.Models.FiniteElement;

/// <summary>
/// Node-index walk order for mesh wireframes. Q8/T6 store corners then edge midsides in
/// <see cref="ElementBase.NodeIds"/> (Smith emb_2d layout); perimeter order interleaves them.
/// </summary>
public static class FEMeshOutlineHelper
{
    /// <summary>Closed walk over <see cref="ElementBase.NodeIds"/> for wireframe drawing.</summary>
    public static IReadOnlyList<int> GetOutlineNodeIndices(
        ElementBase element,
        bool useFullQuadraticPerimeter,
        bool smithEmb2dQ8LocalOrder = false)
    {
        return element switch
        {
            QuadraticQuadrilateralElement when element.NodeIds.Length >= 8 && useFullQuadraticPerimeter && smithEmb2dQ8LocalOrder
                => new[] { 0, 1, 2, 3, 4, 5, 6, 7 },
            QuadraticQuadrilateralElement when element.NodeIds.Length >= 8 && useFullQuadraticPerimeter
                => new[] { 0, 4, 1, 5, 2, 6, 3, 7 },
            QuadraticQuadrilateralElement when element.NodeIds.Length >= 8 && smithEmb2dQ8LocalOrder
                => new[] { 0, 2, 4, 6 },
            QuadraticQuadrilateralElement when element.NodeIds.Length >= 4
                => new[] { 0, 1, 2, 3 },
            LinearQuadrilateralElement when element.NodeIds.Length >= 4
                => new[] { 0, 1, 2, 3 },
            QuadraticTriangleElement when element.NodeIds.Length >= 6 && useFullQuadraticPerimeter
                => new[] { 0, 3, 1, 4, 2, 5 },
            QuadraticTriangleElement when element.NodeIds.Length >= 3
                => new[] { 0, 1, 2 },
            LinearTriangleElement when element.NodeIds.Length >= 3
                => new[] { 0, 1, 2 },
            LinearRodElement when element.NodeIds.Length >= 2
                => new[] { 0, 1 },
            _ => Array.Empty<int>()
        };
    }

    /// <summary>
    /// Corner-only indices for filled contour polygons (Q8 Smith: N1,N3,N5,N7 at local 0,2,4,6).
    /// </summary>
    public static IReadOnlyList<int> GetContourCornerNodeIndices(
        ElementBase element,
        bool smithEmb2dQ8LocalOrder = false)
    {
        return element switch
        {
            QuadraticQuadrilateralElement when element.NodeIds.Length >= 8 && smithEmb2dQ8LocalOrder
                => new[] { 0, 2, 4, 6 },
            QuadraticQuadrilateralElement when element.NodeIds.Length >= 4
                => new[] { 0, 1, 2, 3 },
            LinearQuadrilateralElement when element.NodeIds.Length >= 4
                => new[] { 0, 1, 2, 3 },
            QuadraticTriangleElement when element.NodeIds.Length >= 3
                => new[] { 0, 1, 2 },
            LinearTriangleElement when element.NodeIds.Length >= 3
                => new[] { 0, 1, 2 },
            _ => Array.Empty<int>()
        };
    }
}
