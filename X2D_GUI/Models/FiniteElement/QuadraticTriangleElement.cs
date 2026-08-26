namespace GeotechSlopeApp.Models.FiniteElement;

/// <summary>
/// 6-node quadratic triangle element (T6).
/// <see cref="ElementBase.NodeIds"/> layout:
///   [0] corner 1, [1] corner 2, [2] corner 3,
///   [3] midside on edge 1-2, [4] midside on edge 2-3, [5] midside on edge 3-1.
/// </summary>
public class QuadraticTriangleElement : ElementBase
{
}
