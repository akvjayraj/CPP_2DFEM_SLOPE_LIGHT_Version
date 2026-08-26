using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.FiniteElement;
using GeotechSlopeApp.Scripting;

namespace GeotechSlopeApp.ViewModels;

public class ScriptViewModel : INotifyPropertyChanged
{
    private const string Slope64Ex1RelativePath = "scripts/slope_stability_T3_SRF/ex1_homogeneous_slope.dat";
    private const string Slope64Ex1EmbeddedResourceName = "Slope64Ex1HomogeneousSlope.dat";
    private const string Slope64Ex1DisplayName = "SLOPE64 Example 1 — homogeneous slope (structured T3 mesh)";

    private readonly MainWindowViewModel _main;
    private readonly CppAnalysisRunner _p41Runner = new();
    private string _scriptContent = "";
    private string _outputText = "";
    private string _statusMessage = "p51 plane-strain .dat — circular tunnel example loaded. Press Execute Script to run.";
    private bool _isExecuting;
    private bool _isManualContent = false;
    private bool _suppressAutoRefresh;
    private int _scriptSyncDepth;
    private int _projectFileApplyDepth;
    private int _datRefreshSuspendCount;
    private bool _deferLoadedDatFullImport;
    private string _scriptRunBaseName = "fe_script_run";
    private ScriptEditorLanguage _editorLanguage = ScriptEditorLanguage.Dat;
    private string _pythonScript = DefaultPythonScript;

    private bool IsDatRefreshPaused =>
        _suppressAutoRefresh || _scriptSyncDepth > 0 || _datRefreshSuspendCount > 0;

    /// <summary>Suppresses <see cref="RefreshDatContent"/> while bulk mesh/script updates run (e.g. Load Script).</summary>
    public IDisposable SuspendDatRefresh()
    {
        _datRefreshSuspendCount++;
        return new DatRefreshResumeScope(this);
    }

    private sealed class DatRefreshResumeScope(ScriptViewModel vm) : IDisposable
    {
        private ScriptViewModel? _vm = vm;

        public void Dispose()
        {
            if (_vm == null)
                return;
            _vm._datRefreshSuspendCount--;
            _vm = null;
        }
    }

    public ScriptViewModel(MainWindowViewModel main)
    {
        _main = main ?? throw new ArgumentNullException(nameof(main));

        ExecuteScriptCommand = new RelayCommand(OnExecuteScript, () => !IsExecuting);
        LoadScriptCommand = new RelayCommand(OnLoadScript);
        LoadSlope64Ex1Command = new RelayCommand(OnLoadSlope64Ex1);
        SaveScriptCommand = new RelayCommand(OnSaveScript);
        ClearScriptCommand = new RelayCommand(OnClearScript);
        BrowsePythonCommand = new RelayCommand(OnBrowsePython);
        CopyOutputCommand = new RelayCommand(OnCopyOutput);
        ClearOutputCommand = new RelayCommand(OnClearOutput);

        EmitHeaderCommand        = new RelayCommand(() => EmitSection(P610T3DatContentBuilder.Section.Header),        () => CanEmitHeader);
        EmitMaterialsCommand     = new RelayCommand(() => EmitSection(P610T3DatContentBuilder.Section.Materials),     () => CanEmitMaterials);
        EmitMeshCommand          = new RelayCommand(() => EmitSection(P610T3DatContentBuilder.Section.Mesh),          () => CanEmitMesh);
        EmitBcsCommand           = new RelayCommand(() => EmitSection(P610T3DatContentBuilder.Section.Bcs),           () => CanEmitBcs);
        EmitOutputNodesCommand   = new RelayCommand(() => EmitSection(P610T3DatContentBuilder.Section.OutputNodes),   () => CanEmitOutputNodes);
        EmitSolverControlCommand = new RelayCommand(() => EmitSection(P610T3DatContentBuilder.Section.SolverControl), () => CanEmitSolverControl);
        EmitLayersCommand        = new RelayCommand(() => EmitSection(P610T3DatContentBuilder.Section.Layers),        () => CanEmitLayers);
        EmitAllP610T3Command     = new RelayCommand(OnEmitAllP610T3,                                                  () => IsP610T3Mode && CanEmitAll);

        // Seed the editor with the circular-tunnel default BEFORE wiring event
        // handlers so nothing (material/mesh/geometry PropertyChanged fired by
        // subsequent VM construction or binding init) can race in and overwrite
        // it with the 2x2 p51 sample via RefreshDatContent.
        _isManualContent = true;
        ScriptContent = DefaultTunnelDatContent;

        SubscribeToDataChanges();
        SyncFromScript();
    }

    /// <summary>
    /// Script → GUI: analysis category, mesh type, materials, solver control (when SRF deck detected).
    /// </summary>
    /// <summary>
    /// While a <c>.xfem</c> file is being applied, script→canvas sync must not run (it would
    /// re-import mesh boundaries and undo geometry edits such as bulk-deleted points).
    /// </summary>
    public void BeginApplyProjectFile() => _projectFileApplyDepth++;

    public void EndApplyProjectFile()
    {
        if (_projectFileApplyDepth > 0)
            _projectFileApplyDepth--;
    }

    public void SyncFromScript()
    {
        if (_scriptSyncDepth > 0 || _projectFileApplyDepth > 0)
            return;

        _scriptSyncDepth++;
        var prevSuppress = _suppressAutoRefresh;
        _suppressAutoRefresh = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(ScriptContent)
                && P610AnalysisOptionsHelper.IsSrfDatDeck(ScriptContent))
                _main.MaterialPropertiesVM.SelectedAnalysisCategory =
                    MaterialPropertiesViewModel.SrfAnalysisCategory;

            if (!string.IsNullOrWhiteSpace(ScriptContent))
            {
                SyncMeshTypeFromScript();
                if (!_deferLoadedDatFullImport)
                {
                    SyncMeshAndBoundaryFromScript();
                    SyncProjectMaterialsFromScript();
                    SyncLayeringFromScriptEtype();
                }
                SyncSolverControlFromScript();
            }

            SyncPseudoStaticFromScript();
            SyncGroundwaterFromScript();

