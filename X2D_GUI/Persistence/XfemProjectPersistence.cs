using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.Analysis;
using GeotechSlopeApp.Models.FieldStations;
using GeotechSlopeApp.Models.Wave3;
using GeotechSlopeApp.Models.Geometry;
using GeotechSlopeApp.Models.Support;
using GeotechSlopeApp.Services.GeometryImport;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Persistence;

/// <summary>
/// Native project format (.xfem): JSON document with all major application state (tabs).
/// </summary>
public static class XfemProjectPersistence
{
    public const string FileExtension = ".xfem";
    /// <summary>1.2 adds user-defined mesh tables, UI subtab indices, SRF controls, verification inputs, full staged-construction stages, and FE run snapshots. 1.1 adds surface excavation history and extended mesh-popup UI. 1.3 adds arc segment metadata, surface assignedMaterialId, and reliable geometry flush on save. 1.8 adds kernel booleans, geological features, and project CRS. 1.9 adds georeferenced slope-image underlay (sidecar PNG + calibration). 1.10 adds Field Stations evidence, snap settings, and optional joint-network definitions (nullable; older 1.10 files load without them). 1.11 adds Wave 3 delivery: probabilistic settings, analysis cases, and report options.</summary>
    public const string CurrentFormatVersion = "1.11";

    /// <summary>Material Properties main tab index (must match <see cref="MainWindowViewModel"/>).</summary>
    private const int MaterialPropertiesMainTabIndex = 2;

    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static void Save(MainWindowViewModel vm, string path)
    {
        if (vm.MaterialPropertiesVM?.IsSrfAnalysisMode == true
            && vm.MeshVM is { IsDiscretized: true, MeshInformationRows.Count: > 0 })
        {
            vm.ScriptVM.TrySyncDiscretizedMeshToScriptDat();
        }

        FlushPendingTabStateBeforeSave(vm);
        ReconcileSrfRoutingBeforePersist(vm);
        PersistSlopeImageSidecar(vm, path);
        var doc = Capture(vm, projectPath: path);
        var json = JsonSerializer.Serialize(doc, JsonOptions);
        File.WriteAllText(path, json);
        WriteExcavationDiagnostic(path, "SAVE", vm, doc);
    }

    public static void Load(MainWindowViewModel vm, string path)
    {
        var json = File.ReadAllText(path);
        var doc = JsonSerializer.Deserialize<XfemDocument>(json, JsonOptions)
                  ?? throw new InvalidDataException("Invalid .xfem file (empty document).");
        if (!IsSupportedFormatVersion(doc.FormatVersion))
            throw new InvalidDataException(
                $"Unsupported .xfem format version: {doc.FormatVersion}. Supported: 1.0, 1.1, 1.2, 1.3, 1.8, 1.9, 1.10, or 1.11.");
        if (doc.ProjectSettings == null || doc.Geometry == null)
            throw new InvalidDataException("Invalid .xfem file: missing projectSettings or geometry.");
        WriteExcavationDiagnostic(path, "LOAD-PRE", vm, doc);
        try
        {
            vm.ScriptVM.BeginApplyProjectFile();
            Apply(vm, doc, path);
        }
        finally
        {
            vm.ScriptVM.EndApplyProjectFile();
        }

        // Manual SRF decks are stored verbatim in .xfem — skip mesh→script rebuild on open
        // (avoids expensive resync and ApplyElementTypeFromScript feedback loops).
        var restoredManualScript = doc.Script is { IsManualContent: true }
            && !string.IsNullOrWhiteSpace(doc.Script.ScriptContent);
        if (!restoredManualScript)
            vm.ScriptVM.RefreshDatContent();
        ReconcileSrfRoutingAfterLoad(vm);
        WriteExcavationDiagnostic(path, "LOAD-POST", vm, doc);
    }

    /// <summary>
    /// After .xfem load: fix SRF + Elastic global criterion from library/script, then patch AnalysisOptions.
    /// </summary>
    private static void ReconcileSrfRoutingAfterLoad(MainWindowViewModel vm)
    {
        var mat = vm.MaterialPropertiesVM;
        if (mat?.IsSrfAnalysisMode != true)
            return;

        var script = vm.ScriptVM.ScriptContent;
        if (mat.ReconcileSrfFailureCriterion(script))
            vm.ScriptVM.SyncAnalysisOptionsToScript();
    }

    /// <summary>
    /// Before .xfem save: ensure SRF projects persist a slope failure criterion, not Elastic.
    /// </summary>
    private static void ReconcileSrfRoutingBeforePersist(MainWindowViewModel vm)
    {
        var mat = vm.MaterialPropertiesVM;
        if (mat?.IsSrfAnalysisMode != true)
            return;

        var script = vm.ScriptVM.ScriptContent;
        if (mat.ReconcileSrfFailureCriterion(script))
            vm.ScriptVM.SyncAnalysisOptionsToScript();
    }

    /// <summary>Full in-memory snapshot of all tab state (used by workspace slot switching).</summary>
    public static XfemDocument CaptureSnapshot(MainWindowViewModel vm) => Capture(vm);

    /// <summary>JSON snapshot for dirty-state comparison (no file I/O).</summary>
    public static string SerializeSnapshotForDirtyCheck(MainWindowViewModel vm)
        => JsonSerializer.Serialize(CaptureSnapshot(vm), JsonOptions);

    /// <summary>Restores all tab state from a snapshot (used by workspace slot switching).</summary>
    public static void ApplyDocument(MainWindowViewModel vm, XfemDocument doc)
    {
        try
        {
            vm.ScriptVM.BeginApplyProjectFile();
            Apply(vm, doc);
        }
        finally
        {
            vm.ScriptVM.EndApplyProjectFile();
        }
    }

    /// <summary>Model-focused snapshot for undo/redo (excludes solver run artifacts and script console output).</summary>
    public static XfemDocument CaptureModelSnapshot(MainWindowViewModel vm, bool skipMaintenance = false)
    {
        var doc = Capture(vm, skipMaintenance);
        if (doc.Results != null)
        {
            doc.Results.FeRun = null;
            doc.Results.FeStatusMessage = null;
        }
        if (doc.Script != null)
        {
            doc.Script.OutputText = null;
            doc.Script.StatusMessage = null;
        }
        return doc;
    }

    /// <summary>Restore model state from an undo snapshot; optionally keep tab navigation and results view shell.</summary>
    public static void ApplyModelSnapshot(MainWindowViewModel vm, XfemDocument doc, bool preserveUiShell = true)
    {
        int? tabIndex = null;
        bool? zoomPan = null;
        int? resultsSubTab = null;
        string? visualizationMode = null;
        string? feStatusMessage = null;

        if (preserveUiShell)
        {
            tabIndex = vm.SelectedTabIndex;
            zoomPan = vm.ZoomPanModeEnabled;
            resultsSubTab = vm.ResultsVM.ResultsSubTabIndex;
            visualizationMode = vm.ResultsVM.VisualizationMode;
            feStatusMessage = vm.ResultsVM.FeStatusMessage;
        }

        try
        {
            vm.ScriptVM.BeginApplyProjectFile();
            Apply(vm, doc);
        }
        finally
        {
            vm.ScriptVM.EndApplyProjectFile();
        }

        if (!preserveUiShell)
            return;

        if (tabIndex.HasValue)
            vm.SelectedTabIndex = tabIndex.Value;
        if (zoomPan.HasValue)
            vm.ZoomPanModeEnabled = zoomPan.Value;
        if (resultsSubTab.HasValue)
            vm.ResultsVM.ResultsSubTabIndex = resultsSubTab.Value;
        if (!string.IsNullOrEmpty(visualizationMode))
            vm.ResultsVM.VisualizationMode = visualizationMode;
        if (feStatusMessage != null)
            vm.ResultsVM.FeStatusMessage = feStatusMessage;
    }

    /// <summary>Factory-default blank project document.</summary>
    public static XfemDocument CreateBlankDocument() => CreateBlankDocumentCore();

    /// <summary>
    /// Resets all tab and subtab model state to factory defaults for a new workspace slot.
    /// </summary>
    public static void ApplyNewBlankProject(MainWindowViewModel vm)
    {
        ApplyDocument(vm, CreateBlankDocumentCore());

        vm.FEAnalysisVM.ClearMeshDataCommand.Execute(null);
        vm.ResultsVM.ClearP610T3StagedData();
        vm.ResultsVM.ResultsSubTabIndex = 0;
        vm.ResultsVM.FeStatusMessage = "Run FE Analysis to view results.";
        vm.ResultsVM.VisualizationMode = ResultsViewModel.VisualizationModeContour2D;

        vm.ScriptVM.ResetToApplicationDefault();
        vm.VerificationVM.ResetToApplicationDefault();

        vm.SelectedTabIndex = 0;
        vm.FEAnalysisVM.FeAnalysisSubTabIndex = 0;
        vm.ProjectSettingsVM.SelectedSettingsTabIndex = 0;
        vm.MeshVM.MeshPopupSelectedTabIndex = MeshViewModel.MeshPopupUserDefinedTabIndex;
        vm.CurrentStage = null;
        vm.ZoomPanModeEnabled = false;

        vm.SyncFooterWithStagedAnalysisGrid();
    }

    private static XfemDocument CreateBlankDocumentCore()
    {
        return new XfemDocument
        {
            FormatVersion = CurrentFormatVersion,
            Main = new XfemMainState
            {
                SelectedTabIndex = 0,
                ZoomPanModeEnabled = false,
                CurrentStage = null,
            },
            ProjectSettings = new XfemProjectSettingsState
            {
                SelectedDimension = ProblemDimension.TwoD,
                SelectedSettingsTabIndex = 0,
                IsDeterministicAnalysis = true,
                IsProbabilisticAnalysis = false,
                NumberOfStages = 0,
                SelectedStageFooterTabIndex = null,
                StageFooterTabs = new List<XfemStageFooterTab>(),
                AnalysisCategoryOnFlags = new List<XfemAnalysisCategoryOnFlag>
                {
                    new()
                    {
                        Category = FEAnalysisViewModel.StructuralStaticElasticityCategory,
                        IsOn = true
                    }
                },
                ProjectName = "",
                Author = "",
                CreatedOn = DateTime.Today,
                Units = "SI",
                BishopMaxIterations = 100,
                BishopConvergenceTolerance = 1e-6,
                BishopInitialFactor = 1.0,
                NumberOfStagesModel = 1,
                AxisXMin = 0.0,
                AxisXMax = 100.0,
                AxisYMin = 0.0,
                AxisYMax = 60.0,
                GridSpacingMeters = 10.0,
            },
            Geometry = new XfemGeometryState
            {
                CanvasWidth = 600.0,
                CanvasHeight = 400.0,
                AxisXMin = 0.0,
                AxisXMax = 100.0,
                AxisYMin = 0.0,
                AxisYMax = 60.0,
                LayerElevationInput = "",
                HasAddedElevationLayer = false,
                TunnelCenterXInput = "",
                TunnelCenterYInput = "",
                TunnelRadiusInput = "",
                GeometryXInput = "",
                GeometryYInput = "",
                Vertices = new List<XfemPoint>(),
                LineSegments = new List<XfemLineSegment>(),
                InternalEdges = new List<XfemInternalEdge>(),
                GeometryCircles = new List<XfemGeometryCircle>(),
                Surfaces = new List<XfemGeometrySurface>(),
                TunnelRows = new List<XfemTunnelRow>(),
                GroundwaterLines = new List<XfemGroundwaterLine>(),
                ExcavationPolygonsAxis = new List<List<XfemPoint>>(),
                SurfaceExcavationOperations = new List<SurfaceBooleanOperationSnapshot>(),
                JointPropertySets = new List<XfemJointPropertySet>(),
            },
            Materials = new XfemMaterialsState
            {
                SelectedFailureCriterion = FailureCriterion.Elastic,
                SelectedMaterialType = MaterialType.Isotropic,
                SelectedAnalysisCategory = FEAnalysisViewModel.StructuralStaticElasticityCategory,
                SelectedDetailedAnalysisType = FEAnalysisViewModel.PlaneStrainDetailedType,
                ShowDeterministicTab = true,
                ShowProbabilisticTab = false,
                MaterialLibrary = new List<XfemMaterialLibraryEntry>(),
            },
            Mesh = null,
            Loads = null,
            BoundaryConditions = new XfemBoundaryConditionsState
            {
                Rows = new List<XfemBoundaryRow>(),
                StatusMessage = "",
                NodesText = "",
                BcValueForBoundary = "0",
            },
            FeAnalysis = new XfemFeAnalysisState
            {
                StatusMessage = "Load mesh data or run analysis.",
                FeAnalysisSubTabIndex = 0,
                StagedExcavationEnabled = false,
                GravityEnabled = true,
                GravityAcceleration = 9.81,
                LoadSteppingEnabled = false,
                NumberOfLoadSteps = 1,
                CppExecutableDirectory = "",
                ConvergenceTolerance = 1.0e-5,
                MaxIterations = 250,
                LoadIncrements = 5,
                LayersOverride = null,
                SrfTrialCount = 3,
                SrfValues = new List<double> { 1.0, 1.1, 1.2 },
                StageDefinitions = new List<XfemStageDefinition>(),
            },
            Results = new XfemResultsState
            {
                ResultsSubTabIndex = 0,
                VisualizationMode = ResultsViewModel.VisualizationModeContour2D,
                FeStatusMessage = "Run FE Analysis to view results.",
                FeRun = null,
            },
            StagedConstruction = new XfemStagedConstructionState
            {
                Stages = new List<XfemConstructionStageExcavations>(),
            },
            FieldStress = new XfemFieldStressState
            {
                Global = new XfemFieldStressSpec { Mode = FieldStressMode.Gravity },
                Overrides = new List<XfemFieldStressOverride>(),
            },
            UserDefinedMesh = new XfemUserDefinedMeshState
            {
                ElementType = "T3",
                Nodes = new List<XfemUdmNodeRow>(),
                Elements = new List<XfemUdmElementRow>(),
            },
            Verification = new XfemVerificationState
            {
                SelectedProblem = "Kirsch — circular hole, plane strain (p610_t3_elastic)",
                TolerancePercent = 5.0,
                AutoFromMeta = true,
                E_kPa = 1.0e7,
                Nu = 0.2,
                Sigma_v_kPa = 100.0,
                K0 = 1.0,
                A = 1.0,
                Cx = 0.0,
                Cy = 0.0,
                Pi_kPa = 0.0,
                PhiDeg = 30.0,
                C_kPa = 3450.0,
                PsiDeg = 30.0,
                Gamma = 0.0,
            },
        };
    }

    /// <summary>
    /// Diagnostic log appended next to the .xfem file. Records what excavation data
    /// was on the in-memory model vs what the DTO contained, so we can pinpoint where
    /// information is lost between Save / Load.
    /// </summary>
    private static void WriteExcavationDiagnostic(string xfemPath, string phase, MainWindowViewModel vm, XfemDocument doc)
    {
        try
        {
            var logPath = xfemPath + ".savelog.txt";
            using var w = new StreamWriter(logPath, append: true);
            w.WriteLine($"=== {phase} @ {System.DateTime.Now:O} ===");
            var sc = vm.StagedConstructionVM;
            w.WriteLine($"  vm.FEAnalysisVM.StagedExcavationEnabled = {vm.FEAnalysisVM.StagedExcavationEnabled}");
            w.WriteLine($"  vm.FEAnalysisVM.StageDefinitions.Count   = {vm.FEAnalysisVM.StageDefinitions.Count}");
            w.WriteLine($"  vm.StagedConstructionVM.Stages.Count     = {sc.Stages.Count}");
            foreach (var s in sc.Stages.OrderBy(x => x.StageNumber))
                w.WriteLine($"    Stage {s.StageNumber} '{s.StageName}' [{s.StageType}] zones={s.ExcavationZones.Count} surfaces=[{string.Join(",", s.ExcavatedSurfaceNumbers)}]");
            w.WriteLine($"  doc.StagedConstruction == null ? {(doc.StagedConstruction == null)}");
            if (doc.StagedConstruction?.Stages is { } drows)
            {
                w.WriteLine($"  doc.StagedConstruction.Stages.Count = {drows.Count}");
                foreach (var r in drows.OrderBy(x => x.StageNumber))
                    w.WriteLine($"    [DTO] Stage {r.StageNumber} zones={(r.Zones?.Count ?? 0)}");
            }
            w.WriteLine();
        }
        catch { /* diagnostic only - never break Save/Load */ }
    }

    private static bool IsSupportedFormatVersion(string? v) =>
        v is "1.0" or "1.1" or "1.2" or "1.3" or "1.8" or "1.9" or "1.10" or "1.11";

    private static SurfaceBooleanOperationSnapshot CaptureSurfaceBooleanRow(BooleanOperationRow r)
    {
        return new SurfaceBooleanOperationSnapshot
        {
            OperationId = r.OperationId,
            OperationType = r.OperationType,
            TypeEnum = r.TypeEnum,
            RegionAName = r.RegionAName,
            RegionBName = r.RegionBName,
            ResultRegionName = r.ResultRegionName,
            RegionASurfaceNumber = r.RegionASurfaceNumber,
            RegionBSurfaceNumber = r.RegionBSurfaceNumber,
            ParticipatingSurfaceNumbers = r.IsMultiRegion && r.ParticipatingSurfaceNumbers.Count > 0
                ? new List<int>(r.ParticipatingSurfaceNumbers)
                : null,
            RegionAArea = r.RegionAArea,
            RegionBArea = r.RegionBArea,
            ResultArea = r.ResultArea,
            IsComplete = r.IsComplete,
            AppliedInStageNumber = r.AppliedInStageNumber
        };
    }

    private static XfemGeologicalFeature CaptureGeologicalFeature(GeologicalFeature f) => new()
    {
        Id = f.Id,
        Kind = f.Kind.ToString(),
        Name = f.Name,
        IsEnabled = f.IsEnabled,
        MaterialId = f.MaterialId,
        WaterElevation = f.WaterElevation,
        DipAngleDeg = f.DipAngleDeg,
        KnKpa = f.KnKpa,
        KsKpa = f.KsKpa,
        JointCohesionKpa = f.JointCohesionKpa,
        JointFrictionDeg = f.JointFrictionDeg,
        JointTensionCutoffKpa = f.JointTensionCutoffKpa,
        PropertySetId = f.PropertySetId,
        EndCondition = f.EndCondition.ToString(),
        InstallStage = f.InstallStage,
        JointNetworkId = f.JointNetworkId,
        JointNetworkName = f.JointNetworkName,
        Vertices = f.Vertices.Select(v => new XfemPoint(v.X, v.Y)).ToList()
    };

    private static XfemJointPropertySet CaptureJointPropertySet(FeJointPropertySet s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        ColourArgb = MaterialColourToArgb(s.Colour),
        Criterion = s.Criterion.ToString(),
        TensileStrengthKpa = s.TensileStrengthKpa,
        PeakCohesionKpa = s.PeakCohesionKpa,
        PeakFrictionDeg = s.PeakFrictionDeg,
        UseResidualStrength = s.UseResidualStrength,
        ResidualTensileStrengthKpa = s.ResidualTensileStrengthKpa,
        ResidualCohesionKpa = s.ResidualCohesionKpa,
        ResidualFrictionDeg = s.ResidualFrictionDeg,
        NormalStiffnessKpaPerM = s.NormalStiffnessKpaPerM,
        ShearStiffnessKpaPerM = s.ShearStiffnessKpaPerM,
        UseGroundwaterPorePressure = s.UseGroundwaterPorePressure,
        UseAdditionalPressure = s.UseAdditionalPressure,
        AdditionalPressureKpa = s.AdditionalPressureKpa,
        ApplyPressureToLinerSideOnly = s.ApplyPressureToLinerSideOnly,
        UseStagedProperties = s.UseStagedProperties,
        AllowSlipFromStage = s.AllowSlipFromStage,
        InitialJointDeformation = s.InitialJointDeformation,
        StageFactors = s.StageFactors.Select(f => new XfemJointStageFactor
        {
            StageNumber = f.StageNumber,
            KnFactor = f.KnFactor,
            KsFactor = f.KsFactor,
            CohesionFactor = f.CohesionFactor,
            FrictionFactor = f.FrictionFactor
        }).ToList()
    };

    private static XfemKernelBooleanOperation CaptureKernelBoolean(KernelBooleanOperation op) => new()
    {
        Sequence = op.Sequence,
        Kind = op.Kind.ToString(),
        SubjectFaceId = op.SubjectFaceId,
        ToolFaceId = op.ToolFaceId,
        ResultMaterialId = op.ResultMaterialId,
        StageNumber = op.StageNumber,
        Label = op.Label,
        FillRing = op.FillRing.Count > 0
            ? op.FillRing.Select(v => new XfemPoint(v.X, v.Y)).ToList()
            : null
    };

    private static IEnumerable<GeologicalFeature> RestoreGeologicalFeatures(IEnumerable<XfemGeologicalFeature>? rows)
    {
        if (rows == null) yield break;
        foreach (var row in rows)
        {
            if (!Enum.TryParse<GeologicalFeatureKind>(row.Kind, ignoreCase: true, out var kind))
                kind = GeologicalFeatureKind.WeakPlane;
            var feature = new GeologicalFeature
            {
                Id = string.IsNullOrWhiteSpace(row.Id) ? Guid.NewGuid().ToString("N") : row.Id,
                Kind = kind,
                Name = row.Name ?? kind.ToString(),
                IsEnabled = row.IsEnabled,
                MaterialId = row.MaterialId,
                WaterElevation = row.WaterElevation,
                DipAngleDeg = row.DipAngleDeg,
                KnKpa = row.KnKpa,
                KsKpa = row.KsKpa,
                JointCohesionKpa = row.JointCohesionKpa,
                JointFrictionDeg = row.JointFrictionDeg,
                JointTensionCutoffKpa = row.JointTensionCutoffKpa,
                PropertySetId = row.PropertySetId,
                EndCondition = Enum.TryParse<FeJointEndCondition>(row.EndCondition, ignoreCase: true, out var ends)
                    ? ends
                    : FeJointEndCondition.BothClosed,
                InstallStage = row.InstallStage > 0 ? row.InstallStage : 1,
                JointNetworkId = row.JointNetworkId,
                JointNetworkName = row.JointNetworkName
            };
            if (row.Vertices != null)
            {
                foreach (var v in row.Vertices)
                    feature.Vertices.Add((v.X, v.Y));
            }

            yield return feature;
        }
    }

    private static IEnumerable<FeJointPropertySet> RestoreJointPropertySets(IEnumerable<XfemJointPropertySet>? rows)
    {
        if (rows == null) yield break;
        foreach (var row in rows)
        {
            if (!Enum.TryParse<FeJointSlipCriterion>(row.Criterion, ignoreCase: true, out var criterion))
                criterion = FeJointSlipCriterion.MohrCoulomb;
            var set = new FeJointPropertySet
            {
                Id = row.Id > 0 ? row.Id : 1,
                Name = string.IsNullOrWhiteSpace(row.Name) ? $"Joint {row.Id}" : row.Name,
                Colour = row.ColourArgb is uint argb && argb != 0
                    ? MaterialColourFromArgb(argb)
                    : Color.FromRgb(0xEA, 0x58, 0x0C),
                Criterion = criterion,
                TensileStrengthKpa = row.TensileStrengthKpa,
                PeakCohesionKpa = row.PeakCohesionKpa,
                PeakFrictionDeg = row.PeakFrictionDeg,
                UseResidualStrength = row.UseResidualStrength,
                ResidualTensileStrengthKpa = row.ResidualTensileStrengthKpa,
                ResidualCohesionKpa = row.ResidualCohesionKpa,
                ResidualFrictionDeg = row.ResidualFrictionDeg,
                NormalStiffnessKpaPerM = row.NormalStiffnessKpaPerM,
                ShearStiffnessKpaPerM = row.ShearStiffnessKpaPerM,
                UseGroundwaterPorePressure = row.UseGroundwaterPorePressure,
                UseAdditionalPressure = row.UseAdditionalPressure,
                AdditionalPressureKpa = row.AdditionalPressureKpa,
                ApplyPressureToLinerSideOnly = row.ApplyPressureToLinerSideOnly,
                UseStagedProperties = row.UseStagedProperties,
                AllowSlipFromStage = row.AllowSlipFromStage > 0 ? row.AllowSlipFromStage : 1,
                InitialJointDeformation = row.InitialJointDeformation
            };
            if (row.StageFactors != null)
            {
                foreach (var f in row.StageFactors)
                    set.StageFactors.Add(new FeJointStageFactor
                    {
                        StageNumber = f.StageNumber,
                        KnFactor = f.KnFactor,
                        KsFactor = f.KsFactor,
                        CohesionFactor = f.CohesionFactor,
                        FrictionFactor = f.FrictionFactor
                    });
            }

            yield return set;
        }
    }

    private static XfemJointNetwork CaptureJointNetwork(FeJointNetworkDefinition n) => new()
    {
        Id = n.Id,
        Name = n.Name,
        Model = n.Model.ToString(),
        TargetSurfaceNumbers = n.TargetSurfaceNumbers.Count > 0 ? n.TargetSurfaceNumbers.ToList() : null,
        PropertySetId = n.PropertySetId,
        CrossPropertySetId = n.CrossPropertySetId,
        EndCondition = n.EndCondition.ToString(),
        InstallStage = n.InstallStage,
        RandomSeed = n.RandomSeed,
        RandomLocation = n.RandomLocation,
        ThroughX = n.ThroughX,
        ThroughY = n.ThroughY,
        Intensity = n.Intensity,
        VoronoiDensityMethod = n.VoronoiDensityMethod.ToString(),
        VoronoiRegularity = n.VoronoiRegularity.ToString(),
        HexagonHorizontal = n.HexagonHorizontal,
        SiteCount = n.SiteCount,
        AnisotropyX = n.AnisotropyX,
        AnisotropyY = n.AnisotropyY,
        Set1 = CaptureJointSet(n.Set1),
        Set2 = CaptureJointSet(n.Set2),
        Orientation = CaptureJointDistribution(n.Orientation),
        Length = CaptureJointDistribution(n.Length),
        Persistence = CaptureJointDistribution(n.Persistence)
    };

    private static XfemJointSetParameters CaptureJointSet(FeJointSetParameters s) => new()
    {
        DipDeg = s.DipDeg,
        DipStdDeg = s.DipStdDeg,
        InfiniteLength = s.InfiniteLength,
        RandomOffset = s.RandomOffset,
        OffsetLength = s.OffsetLength,
        FirstJointOffset = s.FirstJointOffset,
        UseStatisticalSpacing = s.UseStatisticalSpacing,
        Spacing = CaptureJointDistribution(s.Spacing),
        Length = CaptureJointDistribution(s.Length),
        Persistence = CaptureJointDistribution(s.Persistence)
    };

    private static XfemJointDistribution CaptureJointDistribution(FeJointStatisticalDistribution d) => new()
    {
        Kind = d.Kind.ToString(),
        Mean = d.Mean,
        StdDev = d.StdDev,
        Min = d.Min,
        Max = d.Max,
        AutoMinMax3Std = d.AutoMinMax3Std
    };

    private static IEnumerable<FeJointNetworkDefinition> RestoreJointNetworks(IEnumerable<XfemJointNetwork>? rows)
    {
        if (rows == null) yield break;
        foreach (var row in rows)
        {
            if (!Enum.TryParse<FeJointNetworkModel>(row.Model, ignoreCase: true, out var model))
                model = FeJointNetworkModel.ParallelDeterministic;
            var def = new FeJointNetworkDefinition
            {
                Id = string.IsNullOrWhiteSpace(row.Id) ? Guid.NewGuid().ToString("N") : row.Id,
                Name = string.IsNullOrWhiteSpace(row.Name) ? "Network 1" : row.Name,
                Model = model,
                PropertySetId = row.PropertySetId,
                CrossPropertySetId = row.CrossPropertySetId,
                EndCondition = Enum.TryParse<FeJointEndCondition>(row.EndCondition, ignoreCase: true, out var ends)
                    ? ends
                    : FeJointEndCondition.BothClosed,
                InstallStage = row.InstallStage > 0 ? row.InstallStage : 1,
                RandomSeed = row.RandomSeed,
                RandomLocation = row.RandomLocation,
                ThroughX = row.ThroughX,
                ThroughY = row.ThroughY,
                Intensity = row.Intensity,
                SiteCount = row.SiteCount > 0 ? row.SiteCount : 24,
                AnisotropyX = row.AnisotropyX > 0 ? row.AnisotropyX : 1,
                AnisotropyY = row.AnisotropyY > 0 ? row.AnisotropyY : 1,
                HexagonHorizontal = row.HexagonHorizontal
            };
            if (Enum.TryParse<FeJointVoronoiDensityMethod>(row.VoronoiDensityMethod, ignoreCase: true, out var dens))
                def.VoronoiDensityMethod = dens;
            if (Enum.TryParse<FeJointVoronoiRegularity>(row.VoronoiRegularity, ignoreCase: true, out var reg))
                def.VoronoiRegularity = reg;
            if (row.TargetSurfaceNumbers != null)
            {
                foreach (var n in row.TargetSurfaceNumbers)
                    def.TargetSurfaceNumbers.Add(n);
            }

            if (row.Set1 != null) RestoreJointSet(def.Set1, row.Set1);
            if (row.Set2 != null) RestoreJointSet(def.Set2, row.Set2);
            if (row.Orientation != null) RestoreJointDistribution(def.Orientation, row.Orientation);
            if (row.Length != null) RestoreJointDistribution(def.Length, row.Length);
            if (row.Persistence != null) RestoreJointDistribution(def.Persistence, row.Persistence);
            yield return def;
        }
    }

