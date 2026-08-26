using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.ViewModels;

/// <summary>
/// Orchestrates the cross-view-model writes that turn a populated <see cref="TunnelWizardViewModel"/>
/// into a fully primed model: outer-domain axis range, the tunnel polygon (or circle), mesh
/// parameters + a fresh tunnel mesh, auto-detected boundary conditions, a field-stress preset, and
/// an Initial-Stress + Excavation stage pair with the polygon registered as the excavation zone.
/// <para>
/// Returns a <see cref="TunnelWizardApplyReport"/> describing each step's outcome so the dialog can
/// surface specific failures ("mesh produced 0 elements") rather than a generic error.
/// </para>
/// </summary>
public sealed class TunnelWizardApplier
{
    private readonly GeometryDefinitionViewModel _geometry;
    private readonly MeshViewModel _mesh;
    private readonly BoundaryConditionsViewModel _bcs;
    private readonly FieldStressViewModel _stress;
    private readonly StagedConstructionViewModel _staged;

    public TunnelWizardApplier(
        GeometryDefinitionViewModel geometry,
        MeshViewModel mesh,
        BoundaryConditionsViewModel bcs,
        FieldStressViewModel stress,
        StagedConstructionViewModel staged)
    {
        _geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        _mesh     = mesh     ?? throw new ArgumentNullException(nameof(mesh));
        _bcs      = bcs      ?? throw new ArgumentNullException(nameof(bcs));
        _stress   = stress   ?? throw new ArgumentNullException(nameof(stress));
        _staged   = staged   ?? throw new ArgumentNullException(nameof(staged));
    }

    public TunnelWizardApplyReport Apply(TunnelWizardViewModel state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        var report = new TunnelWizardApplyReport();

        // 1. Outer domain — frame all model canvases with nice outward-rounded axis limits.
        _geometry.FitAxisBoundsToWorldExtents(
            state.DomainXMin, state.DomainXMax, state.DomainYMin, state.DomainYMax);
        report.Steps.Add($"Outer domain set to x∈[{state.DomainXMin:G6}, {state.DomainXMax:G6}], y∈[{state.DomainYMin:G6}, {state.DomainYMax:G6}].");

        // 2. Tunnel shape — circles take the legacy path (concentric rings); other shapes go through
        // FilteredTunnelExcavationPolygons which the mesh routine consumes as polygonHoles.
        List<Point> axisPolygon = TunnelShapeBuilder.BuildPolygon(state.ShapeParams);
        if (axisPolygon.Count < 3)
        {
            report.Failures.Add("Generated tunnel polygon has fewer than 3 points (check shape parameters).");
            return report;
        }

        if (state.ShapeKind == TunnelShapeKind.Circle)
        {
            _geometry.AddTunnelCircle(state.CenterX, state.CenterY, state.Radius);
            report.Steps.Add($"Added circular tunnel (R = {state.Radius:G6} m) at ({state.CenterX:G6}, {state.CenterY:G6}).");
        }
        else
        {
            _geometry.AddTunnelPolygon(axisPolygon, state.ShapeKind);
            report.Steps.Add($"Added {state.ShapeKind} tunnel polygon ({axisPolygon.Count} vertices).");
        }

        // 3. Field-stress preset — mutate fields on the existing FieldStressSpec (Global is read-only accessor).
        ApplyStressPreset(state);
        report.Steps.Add($"Field-stress preset '{state.StressPreset}' applied (Mode = {_stress.Global.Mode}).");

        // 4. Mesh parameters — push the wizard's preset values as strings (MeshViewModel parses on demand).
        _mesh.FarFieldEdgeLength   = state.FarFieldEdgeLength.ToString("G6", CultureInfo.InvariantCulture);
        _mesh.NearTunnelEdgeLength = state.NearTunnelEdgeLength.ToString("G6", CultureInfo.InvariantCulture);
        _mesh.RingCount            = state.RingCount.ToString(CultureInfo.InvariantCulture);
        _mesh.RingGrowthFactor     = state.RingGrowthFactor.ToString("G6", CultureInfo.InvariantCulture);
        _mesh.FirstRingThickness   = state.FirstRingThickness.ToString("G6", CultureInfo.InvariantCulture);

        // The mesher needs at least 2 axis-distinct geometry vertices to fall back to a bbox outer
        // polygon when no surfaces exist. Seed the four outer-rectangle corners only if no existing
        // vertices reach the requested domain extent (so we don't trample a hand-built model).
        EnsureOuterRectangleVertices(state);

        // 5. Trigger mesh generation. Synchronous — MeshInformationRows are populated immediately.
        if (_mesh.GenerateTunnelMeshCommand?.CanExecute(null) == true)
        {
            _mesh.GenerateTunnelMeshCommand.Execute(null);
        }
        if (_mesh.MeshInformationRows.Count == 0)
        {
            report.Failures.Add($"Mesh generation produced 0 elements ({_mesh.TunnelMeshStatus}).");
            return report;
        }
        report.Steps.Add($"Generated tunnel mesh: {_mesh.MeshInformationRows.Count} elements, status: {_mesh.TunnelMeshStatus}");

        // 6. Auto-add boundary conditions to the four outer edges.
        int bcCount = AutoApplyBoundaryConditions(state);
        report.Steps.Add($"Applied boundary conditions to {bcCount} mesh node(s) on the outer rectangle.");

        // 7. Stages: ensure an Initial Stress stage, then add an Excavation stage with the tunnel
        // polygon registered as an ExcavationZone (this is what P610T3DatContentBuilder uses to mark
        // elements inactive in the .dat layer block — works for circles and polygons alike).
        EnsureInitialStage();
        AddExcavationStage(axisPolygon, state.ShapeKind);
        report.Steps.Add($"Added staged construction: Initial Stress + Excavate {state.ShapeKind}.");

        report.Success = true;
        return report;
    }

