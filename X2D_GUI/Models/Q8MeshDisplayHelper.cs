using System.Windows;
using System.Windows.Media;

namespace GeotechSlopeApp.Models;

/// <summary>
/// Q8 element outlines for mesh canvases. p64 / Smith <c>emb_2d_geom</c> stores local nodes
/// N1..N8 in perimeter order (corner, midside, corner, …). RS2D / JSON use serendipity order
/// (four corners then four midsides).
/// </summary>
public static class Q8MeshDisplayHelper
{
    /// <summary>Smith emb_2d_geom / explicit p64 .dat: walk local nodes 1..8 in file order.</summary>
    public static PointCollection BuildSmithEmb2dOutline(
        double n1x, double n1y, double n2x, double n2y, double n3x, double n3y, double n4x, double n4y,
        double n5x, double n5y, double n6x, double n6y, double n7x, double n7y, double n8x, double n8y)
    {
        return new PointCollection
        {
            new(n1x, n1y), new(n2x, n2y), new(n3x, n3y), new(n4x, n4y),
            new(n5x, n5y), new(n6x, n6y), new(n7x, n7y), new(n8x, n8y),
        };
    }

    public static PointCollection BuildSmithEmb2dOutline(MeshInformationRow row) =>
        BuildSmithEmb2dOutline(
            row.N1X, row.N1Y, row.N2X, row.N2Y, row.N3X, row.N3Y, row.N4X, row.N4Y,
            row.N5X, row.N5Y, row.N6X, row.N6Y, row.N7X, row.N7Y, row.N8X, row.N8Y);

    /// <summary>Serendipity order: N1, M12, N2, M23, N3, M34, N4, M41.</summary>
    public static PointCollection BuildSerendipityOutline(
        double n1x, double n1y, double n2x, double n2y, double n3x, double n3y, double n4x, double n4y,
        double n5x, double n5y, double n6x, double n6y, double n7x, double n7y, double n8x, double n8y)
    {
        return new PointCollection
        {
            new(n1x, n1y), new(n5x, n5y), new(n2x, n2y), new(n6x, n6y),
            new(n3x, n3y), new(n7x, n7y), new(n4x, n4y), new(n8x, n8y),
        };
    }

    public static PointCollection BuildSerendipityOutline(MeshInformationRow row) =>
        BuildSerendipityOutline(
            row.N1X, row.N1Y, row.N2X, row.N2Y, row.N3X, row.N3Y, row.N4X, row.N4Y,
            row.N5X, row.N5Y, row.N6X, row.N6Y, row.N7X, row.N7Y, row.N8X, row.N8Y);

    public static PointCollection BuildOutlineFromRow(MeshInformationRow row) =>
        row.SmithEmb2dLocalOrder ? BuildSmithEmb2dOutline(row) : BuildSerendipityOutline(row);

    /// <summary>
    /// Four corner nodes only — textbook / Smith Fig. 6.4 style mesh lines (straight quad edges).
    /// Smith: N1,N3,N5,N7; serendipity: N1..N4.
    /// </summary>
    public static PointCollection BuildCornerQuadOutline(
        double n1x, double n1y, double n2x, double n2y, double n3x, double n3y, double n4x, double n4y,
        double n5x, double n5y, double n6x, double n6y, double n7x, double n7y, double n8x, double n8y,
        bool smithEmb2dLocalOrder)
    {
        return smithEmb2dLocalOrder
            ? new PointCollection { new(n1x, n1y), new(n3x, n3y), new(n5x, n5y), new(n7x, n7y) }
            : new PointCollection { new(n1x, n1y), new(n2x, n2y), new(n3x, n3y), new(n4x, n4y) };
    }

    public static PointCollection BuildCornerQuadOutline(MeshInformationRow row) =>
        BuildCornerQuadOutline(
            row.N1X, row.N1Y, row.N2X, row.N2Y, row.N3X, row.N3Y, row.N4X, row.N4Y,
            row.N5X, row.N5Y, row.N6X, row.N6Y, row.N7X, row.N7Y, row.N8X, row.N8Y,
            row.SmithEmb2dLocalOrder);

    /// <summary>Element centroid from the four corner nodes (Smith: N1,N3,N5,N7; serendipity: N1..N4).</summary>
    public static (double cx, double cy) CornerCentroid(
        bool smithEmb2dLocalOrder,
        Point p1, Point p2, Point p3, Point p4, Point p5, Point p6, Point p7, Point p8)
    {
        if (smithEmb2dLocalOrder)
            return ((p1.X + p3.X + p5.X + p7.X) / 4.0, (p1.Y + p3.Y + p5.Y + p7.Y) / 4.0);
        return ((p1.X + p2.X + p3.X + p4.X) / 4.0, (p1.Y + p2.Y + p3.Y + p4.Y) / 4.0);
    }
}