    private static void RestoreJointSet(FeJointSetParameters dest, XfemJointSetParameters src)
    {
        dest.DipDeg = src.DipDeg;
        dest.DipStdDeg = src.DipStdDeg;
        dest.InfiniteLength = src.InfiniteLength;
        dest.RandomOffset = src.RandomOffset;
        dest.OffsetLength = src.OffsetLength;
        dest.FirstJointOffset = src.FirstJointOffset;
        dest.UseStatisticalSpacing = src.UseStatisticalSpacing;
        if (src.Spacing != null) RestoreJointDistribution(dest.Spacing, src.Spacing);
        if (src.Length != null) RestoreJointDistribution(dest.Length, src.Length);
        if (src.Persistence != null) RestoreJointDistribution(dest.Persistence, src.Persistence);
    }

    private static void RestoreJointDistribution(FeJointStatisticalDistribution dest, XfemJointDistribution src)
    {
        if (Enum.TryParse<FeJointDistributionKind>(src.Kind, ignoreCase: true, out var kind))
            dest.Kind = kind;
        dest.Mean = src.Mean;
        dest.StdDev = src.StdDev;
        dest.Min = src.Min;
        dest.Max = src.Max;
        dest.AutoMinMax3Std = src.AutoMinMax3Std;
    }

    private static IEnumerable<KernelBooleanOperation> RestoreKernelBooleans(IEnumerable<XfemKernelBooleanOperation>? rows)
    {
        if (rows == null) yield break;
        foreach (var row in rows)
        {
            if (!Enum.TryParse<KernelBooleanKind>(row.Kind, ignoreCase: true, out var kind))
                kind = KernelBooleanKind.Union;
            var op = new KernelBooleanOperation
            {
                Sequence = row.Sequence,
                Kind = kind,
                SubjectFaceId = row.SubjectFaceId,
                ToolFaceId = row.ToolFaceId,
                ResultMaterialId = row.ResultMaterialId,
                StageNumber = row.StageNumber,
                Label = row.Label ?? ""
            };
            if (row.FillRing != null)
            {
                foreach (var v in row.FillRing)
                    op.FillRing.Add((v.X, v.Y));
            }

            yield return op;
        }
    }

    /// <summary>
    /// Writes/updates <c>{project}.underlay.png</c> beside the .xfem and points the live underlay at it.
    /// </summary>
    private static void PersistSlopeImageSidecar(MainWindowViewModel vm, string xfemPath)
    {
        var g = vm.GeometryDefinitionVM;
        var underlay = g.SlopeImageUnderlay;
        if (underlay == null || g.SlopeImageBitmap == null)
            return;

        var sidecar = SlopeImageImportService.WriteSidecarBesideProject(
            xfemPath,
            underlay.SourcePath,
            g.SlopeImageBitmap as System.Windows.Media.Imaging.BitmapSource);
        underlay.SourcePath = sidecar;
    }

    private static XfemSlopeImageUnderlay? CaptureSlopeImageUnderlay(GeometryDefinitionViewModel g, string? projectPath)
    {
        var u = g.SlopeImageUnderlay;
        if (u == null || g.SlopeImageBitmap == null)
            return null;

        var imagePath = u.SourcePath ?? "";
        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
                imagePath = SlopeImageImportService.MakeRelativeToProject(projectPath, imagePath);
            else
                imagePath = Path.GetFileNameWithoutExtension(projectPath) + ".underlay.png";
        }

