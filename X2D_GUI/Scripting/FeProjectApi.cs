using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Persistence;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Scripting;

/// <summary>
/// Thin STA-safe facade for notebook-style Python (JSON-RPC) and C# tests.
/// Does not expose ViewModels.
/// </summary>
public sealed class FeProjectApi
{
    private readonly MainWindowViewModel _vm;

    public FeProjectApi(MainWindowViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
    }

    public void Clear() => OnUi(() => XfemProjectPersistence.ApplyNewBlankProject(_vm));

    public void SetTitle(string name) => OnUi(() =>
        _vm.ProjectSettingsVM.Project.ProjectName = name ?? "");

    public void SetSsr(bool on) => OnUi(() =>
    {
        var mat = _vm.MaterialPropertiesVM;
        mat.SelectedAnalysisCategory = MaterialPropertiesViewModel.SrfAnalysisCategory;
        if (on)
        {
            mat.SelectedFailureCriterion = FailureCriterion.MohrCoulomb;
            mat.SelectedMaterialType = MaterialType.Isotropic;
        }

        _vm.ProjectSettingsVM.SetCategoryOn(MaterialPropertiesViewModel.SrfAnalysisCategory, on);
        var node = _vm.ProjectSettingsVM.AnalysisTree.FirstOrDefault(c =>
            string.Equals(c.Name, MaterialPropertiesViewModel.SrfAnalysisCategory, StringComparison.Ordinal));
        if (node != null)
            _vm.ProjectSettingsVM.SelectedAnalysisCategoryNode = node;
    });

    public bool IsSsrOn() => OnUi(() =>
        _vm.ProjectSettingsVM.IsCategoryOn(MaterialPropertiesViewModel.SrfAnalysisCategory));

    public void SetOutline(IReadOnlyList<(double X, double Y)> points) =>
        OnUi(() => _vm.GeometryDefinitionVM.SetOutlineAxis(points));

    public IReadOnlyList<(double X, double Y)> GetOutline() => OnUi(() =>
        (IReadOnlyList<(double X, double Y)>)_vm.GeometryDefinitionVM.GeometryVertexRows
            .Select(v => (v.AxisX, v.AxisY))
            .ToList());

    public void AddLayer(double elevation) => OnUi(() =>
        _vm.GeometryDefinitionVM.ApplyInferredMaterialLayerCuts(new[] { elevation }));