    // ── Stage helpers ───────────────────────────────────────────────────────

    private void EnsureInitialStage()
    {
        if (_staged.Stages.Count == 0)
        {
            _staged.AddStageCommand?.Execute(null);
        }
    }

    private void AddExcavationStage(IReadOnlyList<Point> axisPolygon, TunnelShapeKind kind)
    {
        _staged.AddStageCommand?.Execute(null);
        var stage = _staged.Stages.LastOrDefault();
        if (stage == null) return;

        stage.StageType = "Excavation";
        if (string.IsNullOrWhiteSpace(stage.StageName) || stage.StageName.StartsWith("Stage ", StringComparison.Ordinal))
        {
            stage.StageName = $"Excavate {kind}";
        }

        var canvasPoly = new PointCollection(axisPolygon.Count);
        var axisTuples = new List<(double X, double Y)>(axisPolygon.Count);
        double sumX = 0, sumY = 0;
        foreach (var p in axisPolygon)
        {
            canvasPoly.Add(_geometry.ConvertAxisToCanvas(p));
            axisTuples.Add((p.X, p.Y));
            sumX += p.X; sumY += p.Y;
        }
        double cx = sumX / axisPolygon.Count;
        double cy = sumY / axisPolygon.Count;

        stage.ExcavationZones.Add(new StageExcavationZone
        {
            // -1 marks "synthesised by the Tunnel Wizard, not tied to a Geometry surface".
            SurfaceNumber        = -1,
            AppliedInStageNumber = stage.StageNumber,
            CanvasPolygon        = canvasPoly,
            AxisPolygon          = axisTuples,
            CentroidX            = _geometry.ConvertAxisToCanvas(new Point(cx, cy)).X,
            CentroidY            = _geometry.ConvertAxisToCanvas(new Point(cx, cy)).Y,
            DisplayLabel         = $"Exc {kind}",
        });
    }

    // ── Stress-preset translation ──────────────────────────────────────────

    private void ApplyStressPreset(TunnelWizardViewModel state)
    {
        var spec = _stress.Global;
        switch (state.StressPreset)
        {
            case TunnelStressPreset.Hydrostatic:
                spec.Mode      = FieldStressMode.Constant;
                spec.Sigma1    = -Math.Abs(state.StressMagnitude);
                spec.Sigma3    = -Math.Abs(state.StressMagnitude);
                spec.SigmaZ    = -Math.Abs(state.StressMagnitude);
                spec.AngleDeg  = state.StressAngleDeg;
                spec.Kx        = 1.0;
                spec.Kz        = 1.0;
                break;

            case TunnelStressPreset.K0Lateral:
                {
                    double sigmaV = -Math.Abs(state.StressGamma * state.StressDepth);
                    double sigmaH = sigmaV * state.StressK;
                    spec.Mode      = FieldStressMode.Constant;
                    spec.Sigma1    = sigmaV; // major principal = vertical for K < 1
                    spec.Sigma3    = sigmaH;
                    spec.SigmaZ    = sigmaH;
                    spec.AngleDeg  = state.StressAngleDeg;
                    spec.Kx        = state.StressK;
                    spec.Kz        = state.StressK;
                }
                break;

            case TunnelStressPreset.Gravity:
                spec.Mode      = FieldStressMode.Gravity;
                spec.Kx        = state.StressK;
                spec.Kz        = state.StressK;
                spec.AutoSurface = true;
                break;

            case TunnelStressPreset.Direct:
                spec.Mode      = FieldStressMode.Direct;
                spec.DirectSxx = state.DirectSxx;
                spec.DirectSyy = state.DirectSyy;
                spec.DirectTxy = state.DirectTxy;
                break;
        }
    }

    // ── Outer-rectangle vertices ───────────────────────────────────────────