        return new XfemSlopeImageUnderlay
        {
            ImageRelativePath = imagePath,
            IsVisible = u.IsVisible,
            Opacity = u.Opacity,
            Mode = u.Mode == SlopeImageCalibrationMode.AxisAlignedBounds
                ? "axisAlignedBounds"
                : "twoPoint",
            ImagePixelWidth = u.ImagePixelWidth,
            ImagePixelHeight = u.ImagePixelHeight,
            Point1ImageX = u.Point1ImagePx.X,
            Point1ImageY = u.Point1ImagePx.Y,
            Point1WorldX = u.Point1World.X,
            Point1WorldY = u.Point1World.Y,
            Point2ImageX = u.Point2ImagePx.X,
            Point2ImageY = u.Point2ImagePx.Y,
            Point2WorldX = u.Point2World.X,
            Point2WorldY = u.Point2World.Y,
            WorldMinX = u.WorldMinX,
            WorldMinY = u.WorldMinY,
            WorldMaxX = u.WorldMaxX,
            WorldMaxY = u.WorldMaxY
        };
    }

    private static void RestoreSlopeImageUnderlay(
        GeometryDefinitionViewModel g,
        XfemSlopeImageUnderlay? dto,
        string? projectPath)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.ImageRelativePath))
        {
            g.ClearSlopeImageUnderlaySilent();
            return;
        }

        string abs;
        if (!string.IsNullOrWhiteSpace(projectPath))
            abs = SlopeImageImportService.ResolveBesideProject(projectPath, dto.ImageRelativePath);
        else if (Path.IsPathRooted(dto.ImageRelativePath))
            abs = Path.GetFullPath(dto.ImageRelativePath);
        else
        {
            g.ClearSlopeImageUnderlaySilent();
            return;
        }

        if (!File.Exists(abs))
        {
            g.ClearSlopeImageUnderlaySilent();
            g.SetGeometryImportStatusMessage(
                $"Slope image underlay not found: {Path.GetFileName(abs)}");
            return;
        }

        var mode = string.Equals(dto.Mode, "axisAlignedBounds", StringComparison.OrdinalIgnoreCase)
            ? SlopeImageCalibrationMode.AxisAlignedBounds
            : SlopeImageCalibrationMode.TwoPoint;

        var underlay = new SlopeImageUnderlay
        {
            SourcePath = abs,
            IsVisible = dto.IsVisible,
            Opacity = dto.Opacity is > 0 and <= 1 ? dto.Opacity : 0.65,
            Mode = mode,
            ImagePixelWidth = dto.ImagePixelWidth,
            ImagePixelHeight = dto.ImagePixelHeight,
            Point1ImagePx = new Point(dto.Point1ImageX, dto.Point1ImageY),
            Point1World = new Point(dto.Point1WorldX, dto.Point1WorldY),
            Point2ImagePx = new Point(dto.Point2ImageX, dto.Point2ImageY),
            Point2World = new Point(dto.Point2WorldX, dto.Point2WorldY),
            WorldMinX = dto.WorldMinX,
            WorldMinY = dto.WorldMinY,
            WorldMaxX = dto.WorldMaxX,
            WorldMaxY = dto.WorldMaxY
        };

        try
        {
            var bitmap = SlopeImageImportService.LoadBitmap(abs);
            g.ApplySlopeImageUnderlay(underlay, bitmap, expandAxis: false);
        }
        catch (Exception ex)
        {
            g.ClearSlopeImageUnderlaySilent();
            g.SetGeometryImportStatusMessage($"Could not load slope image underlay: {ex.Message}");
        }
    }

    /// <summary>
    /// Merges tab-local geometry edits into <see cref="MainWindowViewModel.GeometryDefinitionVM"/> before capture.
    /// Material Properties uses a geometry copy while that tab is active.
    /// </summary>
    private static void FlushPendingTabStateBeforeSave(MainWindowViewModel vm)
    {
        if (vm.SelectedTabIndex == MaterialPropertiesMainTabIndex)
            vm.MaterialPropertiesVM.GeometryDefinition.CopyGeometryTo(vm.GeometryDefinitionVM);
    }

    private static XfemDocument Capture(MainWindowViewModel vm, bool skipMaintenance = false, string? projectPath = null)
    {
        var g = vm.GeometryDefinitionVM;
        var p = vm.ProjectSettingsVM;

        if (!skipMaintenance)
            g.PruneDuplicateBoundaryInternalEdges();

        var vertices = g.GeometryVertexRows.Select(v => new XfemPoint(v.AxisX, v.AxisY)
        {
            StablePointId = v.StablePointId > 0 ? v.StablePointId : null,
            SplitFromLineNumber = v.SplitFromLineNumber,
            IsMaterialLayerCutVertex = v.IsMaterialLayerCutVertex ? true : null
        }).ToList();
        var segments = g.GeometryLineSegments.Where(s => !s.IsCircle).Select(CaptureSegment).ToList();
        var geometryCircles = g.GeometryInternalCircles.Select(c => new XfemGeometryCircle
        {
            Id = c.Id,
            CenterAxisX = c.CenterAxisX,
            CenterAxisY = c.CenterAxisY,
            RadiusAxis = c.RadiusAxis
        }).ToList();
        var internalEdges = g.GeometryInternalEdges.Select(CaptureInternalEdge).ToList();
        var surfaces = g.GeometrySurfaces.Select(CaptureGeometrySurface).ToList();

        var tunnelRows = g.TunnelExcavationCircleRows.Select(r => new XfemTunnelRow
        {
            CircleNumber = r.CircleNumber,
            CenterX = r.CenterX,
            CenterY = r.CenterY,
            Radius = r.Radius,
            Stage = r.Stage
        }).ToList();

        var gwLines = g.GroundwaterLines.Select(l =>
        {
            var s = g.ConvertCanvasToAxis(l.Start);
            var e = g.ConvertCanvasToAxis(l.End);
            return new XfemGroundwaterLine { StartX = s.X, StartY = s.Y, EndX = e.X, EndY = e.Y, Elevation = l.Elevation };
        }).ToList();
        var gwSurf = g.GroundwaterSurfPoints
            .Select(p => new XfemPoint(p.AxisX, p.AxisY))
            .ToList();

        var polyAxis = new List<List<XfemPoint>>();
        foreach (var pc in g.ExcavationPolygons)
            polyAxis.Add(pc.Select(pt => ToAxis(g, pt)).ToList());

        var mesh = CaptureMesh(vm.MeshVM);
        var loads = vm.LoadConditionsVM.Loads.Select(l => CaptureLoad(l, vm.GeometryDefinitionVM)).ToList();
        var waterHeads = vm.GroundwaterVM.WaterHeads.Select(CaptureWaterHead).ToList();
        var transientFixedPressures = vm.GroundwaterVM.TransientFixedPressureNodes
            .Select(CaptureTransientFixedPressure).ToList();
        var bcs = vm.BoundaryConditionsVM.BoundaryConditions.Select(CaptureBc).ToList();

        return new XfemDocument
        {
            FormatVersion = CurrentFormatVersion,
            Main = new XfemMainState
            {
                SelectedTabIndex = vm.SelectedTabIndex,
                ZoomPanModeEnabled = vm.ZoomPanModeEnabled,
                CurrentStage = vm.CurrentStage
            },
            ProjectSettings = new XfemProjectSettingsState
            {
                SelectedDimension = p.SelectedDimension,
                SelectedSettingsTabIndex = p.SelectedSettingsTabIndex,
                IsDeterministicAnalysis = p.IsDeterministicAnalysis,
                IsProbabilisticAnalysis = p.IsProbabilisticAnalysis,
                NumberOfStages = p.NumberOfStages,
                SelectedStageFooterTabIndex = p.SelectedStageFooterTabIndex,
                StageFooterTabs = p.StageFooterTabs.Select(t => new XfemStageFooterTab { StageNumber = t.StageNumber, StageName = t.StageName }).ToList(),
                GwStaticIncludeWaterSurfaces = p.GroundwaterStaticSettings.IncludeWaterSurfaces,
                GwStaticIncludePondedFreeWater = p.GroundwaterStaticSettings.IncludePondedFreeWater,
                GwStaticIncludeRuCoefficients = p.GroundwaterStaticSettings.IncludeRuCoefficients,
                GwStaticIncludeWaterPressureGridTotalHead = p.GroundwaterStaticSettings.IncludeWaterPressureGridTotalHead,
                GwStaticIncludeWaterPressureGridPressureHead = p.GroundwaterStaticSettings.IncludeWaterPressureGridPressureHead,
                GwStaticIncludeWaterPressureGridPorePressure = p.GroundwaterStaticSettings.IncludeWaterPressureGridPorePressure,
                SeepageUpstreamHead = p.SeepageSettings.UpstreamHead,
                SeepageDownstreamHead = p.SeepageSettings.DownstreamHead,
                SeepageAxisymmetric = p.SeepageSettings.Axisymmetric,
                SrfAxisymmetric = p.SrfAxisymmetric,
                SeepageFreeSurfaceTolerance = p.SeepageSettings.FreeSurfaceTolerance,
                SeepageFreeSurfaceMaxIterations = p.SeepageSettings.FreeSurfaceMaxIterations,
                SeepageIncludeWaterHeadPlaneAxisym = p.SeepageSettings.IncludeWaterHeadPlaneAxisym,
                SeepageIncludeWaterHeadFreeSurface = p.SeepageSettings.IncludeWaterHeadFreeSurface,
                SeepageIncludeWaterHeadGeneral2D = p.SeepageSettings.IncludeWaterHeadGeneral2D,
                SeepageIncludeWaterHeadGeneral3D = p.SeepageSettings.IncludeWaterHeadGeneral3D,
                TransientAxisymmetric = p.TransientSettings.Axisymmetric,
                TransientTimeStep = p.TransientSettings.TimeStep,
                TransientNumberOfSteps = p.TransientSettings.NumberOfSteps,
                TransientTheta = p.TransientSettings.Theta,
                TransientPrintInterval = p.TransientSettings.PrintInterval,
                TransientMonitorNode = p.TransientSettings.MonitorNode,
                TransientContourStep = p.TransientSettings.ContourStep,
                TransientContourIntervals = p.TransientSettings.ContourIntervals,
                TransientInitialPressure = p.TransientSettings.InitialPressure,
                TransientFixedPressure = p.TransientSettings.FixedPressure,
                TransientFixTopFace = p.TransientSettings.FixTopFace,
                TransientFixBottomFace = p.TransientSettings.FixBottomFace,
                TransientFixMinXFace = p.TransientSettings.FixMinXFace,
                TransientFixMaxXFace = p.TransientSettings.FixMaxXFace,
                AnalysisCategoryOnFlags = p.GetCategoryOnSnapshot()
                    .Select(kv => new XfemAnalysisCategoryOnFlag { Category = kv.Key, IsOn = kv.Value })
                    .ToList(),
                ProjectName = p.Project.ProjectName,
                Author = p.Project.Author,
                CreatedOn = p.Project.CreatedOn,
                Units = p.Project.Units,
                BishopMaxIterations = p.Project.BishopMaxIterations,
                BishopConvergenceTolerance = p.Project.BishopConvergenceTolerance,
                BishopInitialFactor = p.Project.BishopInitialFactor,
                NumberOfStagesModel = p.Project.NumberOfStages,
                AxisXMin = p.Project.AxisXMin,
                AxisXMax = p.Project.AxisXMax,
                AxisYMin = p.Project.AxisYMin,
                AxisYMax = p.Project.AxisYMax,
                GridSpacingMeters = p.Project.GridSpacingMeters,
                ShowGrid = p.Project.ShowGrid,
                ShowAxes = p.Project.ShowAxes,
                CoordinateSystem = new XfemCoordinateSystem
                {
                    EpsgCode = p.Project.CoordinateSystem.EpsgCode,
                    Name = p.Project.CoordinateSystem.Name,
                    OriginEasting = p.Project.CoordinateSystem.OriginEasting,
                    OriginNorthing = p.Project.CoordinateSystem.OriginNorthing,
                    RotationDeg = p.Project.CoordinateSystem.RotationDeg,
                    Scale = p.Project.CoordinateSystem.Scale
                },
                FieldStations = CaptureFieldStations(p.FieldStations),
                FieldStationSoilBlockMargin = p.FieldStationSoilBlockMargin,
                FieldStationReferenceElevation = p.FieldStationReferenceElevation,
                FieldStationsActive = p.FieldStationsActive,
                PlotBoreholes = p.PlotBoreholes,
                Drawdown = CaptureDrawdown(p.DrawdownSettings)
            },
            Geometry = new XfemGeometryState
            {
                CanvasWidth = g.CanvasWidth,
                CanvasHeight = g.CanvasHeight,
                AxisXMin = g.AxisXMin,
                AxisXMax = g.AxisXMax,
                AxisYMin = g.AxisYMin,
                AxisYMax = g.AxisYMax,
                LayerElevationInput = g.LayerElevationInput,
                HasAddedElevationLayer = g.HasAddedElevationLayer,
                MaterialLayerCutElevations = g.MaterialLayerCutElevationsSnapshot.Count > 0
                    ? g.MaterialLayerCutElevationsSnapshot.ToList()
                    : null,
                NonHorizontalLayerCuts = CaptureNonHorizontalLayerCuts(g),
                LayerMaterialAssignments = g.LayerMaterialAssignmentsSnapshot.Count > 0
                    ? g.LayerMaterialAssignmentsSnapshot.Select(kv => new XfemLayerMaterialAssignment
                    {
                        DisplayLayerNumber = kv.Key,
                        MaterialId = kv.Value
                    }).ToList()
                    : null,
                LayerDisplayNames = g.LayerDisplayNamesSnapshot.Count > 0
                    ? g.LayerDisplayNamesSnapshot.Select(kv => new XfemLayerDisplayName
                    {
                        DisplayLayerNumber = kv.Key,
                        Name = kv.Value
                    }).ToList()
                    : null,
                LayerDisplayColors = g.LayerDisplayColorsSnapshot.Count > 0
                    ? g.LayerDisplayColorsSnapshot.Select(kv => new XfemLayerDisplayColor
                    {
                        DisplayLayerNumber = kv.Key,
                        Argb = kv.Value
                    }).ToList()
                    : null,
                SurfaceMaterialFills = g.SurfaceMaterialFillsSnapshot.Count > 0
                    ? g.SurfaceMaterialFillsSnapshot.Select(s => new XfemSurfaceMaterialFill
                    {
                        SurfaceNumber = s.SurfaceNumber,
                        Argb = s.Argb,
                        MaterialId = s.MaterialId
                    }).ToList()
                    : null,
                TunnelCenterXInput = g.TunnelCenterXInput,
                TunnelCenterYInput = g.TunnelCenterYInput,
                TunnelRadiusInput = g.TunnelRadiusInput,
                GeometryXInput = g.GeometryXInput,
                GeometryYInput = g.GeometryYInput,
                Vertices = vertices,
                LineSegments = segments,
                InternalEdges = internalEdges,
                GeometryCircles = geometryCircles,
                Surfaces = surfaces,
                SelectedSurfaceNumber = g.SelectedGeometrySurface?.SurfaceNumber,
                TunnelRows = tunnelRows,
                GroundwaterLines = gwLines,
                GroundwaterEnabled = vm.GroundwaterVM.IsEnabled,
                GroundwaterGammaW = vm.GroundwaterVM.GammaW,
                GroundwaterMethod = vm.GroundwaterVM.Method switch
                {
                    GroundwaterMethod.Ru => "ru",
                    GroundwaterMethod.Bbar => "bbar",
                    GroundwaterMethod.Grid => "grid",
                    _ => "hydrostatic"
                },
                GroundwaterRu = vm.GroundwaterVM.Method == GroundwaterMethod.Ru ? vm.GroundwaterVM.Ru : null,
                GroundwaterBbar = vm.GroundwaterVM.Method == GroundwaterMethod.Bbar ? vm.GroundwaterVM.Bbar : null,
                GroundwaterShowSaturatedMaterialZone = vm.GroundwaterVM.ShowSaturatedMaterialZone,
                GroundwaterShowUnsaturatedMaterialZone = vm.GroundwaterVM.ShowUnsaturatedMaterialZone,
                GroundwaterShowPondedFreeWaterRegion = vm.GroundwaterVM.ShowPondedFreeWaterRegion,
                GroundwaterPondedWaterDisplayPattern = FormatPondedWaterDisplayPattern(vm.GroundwaterVM.PondedWaterDisplayPattern),
                GroundwaterShowPondedFacePressureArrows = vm.GroundwaterVM.ShowPondedFacePressureArrows,
                GroundwaterShowPondedHeadIsobars = vm.GroundwaterVM.ShowPondedHeadIsobars,
                GroundwaterPondedWaterLevelMethod = vm.GroundwaterVM.PondedWaterLevelMethod == PondedWaterLevelMethod.TotalHead
                    ? "total_head"
                    : "phreatic",
                GroundwaterPondedTotalHead = vm.GroundwaterVM.PondedTotalHead,
                GroundwaterSurfPoints = gwSurf,
                WaterPressureGrids = SnapshotNamedWaterPressureGrids(vm.GroundwaterVM.WaterPressureGrids),
                WaterPressureGridTotalHead = SnapshotWaterPressureGridByType(vm.GroundwaterVM.WaterPressureGrids, WaterPressureGridType.TotalHead),
                WaterPressureGridPressureHead = SnapshotWaterPressureGridByType(vm.GroundwaterVM.WaterPressureGrids, WaterPressureGridType.PressureHead),
                WaterPressureGridPorePressure = SnapshotWaterPressureGridByType(vm.GroundwaterVM.WaterPressureGrids, WaterPressureGridType.PorePressure),
                ExcavationPolygonsAxis = polyAxis,
                SurfaceExcavationOperations = g.ExcavationOperations.Select(CaptureSurfaceBooleanRow).ToList(),
                GeologicalFeatures = g.GeologicalFeatures.Select(CaptureGeologicalFeature).ToList(),
                JointPropertySets = g.JointPropertySets.Select(CaptureJointPropertySet).ToList(),
                JointNetworks = g.JointNetworks.Select(CaptureJointNetwork).ToList(),
                KernelBooleanHistory = g.KernelBooleanHistory.Select(CaptureKernelBoolean).ToList(),
                SlopeImageUnderlay = CaptureSlopeImageUnderlay(g, projectPath)
            },
            Materials = new XfemMaterialsState
            {
                SelectedFailureCriterion = vm.MaterialPropertiesVM.SelectedFailureCriterion,
                SelectedMaterialType = vm.MaterialPropertiesVM.SelectedMaterialType,
                SelectedAnalysisCategory = vm.MaterialPropertiesVM.SelectedAnalysisCategory,
                SelectedDetailedAnalysisType = vm.MaterialPropertiesVM.SelectedDetailedAnalysisType,
                LastGroundwaterTreeMode = vm.MaterialPropertiesVM.LastGroundwaterTreeMode,
                LastSteadyStateSolverType = vm.MaterialPropertiesVM.LastSteadyStateSolverType,
                LastTransientSolverType = vm.MaterialPropertiesVM.LastTransientSolverType,
                ShowDeterministicTab = vm.MaterialPropertiesVM.ShowDeterministicTab,
                ShowProbabilisticTab = vm.MaterialPropertiesVM.ShowProbabilisticTab,
                SelectedMaterialLibraryId = vm.MaterialPropertiesVM.SelectedMaterialModel?.Id,
                SelectedMaterialsLayerDisplayNumber = vm.MaterialPropertiesVM.SelectedLayerSummaryForMaterials?.DisplayLayerNumber,
                SelectedLayerAssignmentDisplayLayerNumber = vm.MaterialPropertiesVM.SelectedLayerAssignmentRow?.DisplayLayerNumber,
                MaterialLibrary = vm.MaterialPropertiesVM.Materials.Select(CaptureMaterialLibraryEntry).ToList()
            },
            Mesh = mesh,
            Loads = loads,
            GroundwaterHeads = waterHeads.Count > 0 ? waterHeads : null,
            TransientFixedPressures = transientFixedPressures.Count > 0
                ? transientFixedPressures
                : null,
            BoundaryConditions = new XfemBoundaryConditionsState
            {
                Rows = bcs,
                StatusMessage = vm.BoundaryConditionsVM.StatusMessage,
                NodesText = vm.BoundaryConditionsVM.NodesText,
                SelectedBCTypeForBoundary = vm.BoundaryConditionsVM.SelectedBCTypeForBoundary,
                BcValueForBoundary = vm.BoundaryConditionsVM.BcValueForBoundary
            },
            FeAnalysis = new XfemFeAnalysisState
            {
                StatusMessage = vm.FEAnalysisVM.StatusMessage,
                FeAnalysisSubTabIndex = vm.FEAnalysisVM.FeAnalysisSubTabIndex,
                StagedExcavationEnabled = vm.FEAnalysisVM.StagedExcavationEnabled,
                GravityEnabled = vm.FEAnalysisVM.GravityEnabled,
                GravityAcceleration = vm.FEAnalysisVM.GravityAcceleration,
                LoadSteppingEnabled = vm.FEAnalysisVM.LoadSteppingEnabled,
                NumberOfLoadSteps = vm.FEAnalysisVM.NumberOfLoadSteps,
                CppExecutableDirectory = vm.FEAnalysisVM.CppExecutableDirectory,
                ConvergenceTolerance = vm.FEAnalysisVM.ConvergenceTolerance,
                MaxIterations = vm.FEAnalysisVM.MaxIterations,
                LoadIncrements = vm.FEAnalysisVM.LoadIncrements,
                LayersOverride = vm.FEAnalysisVM.LayersOverride,
                SrfTrialCount = vm.FEAnalysisVM.SrfTrialCount,
                SrfValues = vm.FEAnalysisVM.SrfValues.Select(e => e.Value).ToList(),
                SrfSearchMode = vm.FEAnalysisVM.SelectedSrfSearchMode.ToString(),
                SrfInsituMode = vm.FEAnalysisVM.SelectedSrfInsituMode.ToString(),
                AutoDetectSrfBracket = vm.FEAnalysisVM.AutoDetectSrfBracket,
                SrfBracketSeed = vm.FEAnalysisVM.SrfBracketSeed,
                SrfBracketExpansionFactor = vm.FEAnalysisVM.SrfBracketExpansionFactor,
                SrfMaxExpansionSteps = vm.FEAnalysisVM.SrfMaxExpansionSteps,
                SrfFoSMin = vm.FEAnalysisVM.SrfFoSMin,
                SrfFoSMax = vm.FEAnalysisVM.SrfFoSMax,
                SrfBracketTolerance = vm.FEAnalysisVM.SrfBracketTolerance,
                SrfMaxBracketIterations = vm.FEAnalysisVM.SrfMaxBracketIterations,
                StageDefinitions = vm.FEAnalysisVM.StageDefinitions.Select(s => new XfemStageDefinition
                {
                    StageNumber = s.StageNumber,
                    StageName = s.StageName,
                    StageType = s.StageType,
                    Description = s.Description,
                    IsActive = s.IsActive,
                    Duration = s.Duration,
                    Units = s.Units,
                    StartsFromStageNumber = s.StartsFromStageNumber,
                    ResetDisplacementsAtStart = s.ResetDisplacementsAtStart,
                    StressReleaseLambda = s.StressReleaseLambda,
                    LoadFactor = s.LoadFactor,
                    SolverMode = s.SolverMode,
                    MaxIterations = s.MaxIterations,
                    Tolerance = s.Tolerance,
                    MaxLoadSteps = s.MaxLoadSteps,
                    ArcLength = s.ArcLength,
                    ActivateSurfaceNumbers = s.ActivateSurfaceNumbers.Count > 0 ? s.ActivateSurfaceNumbers.ToList() : null,
                    DeactivateSurfaceNumbers = s.DeactivateSurfaceNumbers.Count > 0 ? s.DeactivateSurfaceNumbers.ToList() : null,
                    LiningEnabled = s.LiningEnabled,
                    BoltsEnabled = s.BoltsEnabled,
                    GroundwaterLineTag = s.GroundwaterLineTag,
                    ExportUx = s.ExportUx,
                    ExportUy = s.ExportUy,
                    ExportSxx = s.ExportSxx,
                    ExportSyy = s.ExportSyy,
                    ExportTxy = s.ExportTxy,
                    ExportPlasticZones = s.ExportPlasticZones,
                    ExportFosHistory = s.ExportFosHistory,
                    Notes = string.IsNullOrWhiteSpace(s.Notes) ? null : s.Notes,
                    PseudoStaticKh = s.PseudoStaticKh,
                    PseudoStaticKv = s.PseudoStaticKv
                }).ToList()
            },
            Results = new XfemResultsState
            {
                ResultsSubTabIndex = vm.ResultsVM.ResultsSubTabIndex,
                VectorOutputType = vm.ResultsVM.VectorOutputType,
                VectorDisplacementComponent = vm.ResultsVM.VectorDisplacementComponent,
                VectorScale = vm.ResultsVM.VectorScale,
                DeformedMeshExaggeration = vm.ResultsVM.DeformedMeshExaggeration,
                ShowUndeformedOutline = vm.ResultsVM.ShowUndeformedOutline,
                ShowMesh = vm.ResultsVM.ShowMesh,
                IsLinePlotMode = vm.ResultsVM.IsLinePlotMode,
                SelectedOutputType = vm.ResultsVM.SelectedOutputType,
                VisualizationMode = vm.ResultsVM.VisualizationMode,
                FeStatusMessage = vm.ResultsVM.FeStatusMessage,
                FeRun = XfemFeRunSnapshot.Capture(vm.ResultsVM),
            },
            Script = new XfemScriptState
            {
                ScriptContent = vm.ScriptVM.ScriptContent,
                OutputText = vm.ScriptVM.OutputText,
                StatusMessage = vm.ScriptVM.StatusMessage,
                IsManualContent = vm.ScriptVM.IsManualScriptContent,
                PythonScript = vm.ScriptVM.PythonScript,
                ScriptLanguage = vm.ScriptVM.EditorLanguage.ToString(),
            },
            StagedConstruction = CaptureStagedConstruction(vm.StagedConstructionVM),
            FieldStress = CaptureFieldStress(vm.LoadConditionsVM.FieldStress.Model),
            SeismicLoad = CaptureSeismicLoad(vm.LoadConditionsVM),
            Support = CaptureSupport(vm.SupportVM),
            Wave3 = CaptureWave3(vm),
            AnalysisWorkspace = CaptureAnalysisWorkspace(vm),
            UserDefinedMesh = CaptureUserDefinedMesh(vm.MeshVM.UserDefinedMeshVM),
            Verification = CaptureVerification(vm.VerificationVM),
            SnapSettings = vm.GeometryDefinitionVM.SnapSettings.Clone(),
        };
    }

    private static XfemAnalysisWorkspaceState? CaptureAnalysisWorkspace(MainWindowViewModel vm)
    {
        if (vm.Workspace == null)
            return null;
        var s = vm.Workspace.Settings;
        var layout = Services.Appearance.UiAppearanceService.Instance.Settings;
        return new XfemAnalysisWorkspaceState
        {
            Intent = s.Intent.ToString(),
            InitialStrengthReductionFactor = s.InitialStrengthReductionFactor,
            StrengthReductionIncrement = s.StrengthReductionIncrement,
            CheckDisplacementJump = s.CheckDisplacementJump,
            CheckNonConvergence = s.CheckNonConvergence,
            CheckPlasticZoneConnect = s.CheckPlasticZoneConnect,
            PorePressureOption = s.PorePressureOption,
            ExplorerWidth = layout.ExplorerWidth,
            ResultsWidth = layout.ResultsWidth,
            SolverLogHeight = layout.SolverLogHeight
        };
    }

    private static void ApplyAnalysisWorkspace(MainWindowViewModel vm, XfemAnalysisWorkspaceState dto)
    {
        if (Enum.TryParse<AnalysisIntent>(dto.Intent, out var intent))
            vm.Workspace.Settings.Intent = intent;
        vm.Workspace.Settings.InitialStrengthReductionFactor = dto.InitialStrengthReductionFactor;
        vm.Workspace.Settings.StrengthReductionIncrement = dto.StrengthReductionIncrement;
        vm.Workspace.Settings.CheckDisplacementJump = dto.CheckDisplacementJump;
        vm.Workspace.Settings.CheckNonConvergence = dto.CheckNonConvergence;
        vm.Workspace.Settings.CheckPlasticZoneConnect = dto.CheckPlasticZoneConnect;
        if (!string.IsNullOrWhiteSpace(dto.PorePressureOption))
            vm.Workspace.Settings.PorePressureOption = dto.PorePressureOption;
        if (dto.ExplorerWidth is > 80)
            Services.Appearance.UiAppearanceService.Instance.Settings.ExplorerWidth = dto.ExplorerWidth.Value;
        if (dto.ResultsWidth is > 80)
            Services.Appearance.UiAppearanceService.Instance.Settings.ResultsWidth = dto.ResultsWidth.Value;
        if (dto.SolverLogHeight is > 40)
            Services.Appearance.UiAppearanceService.Instance.Settings.SolverLogHeight = dto.SolverLogHeight.Value;
    }

    private static XfemWave3State CaptureWave3(MainWindowViewModel vm)
    {
        var p = vm.ProbabilisticAnalysisVM.Settings;
        var b = vm.BackAnalysisVM.Settings;
        var s = vm.SensitivityAnalysisVM.Settings;
        var o = vm.ReportGenerationVM.Options;
        return new XfemWave3State
        {
            Engine = p.Engine,
            SampleCount = p.SampleCount,
            DesignFactorOfSafety = p.DesignFactorOfSafety,
            RandomSeed = p.RandomSeed,
            UseFixedSeed = p.UseFixedSeed,
            SensitivityVariationPercent = s.MaterialVariationPercent,
            BackTargetFs = b.TargetFs,
            BackFsTolerance = b.FsTolerance,
            BackMaxIterations = b.MaxIterations,
            Report = new XfemReportOptionsState
            {
                Company = o.Company,
                ReportTitle = o.ReportTitle,
                Client = o.Client,
                Location = o.Location,
                DocumentNumber = o.DocumentNumber,
                Revision = o.Revision,
                PreparedBy = o.PreparedBy,
                CheckedBy = o.CheckedBy,
                Notes = o.Notes,
                IncludeCoverPage = o.IncludeCoverPage,
                IncludeMaterials = o.IncludeMaterials,
                IncludeGroundwater = o.IncludeGroundwater,
                IncludeValidation = o.IncludeValidation,
                IncludeSensitivity = o.IncludeSensitivity,
                IncludeProbabilistic = o.IncludeProbabilistic,
                IncludeBackAnalysis = o.IncludeBackAnalysis,
            },
            Cases = vm.AnalysisCasesVM.CaseSet.Cases.Select(c => new XfemAnalysisCaseState
            {
                Name = c.Name,
                IsActive = c.IsActive,
                SeismicKh = c.SeismicKh,
                SeismicKv = c.SeismicKv,
                LoadFactor = c.LoadFactor,
                Notes = c.Notes,
                LastFoS = c.LastFoS,
                Status = c.Status,
            }).ToList(),
        };
    }

    private static void ApplyWave3(MainWindowViewModel vm, XfemWave3State dto)
    {
        var p = vm.ProbabilisticAnalysisVM.Settings;
        p.Engine = dto.Engine;
        vm.ProbabilisticAnalysisVM.SelectedEngine = dto.Engine;
        p.SampleCount = dto.SampleCount;
        p.DesignFactorOfSafety = dto.DesignFactorOfSafety;
        p.RandomSeed = dto.RandomSeed;
        p.UseFixedSeed = dto.UseFixedSeed;
        vm.SensitivityAnalysisVM.Settings.MaterialVariationPercent = dto.SensitivityVariationPercent;
        var b = vm.BackAnalysisVM.Settings;
        b.TargetFs = dto.BackTargetFs;
        b.FsTolerance = dto.BackFsTolerance;
        b.MaxIterations = dto.BackMaxIterations;
        if (dto.Report != null)
        {
            var o = vm.ReportGenerationVM.Options;
            o.Company = dto.Report.Company ?? "";
            o.ReportTitle = dto.Report.ReportTitle ?? o.ReportTitle;
            o.Client = dto.Report.Client ?? "";
            o.Location = dto.Report.Location ?? "";
            o.DocumentNumber = dto.Report.DocumentNumber ?? "";
            o.Revision = dto.Report.Revision ?? "A";
            o.PreparedBy = dto.Report.PreparedBy ?? "";
            o.CheckedBy = dto.Report.CheckedBy ?? "";
            o.Notes = dto.Report.Notes ?? "";
            o.IncludeCoverPage = dto.Report.IncludeCoverPage;
            o.IncludeMaterials = dto.Report.IncludeMaterials;
            o.IncludeGroundwater = dto.Report.IncludeGroundwater;
            o.IncludeValidation = dto.Report.IncludeValidation;
            o.IncludeSensitivity = dto.Report.IncludeSensitivity;
            o.IncludeProbabilistic = dto.Report.IncludeProbabilistic;
            o.IncludeBackAnalysis = dto.Report.IncludeBackAnalysis;
        }

        vm.AnalysisCasesVM.CaseSet.Cases.Clear();
        foreach (var c in dto.Cases ?? new List<XfemAnalysisCaseState>())
        {
            vm.AnalysisCasesVM.CaseSet.Cases.Add(new FemAnalysisCase
            {
                Name = c.Name ?? "Case",
                IsActive = c.IsActive,
                SeismicKh = c.SeismicKh,
                SeismicKv = c.SeismicKv,
                LoadFactor = c.LoadFactor,
                Notes = c.Notes ?? "",
                LastFoS = c.LastFoS,
                Status = c.Status ?? "",
            });
        }
        if (vm.AnalysisCasesVM.CaseSet.Cases.Count == 0)
            vm.AnalysisCasesVM.CaseSet.AddDefault();
        vm.AnalysisCasesVM.SelectedCase = vm.AnalysisCasesVM.CaseSet.Cases[0];
    }

    private static XfemFieldStressState? CaptureFieldStress(FieldStressModel? m)
    {
        if (m == null) return null;
        return new XfemFieldStressState
        {
            Global = CaptureFieldStressSpec(m.Global),
            Overrides = m.Overrides.Count == 0
                ? null
                : m.Overrides.Select(o => new XfemFieldStressOverride
                {
                    MaterialIndex = o.MaterialIndex,
                    MaterialName = string.IsNullOrWhiteSpace(o.MaterialName) ? null : o.MaterialName,
                    Spec = CaptureFieldStressSpec(o.Spec)
                }).ToList(),
            TractionGroups = m.TractionGroups.Count == 0
                ? null
                : m.TractionGroups.Select(g => new XfemTractionGroup
                {
                    Mode = g.Mode,
                    Sxx = g.Sxx,
                    Syy = g.Syy,
                    Txy = g.Txy,
                    Edges = g.Edges.Select(e => new XfemTractionEdge
                    {
                        NodeA = e.NodeA,
                        NodeB = e.NodeB,
                        NodeC = e.NodeC,
                    }).ToList(),
                }).ToList()
        };
    }

    private static XfemSeismicLoadState? CaptureSeismicLoad(LoadConditionsViewModel loads)
    {
        if (!loads.SeismicAnalysisEnabled && !loads.ScriptPseudoStaticCoeffsActive && !loads.Newmark.IsEnabled)
            return null;

        return new XfemSeismicLoadState
        {
            SeismicAnalysisEnabled = loads.SeismicAnalysisEnabled,
            ScriptPseudoStaticKh = loads.SeismicLegendKh,
            ScriptPseudoStaticKv = loads.SeismicLegendKv,
            Newmark = CaptureNewmark(loads.Newmark)
        };
    }

    private static XfemNewmarkState CaptureNewmark(FeNewmarkSettings s) => new()
    {
        IsEnabled = s.IsEnabled,
        YieldAcceleration = s.YieldAcceleration,
        EstimateKyFromSrf = s.EstimateKyFromSrf,
        PgaG = s.PgaG,
        Magnitude = s.Magnitude,
        AriasIntensity = s.AriasIntensity,
        SlidingMassPeriodTs = s.SlidingMassPeriodTs,
        SpectralAccelerationG = s.SpectralAccelerationG,
        FundamentalPeriodT0 = s.FundamentalPeriodT0,
        UseJibson = s.UseJibson,
        UseJibsonMagnitude = s.UseJibsonMagnitude,
        UseJibsonArias = s.UseJibsonArias,
        UseAmbraseys = s.UseAmbraseys,
        UseMakdisiSeed = s.UseMakdisiSeed,
        UseBrayTravasarou = s.UseBrayTravasarou,
        UseTimeHistory = s.UseTimeHistory,
        TimeHistoryPath = s.TimeHistoryPath,
        TimeHistoryUnits = s.TimeHistoryUnits,
        TimeHistoryScale = s.TimeHistoryScale,
        TwoWaySliding = s.TwoWaySliding
    };

    private static void ApplyNewmark(FeNewmarkSettings dest, XfemNewmarkState src)
    {
        dest.IsEnabled = src.IsEnabled;
        dest.YieldAcceleration = src.YieldAcceleration;
        dest.EstimateKyFromSrf = src.EstimateKyFromSrf;
        dest.PgaG = src.PgaG;
        dest.Magnitude = src.Magnitude;
        dest.AriasIntensity = src.AriasIntensity;
        dest.SlidingMassPeriodTs = src.SlidingMassPeriodTs;
        dest.SpectralAccelerationG = src.SpectralAccelerationG;
        dest.FundamentalPeriodT0 = src.FundamentalPeriodT0;
        dest.UseJibson = src.UseJibson;
        dest.UseJibsonMagnitude = src.UseJibsonMagnitude;
        dest.UseJibsonArias = src.UseJibsonArias;
        dest.UseAmbraseys = src.UseAmbraseys;
        dest.UseMakdisiSeed = src.UseMakdisiSeed;
        dest.UseBrayTravasarou = src.UseBrayTravasarou;
        dest.UseTimeHistory = src.UseTimeHistory;
        dest.TimeHistoryPath = src.TimeHistoryPath ?? "";
        dest.TimeHistoryUnits = src.TimeHistoryUnits;
        dest.TimeHistoryScale = src.TimeHistoryScale;
        dest.TwoWaySliding = src.TwoWaySliding;
    }

    private static XfemSupportState CaptureSupport(SupportViewModel vm) => new()
    {
        PropertySets = vm.PropertySets.Select(p => new XfemSupportPropertySet
        {
            Id = p.Id,
            Name = p.Name,
            Type = p.Type.ToString(),
            AxialStiffnessKn = p.AxialStiffnessKn,
            PrestressKn = p.PrestressKn,
            OutOfPlaneSpacingM = p.OutOfPlaneSpacingM,
            TensileCapacityKn = p.TensileCapacityKn,
            BondStrengthKnPerM = p.BondStrengthKnPerM,
            BondLengthM = p.BondLengthM,
            FreeLengthM = p.FreeLengthM,
            ShearCapacityKn = p.ShearCapacityKn,
            InstallStage = p.InstallStage,
            ExcludeFromSrf = p.ExcludeFromSrf
        }).ToList(),
        Elements = vm.Elements.Select(e => new XfemSupportElement
        {
            Id = e.Id,
            PropertySetId = e.PropertySetId,
            HeadX = e.HeadX,
            HeadY = e.HeadY,
            TailX = e.TailX,
            TailY = e.TailY
        }).ToList()
    };

    private static void ApplySupport(SupportViewModel vm, XfemSupportState dto)
    {
        var sets = (dto.PropertySets ?? new List<XfemSupportPropertySet>()).Select(p =>
        {
            Enum.TryParse<FeSupportType>(p.Type, ignoreCase: true, out var type);
            return new FeSupportPropertySet
            {
                Id = p.Id,
                Name = p.Name ?? $"Support {p.Id}",
                Type = type,
                AxialStiffnessKn = p.AxialStiffnessKn,
                PrestressKn = p.PrestressKn,
                OutOfPlaneSpacingM = p.OutOfPlaneSpacingM > 0 ? p.OutOfPlaneSpacingM : 1.5,
                TensileCapacityKn = p.TensileCapacityKn,
                BondStrengthKnPerM = p.BondStrengthKnPerM,
                BondLengthM = p.BondLengthM,
                FreeLengthM = p.FreeLengthM,
                ShearCapacityKn = p.ShearCapacityKn,
                InstallStage = p.InstallStage,
                ExcludeFromSrf = p.ExcludeFromSrf
            };
        });
        var elements = (dto.Elements ?? new List<XfemSupportElement>()).Select(e => new FeSupportElement
        {
            Id = e.Id,
            PropertySetId = e.PropertySetId,
            HeadX = e.HeadX,
            HeadY = e.HeadY,
            TailX = e.TailX,
            TailY = e.TailY
        });
        vm.ReplaceFrom(sets, elements);
    }

    private static XfemDrawdownState CaptureDrawdown(FeDrawdownSettings s) => new()
    {
        IsEnabled = s.IsEnabled,
        Method = s.Method.ToString(),
        PreReservoirLevel = s.PreReservoirLevel,
        PostReservoirLevel = s.PostReservoirLevel,
        TakeLowerOfUndrainedAndDrained = s.TakeLowerOfUndrainedAndDrained,
        PreDrawdownSurface = s.PreDrawdownSurface.Select(p => new XfemPoint(p.X, p.Y)).ToList(),
        PostDrawdownSurface = s.PostDrawdownSurface.Select(p => new XfemPoint(p.X, p.Y)).ToList()
    };

    private static void ApplyDrawdown(FeDrawdownSettings dest, XfemDrawdownState src)
    {
        dest.IsEnabled = src.IsEnabled;
        if (Enum.TryParse<FeDrawdownMethod>(src.Method, ignoreCase: true, out var method))
            dest.Method = method;
        dest.PreReservoirLevel = src.PreReservoirLevel;
        dest.PostReservoirLevel = src.PostReservoirLevel;
        dest.TakeLowerOfUndrainedAndDrained = src.TakeLowerOfUndrainedAndDrained;
        dest.PreDrawdownSurface.Clear();
        dest.PostDrawdownSurface.Clear();
        if (src.PreDrawdownSurface != null)
        {
            foreach (var p in src.PreDrawdownSurface)
                dest.PreDrawdownSurface.Add(new FeDrawdownWaterPoint { X = p.X, Y = p.Y });
        }
        if (src.PostDrawdownSurface != null)
        {
            foreach (var p in src.PostDrawdownSurface)
                dest.PostDrawdownSurface.Add(new FeDrawdownWaterPoint { X = p.X, Y = p.Y });
        }
    }

    private static XfemFieldStressSpec CaptureFieldStressSpec(FieldStressSpec s) => new()
    {
        Mode = s.Mode,
        Sigma1 = s.Sigma1,
        Sigma3 = s.Sigma3,
        SigmaZ = s.SigmaZ,
        AngleDeg = s.AngleDeg,
        DirectSxx = s.DirectSxx,
        DirectSyy = s.DirectSyy,
        DirectTxy = s.DirectTxy,
        Kx = s.Kx,
        Kz = s.Kz,
        AutoSurface = s.AutoSurface,
        YSurface = s.YSurface,
        UseEffectiveStressRatio = s.UseEffectiveStressRatio
    };

    private static FieldStressModel? BuildFieldStressModel(XfemFieldStressState? dto)
    {
        if (dto == null) return null;
        var model = new FieldStressModel();
        if (dto.Global != null) ApplySpec(model.Global, dto.Global);
        if (dto.Overrides != null)
        {
            foreach (var o in dto.Overrides)
            {
                var ov = new FieldStressOverride
                {
                    MaterialIndex = o.MaterialIndex,
                    MaterialName = o.MaterialName ?? "",
                    Spec = new FieldStressSpec()
                };
                if (o.Spec != null) ApplySpec(ov.Spec, o.Spec);
                model.Overrides.Add(ov);
            }
        }
        if (dto.TractionGroups != null)
        {
            foreach (var dg in dto.TractionGroups)
            {
                var grp = new TractionGroup
                {
                    Mode = dg.Mode,
                    Sxx = dg.Sxx,
                    Syy = dg.Syy,
                    Txy = dg.Txy,
                };
                if (dg.Edges != null)
                {
                    foreach (var de in dg.Edges)
                    {
                        grp.Edges.Add(new TractionEdge
                        {
                            NodeA = de.NodeA,
                            NodeB = de.NodeB,
                            NodeC = de.NodeC,
                        });
                    }
                }
                model.TractionGroups.Add(grp);
            }
        }
        return model;

        static void ApplySpec(FieldStressSpec target, XfemFieldStressSpec src)
        {
            target.Mode = src.Mode;
            target.Sigma1 = src.Sigma1;
            target.Sigma3 = src.Sigma3;
            target.SigmaZ = src.SigmaZ;
            target.AngleDeg = src.AngleDeg;
            target.DirectSxx = src.DirectSxx;
            target.DirectSyy = src.DirectSyy;
            target.DirectTxy = src.DirectTxy;
            target.Kx = src.Kx;
            target.Kz = src.Kz;
            target.AutoSurface = src.AutoSurface;
            target.YSurface = src.YSurface;
            target.UseEffectiveStressRatio = src.UseEffectiveStressRatio;
        }
    }

    private static XfemPoint ToAxis(GeometryDefinitionViewModel g, Point canvasPt)
    {
        var a = g.ConvertCanvasToAxis(canvasPt);
        return new XfemPoint(a.X, a.Y);
    }

    private static List<XfemWaterPressureNamedGrid>? SnapshotNamedWaterPressureGrids(
        System.Collections.ObjectModel.ObservableCollection<WaterPressureGrid> grids)
    {
        if (grids.Count == 0)
            return null;

        var list = new List<XfemWaterPressureNamedGrid>();
        foreach (var g in grids)
        {
            var points = g.Points
                .Where(p => Math.Abs(p.X) > 1e-15 || Math.Abs(p.Y) > 1e-15 || Math.Abs(p.Value) > 1e-15)
                .Select(p => new XfemWaterPressureGridPoint { X = p.X, Y = p.Y, Value = p.Value })
                .ToList();
            list.Add(new XfemWaterPressureNamedGrid
            {
                Name = g.Name,
                GridType = g.GridType.ToPersistenceString(),
                Points = points.Count == 0 ? null : points
            });
        }
        return list.Count == 0 ? null : list;
    }

    private static List<XfemWaterPressureGridPoint>? SnapshotWaterPressureGridByType(
        System.Collections.ObjectModel.ObservableCollection<WaterPressureGrid> grids,
        WaterPressureGridType type)
    {
        var list = grids
            .Where(g => g.GridType == type)
            .SelectMany(g => g.Points)
            .Where(p => Math.Abs(p.X) > 1e-15 || Math.Abs(p.Y) > 1e-15 || Math.Abs(p.Value) > 1e-15)
            .Select(p => new XfemWaterPressureGridPoint { X = p.X, Y = p.Y, Value = p.Value })
            .ToList();
        return list.Count == 0 ? null : list;
    }

    private static WaterPressureGridData? ToWaterPressureGridData(XfemWaterPressureNamedGrid? dto)
    {
        if (dto == null)
            return null;
        if (!WaterPressureGridTypeExtensions.TryParse(dto.GridType, out var type))
            return null;
        return new WaterPressureGridData
        {
            Name = string.IsNullOrWhiteSpace(dto.Name) ? "Default Grid" : dto.Name.Trim(),
            GridType = type,
            Points = dto.Points?.Select(p => (p.X, p.Y, p.Value)).ToList() ?? new()
        };
    }

    private static XfemInternalEdge CaptureInternalEdge(GeometryInternalEdge e)
    {
        if (e.IsArc)
        {
            return new XfemInternalEdge
            {
                IsArc = true,
                StableMin = e.StableMin,
                StableMax = e.StableMax,
                StableMid = e.StableMid,
                StableStart = e.StableStart,
                StableEnd = e.StableEnd
            };
        }

        return new XfemInternalEdge { IsArc = false, StableMin = e.StableMin, StableMax = e.StableMax };
    }

    private static XfemGeometrySurface CaptureGeometrySurface(GeometrySurface s) => new()
    {
        SurfaceNumber = s.SurfaceNumber,
        SurfaceName = s.SurfaceName,
        SelectedLineNumbers = s.PickedLineNumbers.Count > 0 ? s.PickedLineNumbers.ToList() : null,
        Tag = s.Tag,
        ColorIndex = s.ColorIndex,
        AssignedMaterialId = s.AssignedMaterialId,
        IsPolygonLocked = s.IsPolygonLocked,
        LockedPolygonVertexLabels = s.LockedPolygonVertexLabels.Count > 0 ? s.LockedPolygonVertexLabels.ToList() : null,
        DraftPolygonVertexLabels = s.DraftPolygonVertexLabels.Count > 0 ? s.DraftPolygonVertexLabels.ToList() : null,
        ConnectedLineNumbers = s.ConnectedLineNumbers.Count > 0 ? s.ConnectedLineNumbers.ToList() : null
    };

    private static XfemLineSegment CaptureSegment(GeometryLineSegment s) => new()
    {
        LineNumber = s.LineNumber,
        FromPoint = s.FromPoint,
        ToPoint = s.ToPoint,
        IsBoundaryEdge = s.IsBoundaryEdge,
        IsArc = s.IsArc,
        ThroughPoint = s.ThroughPoint,
        ArcRadius = s.ArcRadius,
        IsApplied = s.IsApplied,
        Tag = s.Tag,
        UnitWeight = s.UnitWeight,
        IsSaturatedUnitWeightEnabled = s.IsSaturatedUnitWeightEnabled,
        SaturatedUnitWeight = s.SaturatedUnitWeight,
        Cohesion = s.Cohesion,
        FrictionAngle = s.FrictionAngle,
        YoungsModulus = s.YoungsModulus,
        PoissonsRatio = s.PoissonsRatio,
        Density = s.Density,
        ConsiderDensityForFE = s.ConsiderDensityForFE,
        PlaneStrain = s.PlaneStrain
    };

    private static XfemLoad CaptureLoad(LoadModel l, GeometryDefinitionViewModel geometry)
    {
        LoadPlacementCoords.WriteAxisForPersist(l, geometry, out var sx, out var sy, out var ex, out var ey);
        return new()
        {
            Type = l.Type,
            Direction = l.Direction,
            Magnitude = l.Magnitude,
            EndMagnitude = l.EndMagnitude,
            StartX = sx,
            StartY = sy,
            EndX = ex,
            EndY = ey,
            AppliedToNodeId = l.AppliedToNodeId,
            StageNumber = l.StageNumber,
            Name = string.IsNullOrWhiteSpace(l.Name) ? null : l.Name,
            Orientation = l.Orientation,
            AngleDeg = Math.Abs(l.AngleDeg) > 1e-12 || l.IsAngleOrientation ? l.AngleDeg : null,
            IsStageLoad = l.IsStageLoad ? true : null,
            IsEnabled = l.IsEnabled,
            IdCode = string.IsNullOrWhiteSpace(l.IdCode) ? null : l.IdCode,
            ApplyTo = string.IsNullOrWhiteSpace(l.ApplyTo) ? null : l.ApplyTo,
            LoadGroup = string.IsNullOrWhiteSpace(l.LoadGroup) ? null : l.LoadGroup,
            CoordinateSystem = string.IsNullOrWhiteSpace(l.CoordinateSystem) ? null : l.CoordinateSystem,
            Distribution = l.Distribution,
            LoadProjection = string.IsNullOrWhiteSpace(l.LoadProjection) ? null : l.LoadProjection,
            LoadDepth = Math.Abs(l.LoadDepth) > 1e-12 ? l.LoadDepth : null,
            UseProjection = l.UseProjection ? true : null,
            HasRampTimeDependency = l.HasRampTimeDependency ? true : null,
            ApplyFromStage = l.ApplyFromStage,
            ApplyToStage = l.ApplyToStage,
            Notes = string.IsNullOrWhiteSpace(l.Notes) ? null : l.Notes,
        };
    }

    private static XfemWaterHead CaptureWaterHead(WaterHeadModel h) => new()
    {
        Name = string.IsNullOrWhiteSpace(h.Name) ? null : h.Name,
        IsEnabled = h.IsEnabled,
        Role = h.Role.ToString(),
        DefinitionMethod = h.DefinitionMethod.ToString(),
        Head = h.Head,
        PlacementType = h.PlacementType.ToString(),
        StartX = h.StartX,
        StartY = h.StartY,
        EndX = h.EndX,
        EndY = h.EndY,
        AppliedToNodeId = h.AppliedToNodeId,
        HasPlacement = h.HasPlacement
    };

    private static WaterHeadModel RestoreWaterHead(XfemWaterHead h)
    {
        Enum.TryParse<WaterHeadRole>(h.Role, ignoreCase: true, out var role);
        Enum.TryParse<WaterHeadDefinitionMethod>(h.DefinitionMethod, ignoreCase: true, out var method);
        Enum.TryParse<WaterHeadPlacementType>(h.PlacementType, ignoreCase: true, out var placement);
        return new WaterHeadModel
        {
            Name = h.Name ?? "",
            IsEnabled = h.IsEnabled ?? true,
            Role = role,
            DefinitionMethod = method == 0 && string.IsNullOrEmpty(h.DefinitionMethod)
                ? WaterHeadDefinitionMethod.UserDefined
                : method,
            Head = h.Head,
            PlacementType = placement == 0 && string.IsNullOrEmpty(h.PlacementType)
                ? WaterHeadPlacementType.Point
                : placement,
            StartX = h.StartX,
            StartY = h.StartY,
            EndX = h.EndX,
            EndY = h.EndY,
            AppliedToNodeId = h.AppliedToNodeId,
            HasPlacement = h.HasPlacement ?? (h.AppliedToNodeId != null
                || Math.Abs(h.StartX) > 1e-12 || Math.Abs(h.StartY) > 1e-12
                || Math.Abs(h.EndX) > 1e-12 || Math.Abs(h.EndY) > 1e-12)
        };
    }

    private static XfemTransientFixedPressure CaptureTransientFixedPressure(
        TransientFixedPressureNode node) => new()
    {
        Name = string.IsNullOrWhiteSpace(node.Name) ? null : node.Name,
        IsEnabled = node.IsEnabled,
        DefinitionMethod = node.DefinitionMethod.ToString(),
        Face = node.Face.ToString(),
        PlacementType = node.PlacementType.ToString(),
        Pressure = node.Pressure,
        StartX = node.StartX,
        StartY = node.StartY,
        EndX = node.EndX,
        EndY = node.EndY,
        NodeId = node.NodeId,
        HasPlacement = node.HasPlacement
    };

    private static TransientFixedPressureNode RestoreTransientFixedPressure(
        XfemTransientFixedPressure node)
    {
        Enum.TryParse<WaterHeadDefinitionMethod>(node.DefinitionMethod, ignoreCase: true, out var definition);
        Enum.TryParse<TransientDrainFace>(node.Face, ignoreCase: true, out var face);
        Enum.TryParse<WaterHeadPlacementType>(node.PlacementType, ignoreCase: true, out var placement);
        bool hasCoords = Math.Abs(node.StartX) > 1e-12 || Math.Abs(node.StartY) > 1e-12
            || Math.Abs(node.AxisX) > 1e-12 || Math.Abs(node.AxisY) > 1e-12
            || Math.Abs(node.EndX) > 1e-12 || Math.Abs(node.EndY) > 1e-12;
        return new TransientFixedPressureNode
        {
            Name = node.Name ?? "",
            IsEnabled = node.IsEnabled ?? true,
            DefinitionMethod = string.IsNullOrEmpty(node.DefinitionMethod)
                ? (node.NodeId > 0 || node.HasPlacement == true
                    ? WaterHeadDefinitionMethod.MouseSelect
                    : WaterHeadDefinitionMethod.UserDefined)
                : definition,
            Face = string.IsNullOrEmpty(node.Face) ? TransientDrainFace.Top : face,
            PlacementType = string.IsNullOrEmpty(node.PlacementType)
                ? WaterHeadPlacementType.Point
                : placement,
            Pressure = node.Pressure,
            StartX = Math.Abs(node.StartX) > 1e-12 || Math.Abs(node.StartY) > 1e-12 ? node.StartX : node.AxisX,
            StartY = Math.Abs(node.StartX) > 1e-12 || Math.Abs(node.StartY) > 1e-12 ? node.StartY : node.AxisY,
            EndX = node.EndX,
            EndY = node.EndY,
            NodeId = node.NodeId,
            HasPlacement = node.HasPlacement ?? (node.NodeId > 0 || hasCoords)
        };
    }

    private static XfemBoundaryRow CaptureBc(BoundaryConditionRow r) => new()
    {
        NodeId = r.NodeId,
        BoundaryName = r.BoundaryName,
        BCType = r.BCType,
        AnalysisCategory = string.IsNullOrWhiteSpace(r.AnalysisCategory)
            ? BoundaryConditionAnalysisCategories.Resolve(r.BCType)
            : r.AnalysisCategory,
        DegreeOfFreedom = r.DegreeOfFreedom,
        Value = r.Value,
        Description = r.Description
    };

    private static XfemMeshState CaptureMesh(MeshViewModel m)
    {
        var nodes = m.DiscretizationNodes.Select(n => new XfemPoint(n.Location.X, n.Location.Y)).ToList();
        var elements = m.MeshElements.Select(e => new XfemMeshElement
        {
            DisplayId = e.DisplayId,
            IsActive = e.IsActive,
            IsHighlighted = e.IsHighlighted,
            IsQuadraticTriangle = e.IsQuadraticTriangle,
            IsQuadrilateral = e.IsQuadrilateral,
            IsQuadraticQuad = e.IsQuadraticQuad,
            IsUnmatchedTriangle = e.IsUnmatchedTriangle,
            CenterX = e.CenterX,
            CenterY = e.CenterY,
            NodePoints = e.Nodes.Select(p => new XfemPoint(p.X, p.Y)).ToList()
        }).ToList();
        var rows = m.MeshInformationRows.Select(r => new XfemMeshInfoRow
        {
            ElementId = r.ElementId,
            Node1 = r.Node1,
            Node2 = r.Node2,
            Node3 = r.Node3,
            Node4 = r.Node4,
            Node5 = r.Node5,
            Node6 = r.Node6,
            Node7 = r.Node7,
            Node8 = r.Node8,
            Etype = r.Etype,
            N1X = r.N1X,
            N1Y = r.N1Y,
            N2X = r.N2X,
            N2Y = r.N2Y,
            N3X = r.N3X,
            N3Y = r.N3Y,
            N4X = r.N4X,
            N4Y = r.N4Y,
            N5X = r.N5X,
            N5Y = r.N5Y,
            N6X = r.N6X,
            N6Y = r.N6Y,
            N7X = r.N7X,
            N7Y = r.N7Y,
            N8X = r.N8X,
            N8Y = r.N8Y,
            Kind = string.IsNullOrEmpty(r.Kind) ? null : r.Kind,
            Status = r.Status,
            SmithEmb2dLocalOrder = r.SmithEmb2dLocalOrder,
        }).ToList();

        return new XfemMeshState
        {
            OneDMeshLength = m.OneDMeshLength,
            OneDMeshN = m.OneDMeshN,
            OneDMeshStatus = m.OneDMeshStatus,
            IsDiscretized = m.IsDiscretized,
            ShowMeshElements = m.ShowMeshElements,
            ShowMeshNodes = m.ShowMeshNodes,
            SelectedTwoDElementFamily = m.SelectedTwoDElementFamily,
            SelectedTwoDNodeOption = m.SelectedTwoDNodeOption,
            SelectedMeshNumberingDirection = m.SelectedMeshNumberingDirection,
            DiscretizationNodes = nodes,
            Elements = elements,
            InformationRows = rows,
            // ── Mesh popup / generator UI (optional in older .xfem files) ──
            MeshPopupSelectedTabIndex = m.MeshPopupSelectedTabIndex,
            MeshElementOrder = m.MeshElementOrder,
            Rs2dMeshType = m.Rs2dMeshType,
            Rs2dElementType = m.Rs2dElementType,
            Rs2dGradationFactor = m.Rs2dGradationFactor,
            Rs2dExcavationNodeCount = m.Rs2dExcavationNodeCount,
            Rs2dBoundaryNodeCount = string.IsNullOrEmpty(m.Rs2dBoundaryNodeCount) ? null : m.Rs2dBoundaryNodeCount,
            Rs2dTargetElementCount = string.IsNullOrEmpty(m.Rs2dTargetElementCount) ? null : m.Rs2dTargetElementCount,
            Rs2dAdvancedExpanded = m.Rs2dAdvancedExpanded,
            Rs2dDiscretized = m.Rs2dDiscretized,
            GlobalDensityPreset = m.GlobalDensityPreset,
            GlobalMeshSize = m.GlobalMeshSize,
            NearBoundaryMeshSize = m.NearBoundaryMeshSize,
            SurfaceMeshStatus = m.SurfaceMeshStatus,
            SurfaceMeshRowOverrides = m.SurfaceMeshRows.Count > 0
                ? m.SurfaceMeshRows.Select(r => new XfemSurfaceMeshRow
                {
                    SurfaceNumber = r.SurfaceNumber,
                    MeshSizeOverride = string.IsNullOrEmpty(r.MeshSizeOverride) ? null : r.MeshSizeOverride,
                    IsExcluded = r.IsExcluded,
                    Refinement = r.Refinement.ToString()
                }).ToList()
                : null,
            RectangularMeshL = m.RectangularMeshL,
            RectangularMeshH = m.RectangularMeshH,
            RectangularMeshNx = m.RectangularMeshNx,
            RectangularMeshNy = m.RectangularMeshNy,
            RectangularMeshStatus = string.IsNullOrEmpty(m.RectangularMeshStatus) ? null : m.RectangularMeshStatus,
            TriangleMeshStatus = string.IsNullOrEmpty(m.TriangleMeshStatus) ? null : m.TriangleMeshStatus,
            TunnelMeshGenerationMethod = m.TunnelMeshGenerationMethod,
            MeshSmoothingMethod = m.MeshSmoothingMethod,
            SmoothingIterations = m.SmoothingIterations,
            AutoRegenerateMesh = m.AutoRegenerateMesh,
            RefineNearExcavation = m.RefineNearExcavation,
            RefineNearLoads = m.RefineNearLoads,
            RefineNearMaterialInterfaces = m.RefineNearMaterialInterfaces,
            RefineNearStiffnessContrast = m.RefineNearStiffnessContrast,
            TunnelDiameter = m.TunnelDiameter,
            FarFieldEdgeLength = m.FarFieldEdgeLength,
            NearTunnelEdgeLength = m.NearTunnelEdgeLength,
            RingCount = m.RingCount,
            RingGrowthFactor = m.RingGrowthFactor,
            FirstRingThickness = m.FirstRingThickness,
            PointLoadRefinementRadius = m.PointLoadRefinementRadius,
            MinAngleDegrees = m.MinAngleDegrees,
            MaxRefinementIterations = m.MaxRefinementIterations,
            MergeTolerance = m.MergeTolerance,
            TunnelMeshStatus = string.IsNullOrEmpty(m.TunnelMeshStatus) ? null : m.TunnelMeshStatus
        };
    }

    private static XfemStagedConstructionState? CaptureStagedConstruction(StagedConstructionViewModel sc)
    {
        if (sc.Stages.Count == 0) return null;

        var rows = sc.Stages.OrderBy(s => s.StageNumber).Select(stage => new XfemConstructionStageExcavations
        {
            StageNumber = stage.StageNumber,
            StageName = stage.StageName,
            StageType = stage.StageType,
            Description = string.IsNullOrWhiteSpace(stage.Description) ? null : stage.Description,
            IsActive = stage.IsActive,
            ExcavatedSurfaceNumbers = stage.ExcavatedSurfaceNumbers.Count > 0
                ? stage.ExcavatedSurfaceNumbers.OrderBy(x => x).ToList()
                : null,
            ExcavatedElementIds = stage.ExcavatedElementIds.Count > 0
                ? stage.ExcavatedElementIds.ToList()
                : null,
            Zones = stage.ExcavationZones.Count > 0
                ? stage.ExcavationZones.Select(z => new XfemStageExcavationZoneSnapshot
                {
                    SurfaceNumber = z.SurfaceNumber,
                    AppliedInStageNumber = z.AppliedInStageNumber,
                    DisplayLabel = string.IsNullOrWhiteSpace(z.DisplayLabel) ? null : z.DisplayLabel,
                    AxisPolygon = z.AxisPolygon.Select(p => new XfemPoint(p.X, p.Y)).ToList()
                }).ToList()
                : null,
        }).ToList();

        return new XfemStagedConstructionState
        {
            ActiveStageNumber = sc.ActiveStage?.StageNumber,
            Stages = rows,
        };
    }

    private static XfemUserDefinedMeshState? CaptureUserDefinedMesh(UserDefinedMeshViewModel udm)
    {
        if (udm.NodeRows.Count == 0 && udm.ElementRows.Count == 0
            && string.IsNullOrWhiteSpace(udm.ElementType))
            return null;

        return new XfemUserDefinedMeshState
        {
            ElementType = udm.ElementType,
            Q8MeshMode = udm.Q8MeshMode,
            Q8MeshDensity = udm.Q8MeshDensity,
            P64Nx1 = udm.P64Nx1,
            P64Nx2 = udm.P64Nx2,
            P64Ny1 = udm.P64Ny1,
            P64Ny2 = udm.P64Ny2,
            P64W1 = udm.P64W1,
            P64S1 = udm.P64S1,
            P64W2 = udm.P64W2,
            P64H1 = udm.P64H1,
            P64H2 = udm.P64H2,
            RectUseGeometryExtent = udm.RectUseGeometryExtent,
            RectXMin = udm.RectXMin,
            RectXMax = udm.RectXMax,
            RectYMin = udm.RectYMin,
            RectYMax = udm.RectYMax,
            RectElementsX = udm.RectElementsX,
            RectElementsY = udm.RectElementsY,
            Nodes = udm.NodeRows.Select(n => new XfemUdmNodeRow
            {
                NodeId = n.NodeId,
                X = n.X,
                Y = n.Y,
            }).ToList(),
            Elements = udm.ElementRows.Select(e => new XfemUdmElementRow
            {
                ElemId = e.ElemId,
                N1 = e.N1,
                N2 = e.N2,
                N3 = e.N3,
                N4 = e.N4,
                N5 = e.N5,
                N6 = e.N6,
                N7 = e.N7,
                N8 = e.N8,
                Etype = e.Etype,
            }).ToList(),
        };
    }

    private static XfemVerificationState? CaptureVerification(VerificationViewModel v)
    {
        return new XfemVerificationState
        {
            SelectedProblem = v.SelectedProblem,
            TolerancePercent = v.TolerancePercent,
            AutoFromMeta = v.AutoFromMeta,
            E_kPa = v.E_kPa,
            Nu = v.Nu,
            Sigma_v_kPa = v.Sigma_v_kPa,
            K0 = v.K0,
            A = v.A,
            Cx = v.Cx,
            Cy = v.Cy,
            Pi_kPa = v.Pi_kPa,
            PhiDeg = v.PhiDeg,
            C_kPa = v.C_kPa,
            PsiDeg = v.PsiDeg,
            Gamma = v.Gamma,
        };
    }

    private static void ApplyUserDefinedMesh(UserDefinedMeshViewModel udm, XfemUserDefinedMeshState dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.ElementType))
            udm.ElementType = dto.ElementType;
        if (!string.IsNullOrWhiteSpace(dto.Q8MeshMode))
            udm.Q8MeshMode = dto.Q8MeshMode;
        if (!string.IsNullOrWhiteSpace(dto.Q8MeshDensity))
            udm.Q8MeshDensity = dto.Q8MeshDensity;
        if (dto.P64Nx1 != null) udm.P64Nx1 = dto.P64Nx1;
        if (dto.P64Nx2 != null) udm.P64Nx2 = dto.P64Nx2;
        if (dto.P64Ny1 != null) udm.P64Ny1 = dto.P64Ny1;
        if (dto.P64Ny2 != null) udm.P64Ny2 = dto.P64Ny2;
        if (dto.P64W1 != null) udm.P64W1 = dto.P64W1;
        if (dto.P64S1 != null) udm.P64S1 = dto.P64S1;
        if (dto.P64W2 != null) udm.P64W2 = dto.P64W2;
        if (dto.P64H1 != null) udm.P64H1 = dto.P64H1;
        if (dto.P64H2 != null) udm.P64H2 = dto.P64H2;
        if (dto.RectUseGeometryExtent is bool ruge)
            udm.RectUseGeometryExtent = ruge;
        if (dto.RectXMin != null) udm.RectXMin = dto.RectXMin;
        if (dto.RectXMax != null) udm.RectXMax = dto.RectXMax;
        if (dto.RectYMin != null) udm.RectYMin = dto.RectYMin;
        if (dto.RectYMax != null) udm.RectYMax = dto.RectYMax;
        if (dto.RectElementsX != null) udm.RectElementsX = dto.RectElementsX;
        if (dto.RectElementsY != null) udm.RectElementsY = dto.RectElementsY;

        udm.NodeRows.Clear();
        foreach (var n in dto.Nodes ?? new List<XfemUdmNodeRow>())
            udm.NodeRows.Add(new UdmNodeRow { NodeId = n.NodeId, X = n.X ?? "", Y = n.Y ?? "" });

        udm.ElementRows.Clear();
        foreach (var e in dto.Elements ?? new List<XfemUdmElementRow>())
        {
            udm.ElementRows.Add(new UdmElementRow
            {
                ElemId = e.ElemId,
                N1 = e.N1 ?? "",
                N2 = e.N2 ?? "",
                N3 = e.N3 ?? "",
                N4 = e.N4 ?? "",
                N5 = e.N5 ?? "",
                N6 = e.N6 ?? "",
                N7 = e.N7 ?? "",
                N8 = e.N8 ?? "",
                Etype = e.Etype ?? "1",
            });
        }

        if (udm.NodeRows.Count > 0 && udm.ElementRows.Count > 0)
            udm.TryGenerateMeshFromTables();
    }

    private static void ApplyVerification(VerificationViewModel v, XfemVerificationState dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.SelectedProblem))
            v.SelectedProblem = dto.SelectedProblem;
        v.TolerancePercent = dto.TolerancePercent;
        v.AutoFromMeta = dto.AutoFromMeta;
        v.E_kPa = dto.E_kPa;
        v.Nu = dto.Nu;
        v.Sigma_v_kPa = dto.Sigma_v_kPa;
        v.K0 = dto.K0;
        v.A = dto.A;
        v.Cx = dto.Cx;
        v.Cy = dto.Cy;
        v.Pi_kPa = dto.Pi_kPa;
        v.PhiDeg = dto.PhiDeg;
        v.C_kPa = dto.C_kPa;
        v.PsiDeg = dto.PsiDeg;
        v.Gamma = dto.Gamma;
    }

    private static void ApplyStagedConstructionFromDocument(MainWindowViewModel vm, XfemStagedConstructionState? dto)
    {
        var sc = vm.StagedConstructionVM;
        var fe = vm.FEAnalysisVM;
        var g = vm.GeometryDefinitionVM;
        var staged = fe.StagedExcavationEnabled && fe.StageDefinitions.Count > 0;
        sc.SyncConstructionStagesFromFeAnalysis(fe, staged);

        if (dto?.Stages is { Count: > 0 } rows)
        {
            // When "Staged Excavation" is disabled in FE Analysis, SyncConstructionStagesFromFeAnalysis
            // clears sc.Stages. But the user may still have created excavations via the Material
            // Properties popup's Excavate button (which locally calls AddStage() on demand). To avoid
            // silently dropping those saved excavations on reload, materialize local stages here —
            // mirroring StagedConstructionViewModel.AddStage() defaults (Stage 1 = Initial Stress,
            // later stages = Excavation).
            if (!staged)
            {
                foreach (var row in rows.OrderBy(r => r.StageNumber))
                {
                    if (sc.Stages.Any(s => s.StageNumber == row.StageNumber)) continue;
                    sc.Stages.Add(new ConstructionStage
                    {
                        StageNumber = row.StageNumber,
                        StageName = row.StageNumber == 1 ? "Initial Stress" : $"Stage {row.StageNumber}",
                        StageType = row.StageNumber == 1 ? "Initial Stress" : "Excavation"
                    });
                }
            }

            foreach (var row in rows)
            {
                var stage = sc.Stages.FirstOrDefault(s => s.StageNumber == row.StageNumber);
                if (stage == null) continue;
                if (!string.IsNullOrWhiteSpace(row.StageName))
                    stage.StageName = row.StageName;
                if (!string.IsNullOrWhiteSpace(row.StageType))
                    stage.StageType = row.StageType;
                if (!string.IsNullOrWhiteSpace(row.Description))
                    stage.Description = row.Description;
                if (row.IsActive is bool ia)
                    stage.IsActive = ia;
                stage.ExcavatedSurfaceNumbers.Clear();
                stage.ExcavationZones.Clear();
                stage.ExcavatedElementIds.Clear();
                if (row.ExcavatedSurfaceNumbers != null)
                {
                    foreach (var sn in row.ExcavatedSurfaceNumbers)
                        stage.ExcavatedSurfaceNumbers.Add(sn);
                }
                if (row.ExcavatedElementIds != null)
                    stage.ExcavatedElementIds.AddRange(row.ExcavatedElementIds);
                foreach (var z in row.Zones ?? new List<XfemStageExcavationZoneSnapshot>())
                {
                    var axisList = new List<(double X, double Y)>();
                    foreach (var pt in z.AxisPolygon ?? new List<XfemPoint>())
                        axisList.Add((pt.X, pt.Y));

                    var canvasPoly = new PointCollection();
                    double cx = 0, cy = 0;
                    if (axisList.Count > 0)
                    {
                        foreach (var ap in axisList)
                        {
                            var c = g.ConvertAxisToCanvas(new Point(ap.X, ap.Y));
                            canvasPoly.Add(c);
                            cx += c.X;
                            cy += c.Y;
                        }
                        cx /= axisList.Count;
                        cy /= axisList.Count;
                    }

                    stage.ExcavatedSurfaceNumbers.Add(z.SurfaceNumber);
                    stage.ExcavationZones.Add(new StageExcavationZone
                    {
                        SurfaceNumber = z.SurfaceNumber,
                        AppliedInStageNumber = z.AppliedInStageNumber != 0 ? z.AppliedInStageNumber : row.StageNumber,
                        AxisPolygon = axisList,
                        CanvasPolygon = canvasPoly,
                        CentroidX = cx,
                        CentroidY = cy,
                        DisplayLabel = string.IsNullOrWhiteSpace(z.DisplayLabel) ? $"Exc S{z.SurfaceNumber}" : z.DisplayLabel!
                    });
                }
            }
        }

        if (dto?.ActiveStageNumber is int asn)
        {
            var active = sc.Stages.FirstOrDefault(s => s.StageNumber == asn);
            if (active != null)
                sc.ActiveStage = active;
        }

        sc.SyncActiveStageFromFooter(vm.CurrentStage);
        sc.RefreshExcavationDisplay();
        sc.NotifyExcavatedElementIdsChanged();
    }

    private static bool HasGwStaticFlags(XfemProjectSettingsState ps) =>
        ps.GwStaticIncludeWaterSurfaces.HasValue
        || ps.GwStaticIncludePondedFreeWater.HasValue
        || ps.GwStaticIncludeRuCoefficients.HasValue
        || ps.GwStaticIncludeWaterHead.HasValue
        || ps.GwStaticIncludeWaterPressureGridTotalHead.HasValue
        || ps.GwStaticIncludeWaterPressureGridPressureHead.HasValue
        || ps.GwStaticIncludeWaterPressureGridPorePressure.HasValue;

    private static XfemAnalysisTypeSubtab? FindLegacyGroundwaterStaticSubtab(XfemProjectSettingsState ps) =>
        ps.AnalysisTypeSubtabs?.FirstOrDefault(t =>
            MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(t.AnalysisCategory)
            && string.Equals(t.DetailedAnalysisType, MaterialPropertiesViewModel.GroundwaterStaticType, StringComparison.Ordinal));

    /// <summary>Restores GW Static component flags: flat fields first, then legacy AnalysisTypeSubtabs, else defaults.</summary>
    private static void ApplyGroundwaterStaticFlags(ProjectSettingsViewModel p, XfemProjectSettingsState ps)
    {
        if (HasGwStaticFlags(ps))
        {
            p.GroundwaterStaticSettings.IncludeWaterSurfaces = ps.GwStaticIncludeWaterSurfaces ?? false;
            // Older projects without the flag keep auto ponded-on behavior.
            p.GroundwaterStaticSettings.IncludePondedFreeWater = ps.GwStaticIncludePondedFreeWater ?? true;
            p.GroundwaterStaticSettings.IncludeRuCoefficients = ps.GwStaticIncludeRuCoefficients ?? false;
            p.GroundwaterStaticSettings.IncludeWaterPressureGridTotalHead = ps.GwStaticIncludeWaterPressureGridTotalHead ?? false;
            p.GroundwaterStaticSettings.IncludeWaterPressureGridPressureHead = ps.GwStaticIncludeWaterPressureGridPressureHead ?? false;
            p.GroundwaterStaticSettings.IncludeWaterPressureGridPorePressure = ps.GwStaticIncludeWaterPressureGridPorePressure ?? false;
            return;
        }

        var legacy = FindLegacyGroundwaterStaticSubtab(ps);
        if (legacy != null)
        {
            // Legacy subtab files always wrote all five flags; treat a missing flag as unchecked.
            p.GroundwaterStaticSettings.IncludeWaterSurfaces = legacy.IncludeWaterSurfaces ?? false;
            p.GroundwaterStaticSettings.IncludePondedFreeWater = true;
            p.GroundwaterStaticSettings.IncludeRuCoefficients = legacy.IncludeRuCoefficients ?? false;
            p.GroundwaterStaticSettings.IncludeWaterPressureGridTotalHead = legacy.IncludeWaterPressureGridTotalHead ?? false;
            p.GroundwaterStaticSettings.IncludeWaterPressureGridPressureHead = legacy.IncludeWaterPressureGridPressureHead ?? false;
            p.GroundwaterStaticSettings.IncludeWaterPressureGridPorePressure = legacy.IncludeWaterPressureGridPorePressure ?? false;
            return;
        }

        p.GroundwaterStaticSettings.ApplyDefaults();
    }

    /// <summary>Restores steady-seepage (p72/p73/p74) options; missing fields keep the model defaults.</summary>
    private static void ApplySeepageSettings(ProjectSettingsViewModel p, XfemProjectSettingsState ps)
    {
        if (ps.SeepageUpstreamHead.HasValue) p.SeepageSettings.UpstreamHead = ps.SeepageUpstreamHead.Value;
        if (ps.SeepageDownstreamHead.HasValue) p.SeepageSettings.DownstreamHead = ps.SeepageDownstreamHead.Value;
        if (ps.SeepageAxisymmetric.HasValue) p.SeepageSettings.Axisymmetric = ps.SeepageAxisymmetric.Value;
        if (ps.SrfAxisymmetric.HasValue) p.SrfAxisymmetric = ps.SrfAxisymmetric.Value;
        if (ps.SeepageFreeSurfaceTolerance.HasValue) p.SeepageSettings.FreeSurfaceTolerance = ps.SeepageFreeSurfaceTolerance.Value;
        if (ps.SeepageFreeSurfaceMaxIterations.HasValue) p.SeepageSettings.FreeSurfaceMaxIterations = ps.SeepageFreeSurfaceMaxIterations.Value;

        var hasNewWaterHeadFlags =
            ps.SeepageIncludeWaterHeadPlaneAxisym.HasValue
            || ps.SeepageIncludeWaterHeadFreeSurface.HasValue
            || ps.SeepageIncludeWaterHeadGeneral2D.HasValue
            || ps.SeepageIncludeWaterHeadGeneral3D.HasValue;

        if (hasNewWaterHeadFlags)
        {
            if (ps.SeepageIncludeWaterHeadPlaneAxisym.HasValue)
                p.SeepageSettings.IncludeWaterHeadPlaneAxisym = ps.SeepageIncludeWaterHeadPlaneAxisym.Value;
            if (ps.SeepageIncludeWaterHeadFreeSurface.HasValue)
                p.SeepageSettings.IncludeWaterHeadFreeSurface = ps.SeepageIncludeWaterHeadFreeSurface.Value;
            if (ps.SeepageIncludeWaterHeadGeneral2D.HasValue)
                p.SeepageSettings.IncludeWaterHeadGeneral2D = ps.SeepageIncludeWaterHeadGeneral2D.Value;
            if (ps.SeepageIncludeWaterHeadGeneral3D.HasValue)
                p.SeepageSettings.IncludeWaterHeadGeneral3D = ps.SeepageIncludeWaterHeadGeneral3D.Value;
        }
        else if (ps.GwStaticIncludeWaterHead == true)
        {
            // Legacy Static → Water Head migrated onto all Steady State solvers.
            p.SeepageSettings.SetAllIncludeWaterHead(true);
        }
    }

    /// <summary>Restores Transient State (p84/p88) options; missing fields keep model defaults.</summary>
    private static void ApplyTransientSettings(ProjectSettingsViewModel p, XfemProjectSettingsState ps)
    {
        if (ps.TransientAxisymmetric.HasValue) p.TransientSettings.Axisymmetric = ps.TransientAxisymmetric.Value;
        if (ps.TransientTimeStep.HasValue) p.TransientSettings.TimeStep = ps.TransientTimeStep.Value;
        if (ps.TransientNumberOfSteps.HasValue) p.TransientSettings.NumberOfSteps = ps.TransientNumberOfSteps.Value;
        if (ps.TransientTheta.HasValue) p.TransientSettings.Theta = ps.TransientTheta.Value;
        if (ps.TransientPrintInterval.HasValue) p.TransientSettings.PrintInterval = ps.TransientPrintInterval.Value;
        if (ps.TransientMonitorNode.HasValue) p.TransientSettings.MonitorNode = ps.TransientMonitorNode.Value;
        if (ps.TransientContourStep.HasValue) p.TransientSettings.ContourStep = ps.TransientContourStep.Value;
        if (ps.TransientContourIntervals.HasValue) p.TransientSettings.ContourIntervals = ps.TransientContourIntervals.Value;
        if (ps.TransientInitialPressure.HasValue) p.TransientSettings.InitialPressure = ps.TransientInitialPressure.Value;
        if (ps.TransientFixedPressure.HasValue) p.TransientSettings.FixedPressure = ps.TransientFixedPressure.Value;
        if (ps.TransientFixTopFace.HasValue) p.TransientSettings.FixTopFace = ps.TransientFixTopFace.Value;
        if (ps.TransientFixBottomFace.HasValue) p.TransientSettings.FixBottomFace = ps.TransientFixBottomFace.Value;
        if (ps.TransientFixMinXFace.HasValue) p.TransientSettings.FixMinXFace = ps.TransientFixMinXFace.Value;
        if (ps.TransientFixMaxXFace.HasValue) p.TransientSettings.FixMaxXFace = ps.TransientFixMaxXFace.Value;
    }

    private static void Apply(MainWindowViewModel vm, XfemDocument d, string? projectPath = null)
    {
        using (vm.GeometryDefinitionVM.BeginGeometryBulkUpdate())
        using (vm.MeshVM.BeginMeshCanvasBulkUpdate())
        {
            ApplyCore(vm, d, projectPath);
        }
    }

    private static void ApplyCore(MainWindowViewModel vm, XfemDocument d, string? projectPath = null)
    {
        if (d.Mesh == null)
            vm.MeshVM.ClearMeshCommand.Execute(null);
        if (d.Loads == null)
        {
            vm.LoadConditionsVM.Loads.Clear();
            vm.LoadConditionsVM.NotifyLoadsReplacedFromProjectFile();
        }

        var p = vm.ProjectSettingsVM;
        if (d.ProjectSettings is { } ps)
        {
            p.SelectedDimension = ps.SelectedDimension;
            ApplyGroundwaterStaticFlags(p, ps);
            ApplySeepageSettings(p, ps);
            ApplyTransientSettings(p, ps);
            if (ps.AnalysisCategoryOnFlags != null)
            {
                // Explicit list (including empty): missing categories default OFF.
                p.ApplyCategoryOnSnapshot(ps.AnalysisCategoryOnFlags
                    .Where(f => !string.IsNullOrWhiteSpace(f.Category))
                    .Select(f => (f.Category!, f.IsOn)));
            }
            else if (vm.MaterialPropertiesVM.AnalysisCategories.Count > 0)
            {
                // Legacy .xfem without this field: categories were always available.
                p.ApplyCategoryOnSnapshot(
                    vm.MaterialPropertiesVM.AnalysisCategories.Select(c => (c, true)));
            }
            p.SelectedSettingsTabIndex = ps.SelectedSettingsTabIndex;
            p.ClampSelectedSettingsTabIndex();
            p.IsDeterministicAnalysis = ps.IsDeterministicAnalysis;
            p.IsProbabilisticAnalysis = ps.IsProbabilisticAnalysis;
            p.NumberOfStages = ps.NumberOfStages;
            p.SelectedStageFooterTabIndex = ps.SelectedStageFooterTabIndex;
            p.StageFooterTabs = ps.StageFooterTabs?.Select(t => new StageFooterTab { StageNumber = t.StageNumber, StageName = t.StageName ?? "" }).ToList() ?? new List<StageFooterTab>();
            p.Project.ProjectName = ps.ProjectName ?? "";
            p.Project.Author = ps.Author ?? "";
            p.Project.CreatedOn = ps.CreatedOn;
            p.Project.Units = ps.Units ?? "SI";
            p.Project.BishopMaxIterations = ps.BishopMaxIterations;
            p.Project.BishopConvergenceTolerance = ps.BishopConvergenceTolerance;
            p.Project.BishopInitialFactor = ps.BishopInitialFactor;
            p.Project.NumberOfStages = ps.NumberOfStagesModel;
            p.Project.AxisXMin = ps.AxisXMin;
            p.Project.AxisXMax = ps.AxisXMax;
            p.Project.AxisYMin = ps.AxisYMin;
            p.Project.AxisYMax = ps.AxisYMax;
            p.Project.GridSpacingMeters = ps.GridSpacingMeters;
            p.Project.ShowGrid = ps.ShowGrid;
            p.Project.ShowAxes = ps.ShowAxes;
            if (ps.CoordinateSystem is { } crs)
            {
                p.Project.CoordinateSystem.EpsgCode = crs.EpsgCode;
                p.Project.CoordinateSystem.Name = crs.Name ?? p.Project.CoordinateSystem.Name;
                p.Project.CoordinateSystem.OriginEasting = crs.OriginEasting;
                p.Project.CoordinateSystem.OriginNorthing = crs.OriginNorthing;
                p.Project.CoordinateSystem.RotationDeg = crs.RotationDeg;
                p.Project.CoordinateSystem.Scale = crs.Scale == 0 ? 1.0 : crs.Scale;
            }
            if (ps.FieldStationSoilBlockMargin is double margin && double.IsFinite(margin))
                p.FieldStationSoilBlockMargin = margin;
            if (ps.FieldStationReferenceElevation is double refElev && double.IsFinite(refElev))
                p.FieldStationReferenceElevation = refElev;
            if (ps.FieldStationsActive is bool fsActive)
                p.FieldStationsActive = fsActive;
            if (ps.PlotBoreholes is bool plotBh)
                p.PlotBoreholes = plotBh;
            ApplyFieldStations(p.FieldStations, ps.FieldStations);
            if (ps.Drawdown != null)
                ApplyDrawdown(p.DrawdownSettings, ps.Drawdown);
        }

        vm.GeometryDefinitionVM.SyncGridSpacing(p.Project.GridSpacingMeters);
        vm.GeometryDefinitionVM.ShowGrid = p.Project.ShowGrid;
        vm.GeometryDefinitionVM.ShowAxes = p.Project.ShowAxes;

        var g = vm.GeometryDefinitionVM;
        g.ClearGeometryImportStatusMessage();
        if (d.Geometry is { } geo)
        {
            g.GeometryVertexRows.Clear();
            foreach (var v in geo.Vertices ?? new List<XfemPoint>())
            {
                var display = new GeometryDefinitionViewModel.GeometryVertexDisplay
                {
                    Index = g.GeometryVertexRows.Count,
                    AxisX = v.X,
                    AxisY = v.Y
                };
                display.Owner = g;
                if (v.StablePointId is int sid && sid > 0)
                    display.StablePointId = sid;
                display.SplitFromLineNumber = v.SplitFromLineNumber;
                display.IsMaterialLayerCutVertex = v.IsMaterialLayerCutVertex == true;
                g.GeometryVertexRows.Add(display);
            }
            for (int i = 0; i < g.GeometryVertexRows.Count; i++)
                g.GeometryVertexRows[i].Index = i;

            g.NormalizeStablePointIdsAfterLoad();

            g.CanvasWidth = geo.CanvasWidth;
            g.CanvasHeight = geo.CanvasHeight;
            g.AxisXMin = geo.AxisXMin;
            g.AxisXMax = geo.AxisXMax;
            g.AxisYMin = geo.AxisYMin;
            g.AxisYMax = geo.AxisYMax;
            g.LayerElevationInput = geo.LayerElevationInput ?? "";
            g.SetMaterialLayerCutElevationsFromRestore(geo.MaterialLayerCutElevations);
            g.ReplaceLayerMaterialAssignmentsFrom(
                geo.LayerMaterialAssignments != null && geo.LayerMaterialAssignments.Count > 0
                    ? geo.LayerMaterialAssignments.ToDictionary(x => x.DisplayLayerNumber, x => x.MaterialId)
                    : null);
            g.ReplaceLayerDisplayNamesFrom(
                geo.LayerDisplayNames != null && geo.LayerDisplayNames.Count > 0
                    ? geo.LayerDisplayNames.ToDictionary(x => x.DisplayLayerNumber, x => x.Name ?? "")
                    : null);
            g.ReplaceLayerDisplayColorsFrom(
                geo.LayerDisplayColors != null && geo.LayerDisplayColors.Count > 0
                    ? geo.LayerDisplayColors.ToDictionary(x => x.DisplayLayerNumber, x => x.Argb)
                    : null);
            g.ReplaceSurfaceMaterialFillsFrom(
                geo.SurfaceMaterialFills != null && geo.SurfaceMaterialFills.Count > 0
                    ? geo.SurfaceMaterialFills.Select(s => (s.SurfaceNumber, s.Argb, s.MaterialId)).ToList()
                    : null);
            g.TunnelCenterXInput = geo.TunnelCenterXInput ?? "";
            g.TunnelCenterYInput = geo.TunnelCenterYInput ?? "";
            g.TunnelRadiusInput = geo.TunnelRadiusInput ?? "";
            g.GeometryXInput = geo.GeometryXInput ?? "";
            g.GeometryYInput = geo.GeometryYInput ?? "";

            g.GeometryInternalEdges.Clear();
            foreach (var ie in geo.InternalEdges ?? new List<XfemInternalEdge>())
            {
                if (ie.StableMin <= 0 || ie.StableMax <= 0) continue;
                if (ie.IsArc && ie.StableMid is int mid && mid > 0 &&
                    ie.StableStart is int ss && ss > 0 && ie.StableEnd is int se && se > 0)
                    g.GeometryInternalEdges.Add(new GeometryInternalEdge(ss, mid, se));
                else
                    g.GeometryInternalEdges.Add(new GeometryInternalEdge(ie.StableMin, ie.StableMax));
            }

            var savedSegs = geo.LineSegments ?? new List<XfemLineSegment>();
            g.RemoveStaleInternalEdgesFromSavedBoundarySegments(
                savedSegs
                    .Where(s => !string.IsNullOrWhiteSpace(s.FromPoint) && s.FromPoint != "—")
                    .Select(s => (s.FromPoint ?? "", s.ToPoint ?? "", s.IsBoundaryEdge)));
            g.PruneDuplicateBoundaryInternalEdges();

            g.ClearGeometryInternalCirclesBeforeLoad();
            g.RebuildGeometryAfterProjectLoad();
            g.SetNonHorizontalLayerCutsFromRestore(RestoreNonHorizontalLayerCuts(geo.NonHorizontalLayerCuts));

            var savedByLine = savedSegs
                .Where(s => s.LineNumber > 0)
                .GroupBy(s => s.LineNumber)
                .ToDictionary(g => g.Key, g => g.First());
            foreach (var seg in g.GeometryLineSegments.Where(s => !s.IsCircle))
            {
                if (savedByLine.TryGetValue(seg.LineNumber, out var src))
                    ApplySegmentProps(seg, src);
            }

            g.RestoreGeometryInternalCircles(geo.GeometryCircles?.Select(x => new GeometryInternalCircle
            {
                Id = x.Id,
                CenterAxisX = x.CenterAxisX,
                CenterAxisY = x.CenterAxisY,
                RadiusAxis = x.RadiusAxis
            }).ToList());

            g.FilteredTunnelExcavationCircles.Clear();
            g.TunnelExcavationCircleRows.Clear();
            g.FilteredTunnelExcavationCircleRows.Clear();
            foreach (var tr in geo.TunnelRows ?? new List<XfemTunnelRow>())
                AddTunnelFromAxis(g, tr);

            g.GroundwaterLines.Clear();
            if (geo.GroundwaterSurfPoints is { Count: > 0 })
            {
                g.ApplyGroundwaterSurfPoints(geo.GroundwaterSurfPoints.Select(p => (p.X, p.Y)).ToList());
            }
            else
            {
                foreach (var wl in geo.GroundwaterLines ?? new List<XfemGroundwaterLine>())
                {
                    var s = g.ConvertAxisToCanvas(new Point(wl.StartX, wl.StartY));
                    var e = g.ConvertAxisToCanvas(new Point(wl.EndX, wl.EndY));
                    g.GroundwaterLines.Add(new GroundwaterLine(s, e, wl.Elevation));
                }
            }
            var gwModel = new GroundwaterModel
            {
                IsEnabled = geo.GroundwaterEnabled ?? false,
                GammaW = geo.GroundwaterGammaW ?? 9.81,
                Method = string.Equals(geo.GroundwaterMethod, "ru", StringComparison.OrdinalIgnoreCase)
                    ? GroundwaterMethod.Ru
                    : string.Equals(geo.GroundwaterMethod, "bbar", StringComparison.OrdinalIgnoreCase)
                        ? GroundwaterMethod.Bbar
                        : string.Equals(geo.GroundwaterMethod, "grid", StringComparison.OrdinalIgnoreCase)
                            ? GroundwaterMethod.Grid
                            : GroundwaterMethod.Hydrostatic,
                Ru = geo.GroundwaterRu ?? 0.0,
                Bbar = geo.GroundwaterBbar ?? 0.0,
                DrawMode = vm.GroundwaterVM.DrawMode,
                ShowSaturatedMaterialZone = geo.GroundwaterShowSaturatedMaterialZone ?? true,
                ShowUnsaturatedMaterialZone = geo.GroundwaterShowUnsaturatedMaterialZone ?? false,
                ShowPondedFreeWaterRegion = geo.GroundwaterShowPondedFreeWaterRegion ?? true,
                PondedWaterDisplayPattern = ParsePondedWaterDisplayPattern(geo.GroundwaterPondedWaterDisplayPattern),
                ShowPondedFacePressureArrows = geo.GroundwaterShowPondedFacePressureArrows ?? true,
                ShowPondedHeadIsobars = geo.GroundwaterShowPondedHeadIsobars ?? true,
                PondedWaterLevelMethod = string.Equals(geo.GroundwaterPondedWaterLevelMethod, "total_head", StringComparison.OrdinalIgnoreCase)
                    ? PondedWaterLevelMethod.TotalHead
                    : PondedWaterLevelMethod.PhreaticLine,
                PondedTotalHead = geo.GroundwaterPondedTotalHead ?? 0.0,
                SurfPoints = geo.GroundwaterSurfPoints?.Select(p => (p.X, p.Y)).ToList() ?? new(),
                WaterPressureGrids = (geo.WaterPressureGrids ?? new List<XfemWaterPressureNamedGrid>())
                    .Select(ToWaterPressureGridData)
                    .OfType<WaterPressureGridData>()
                    .ToList(),
                TotalHeadGridPoints = geo.WaterPressureGridTotalHead?.Select(p => (p.X, p.Y, p.Value)).ToList() ?? new(),
                PressureHeadGridPoints = geo.WaterPressureGridPressureHead?.Select(p => (p.X, p.Y, p.Value)).ToList() ?? new(),
                PorePressureGridPoints = geo.WaterPressureGridPorePressure?.Select(p => (p.X, p.Y, p.Value)).ToList() ?? new()
            };
            vm.GroundwaterVM.ApplyImportedModel(gwModel);
            g.RefreshGroundwaterOverlays();

            g.ExcavationPolygons.Clear();
            foreach (var loop in geo.ExcavationPolygonsAxis ?? new List<List<XfemPoint>>())
            {
                var pc = new PointCollection();
                foreach (var pt in loop)
                    pc.Add(g.ConvertAxisToCanvas(new Point(pt.X, pt.Y)));
                g.ExcavationPolygons.Add(pc);
            }

            g.GeometrySurfaces.Clear();
            foreach (var dto in geo.Surfaces ?? new List<XfemGeometrySurface>())
            {
                var s = new GeometrySurface(dto.SurfaceNumber, dto.SurfaceName ?? "", dto.ConnectedLineNumbers);
                s.Tag = dto.Tag;
                s.ColorIndex = dto.ColorIndex;
                s.AssignedMaterialId = dto.AssignedMaterialId;
                s.IsPolygonLocked = dto.IsPolygonLocked;
                var pickedNums = dto.SelectedLineNumbers ?? new List<int>();
                if (pickedNums.Count == 0)
                {
                    void addLegacy(int? n) { if (n is > 0) pickedNums.Add(n.Value); }
                    addLegacy(dto.SelectedLine1);
                    addLegacy(dto.SelectedLine2);
                    addLegacy(dto.SelectedLine3);
                    addLegacy(dto.SelectedLine4);
                    addLegacy(dto.SelectedLine5);
                }
                foreach (var n in pickedNums)
                    s.PickedLineNumbers.Add(n);
                foreach (var lab in dto.LockedPolygonVertexLabels ?? new List<string>())
                    s.LockedPolygonVertexLabels.Add(lab);
                foreach (var lab in dto.DraftPolygonVertexLabels ?? new List<string>())
                    s.DraftPolygonVertexLabels.Add(lab);
                g.GeometrySurfaces.Add(s);
            }

            var selSurf = geo.SelectedSurfaceNumber;
            g.SelectedGeometrySurface = selSurf is int sn
                ? g.GeometrySurfaces.FirstOrDefault(x => x.SurfaceNumber == sn)
                : null;

            g.RestoreSurfaceExcavationOperations(geo.SurfaceExcavationOperations);
            g.ReplaceJointPropertySets(RestoreJointPropertySets(geo.JointPropertySets));
            g.RestoreJointNetworks(RestoreJointNetworks(geo.JointNetworks));
            g.RestoreGeologicalFeatures(RestoreGeologicalFeatures(geo.GeologicalFeatures));
            g.RestoreKernelBooleanHistory(RestoreKernelBooleans(geo.KernelBooleanHistory));
            RestoreSlopeImageUnderlay(g, geo.SlopeImageUnderlay, projectPath ?? vm.CurrentXfemProjectPath);

            g.EnsureEmbankmentMacroSurfaceAndAxisFit();

            // Material Properties tab keeps a geometry mirror; sync it before layer-assignment rebuild.
            vm.MaterialPropertiesVM.GeometryDefinition.CopyGeometryFrom(g);
        }

        if (d.Materials is { } mat)
        {
            vm.MaterialPropertiesVM.SelectedFailureCriterion = mat.SelectedFailureCriterion;
            vm.MaterialPropertiesVM.SelectedMaterialType = mat.SelectedMaterialType;
            vm.MaterialPropertiesVM.SelectedAnalysisCategory =
                MaterialPropertiesViewModel.NormalizeAnalysisCategory(mat.SelectedAnalysisCategory);
            vm.MaterialPropertiesVM.SelectedDetailedAnalysisType =
                MaterialPropertiesViewModel.NormalizeDetailedAnalysisType(mat.SelectedDetailedAnalysisType);
            vm.MaterialPropertiesVM.LastGroundwaterTreeMode = mat.LastGroundwaterTreeMode;
            vm.MaterialPropertiesVM.LastSteadyStateSolverType =
                MaterialPropertiesViewModel.NormalizeDetailedAnalysisType(mat.LastSteadyStateSolverType);
            vm.MaterialPropertiesVM.LastTransientSolverType =
                MaterialPropertiesViewModel.NormalizeDetailedAnalysisType(mat.LastTransientSolverType);
            // Legacy projects: infer coupling memory from the restored detailed type.
            if (string.IsNullOrEmpty(vm.MaterialPropertiesVM.LastGroundwaterTreeMode))
            {
                if (MaterialPropertiesViewModel.IsSeepageFlowAnalysisType(
                        vm.MaterialPropertiesVM.SelectedDetailedAnalysisType))
                {
                    vm.MaterialPropertiesVM.RememberGroundwaterTreeMode(
                        vm.MaterialPropertiesVM.SelectedDetailedAnalysisType);
                }
                else if (MaterialPropertiesViewModel.IsTransientFlowAnalysisType(
                             vm.MaterialPropertiesVM.SelectedDetailedAnalysisType))
                {
                    vm.MaterialPropertiesVM.RememberGroundwaterTreeMode(
                        vm.MaterialPropertiesVM.SelectedDetailedAnalysisType);
                }
                else if (string.Equals(
                             MaterialPropertiesViewModel.NormalizeDetailedAnalysisType(
                                 vm.MaterialPropertiesVM.SelectedDetailedAnalysisType),
                             MaterialPropertiesViewModel.GroundwaterStaticType,
                             StringComparison.Ordinal))
                {
                    vm.MaterialPropertiesVM.RememberGroundwaterTreeMode(
                        MaterialPropertiesViewModel.GroundwaterStaticType);
                }
            }
            vm.MaterialPropertiesVM.ShowDeterministicTab = mat.ShowDeterministicTab;
            vm.MaterialPropertiesVM.ShowProbabilisticTab = mat.ShowProbabilisticTab;

            var restored = (mat.MaterialLibrary ?? new List<XfemMaterialLibraryEntry>())
                .Select(MaterialModelFromLibraryEntry)
                .ToList();
            vm.MaterialPropertiesVM.ReplaceMaterialLibraryFromProject(
                restored,
                mat.SelectedMaterialLibraryId,
                mat.SelectedMaterialsLayerDisplayNumber,
                mat.SelectedLayerAssignmentDisplayLayerNumber);
        }
        else
        {
            vm.MaterialPropertiesVM.ReplaceMaterialLibraryFromProject(new List<MaterialModel>(), null, null, null);
        }

        // Legacy files gated groundwater inputs by an open Static subtab regardless of the
        // Analysis dropdown selection; keep that behavior by selecting GW Static now that the
        // materials section has restored the (possibly different) saved selection.
        if (d.ProjectSettings is { } psLegacy
            && !HasGwStaticFlags(psLegacy)
            && FindLegacyGroundwaterStaticSubtab(psLegacy) != null)
        {
            vm.MaterialPropertiesVM.SelectedAnalysisCategory = MaterialPropertiesViewModel.GroundwaterAnalysisCategory;
            vm.MaterialPropertiesVM.SelectedDetailedAnalysisType = MaterialPropertiesViewModel.GroundwaterStaticType;
        }

        vm.MeshVM.RefreshAfterProjectFileLoad();
        if (d.Mesh is { } meshDto)
            ApplyMesh(vm.MeshVM, meshDto);

        if (d.Loads != null)
        {
            vm.LoadConditionsVM.Loads.Clear();
            foreach (var ld in d.Loads)
            {
                vm.LoadConditionsVM.Loads.Add(new LoadModel
                {
                    Type = ld.Type,
                    Direction = ld.Direction,
                    Magnitude = ld.Magnitude,
                    EndMagnitude = ld.Type == LoadType.Triangular && Math.Abs(ld.EndMagnitude) < 1e-12
                        ? ld.Magnitude
                        : ld.EndMagnitude,
                    StartX = ld.StartX,
                    StartY = ld.StartY,
                    EndX = ld.EndX,
                    EndY = ld.EndY,
                    AppliedToNodeId = ld.AppliedToNodeId,
                    StageNumber = ld.StageNumber,
                    Name = ld.Name ?? "",
                    Orientation = ld.Orientation ?? LoadOrientation.NormalToBoundary,
                    AngleDeg = ld.AngleDeg ?? 0.0,
                    IsStageLoad = ld.IsStageLoad ?? false,
                    IsEnabled = ld.IsEnabled ?? true,
                    IdCode = ld.IdCode ?? "",
                    ApplyTo = ld.ApplyTo ?? "",
                    LoadGroup = ld.LoadGroup ?? "LG-01 (Permanent)",
                    CoordinateSystem = ld.CoordinateSystem ?? "Global",
                    Distribution = ld.Distribution ?? LoadDistribution.Uniform,
                    LoadProjection = ld.LoadProjection ?? "Normal to Surface",
                    LoadDepth = ld.LoadDepth ?? 0,
                    UseProjection = ld.UseProjection ?? false,
                    HasRampTimeDependency = ld.HasRampTimeDependency ?? false,
                    ApplyFromStage = ld.ApplyFromStage,
                    ApplyToStage = ld.ApplyToStage,
                    Notes = ld.Notes ?? "",
                });
            }
        }

        vm.GroundwaterVM.WaterHeads.Clear();
        if (d.GroundwaterHeads != null)
        {
            foreach (var wh in d.GroundwaterHeads)
                vm.GroundwaterVM.WaterHeads.Add(RestoreWaterHead(wh));
        }
        // Water heads are restored after the earlier RefreshGroundwaterOverlays() call.
        vm.GeometryDefinitionVM.RefreshWaterHeadOverlays();

        vm.GroundwaterVM.TransientFixedPressureNodes.Clear();
        if (d.TransientFixedPressures != null)
        {
            foreach (var fixedPressure in d.TransientFixedPressures)
                vm.GroundwaterVM.TransientFixedPressureNodes.Add(
                    RestoreTransientFixedPressure(fixedPressure));
        }
        vm.GeometryDefinitionVM.RefreshTransientPressureOverlays();

        if (d.BoundaryConditions is { } bc)
        {
            vm.BoundaryConditionsVM.BoundaryConditions.Clear();
            foreach (var row in bc.Rows ?? new List<XfemBoundaryRow>())
            {
                vm.BoundaryConditionsVM.BoundaryConditions.Add(new BoundaryConditionRow
                {
                    NodeId = row.NodeId,
                    BoundaryName = row.BoundaryName ?? "",
                    BCType = row.BCType ?? "Fixed",
                    AnalysisCategory = string.IsNullOrWhiteSpace(row.AnalysisCategory)
                        ? BoundaryConditionAnalysisCategories.Resolve(row.BCType)
                        : row.AnalysisCategory,
                    DegreeOfFreedom = row.DegreeOfFreedom,
                    Value = row.Value ?? "0",
                    Description = row.Description ?? ""
                });
            }
            vm.BoundaryConditionsVM.StatusMessage = bc.StatusMessage ?? "";
            vm.BoundaryConditionsVM.NodesText = bc.NodesText ?? "";
            vm.BoundaryConditionsVM.SelectedBCTypeForBoundary = bc.SelectedBCTypeForBoundary;
            vm.BoundaryConditionsVM.BcValueForBoundary = bc.BcValueForBoundary ?? "0";
            vm.BoundaryConditionsVM.EnsureAnalysisCategoriesOnRows();
        }

        if (d.FeAnalysis is { } fe)
        {
            vm.FEAnalysisVM.StatusMessage = fe.StatusMessage ?? "";
            if (fe.FeAnalysisSubTabIndex is int feTab)
                vm.FEAnalysisVM.FeAnalysisSubTabIndex = feTab;
            vm.FEAnalysisVM.StagedExcavationEnabled = fe.StagedExcavationEnabled;
            vm.FEAnalysisVM.GravityEnabled = fe.GravityEnabled;
            vm.FEAnalysisVM.GravityAcceleration = fe.GravityAcceleration;
            vm.FEAnalysisVM.LoadSteppingEnabled = fe.LoadSteppingEnabled;
            vm.FEAnalysisVM.NumberOfLoadSteps = fe.NumberOfLoadSteps;
            vm.FEAnalysisVM.CppExecutableDirectory = fe.CppExecutableDirectory ?? "";

            // Solver control block — only assign when present so older .xfem files keep VM defaults.
            if (fe.ConvergenceTolerance.HasValue) vm.FEAnalysisVM.ConvergenceTolerance = fe.ConvergenceTolerance.Value;
            if (fe.MaxIterations.HasValue)        vm.FEAnalysisVM.MaxIterations        = fe.MaxIterations.Value;
            if (fe.LoadIncrements.HasValue)       vm.FEAnalysisVM.LoadIncrements       = fe.LoadIncrements.Value;
            // LayersOverride is itself nullable in the VM (null = "auto"), so always copy through.
            vm.FEAnalysisVM.LayersOverride = fe.LayersOverride;

            if (fe.SrfTrialCount is int nsrf && nsrf >= 1)
            {
                vm.FEAnalysisVM.SrfTrialCount = nsrf;
                if (fe.SrfValues is { Count: > 0 } fList)
                {
                    for (int i = 0; i < Math.Min(fList.Count, vm.FEAnalysisVM.SrfValues.Count); i++)
                        vm.FEAnalysisVM.SrfValues[i].Value = fList[i];
                }
            }

            // SRF bracket search settings — only assign when present so older .xfem files keep VM defaults.
            if (!string.IsNullOrWhiteSpace(fe.SrfSearchMode)
                && System.Enum.TryParse<SrfSearchMode>(fe.SrfSearchMode, ignoreCase: true, out var srfSearchMode))
                vm.FEAnalysisVM.SelectedSrfSearchMode = srfSearchMode;
            if (!string.IsNullOrWhiteSpace(fe.SrfInsituMode)
                && System.Enum.TryParse<SrfInsituMode>(fe.SrfInsituMode, ignoreCase: true, out var srfInsituMode))
                vm.FEAnalysisVM.SelectedSrfInsituMode = srfInsituMode;
            if (fe.AutoDetectSrfBracket.HasValue) vm.FEAnalysisVM.AutoDetectSrfBracket = fe.AutoDetectSrfBracket.Value;
            if (fe.SrfBracketSeed.HasValue) vm.FEAnalysisVM.SrfBracketSeed = fe.SrfBracketSeed.Value;
            if (fe.SrfBracketExpansionFactor.HasValue) vm.FEAnalysisVM.SrfBracketExpansionFactor = fe.SrfBracketExpansionFactor.Value;
            if (fe.SrfMaxExpansionSteps.HasValue) vm.FEAnalysisVM.SrfMaxExpansionSteps = fe.SrfMaxExpansionSteps.Value;
            if (fe.SrfFoSMin.HasValue) vm.FEAnalysisVM.SrfFoSMin = fe.SrfFoSMin.Value;
            if (fe.SrfFoSMax.HasValue) vm.FEAnalysisVM.SrfFoSMax = fe.SrfFoSMax.Value;
            if (fe.SrfBracketTolerance.HasValue) vm.FEAnalysisVM.SrfBracketTolerance = fe.SrfBracketTolerance.Value;
            if (fe.SrfMaxBracketIterations.HasValue) vm.FEAnalysisVM.SrfMaxBracketIterations = fe.SrfMaxBracketIterations.Value;
            if (fe.SrfFoSMin.HasValue && fe.SrfFoSMax.HasValue && fe.SrfFoSMin.Value > 0 && fe.SrfFoSMax.Value > fe.SrfFoSMin.Value)
                vm.FEAnalysisVM.SetDetectedSrfBracketPreview(fe.SrfFoSMin.Value, fe.SrfFoSMax.Value);

            RestoreStageDefinitionsAfterLoad(vm, fe);
        }

        if (d.Results is { } res)
        {
            if (res.ResultsSubTabIndex is int rTab)
                vm.ResultsVM.ResultsSubTabIndex = rTab;
            vm.ResultsVM.VectorOutputType = res.VectorOutputType ?? vm.ResultsVM.VectorOutputType;
            // Missing/legacy VectorScale deserializes as 0 — treat as default so Vector mode is visible.
            vm.ResultsVM.VectorScale = res.VectorScale > 0 ? res.VectorScale : 1.0;
            if (res.DeformedMeshExaggeration > 0)
                vm.ResultsVM.DeformedMeshExaggeration = res.DeformedMeshExaggeration;
            vm.ResultsVM.ShowUndeformedOutline = res.ShowUndeformedOutline;
            vm.ResultsVM.ShowMesh = res.ShowMesh;
            vm.ResultsVM.SelectedOutputType = res.SelectedOutputType ?? vm.ResultsVM.SelectedOutputType;
            if (!string.IsNullOrWhiteSpace(res.VisualizationMode))
                vm.ResultsVM.VisualizationMode = res.VisualizationMode;
            vm.ResultsVM.FeStatusMessage = res.FeStatusMessage ?? "";
            if (res.IsLinePlotMode is bool lp)
                vm.ResultsVM.IsLinePlotMode = lp;
            if (!string.IsNullOrWhiteSpace(res.VectorDisplacementComponent))
                vm.ResultsVM.VectorDisplacementComponent = res.VectorDisplacementComponent;
            XfemFeRunSnapshot.Apply(vm.ResultsVM, res.FeRun, vm.MeshVM);
        }

        if (d.Script is { } sc)
        {
            vm.ScriptVM.IsManualScriptContent = sc.IsManualContent ?? false;
            vm.ScriptVM.ScriptContent = sc.ScriptContent ?? "";
            vm.ScriptVM.OutputText = sc.OutputText ?? "";
            if (!string.IsNullOrWhiteSpace(sc.PythonScript))
                vm.ScriptVM.PythonScript = sc.PythonScript;
            if (Enum.TryParse<GeotechSlopeApp.Scripting.ScriptEditorLanguage>(sc.ScriptLanguage, ignoreCase: true, out var lang))
                vm.ScriptVM.EditorLanguage = lang;
            if (!string.IsNullOrWhiteSpace(sc.StatusMessage))
                vm.ScriptVM.StatusMessage = sc.StatusMessage;
        }

        if (d.Main is { } main)
        {
            vm.SelectedTabIndex = main.SelectedTabIndex;
            vm.ZoomPanModeEnabled = main.ZoomPanModeEnabled;
            vm.CurrentStage = main.CurrentStage;
        }

        vm.ProjectSettingsVM.PullAxisFromGeometry();
        vm.ProjectSettingsVM.ApplyAxisToAllCanvasesCommand.Execute(null);
        vm.ApplyGeometryCanvasDecorationsForTab(vm.SelectedTabIndex);

        ApplyStagedConstructionFromDocument(vm, d.StagedConstruction);
        if (d.UserDefinedMesh is { } udmDto)
            ApplyUserDefinedMesh(vm.MeshVM.UserDefinedMeshVM, udmDto);
        if (d.Verification is { } ver)
            ApplyVerification(vm.VerificationVM, ver);
        // Field stress restore must precede the Loads-replaced notification so the Load Conditions
        // tab re-binds against the restored mode/spec/overrides (NotifyLoadsReplacedFromProjectFile
        // fires OnPropertyChanged(nameof(FieldStress))).
        var fsModel = BuildFieldStressModel(d.FieldStress);
        if (fsModel != null)
            vm.LoadConditionsVM.FieldStress.LoadFromModel(fsModel);
        if (d.SeismicLoad is { } seismic)
        {
            vm.LoadConditionsVM.RestoreSeismicLoadState(
                seismic.SeismicAnalysisEnabled,
                seismic.ScriptPseudoStaticKh,
                seismic.ScriptPseudoStaticKv);
            if (seismic.Newmark != null)
                ApplyNewmark(vm.LoadConditionsVM.Newmark, seismic.Newmark);
        }
        if (d.Support != null)
            ApplySupport(vm.SupportVM, d.Support);
        if (d.Wave3 != null)
            ApplyWave3(vm, d.Wave3);
        if (d.AnalysisWorkspace != null)
            ApplyAnalysisWorkspace(vm, d.AnalysisWorkspace);
        vm.LoadConditionsVM.NotifyLoadsReplacedFromProjectFile();

        if (d.SnapSettings != null)
            vm.GeometryDefinitionVM.SnapSettings.CopyFrom(d.SnapSettings);

        // Mesh outlines appear from IsDiscretized + MeshElements. BC glyphs stay hidden until
        // Show Marker is toggled — same path script import uses after a discretized mesh load.
        vm.BoundaryConditionsVM.ShowMarkersAfterMeshImport();
    }

    /// <summary>
    /// Rebuilds <see cref="FEAnalysisViewModel.StageDefinitions"/> after load. Older .xfem files only stored footer tabs;
    /// without repopulating the grid, post-load footer sync would clear the strip when staged analysis is on.
    /// </summary>
    private static void RestoreStageDefinitionsAfterLoad(MainWindowViewModel vm, XfemFeAnalysisState fe)
    {
        var coll = vm.FEAnalysisVM.StageDefinitions;
        coll.Clear();
        vm.FEAnalysisVM.SelectedStageDefinition = null;

        if (!fe.StagedExcavationEnabled)
            return;

        if (fe.StageDefinitions is { Count: > 0 } saved)
        {
            foreach (var dto in saved)
            {
                var sd = new StageDefinition
                {
                    StageNumber = dto.StageNumber,
                    StageName = dto.StageName ?? "",
                    StageType = string.IsNullOrWhiteSpace(dto.StageType) ? "Initial Stress" : dto.StageType!,
                    Description = dto.Description ?? "",
                    IsActive = dto.IsActive
                };
                if (dto.Duration is double dur) sd.Duration = dur;
                if (!string.IsNullOrEmpty(dto.Units)) sd.Units = dto.Units!;
                sd.StartsFromStageNumber = dto.StartsFromStageNumber;
                if (dto.ResetDisplacementsAtStart is bool rst) sd.ResetDisplacementsAtStart = rst;
                if (dto.StressReleaseLambda is double lam) sd.StressReleaseLambda = lam;
                if (dto.LoadFactor is double lf) sd.LoadFactor = lf;
                if (!string.IsNullOrEmpty(dto.SolverMode)) sd.SolverMode = dto.SolverMode!;
                if (dto.MaxIterations is int mi) sd.MaxIterations = mi;
                if (dto.Tolerance is double tol) sd.Tolerance = tol;
                if (dto.MaxLoadSteps is int mls) sd.MaxLoadSteps = mls;
                if (dto.ArcLength is bool arc) sd.ArcLength = arc;
                if (dto.ActivateSurfaceNumbers is { Count: > 0 } acts)
                    foreach (var n in acts) sd.ActivateSurfaceNumbers.Add(n);
                if (dto.DeactivateSurfaceNumbers is { Count: > 0 } deacts)
                    foreach (var n in deacts) sd.DeactivateSurfaceNumbers.Add(n);
                if (dto.LiningEnabled is bool lin) sd.LiningEnabled = lin;
                if (dto.BoltsEnabled is bool bolts) sd.BoltsEnabled = bolts;
                sd.GroundwaterLineTag = dto.GroundwaterLineTag;
                if (dto.ExportUx is bool eux) sd.ExportUx = eux;
                if (dto.ExportUy is bool euy) sd.ExportUy = euy;
                if (dto.ExportSxx is bool esxx) sd.ExportSxx = esxx;
                if (dto.ExportSyy is bool esyy) sd.ExportSyy = esyy;
                if (dto.ExportTxy is bool etxy) sd.ExportTxy = etxy;
                if (dto.ExportPlasticZones is bool epz) sd.ExportPlasticZones = epz;
                if (dto.ExportFosHistory is bool efos) sd.ExportFosHistory = efos;
                sd.Notes = dto.Notes ?? "";
                if (dto.PseudoStaticKh is double pkh) sd.PseudoStaticKh = pkh;
                if (dto.PseudoStaticKv is double pkv) sd.PseudoStaticKv = pkv;
                coll.Add(sd);
            }
        }
        else
        {
            var tabs = vm.ProjectSettingsVM.StageFooterTabs;
            foreach (var tab in tabs)
            {
                coll.Add(new StageDefinition
                {
                    StageNumber = tab.StageNumber,
                    StageName = string.IsNullOrWhiteSpace(tab.StageName) ? $"Stage {tab.StageNumber}" : tab.StageName,
                    StageType = "Initial Stress",
                    Description = "",
                    IsActive = true
                });
            }
        }

        vm.FEAnalysisVM.SelectedStageDefinition = coll.FirstOrDefault();
    }

    private static uint MaterialColourToArgb(Color c) =>
        ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    private static Color MaterialColourFromArgb(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    private static List<XfemNonHorizontalLayerCut>? CaptureNonHorizontalLayerCuts(GeometryDefinitionViewModel g)
    {
        var cuts = g.NonHorizontalLayerCutsSnapshot;
        if (cuts.Count == 0) return null;
        return cuts.Select(cut => new XfemNonHorizontalLayerCut
        {
            Points = cut.Select(p => new XfemPoint(p.X, p.Y)).ToList()
        }).ToList();
    }

    private static List<IReadOnlyList<(double X, double Y)>>? RestoreNonHorizontalLayerCuts(
        List<XfemNonHorizontalLayerCut>? cuts)
    {
        if (cuts == null || cuts.Count == 0) return null;
        var restored = new List<IReadOnlyList<(double X, double Y)>>();
        foreach (var cut in cuts)
        {
            if (cut.Points == null || cut.Points.Count < 2) continue;
            restored.Add(cut.Points.Select(p => (p.X, p.Y)).ToList());
        }
        return restored.Count > 0 ? restored : null;
    }

    private static XfemMaterialLibraryEntry CaptureMaterialLibraryEntry(MaterialModel m) => new()
    {
        Id = m.Id,
        Name = m.Name,
        Description = m.Description,
        ColourArgb = MaterialColourToArgb(m.Colour),
        FailureCriterion = m.FailureCriterion,
        MaterialType = m.MaterialType,
        YoungsModulus = m.YoungsModulus,
        PoissonsRatio = m.PoissonsRatio,
        YoungsModulusH = m.YoungsModulusH,
        YoungsModulusV = m.YoungsModulusV,
        PoissonsRatioHH = m.PoissonsRatioHH,
        PoissonsRatioVH = m.PoissonsRatioVH,
        ShearModulusVH = m.ShearModulusVH,
        UnitWeight = m.UnitWeight,
        SaturatedUnitWeight = m.SaturatedUnitWeight,
        IsSaturatedEnabled = m.IsSaturatedEnabled,
        Cohesion = m.Cohesion,
        FrictionAngle = m.FrictionAngle,
        DilationAngle = m.DilationAngle,
        TensileCutoff = m.TensileCutoff,
        Ucs = m.Ucs,
        HoekBrownMi = m.HoekBrownMi,
        Gsi = m.Gsi,
        Disturbance = m.Disturbance,
        ModulusRatio = m.ModulusRatio,
        DpAlpha = m.DpAlpha,
        DpK = m.DpK,
        PermeabilityX = m.PermeabilityX,
        PermeabilityY = m.PermeabilityY,
        Porosity = m.Porosity,
        InitialVoidRatio = m.InitialVoidRatio,
        SpecificStorage = m.SpecificStorage,
        ThermalConductivityX = m.ThermalConductivityX,
        ThermalConductivityY = m.ThermalConductivityY,
        HeatCapacity = m.HeatCapacity,
        ThermalExpansion = m.ThermalExpansion,
        Density = m.Density,
        DampingRatio = m.DampingRatio,
        DevelopsUndrainedStrengthOnDrawdown = m.DevelopsUndrainedStrengthOnDrawdown,
        DrawdownRCohesion = m.DrawdownRCohesion,
        DrawdownRFrictionAngle = m.DrawdownRFrictionAngle,
        DrawdownKc1Cohesion = m.DrawdownKc1Cohesion,
        DrawdownKc1Angle = m.DrawdownKc1Angle,
        DrawdownKfCohesion = m.DrawdownKfCohesion,
        DrawdownKfAngle = m.DrawdownKfAngle,
        DrawdownKf = m.DrawdownKf,
        EnableProbabilisticVariation = m.EnableProbabilisticVariation,
        CohesionPhiCorrelation = m.CohesionPhiCorrelation,
        PropertyStatistics = m.PropertyStatistics.Values.Select(s => new XfemMaterialStatState
        {
            PropertyId = s.PropertyId,
            UseAsRv = s.UseAsRv,
            Distribution = s.Distribution,
            CovPercent = s.CovPercent,
            Min = s.Min,
            Max = s.Max,
        }).ToList()
    };

    private static MaterialModel MaterialModelFromLibraryEntry(XfemMaterialLibraryEntry e)
    {
        var material = new MaterialModel
    {
        Id = e.Id,
        Name = e.Name ?? "Material",
        Description = e.Description ?? "",
        Colour = MaterialColourFromArgb(e.ColourArgb),
        FailureCriterion = e.FailureCriterion,
        MaterialType = e.MaterialType,
        YoungsModulus = e.YoungsModulus,
        PoissonsRatio = e.PoissonsRatio,
        YoungsModulusH = e.YoungsModulusH,
        YoungsModulusV = e.YoungsModulusV,
        PoissonsRatioHH = e.PoissonsRatioHH,
        PoissonsRatioVH = e.PoissonsRatioVH,
        ShearModulusVH = e.ShearModulusVH,
        UnitWeight = e.UnitWeight,
        SaturatedUnitWeight = e.SaturatedUnitWeight,
        IsSaturatedEnabled = e.IsSaturatedEnabled,
        Cohesion = e.Cohesion,
        FrictionAngle = e.FrictionAngle,
        DilationAngle = e.DilationAngle,
        TensileCutoff = e.TensileCutoff,
        Ucs = e.Ucs,
        HoekBrownMi = e.HoekBrownMi,
        Gsi = e.Gsi,
        Disturbance = e.Disturbance,
        ModulusRatio = e.ModulusRatio,
        DpAlpha = e.DpAlpha,
        DpK = e.DpK,
        PermeabilityX = e.PermeabilityX,
        PermeabilityY = e.PermeabilityY,
        Porosity = e.Porosity,
        InitialVoidRatio = e.InitialVoidRatio,
        SpecificStorage = e.SpecificStorage,
        ThermalConductivityX = e.ThermalConductivityX,
        ThermalConductivityY = e.ThermalConductivityY,
        HeatCapacity = e.HeatCapacity,
        ThermalExpansion = e.ThermalExpansion,
        Density = e.Density,
        DampingRatio = e.DampingRatio,
        DevelopsUndrainedStrengthOnDrawdown = e.DevelopsUndrainedStrengthOnDrawdown,
        DrawdownRCohesion = e.DrawdownRCohesion,
        DrawdownRFrictionAngle = e.DrawdownRFrictionAngle,
        DrawdownKc1Cohesion = e.DrawdownKc1Cohesion,
        DrawdownKc1Angle = e.DrawdownKc1Angle,
        DrawdownKfCohesion = e.DrawdownKfCohesion,
        DrawdownKfAngle = e.DrawdownKfAngle,
        DrawdownKf = e.DrawdownKf,
        EnableProbabilisticVariation = e.EnableProbabilisticVariation,
        CohesionPhiCorrelation = e.CohesionPhiCorrelation
    };
        material.EnsurePropertyStatistics();
        foreach (var s in e.PropertyStatistics ?? new List<XfemMaterialStatState>())
        {
            material.PropertyStatistics[s.PropertyId] = new MaterialPropertyStatistics
            {
                PropertyId = s.PropertyId,
                UseAsRv = s.UseAsRv,
                Distribution = s.Distribution,
                CovPercent = s.CovPercent,
                Min = s.Min,
                Max = s.Max,
            };
        }
        return material;
    }

    private static void AddTunnelFromAxis(GeometryDefinitionViewModel g, XfemTunnelRow tr)
    {
        var centerAxis = new Point(tr.CenterX, tr.CenterY);
        var centerCanvas = g.ConvertAxisToCanvas(centerAxis);
        var rimAxis = new Point(tr.CenterX + tr.Radius, tr.CenterY);
        var rimCanvas = g.ConvertAxisToCanvas(rimAxis);
        var dx = rimCanvas.X - centerCanvas.X;
        var dy = rimCanvas.Y - centerCanvas.Y;
        var radiusCanvas = Math.Sqrt(dx * dx + dy * dy);
        var fullCircle = new PointCollection();
        const int samples = 64;
        for (var i = 0; i <= samples; i++)
        {
            var angle = i * 2.0 * Math.PI / samples;
            fullCircle.Add(new Point(centerCanvas.X + radiusCanvas * Math.Cos(angle), centerCanvas.Y + radiusCanvas * Math.Sin(angle)));
        }
        var circle = new TunnelExcavationCircle
        {
            Center = centerCanvas,
            Radius = radiusCanvas,
            StageActivation = tr.Stage
        };
        circle.ClippedSegments.Add(fullCircle);
        g.FilteredTunnelExcavationCircles.Add(circle);
        var row = new TunnelExcavationCircleRow
        {
            CircleNumber = tr.CircleNumber,
            CenterX = tr.CenterX,
            CenterY = tr.CenterY,
            Radius = tr.Radius,
            UnitLabel = ProjectUnitSystem.LengthLabel(g.ProjectSettings?.Project.Units),
            Circle = circle,
            Stage = tr.Stage
        };
        g.TunnelExcavationCircleRows.Add(row);
        g.FilteredTunnelExcavationCircleRows.Add(row);
    }

    private static string FormatPondedWaterDisplayPattern(PondedWaterDisplayPattern pattern) =>
        pattern switch
        {
            PondedWaterDisplayPattern.Solid => "solid",
            PondedWaterDisplayPattern.DiagonalHatch => "diagonal_hatch",
            PondedWaterDisplayPattern.CrossHatch => "cross_hatch",
            PondedWaterDisplayPattern.Waves => "waves",
            PondedWaterDisplayPattern.Dots => "dots",
            _ => "depth_gradient",
        };

    private static PondedWaterDisplayPattern ParsePondedWaterDisplayPattern(string? value) =>
        (value ?? "").Trim().ToLowerInvariant() switch
        {
            "solid" => PondedWaterDisplayPattern.Solid,
            "diagonal_hatch" or "diagonal" or "hatch" => PondedWaterDisplayPattern.DiagonalHatch,
            "cross_hatch" or "cross" => PondedWaterDisplayPattern.CrossHatch,
            "waves" or "wave" => PondedWaterDisplayPattern.Waves,
            "dots" or "dot" or "stipple" => PondedWaterDisplayPattern.Dots,
            _ => PondedWaterDisplayPattern.DepthGradient,
        };

    private static void ApplySegmentProps(GeometryLineSegment dst, XfemLineSegment src)
    {
        dst.IsArc = src.IsArc;
        dst.ThroughPoint = src.ThroughPoint ?? "";
        dst.ArcRadius = src.ArcRadius;
        dst.IsApplied = src.IsApplied;
        dst.Tag = src.Tag;
        dst.UnitWeight = src.UnitWeight ?? "";
        dst.IsSaturatedUnitWeightEnabled = src.IsSaturatedUnitWeightEnabled;
        dst.SaturatedUnitWeight = src.SaturatedUnitWeight ?? "";
        dst.Cohesion = src.Cohesion ?? "";
        dst.FrictionAngle = src.FrictionAngle ?? "";
        dst.YoungsModulus = src.YoungsModulus ?? "";
        dst.PoissonsRatio = src.PoissonsRatio ?? "";
        dst.Density = src.Density ?? "";
        dst.ConsiderDensityForFE = src.ConsiderDensityForFE;
        dst.PlaneStrain = src.PlaneStrain;
    }

    private static void ApplyMesh(MeshViewModel m, XfemMeshState dto)
    {
        m.DiscretizationNodes.Clear();
        m.MeshElements.Clear();
        m.MeshInformationRows.Clear();
        m.BoundaryEdgeNodes.Clear();
        int nodeIndex = 0;
        foreach (var n in dto.DiscretizationNodes ?? new List<XfemPoint>())
        {
            nodeIndex++;
            m.DiscretizationNodes.Add(new MeshDiscretizationNodeDisplay
            {
                NodeId = nodeIndex,
                Location = new Point(n.X, n.Y)
            });
        }
        foreach (var el in dto.Elements ?? new List<XfemMeshElement>())
        {
            var nodes = new PointCollection();
            foreach (var p in el.NodePoints ?? new List<XfemPoint>())
                nodes.Add(new Point(p.X, p.Y));
            int npt = el.NodePoints?.Count ?? 0;
            m.MeshElements.Add(new MeshElementDisplay
            {
                Nodes = nodes,
                CenterX = el.CenterX,
                CenterY = el.CenterY,
                DisplayId = el.DisplayId,
                IsActive = el.IsActive,
                IsHighlighted = el.IsHighlighted,
                IsQuadraticTriangle = el.IsQuadraticTriangle || (npt == 6 && !el.IsQuadrilateral),
                IsQuadrilateral = el.IsQuadrilateral || npt == 4 || npt == 8,
                IsQuadraticQuad = el.IsQuadraticQuad || npt == 8,
                IsUnmatchedTriangle = el.IsUnmatchedTriangle,
            });
        }
        foreach (var r in dto.InformationRows ?? new List<XfemMeshInfoRow>())
        {
            m.MeshInformationRows.Add(new MeshInformationRow
            {
                ElementId = r.ElementId,
                Node1 = r.Node1,
                Node2 = r.Node2,
                Node3 = r.Node3,
                Node4 = r.Node4,
                Node5 = r.Node5,
                Node6 = r.Node6,
                Node7 = r.Node7,
                Node8 = r.Node8,
                Etype = r.Etype,
                N1X = r.N1X,
                N1Y = r.N1Y,
                N2X = r.N2X,
                N2Y = r.N2Y,
                N3X = r.N3X,
                N3Y = r.N3Y,
                N4X = r.N4X,
                N4Y = r.N4Y,
                N5X = r.N5X,
                N5Y = r.N5Y,
                N6X = r.N6X,
                N6Y = r.N6Y,
                N7X = r.N7X,
                N7Y = r.N7Y,
                N8X = r.N8X,
                N8Y = r.N8Y,
                Kind = r.Kind ?? "",
                Status = r.Status ?? "Active",
                SmithEmb2dLocalOrder = r.SmithEmb2dLocalOrder,
            });
        }
        for (int i = 0; i < m.MeshElements.Count && i < m.MeshInformationRows.Count; i++)
        {
            if (m.MeshInformationRows[i].SmithEmb2dLocalOrder)
                m.MeshElements[i].Q8SmithEmb2dLocalOrder = true;
        }
        m.OneDMeshLength = dto.OneDMeshLength ?? "";
        m.OneDMeshN = dto.OneDMeshN ?? "5";
        m.OneDMeshStatus = dto.OneDMeshStatus ?? "";
        m.ShowMeshElements = dto.ShowMeshElements;
        m.ShowMeshNodes = dto.ShowMeshNodes;
        m.SelectedTwoDElementFamily = dto.SelectedTwoDElementFamily ?? m.SelectedTwoDElementFamily;
        m.SelectedTwoDNodeOption = dto.SelectedTwoDNodeOption ?? m.SelectedTwoDNodeOption;
        if (!string.IsNullOrEmpty(dto.SelectedMeshNumberingDirection))
            m.SelectedMeshNumberingDirection = dto.SelectedMeshNumberingDirection;

        ApplyMeshUiState(m, dto);

        // Rebuild T6 outlines from information rows *before* IsDiscretized goes true so the first canvas refresh
        // sees 6-node outlines. Resync mutates MeshElements in place (no collection change), so a refresh after
        // IsDiscretized alone would miss upgraded outlines when files store 3 NodePoints + T6 rows.
        if (dto.IsDiscretized)
            m.ResyncT6MeshElementOutlinesFromInformationRows();
        m.IsDiscretized = dto.IsDiscretized;
        m.RestoreJointInterfacesAfterProjectLoad();
    }

    /// <summary>Restores Mesh popup subtabs (RS2D, surface list overrides, tunnel, etc.) written by <see cref="CaptureMesh"/>.</summary>
    private static void ApplyMeshUiState(MeshViewModel m, XfemMeshState dto)
    {
        if (dto.MeshPopupSelectedTabIndex is int tabIdx && tabIdx >= 0)
            m.MeshPopupSelectedTabIndex = MeshViewModel.MigrateMeshPopupTabIndex(tabIdx);
        if (!string.IsNullOrEmpty(dto.MeshElementOrder))
            m.MeshElementOrder = dto.MeshElementOrder;
        if (!string.IsNullOrEmpty(dto.Rs2dMeshType))
            m.Rs2dMeshType = dto.Rs2dMeshType;
        if (!string.IsNullOrEmpty(dto.Rs2dElementType))
            m.Rs2dElementType = MeshViewModel.NormalizeRs2dElementType(dto.Rs2dElementType);
        if (!string.IsNullOrEmpty(dto.Rs2dGradationFactor))
            m.Rs2dGradationFactor = dto.Rs2dGradationFactor;
        if (!string.IsNullOrEmpty(dto.Rs2dExcavationNodeCount))
            m.Rs2dExcavationNodeCount = dto.Rs2dExcavationNodeCount;
        if (!string.IsNullOrEmpty(dto.Rs2dBoundaryNodeCount))
            m.Rs2dBoundaryNodeCount = dto.Rs2dBoundaryNodeCount;
        if (!string.IsNullOrEmpty(dto.Rs2dTargetElementCount))
            m.Rs2dTargetElementCount = dto.Rs2dTargetElementCount;
        if (dto.Rs2dAdvancedExpanded is bool rae)
            m.Rs2dAdvancedExpanded = rae;
        if (dto.Rs2dDiscretized is bool rd)
            m.Rs2dDiscretized = rd;
        m.RestorePersistedGlobalDensity(dto.GlobalDensityPreset, dto.GlobalMeshSize, dto.NearBoundaryMeshSize);
        if (!string.IsNullOrEmpty(dto.SurfaceMeshStatus))
            m.SurfaceMeshStatus = dto.SurfaceMeshStatus;
        if (!string.IsNullOrEmpty(dto.RectangularMeshL))
            m.RectangularMeshL = dto.RectangularMeshL;
        if (!string.IsNullOrEmpty(dto.RectangularMeshH))
            m.RectangularMeshH = dto.RectangularMeshH;
        if (!string.IsNullOrEmpty(dto.RectangularMeshNx))
            m.RectangularMeshNx = dto.RectangularMeshNx;
        if (!string.IsNullOrEmpty(dto.RectangularMeshNy))
            m.RectangularMeshNy = dto.RectangularMeshNy;
        if (!string.IsNullOrEmpty(dto.RectangularMeshStatus))
            m.RectangularMeshStatus = dto.RectangularMeshStatus;
        if (!string.IsNullOrEmpty(dto.TriangleMeshStatus))
            m.TriangleMeshStatus = dto.TriangleMeshStatus;
        if (dto.TunnelMeshGenerationMethod is TunnelMeshGenerationMethod tgm)
            m.TunnelMeshGenerationMethod = tgm;
        if (dto.MeshSmoothingMethod is MeshSmoothingMethod msm)
            m.MeshSmoothingMethod = msm;
        if (!string.IsNullOrEmpty(dto.SmoothingIterations))
            m.SmoothingIterations = dto.SmoothingIterations;
        if (dto.AutoRegenerateMesh is bool arm)
            m.AutoRegenerateMesh = arm;
        if (dto.RefineNearExcavation is bool rne)
            m.RefineNearExcavation = rne;
        if (dto.RefineNearLoads is bool rnl)
            m.RefineNearLoads = rnl;
        if (dto.RefineNearMaterialInterfaces is bool rnmi)
            m.RefineNearMaterialInterfaces = rnmi;
        if (dto.RefineNearStiffnessContrast is bool rnsc)
            m.RefineNearStiffnessContrast = rnsc;
        if (!string.IsNullOrEmpty(dto.TunnelDiameter))
            m.TunnelDiameter = dto.TunnelDiameter;
        if (!string.IsNullOrEmpty(dto.FarFieldEdgeLength))
            m.FarFieldEdgeLength = dto.FarFieldEdgeLength;
        if (!string.IsNullOrEmpty(dto.NearTunnelEdgeLength))
            m.NearTunnelEdgeLength = dto.NearTunnelEdgeLength;
        if (!string.IsNullOrEmpty(dto.RingCount))
            m.RingCount = dto.RingCount;
        if (!string.IsNullOrEmpty(dto.RingGrowthFactor))
            m.RingGrowthFactor = dto.RingGrowthFactor;
        if (!string.IsNullOrEmpty(dto.FirstRingThickness))
            m.FirstRingThickness = dto.FirstRingThickness;
        if (!string.IsNullOrEmpty(dto.PointLoadRefinementRadius))
            m.PointLoadRefinementRadius = dto.PointLoadRefinementRadius;
        if (!string.IsNullOrEmpty(dto.MinAngleDegrees))
            m.MinAngleDegrees = dto.MinAngleDegrees;
        if (!string.IsNullOrEmpty(dto.MaxRefinementIterations))
            m.MaxRefinementIterations = dto.MaxRefinementIterations;
        if (!string.IsNullOrEmpty(dto.MergeTolerance))
            m.MergeTolerance = dto.MergeTolerance;
        if (!string.IsNullOrEmpty(dto.TunnelMeshStatus))
            m.TunnelMeshStatus = dto.TunnelMeshStatus;

        m.RefreshSurfaceMeshRows();
        if (dto.SurfaceMeshRowOverrides is { Count: > 0 } ovs)
        {
            foreach (var ov in ovs)
            {
                var row = m.SurfaceMeshRows.FirstOrDefault(r => r.SurfaceNumber == ov.SurfaceNumber);
                if (row == null) continue;
                row.MeshSizeOverride = ov.MeshSizeOverride ?? "";
                row.IsExcluded = ov.IsExcluded;
                if (!string.IsNullOrWhiteSpace(ov.Refinement)
                    && Enum.TryParse<MeshRefinementLevel>(ov.Refinement, ignoreCase: true, out var lvl))
                    row.Refinement = lvl;
            }
        }

        m.RefreshTriangleMeshStructuredExtentsFromGeometry();
    }

    private static XfemFieldStationSetState? CaptureFieldStations(FieldStationSet set)
    {
        if (set.Stations.Count == 0 && set.UnitMaterialMap.Count == 0)
            return null;

        return new XfemFieldStationSetState
        {
            LastCommitUtc = set.LastCommitUtc?.ToString("O"),
            Stations = set.Stations.Select(s => new XfemFieldStationState
            {
                Id = s.Id,
                X = s.X,
                CollarY = s.CollarY,
                Label = string.IsNullOrWhiteSpace(s.Label) ? null : s.Label,
                Intervals = s.Intervals.Select(i => new XfemFieldUnitInterval
                {
                    TopY = i.TopY,
                    BottomY = i.BottomY,
                    UnitCode = i.UnitCode,
                    Note = string.IsNullOrWhiteSpace(i.Note) ? null : i.Note
                }).ToList()
            }).ToList(),
            UnitMaterialMap = set.UnitMaterialMap.Select(r => new XfemFieldUnitMapRow
            {
                UnitCode = r.UnitCode,
                MaterialId = r.MaterialId,
                ColourArgb = MaterialColourToArgb(r.Colour)
            }).ToList()
        };
    }

    private static void ApplyFieldStations(FieldStationSet target, XfemFieldStationSetState? dto)
    {
        target.Clear();
        if (dto == null)
            return;

        if (dto.Stations != null)
        {
            foreach (var s in dto.Stations)
            {
                var station = new FieldStation
                {
                    Id = s.Id ?? "",
                    X = s.X,
                    CollarY = s.CollarY,
                    Label = s.Label ?? ""
                };
                if (s.Intervals != null)
                {
                    foreach (var i in s.Intervals)
                    {
                        station.Intervals.Add(new FieldUnitInterval
                        {
                            TopY = i.TopY,
                            BottomY = i.BottomY,
                            UnitCode = i.UnitCode ?? "",
                            Note = i.Note ?? ""
                        });
                    }
                }
                target.Stations.Add(station);
            }
        }

        if (dto.UnitMaterialMap != null)
        {
            var used = new List<Color>();
            foreach (var row in dto.UnitMaterialMap)
            {
                Color colour;
                if (row.ColourArgb is uint argb && argb != 0)
                    colour = MaterialColourFromArgb(argb);
                else
                    colour = FieldStationUnitColours.NextUnused(used);
                if (!used.Contains(colour))
                    used.Add(colour);

                target.UnitMaterialMap.Add(new FieldUnitMaterialMapRow
                {
                    UnitCode = row.UnitCode ?? "",
                    MaterialId = row.MaterialId,
                    Colour = colour
                });
            }
        }
        else
        {
            target.RebuildUnitMapPreservingAssignments();
        }

        if (!string.IsNullOrWhiteSpace(dto.LastCommitUtc)
            && DateTimeOffset.TryParse(dto.LastCommitUtc, out var stamp))
            target.LastCommitUtc = stamp;

        target.NotifyCounts();
    }

    // --- Document DTOs ---

    public class XfemDocument
    {
        public string FormatVersion { get; set; } = CurrentFormatVersion;
        public XfemMainState? Main { get; set; }
        public XfemProjectSettingsState? ProjectSettings { get; set; }
        public XfemGeometryState? Geometry { get; set; }
        public XfemMaterialsState? Materials { get; set; }
        public XfemMeshState? Mesh { get; set; }
        public List<XfemLoad>? Loads { get; set; }
        /// <summary>Groundwater → Add Water Head rows; null/empty in older files.</summary>
        public List<XfemWaterHead>? GroundwaterHeads { get; set; }
        /// <summary>Typed/mouse-placed transient prescribed-pressure nodes.</summary>
        public List<XfemTransientFixedPressure>? TransientFixedPressures { get; set; }
        public XfemBoundaryConditionsState? BoundaryConditions { get; set; }
        public XfemFeAnalysisState? FeAnalysis { get; set; }
        public XfemResultsState? Results { get; set; }
        public XfemScriptState? Script { get; set; }
        /// <summary>Per-stage excavation zones (axis polygons); optional for older files.</summary>
        public XfemStagedConstructionState? StagedConstruction { get; set; }
        /// <summary>Field-stress configuration (Constant/Gravity global + per-material overrides). Optional for older files.</summary>
        public XfemFieldStressState? FieldStress { get; set; }
        /// <summary>Script-level pseudo-static enable + k_h/k_v (Load Conditions → Seismic / Inertia Load). Optional for older files.</summary>
        public XfemSeismicLoadState? SeismicLoad { get; set; }
        /// <summary>FE support property sets and 2-node placements. Optional for older files.</summary>
        public XfemSupportState? Support { get; set; }
        /// <summary>Wave 3 delivery settings (1.11+).</summary>
        public XfemWave3State? Wave3 { get; set; }
        /// <summary>Optional Analysis Workspace intent / SSR extras. Layout mode stays in user settings.</summary>
        public XfemAnalysisWorkspaceState? AnalysisWorkspace { get; set; }
        /// <summary>User-Defined Mesh node/element tables and Q8 generator parameters (1.2+).</summary>
        public XfemUserDefinedMeshState? UserDefinedMesh { get; set; }
        /// <summary>Verification tab inputs (1.2+).</summary>
        public XfemVerificationState? Verification { get; set; }

        /// <summary>Canvas drawing aids: grid snap, object snaps and the ortho constraint (1.10+).</summary>
        public GeometrySnapSettings? SnapSettings { get; set; }
    }

    public class XfemAnalysisWorkspaceState
    {
        public string? Intent { get; set; }
        public double InitialStrengthReductionFactor { get; set; } = 1.0;
        public double StrengthReductionIncrement { get; set; } = 0.1;
        public bool CheckDisplacementJump { get; set; } = true;
        public bool CheckNonConvergence { get; set; } = true;
        public bool CheckPlasticZoneConnect { get; set; }
        public string? PorePressureOption { get; set; }
        public double? ExplorerWidth { get; set; }
        public double? ResultsWidth { get; set; }
        public double? SolverLogHeight { get; set; }
    }

    public class XfemWave3State
    {
        public FemProbabilisticEngine Engine { get; set; }
        public int SampleCount { get; set; } = 100;
        public double DesignFactorOfSafety { get; set; } = 1;
        public int RandomSeed { get; set; } = 1;
        public bool UseFixedSeed { get; set; } = true;
        public double SensitivityVariationPercent { get; set; } = 20;
        public double BackTargetFs { get; set; } = 1;
        public double BackFsTolerance { get; set; } = 1e-4;
        public int BackMaxIterations { get; set; } = 40;
        public XfemReportOptionsState? Report { get; set; }
        public List<XfemAnalysisCaseState>? Cases { get; set; }
    }

    public class XfemReportOptionsState
    {
        public string? Company { get; set; }
        public string? ReportTitle { get; set; }
        public string? Client { get; set; }
        public string? Location { get; set; }
        public string? DocumentNumber { get; set; }
        public string? Revision { get; set; }
        public string? PreparedBy { get; set; }
        public string? CheckedBy { get; set; }
        public string? Notes { get; set; }
        public bool IncludeCoverPage { get; set; } = true;
        public bool IncludeMaterials { get; set; } = true;
        public bool IncludeGroundwater { get; set; } = true;
        public bool IncludeValidation { get; set; } = true;
        public bool IncludeSensitivity { get; set; } = true;
        public bool IncludeProbabilistic { get; set; } = true;
        public bool IncludeBackAnalysis { get; set; } = true;
    }

    public class XfemAnalysisCaseState
    {
        public string? Name { get; set; }
        public bool IsActive { get; set; } = true;
        public double SeismicKh { get; set; }
        public double SeismicKv { get; set; }
        public double LoadFactor { get; set; } = 1;
        public string? Notes { get; set; }
        public double? LastFoS { get; set; }
        public string? Status { get; set; }
    }

    public class XfemMaterialStatState
    {
        public MaterialStatPropertyId PropertyId { get; set; }
        public bool UseAsRv { get; set; }
        public FemStatDistribution Distribution { get; set; }
        public double CovPercent { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
    }

    // ── Field Stress (Load Conditions) DTOs ─────────────────────────────────
    public class XfemSeismicLoadState
    {
        public bool SeismicAnalysisEnabled { get; set; }
        public double ScriptPseudoStaticKh { get; set; }
        public double ScriptPseudoStaticKv { get; set; }
        public XfemNewmarkState? Newmark { get; set; }
    }

    public class XfemNewmarkState
    {
        public bool IsEnabled { get; set; }
        public double YieldAcceleration { get; set; }
        public bool EstimateKyFromSrf { get; set; } = true;
        public double PgaG { get; set; }
        public double Magnitude { get; set; }
        public double AriasIntensity { get; set; }
        public double SlidingMassPeriodTs { get; set; }
        public double SpectralAccelerationG { get; set; }
        public double FundamentalPeriodT0 { get; set; }
        public bool UseJibson { get; set; }
        public bool UseJibsonMagnitude { get; set; }
        public bool UseJibsonArias { get; set; }
        public bool UseAmbraseys { get; set; }
        public bool UseMakdisiSeed { get; set; }
        public bool UseBrayTravasarou { get; set; }
        public bool UseTimeHistory { get; set; }
        public string? TimeHistoryPath { get; set; }
        public FeNewmarkAccelerationUnits TimeHistoryUnits { get; set; }
        public double TimeHistoryScale { get; set; } = 1.0;
        public bool TwoWaySliding { get; set; }
    }

    public class XfemSupportState
    {
        public List<XfemSupportPropertySet>? PropertySets { get; set; }
        public List<XfemSupportElement>? Elements { get; set; }
    }

    public class XfemSupportPropertySet
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Type { get; set; }
        public double AxialStiffnessKn { get; set; }
        public double PrestressKn { get; set; }
        public double OutOfPlaneSpacingM { get; set; }
        public double TensileCapacityKn { get; set; }
        public double BondStrengthKnPerM { get; set; }
        public double BondLengthM { get; set; }
        public double FreeLengthM { get; set; }
        public double ShearCapacityKn { get; set; }
        public int InstallStage { get; set; }
        public bool ExcludeFromSrf { get; set; } = true;
    }

    public class XfemSupportElement
    {
        public int Id { get; set; }
        public int PropertySetId { get; set; }
        public double HeadX { get; set; }
        public double HeadY { get; set; }
        public double TailX { get; set; }
        public double TailY { get; set; }
    }

    public class XfemDrawdownState
    {
        public bool IsEnabled { get; set; }
        public string? Method { get; set; }
        public double PreReservoirLevel { get; set; }
        public double PostReservoirLevel { get; set; }
        public bool TakeLowerOfUndrainedAndDrained { get; set; } = true;
        public List<XfemPoint>? PreDrawdownSurface { get; set; }
        public List<XfemPoint>? PostDrawdownSurface { get; set; }
    }

    public class XfemFieldStressState
    {
        public XfemFieldStressSpec? Global { get; set; }
        public List<XfemFieldStressOverride>? Overrides { get; set; }
        /// <summary>Explicit traction-group block parsed from .dat / hand-edited via the GUI. Optional for older files.</summary>
        public List<XfemTractionGroup>? TractionGroups { get; set; }
    }

    public class XfemTractionGroup
    {
        public int Mode { get; set; }
        public double Sxx { get; set; }
        public double Syy { get; set; }
        public double Txy { get; set; }
        public List<XfemTractionEdge>? Edges { get; set; }
    }

    public class XfemTractionEdge
    {
        public int NodeA { get; set; }
        public int NodeB { get; set; }
        public int NodeC { get; set; }
    }

    public class XfemFieldStressSpec
    {
        public FieldStressMode Mode { get; set; } = FieldStressMode.Gravity;
        public double Sigma1 { get; set; } = -1000.0;
        public double Sigma3 { get; set; } = -500.0;
        public double SigmaZ { get; set; } = -750.0;
        public double AngleDeg { get; set; }
        public double DirectSxx { get; set; } = -100.0;
        public double DirectSyy { get; set; } = -100.0;
        public double DirectTxy { get; set; }
        public double Kx { get; set; } = 0.5;
        public double Kz { get; set; } = 0.5;
        public bool AutoSurface { get; set; } = true;
        public double YSurface { get; set; }
        public bool UseEffectiveStressRatio { get; set; }
    }

    public class XfemFieldStressOverride
    {
        public int MaterialIndex { get; set; } = 1;
        public string? MaterialName { get; set; }
        public XfemFieldStressSpec? Spec { get; set; }
    }

    public class XfemMainState
    {
        public int SelectedTabIndex { get; set; }
        public bool ZoomPanModeEnabled { get; set; }
        public int? CurrentStage { get; set; }
    }

    public class XfemProjectSettingsState
    {
        public ProblemDimension SelectedDimension { get; set; }
        public int SelectedSettingsTabIndex { get; set; }
        public bool IsDeterministicAnalysis { get; set; }
        public bool IsProbabilisticAnalysis { get; set; }
        public int NumberOfStages { get; set; }
        public int? SelectedStageFooterTabIndex { get; set; }
        public List<XfemStageFooterTab>? StageFooterTabs { get; set; }
        /// <summary>Legacy (pre master-detail Analysis panel); read for backward compatibility only.</summary>
        public List<XfemAnalysisTypeSubtab>? AnalysisTypeSubtabs { get; set; }
        public bool? GwStaticIncludeWaterSurfaces { get; set; }
        public bool? GwStaticIncludePondedFreeWater { get; set; }
        public bool? GwStaticIncludeRuCoefficients { get; set; }
        /// <summary>Legacy Static → Water Head; migrated to SeepageIncludeWaterHead* on load.</summary>
        public bool? GwStaticIncludeWaterHead { get; set; }
        public bool? GwStaticIncludeWaterPressureGridTotalHead { get; set; }
        public bool? GwStaticIncludeWaterPressureGridPressureHead { get; set; }
        public bool? GwStaticIncludeWaterPressureGridPorePressure { get; set; }
        /// <summary>Steady seepage (p72/p73/p74) options; null in older project files.</summary>
        public double? SeepageUpstreamHead { get; set; }
        public double? SeepageDownstreamHead { get; set; }
        public bool? SeepageAxisymmetric { get; set; }
        /// <summary>Mechanical continuum axisymmetric SRF (<c>*_SRF_axisym</c>); null in older projects.</summary>
        public bool? SrfAxisymmetric { get; set; }
        public double? SeepageFreeSurfaceTolerance { get; set; }
        public int? SeepageFreeSurfaceMaxIterations { get; set; }
        public bool? SeepageIncludeWaterHeadPlaneAxisym { get; set; }
        public bool? SeepageIncludeWaterHeadFreeSurface { get; set; }
        public bool? SeepageIncludeWaterHeadGeneral2D { get; set; }
        public bool? SeepageIncludeWaterHeadGeneral3D { get; set; }
        /// <summary>Transient State (p84/p88) options; null in older project files.</summary>
        public bool? TransientAxisymmetric { get; set; }
        public double? TransientTimeStep { get; set; }
        public int? TransientNumberOfSteps { get; set; }
        public double? TransientTheta { get; set; }
        public int? TransientPrintInterval { get; set; }
        public int? TransientMonitorNode { get; set; }
        public int? TransientContourStep { get; set; }
        public int? TransientContourIntervals { get; set; }
        public double? TransientInitialPressure { get; set; }
        public double? TransientFixedPressure { get; set; }
        public bool? TransientFixTopFace { get; set; }
        public bool? TransientFixBottomFace { get; set; }
        public bool? TransientFixMinXFace { get; set; }
        public bool? TransientFixMaxXFace { get; set; }
        /// <summary>Project Settings → Analysis category ON flags; null in older project files (all ON).</summary>
        public List<XfemAnalysisCategoryOnFlag>? AnalysisCategoryOnFlags { get; set; }
        public string? ProjectName { get; set; }
        public string? Author { get; set; }
        public DateTime CreatedOn { get; set; }
        public string? Units { get; set; }
        public int BishopMaxIterations { get; set; }
        public double BishopConvergenceTolerance { get; set; }
        public double BishopInitialFactor { get; set; }
        public int NumberOfStagesModel { get; set; }
        public double AxisXMin { get; set; }
        public double AxisXMax { get; set; }
        public double AxisYMin { get; set; }
        public double AxisYMax { get; set; }
        public double GridSpacingMeters { get; set; }
        public bool ShowGrid { get; set; }
        public bool ShowAxes { get; set; } = true;
        public XfemCoordinateSystem? CoordinateSystem { get; set; }
        public XfemFieldStationSetState? FieldStations { get; set; }
        public double? FieldStationSoilBlockMargin { get; set; }
        public double? FieldStationReferenceElevation { get; set; }
        public bool? FieldStationsActive { get; set; }
        public bool? PlotBoreholes { get; set; }
        public XfemDrawdownState? Drawdown { get; set; }
    }

    public class XfemCoordinateSystem
    {
        public int? EpsgCode { get; set; }
        public string? Name { get; set; }
        public double OriginEasting { get; set; }
        public double OriginNorthing { get; set; }
        public double RotationDeg { get; set; }
        public double Scale { get; set; } = 1.0;
    }

    public class XfemFieldStationSetState
    {
        public List<XfemFieldStationState>? Stations { get; set; }
        public List<XfemFieldUnitMapRow>? UnitMaterialMap { get; set; }
        public string? LastCommitUtc { get; set; }
    }

    public class XfemFieldStationState
    {
        public string? Id { get; set; }
        public double X { get; set; }
        public double CollarY { get; set; }
        public string? Label { get; set; }
        public List<XfemFieldUnitInterval>? Intervals { get; set; }
    }

    public class XfemFieldUnitInterval
    {
        public double TopY { get; set; }
        public double BottomY { get; set; }
        public string? UnitCode { get; set; }
        public string? Note { get; set; }
    }

    public class XfemFieldUnitMapRow
    {
        public string? UnitCode { get; set; }
        public int? MaterialId { get; set; }
        public uint? ColourArgb { get; set; }
    }

    public class XfemStageFooterTab
    {
        public int StageNumber { get; set; }
        public string? StageName { get; set; }
    }

    public class XfemAnalysisTypeSubtab
    {
        public string? AnalysisCategory { get; set; }
        public string? DetailedAnalysisType { get; set; }
        public string? Header { get; set; }
        public bool? IncludeWaterSurfaces { get; set; }
        public bool? IncludeRuCoefficients { get; set; }
        public bool? IncludeWaterPressureGridTotalHead { get; set; }
        public bool? IncludeWaterPressureGridPressureHead { get; set; }
        public bool? IncludeWaterPressureGridPorePressure { get; set; }
    }

    /// <summary>Project Settings → Analysis category master ON switch.</summary>
    public class XfemAnalysisCategoryOnFlag
    {
        public string? Category { get; set; }
        public bool IsOn { get; set; }
    }

    public class XfemLayerMaterialAssignment
    {
        public int DisplayLayerNumber { get; set; }
        public int MaterialId { get; set; }
    }

    /// <summary>One finalized non-horizontal material interface polyline (axis coordinates).</summary>
    public class XfemNonHorizontalLayerCut
    {
        public List<XfemPoint>? Points { get; set; }
    }

    public class XfemLayerDisplayName
    {
        public int DisplayLayerNumber { get; set; }
        public string? Name { get; set; }
    }

    public class XfemLayerDisplayColor
    {
        public int DisplayLayerNumber { get; set; }
        public uint Argb { get; set; }
    }

    /// <summary>
    /// One Materials &amp; Regions "Assign" paint override (Materials grid Assign + canvas surface click).
    /// <see cref="Argb"/> is the painted brush colour; <see cref="MaterialId"/> is the source material's
    /// project-library id (nullable so older saves and ad-hoc fills still round-trip).
    /// </summary>
    public class XfemSurfaceMaterialFill
    {
        public int SurfaceNumber { get; set; }
        public uint Argb { get; set; }
        public int? MaterialId { get; set; }
    }

    public class XfemGeometryState
    {
        public double CanvasWidth { get; set; }
        public double CanvasHeight { get; set; }
        public double AxisXMin { get; set; }
        public double AxisXMax { get; set; }
        public double AxisYMin { get; set; }
        public double AxisYMax { get; set; }
        public string? LayerElevationInput { get; set; }
        public bool HasAddedElevationLayer { get; set; }
        /// <summary>Explicit "Add layer" elevations for horizontal cut overlay (axis units).</summary>
        public List<double>? MaterialLayerCutElevations { get; set; }

        /// <summary>Non-horizontal (inclined/curved) material interface polylines in axis coordinates.</summary>
        public List<XfemNonHorizontalLayerCut>? NonHorizontalLayerCuts { get; set; }

        /// <summary>Project library material ID per layering row (display layer #).</summary>
        public List<XfemLayerMaterialAssignment>? LayerMaterialAssignments { get; set; }

        /// <summary>User-editable layer names from Layering Controls (display layer #).</summary>
        public List<XfemLayerDisplayName>? LayerDisplayNames { get; set; }

        /// <summary>Layer colour swatches (ARGB).</summary>
        public List<XfemLayerDisplayColor>? LayerDisplayColors { get; set; }

        /// <summary>Materials &amp; Regions Assign paint overrides (per surface number).</summary>
        public List<XfemSurfaceMaterialFill>? SurfaceMaterialFills { get; set; }
        public string? TunnelCenterXInput { get; set; }
        public string? TunnelCenterYInput { get; set; }
        public string? TunnelRadiusInput { get; set; }
        public string? GeometryXInput { get; set; }
        public string? GeometryYInput { get; set; }
        public List<XfemPoint>? Vertices { get; set; }
        public List<XfemLineSegment>? LineSegments { get; set; }
        public List<XfemInternalEdge>? InternalEdges { get; set; }
        /// <summary>Draw-circle tool: center + radius (axis units); one logical line segment each.</summary>
        public List<XfemGeometryCircle>? GeometryCircles { get; set; }
        /// <summary>Surface grid rows (closed regions from L1–L5 picks).</summary>
        public List<XfemGeometrySurface>? Surfaces { get; set; }
        /// <summary>Which surface row was selected in the details grid.</summary>
        public int? SelectedSurfaceNumber { get; set; }
        public List<XfemTunnelRow>? TunnelRows { get; set; }
        public List<XfemGroundwaterLine>? GroundwaterLines { get; set; }
        public bool? GroundwaterEnabled { get; set; }
        public double? GroundwaterGammaW { get; set; }
        public string? GroundwaterMethod { get; set; }
        public double? GroundwaterRu { get; set; }
        public double? GroundwaterBbar { get; set; }
        /// <summary>Canvas-only zone visibility. Null in legacy files means true.</summary>
        public bool? GroundwaterShowSaturatedMaterialZone { get; set; }
        /// <summary>Canvas-only zone visibility. Null in legacy files means false.</summary>
        public bool? GroundwaterShowUnsaturatedMaterialZone { get; set; }
        /// <summary>Canvas-only ponded free-water depth field. Null in legacy files means true.</summary>
        public bool? GroundwaterShowPondedFreeWaterRegion { get; set; }
        /// <summary>Canvas-only ponded fill pattern. Null → depth_gradient.</summary>
        public string? GroundwaterPondedWaterDisplayPattern { get; set; }
        /// <summary>Canvas-only ponded face-pressure load arrows. Null in legacy files means true.</summary>
        public bool? GroundwaterShowPondedFacePressureArrows { get; set; }
        /// <summary>Canvas-only ponded head isobars. Null in legacy files means true.</summary>
        public bool? GroundwaterShowPondedHeadIsobars { get; set; }
        /// <summary>"phreatic" or "total_head". Null → phreatic.</summary>
        public string? GroundwaterPondedWaterLevelMethod { get; set; }
        public double? GroundwaterPondedTotalHead { get; set; }
        public List<XfemPoint>? GroundwaterSurfPoints { get; set; }
        public List<XfemWaterPressureNamedGrid>? WaterPressureGrids { get; set; }
        public List<XfemWaterPressureGridPoint>? WaterPressureGridTotalHead { get; set; }
        public List<XfemWaterPressureGridPoint>? WaterPressureGridPressureHead { get; set; }
        public List<XfemWaterPressureGridPoint>? WaterPressureGridPorePressure { get; set; }
        public List<List<XfemPoint>>? ExcavationPolygonsAxis { get; set; }
        /// <summary>Surface region boolean / excavate history (canvas shows last operation).</summary>
        public List<SurfaceBooleanOperationSnapshot>? SurfaceExcavationOperations { get; set; }
        public List<XfemGeologicalFeature>? GeologicalFeatures { get; set; }
        public List<XfemJointPropertySet>? JointPropertySets { get; set; }
        public List<XfemJointNetwork>? JointNetworks { get; set; }
        public List<XfemKernelBooleanOperation>? KernelBooleanHistory { get; set; }

        /// <summary>
        /// Georeferenced slope-image underlay (format 1.9+). Image bytes live in a PNG sidecar
        /// next to the .xfem; this object stores the relative path and calibration.
        /// </summary>
        public XfemSlopeImageUnderlay? SlopeImageUnderlay { get; set; }
    }

    /// <summary>Calibration + path for a Geometry Definition raster underlay (sidecar PNG).</summary>
    public class XfemSlopeImageUnderlay
    {
        /// <summary>Path relative to the .xfem (e.g. <c>project.underlay.png</c>), or absolute.</summary>
        public string? ImageRelativePath { get; set; }
        public bool IsVisible { get; set; } = true;
        public double Opacity { get; set; } = 0.65;
        /// <summary><c>twoPoint</c> or <c>axisAlignedBounds</c>.</summary>
        public string? Mode { get; set; }
        public int ImagePixelWidth { get; set; }
        public int ImagePixelHeight { get; set; }
        public double Point1ImageX { get; set; }
        public double Point1ImageY { get; set; }
        public double Point1WorldX { get; set; }
        public double Point1WorldY { get; set; }
        public double Point2ImageX { get; set; }
        public double Point2ImageY { get; set; }
        public double Point2WorldX { get; set; }
        public double Point2WorldY { get; set; }
        public double WorldMinX { get; set; }
        public double WorldMinY { get; set; }
        public double WorldMaxX { get; set; }
        public double WorldMaxY { get; set; }
    }

    public class XfemGeologicalFeature
    {
        public string Id { get; set; } = "";
        public string Kind { get; set; } = "WeakPlane";
        public string Name { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
        public int? MaterialId { get; set; }
        public double? WaterElevation { get; set; }
        public double? DipAngleDeg { get; set; }
        public double? KnKpa { get; set; }
        public double? KsKpa { get; set; }
        public double? JointCohesionKpa { get; set; }
        public double? JointFrictionDeg { get; set; }
        public double? JointTensionCutoffKpa { get; set; }
        public int? PropertySetId { get; set; }
        public string? EndCondition { get; set; }
        public int InstallStage { get; set; } = 1;
        public string? JointNetworkId { get; set; }
        public string? JointNetworkName { get; set; }
        public List<XfemPoint>? Vertices { get; set; }
    }

    public class XfemJointStageFactor
    {
        public int StageNumber { get; set; } = 1;
        public double KnFactor { get; set; } = 1;
        public double KsFactor { get; set; } = 1;
        public double CohesionFactor { get; set; } = 1;
        public double FrictionFactor { get; set; } = 1;
    }

    public class XfemJointPropertySet
    {
        public int Id { get; set; } = 1;
        public string Name { get; set; } = "Joint 1";
        public uint? ColourArgb { get; set; }
        public string? Criterion { get; set; }
        public double TensileStrengthKpa { get; set; }
        public double PeakCohesionKpa { get; set; }
        public double PeakFrictionDeg { get; set; } = 20;
        public bool UseResidualStrength { get; set; }
        public double ResidualTensileStrengthKpa { get; set; }
        public double ResidualCohesionKpa { get; set; }
        public double ResidualFrictionDeg { get; set; } = 20;
        public double NormalStiffnessKpaPerM { get; set; } = 1e5;
        public double ShearStiffnessKpaPerM { get; set; } = 1e4;
        public bool UseGroundwaterPorePressure { get; set; } = true;
        public bool UseAdditionalPressure { get; set; }
        public double AdditionalPressureKpa { get; set; }
        public bool ApplyPressureToLinerSideOnly { get; set; }
        public bool UseStagedProperties { get; set; }
        public int AllowSlipFromStage { get; set; } = 1;
        public bool InitialJointDeformation { get; set; }
        public List<XfemJointStageFactor>? StageFactors { get; set; }
    }

    public class XfemJointDistribution
    {
        public string? Kind { get; set; }
        public double Mean { get; set; }
        public double StdDev { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public bool AutoMinMax3Std { get; set; } = true;
    }

    public class XfemJointSetParameters
    {
        public double DipDeg { get; set; }
        public double DipStdDeg { get; set; }
        public bool InfiniteLength { get; set; } = true;
        public bool RandomOffset { get; set; }
        public double OffsetLength { get; set; }
        public double FirstJointOffset { get; set; }
        public bool UseStatisticalSpacing { get; set; }
        public XfemJointDistribution? Spacing { get; set; }
        public XfemJointDistribution? Length { get; set; }
        public XfemJointDistribution? Persistence { get; set; }
    }

    public class XfemJointNetwork
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "Network 1";
        public string Model { get; set; } = "ParallelDeterministic";
        public List<int>? TargetSurfaceNumbers { get; set; }
        public int? PropertySetId { get; set; }
        public int? CrossPropertySetId { get; set; }
        public string? EndCondition { get; set; }
        public int InstallStage { get; set; } = 1;
        public int RandomSeed { get; set; } = 1;
        public bool RandomLocation { get; set; } = true;
        public double ThroughX { get; set; }
        public double ThroughY { get; set; }
        public double Intensity { get; set; } = 0.4;
        public string? VoronoiDensityMethod { get; set; }
        public string? VoronoiRegularity { get; set; }
        public bool HexagonHorizontal { get; set; } = true;
        public int SiteCount { get; set; } = 24;
        public double AnisotropyX { get; set; } = 1;
        public double AnisotropyY { get; set; } = 1;
        public XfemJointSetParameters? Set1 { get; set; }
        public XfemJointSetParameters? Set2 { get; set; }
        public XfemJointDistribution? Orientation { get; set; }
        public XfemJointDistribution? Length { get; set; }
        public XfemJointDistribution? Persistence { get; set; }
    }

    public class XfemKernelBooleanOperation
    {
        public int Sequence { get; set; }
        public string Kind { get; set; } = "Union";
        public int SubjectFaceId { get; set; }
        public int ToolFaceId { get; set; }
        public int? ResultMaterialId { get; set; }
        public int? StageNumber { get; set; }
        public string Label { get; set; } = "";
        public List<XfemPoint>? FillRing { get; set; }
    }

    public class XfemPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        /// <summary>Stable vertex id (persists across reordering). Omitted in JSON when null.</summary>
        public int? StablePointId { get; set; }
        /// <summary>When set, vertex was created by splitting this line number (historical reference).</summary>
        public int? SplitFromLineNumber { get; set; }
        /// <summary>True when vertex was inserted by Material Properties Add Layer (horizontal cut).</summary>
        public bool? IsMaterialLayerCutVertex { get; set; }
        public XfemPoint() { }
        public XfemPoint(double x, double y) { X = x; Y = y; }
    }

    public class XfemWaterPressureGridPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Value { get; set; }
    }

    public class XfemWaterPressureNamedGrid
    {
        public string? Name { get; set; }
        public string? GridType { get; set; }
        public List<XfemWaterPressureGridPoint>? Points { get; set; }
    }

    public class XfemInternalEdge
    {
        /// <summary>True for arc edges (three stable ids); false for straight edges between StableMin and StableMax.</summary>
        public bool IsArc { get; set; }
        public int StableMin { get; set; }
        public int StableMax { get; set; }
        /// <summary>Through-point stable id for arcs.</summary>
        public int? StableMid { get; set; }
        public int? StableStart { get; set; }
        public int? StableEnd { get; set; }
    }

    public class XfemGeometryCircle
    {
        public int Id { get; set; }
        public double CenterAxisX { get; set; }
        public double CenterAxisY { get; set; }
        public double RadiusAxis { get; set; }
    }

    public class XfemGeometrySurface
    {
        public int SurfaceNumber { get; set; }
        public string? SurfaceName { get; set; }
        /// <summary>Ordered list of line-segment numbers picked for this surface boundary.</summary>
        public List<int>? SelectedLineNumbers { get; set; }
        // Legacy fields kept for reading older .xfem files; not written in new saves.
        public int? SelectedLine1 { get; set; }
        public int? SelectedLine2 { get; set; }
        public int? SelectedLine3 { get; set; }
        public int? SelectedLine4 { get; set; }
        public int? SelectedLine5 { get; set; }
        public string? Tag { get; set; }
        public int ColorIndex { get; set; }
        /// <summary>Project material library id assigned to this surface region.</summary>
        public int? AssignedMaterialId { get; set; }
        public bool IsPolygonLocked { get; set; }
        public List<string>? LockedPolygonVertexLabels { get; set; }
        public List<string>? DraftPolygonVertexLabels { get; set; }
        public List<int>? ConnectedLineNumbers { get; set; }
    }

    public class XfemLineSegment
    {
        public int LineNumber { get; set; }
        public string? FromPoint { get; set; }
        public string? ToPoint { get; set; }
        /// <summary>False for shared edges between non-consecutive vertices. Defaults to true when omitted (older files).</summary>
        public bool? IsBoundaryEdge { get; set; }
        public bool IsArc { get; set; }
        public string? ThroughPoint { get; set; }
        public double ArcRadius { get; set; }
        public bool IsApplied { get; set; }
        public string? Tag { get; set; }
        public string? UnitWeight { get; set; }
        public bool IsSaturatedUnitWeightEnabled { get; set; }
        public string? SaturatedUnitWeight { get; set; }
        public string? Cohesion { get; set; }
        public string? FrictionAngle { get; set; }
        public string? YoungsModulus { get; set; }
        public string? PoissonsRatio { get; set; }
        public string? Density { get; set; }
        public bool ConsiderDensityForFE { get; set; }
        public bool PlaneStrain { get; set; }
    }

    public class XfemTunnelRow
    {
        public int CircleNumber { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double Radius { get; set; }
        public int? Stage { get; set; }
    }

    public class XfemGroundwaterLine
    {
        public double StartX { get; set; }
        public double StartY { get; set; }
        public double EndX { get; set; }
        public double EndY { get; set; }
        public double Elevation { get; set; }
    }

    public class XfemMaterialsState
    {
        public FailureCriterion SelectedFailureCriterion { get; set; }
        public MaterialType SelectedMaterialType { get; set; }
        public string? SelectedAnalysisCategory { get; set; }
        public string? SelectedDetailedAnalysisType { get; set; }
        /// <summary>"Static", "Steady State", or "Transient State" — last Flow-Seepage tree mode.</summary>
        public string? LastGroundwaterTreeMode { get; set; }
        /// <summary>Last p72/p73/p74 detailed type for coupled seepage → SRF.</summary>
        public string? LastSteadyStateSolverType { get; set; }
        /// <summary>Last p84/p88 detailed type for Transient State.</summary>
        public string? LastTransientSolverType { get; set; }
        public bool ShowDeterministicTab { get; set; }
        public bool ShowProbabilisticTab { get; set; }
        public int? SelectedMaterialLibraryId { get; set; }
        public int? SelectedMaterialsLayerDisplayNumber { get; set; }
        public int? SelectedLayerAssignmentDisplayLayerNumber { get; set; }
        public List<XfemMaterialLibraryEntry>? MaterialLibrary { get; set; }
    }

    public class XfemMaterialLibraryEntry
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public uint ColourArgb { get; set; }
        public FailureCriterion FailureCriterion { get; set; }
        public MaterialType MaterialType { get; set; }
        public double YoungsModulus { get; set; }
        public double PoissonsRatio { get; set; }
        public double YoungsModulusH { get; set; }
        public double YoungsModulusV { get; set; }
        public double PoissonsRatioHH { get; set; }
        public double PoissonsRatioVH { get; set; }
        public double ShearModulusVH { get; set; }
        public double UnitWeight { get; set; }
        public double SaturatedUnitWeight { get; set; }
        public bool IsSaturatedEnabled { get; set; }
        public double Cohesion { get; set; }
        public double FrictionAngle { get; set; }
        public double DilationAngle { get; set; }
        public double TensileCutoff { get; set; }
        public double Ucs { get; set; }
        public double HoekBrownMi { get; set; }
        public double Gsi { get; set; }
        public double Disturbance { get; set; }
        public double ModulusRatio { get; set; }
        public double DpAlpha { get; set; }
        public double DpK { get; set; }
        public double PermeabilityX { get; set; }
        public double PermeabilityY { get; set; }
        public double Porosity { get; set; }
        public double InitialVoidRatio { get; set; }
        public double SpecificStorage { get; set; }
        public double ThermalConductivityX { get; set; }
        public double ThermalConductivityY { get; set; }
        public double HeatCapacity { get; set; }
        public double ThermalExpansion { get; set; }
        public double Density { get; set; }
        public double DampingRatio { get; set; }
        public bool DevelopsUndrainedStrengthOnDrawdown { get; set; }
        public double DrawdownRCohesion { get; set; }
        public double DrawdownRFrictionAngle { get; set; }
        public double DrawdownKc1Cohesion { get; set; }
        public double DrawdownKc1Angle { get; set; }
        public double DrawdownKfCohesion { get; set; }
        public double DrawdownKfAngle { get; set; }
        public double DrawdownKf { get; set; }
        public bool EnableProbabilisticVariation { get; set; }
        public double CohesionPhiCorrelation { get; set; }
        public List<XfemMaterialStatState>? PropertyStatistics { get; set; }
    }

    /// <summary>User overrides for one surface row in the Surface Mesh subtab (merged after geometry load).</summary>
    public class XfemSurfaceMeshRow
    {
        public int SurfaceNumber { get; set; }
        public string? MeshSizeOverride { get; set; }
        public bool IsExcluded { get; set; }
        /// <summary><see cref="MeshRefinementLevel"/> name.</summary>
        public string? Refinement { get; set; }
    }

    public class XfemMeshState
    {
        public string? OneDMeshLength { get; set; }
        public string? OneDMeshN { get; set; }
        public string? OneDMeshStatus { get; set; }
        public bool IsDiscretized { get; set; }
        public bool ShowMeshElements { get; set; }
        public bool ShowMeshNodes { get; set; }
        public string? SelectedTwoDElementFamily { get; set; }
        public string? SelectedTwoDNodeOption { get; set; }
        /// <summary>p51-style mesh numbering: "dir = x", "dir = y", or "dir = r".</summary>
        public string? SelectedMeshNumberingDirection { get; set; }
        public List<XfemPoint>? DiscretizationNodes { get; set; }
        public List<XfemMeshElement>? Elements { get; set; }
        public List<XfemMeshInfoRow>? InformationRows { get; set; }

        /// <summary>Mesh popup TabControl selected index (Surface / RS2D / Tunnel / …).</summary>
        public int? MeshPopupSelectedTabIndex { get; set; }
        public string? MeshElementOrder { get; set; }
        public string? Rs2dMeshType { get; set; }
        public string? Rs2dElementType { get; set; }
        public string? Rs2dGradationFactor { get; set; }
        public string? Rs2dExcavationNodeCount { get; set; }
        /// <summary>Optional Unstructured Mesh boundary discretization node count; null in older files.</summary>
        public string? Rs2dBoundaryNodeCount { get; set; }
        /// <summary>Optional Unstructured Mesh approximate target element count; null in older files.</summary>
        public string? Rs2dTargetElementCount { get; set; }
        public bool? Rs2dAdvancedExpanded { get; set; }
        public bool? Rs2dDiscretized { get; set; }
        public string? GlobalDensityPreset { get; set; }
        public string? GlobalMeshSize { get; set; }
        public string? NearBoundaryMeshSize { get; set; }
        public string? SurfaceMeshStatus { get; set; }
        public List<XfemSurfaceMeshRow>? SurfaceMeshRowOverrides { get; set; }
        public string? RectangularMeshL { get; set; }
        public string? RectangularMeshH { get; set; }
        public string? RectangularMeshNx { get; set; }
        public string? RectangularMeshNy { get; set; }
        public string? RectangularMeshStatus { get; set; }
        public string? TriangleMeshStatus { get; set; }
        public TunnelMeshGenerationMethod? TunnelMeshGenerationMethod { get; set; }
        public MeshSmoothingMethod? MeshSmoothingMethod { get; set; }
        public string? SmoothingIterations { get; set; }
        public bool? AutoRegenerateMesh { get; set; }
        public bool? RefineNearExcavation { get; set; }
        public bool? RefineNearLoads { get; set; }
        public bool? RefineNearMaterialInterfaces { get; set; }
        public bool? RefineNearStiffnessContrast { get; set; }
        public string? TunnelDiameter { get; set; }
        public string? FarFieldEdgeLength { get; set; }
        public string? NearTunnelEdgeLength { get; set; }
        public string? RingCount { get; set; }
        public string? RingGrowthFactor { get; set; }
        public string? FirstRingThickness { get; set; }
        public string? PointLoadRefinementRadius { get; set; }
        public string? MinAngleDegrees { get; set; }
        public string? MaxRefinementIterations { get; set; }
        public string? MergeTolerance { get; set; }
        public string? TunnelMeshStatus { get; set; }
    }

    public class XfemMeshElement
    {
        public int DisplayId { get; set; }
        public bool IsActive { get; set; }
        public bool IsHighlighted { get; set; }
        /// <summary>True for 6-node triangle display (T6 wireframe).</summary>
        public bool IsQuadraticTriangle { get; set; }
        /// <summary>True for Q4/Q8 quadrilateral display.</summary>
        public bool IsQuadrilateral { get; set; }
        /// <summary>True for Q8 (8-node serendipity) display.</summary>
        public bool IsQuadraticQuad { get; set; }
        /// <summary>True for triangle that was unmatched during Q4/Q8 conversion (kept as fallback).</summary>
        public bool IsUnmatchedTriangle { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public List<XfemPoint>? NodePoints { get; set; }
    }

    public class XfemMeshInfoRow
    {
        public int ElementId { get; set; }
        public int Node1 { get; set; }
        public int Node2 { get; set; }
        public int Node3 { get; set; }
        public int Node4 { get; set; }
        public int Node5 { get; set; }
        public int Node6 { get; set; }
        public int Node7 { get; set; }
        public int Node8 { get; set; }
        public int Etype { get; set; }
        public double N1X { get; set; }
        public double N1Y { get; set; }
        public double N2X { get; set; }
        public double N2Y { get; set; }
        public double N3X { get; set; }
        public double N3Y { get; set; }
        public double N4X { get; set; }
        public double N4Y { get; set; }
        public double N5X { get; set; }
        public double N5Y { get; set; }
        public double N6X { get; set; }
        public double N6Y { get; set; }
        public double N7X { get; set; }
        public double N7Y { get; set; }
        public double N8X { get; set; }
        public double N8Y { get; set; }
        public string? Kind { get; set; }
        public string? Status { get; set; }
        public bool SmithEmb2dLocalOrder { get; set; }
    }

    public class XfemLoad
    {
        public LoadType Type { get; set; }
        public LoadDirection Direction { get; set; }
        public double Magnitude { get; set; }
        public double EndMagnitude { get; set; }
        public double StartX { get; set; }
        public double StartY { get; set; }
        public double EndX { get; set; }
        public double EndY { get; set; }
        public int? AppliedToNodeId { get; set; }
        /// <summary>Construction stage this load is scoped to. Null = legacy / pre-stage (visible in every stage).</summary>
        public int? StageNumber { get; set; }
        /// <summary>User-visible load name (e.g. "Distributed Load 1"). Optional for older files.</summary>
        public string? Name { get; set; }
        /// <summary>RS2-style orientation for distributed loads. Null = legacy (no orientation stored).</summary>
        public LoadOrientation? Orientation { get; set; }
        /// <summary>Angle in degrees for AngleFromHorizontal / AngleToBoundary orientations.</summary>
        public double? AngleDeg { get; set; }
        /// <summary>Whether per-stage magnitude scaling is active for this load.</summary>
        public bool? IsStageLoad { get; set; }
        /// <summary>When false, load is Off (hidden on canvas, omitted from solver). Null = legacy → On.</summary>
        public bool? IsEnabled { get; set; }
        public string? IdCode { get; set; }
        public string? ApplyTo { get; set; }
        public string? LoadGroup { get; set; }
        public string? CoordinateSystem { get; set; }
        public LoadDistribution? Distribution { get; set; }
        public string? LoadProjection { get; set; }
        public double? LoadDepth { get; set; }
        public bool? UseProjection { get; set; }
        public bool? HasRampTimeDependency { get; set; }
        public int? ApplyFromStage { get; set; }
        public int? ApplyToStage { get; set; }
        public string? Notes { get; set; }
    }

    /// <summary>Groundwater → Add Water Head row (typed Upstream/Downstream and/or mouse-placed).</summary>
    public class XfemWaterHead
    {
        public string? Name { get; set; }
        public bool? IsEnabled { get; set; }
        public string? Role { get; set; }
        public string? DefinitionMethod { get; set; }
        public double Head { get; set; }
        public string? PlacementType { get; set; }
        public double StartX { get; set; }
        public double StartY { get; set; }
        public double EndX { get; set; }
        public double EndY { get; set; }
        public int? AppliedToNodeId { get; set; }
        public bool? HasPlacement { get; set; }
    }

    public class XfemTransientFixedPressure
    {
        public string? Name { get; set; }
        public bool? IsEnabled { get; set; }
        public string? DefinitionMethod { get; set; }
        public string? Face { get; set; }
        public string? PlacementType { get; set; }
        public double Pressure { get; set; }
        public double StartX { get; set; }
        public double StartY { get; set; }
        public double EndX { get; set; }
        public double EndY { get; set; }
        /// <summary>Legacy alias for StartX in older files.</summary>
        public double AxisX { get; set; }
        /// <summary>Legacy alias for StartY in older files.</summary>
        public double AxisY { get; set; }
        public int NodeId { get; set; }
        public bool? HasPlacement { get; set; }
    }

    public class XfemBoundaryConditionsState
    {
        public List<XfemBoundaryRow>? Rows { get; set; }
        public string? StatusMessage { get; set; }
        public string? NodesText { get; set; }
        public string? SelectedBCTypeForBoundary { get; set; }
        public string? BcValueForBoundary { get; set; }
    }

    public class XfemBoundaryRow
    {
        public int NodeId { get; set; }
        public string? BoundaryName { get; set; }
        public string? BCType { get; set; }
        /// <summary>Structural vs Flow-Seepage/Groundwater; omitted in older .xfem files.</summary>
        public string? AnalysisCategory { get; set; }
        public int DegreeOfFreedom { get; set; }
        public string? Value { get; set; }
        public string? Description { get; set; }
    }

    public class XfemFeAnalysisState
    {
        public string? StatusMessage { get; set; }
        public int? FeAnalysisSubTabIndex { get; set; }
        public bool StagedExcavationEnabled { get; set; }
        public bool GravityEnabled { get; set; }
        public double GravityAcceleration { get; set; }
        public bool LoadSteppingEnabled { get; set; }
        public int NumberOfLoadSteps { get; set; }
        public string? CppExecutableDirectory { get; set; }
        /// <summary>Staged analysis rows (Project Settings → Analysis Type). Omitted in older .xfem files.</summary>
        public List<XfemStageDefinition>? StageDefinitions { get; set; }

        // ─── Solver control block (p610_t3 .dat: tol  limit  incs  layers) ───
        // Nullable so older .xfem files (where these did not exist) load with the
        // FEAnalysisViewModel defaults intact.
        public double? ConvergenceTolerance { get; set; }
        public int? MaxIterations { get; set; }
        public int? LoadIncrements { get; set; }
        public int? LayersOverride { get; set; }
        public int? SrfTrialCount { get; set; }
        public List<double>? SrfValues { get; set; }

        // ─── SRF bracket search (alternative to the fixed nsrf/F-list sweep above) ───
        // Nullable so older .xfem files load with the FEAnalysisViewModel bracket defaults intact.
        public string? SrfSearchMode { get; set; }
        public string? SrfInsituMode { get; set; }
        public bool? AutoDetectSrfBracket { get; set; }
        public double? SrfBracketSeed { get; set; }
        public double? SrfBracketExpansionFactor { get; set; }
        public int? SrfMaxExpansionSteps { get; set; }
        public double? SrfFoSMin { get; set; }
        public double? SrfFoSMax { get; set; }
        public double? SrfBracketTolerance { get; set; }
        public int? SrfMaxBracketIterations { get; set; }
    }

    public class XfemStageDefinition
    {
        public int StageNumber { get; set; }
        public string? StageName { get; set; }
        public string? StageType { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;

        // Phase timing / branching (1.1+ extension; omitted for older files).
        public double? Duration { get; set; }
        public string? Units { get; set; }
        public int? StartsFromStageNumber { get; set; }
        public bool? ResetDisplacementsAtStart { get; set; }

        // Load / stress release.
        public double? StressReleaseLambda { get; set; }
        public double? LoadFactor { get; set; }

        // Solver knobs.
        public string? SolverMode { get; set; }
        public int? MaxIterations { get; set; }
        public double? Tolerance { get; set; }
        public int? MaxLoadSteps { get; set; }
        public bool? ArcLength { get; set; }

        // Activity (surface / structural / groundwater).
        public List<int>? ActivateSurfaceNumbers { get; set; }
        public List<int>? DeactivateSurfaceNumbers { get; set; }
        public bool? LiningEnabled { get; set; }
        public bool? BoltsEnabled { get; set; }
        public string? GroundwaterLineTag { get; set; }

        // Results export flags.
        public bool? ExportUx { get; set; }
        public bool? ExportUy { get; set; }
        public bool? ExportSxx { get; set; }
        public bool? ExportSyy { get; set; }
        public bool? ExportTxy { get; set; }
        public bool? ExportPlasticZones { get; set; }
        public bool? ExportFosHistory { get; set; }

        public string? Notes { get; set; }

        public double? PseudoStaticKh { get; set; }
        public double? PseudoStaticKv { get; set; }
    }

    public class XfemResultsState
    {
        public int? ResultsSubTabIndex { get; set; }
        public string? VectorOutputType { get; set; }
        public string? VectorDisplacementComponent { get; set; }
        public double VectorScale { get; set; } = 1.0;
        public double DeformedMeshExaggeration { get; set; } = 50;
        public bool ShowUndeformedOutline { get; set; } = true;
        public bool ShowMesh { get; set; }
        public bool? IsLinePlotMode { get; set; }
        public string? SelectedOutputType { get; set; }
        public string? VisualizationMode { get; set; }
        public string? FeStatusMessage { get; set; }
        public XfemFeRunSnapshot.XfemFeRunState? FeRun { get; set; }
    }

    public class XfemScriptState
    {
        public string? ScriptContent { get; set; }
        public string? OutputText { get; set; }
        public string? StatusMessage { get; set; }
        public bool? IsManualContent { get; set; }
        public string? PythonScript { get; set; }
        public string? ScriptLanguage { get; set; }
    }

    public class XfemStagedConstructionState
    {
        public int? ActiveStageNumber { get; set; }
        public bool? IsExcavateMode { get; set; }
        public string? StatusMessage { get; set; }
        public List<XfemConstructionStageExcavations>? Stages { get; set; }
    }

    public class XfemConstructionStageExcavations
    {
        public int StageNumber { get; set; }
        public string? StageName { get; set; }
        public string? StageType { get; set; }
        public string? Description { get; set; }
        public bool? IsActive { get; set; }
        public List<int>? ExcavatedSurfaceNumbers { get; set; }
        public List<int>? ExcavatedElementIds { get; set; }
        public List<XfemStageExcavationZoneSnapshot>? Zones { get; set; }
    }

    public class XfemUserDefinedMeshState
    {
        public string? ElementType { get; set; }
        public string? Q8MeshMode { get; set; }
        public string? Q8MeshDensity { get; set; }
        public string? P64Nx1 { get; set; }
        public string? P64Nx2 { get; set; }
        public string? P64Ny1 { get; set; }
        public string? P64Ny2 { get; set; }
        public string? P64W1 { get; set; }
        public string? P64S1 { get; set; }
        public string? P64W2 { get; set; }
        public string? P64H1 { get; set; }
        public string? P64H2 { get; set; }
        public bool? RectUseGeometryExtent { get; set; }
        public string? RectXMin { get; set; }
        public string? RectXMax { get; set; }
        public string? RectYMin { get; set; }
        public string? RectYMax { get; set; }
        public string? RectElementsX { get; set; }
        public string? RectElementsY { get; set; }
        public List<XfemUdmNodeRow>? Nodes { get; set; }
        public List<XfemUdmElementRow>? Elements { get; set; }
    }

    public class XfemUdmNodeRow
    {
        public int NodeId { get; set; }
        public string? X { get; set; }
        public string? Y { get; set; }
    }

    public class XfemUdmElementRow
    {
        public int ElemId { get; set; }
        public string? N1 { get; set; }
        public string? N2 { get; set; }
        public string? N3 { get; set; }
        public string? N4 { get; set; }
        public string? N5 { get; set; }
        public string? N6 { get; set; }
        public string? N7 { get; set; }
        public string? N8 { get; set; }
        public string? Etype { get; set; }
    }

    public class XfemVerificationState
    {
        public string? SelectedProblem { get; set; }
        public double TolerancePercent { get; set; } = 10;
        public bool AutoFromMeta { get; set; } = true;
        public double E_kPa { get; set; }
        public double Nu { get; set; }
        public double Sigma_v_kPa { get; set; }
        public double K0 { get; set; }
        public double A { get; set; }
        public double Cx { get; set; }
        public double Cy { get; set; }
        public double Pi_kPa { get; set; }
        public double PhiDeg { get; set; }
        public double C_kPa { get; set; }
        public double PsiDeg { get; set; }
        public double Gamma { get; set; }
    }

    public class XfemStageExcavationZoneSnapshot
    {
        public int SurfaceNumber { get; set; }
        public int AppliedInStageNumber { get; set; }
        public string? DisplayLabel { get; set; }
        public List<XfemPoint>? AxisPolygon { get; set; }
    }
}
