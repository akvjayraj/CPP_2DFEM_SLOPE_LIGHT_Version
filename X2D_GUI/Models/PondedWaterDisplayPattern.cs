namespace GeotechSlopeApp.Models;

/// <summary>Canvas-only fill style for the ponded free-water region.</summary>
public enum PondedWaterDisplayPattern
{
    /// <summary>Vertical depth wash (transparent at water surface → denser at ground).</summary>
    DepthGradient = 0,
    /// <summary>Uniform translucent teal fill.</summary>
    Solid = 1,
    /// <summary>Diagonal hatch (CAD-style water hatch).</summary>
    DiagonalHatch = 2,
    /// <summary>Cross hatch.</summary>
    CrossHatch = 3,
    /// <summary>Horizontal wave / ripple lines.</summary>
    Waves = 4,
    /// <summary>Stipple / dotted fill.</summary>
    Dots = 5,
}