    public int AddMaterial(string name, double cKpa, double phiDeg, double eKpa, double nu, double gamma) =>
        OnUi(() =>
        {
            var mat = _vm.MaterialPropertiesVM;
            mat.SelectedAnalysisCategory = MaterialPropertiesViewModel.SrfAnalysisCategory;
            mat.SelectedFailureCriterion = FailureCriterion.MohrCoulomb;
            mat.SelectedMaterialType = MaterialType.Isotropic;
            var id = mat.Materials.Count == 0 ? 1 : mat.Materials.Max(m => m.Id) + 1;
            mat.Materials.Add(new MaterialModel
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(name) ? $"Material {id}" : name,
                FailureCriterion = FailureCriterion.MohrCoulomb,
                MaterialType = MaterialType.Isotropic,
                Cohesion = cKpa,
                FrictionAngle = phiDeg,
                DilationAngle = 0,
                YoungsModulus = eKpa,
                PoissonsRatio = nu,
                UnitWeight = gamma,
                Colour = Color.FromRgb(0xB4, 0xD2, 0xA0)
            });
            _vm.GeometryDefinitionVM.SetLayerMaterialAssignmentMaterialId(1, id);
            return id;
        });

    public int MaterialCount => OnUi(() => _vm.MaterialPropertiesVM.Materials.Count);

    public string GenerateMesh(string element = "Q4", double? size = null) => OnUi(() =>
    {
        var mesh = _vm.MeshVM;
        mesh.Rs2dElementType = element;
        if (size is > 0)
            mesh.GlobalMeshSize = size.Value.ToString(CultureInfo.InvariantCulture);
        var status = mesh.RunRs2dDiscretize();
        if (!mesh.Rs2dDiscretized)
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(status) ? "Discretize failed." : status);
        if (mesh.Rs2dMeshCommand.CanExecute(null))
            mesh.Rs2dMeshCommand.Execute(null);
        _vm.ScriptVM.TrySyncDiscretizedMeshToScriptDat(ignoreLoadedDeckPreserve: true);
        return mesh.SurfaceMeshStatus ?? "Mesh generated.";
    });

    public void ClearBoundaryConditions() => OnUi(() =>
    {
        if (_vm.BoundaryConditionsVM.ClearBoundaryConditionsCommand.CanExecute(null))
            _vm.BoundaryConditionsVM.ClearBoundaryConditionsCommand.Execute(null);
    });

    public string ApplySlopeBoundaryDefaults() => OnUi(() =>
    {
        var geom = _vm.GeometryDefinitionVM;
        var mesh = _vm.MeshVM;
        var bc = _vm.BoundaryConditionsVM;
        if (geom.GeometryVertexRows.Count < 3)
            throw new InvalidOperationException(
                "Set a geometry outline before applying slope boundary conditions.");
        if (mesh.MeshInformationRows.Count == 0)
            throw new InvalidOperationException(
                "Generate a mesh before project.bcs.apply_slope_defaults().");

        if (bc.ClearBoundaryConditionsCommand.CanExecute(null))
            bc.ClearBoundaryConditionsCommand.Execute(null);
        if (bc.ImportOuterBoundariesCommand.CanExecute(null))
            bc.ImportOuterBoundariesCommand.Execute(null);

        ClassifySlopeEdges(geom, mesh, bc, out var left, out var right, out var baseNodes);
        if (left.Count == 0 && right.Count == 0 && baseNodes.Count == 0)
            throw new InvalidOperationException(
                "Import Boundaries found no mesh nodes on the left, right, or base. Generate a mesh first.");

        var corners = new HashSet<int>(left);
        corners.IntersectWith(baseNodes);
        var rightBase = new HashSet<int>(right);
        rightBase.IntersectWith(baseNodes);
        foreach (var id in rightBase)
            corners.Add(id);

        ApplyBcRows(bc, "Roller X", left.Except(corners), "left");
        ApplyBcRows(bc, "Roller X", right.Except(corners), "right");
        ApplyBcRows(bc, "Roller Y", baseNodes.Except(corners), "base");
        ApplyBcRows(bc, "Fixed", corners, "corners");
        return $"Applied slope BCs: {left.Count} left, {right.Count} right, {baseNodes.Count} base, {corners.Count} corner node(s).";
    });

    public string RunFe(TimeSpan? timeout = null)
    {
        OnUi(() =>
        {
            if (!_vm.ProjectSettingsVM.IsActiveAnalysisCategoryOn)
                throw new InvalidOperationException(
                    "Analysis category is OFF. Call project.fe.set_ssr(True) before project.fe.run().");
            if (_vm.FEAnalysisVM.RunAnalysisCommand.CanExecute(null))
                _vm.FEAnalysisVM.RunAnalysisCommand.Execute(null);
        });

        var limit = timeout ?? TimeSpan.FromMinutes(10);
        var started = DateTime.UtcNow;
        var sawSolving = false;
        while (DateTime.UtcNow - started < limit)
        {
            var solving = OnUi(() => _vm.FEAnalysisVM.IsSolving);
            if (solving)
                sawSolving = true;
            else if (sawSolving || DateTime.UtcNow - started > TimeSpan.FromSeconds(2))
                break;
            Thread.Sleep(100);
        }

        return OnUi(() => _vm.FEAnalysisVM.StatusMessage ?? "");
    }

    public double? Srf() => OnUi(() => _vm.ResultsVM.EstimatedSrfFoS);

    public void Save(string path) => OnUi(() =>
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Save path is required.");
        XfemProjectPersistence.Save(_vm, path);
    });

    public void Load(string path) => OnUi(() =>
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Load path is required.");
        XfemProjectPersistence.Load(_vm, path);
    });

    private static void ClassifySlopeEdges(
        GeometryDefinitionViewModel geom,
        MeshViewModel mesh,
        BoundaryConditionsViewModel bc,
        out HashSet<int> left,
        out HashSet<int> right,
        out HashSet<int> baseNodes)
    {
        left = new HashSet<int>();
        right = new HashSet<int>();
        baseNodes = new HashSet<int>();

        var verts = geom.GeometryVertexRows;
        if (verts.Count == 0)
            return;

        double xmin = verts.Min(v => v.AxisX);
        double xmax = verts.Max(v => v.AxisX);
        double ymin = verts.Min(v => v.AxisY);
        double span = Math.Max(xmax - xmin, verts.Max(v => v.AxisY) - ymin);
        double tol = Math.Max(1e-6, span * 0.02);

        var coords = new Dictionary<int, (double X, double Y)>();
        foreach (var row in mesh.MeshInformationRows)
        {
            AddMeshNode(coords, row.Node1, row.N1X, row.N1Y);
            AddMeshNode(coords, row.Node2, row.N2X, row.N2Y);
            AddMeshNode(coords, row.Node3, row.N3X, row.N3Y);
            AddMeshNode(coords, row.Node4, row.N4X, row.N4Y);
            AddMeshNode(coords, row.Node5, row.N5X, row.N5Y);
            AddMeshNode(coords, row.Node6, row.N6X, row.N6Y);
            AddMeshNode(coords, row.Node7, row.N7X, row.N7Y);
            AddMeshNode(coords, row.Node8, row.N8X, row.N8Y);
        }

        foreach (var imported in bc.AvailableOuterBoundaries)
        {
            foreach (var id in imported.NodeIds)
            {
                if (!coords.TryGetValue(id, out var p))
                    continue;
                if (Math.Abs(p.X - xmin) <= tol) left.Add(id);
                if (Math.Abs(p.X - xmax) <= tol) right.Add(id);
                if (Math.Abs(p.Y - ymin) <= tol) baseNodes.Add(id);
            }
        }
    }

    private static void AddMeshNode(Dictionary<int, (double X, double Y)> map, int id, double x, double y)
    {
        if (id > 0)
            map[id] = (x, y);
    }

    private static void ApplyBcRows(
        BoundaryConditionsViewModel bc, string bcType, IEnumerable<int> nodeIds, string boundaryName)
    {
        var analysisCategory = BoundaryConditionAnalysisCategories.Resolve(bcType);
        foreach (var nodeId in nodeIds)
        {
            if (nodeId <= 0) continue;
            var existing = bc.BoundaryConditions.FirstOrDefault(r =>
                r.NodeId == nodeId
                && r.BoundaryName == boundaryName
                && string.Equals(r.AnalysisCategory, analysisCategory, StringComparison.Ordinal));
            if (existing != null)
            {
                existing.BCType = bcType;
                existing.Value = "0";
                existing.Description = $"{bcType} = 0";
                continue;
            }

            bc.BoundaryConditions.Add(new BoundaryConditionRow
            {
                NodeId = nodeId,
                BoundaryName = boundaryName,
                BCType = bcType,
                AnalysisCategory = analysisCategory,
                DegreeOfFreedom = 0,
                Value = "0",
                Description = $"{bcType} = 0"
            });
        }
    }

    private T OnUi<T>(Func<T> action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
            return action();
        return dispatcher.Invoke(action, DispatcherPriority.Normal);
    }

    private void OnUi(Action action) => OnUi(() =>
    {
        action();
        return 0;
    });
}