            if (_main.MaterialPropertiesVM.IsSrfAnalysisMode
                && !_main.MaterialPropertiesVM.IsSrfReadyForRun
                && _main.MaterialPropertiesVM.ReconcileSrfFailureCriterion(ScriptContent))
            {
                SyncAnalysisOptionsToScript();
            }
        }
        finally
        {
            _suppressAutoRefresh = prevSuppress;
            _scriptSyncDepth--;
            if (_scriptSyncDepth == 0 && _main.LoadConditionsVM.IsSeismicLoadUiEnabled)
                SyncPseudoStaticToScript();
        }

        if (_scriptSyncDepth == 0)
            _main.FEAnalysisVM.RefreshRunPseudoStaticDisplay();
    }

    /// <summary>
    /// Maps script <c>etype</c> + mesh centroids to Material Properties layering controls and assignments.
    /// </summary>
    private void SyncLayeringFromScriptEtype()
    {
        if (string.IsNullOrWhiteSpace(ScriptContent))
            return;
        if (!P610AnalysisOptionsHelper.IsSrfDatDeck(ScriptContent))
            return;

        var geom = _main.GeometryDefinitionVM;
        var mat = _main.MaterialPropertiesVM;
        if (!DatEtypeLayeringSync.TrySyncLayeringFromMeshEtype(
                ScriptContent, _main.MeshVM, mat, geom, out var layerMsg))
            return;

        if (!ReferenceEquals(mat.GeometryDefinition, geom))
            mat.GeometryDefinition.CopyGeometryFrom(geom);

        if (!string.IsNullOrWhiteSpace(layerMsg))
            StatusMessage = string.IsNullOrWhiteSpace(StatusMessage)
                ? layerMsg
                : $"{StatusMessage} {layerMsg}";
    }

    /// <summary>
    /// Loads synced script content without re-importing mesh tables or Project Materials
    /// (canvas and material library are already the GUI source of truth).
    /// </summary>
    public void ApplySyncedScriptDeck(string datContent, string? meshType = null, bool skipPseudoStaticImport = false)
    {
        if (string.IsNullOrWhiteSpace(datContent))
            return;

        // MeshVM.ApplyElementTypeFromScript fires PropertyChanged → RefreshDatContent →
        // SyncGuiStateToScript → TrySyncDiscretizedMeshToScriptDat → here again (stack overflow).
        _scriptSyncDepth++;
        try
        {
            _suppressAutoRefresh = true;
            ScriptContent = datContent;
            _isManualContent = true;
            _suppressAutoRefresh = false;

            _main.MaterialPropertiesVM.SelectedAnalysisCategory =
                MaterialPropertiesViewModel.SrfAnalysisCategory;
            if (!string.IsNullOrWhiteSpace(meshType))
                _main.MeshVM.ApplyElementTypeFromScript(meshType);
            else
                SyncMeshTypeFromScript();
            SyncSolverControlFromScript();
            if (!skipPseudoStaticImport)
                SyncPseudoStaticFromScript();
            _main.FEAnalysisVM.RefreshRunPseudoStaticDisplay();
        }
        finally
        {
            _scriptSyncDepth--;
        }
    }

    /// <summary>
    /// Loads a generated p64 Q8 deck into the editor without re-importing mesh tables (already on canvas).
    /// </summary>
    public void ApplyGeneratedP64Deck(string datContent)
        => ApplySyncedScriptDeck(datContent);

    /// <summary>
    /// Loaded explicit p64/SRF deck still matches the mesh tab — run the file verbatim (no mesh→script rebuild).
    /// Branded <c>slope_stability_*_SRF</c> runs always rebuild a p610 .dat from the canvas (p64 explicit layout is incompatible).
    /// </summary>
    public bool ShouldPreserveLoadedExplicitDeck()
    {
        var content = ScriptContent;
        if (!IsManualScriptContent || string.IsNullOrWhiteSpace(content))
            return false;
        if (!DatMeshBoundaryImporter.IsExplicitMeshSourceDeck(content))
            return false;
        if (!DatMeshBoundaryImporter.TryGetExplicitMeshCounts(content, out int datNels, out int datNn))
            return false;

        var mesh = _main.MeshVM;
        if (mesh.MeshInformationRows.Count != datNels)
            return false;
        if (mesh.DiscretizationNodes.Count > 0 && mesh.DiscretizationNodes.Count != datNn)
            return false;

        var mat = _main.MaterialPropertiesVM;
        var options = P610AnalysisOptionsHelper.BuildFromViewModels(mat, mesh);
        var program = P610AnalysisOptionsHelper.ResolveRunnerExecutableForFeAnalysis(
            options, mesh, srfAnalysisMode: mat?.IsSrfAnalysisMode == true);
        if (P610AnalysisOptionsHelper.IsSlopeStabilitySrfExecutable(program))
            return false;

        if (P610AnalysisOptionsHelper.TryParse(content, out var parsed))
        {
            var canvasMesh = P610AnalysisOptionsHelper.ResolveMeshType(mesh);
            if (!string.Equals(parsed.MeshType, canvasMesh, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        int nn = mesh.DiscretizationNodes.Count > 0
            ? mesh.DiscretizationNodes.Count
            : datNn;
        if (!SrfExplicitDeckFreshness.IsDeckFreshForPreserve(
                content,
                _main.BoundaryConditionsVM,
                mesh.MeshInformationRows.ToList(),
                nn,
                _main.FEAnalysisVM))
            return false;

        return true;
    }

    /// <summary>
    /// Rebuilds the Script Editor .dat from the current discretized mesh on the Mesh tab so
    /// Run FE Analysis and Execute Script use the same topology as the canvas (Q4/Q8/T3/T6).
    /// Skipped when <see cref="ShouldPreserveLoadedExplicitDeck"/> is true (loaded explicit deck matches canvas),
    /// unless <paramref name="ignoreLoadedDeckPreserve"/> is set for GUI-driven live sync.
    /// </summary>
    public bool TrySyncDiscretizedMeshToScriptDat(bool ignoreLoadedDeckPreserve = false)
    {
        var mesh = _main.MeshVM;
        var mat = _main.MaterialPropertiesVM;
        var bc = _main.BoundaryConditionsVM;
        var fe = _main.FEAnalysisVM;
        // #region agent log
        AgentDebugRuntimeLog.Write("trace-q4-sync", "Q4SYNC", "ScriptViewModel.cs:TrySyncDiscretizedMeshToScriptDat",
            "entry",
            new
            {
                ignoreLoadedDeckPreserve,
                mesh = AgentDebugRuntimeLog.SummarizeMesh(mesh),
                script = AgentDebugRuntimeLog.SummarizeScript(this)
            });
        // #endregion
        if (mesh is not { IsDiscretized: true } || mesh.MeshInformationRows.Count == 0)
            return false;
        if (mat?.IsSrfAnalysisMode != true)
            return false;
        if (!ignoreLoadedDeckPreserve && ShouldPreserveLoadedExplicitDeck())
            return false;

        mesh.EnsureDiscretizationNodeIds();
        mesh.EnsureMeshInformationElementIds();
        mesh.RefreshMaterialLayerEtypesFromGeometry();

        // Mesh→script rebuild drops AnalysisOptions k_h/k_v; preserve before replace.
        DatPseudoStaticImporter.TryImportFromDat(ScriptContent, out var keepKh, out var keepKv);
        if ((Math.Abs(keepKh) < 1e-12 && Math.Abs(keepKv) < 1e-12)
            && _main.LoadConditionsVM.IsSeismicLoadUiEnabled)
        {
            keepKh = _main.LoadConditionsVM.SeismicLegendKh;
            keepKv = _main.LoadConditionsVM.SeismicLegendKv;
        }
        bool preservePseudoStatic = Math.Abs(keepKh) > 1e-12 || Math.Abs(keepKv) > 1e-12;

        try
        {
            var options = P610AnalysisOptionsHelper.BuildFromViewModels(mat, mesh);
            var program = P610AnalysisOptionsHelper.ResolveRunnerExecutableForFeAnalysis(
                options, mesh, srfAnalysisMode: true);
            var meshType = P610AnalysisOptionsHelper.ResolveMeshType(mesh);
            var route = SrfLiteratureOptimalRouting.Resolve(meshType, options.FailureCriterion, _main.ProjectSettingsVM?.SrfAxisymmetric == true);

            string dat;
            if (route.Deck == SrfLiteratureOptimalRouting.DeckFormat.P64Explicit)
            {
                dat = SrfExplicitDatWriter.WriteFromViewModels(
                    mesh, mat, bc, fe, _main.LoadConditionsVM, _main.GroundwaterVM, _main.SupportVM);
            }
            else if (P610AnalysisOptionsHelper.IsSlopeStabilitySrfExecutable(program))
            {
                dat = P610T3DatContentBuilder.Build(mesh, mat, bc, _main.StagedConstructionVM, fe, _main.LoadConditionsVM, _main.GroundwaterVM, _main.SupportVM);
                dat = DatSolverControlExporter.EnsureSrfT3GravityEquilibrationDeck(dat);
            }
            else
            {
                return false;
            }

            // #region agent log
            AgentDebugRuntimeLog.Write("trace-q4-sync", "Q4SYNC", "ScriptViewModel.cs:TrySyncDiscretizedMeshToScriptDat",
                "before ApplySyncedScriptDeck",
                new
                {
                    program,
                    options.MeshType,
                    mesh = AgentDebugRuntimeLog.SummarizeMesh(mesh),
                    generatedDat = AgentDebugRuntimeLog.SummarizeDat(dat)
                });
            // #endregion
            ApplySyncedScriptDeck(dat, options.MeshType, skipPseudoStaticImport: preservePseudoStatic);
            // #region agent log
            AgentDebugRuntimeLog.Write("trace-q4-sync", "Q4SYNC", "ScriptViewModel.cs:TrySyncDiscretizedMeshToScriptDat",
                "after ApplySyncedScriptDeck",
                new
                {
                    program,
                    options.MeshType,
                    mesh = AgentDebugRuntimeLog.SummarizeMesh(mesh),
                    script = AgentDebugRuntimeLog.SummarizeScript(this)
                });
            // #endregion
            if (preservePseudoStatic)
            {
                _main.LoadConditionsVM.SetScriptPseudoStaticCoefficients(keepKh, keepKv);
                SyncPseudoStaticToScript();
            }
            else
            {
                SyncPseudoStaticToScript();
            }
            SyncGroundwaterToScript();
            _main.FEAnalysisVM.RefreshRunPseudoStaticDisplay();
            StatusMessage = $"Script .dat synced from mesh ({options.MeshType}, {mesh.MeshInformationRows.Count} elements).";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not sync script from mesh: {ex.Message}";
            return false;
        }
    }

    private static bool IsNativeQ8SrfExecutable(string? program)
        => !string.IsNullOrWhiteSpace(program)
           && program.Contains("_Q8", StringComparison.OrdinalIgnoreCase)
           && P610AnalysisOptionsHelper.IsSlopeStabilitySrfExecutable(program);

    /// <summary>
    /// Fills User-Defined Mesh tables and Boundary Conditions popup from explicit or compact p64 SRF .dat.
    /// </summary>
    public void SyncMeshAndBoundaryFromScript()
    {
        if (string.IsNullOrWhiteSpace(ScriptContent))
            return;
        if (!P610AnalysisOptionsHelper.IsSrfDatDeck(ScriptContent))
            return;

        try
        {
            if (DatMeshBoundaryImporter.TryImportToViewModels(
                    ScriptContent,
                    _main.MeshVM.UserDefinedMeshVM,
                    _main.BoundaryConditionsVM,
                    out var summary))
            {
                _main.MeshVM.MeshPopupSelectedTabIndex = MeshViewModel.MeshPopupUserDefinedTabIndex;
                _main.BoundaryConditionsVM.ShowMarkersAfterMeshImport();
                _main.MeshVM.NotifyMeshCanvasDataChanged();
                StatusMessage =
                    $"Mesh/BC from script: {summary.NElements} {summary.MeshType} elements, " +
                    $"{summary.NNodes} nodes, {summary.NBoundaryConditions} BC(s). " +
                    "Use Geometry → From mesh boundary to update geometry outline.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Script mesh import failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Applies <c>mesh_type</c> from the Script Editor .dat when Analysis = Slope stability (SRF).
    /// </summary>
    public void SyncMeshTypeFromScript()
    {
        if (string.IsNullOrWhiteSpace(ScriptContent))
            return;
        if (!P610AnalysisOptionsHelper.IsSrfDatDeck(ScriptContent))
            return;
        if (!P610AnalysisOptionsHelper.TryResolveMeshTypeFromDat(ScriptContent, out var meshType))
            return;
        _main.MeshVM.ApplyElementTypeFromScript(meshType);
    }

    /// <summary>
    /// Fills Material Properties → Project Materials from the Script Editor .dat (p610 / p64).
    /// </summary>
    public void SyncProjectMaterialsFromScript()
    {
        if (string.IsNullOrWhiteSpace(ScriptContent))
            return;
        DatMaterialImporter.TryImportToProjectMaterials(ScriptContent, _main.MaterialPropertiesVM);
    }

    /// <summary>
    /// Fills FE Analysis → Solver control from the Script Editor .dat (p610 and p64 SRF tails).
    /// </summary>
    public void SyncSolverControlFromScript()
    {
        if (string.IsNullOrWhiteSpace(ScriptContent))
            return;
        if (!ShouldSyncSolverControl(ScriptContent))
            return;

        DatSolverControlImporter.TryImportToFeAnalysis(
            ScriptContent,
            _main.FEAnalysisVM,
            srfAnalysisMode: _main.MaterialPropertiesVM?.IsSrfAnalysisMode == true);
    }

    private static bool ShouldSyncSolverControl(string datContent)
    {
        if (P610T3ElasticDatContentBuilder.IsP610T3ElasticContent(datContent)
            && !P610AnalysisOptionsHelper.IsSrfDatDeck(datContent))
            return false;
        if (P610AnalysisOptionsHelper.IsSrfDatDeck(datContent))
            return true;
        return DatSolverControlExporter.HasSolverControlBlock(datContent);
    }

    /// <summary>
    /// Material Properties → Script Editor: patch AnalysisOptions (failure criterion, material type, mesh type).
    /// </summary>
    public void SyncAnalysisOptionsToScript()
    {
        if (_scriptSyncDepth > 0 || _projectFileApplyDepth > 0)
            return;
        if (string.IsNullOrWhiteSpace(ScriptContent))
            return;

        if (!P610AnalysisOptionsHelper.TryPatchAnalysisOptionsFromViewModels(
                ScriptContent,
                _main.MaterialPropertiesVM,
                _main.MeshVM,
                out var patched,
                out var changed))
            return;

        if (!changed)
            return;

        _suppressAutoRefresh = true;
        ScriptContent = patched;
        _suppressAutoRefresh = false;
        StatusMessage = "Script .dat AnalysisOptions updated from Material Properties (failure criterion / material type).";
    }

    /// <summary>
    /// GUI tabs → Script Editor: push current Material Properties, Mesh, BCs, solver control,
    /// pseudo-static, groundwater, and AnalysisOptions into the loaded .dat without requiring Execute.
    /// </summary>
    public void SyncGuiStateToScript()
    {
        if (_scriptSyncDepth > 0 || _projectFileApplyDepth > 0 || IsDatRefreshPaused)
            return;
        if (_main.MeshVM.IsMeshCanvasBulkUpdate)
            return;
        if (string.IsNullOrWhiteSpace(ScriptContent))
            return;

        SyncAnalysisOptionsToScript();

        var mat = _main.MaterialPropertiesVM;
        if (mat?.IsSrfAnalysisMode == true
            && _main.MeshVM is { IsDiscretized: true, MeshInformationRows.Count: > 0 })
        {
            TrySyncDiscretizedMeshToScriptDat(ignoreLoadedDeckPreserve: true);
            SyncSolverControlToScript();
            SyncPseudoStaticToScript();
            SyncGroundwaterToScript();
            SyncDistributedEdgeLoadsToScript();
            return;
        }

        AutoRefreshP610DatSections();
        SyncSolverControlToScript();
        SyncPseudoStaticToScript();
        SyncGroundwaterToScript();
        SyncDistributedEdgeLoadsToScript();
    }

    /// <summary>
    /// FE Analysis → Script Editor: patch tol, limit, incs, layers (p610) or nsrf/F trials (p64 SRF).
    /// </summary>
    public void SyncSolverControlToScript()
    {
        if (string.IsNullOrWhiteSpace(ScriptContent))
            return;
        if (!ShouldSyncSolverControl(ScriptContent))
            return;

        if (!DatSolverControlExporter.TryPatchSolverControl(
                ScriptContent,
                _main.FEAnalysisVM,
                srfAnalysisMode: _main.MaterialPropertiesVM?.IsSrfAnalysisMode == true,
                out var patched,
                out var changed))
        {
            if (!_isManualContent && IsP610T3Mode && HasP610Mesh)
                AutoRefreshP610DatSections();
            return;
        }

        _suppressAutoRefresh = true;
        ScriptContent = patched;
        _suppressAutoRefresh = false;
        if (changed)
            StatusMessage = "Script .dat solver control updated from FE Analysis.";
    }

    /// <summary>Groundwater popup → Script Editor: merge BEGIN:Groundwater block and numeric tail.</summary>
    public void SyncGroundwaterToScript()
    {
        if (_scriptSyncDepth > 0 || _projectFileApplyDepth > 0)
            return;
        if (string.IsNullOrWhiteSpace(ScriptContent))
            return;

        var displayModel = _main.GroundwaterVM.BuildModel();
        var mats = _main.MaterialPropertiesVM?.Materials;
        if (mats is { Count: > 0 } && mats.Any(m => m.IsSaturatedEnabled))
        {
            displayModel.GammaSat = mats
                .Select(m => m.IsSaturatedEnabled ? m.SaturatedUnitWeight : m.UnitWeight)
                .ToList();
        }
        var model = GroundwaterDatImporter.CloneForSolverExport(displayModel, ScriptContent);
        var updated = GroundwaterDatWriter.MergeGroundwaterSection(ScriptContent, model);
        if (GroundwaterDatWriter.ShouldAppendNumericTail(updated, model))
            updated = GroundwaterDatWriter.AppendNumericTail(updated, model);

        if (!string.Equals(updated, ScriptContent, StringComparison.Ordinal))
        {
            _suppressAutoRefresh = true;
            ScriptContent = updated;
            _suppressAutoRefresh = false;
        }

        // Ponded face pressures depend on the WT — refresh edge-load block too.
        SyncDistributedEdgeLoadsToScript();
    }

    /// <summary>Load Conditions → Script Editor: merge distributed-edge and nodal point load blocks.</summary>
    public void SyncDistributedEdgeLoadsToScript()
    {
        if (_scriptSyncDepth > 0 || _projectFileApplyDepth > 0)
            return;
        if (string.IsNullOrWhiteSpace(ScriptContent))
            return;
        if (!_main.MaterialPropertiesVM.Is2DProblem)
            return;

        var gwModel = _main.GroundwaterVM.BuildModel();
        var mats = _main.MaterialPropertiesVM?.Materials;
        if (mats is { Count: > 0 } && mats.Any(m => m.IsSaturatedEnabled))
        {
            gwModel.GammaSat = mats
                .Select(m => m.IsSaturatedEnabled ? m.SaturatedUnitWeight : m.UnitWeight)
                .ToList();
        }

        var updated = DistributedEdgeLoadDatWriter.MergeFromViewModels(
            ScriptContent,
            _main.LoadConditionsVM,
            _main.MeshVM,
            _main.GeometryDefinitionVM,
            gwModel);
        updated = NodalPointLoadDatWriter.MergeFromViewModels(
            updated,
            _main.LoadConditionsVM,
            _main.MeshVM,
            _main.GeometryDefinitionVM);

        if (string.Equals(updated, ScriptContent, StringComparison.Ordinal))
            return;

        _suppressAutoRefresh = true;
        ScriptContent = updated;
        _suppressAutoRefresh = false;
    }

    /// <summary>Script Editor → Groundwater popup: parse nosurf/surf from .dat.</summary>
    public void SyncGroundwaterFromScript()
    {
        if (string.IsNullOrWhiteSpace(ScriptContent))
            return;
        if (!GroundwaterDatImporter.TryImportFromDat(ScriptContent, out var model))
            return;
        GroundwaterDatImporter.ApplyMeshCoordYOffsetForDisplay(ScriptContent, model);
        _main.GroundwaterVM.ApplyImportedModel(model);
    }

    /// <summary>
    /// Script Editor → Project Settings / Load Conditions: sync staged analysis and k_h/k_v
    /// from <c>! k_h</c> / <c>! k_v</c> in the Script Editor .dat.
    /// </summary>
    public void SyncPseudoStaticFromScript()
    {
        DatPseudoStaticImporter.TryImportToSeismicStage(
            ScriptContent, _main.FEAnalysisVM, _main.StagedConstructionVM, _main.LoadConditionsVM, out _);
    }

    /// <summary>
    /// Load Conditions → Script Editor: patch <c>! k_h</c> / <c>! k_v</c> for the seismic edit target
    /// (active footer seismic stage or picker selection when Seismic / Inertia Load is enabled).
    /// </summary>
    public void SyncPseudoStaticToScript()
    {
        if (_scriptSyncDepth > 0 || _projectFileApplyDepth > 0)
            return;
        if (string.IsNullOrWhiteSpace(ScriptContent))
            return;

        if (!_main.LoadConditionsVM.IsSeismicLoadUiEnabled)
        {
            if (DatPseudoStaticExporter.TryPatchPseudoStatic(
                    ScriptContent, 0, 0, out var cleared, out var zeroed) && zeroed)
            {
                _suppressAutoRefresh = true;
                ScriptContent = cleared;
                _suppressAutoRefresh = false;
                StatusMessage = "Script .dat pseudo-static coefficients (k_h, k_v) cleared.";
            }
        }
        else
        {
            var (kh, kv) = PseudoStaticSeismicRunHelper.ResolveCoefficientsForScriptEditor(
                _main.FEAnalysisVM, _main.LoadConditionsVM);
            if (DatPseudoStaticExporter.TryPatchPseudoStatic(
                    ScriptContent, kh, kv, out var patched, out var changed)
                && changed)
            {
                _suppressAutoRefresh = true;
                ScriptContent = patched;
                _suppressAutoRefresh = false;
                StatusMessage = "Script .dat pseudo-static coefficients (k_h, k_v) updated from staged seismic settings.";
            }
        }

        _main.FEAnalysisVM.RefreshRunPseudoStaticDisplay();
    }

    /// <summary>Latest .dat text with GUI solver settings applied (for Run FE Analysis / Execute Script).</summary>
    public string GetScriptContentForSolverRun(int? stageNumber = null)
    {
        if (ShouldSyncSolverControl(ScriptContent))
            SyncSolverControlToScript();
        // Script → GUI import only when the GUI has no seismic state of its own:
        // mesh sync often rebuilds the .dat without `! k_h` / `! k_v`, and importing
        // that stripped buffer would force the "Enable seismic analysis" checkbox off
        // mid-run. With the checkbox on, the GUI is the source of truth and
        // SyncPseudoStaticToScript re-patches the coefficients into the deck.
        if (!_main.LoadConditionsVM.IsSeismicLoadUiEnabled)
            SyncPseudoStaticFromScript();
        SyncPseudoStaticToScript();

        var content = DatMeshBoundaryImporter.IsExplicitMeshSourceDeck(ScriptContent)
            ? ScriptContent
            : DatSolverControlExporter.SanitizeP610DatForSolver(ScriptContent);

        // Re-assert k_h/k_v from the buffer or Load Conditions holder (mesh sync may have stripped them).
        if (DatPseudoStaticImporter.TryImportFromDat(content, out var datKh, out var datKv))
        {
            DatPseudoStaticExporter.TryPatchPseudoStatic(content, datKh, datKv, out content, out _);
        }
        else if (_main.LoadConditionsVM.IsSeismicLoadUiEnabled)
        {
            var (kh, kv) = PseudoStaticSeismicRunHelper.ResolveCoefficientsForScriptEditor(
                _main.FEAnalysisVM, _main.LoadConditionsVM);
            DatPseudoStaticExporter.TryPatchPseudoStatic(content, kh, kv, out content, out _);
        }

        if (stageNumber.HasValue)
        {
            var (kh, kv) = PseudoStaticSeismicRunHelper.ResolveCoefficientsForStage(
                _main.FEAnalysisVM, stageNumber.Value);
            if (DatPseudoStaticExporter.TryPatchPseudoStatic(content, kh, kv, out var patched, out _))
                content = patched;
        }

        content = SupportDatWriter.MergeFromViewModels(
            content, _main.SupportVM.PropertySets, _main.SupportVM.Elements);
        if (_main.GeometryDefinitionVM.GeologicalFeatures is { Count: > 0 } joints)
            content = JointDatWriter.MergeSection(
                content,
                JointDatWriter.BuildBlock(joints, _main.GeometryDefinitionVM.JointPropertySets, _main.MeshVM.JointInterfaceElements));

        return content;
    }

    private void SubscribeToDataChanges()
    {
        var geom = _main.GeometryDefinitionVM;
        geom.PropertyChanged += (_, _) => RefreshDatContent();
        geom.GeometryVertexRows.CollectionChanged += OnGeometryChanged;
        geom.GeometryLineSegments.CollectionChanged += OnGeometryChanged;
        SubscribeToVertices(geom.GeometryVertexRows);
        SubscribeToSegments(geom.GeometryLineSegments);

        var matGeom = _main.MaterialPropertiesVM.GeometryDefinition;
        if (!ReferenceEquals(matGeom, geom))
        {
            matGeom.GeometryLineSegments.CollectionChanged += OnGeometryChanged;
            SubscribeToSegments(matGeom.GeometryLineSegments);
        }
        _main.MaterialPropertiesVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MaterialPropertiesViewModel.SelectedFailureCriterion)
                or nameof(MaterialPropertiesViewModel.SelectedMaterialType))
                MaybeAutoSwitchToP610T3();

            if (ShouldRefreshScriptFromMaterialPropertiesChange(e.PropertyName))
                RefreshDatContent();
        };
        SubscribeToMaterials(_main.MaterialPropertiesVM.Materials);
        _main.MaterialPropertiesVM.LayerAssignments.CollectionChanged += (_, _) => RefreshDatContent();
        foreach (var row in _main.MaterialPropertiesVM.LayerAssignments)
            row.PropertyChanged += (_, _) => RefreshDatContent();

        _main.MeshVM.PropertyChanged += (_, e) =>
        {
            RefreshDatContent();
            if (e.PropertyName == nameof(MeshViewModel.IsDiscretized))
                MaybeAutoSwitchToP610T3();
        };
        _main.MeshVM.DiscretizationNodes.CollectionChanged += (_, _) => RefreshDatContent();
        _main.MeshVM.MeshElements.CollectionChanged += (_, _) => RefreshDatContent();
        _main.MeshVM.MeshInformationRows.CollectionChanged += (_, _) =>
        {
            RefreshDatContent();
            MaybeAutoSwitchToP610T3();
            RaiseWizardState();
        };

        _main.ProjectSettingsVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProjectSettingsViewModel.SelectedDimension))
                RefreshDatContent();
            if (e.PropertyName is nameof(ProjectSettingsViewModel.IsActiveAnalysisCategoryOn)
                or nameof(ProjectSettingsViewModel.AreSelectedCategoryTypeOptionsEnabled))
                OnPropertyChanged(nameof(IsSsrOn));
        };

        _main.LoadConditionsVM.Loads.CollectionChanged += OnLoadsChanged;
        foreach (var load in _main.LoadConditionsVM.Loads)
            load.PropertyChanged += (_, _) => RefreshDatContent();

        _main.LoadConditionsVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LoadConditionsViewModel.IsSeismicLoadUiEnabled)
                or nameof(LoadConditionsViewModel.SeismicAnalysisEnabled)
                or nameof(LoadConditionsViewModel.AreSeismicCoefficientsEditable)
                or nameof(LoadConditionsViewModel.SelectedSeismicStageForEdit)
                or nameof(LoadConditionsViewModel.SeismicEditTarget)
                or nameof(LoadConditionsViewModel.IsSeismicExpanderExpanded)
                or nameof(LoadConditionsViewModel.ActiveSeismicStageDefinition)
                or nameof(LoadConditionsViewModel.ScriptPseudoStaticCoeffsActive))
                SyncPseudoStaticToScript();
        };
        _main.LoadConditionsVM.PseudoStaticCoefficientsChanged += (_, _) => SyncPseudoStaticToScript();

        _main.BoundaryConditionsVM.BoundaryConditions.CollectionChanged += OnBoundaryConditionsChanged;
        foreach (var row in _main.BoundaryConditionsVM.BoundaryConditions)
            row.PropertyChanged += (_, _) => RefreshDatContent();

        // GUI → .dat: solver control edits on FE Analysis sync into the Script Editor.
        _main.FEAnalysisVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FEAnalysisViewModel.ConvergenceTolerance)
                or nameof(FEAnalysisViewModel.ConvergenceToleranceText)
                or nameof(FEAnalysisViewModel.MaxIterations)
                or nameof(FEAnalysisViewModel.SrfTrialCount)
                or nameof(FEAnalysisViewModel.LoadIncrements)
                or nameof(FEAnalysisViewModel.LayersOverride)
                or nameof(FEAnalysisViewModel.LayersText))
            {
                OnFeSolverControlChanged();
                return;
            }

            if (e.PropertyName == nameof(FEAnalysisViewModel.StagedExcavationEnabled))
                SyncPseudoStaticToScript();

            if (e.PropertyName == nameof(FEAnalysisViewModel.HasSeismicDynamicStage))
                SyncPseudoStaticToScript();
        };

        _main.FEAnalysisVM.SrfValues.CollectionChanged += OnSrfValuesCollectionChanged;
        SubscribeSrfTrialEntries(_main.FEAnalysisVM.SrfValues);

        _main.FEAnalysisVM.StageDefinitions.CollectionChanged += OnStageDefinitionsForPseudoStaticChanged;
        foreach (var stage in _main.FEAnalysisVM.StageDefinitions)
            stage.PropertyChanged += OnStageDefinitionForPseudoStaticChanged;

        _main.StagedConstructionVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(StagedConstructionViewModel.ActiveStageNumber)
                or nameof(StagedConstructionViewModel.ActiveStage))
                SyncPseudoStaticToScript();
            else
                RefreshDatContent();
        };

        if (_main.GroundwaterVM != null)
            _main.GroundwaterVM.PropertyChanged += OnGroundwaterVmPropertyChangedForScript;
    }

    /// <summary>
    /// Only rewrite the Script .dat when groundwater inputs that affect the solver deck change.
    /// UI-gate / overlay notifications (IsWaterHeadAllowed, unit labels, etc.) must not rebuild .dat.
    /// </summary>
    private void OnGroundwaterVmPropertyChangedForScript(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null
            or nameof(GroundwaterViewModel.IsEnabled)
            or nameof(GroundwaterViewModel.GammaW)
            or nameof(GroundwaterViewModel.Method)
            or nameof(GroundwaterViewModel.Ru)
            or nameof(GroundwaterViewModel.IsRuMode)
            or nameof(GroundwaterViewModel.IsHydrostaticMode)
            or nameof(GroundwaterViewModel.PondedWaterLevelMethod)
            or nameof(GroundwaterViewModel.PondedTotalHead)
            or nameof(GroundwaterViewModel.GroundwaterSurfPoints)
            or nameof(GroundwaterViewModel.TotalHeadGridPointCount)
            or nameof(GroundwaterViewModel.PressureHeadGridPointCount)
            or nameof(GroundwaterViewModel.PorePressureGridPointCount)
            or nameof(GroundwaterViewModel.WaterHeads)
            or nameof(GroundwaterViewModel.WaterPressureGrids))
        {
            RefreshDatContent();
        }
    }

    private void SubscribeToMaterials(ObservableCollection<MaterialModel> materials)
    {
        materials.CollectionChanged += OnMaterialsCollectionChanged;
        foreach (var material in materials)
            material.PropertyChanged += OnMaterialModelPropertyChanged;
    }

    private void OnMaterialsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (MaterialModel material in e.OldItems)
                material.PropertyChanged -= OnMaterialModelPropertyChanged;
        }

        if (e.NewItems != null)
        {
            foreach (MaterialModel material in e.NewItems)
                material.PropertyChanged += OnMaterialModelPropertyChanged;
        }

        RefreshDatContent();
    }

    private void OnMaterialModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => RefreshDatContent();

    private void OnStageDefinitionsForPseudoStaticChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (StageDefinition stage in e.OldItems)
                stage.PropertyChanged -= OnStageDefinitionForPseudoStaticChanged;
        }

        if (e.NewItems != null)
        {
            foreach (StageDefinition stage in e.NewItems)
                stage.PropertyChanged += OnStageDefinitionForPseudoStaticChanged;
        }

        SyncPseudoStaticToScript();
    }

    private void OnStageDefinitionForPseudoStaticChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StageDefinition.StageType)
            or nameof(StageDefinition.PseudoStaticKh)
            or nameof(StageDefinition.PseudoStaticKv))
            SyncPseudoStaticToScript();
    }

    private void OnFeSrfSolverControlChanged()
        => OnFeSolverControlChanged();

    private void OnFeSolverControlChanged()
    {
        SyncSolverControlToScript();
        if (!_isManualContent && IsP610T3Mode && HasP610Mesh)
            AutoRefreshP610DatSections();
    }

    private void OnSrfValuesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (SrfTrialEntry entry in e.OldItems)
                entry.PropertyChanged -= OnSrfTrialEntryPropertyChanged;
        }

        if (e.NewItems != null)
            SubscribeSrfTrialEntries(e.NewItems.Cast<SrfTrialEntry>());

        OnFeSrfSolverControlChanged();
    }

    private void SubscribeSrfTrialEntries(IEnumerable<SrfTrialEntry> entries)
    {
        foreach (var entry in entries)
            entry.PropertyChanged += OnSrfTrialEntryPropertyChanged;
    }

    private void OnSrfTrialEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SrfTrialEntry.Value))
            OnFeSrfSolverControlChanged();
    }

    private void SubscribeToVertices(ObservableCollection<GeometryDefinitionViewModel.GeometryVertexDisplay> vertices)
    {
        foreach (var v in vertices)
            v.PropertyChanged += (_, _) => RefreshDatContent();
    }

    private void SubscribeToSegments(ObservableCollection<GeometryLineSegment> segments)
    {
        foreach (var s in segments)
            s.PropertyChanged += (_, _) => RefreshDatContent();
    }

    private void OnGeometryChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshDatContent();
        if (sender is System.Collections.IList list && e.NewItems != null)
            foreach (var item in e.NewItems.Cast<object>())
                if (item is INotifyPropertyChanged npc)
                    npc.PropertyChanged += (_, _) => RefreshDatContent();
    }

    private void OnLoadsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshDatContent();
        if (e.NewItems != null)
            foreach (var item in e.NewItems.Cast<object>())
                if (item is INotifyPropertyChanged npc)
                    npc.PropertyChanged += (_, _) => RefreshDatContent();
    }

    private void OnBoundaryConditionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshDatContent();
        if (e.NewItems != null)
            foreach (var item in e.NewItems.Cast<object>())
                if (item is INotifyPropertyChanged npc)
                    npc.PropertyChanged += (_, _) => RefreshDatContent();
    }

    /// <summary>Returns true when the script content starts with a p51 plane/axisymmetric header (ignoring comments).</summary>
    private static bool ScriptContentIsP51(string content)
    {
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('!')) continue;
            return line.StartsWith("'plane'", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("'axisymmetric'", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    /// <summary>Returns true when the script content was generated for p610_t3 (solver tag present).</summary>
    private static bool ScriptContentIsP610T3(string content)
        => P610T3DatContentBuilder.IsP610T3Content(content);

    /// <summary>Returns true when the script content was generated for p610_t3_elastic (more specific tag).</summary>
    private static bool ScriptContentIsP610T3Elastic(string content)
        => P610T3ElasticDatContentBuilder.IsP610T3ElasticContent(content);

    /// <summary>
    /// True when a 2D p610-compatible mesh exists (T3/T6/Q4/Q8) — p610_t3 format should be used.
    /// Per-row mesh kind is detected via <see cref="MeshInformationRow.Kind"/> or inferred from connectivity.
    /// </summary>
    private bool ShouldUseP610T3Format()
    {
        if (!(_main.MaterialPropertiesVM?.Is2DProblem ?? false)) return false;
        var rows = _main.MeshVM?.MeshInformationRows;
        return rows is { Count: > 0 };
    }

    /// <summary>
    /// True when the user picked Material Properties ▸ Failure criterion = Elastic and
    /// Material type = Isotropic — the wizard then emits the linear-elastic .dat layout
    /// (single solve, far-field traction BCs) consumed by p610_t3_elastic.exe.
    /// </summary>
    private bool ShouldUseP610T3ElasticFormat()
        => ShouldUseP610T3Format()
           && _main.FEAnalysisVM?.UseP610T3ElasticFromScriptDat == true;

    /// <summary>
    /// UI-only Material Properties changes (row selection, excavation status, etc.) must not
    /// round-trip the script and re-import Project Materials — that overwrote φ/c/E on row click.
    /// </summary>
    private static bool ShouldRefreshScriptFromMaterialPropertiesChange(string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
            return true;

        return propertyName is not (
            nameof(MaterialPropertiesViewModel.SelectedMaterialModel)
            or nameof(MaterialPropertiesViewModel.SelectedLayerAssignmentRow)
            or nameof(MaterialPropertiesViewModel.SelectedLayerSummaryForMaterials)
            or nameof(MaterialPropertiesViewModel.SelectedLineSegment)
            or nameof(MaterialPropertiesViewModel.HasExcavationSummary)
            or nameof(MaterialPropertiesViewModel.IsExcavateMode)
            or nameof(MaterialPropertiesViewModel.ExcavateModeButtonLabel)
            or nameof(MaterialPropertiesViewModel.StagedConstructionStatusMessage)
            or nameof(MaterialPropertiesViewModel.IsAssignMaterialMode)
            or nameof(MaterialPropertiesViewModel.MaterialTypeCount)
            or nameof(MaterialPropertiesViewModel.MaterialTypeCountSummary)
            or nameof(MaterialPropertiesViewModel.IsSrfReadyForRun)
            or nameof(MaterialPropertiesViewModel.SelectedAnalysisCategory)
            or nameof(MaterialPropertiesViewModel.SelectedDetailedAnalysisType)
            or nameof(MaterialPropertiesViewModel.DetailedAnalysisTypes)
            or nameof(MaterialPropertiesViewModel.IsSrfAnalysisMode));
    }

    /// <summary>
    /// Push analysis routing and GUI state into the script deck immediately before solver launch.
    /// Not called on Project Settings dropdown changes — those are UI routing only.
    /// </summary>
    public void SyncAnalysisRoutingFromGui()
    {
        if (IsDatRefreshPaused || _scriptSyncDepth > 0)
            return;
        // #region agent log
        AgentDebugRuntimeLog.Write("trace-q4-sync", "Q4SYNC", "ScriptViewModel.cs:SyncAnalysisRoutingFromGui",
            "entry",
            new
            {
                isManualContent = _isManualContent,
                isSrf = _main.MaterialPropertiesVM?.IsSrfAnalysisMode == true,
                mesh = AgentDebugRuntimeLog.SummarizeMesh(_main.MeshVM),
                script = AgentDebugRuntimeLog.SummarizeScript(this)
            });
        // #endregion
        if (_isManualContent)
        {
            SyncGuiStateToScript();
            // #region agent log
            AgentDebugRuntimeLog.Write("trace-q4-sync", "Q4SYNC", "ScriptViewModel.cs:SyncAnalysisRoutingFromGui",
                "after SyncGuiStateToScript in manual branch",
                new
                {
                    mesh = AgentDebugRuntimeLog.SummarizeMesh(_main.MeshVM),
                    script = AgentDebugRuntimeLog.SummarizeScript(this)
                });
            // #endregion
            var hasAuthoritativeMeshRows = _main.MeshVM is { IsDiscretized: true, MeshInformationRows.Count: > 0 };
            if (_main.MaterialPropertiesVM?.IsSrfAnalysisMode == true && !hasAuthoritativeMeshRows)
            {
                SyncMeshAndBoundaryFromScript();
                // #region agent log
                AgentDebugRuntimeLog.Write("trace-q4-sync", "Q4SYNC", "ScriptViewModel.cs:SyncAnalysisRoutingFromGui",
                    "after SyncMeshAndBoundaryFromScript in manual branch",
                    new
                    {
                        mesh = AgentDebugRuntimeLog.SummarizeMesh(_main.MeshVM),
                        script = AgentDebugRuntimeLog.SummarizeScript(this)
                    });
                // #endregion
            }
            else if (_main.MaterialPropertiesVM?.IsSrfAnalysisMode == true)
            {
                // #region agent log
                AgentDebugRuntimeLog.Write("trace-q4-sync", "Q4SYNC", "ScriptViewModel.cs:SyncAnalysisRoutingFromGui",
                    "skipped script-to-mesh import because Mesh tab is authoritative for SRF run",
                    new
                    {
                        mesh = AgentDebugRuntimeLog.SummarizeMesh(_main.MeshVM),
                        script = AgentDebugRuntimeLog.SummarizeScript(this)
                    });
                // #endregion
            }
            return;
        }
        RefreshDatContent();
        // #region agent log
        AgentDebugRuntimeLog.Write("trace-q4-sync", "Q4SYNC", "ScriptViewModel.cs:SyncAnalysisRoutingFromGui",
            "after RefreshDatContent",
            new
            {
                mesh = AgentDebugRuntimeLog.SummarizeMesh(_main.MeshVM),
                script = AgentDebugRuntimeLog.SummarizeScript(this)
            });
        // #endregion
    }

    public void RefreshDatContent()
    {
        if (IsDatRefreshPaused || _scriptSyncDepth > 0)
            return;
        // Don't overwrite manually loaded / user-edited content
        if (_isManualContent)
        {
            SyncGuiStateToScript();
            return;
        }
        try
        {
            // Note: p610_t3 is never auto-built here except Mohr–Coulomb joint slip,
            // which must not fall through to the p51 demo mesh.

            if (_main.FEAnalysisVM?.HasMohrCoulombJointSlip == true)
            {
                ScriptContent = _main.FEAnalysisVM.TryBuildMohrCoulombJointP610Dat()
                    ?? P610T3DatContentBuilder.SkeletonHeader();
                return;
            }

            if (FEAnalysisViewModel.UseP51PlaneStrainScriptFormat(_main.MaterialPropertiesVM))
            {
                var input = P51DatFileWriter.FromRectangularMesh(
                    width: 1.0, height: 1.0,
                    nxe: 2, nye: 2,
                    youngsModulus: 1e6, poissonsRatio: 0.3,
                    restrainedNodes: new List<(int, int, int)>
                    {
                        (1, 0, 1), (4, 0, 1), (7, 0, 0), (8, 1, 0), (9, 1, 0)
                    },
                    loadedNodes: new List<(int, double, double)>
                    {
                        (1, 0.0, -0.25), (2, 0.0, -0.50), (3, 0.0, -0.25)
                    },
                    dir: _main.MeshVM.MeshNumberingDirectionChar);
                input.UseType2dHeaderLines = true;
                using var sw = new StringWriter();
                new P51DatFileWriter().Write(sw, input);
                ScriptContent = sw.ToString();
                return;
            }

            var content = P41DatContentBuilder.Build(
                _main.GeometryDefinitionVM,
                _main.LoadConditionsVM,
                _main.MaterialPropertiesVM.GeometryDefinition,
                _main.MeshVM,
                _main.BoundaryConditionsVM);
            ScriptContent = content;
        }
        catch (Exception ex)
        {
            ScriptContent = $"# Error building .dat: {ex.Message}\n\n4  1\n100000.0\n0.25  0.25  0.25  0.25\n1\n5 0\n5\n1 -0.625  2 -1.25  3 -1.25  4 -1.25  5 -0.625\n0\n";
        }
    }

    private void AutoRefreshP610DatSections()
    {
        if (!IsP610T3Mode || !HasP610Mesh) return;

        // Verbatim-loaded .dat files (reference / smoke test / hand-written inputs)
        // have no wizard sentinels, so MergeSection would simply append freshly built
        // sections and corrupt the numeric layout the solver expects (e.g. inserting
        // a <BEGIN:Layers> excavation block that replaces the traction group, producing
        // zero results). Leave them untouched — the user already asked the solver to
        // execute them as-is. Reset restores the wizard skeleton when they want it.
        if (IsVerbatimLoadedContent()) return;

        try
        {
            bool elastic = ShouldUseP610T3ElasticFormat()
                || (IsP610T3ElasticMode && _main.FEAnalysisVM?.HasMohrCoulombJointSlip != true);
            string updated = ScriptContent;
            foreach (var section in P610T3DatContentBuilder.SectionOrder)
            {
                string block = elastic
                    ? section switch
                    {
                        P610T3DatContentBuilder.Section.Header        => P610T3ElasticDatContentBuilder.BuildHeader(_main.MeshVM, _main.MaterialPropertiesVM),
                        P610T3DatContentBuilder.Section.Materials     => P610T3ElasticDatContentBuilder.BuildMaterials(_main.MaterialPropertiesVM),
                        P610T3DatContentBuilder.Section.Mesh          => P610T3ElasticDatContentBuilder.BuildMesh(_main.MeshVM, _main.MaterialPropertiesVM),
                        P610T3DatContentBuilder.Section.Bcs           => P610T3ElasticDatContentBuilder.BuildBcs(_main.BoundaryConditionsVM, _main.MeshVM),
                        P610T3DatContentBuilder.Section.OutputNodes   => P610T3ElasticDatContentBuilder.BuildOutputNodes(_main.BoundaryConditionsVM, _main.MeshVM),
                        P610T3DatContentBuilder.Section.SolverControl => P610T3ElasticDatContentBuilder.BuildSolverControl(),
                        P610T3DatContentBuilder.Section.Layers        => P610T3ElasticDatContentBuilder.BuildTractions(_main.MeshVM, _main.BoundaryConditionsVM, _main.LoadConditionsVM?.FieldStress, _main.MaterialPropertiesVM),
                        _ => string.Empty
                    }
                    : section switch
                    {
                        P610T3DatContentBuilder.Section.Header        => P610T3DatContentBuilder.BuildHeader(_main.MeshVM, _main.MaterialPropertiesVM, _main.FEAnalysisVM),
                        P610T3DatContentBuilder.Section.Materials     => P610T3DatContentBuilder.BuildMaterials(_main.MaterialPropertiesVM),
                        P610T3DatContentBuilder.Section.Mesh          => P610T3DatContentBuilder.BuildMesh(_main.MeshVM, _main.MaterialPropertiesVM, _main.GeometryDefinitionVM),
                        P610T3DatContentBuilder.Section.Bcs           => P610T3DatContentBuilder.BuildBcs(_main.BoundaryConditionsVM, _main.MeshVM),
                        P610T3DatContentBuilder.Section.OutputNodes   => P610T3DatContentBuilder.BuildOutputNodes(_main.BoundaryConditionsVM, _main.MeshVM),
                        P610T3DatContentBuilder.Section.SolverControl => P610T3DatContentBuilder.BuildSolverControl(_main.MeshVM, _main.StagedConstructionVM, _main.FEAnalysisVM),
                        P610T3DatContentBuilder.Section.Layers        => P610T3DatContentBuilder.BuildLayers(_main.MeshVM, _main.StagedConstructionVM, _main.FEAnalysisVM, _main.LoadConditionsVM),
                        _ => string.Empty
                    };

                if (!string.IsNullOrWhiteSpace(block))
                    updated = P610T3DatContentBuilder.MergeSection(updated, section, block);
            }

            if (!string.Equals(updated, ScriptContent, StringComparison.Ordinal))
            {
                _suppressAutoRefresh = true;
                ScriptContent = updated;
                _suppressAutoRefresh = false;
                StatusMessage = "Script .dat synchronized from current tab inputs.";
            }
        }
        catch
        {
            // Keep user content untouched when auto-sync cannot regenerate sections yet.
        }
    }

    /// <summary>When true, auto-refresh does not replace script from geometry (user/p64 deck). Persisted in .xfem.</summary>
    public bool IsManualScriptContent
    {
        get => _isManualContent;
        set { _isManualContent = value; OnPropertyChanged(); }
    }

    public string ScriptContent
    {
        get => _scriptContent;
        set
        {
            _scriptContent = value ?? "";
            OnPropertyChanged();
            RaiseWizardState();
            if (!_suppressAutoRefresh)
                SyncFromScript();
        }
    }

    public string OutputText
    {
        get => _outputText;
        set { _outputText = value ?? ""; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value ?? ""; OnPropertyChanged(); }
    }

    public bool IsExecuting
    {
        get => _isExecuting;
        set { _isExecuting = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExecuteScriptCommand)); }
    }

    public ScriptEditorLanguage EditorLanguage
    {
        get => _editorLanguage;
        set
        {
            if (_editorLanguage == value) return;
            _editorLanguage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDatMode));
            OnPropertyChanged(nameof(IsPythonMode));
            OnPropertyChanged(nameof(ShowP610Wizard));
            if (value == ScriptEditorLanguage.Python)
                RefreshPythonStatus();
        }
    }

    public bool IsDatMode
    {
        get => EditorLanguage == ScriptEditorLanguage.Dat;
        set { if (value) EditorLanguage = ScriptEditorLanguage.Dat; }
    }

    public bool IsPythonMode
    {
        get => EditorLanguage == ScriptEditorLanguage.Python;
        set { if (value) EditorLanguage = ScriptEditorLanguage.Python; }
    }

    /// <summary>Same ON flag as Project Settings → Analysis → Slope stability – strength reduction (SRF).</summary>
    public bool IsSsrOn
    {
        get => _main.ProjectSettingsVM.IsCategoryOn(MaterialPropertiesViewModel.SrfAnalysisCategory);
        set
        {
            _main.MaterialPropertiesVM.SelectedAnalysisCategory = MaterialPropertiesViewModel.SrfAnalysisCategory;
            _main.ProjectSettingsVM.SetCategoryOn(MaterialPropertiesViewModel.SrfAnalysisCategory, value);
            OnPropertyChanged();
        }
    }

    public string PythonScript
    {
        get => _pythonScript;
        set { _pythonScript = value ?? ""; OnPropertyChanged(); }
    }

    public ICommand ExecuteScriptCommand { get; }
    public ICommand LoadScriptCommand { get; }
    public ICommand LoadSlope64Ex1Command { get; }
    public ICommand SaveScriptCommand { get; }
    public ICommand ClearScriptCommand { get; }
    public ICommand BrowsePythonCommand { get; }
    public ICommand CopyOutputCommand { get; }
    public ICommand ClearOutputCommand { get; }

    // ─── p610_t3 wizard ─────────────────────────────────────────────────────

    public ICommand EmitHeaderCommand { get; }
    public ICommand EmitMaterialsCommand { get; }
    public ICommand EmitMeshCommand { get; }
    public ICommand EmitBcsCommand { get; }
    public ICommand EmitOutputNodesCommand { get; }
    public ICommand EmitSolverControlCommand { get; }
    public ICommand EmitLayersCommand { get; }
    public ICommand EmitAllP610T3Command { get; }

    /// <summary>
    /// True when the current script is any p610_t3-family .dat (tag present).
    /// This includes both the elasto-plastic <c>p610_t3</c> and the linear-elastic
    /// <c>p610_t3_elastic</c> dialects, since they share the same wizard buttons.
    /// </summary>
    public bool IsP610T3Mode => P610T3DatContentBuilder.IsP610T3Content(ScriptContent);
    public bool ShowP610Wizard => IsP610T3Mode && IsDatMode;

    /// <summary>True only when the script is the p610_t3_elastic dialect (more specific tag).</summary>
    public bool IsP610T3ElasticMode => P610T3ElasticDatContentBuilder.IsP610T3ElasticContent(ScriptContent);

    /// <summary>True when MeshInformationRows contains at least one element of any 2D kind (T3/T6/Q4/Q8).</summary>
    private bool HasP610Mesh
    {
        get
        {
            var rows = _main.MeshVM?.MeshInformationRows;
            return rows is { Count: > 0 };
        }
    }

    public bool CanEmitHeader        => IsP610T3Mode && HasP610Mesh;
    public bool CanEmitMaterials     => IsP610T3Mode && (_main.MaterialPropertiesVM?.Materials?.Count ?? 0) > 0;
    public bool CanEmitMesh          => IsP610T3Mode && HasP610Mesh;
    public bool CanEmitBcs           => IsP610T3Mode && HasP610Mesh && (_main.BoundaryConditionsVM?.BoundaryConditions?.Count ?? 0) > 0;
    public bool CanEmitOutputNodes   => IsP610T3Mode && HasP610Mesh;
    public bool CanEmitSolverControl => IsP610T3Mode && HasP610Mesh;
    public bool CanEmitLayers        => IsP610T3Mode && HasP610Mesh;
    public bool CanEmitAll           => CanEmitHeader && CanEmitMaterials && CanEmitMesh && CanEmitBcs && CanEmitOutputNodes && CanEmitSolverControl && CanEmitLayers;

    public bool SectionHeaderDone        => P610T3DatContentBuilder.IsSectionDone(ScriptContent, P610T3DatContentBuilder.Section.Header);
    public bool SectionMaterialsDone     => P610T3DatContentBuilder.IsSectionDone(ScriptContent, P610T3DatContentBuilder.Section.Materials);
    public bool SectionMeshDone          => P610T3DatContentBuilder.IsSectionDone(ScriptContent, P610T3DatContentBuilder.Section.Mesh);
    public bool SectionBcsDone           => P610T3DatContentBuilder.IsSectionDone(ScriptContent, P610T3DatContentBuilder.Section.Bcs);
    public bool SectionOutputNodesDone   => P610T3DatContentBuilder.IsSectionDone(ScriptContent, P610T3DatContentBuilder.Section.OutputNodes);
    public bool SectionSolverControlDone => P610T3DatContentBuilder.IsSectionDone(ScriptContent, P610T3DatContentBuilder.Section.SolverControl);
    public bool SectionLayersDone        => P610T3DatContentBuilder.IsSectionDone(ScriptContent, P610T3DatContentBuilder.Section.Layers);

    /// <summary>Directory containing p41.exe. Uses FEAnalysisVM's CppExecutableDirectory when running.</summary>
    public string P41ExecutableDirectory
    {
        get => _p41Runner.ExecutableDirectory;
        set => _p41Runner.ExecutableDirectory = value ?? "";
    }

    /// <summary>
    /// Resolves the working directory for a given solver's generated <c>.dat</c> / <c>.res</c> / <c>.json</c>
    /// files. Prefers a <c>scripts\</c> folder in the current repo (walking up from the app's base dir,
    /// looking for CMakeLists.txt as the repo marker), with a per-solver subfolder so the three solvers
    /// don't stomp on each other. Falls back to <c>%TEMP%\GeotechSlopeApp_&lt;solver&gt;\</c> when the app
    /// is not being run from inside the worktree (e.g. installed build).
    /// </summary>
    private static string ResolveSolverWorkDir(string solverTag)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // Deployed/release layout: library\solver lives next to the GUI .exe. When that
        // is the case, anchor the script work directory under BaseDirectory directly so
        // we never accidentally walk up to a parent folder that happens to contain a
        // CMakeLists.txt (e.g. when the release is unzipped into a path with sibling
        // repos, or extracted twice into a Downloads folder).
        if (Directory.Exists(Path.Combine(baseDir, "library", "solver")))
        {
            var deployedWorkDir = Path.Combine(baseDir, "scripts", solverTag);
            Directory.CreateDirectory(deployedWorkDir);
            return deployedWorkDir;
        }

        var repoRoot = FindRepoRoot(baseDir);
        string workDir = repoRoot != null
            ? Path.Combine(repoRoot, "scripts", solverTag)
            : Path.Combine(Path.GetTempPath(), "GeotechSlopeApp_" + solverTag);
        Directory.CreateDirectory(workDir);
        return workDir;
    }

    /// <summary>Walk up from <paramref name="startDir"/> looking for CMakeLists.txt + a scripts/ sibling.</summary>
    private static string? FindRepoRoot(string startDir)
    {
        try
        {
            var dir = new DirectoryInfo(Path.GetFullPath(startDir));
            for (int i = 0; i < 8 && dir != null; ++i, dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "CMakeLists.txt")))
                    return dir.FullName;
            }
        }
        catch { }
        return null;
    }

    private const string MissingPythonMessage =
        "Python was not found. Click Browse Python, set XFEM_PYTHON, or install CPython 3.11+.";

    private void RefreshPythonStatus()
    {
        if (PythonProcessRunner.TryResolve(out var pyExe, out _))
            StatusMessage = "Python ready — " + pyExe;
        else
            StatusMessage = MissingPythonMessage;
    }

    private void OnBrowsePython()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Python (python.exe)|python.exe|Executables (*.exe)|*.exe|All (*.*)|*.*",
            FileName = "python.exe",
            CheckFileExists = true,
            Title = "Select CPython 3.11+ (python.exe)"
        };
        var preferred = PythonProcessRunner.ReadPreferredExecutable();
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            dlg.InitialDirectory = Path.GetDirectoryName(preferred);
            dlg.FileName = Path.GetFileName(preferred);
        }

        if (dlg.ShowDialog() != true)
            return;

        PythonProcessRunner.SetPreferredExecutable(dlg.FileName);
        if (PythonProcessRunner.TryResolve(out var pyExe, out _))
        {
            StatusMessage = "Python ready — " + pyExe;
            OutputText += $"[{DateTime.Now:HH:mm:ss}] Using {pyExe}\n";
        }
        else
        {
            StatusMessage = "The selected file is not CPython 3.11+. Choose python.exe from a 3.11+ install.";
            OutputText += $"[{DateTime.Now:HH:mm:ss}] {StatusMessage}\n";
        }
    }

    private async void OnExecuteScript()
    {
        if (IsPythonMode)
            await RunPythonScriptAsync().ConfigureAwait(true);
        else
            await RunSolverFromScriptAsync().ConfigureAwait(true);
    }

    private async Task RunPythonScriptAsync()
    {
        if (!PythonProcessRunner.TryResolve(out var pyExe, out var pyPrefix))
        {
            StatusMessage = MissingPythonMessage;
            OutputText += $"[{DateTime.Now:HH:mm:ss}] {StatusMessage}\n";
            return;
        }

        var clientDir = PythonProcessRunner.ResolveClientDirectory();
        if (clientDir == null)
        {
            StatusMessage = "xfem.py client was not found next to the app or under scripts/xfem_client.";
            OutputText += $"[{DateTime.Now:HH:mm:ss}] {StatusMessage}\n";
            return;
        }

        IsExecuting = true;
        var pipeName = "xfem-" + Guid.NewGuid().ToString("N");
        var workDir = Path.Combine(Path.GetTempPath(), "GeotechSlopeApp_python");
        Directory.CreateDirectory(workDir);
        var scriptPath = Path.Combine(workDir, "script_editor.py");
        try
        {
            File.WriteAllText(scriptPath, PythonProcessRunner.WrapUserScript(PythonScript ?? "", clientDir));
            using var host = new FeProjectRpcHost(new FeProjectApi(_main), pipeName);
            host.Start();
            OutputText += $"[{DateTime.Now:HH:mm:ss}] Running Python against the live GUI...\n";
            StatusMessage = "Running Python...";
            var (code, output) = await PythonProcessRunner.RunAsync(
                pyExe, pyPrefix, scriptPath, pipeName, clientDir).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(output))
                OutputText += output.EndsWith("\n") ? output : output + "\n";
            StatusMessage = code == 0 ? "Python finished." : $"Python exited with code {code}.";
            OutputText += $"[{DateTime.Now:HH:mm:ss}] {StatusMessage}\n";
        }
        catch (Exception ex)
        {
            StatusMessage = "Python error: " + ex.Message;
            OutputText += $"[{DateTime.Now:HH:mm:ss}] {StatusMessage}\n";
        }
        finally
        {
            IsExecuting = false;
        }
    }

    private string ResolveP610ProgramFromScript()
    {
        var srfMode = _main.MaterialPropertiesVM?.IsSrfAnalysisMode == true;
        if (!srfMode && _main.FEAnalysisVM?.HasMohrCoulombJointSlip == true)
            return "p610_t3_elastoplastic";
        if (P610AnalysisOptionsHelper.TryParse(ScriptContent, out var options))
            return P610AnalysisOptionsHelper.ResolveRunnerExecutableForFeAnalysis(options, _main.MeshVM, srfMode);

        var matVm = _main.MaterialPropertiesVM;
        if (matVm != null)
        {
            var built = P610AnalysisOptionsHelper.BuildFromViewModels(matVm, _main.MeshVM);
            return P610AnalysisOptionsHelper.ResolveRunnerExecutableForFeAnalysis(built, _main.MeshVM, srfMode);
        }

        if (ScriptContentIsP610T3Elastic(ScriptContent))
            return "p610_t3_elastic";

        return "p610_t3_elastoplastic";
    }

    private async Task RunFemCppFromScriptAsync(string programId)
    {
        OutputText += $"[{DateTime.Now:HH:mm:ss}] Running fem_cpp ({programId})...\n";
        StatusMessage = $"Running fem_cpp ({programId})...";
        try
        {
            _main.FEAnalysisVM.ResolveCppExecutablePath();
            var workDir = ResolveSolverWorkDir("fem_cpp");
            var baseName = _scriptRunBaseName;
            if (string.Equals(programId, "p64", StringComparison.OrdinalIgnoreCase)
                && _main.MeshVM is { IsDiscretized: true, MeshInformationRows.Count: > 0 }
                && !ShouldPreserveLoadedExplicitDeck())
            {
                TrySyncDiscretizedMeshToScriptDat();
            }

            var runContent = FemCppProgramRouter.EnsureProgramHeader(GetScriptContentForSolverRun(), programId);

            var result = await _p41Runner.RunFemCppFromDatContentAsync(
                programId, runContent, baseName, workDir).ConfigureAwait(true);

            if (!string.IsNullOrWhiteSpace(result.Stdout))
                OutputText += result.Stdout + (result.Stdout.EndsWith("\n") ? "" : "\n");

            var msg = $"fem_cpp {programId}: {result.JsonResult.Nodes.Count} nodes, " +
                      $"{result.JsonResult.Elements.Count} elements, " +
                      $"{result.JsonResult.Stages.Count} stage(s) — Results & Visualization.";
            var artifactSummary = P64ResFileParser.FormatSummaryText(
                result.ResSummary, result.WorkDir, result.BaseName);
            _main.ResultsVM.SetFemCppArtifactSummary(artifactSummary);
            if (result.ResSummary.Trials.Count > 0)
            {
                _main.ResultsVM.ApplyP64ResSummary(result.ResSummary, runContent);
                _main.ResultsVM.ApplyNewmarkResult(_main.LoadConditionsVM.Newmark, _main.ResultsVM.EstimatedSrfFoS);
            }
            else
                _main.ResultsVM.ClearSrfConvergencePlot();
            _main.ResultsVM.LoadP610T3Result(result.JsonResult, msg, datContent: runContent, meshGeometrySource: _main.MeshVM);
            _main.ResultsVM.StageSummary = artifactSummary;

            StatusMessage = msg;
            OutputText += $"[{DateTime.Now:HH:mm:ss}] {StatusMessage}\n";
            OutputText += $"[{DateTime.Now:HH:mm:ss}] Work dir: {workDir}\n";
            _main.SelectedTabIndex = 9;
            _main.ResultsVM.ResultsSubTabIndex = result.ResSummary.Trials.Count > 0 ? 2 : 1;
            if (_main.ResultsVM.HasPseudoStaticResults)
                _main.ResultsVM.VisualizationMode = ResultsViewModel.VisualizationModeVector;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            OutputText += $"[{DateTime.Now:HH:mm:ss}] Error: {ex.Message}\n";
            MessageBox.Show($"fem_cpp ({programId}) run failed:\n\n{ex.Message}",
                "Run fem_cpp", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsExecuting = false;
        }
    }

    private async Task RunP610VariantFromScriptAsync(string programName)
    {
        OutputText += $"[{DateTime.Now:HH:mm:ss}] Running {programName}...\n";
        StatusMessage = $"Running {programName}...";
        try
        {
            var workDir = ResolveSolverWorkDir(programName);
            if (P610AnalysisOptionsHelper.IsSlopeStabilitySrfExecutable(programName)
                && _main.MeshVM is { IsDiscretized: true, MeshInformationRows.Count: > 0 }
                && !ShouldPreserveLoadedExplicitDeck()
                && !IsVerbatimLoadedContent())
            {
                TrySyncDiscretizedMeshToScriptDat();
            }

            var runContent = GetScriptContentForSolverRun();

            var result = await _p41Runner.RunP610T3VariantFromDatContentAsync(programName, runContent, "p610t3_run", workDir).ConfigureAwait(true);
            var msg = $"{programName} complete: {result.Nodes.Count} nodes, {result.Elements.Count} elements, {result.Stages.Count} stage(s) — use Results (stage) & Visualization.";
            _main.ResultsVM.LoadP610T3Result(result, msg, datContent: runContent, meshGeometrySource: _main.MeshVM);
            // Mc-tunnel hardcoded smoke comparison runs first (legacy mc_tunnel_*_smoke
            // .dat files have no expected_*_kPa headers and rely on TryGetSmokeReference).
            // If the .dat declares its own expected_*_kPa values - the case for every
            // NAFEMS suite under Verification/nafems/ - the mc-tunnel path defers and
            // we fall through to the header-driven patch comparison so the Verification
            // tab populates with the correct analytical reference for that .dat.
            if (!_main.VerificationVM.TryAutoRunMcTunnelSmokeComparison(runContent))
                _main.VerificationVM.TryAutoRunPatchComparison(runContent);
            StatusMessage = msg;
            OutputText += $"[{DateTime.Now:HH:mm:ss}] {StatusMessage}\n";
            _main.SelectedTabIndex = 9;
            _main.ResultsVM.ResultsSubTabIndex = 1;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            OutputText += $"[{DateTime.Now:HH:mm:ss}] Error: {ex.Message}\n";
            MessageBox.Show($"{programName} run failed:\n\n{ex.Message}", $"Run {programName}", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsExecuting = false;
        }
    }

    private async Task RunSolverFromScriptAsync()
    {
        if (string.IsNullOrWhiteSpace(ScriptContent))
        {
            StatusMessage = "No .dat content. Define geometry and mesh first.";
            OutputText += $"[{DateTime.Now:HH:mm:ss}] No .dat content to run.\n";
            return;
        }

        P41ExecutableDirectory = _main.FEAnalysisVM.CppExecutableDirectory;
        IsExecuting = true;

        var matVm = _main.MaterialPropertiesVM;
        if (matVm?.IsSrfAnalysisMode == true)
        {
            if (!matVm.IsSrfReadyForRun)
            {
                StatusMessage = "SRF analysis requires a slope failure criterion.";
                OutputText += $"[{DateTime.Now:HH:mm:ss}] SRF mode requires Mohr–Coulomb, Drucker–Prager, Hoek–Brown, or Generalized Hoek–Brown.\n";
                MessageBox.Show(
                    "Slope stability – strength reduction (SRF) requires a slope failure criterion in Material Properties.",
                    "Execute Script",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                IsExecuting = false;
                return;
            }

            if (ScriptContentIsP610T3(ScriptContent)
                || P610AnalysisOptionsHelper.HasAnalysisOptionsBlock(ScriptContent))
            {
                var loadedProgram = ResolveP610ProgramFromScript();
                if (P610AnalysisOptionsHelper.IsSlopeStabilitySrfExecutable(loadedProgram))
                {
                    await RunP610VariantFromScriptAsync(loadedProgram).ConfigureAwait(true);
                    return;
                }

                if (string.Equals(loadedProgram, "p64", StringComparison.OrdinalIgnoreCase))
                {
                    var femProgramId = FemCppProgramRouter.ResolveProgramId(ScriptContent) ?? "p64";
                    await RunFemCppFromScriptAsync(femProgramId).ConfigureAwait(true);
                    return;
                }

                await RunP610VariantFromScriptAsync(loadedProgram).ConfigureAwait(true);
                return;
            }

            if (FemCppProgramRouter.ResolveProgramId(ScriptContent) is { } loadedFemProgram)
            {
                await RunFemCppFromScriptAsync(loadedFemProgram).ConfigureAwait(true);
                return;
            }

            RefreshDatContent();
            var srfOptions = P610AnalysisOptionsHelper.BuildFromViewModels(matVm, _main.MeshVM);
            var srfProgram = P610AnalysisOptionsHelper.ResolveRunnerExecutableForFeAnalysis(
                srfOptions, _main.MeshVM, srfAnalysisMode: true);
            if (P610AnalysisOptionsHelper.IsSlopeStabilitySrfExecutable(srfProgram))
            {
                await RunP610VariantFromScriptAsync(srfProgram).ConfigureAwait(true);
                return;
            }

            if (string.Equals(srfProgram, "p64", StringComparison.OrdinalIgnoreCase))
            {
                await RunFemCppFromScriptAsync("p64").ConfigureAwait(true);
                return;
            }
        }

        // Route to a p610_t3_* / slope_stability_* solver whenever EITHER the legacy `! solver:p610_t3` tag
        // is present OR the buffer carries an AnalysisOptions block (failure_criterion +
        // material_type). The block is the authoritative source for the .exe name, so a
        // .dat without the tag — but with the block — still lands on the right
        // library/solver/<exe>.exe instead of falling through to the Material Properties
        // p51/p41 fallback below.
        bool isP610 = ScriptContentIsP610T3(ScriptContent)
                      || P610AnalysisOptionsHelper.HasAnalysisOptionsBlock(ScriptContent);
        if (isP610)
        {
            var programName = ResolveP610ProgramFromScript();
            if (P610AnalysisOptionsHelper.IsSlopeStabilitySrfExecutable(programName))
            {
                await RunP610VariantFromScriptAsync(programName).ConfigureAwait(true);
                return;
            }

            if (string.Equals(programName, "p64", StringComparison.OrdinalIgnoreCase))
            {
                var femProgramId = FemCppProgramRouter.ResolveProgramId(ScriptContent) ?? "p64";
                await RunFemCppFromScriptAsync(femProgramId).ConfigureAwait(true);
                return;
            }

            await RunP610VariantFromScriptAsync(programName).ConfigureAwait(true);
            return;
        }

        // fem_cpp geotech (legacy p64 parametric Q8 MC decks without AnalysisOptions block)
        var femOnly = FemCppProgramRouter.ResolveProgramId(ScriptContent);
        if (femOnly != null)
        {
            await RunFemCppFromScriptAsync(femOnly).ConfigureAwait(true);
            return;
        }

        // Route to p51 if content starts with 'plane'/'axisymmetric', or if MaterialProperties says so
        bool runAsP51 = _main.FEAnalysisVM?.HasMohrCoulombJointSlip != true
            && (ScriptContentIsP51(ScriptContent)
                || FEAnalysisViewModel.UseP51PlaneStrainScriptFormat(_main.MaterialPropertiesVM));

        if (runAsP51)
        {
            OutputText += $"[{DateTime.Now:HH:mm:ss}] Running p51 (plane-strain)...\n";
            StatusMessage = "Running p51...";
            try
            {
                var workDir = ResolveSolverWorkDir("p51");
                var result = await _p41Runner.RunP51FromDatContentAsync(ScriptContent, "p51_run", workDir).ConfigureAwait(true);
                ApplyP51Result(result);
                StatusMessage = $"p51 complete: {result.Nodes.Count} nodes, {result.Elements.Count} elements — results in Results & Visualization tab.";
                OutputText += $"[{DateTime.Now:HH:mm:ss}] {StatusMessage}\n";
                _main.SelectedTabIndex = 9;   // navigate to Results & Visualization
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                OutputText += $"[{DateTime.Now:HH:mm:ss}] Error: {ex.Message}\n";
                MessageBox.Show($"p51 run failed:\n\n{ex.Message}", "Run p51", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsExecuting = false;
            }
            return;
        }

        OutputText += $"[{DateTime.Now:HH:mm:ss}] Running p41...\n";
        StatusMessage = "Running p41...";

        try
        {
            var workDir = ResolveSolverWorkDir("p41");
            var result = await _p41Runner.RunP41Async(ScriptContent, "p41_run", workDir).ConfigureAwait(true);

            ApplyP41Result(result);
            StatusMessage = $"p41 complete: {result.Nodes.Count} nodes, {result.Elements.Count} elements — results in Results & Visualization tab.";
            OutputText += $"[{DateTime.Now:HH:mm:ss}] {StatusMessage}\n";
            _main.SelectedTabIndex = 9;   // navigate to Results & Visualization
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            OutputText += $"[{DateTime.Now:HH:mm:ss}] Error: {ex.Message}\n";
            MessageBox.Show($"p41 run failed:\n\n{ex.Message}", "Run p41", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsExecuting = false;
        }
    }

    private void ApplyP51Result(P51ResFileParser.P51ParseResult result)
    {
        var resVm = _main.ResultsVM;
        resVm.ClearP610T3StagedData();
        resVm.FENodes = result.Nodes.ToList();
        resVm.FEElements = result.Elements.ToList<ElementBase>();
        resVm.FeElementStresses = result.ElementStresses.ToList();
        resVm.FeNodalDisplacements = result.Nodes.Select(n =>
        {
            double u = n.Displacements.Length > 0 ? n.Displacements[0] : 0;
            double v = n.Displacements.Length > 1 ? n.Displacements[1] : 0;
            return (object)new
            {
                NodeId = n.Id,
                X = n.X,
                Y = n.Y,
                U = u,
                V = v,
                DisplacementMagnitude = Math.Sqrt(u * u + v * v)
            };
        }).ToList<object>();
        resVm.FeStatusMessage = $"p51 Results: {result.Nodes.Count} nodes, {result.Elements.Count} elements.";
        resVm.StagedAnalysisResult = null;
    }

    private void ApplyP41Result(P41ResFileParser.P41ParseResult result)
    {
        var resVm = _main.ResultsVM;
        resVm.ClearP610T3StagedData();
        resVm.FENodes = result.Nodes.ToList();
        resVm.FEElements = result.Elements.ToList();
        resVm.FeElementStresses = result.ElementStresses.ToList();
        resVm.FeNodalDisplacements = result.Nodes.Select(n =>
        {
            double u = n.Displacements.Length > 0 ? n.Displacements[0] : 0;
            double v = n.Displacements.Length > 1 ? n.Displacements[1] : 0;
            return (object)new
            {
                NodeId = n.Id,
                X = n.X,
                Y = n.Y,
                U = u,
                V = v,
                DisplacementMagnitude = Math.Sqrt(u * u + v * v)
            };
        }).ToList<object>();
        resVm.FeStatusMessage = $"p41 Results: {result.Nodes.Count} nodes, {result.Elements.Count} elements.";
        resVm.StagedAnalysisResult = null;
    }

    // ─── p610_t3 wizard internals ───────────────────────────────────────────

    /// <summary>
    /// When a T3 mesh first appears in a 2D project and the editor still holds the default
    /// p51 tunnel example (or any non-p610_t3 content), replace the buffer with a fresh
    /// p610_t3 (or p610_t3_elastic) skeleton so the wizard is ready to fill in sections.
    /// The elastic skeleton is chosen automatically when Material Properties says
    /// Failure criterion = Elastic and Material type = Isotropic.
    /// </summary>
    /// <summary>
    /// True when the current buffer is a fully-formed p610_t3-family .dat loaded verbatim
    /// (from a file, paste, or reference/smoke test under Verification/) rather than the
    /// wizard's 7-section skeleton.
    ///
    /// Detection rule: the buffer lacks the wizard's <c>&lt;BEGIN:Header&gt;</c> sentinel
    /// (which every skeleton emits as section 1) but is recognizable as a p610-family dat
    /// via either a <c>&lt;BEGIN:AnalysisOptions&gt;</c> block or the legacy
    /// <c>! &lt;solver:p610_t3*&gt;</c> routing tag. Such content must be executed exactly
    /// as loaded — in particular, <see cref="MaybeAutoSwitchToP610T3"/> must not overwrite
    /// it with a fresh skeleton, and <see cref="AutoRefreshP610DatSections"/> must not
    /// rebuild sections from VM state (which would, e.g., drop a traction group and replace
    /// it with a default <c>&lt;BEGIN:Layers&gt;</c> excavation block, yielding all-zero
    /// results when executed).
    ///
    /// Reset (<see cref="OnClearScript"/>) is the explicit escape hatch back to wizard mode.
    /// </summary>
    private bool IsVerbatimLoadedContent()
    {
        var c = _scriptContent;
        if (string.IsNullOrEmpty(c)) return false;

        // A wizard-emitted buffer always carries <BEGIN:Header> as its first fenced section,
        // so its presence is a reliable "this is a skeleton" signal.
        if (c.IndexOf("<BEGIN:Header>", StringComparison.OrdinalIgnoreCase) >= 0) return false;

        // Only protect buffers that routing can recognize as p610_t3-family. Anything else
        // (p51 sample, p41 rod, blank buffer, etc.) keeps the pre-existing auto-refresh flow.
        return c.IndexOf("<BEGIN:AnalysisOptions>", StringComparison.OrdinalIgnoreCase) >= 0
               || c.IndexOf("! <solver:p610_t3", StringComparison.OrdinalIgnoreCase) >= 0
               || c.IndexOf("! solver:p610_t3", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void MaybeAutoSwitchToP610T3()
    {
        if (!ShouldUseP610T3Format()) return;

        // A user-loaded .dat (reference, smoke test, or hand-written) must survive the
        // auto-switch. The Reset button still restores the wizard skeleton on demand.
        if (IsVerbatimLoadedContent()) return;

        bool wantElastic = ShouldUseP610T3ElasticFormat();

        // Already in the correct skeleton — nothing to do.
        if (wantElastic && IsP610T3ElasticMode) return;
        if (!wantElastic && IsP610T3Mode && !IsP610T3ElasticMode) return;

        _isManualContent = true;
        ScriptContent = wantElastic
            ? P610T3ElasticDatContentBuilder.SkeletonHeader()
            : P610T3DatContentBuilder.SkeletonHeader();
        var label = wantElastic ? "p610_t3_elastic" : "p610_t3";
        StatusMessage = $"T3 mesh detected — switched editor to {label} skeleton. Click 1..7 to emit sections.";
        OutputText += $"[{DateTime.Now:HH:mm:ss}] Auto-switched to {label} skeleton (T3 mesh detected).\n";
    }

    private void EmitSection(P610T3DatContentBuilder.Section section)
    {
        try
        {
            // Dispatch to the linear-elastic dialect when either the buffer is already
            // elastic, or the user's material settings ask for elastic emit.
            bool elastic = ShouldUseP610T3ElasticFormat()
                || (IsP610T3ElasticMode && _main.FEAnalysisVM?.HasMohrCoulombJointSlip != true);

            string body = elastic
                ? section switch
                {
                    P610T3DatContentBuilder.Section.Header        => P610T3ElasticDatContentBuilder.BuildHeader(_main.MeshVM, _main.MaterialPropertiesVM),
                    P610T3DatContentBuilder.Section.Materials     => P610T3ElasticDatContentBuilder.BuildMaterials(_main.MaterialPropertiesVM),
                    P610T3DatContentBuilder.Section.Mesh          => P610T3ElasticDatContentBuilder.BuildMesh(_main.MeshVM, _main.MaterialPropertiesVM),
                    P610T3DatContentBuilder.Section.Bcs           => P610T3ElasticDatContentBuilder.BuildBcs(_main.BoundaryConditionsVM, _main.MeshVM),
                    P610T3DatContentBuilder.Section.OutputNodes   => P610T3ElasticDatContentBuilder.BuildOutputNodes(_main.BoundaryConditionsVM, _main.MeshVM),
                    P610T3DatContentBuilder.Section.SolverControl => P610T3ElasticDatContentBuilder.BuildSolverControl(),
                    P610T3DatContentBuilder.Section.Layers        => P610T3ElasticDatContentBuilder.BuildTractions(_main.MeshVM, _main.BoundaryConditionsVM, _main.LoadConditionsVM?.FieldStress, _main.MaterialPropertiesVM),
                    _ => throw new ArgumentOutOfRangeException(nameof(section)),
                }
                : section switch
                {
                    P610T3DatContentBuilder.Section.Header        => P610T3DatContentBuilder.BuildHeader(_main.MeshVM, _main.MaterialPropertiesVM),
                    P610T3DatContentBuilder.Section.Materials     => P610T3DatContentBuilder.BuildMaterials(_main.MaterialPropertiesVM),
                    P610T3DatContentBuilder.Section.Mesh          => P610T3DatContentBuilder.BuildMesh(_main.MeshVM, _main.MaterialPropertiesVM),
                    P610T3DatContentBuilder.Section.Bcs           => P610T3DatContentBuilder.BuildBcs(_main.BoundaryConditionsVM, _main.MeshVM),
                    P610T3DatContentBuilder.Section.OutputNodes   => P610T3DatContentBuilder.BuildOutputNodes(_main.BoundaryConditionsVM, _main.MeshVM),
                    P610T3DatContentBuilder.Section.SolverControl => P610T3DatContentBuilder.BuildSolverControl(_main.MeshVM, _main.StagedConstructionVM, _main.FEAnalysisVM),
                    P610T3DatContentBuilder.Section.Layers        => P610T3DatContentBuilder.BuildLayers(_main.MeshVM, _main.StagedConstructionVM, _main.FEAnalysisVM, _main.LoadConditionsVM),
                    _ => throw new ArgumentOutOfRangeException(nameof(section)),
                };

            // If the user is emitting into a non-p610_t3 buffer (e.g. the default p51 tunnel
            // sample) and they want the elastic dialect, seed the elastic skeleton first so the
            // sentinel markers exist for MergeSection to swap into.
            if (elastic && !IsP610T3ElasticMode)
            {
                _isManualContent = true;
                ScriptContent = P610T3ElasticDatContentBuilder.SkeletonHeader();
            }

            _isManualContent = true;
            ScriptContent = P610T3DatContentBuilder.MergeSection(ScriptContent, section, body);

            var summary = DescribeSection(section, elastic);
            StatusMessage = $"Emitted {section}: {summary}";
            OutputText += $"[{DateTime.Now:HH:mm:ss}] Emitted {section}: {summary}\n";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Emit {section} failed: {ex.Message}";
            OutputText += $"[{DateTime.Now:HH:mm:ss}] Emit {section} failed: {ex.Message}\n";
        }
    }

    private void OnEmitAllP610T3()
    {
        foreach (var s in P610T3DatContentBuilder.SectionOrder)
            EmitSection(s);
        var label = (IsP610T3ElasticMode || ShouldUseP610T3ElasticFormat()) ? "p610_t3_elastic" : "p610_t3";
        StatusMessage = $"Emitted all {label} sections.";
    }

    private string DescribeSection(P610T3DatContentBuilder.Section section, bool elastic = false)
    {
        var meshRows = _main.MeshVM?.MeshInformationRows;
        int nels = meshRows?.Count ?? 0;
        var coordNodes = new HashSet<int>();
        if (meshRows != null)
            foreach (var r in meshRows)
            {
                coordNodes.Add(r.Node1);
                coordNodes.Add(r.Node2);
                coordNodes.Add(r.Node3);
            }
        int nn = coordNodes.Count > 0 ? coordNodes.Max() : 0;
        int npTypes = Math.Max(1, _main.MaterialPropertiesVM?.Materials?.Count ?? 0);
        int bcRows = _main.BoundaryConditionsVM?.BoundaryConditions?.Count ?? 0;

        return section switch
        {
            P610T3DatContentBuilder.Section.Header        => $"nels={nels}, nn={nn}, np_types={npTypes}",
            P610T3DatContentBuilder.Section.Materials     => $"{npTypes} material type(s)",
            P610T3DatContentBuilder.Section.Mesh          => $"{nels} T3 elements, {nn} nodes",
            P610T3DatContentBuilder.Section.Bcs           => $"{bcRows} BC row(s)",
            P610T3DatContentBuilder.Section.OutputNodes   => "output node list written",
            P610T3DatContentBuilder.Section.SolverControl => elastic
                ? "elastic stub (no tol/incs/layers in p610_t3_elastic)"
                : "tol/limit/incs/layers written",
            P610T3DatContentBuilder.Section.Layers        => elastic
                ? "far-field traction edges written"
                : "excavation layers written",
            _ => string.Empty,
        };
    }

    private void RaiseWizardState()
    {
        OnPropertyChanged(nameof(IsP610T3Mode));
        OnPropertyChanged(nameof(ShowP610Wizard));
        OnPropertyChanged(nameof(IsP610T3ElasticMode));
        OnPropertyChanged(nameof(CanEmitHeader));
        OnPropertyChanged(nameof(CanEmitMaterials));
        OnPropertyChanged(nameof(CanEmitMesh));
        OnPropertyChanged(nameof(CanEmitBcs));
        OnPropertyChanged(nameof(CanEmitOutputNodes));
        OnPropertyChanged(nameof(CanEmitSolverControl));
        OnPropertyChanged(nameof(CanEmitLayers));
        OnPropertyChanged(nameof(CanEmitAll));
        OnPropertyChanged(nameof(SectionHeaderDone));
        OnPropertyChanged(nameof(SectionMaterialsDone));
        OnPropertyChanged(nameof(SectionMeshDone));
        OnPropertyChanged(nameof(SectionBcsDone));
        OnPropertyChanged(nameof(SectionOutputNodesDone));
        OnPropertyChanged(nameof(SectionSolverControlDone));
        OnPropertyChanged(nameof(SectionLayersDone));
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnLoadScript()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = IsPythonMode
                ? "Python (*.py)|*.py|All (*.*)|*.*"
                : "Data files (*.dat)|*.dat|All (*.*)|*.*",
            DefaultExt = IsPythonMode ? ".py" : ".dat"
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var text = File.ReadAllText(dlg.FileName);
            if (IsPythonMode || dlg.FileName.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
            {
                EditorLanguage = ScriptEditorLanguage.Python;
                PythonScript = text;
                StatusMessage = $"Loaded {Path.GetFileName(dlg.FileName)}";
                return;
            }

            ApplyLoadedDatContent(text, Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    private void OnLoadSlope64Ex1()
    {
        try
        {
            var content = TryReadSlope64Ex1DatContent(out var sourceLabel);
            if (content == null)
            {
                MessageBox.Show(
                    "Could not find the SLOPE64 Example 1 .dat file.\n\n" +
                    $"Expected at: {Slope64Ex1RelativePath} under the repository root.",
                    "Example: SLOPE64 Ex1",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                StatusMessage = "SLOPE64 Example 1 .dat not found.";
                return;
            }

            ApplyLoadedDatContent(content, sourceLabel ?? Slope64Ex1DisplayName);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    private static string? ResolveSlope64Ex1DatPath()
    {
        var rel = Slope64Ex1RelativePath.Replace('/', Path.DirectorySeparatorChar);
        var probes = new List<string>();

        var repo = FindRepoRoot(AppContext.BaseDirectory);
        if (repo != null)
            probes.Add(Path.Combine(repo, rel));

        var cwd = Directory.GetCurrentDirectory();
        probes.Add(Path.Combine(cwd, rel));

        try
        {
            var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrEmpty(asmDir))
            {
                var fromBin = FindRepoRoot(asmDir);
                if (fromBin != null && !probes.Contains(Path.Combine(fromBin, rel), StringComparer.OrdinalIgnoreCase))
                    probes.Add(Path.Combine(fromBin, rel));
            }
        }
        catch { }

        return probes.FirstOrDefault(File.Exists);
    }

    private static string? TryReadSlope64Ex1DatContent(out string? sourceLabel)
    {
        sourceLabel = null;
        var path = ResolveSlope64Ex1DatPath();
        if (path != null)
        {
            sourceLabel = Path.GetFileName(path);
            return File.ReadAllText(path);
        }

        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(Slope64Ex1EmbeddedResourceName);
        if (stream == null)
            return null;

        using var reader = new StreamReader(stream);
        sourceLabel = "embedded SLOPE64 Ex1";
        return reader.ReadToEnd();
    }

    private void ApplyLoadedDatContent(string content, string displayName)
    {
        try
        {
            ApplyLoadedDatContentCore(content, displayName);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading script: {ex.Message}";
            MessageBox.Show(
                $"Could not load the script file:\n\n{ex.Message}",
                "Load Script",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ApplyLoadedDatContentCore(string content, string displayName)
    {
        _isManualContent = true;
        using (SuspendDatRefresh())
        {
            _suppressAutoRefresh = true;
            var prevDeferFullImport = _deferLoadedDatFullImport;
            _deferLoadedDatFullImport = ScriptContentIsP610T3(content);
            try
            {
                ScriptContent = content;
                SyncFromScript();

                if (displayName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
                    _scriptRunBaseName = Path.GetFileNameWithoutExtension(displayName);
                else if (FemCppProgramRouter.ResolveProgramId(content) is { } pid)
                    _scriptRunBaseName = pid + "_run";

                if (!ScriptContentIsP610T3(content))
                {
                    StatusMessage = $"Loaded {displayName}";
                    _main.FEAnalysisVM.RefreshRunPseudoStaticDisplay();
                    return;
                }

                bool meshLoaded = _main.MeshVM.LoadMeshFromDatContent(content);
                if (!meshLoaded
                    && DatMeshBoundaryImporter.TryImportToViewModels(
                        content,
                        _main.MeshVM.UserDefinedMeshVM,
                        _main.BoundaryConditionsVM,
                        out _))
                {
                    _main.MeshVM.MeshPopupSelectedTabIndex = MeshViewModel.MeshPopupUserDefinedTabIndex;
                    _main.BoundaryConditionsVM.ShowMarkersAfterMeshImport();
                    _main.MeshVM.NotifyMeshCanvasDataChanged();
                    meshLoaded = _main.MeshVM.IsDiscretized && _main.MeshVM.MeshInformationRows.Count > 0;
                }

                if (!meshLoaded)
                {
                    StatusMessage = $"Loaded {displayName} — mesh parse failed (check .dat format)";
                    return;
                }

                var summary = P610T3DatImporter.Import(
                    content,
                    _main.MaterialPropertiesVM,
                    _main.BoundaryConditionsVM,
                    _main.LoadConditionsVM.FieldStress,
                    _main.GeometryDefinitionVM,
                    _main.FEAnalysisVM);

                var verbatimSuffix = IsVerbatimLoadedContent()
                    ? " — executed verbatim (auto-sync suspended; use Reset to rebuild from tabs)"
                    : "";

                if (summary != null)
                {
                    if (summary.ExcavationLayers.Count > 0)
                    {
                        var fe = _main.FEAnalysisVM;
                        fe.StagedExcavationEnabled = true;
                        fe.StageDefinitions.Clear();

                        fe.StageDefinitions.Add(new StageDefinition
                        {
                            StageNumber = 1,
                            StageName   = "Initial Stress",
                            StageType   = "Initial Stress",
                        });

                        for (int i = 0; i < summary.ExcavationLayers.Count; i++)
                        {
                            fe.StageDefinitions.Add(new StageDefinition
                            {
                                StageNumber = 2 + i,
                                StageName   = $"Excavate {i + 1}",
                                StageType   = "Excavation",
                            });
                        }

                        _main.SyncFooterWithStagedAnalysisGrid();

                        var sc = _main.StagedConstructionVM;
                        for (int i = 0; i < summary.ExcavationLayers.Count; i++)
                        {
                            var stage = sc.Stages.FirstOrDefault(s => s.StageNumber == 2 + i);
                            if (stage == null) continue;
                            stage.ExcavatedElementIds.Clear();
                            stage.ExcavatedElementIds.AddRange(summary.ExcavationLayers[i]);
                        }
                        sc.NotifyExcavatedElementIdsChanged();
                    }

                    var fsTag = summary.HasFieldStress ? ", FieldStress loaded" : "";
                    var layerTag = summary.ExcavationLayers.Count > 0
                        ? $", {summary.ExcavationLayers.Count} excavation layer(s)"
                        : "";
                    StatusMessage =
                        $"Loaded {displayName} — " +
                        $"{summary.NElements} elements, {summary.NNodes} nodes, " +
                        $"{summary.NPTypes} material(s), {summary.NBoundaryConditions} BC(s), " +
                        $"{summary.NGeomVertices} boundary vertices{fsTag}{layerTag}{verbatimSuffix}";
                }
                else
                {
                    StatusMessage =
                        $"Loaded {displayName} — mesh OK but metadata/material parse failed (check .dat format){verbatimSuffix}";
                }

                _main.LoadConditionsVM.NotifyLoadsReplacedFromProjectFile();
            }
            finally
            {
                _deferLoadedDatFullImport = prevDeferFullImport;
                _suppressAutoRefresh = false;
            }
        }
    }

    private void OnSaveScript()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = IsPythonMode
                ? "Python (*.py)|*.py|All (*.*)|*.*"
                : "Data files (*.dat)|*.dat|All (*.*)|*.*",
            DefaultExt = IsPythonMode ? ".py" : ".dat",
            FileName = IsPythonMode ? "script.py" : "p41_1.dat"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                File.WriteAllText(dlg.FileName, IsPythonMode ? PythonScript : ScriptContent);
                StatusMessage = $"Saved {dlg.FileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
        }
    }

    private void OnClearScript()
    {
        if (IsPythonMode)
        {
            PythonScript = DefaultPythonScript;
            StatusMessage = "Python example reset.";
            return;
        }

        // In p610_t3 mode (or whenever a T3 mesh is available), Reset rewinds to a fresh
        // skeleton so the user can step through the wizard again without losing the solver.
        if (IsP610T3Mode || ShouldUseP610T3Format())
        {
            bool elastic = ShouldUseP610T3ElasticFormat() || (IsP610T3ElasticMode && !ShouldUseP610T3Format());
            _isManualContent = true;
            ScriptContent = elastic
                ? P610T3ElasticDatContentBuilder.SkeletonHeader()
                : P610T3DatContentBuilder.SkeletonHeader();
            var label = elastic ? "p610_t3_elastic" : "p610_t3";
            StatusMessage = $"{label} skeleton reset — click 1..7 to emit sections.";
            return;
        }

        _isManualContent = false;
        RefreshDatContent();
        StatusMessage = "Reset to current geometry/materials/loads";
    }

    private void OnCopyOutput()
    {
        try
        {
            Clipboard.SetText(OutputText);
            StatusMessage = "Output copied to clipboard";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Copy failed: {ex.Message}";
        }
    }

    private void OnClearOutput()
    {
        OutputText = "";
        StatusMessage = "Output cleared";
    }

    /// <summary>Restores Script Editor to the factory default p51 tunnel deck (File → New Project).</summary>
    public void ResetToApplicationDefault()
    {
        _suppressAutoRefresh = true;
        _isManualContent = true;
        ScriptContent = DefaultTunnelDatContent;
        PythonScript = DefaultPythonScript;
        EditorLanguage = ScriptEditorLanguage.Dat;
        OutputText = "";
        StatusMessage = "p51 plane-strain .dat — circular tunnel example loaded. Press Execute Script to run.";
        _scriptRunBaseName = "fe_script_run";
        _suppressAutoRefresh = false;
        SyncFromScript();
    }

    private const string DefaultPythonScript =
        """
        # Simple homogeneous slope. Drives the live GUI; Run FE uses the generated .dat.
        project.clear()
        project.set_title("Python slope")
        project.fe.set_ssr(True)

        # Geometry (m): base 40, toe (40,10), crest (20,20)
        project.geometry.set_outline([
            (0, 0), (40, 0), (40, 10), (20, 20), (0, 20),
        ])

        # Material (Mohr–Coulomb)
        project.materials.add("Clay", c_kpa=5, phi_deg=20, e_kpa=1e5, nu=0.3, gamma=20)

        # Mesh
        project.mesh.generate(element="Q4", size=2.0)

        # BCs: left/right Roller X, base Roller Y, corners Fixed
        project.bcs.apply_slope_defaults()

        # Strength reduction
        status = project.fe.run()
        print(status)
        print("srf", project.fe.srf())
        """;

    /// <summary>Default p51 plane-strain .dat: circular tunnel 5 m diameter, 20x20 m domain.</summary>
    private static string DefaultTunnelDatContent =>
        """
        ! ============================================================
        ! Circular tunnel excavation — 5 m diameter (r = 2.5 m)
        ! Plane strain, 2D FE analysis (p51 solver)
        ! ============================================================
        !
        ! Domain: 20 m x 20 m  (X: 0..20, Y: 0..-20)
        ! Tunnel centre at (10, -10), radius = 2.5 m
        ! Mesh: 10x10 quads → 200 triangles, 121 nodes
        !
        ! Material 1 – Soil:      E = 50000 kPa, nu = 0.30
        ! Material 2 – Excavated: E = 1e-3  kPa, nu = 0.30
        !
        ! BCs: roller on left/right (fix X), roller on bottom (fix Y)
        ! Loads: in-situ stress release at 8 perimeter nodes (σ_v0 = 200 kPa)
        ! ============================================================

        'plane'
        'triangle'   3   'x'
        10  10  1  2

        ! Material 1 — Soil host (E = 50000 kPa, nu = 0.30)
        5.0e+04  0.30

        ! Material 2 — Excavated zone (near-zero stiffness)
        1.0e-03  0.30

        ! etype — 200 values (1=soil, 2=excavated tunnel elements)
        1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1
        1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1
        1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1
        1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1
        1 1 1 1 1 1 1 1 2 2 2 2 1 1 1 1 1 1 1 1
        1 1 1 1 1 1 1 1 2 2 2 2 1 1 1 1 1 1 1 1
        1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1
        1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1
        1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1
        1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1

        ! x_coords — 11 values, 0 to 20 m
        0.0  2.0  4.0  6.0  8.0  10.0  12.0  14.0  16.0  18.0  20.0

        ! y_coords — 11 values, 0 to -20 m
        0.0  -2.0  -4.0  -6.0  -8.0  -10.0  -12.0  -14.0  -16.0  -18.0  -20.0

        ! Boundary conditions (31 restrained nodes)
        ! Left edge fix X, Right edge fix X, Bottom fix Y
        31
          1  0  1
         12  0  1
         23  0  1
         34  0  1
         45  0  1
         56  0  1
         67  0  1
         78  0  1
         89  0  1
        100  0  1
        111  0  0
         11  0  1
         22  0  1
         33  0  1
         44  0  1
         55  0  1
         66  0  1
         77  0  1
         88  0  1
         99  0  1
        110  0  1
        121  0  0
        112  1  0
        113  1  0
        114  1  0
        115  1  0
        116  1  0
        117  1  0
        118  1  0
        119  1  0
        120  1  0

        ! Nodal loads — in-situ stress release at tunnel perimeter
        ! σ_v0 = 200 kPa, 8 nodes, tributary length = 2 m
        8
         50   -141.0   141.0
         51      0.0   200.0
         52    141.0   141.0
         61   -200.0     0.0
         63    200.0     0.0
         72   -141.0  -141.0
         73      0.0  -200.0
         74    141.0  -141.0

        ! Fixed freedoms
        0
        """;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
