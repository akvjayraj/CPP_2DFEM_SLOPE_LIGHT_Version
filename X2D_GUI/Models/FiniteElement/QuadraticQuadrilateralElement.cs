namespace GeotechSlopeApp.Models.FiniteElement;

/// <summary>
/// 8-node serendipity quadratic quadrilateral element (Q8).
/// <see cref="ElementBase.NodeIds"/> layout:
///   [0] corner 1, [1] corner 2, [2] corner 3, [3] corner 4,
///   [4] midside on edge 1-2, [5] midside on edge 2-3,
///   [6] midside on edge 3-4, [7] midside on edge 4-1.
/// </summary>
public class QuadraticQuadrilateralElement : ElementBase
{
}