    private void EnsureOuterRectangleVertices(TunnelWizardViewModel state)
    {
        // If the user already has a closed surface that frames the wizard's tunnel, leave them alone:
        // the mesher will pick the largest-area polygon as the outer domain.
        if (_geometry.GeometrySurfaces.Count > 0) return;

        // Otherwise the mesher falls back to the bounding box of all GeometryVertexRows. Make sure
        // four corner vertices exist at the requested outer-domain extent so the bbox spans correctly.
        bool hasMin = _geometry.GeometryVertexRows.Any(v => Math.Abs(v.AxisX - state.DomainXMin) < 1e-6 && Math.Abs(v.AxisY - state.DomainYMin) < 1e-6);
        bool hasMax = _geometry.GeometryVertexRows.Any(v => Math.Abs(v.AxisX - state.DomainXMax) < 1e-6 && Math.Abs(v.AxisY - state.DomainYMax) < 1e-6);
        if (hasMin && hasMax) return;

        AddCornerVertex(state.DomainXMin, state.DomainYMin);
        AddCornerVertex(state.DomainXMax, state.DomainYMin);
        AddCornerVertex(state.DomainXMax, state.DomainYMax);
        AddCornerVertex(state.DomainXMin, state.DomainYMax);
    }

    private void AddCornerVertex(double x, double y)
    {
        // Direct insert into the row collection: the wizard is not building a connected polygon, so
        // we don't need to trigger split-segment / line-rebuild plumbing — we just need bbox extent.
        var display = new GeometryDefinitionViewModel.GeometryVertexDisplay
        {
            Index = _geometry.GeometryVertexRows.Count,
            AxisX = x,
            AxisY = y,
        };
        _geometry.GeometryVertexRows.Add(display);
    }

    // ── Auto BC: roller on top/bottom + sides, fix bottom corners ──────────

    private int AutoApplyBoundaryConditions(TunnelWizardViewModel state)
    {
        var meshNodes = new Dictionary<int, (double X, double Y)>();
        foreach (var r in _mesh.MeshInformationRows)
        {
            if (r.Node1 > 0) meshNodes[r.Node1] = (r.N1X, r.N1Y);
            if (r.Node2 > 0) meshNodes[r.Node2] = (r.N2X, r.N2Y);
            if (r.Node3 > 0) meshNodes[r.Node3] = (r.N3X, r.N3Y);
            if (r.Node4 > 0) meshNodes[r.Node4] = (r.N4X, r.N4Y);
            if (r.Node5 > 0) meshNodes[r.Node5] = (r.N5X, r.N5Y);
            if (r.Node6 > 0) meshNodes[r.Node6] = (r.N6X, r.N6Y);
            if (r.Node7 > 0) meshNodes[r.Node7] = (r.N7X, r.N7Y);
            if (r.Node8 > 0) meshNodes[r.Node8] = (r.N8X, r.N8Y);
        }
        if (meshNodes.Count == 0) return 0;

        // Tolerance scales with the bounding-box diagonal so dense and coarse meshes both work.
        double xmin = meshNodes.Values.Min(v => v.X);
        double xmax = meshNodes.Values.Max(v => v.X);
        double ymin = meshNodes.Values.Min(v => v.Y);
        double ymax = meshNodes.Values.Max(v => v.Y);
        double diag = Math.Sqrt((xmax - xmin) * (xmax - xmin) + (ymax - ymin) * (ymax - ymin));
        double tol  = Math.Max(diag * 5e-4, 1e-9);

        int added = 0;
        foreach (var kv in meshNodes)
        {
            int nodeId = kv.Key;
            var (x, y) = kv.Value;

            bool onLeft   = Math.Abs(x - xmin) < tol;
            bool onRight  = Math.Abs(x - xmax) < tol;
            bool onBottom = Math.Abs(y - ymin) < tol;
            bool onTop    = Math.Abs(y - ymax) < tol;
            if (!(onLeft || onRight || onBottom || onTop)) continue;

            // Bottom corners → fully fixed (anchors the model); other boundary nodes → roller normal to edge.
            string bcType;
            string boundary;
            if ((onBottom && onLeft) || (onBottom && onRight))
            {
                bcType = "Fixed"; boundary = "Bottom corner";
            }
            else if (onBottom)
            {
                bcType = "Roller Y"; boundary = "Bottom";
            }
            else if (onTop)
            {
                bcType = "Roller Y"; boundary = "Top";
            }
            else if (onLeft)
            {
                bcType = "Roller X"; boundary = "Left";
            }
            else
            {
                bcType = "Roller X"; boundary = "Right";
            }

            if (_bcs.BoundaryConditions.Any(r => r.NodeId == nodeId)) continue; // don't trample existing
            _bcs.BoundaryConditions.Add(new BoundaryConditionRow
            {
                NodeId = nodeId,
                BoundaryName = boundary,
                BCType = bcType,
                AnalysisCategory = BoundaryConditionAnalysisCategories.Structural,
                DegreeOfFreedom = 0,
                Value = "0",
                Description = $"Tunnel Wizard: {bcType}",
            });
            added++;
        }
        return added;
    }
}

/// <summary>Per-step diagnostics from a wizard apply run.</summary>
public sealed class TunnelWizardApplyReport
{
    public bool Success { get; set; }
    public List<string> Steps { get; } = new();
    public List<string> Failures { get; } = new();

    public string SummaryText => Success
        ? "Tunnel Wizard: applied successfully.\n  • " + string.Join("\n  • ", Steps)
        : "Tunnel Wizard: apply failed.\n  • " + string.Join("\n  • ", Failures.Concat(Steps));
}
