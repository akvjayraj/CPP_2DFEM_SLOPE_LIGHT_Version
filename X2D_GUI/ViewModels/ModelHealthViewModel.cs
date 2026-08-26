using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.ViewModels;

/// <summary>
/// Read-only model-quality coach. Subscribes to the editing view-models
/// (mesh, boundary conditions, load conditions, geometry, staged construction)
/// and produces a flat, severity-ordered list of <see cref="ModelHealthIssue"/>
/// describing the most common ways an analysis fails before it ever reaches the
/// solver: no mesh, missing fixed support, no field stress, far field too close,
/// excavation circle without an activating stage, etc.
///
/// The class deliberately does NOT mutate any of the source view-models -- it is
/// purely advisory so adding a new check cannot regress an existing flow. Issues
/// are recomputed on demand via <see cref="RefreshCommand"/> and after any of
/// the watched collections changes (debounced through CommandManager).
/// </summary>
public sealed class ModelHealthViewModel : INotifyPropertyChanged
{
    private readonly MeshViewModel _mesh;
    private readonly BoundaryConditionsViewModel _bcs;
    private readonly LoadConditionsViewModel _loads;
    private readonly GeometryDefinitionViewModel _geometry;
    private readonly StagedConstructionViewModel _staged;

    public ModelHealthViewModel(
        MeshViewModel mesh,
        BoundaryConditionsViewModel bcs,
        LoadConditionsViewModel loads,
        GeometryDefinitionViewModel geometry,
        StagedConstructionViewModel staged)
    {
        _mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
        _bcs = bcs ?? throw new ArgumentNullException(nameof(bcs));
        _loads = loads ?? throw new ArgumentNullException(nameof(loads));
        _geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        _staged = staged ?? throw new ArgumentNullException(nameof(staged));

        Issues = new ObservableCollection<ModelHealthIssue>();
        Issues.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IssueCount));
            OnPropertyChanged(nameof(CriticalCount));
            OnPropertyChanged(nameof(WarningCount));
            OnPropertyChanged(nameof(InfoCount));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(HasIssues));
            OnPropertyChanged(nameof(IsHealthy));
        };

        RefreshCommand = new RelayCommand(Refresh);

        _mesh.MeshElements.CollectionChanged += OnSourceCollectionChanged;
        _mesh.PropertyChanged += OnSourcePropertyChanged;
        _bcs.BoundaryConditions.CollectionChanged += OnSourceCollectionChanged;
        _geometry.FilteredTunnelExcavationCircles.CollectionChanged += OnSourceCollectionChanged;
        _staged.Stages.CollectionChanged += OnSourceCollectionChanged;

        // First-paint snapshot. Failures here are non-fatal -- the user can
        // always click Refresh to retry once the application finishes seeding.
        Refresh();
    }

    public ObservableCollection<ModelHealthIssue> Issues { get; }

    public int IssueCount => Issues.Count;
    public int CriticalCount => Issues.Count(i => i.Severity == ModelHealthSeverity.Critical);
    public int WarningCount  => Issues.Count(i => i.Severity == ModelHealthSeverity.Warning);
    public int InfoCount     => Issues.Count(i => i.Severity == ModelHealthSeverity.Info);
    public bool HasIssues    => Issues.Count > 0;
    public bool IsHealthy    => CriticalCount == 0 && WarningCount == 0;

    public string SummaryText
    {
        get
        {
            if (IsHealthy && IssueCount == 0)
                return "Model looks healthy.";
            if (IsHealthy)
                return $"{InfoCount} note(s).";
            return $"{CriticalCount} critical · {WarningCount} warning · {InfoCount} info";
        }
    }

    public ICommand RefreshCommand { get; }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // We only care about the few "structural" mesh state properties that
        // can flip a check on or off (discretization done, mesh cleared).
        if (e.PropertyName is nameof(MeshViewModel.IsDiscretized)
            or "ActiveElementCount"
            or nameof(MeshViewModel.MeshElements))
        {
            Refresh();
        }
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Refresh();

    public void Refresh()
    {
        Issues.Clear();
        CheckMesh();
        CheckBoundaryConditions();
        CheckFieldStress();
        CheckTunnelGeometry();
        CheckStagingConsistency();
    }

    // ── Individual checks ────────────────────────────────────────────────────
    //
    // Each check appends 0..N ModelHealthIssue rows. Prefer one issue per
    // distinct user-facing problem; do NOT collapse unrelated symptoms into a
    // single message because the FixHint becomes meaningless when it has to
    // describe two different remediation paths.

    private void CheckMesh()
    {
        int total = _mesh.MeshElements.Count;
        if (total == 0)
        {
            Issues.Add(new ModelHealthIssue
            {
                Severity = ModelHealthSeverity.Critical,
                Title = "No mesh generated",
                Detail = "The model has no elements. The solver will reject any .dat with nels = 0.",
                FixHint = "Open the Mesh tab and click Discretize, then Generate Mesh."
            });
            return;
        }

        int active = _mesh.ActiveElementCount;
        if (active == 0)
        {
            Issues.Add(new ModelHealthIssue
            {
                Severity = ModelHealthSeverity.Critical,
                Title = "All mesh elements deactivated",
                Detail = $"All {total} elements are marked inactive (excavated). The solver will see an empty domain.",
                FixHint = "Reset staged excavation in the Staged Construction tab, or restore at least one stage."
            });
        }
        else if (active < 100)
        {
            Issues.Add(new ModelHealthIssue
            {
                Severity = ModelHealthSeverity.Info,
                Title = $"Mesh has only {active} active elements",
                Detail = "Stress concentrations (e.g. tunnel walls) are typically under-resolved on coarse meshes.",
                FixHint = "Refine the mesh in the Mesh tab; tunnel-quality meshes usually need 1000+ elements."
            });
        }

        // Mixed element types is informative -- the elastic solver only handles
        // one element type at a time, so this is a pre-flight catch for
        // accidental mixes during interactive editing.
        bool hasTri  = _mesh.MeshElements.Any(e => !e.IsQuadrilateral && !e.IsQuadraticTriangle);
        bool hasTri6 = _mesh.MeshElements.Any(e => e.IsQuadraticTriangle);
        bool hasQ4   = _mesh.MeshElements.Any(e => e.IsQuadrilateral && !e.IsQuadraticQuad);
        bool hasQ8   = _mesh.MeshElements.Any(e => e.IsQuadraticQuad);
        int kinds = (hasTri ? 1 : 0) + (hasTri6 ? 1 : 0) + (hasQ4 ? 1 : 0) + (hasQ8 ? 1 : 0);
        if (kinds > 1)
        {
            Issues.Add(new ModelHealthIssue
            {
                Severity = ModelHealthSeverity.Warning,
                Title = "Mixed element types detected",
                Detail = "The current solver expects a single element family per .dat file (e.g. all T3 or all Q4).",
                FixHint = "Re-mesh with a single element type from the Mesh tab."
            });
        }
    }

    private void CheckBoundaryConditions()
    {
        int total = _bcs.BoundaryConditions.Count;
        if (total == 0)
        {
            Issues.Add(new ModelHealthIssue
            {
                Severity = ModelHealthSeverity.Critical,
                Title = "No boundary conditions defined",
                Detail = "Without supports the global stiffness matrix is singular and the solver will fail.",
                FixHint = "Open Boundary Conditions and add at least one Fixed or Roller support."
            });
            return;
        }

        bool anyFixed   = _bcs.BoundaryConditions.Any(b => Equals(b.BCType, "Fixed"));
        bool anyRollerX = _bcs.BoundaryConditions.Any(b => Equals(b.BCType, "Roller X"));
        bool anyRollerY = _bcs.BoundaryConditions.Any(b => Equals(b.BCType, "Roller Y"));

        // Rigid-body modes: need at least one node fully fixed, OR roller
        // supports that pin both X and Y somewhere on the boundary.
        if (!anyFixed && !(anyRollerX && anyRollerY))
        {
            Issues.Add(new ModelHealthIssue
            {
                Severity = ModelHealthSeverity.Critical,
                Title = "Model is not fully restrained",
                Detail = "Without a fixed node (or rollers in both X and Y) the model has rigid body motion.",
                FixHint = "Add a Fixed support, or add roller supports along both side and bottom edges."
            });
        }
    }

    private static bool Equals(string a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void CheckFieldStress()
    {
        var fs = _loads?.FieldStress;
        if (fs == null) return;
        var spec = fs.Global;
        if (spec == null) return;

        bool zeroDirect = spec.Mode == FieldStressMode.Direct
            && spec.DirectSxx == 0 && spec.DirectSyy == 0 && spec.DirectTxy == 0;
        bool zeroConstant = spec.Mode == FieldStressMode.Constant
            && spec.Sigma1 == 0 && spec.Sigma3 == 0 && spec.SigmaZ == 0;
        // Traction mode is configured via the explicit n_traction_groups list. It's "zero"
        // only when no groups exist OR every group's stress tensor is identically zero.
        bool zeroTraction = spec.Mode == FieldStressMode.Traction
            && (fs.TractionGroups.Count == 0
                || fs.TractionGroups.All(g => g.Sxx == 0 && g.Syy == 0 && g.Txy == 0));
        if (zeroDirect || zeroConstant || zeroTraction)
        {
            Issues.Add(new ModelHealthIssue
            {
                Severity = ModelHealthSeverity.Warning,
                Title = "Field stress is zero",
                Detail = "All in-situ stress components are 0 -- the elastic run will produce zero displacements unless other loads are applied.",
                FixHint = "Set σv (and K0 for hydrostatic) on the Load Conditions tab, or switch to Direct tensor / Traction and enter values."
            });
        }
    }

    private void CheckTunnelGeometry()
    {
        var circles = _geometry.FilteredTunnelExcavationCircles;
        if (circles.Count == 0) return;

        // Far-field distance: max(domain extent) / max(tunnel radius). Below
        // ~5 the boundary tractions still feel the hole, so Kirsch / Salencon
        // verification will under-report stresses by several percent.
        double maxRadius = circles.Max(c => Math.Abs(c.Radius));
        if (maxRadius <= 0) return;

        double xMin = _geometry.AxisXMin, xMax = _geometry.AxisXMax;
        double yMin = _geometry.AxisYMin, yMax = _geometry.AxisYMax;
        double extent = Math.Max(xMax - xMin, yMax - yMin);
        if (extent <= 0) return;

        double ratio = extent / (2.0 * maxRadius);
        if (ratio < 5.0)
        {
            Issues.Add(new ModelHealthIssue
            {
                Severity = ModelHealthSeverity.Warning,
                Title = $"Far field too close (≈ {ratio:F1} × diameter)",
                Detail = "Domain extent is less than 5 tunnel diameters. Boundary effects will contaminate stresses near the hole.",
                FixHint = "Extend the outer boundary in the Geometry tab to at least 10 tunnel radii on each side."
            });
        }
        else if (ratio < 10.0)
        {
            Issues.Add(new ModelHealthIssue
            {
                Severity = ModelHealthSeverity.Info,
                Title = $"Domain extent is {ratio:F1} × tunnel diameter",
                Detail = "5–10 diameters is acceptable for elastic analyses; tight plastic zones may still need more.",
                FixHint = ""
            });
        }
    }

    private void CheckStagingConsistency()
    {
        var circles = _geometry.FilteredTunnelExcavationCircles;
        if (circles.Count == 0) return;

        bool stagedEnabled = _staged.Stages.Count > 0;
        if (!stagedEnabled)
        {
            Issues.Add(new ModelHealthIssue
            {
                Severity = ModelHealthSeverity.Info,
                Title = "Tunnel circle defined but no excavation stage",
                Detail = "The circle is drawn but staged construction has no stages -- the tunnel will not be excavated in the analysis.",
                FixHint = "Open Staged Construction and add an Excavate stage referencing the tunnel circle."
            });
            return;
        }

        bool anyActivated = circles.Any(c => c.StageActivation.HasValue);
        if (!anyActivated)
        {
            Issues.Add(new ModelHealthIssue
            {
                Severity = ModelHealthSeverity.Warning,
                Title = "Tunnel circle is not assigned to any stage",
                Detail = "The geometry has tunnel circles but none of them activate in a construction stage.",
                FixHint = "In Staged Construction, attach the circle to an Excavate stage."
            });
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
