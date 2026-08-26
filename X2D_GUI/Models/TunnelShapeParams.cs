using System.Windows;

namespace GeotechSlopeApp.Models;

/// <summary>
/// Tunnel cross-section family supported by the Tunnel Wizard.
/// Phase 1 covers the four standard shapes; <see cref="Polyline"/> is reserved for Phase 2 (DXF / CSV import).
/// </summary>
public enum TunnelShapeKind
{
    Circle,
    Horseshoe,
    Rectangular,
    NatmArch,
    Polyline,
}

/// <summary>
/// Parametric description of a single tunnel cross-section. The Tunnel Wizard hands one of these
/// to <see cref="GeotechSlopeApp.Backend.TunnelShapeBuilder"/> which expands it into a closed,
/// counter-clockwise polygon in axis (model) space.
/// <para>
/// All distances are in axis units (typically metres). Only the fields relevant to <see cref="Kind"/>
/// are honoured — the rest are ignored. <see cref="CenterX"/>/<see cref="CenterY"/> sit at the
/// centroid of the bounding box of the resulting polygon.
/// </para>
/// </summary>
public sealed class TunnelShapeParams
{
    public TunnelShapeKind Kind { get; set; } = TunnelShapeKind.Circle;

    /// <summary>Tunnel centre in axis space (centre of the bounding box of the generated polygon).</summary>
    public double CenterX { get; set; }
    public double CenterY { get; set; }

    /// <summary>Number of segments used to discretize curved portions (whole-circle, arches). Min 8.</summary>
    public int CurveSegments { get; set; } = 48;

    // ── Circle ────────────────────────────────────────────────────────────────
    /// <summary>Outer radius of a circular tunnel.</summary>
    public double Radius { get; set; } = 5.0;

    // ── Rectangular (sharp corners) ───────────────────────────────────────────
    /// <summary>Total width of a rectangular box section.</summary>
    public double Width { get; set; } = 8.0;
    /// <summary>Total height of a rectangular box section.</summary>
    public double Height { get; set; } = 6.0;

    // ── Horseshoe (semi-circular crown + straight walls + flat invert) ────────
    /// <summary>Span (full width at springline) of a horseshoe section.</summary>
    public double HorseshoeSpan { get; set; } = 10.0;
    /// <summary>Total height (springline to invert plus crown radius) of a horseshoe section.</summary>
    public double HorseshoeHeight { get; set; } = 8.0;

    // ── NATM 3-radius arch (crown radius + side radius + invert radius) ───────
    /// <summary>Crown (top arch) radius.</summary>
    public double NatmCrownRadius { get; set; } = 5.0;
    /// <summary>Side (haunch) radius — controls how the side walls flare.</summary>
    public double NatmSideRadius { get; set; } = 8.0;
    /// <summary>Invert (bottom arch) radius. Set equal to span/2 for a flat-on-rebar invert.</summary>
    public double NatmInvertRadius { get; set; } = 12.0;
    /// <summary>Span at the springline.</summary>
    public double NatmSpan { get; set; } = 10.0;
    /// <summary>Height from invert apex to springline.</summary>
    public double NatmHeight { get; set; } = 7.0;
}
