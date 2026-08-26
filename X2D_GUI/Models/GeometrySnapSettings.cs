using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models;

/// <summary>
/// Drawing aids for the slope canvas: where a click is allowed to land, and how a line being drawn
/// is constrained. Saved with the project so a section keeps the grid it was drawn on.
/// </summary>
public sealed class GeometrySnapSettings : INotifyPropertyChanged
{
    private bool _snapEnabled = true;
    private bool _gridSnap;
    private double _gridSnapSpacing = 1.0;
    private bool _vertexSnap = true;
    private bool _midpointSnap = true;
    private bool _intersectionSnap = true;
    private bool _edgeSnap = true;
    private double _snapTolerancePixels = 12.0;
    private bool _orthoEnabled;
    private double _orthoAngleStepDegrees = 90.0;

    /// <summary>Master switch. With this off, a click lands exactly where the cursor is.</summary>
    public bool SnapEnabled
    {
        get => _snapEnabled;
        set => Set(ref _snapEnabled, value);
    }

    /// <summary>Round a click to the nearest point on the snap grid.</summary>
    public bool GridSnap
    {
        get => _gridSnap;
        set => Set(ref _gridSnap, value);
    }

    /// <summary>
    /// Snap grid spacing in model units. Independent of the displayed grid, which is chosen for
    /// legibility at the current zoom rather than for drawing.
    /// </summary>
    public double GridSnapSpacing
    {
        get => _gridSnapSpacing;
        set => Set(ref _gridSnapSpacing, value > 0 ? value : _gridSnapSpacing);
    }

    /// <summary>Snap to an existing vertex.</summary>
    public bool VertexSnap
    {
        get => _vertexSnap;
        set => Set(ref _vertexSnap, value);
    }

    /// <summary>Snap to the midpoint of an existing segment.</summary>
    public bool MidpointSnap
    {
        get => _midpointSnap;
        set => Set(ref _midpointSnap, value);
    }

    /// <summary>Snap to where two segments cross.</summary>
    public bool IntersectionSnap
    {
        get => _intersectionSnap;
        set => Set(ref _intersectionSnap, value);
    }

    /// <summary>Snap onto the nearest point along a segment.</summary>
    public bool EdgeSnap
    {
        get => _edgeSnap;
        set => Set(ref _edgeSnap, value);
    }

    /// <summary>
    /// How close the cursor must be for a snap to take, in screen pixels, so the feel of snapping
    /// does not change as the user zooms.
    /// </summary>
    public double SnapTolerancePixels
    {
        get => _snapTolerancePixels;
        set => Set(ref _snapTolerancePixels, Math.Clamp(value, 2.0, 60.0));
    }

    /// <summary>Constrain a line being drawn to fixed angles from its start point.</summary>
    public bool OrthoEnabled
    {
        get => _orthoEnabled;
        set => Set(ref _orthoEnabled, value);
    }

    /// <summary>Angle increment for the ortho constraint. 90° is horizontal and vertical only.</summary>
    public double OrthoAngleStepDegrees
    {
        get => _orthoAngleStepDegrees;
        set => Set(ref _orthoAngleStepDegrees, Math.Clamp(value, 1.0, 90.0));
    }

    /// <summary>True when at least one object snap could fire.</summary>
    public bool AnyObjectSnap => VertexSnap || MidpointSnap || IntersectionSnap || EdgeSnap;

    public GeometrySnapSettings Clone() => (GeometrySnapSettings)MemberwiseClone();

    /// <summary>Copies every field in place so existing bindings survive a project load.</summary>
    public void CopyFrom(GeometrySnapSettings other)
    {
        if (other == null) return;

        SnapEnabled = other.SnapEnabled;
        GridSnap = other.GridSnap;
        GridSnapSpacing = other.GridSnapSpacing;
        VertexSnap = other.VertexSnap;
        MidpointSnap = other.MidpointSnap;
        IntersectionSnap = other.IntersectionSnap;
        EdgeSnap = other.EdgeSnap;
        SnapTolerancePixels = other.SnapTolerancePixels;
        OrthoEnabled = other.OrthoEnabled;
        OrthoAngleStepDegrees = other.OrthoAngleStepDegrees;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AnyObjectSnap)));
    }
}
