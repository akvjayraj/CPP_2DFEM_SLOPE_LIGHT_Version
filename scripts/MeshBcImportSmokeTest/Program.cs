using System.Globalization;
using System.IO;
using System.Linq;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

static void Assert(bool cond, string msg)
{
    if (!cond) throw new InvalidOperationException(msg);
}

static string FindRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(dir))
    {
        if (Directory.Exists(Path.Combine(dir, "Verification", "slope")))
            return dir;
        dir = Path.GetDirectoryName(dir)!;
    }
    throw new InvalidOperationException("Could not locate repo root (Verification/slope).");
}

var repoRoot = FindRepoRoot();

const double CornerCoordTol = 0.15;

void AssertEmbankmentCornerCoords(
    string label,
    GeometryDefinitionViewModel geom,
    (int id, double x, double y)[] expected,
    (double x, double y)? phantomBboxCorner = null)
{
    foreach (var (id, x, y) in expected)
    {
        var vert = geom.GeometryVertexRows.FirstOrDefault(v => v.StablePointId == id);
        Assert(vert != null, $"{label}: missing macro corner node {id}");
        Assert(Math.Abs(vert.AxisX - x) < CornerCoordTol,
            $"{label}: node {id} X={vert!.AxisX:G6} expected {x:G6}");
        Assert(Math.Abs(vert.AxisY - y) < CornerCoordTol,
            $"{label}: node {id} Y={vert.AxisY:G6} expected {y:G6}");
    }

    if (phantomBboxCorner is { } phantom)
    {
        foreach (var v in geom.GeometryVertexRows)
        {
            bool atPhantom = Math.Abs(v.AxisX - phantom.x) < CornerCoordTol
                             && Math.Abs(v.AxisY - phantom.y) < CornerCoordTol;
            Assert(!atPhantom,
                $"{label}: phantom bbox corner ({phantom.x:G4}, {phantom.y:G4}) must not be imported (got node {v.StablePointId})");
        }
    }
}

void TestDeck(
    string label,
    string relativePath,
    int expectNodes,
    int expectElems,
    int expectBcs,
    int? expectMacroCorners = null,
    (int id, double x, double y)[]? expectCornerCoords = null,
    (double x, double y)? phantomBboxCorner = null)
{
    var path = Path.Combine(repoRoot, relativePath);
    var content = File.ReadAllText(path);
    Assert(P610AnalysisOptionsHelper.IsSrfDatDeck(content), $"{label}: IsSrfDatDeck");

    var mesh = new MeshViewModel();
    var udm = new UserDefinedMeshViewModel(mesh);
    var bcVm = new BoundaryConditionsViewModel { MeshVM = mesh };

    Assert(
        DatMeshBoundaryImporter.TryImportToViewModels(content, udm, bcVm, out var summary),
        $"{label}: TryImportToViewModels");

    Assert(summary.NNodes == expectNodes, $"{label}: nodes {summary.NNodes} != {expectNodes}");
    Assert(summary.NElements == expectElems, $"{label}: elems {summary.NElements} != {expectElems}");
    Assert(udm.NodeRows.Count == expectNodes, $"{label}: NodeRows {udm.NodeRows.Count}");
    Assert(udm.ElementRows.Count == expectElems, $"{label}: ElementRows {udm.ElementRows.Count}");
    Assert(bcVm.BoundaryConditions.Count == expectBcs, $"{label}: BCs {bcVm.BoundaryConditions.Count} != {expectBcs}");
    Assert(mesh.IsDiscretized, $"{label}: canvas mesh not generated");
    Assert(mesh.DiscretizationNodes.Count == expectNodes, $"{label}: canvas nodes {mesh.DiscretizationNodes.Count}");
    Assert(mesh.MeshElements.Count == expectElems, $"{label}: canvas elems {mesh.MeshElements.Count}");

    var geom = new GeometryDefinitionViewModel { MeshSource = mesh };
    geom.ImportBoundaryFromMesh();
    if (expectMacroCorners is int exactCorners)
    {
        Assert(geom.GeometryVertexRows.Count == exactCorners,
            $"{label}: expected {exactCorners} macro corner verts, got {geom.GeometryVertexRows.Count}");
        Assert(geom.GeometryLineSegments.Count == exactCorners,
            $"{label}: expected {exactCorners} boundary segments, got {geom.GeometryLineSegments.Count}");
    }
    else
    {
        Assert(geom.GeometryVertexRows.Count is >= 4 and <= 12,
            $"{label}: macro corner verts out of range {geom.GeometryVertexRows.Count}");
        Assert(geom.GeometryVertexRows.Count < 50,
            $"{label}: boundary not simplified ({geom.GeometryVertexRows.Count} verts)");
        Assert(geom.GeometryLineSegments.Count == geom.GeometryVertexRows.Count,
            $"{label}: segment count {geom.GeometryLineSegments.Count} != vert count {geom.GeometryVertexRows.Count}");
    }

    Assert(!string.IsNullOrWhiteSpace(geom.GeometryImportStatusMessage), $"{label}: import status");

    if (expectCornerCoords != null)
        AssertEmbankmentCornerCoords(label, geom, expectCornerCoords, phantomBboxCorner);

    Console.WriteLine($"OK {label}: {summary.NNodes} nodes, {summary.NElements} elems, {summary.NBoundaryConditions} BCs ({summary.Source}), geom verts={geom.GeometryVertexRows.Count}, segs={geom.GeometryLineSegments.Count}");
}

(int id, double x, double y)[] P64EmbankmentCornersYUp =
[
    (31, 0, 0),
    (1, 0, 15),
    (941, 20, 15),
    (961, 40, 5),
    (1131, 60, 5),
    (1141, 60, 0),
];

(int id, double x, double y)[] P64EmbankmentCornersYDown =
[
    (31, 0, -15),
    (1, 0, 0),
    (941, 20, 0),
    (961, 40, -10),
    (1131, 60, -10),
    (1141, 60, -15),
];

TestDeck("program64", "Verification/slope/p64_embankment_program64.dat", 1141, 350, 191,
    expectMacroCorners: 6, expectCornerCoords: P64EmbankmentCornersYUp, phantomBboxCorner: (60, 15));
TestDeck("generalized", "Verification/slope/p64_cpp_generalized.dat", 1141, 350, 191,
    expectMacroCorners: 6, expectCornerCoords: P64EmbankmentCornersYDown, phantomBboxCorner: (60, 0));
TestDeck("srf_multi", "Verification/slope/srf_sweep/p64_embankment_srf_multi.dat", 1141, 350, 191,
    expectMacroCorners: 6, expectCornerCoords: P64EmbankmentCornersYUp, phantomBboxCorner: (60, 15));
TestDeck("srf_F150", "Verification/slope/srf_sweep/p64_embankment_F150.dat", 1141, 350, 191,
    expectMacroCorners: 6, expectCornerCoords: P64EmbankmentCornersYUp, phantomBboxCorner: (60, 15));

TestDeck("ex2_twolayer", "Verification/slope/Ex2_twolayerslope/ex2_twolayer_slope.dat", 266, 75, 91);

TestDeck(
    "rs2_example01_t3_F100",
    "Verification/slope/RS2_Example01_simple_slope/t3_srf_compare/library_solver/rs2_example01_t3_F100.dat",
    286,
    500,
    46);

{
    var ex2Path = Path.Combine(repoRoot, "Verification/slope/Ex2_twolayerslope/ex2_twolayer_slope.dat");
    var ex2 = File.ReadAllText(ex2Path);
    var mesh = new MeshViewModel();
    var udm = new UserDefinedMeshViewModel(mesh);
    var bcVm = new BoundaryConditionsViewModel { MeshVM = mesh };
    Assert(DatMeshBoundaryImporter.TryImportToViewModels(ex2, udm, bcVm, out _), "Ex2 mesh import for layering");
    var geom = new GeometryDefinitionViewModel { MeshSource = mesh };
    geom.ImportBoundaryFromMesh();
    var vertexCountBefore = geom.GeometryVertexRows.Count;
    var matVm = new MaterialPropertiesViewModel { GeometryDefinition = geom };
    Assert(DatMaterialImporter.TryImportToProjectMaterials(ex2, matVm), "Ex2 materials vm");
    Assert(matVm.Materials.Count >= 2, $"Ex2 material count {matVm.Materials.Count}");
    Assert(
        DatEtypeLayeringSync.TrySyncLayeringFromMeshEtype(ex2, mesh, matVm, geom, out var layerMsg),
        $"Ex2 etype layering sync: {layerMsg}");
    Assert(geom.GeometryVertexRows.Count >= vertexCountBefore,
        $"Ex2 verts after sync {geom.GeometryVertexRows.Count} < before {vertexCountBefore}");
    Assert(geom.LayerCoordinateSummaries.Count == 3, $"Ex2 layer rows {geom.LayerCoordinateSummaries.Count}");
    Assert(geom.MaterialLayerCutElevations.Count == 2, $"Ex2 cut elevations {geom.MaterialLayerCutElevations.Count}");
    Assert(geom.GetLayerMaterialAssignmentMaterialId(1) == 1, "Ex2 layer 1 mat");
    Assert(geom.GetLayerMaterialAssignmentMaterialId(2) == 2, "Ex2 layer 2 mat");
    Assert(geom.GetLayerMaterialAssignmentMaterialId(3) == 1, "Ex2 layer 3 mat");
    var cuts = geom.MaterialLayerCutElevations.OrderBy(y => y).ToList();
    Assert(Math.Abs(cuts[0] - 0.4) < 0.15, $"Ex2 cut0 {cuts[0]}");
    Assert(Math.Abs(cuts[1] - 1.0) < 0.15, $"Ex2 cut1 {cuts[1]}");
    const double cutTol = 1e-4;
    foreach (var cutY in cuts)
    {
        var atCut = geom.GeometryVertexRows.Count(v => Math.Abs(v.AxisY - cutY) <= cutTol);
        Assert(atCut >= 2, $"Ex2 cut Y={cutY:G4}: expected >=2 boundary points, got {atCut}");
    }
    Assert(matVm.LayerAssignments.Count == 3, $"Ex2 assignment rows {matVm.LayerAssignments.Count}");
    Assert(geom.GeometryLineSegments.Count > 0, "Ex2 layer sync should create numbered line segments");
    Assert(geom.GeometryLineSegments[0].LineNumberLabel == "L1", "Ex2 first segment should be L1");
    Assert(geom.GeometryLineSegments.Any(s => s.LineNumberLabel == "L2"), "Ex2 should have L2 segment");
    Assert(geom.GeometryLineSegments.Any(s => !s.IsBoundaryEdge),
        "Ex2 layer sync should create internal layer-chord segments");
    Console.WriteLine($"OK Ex2 layering: {geom.LayerCoordinateSummaries.Count} layers, cuts=[{cuts[0]:G4}, {cuts[1]:G4}], verts={geom.GeometryVertexRows.Count}, segs={geom.GeometryLineSegments.Count}");
}

{
    // Simple quad: layer sync must insert intersection vertices when outline has no points on cut Y.
    var simple = new GeometryDefinitionViewModel();
    simple.GeometryVertexRows.Clear();
    foreach (var (x, y) in new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 5.0), (0.0, 5.0) })
    {
        var v = new GeometryDefinitionViewModel.GeometryVertexDisplay { AxisX = x, AxisY = y };
        v.StablePointId = simple.AllocateStablePointId();
        simple.GeometryVertexRows.Add(v);
    }
    simple.RebuildGeometryAfterProjectLoad();
    int before = simple.GeometryVertexRows.Count;
    simple.ApplyInferredMaterialLayerCuts(new[] { 2.5 });
    Assert(simple.GeometryVertexRows.Count > before,
        $"Simple quad layer insert: {before} -> {simple.GeometryVertexRows.Count}");
    Assert(simple.GeometryVertexRows.Count(v => Math.Abs(v.AxisY - 2.5) <= 1e-6) >= 2,
        "Simple quad: expected left/right points at cut Y=2.5");
    Assert(simple.GeometryLineSegments.Count >= 5,
        $"Simple quad: expected boundary + layer segments, got {simple.GeometryLineSegments.Count}");
    Assert(simple.GeometryLineSegments[0].LineNumberLabel == "L1", "Simple quad first segment L1");
    Assert(simple.GeometryLineSegments.Any(s => !s.IsBoundaryEdge), "Simple quad layer chord segment");
    Console.WriteLine($"OK simple quad layer insert: {before} -> {simple.GeometryVertexRows.Count} verts, {simple.GeometryLineSegments.Count} segs");
}

var built = P64EmbankmentMeshBuilder.BuildQ8Mesh(20, 10, 10, 5, 20, 20, 20, 10, 5);
Console.WriteLine($"Builder BC count: {built.BoundaryConditions.Count}");
TestDeck("compact", "Verification/slope/p64_cpp.dat", 1141, 350, built.BoundaryConditions.Count);

var t3Path = Path.Combine(repoRoot, "scripts/slope_stability_T3_SRF/ex1_homogeneous_slope.dat");
if (File.Exists(t3Path))
{
    var t3 = File.ReadAllText(t3Path);
    var mesh = new MeshViewModel();
    var udm = new UserDefinedMeshViewModel(mesh);
    var bcVm = new BoundaryConditionsViewModel { MeshVM = mesh };
    Assert(DatMeshBoundaryImporter.TryImportToViewModels(t3, udm, bcVm, out var s), "T3 ex1 import");
    Assert(s.NNodes == 825, $"T3 nodes {s.NNodes}");
    Assert(s.NElements == 1520, $"T3 elems {s.NElements}");
    Assert(bcVm.BoundaryConditions.Count > 0, "T3 BCs");
    Assert(mesh.IsDiscretized && mesh.MeshElements.Count == 1520, "T3 canvas mesh");
    Console.WriteLine($"OK T3 ex1: {s.NNodes} nodes, {s.NElements} elems, {bcVm.BoundaryConditions.Count} BCs");
}

// Parametric embankment deck from UI defaults (must not use canonical file counts when params change)
{
    var opts = new P64EmbankmentDatWriter.WriteOptions();
    var mesh = P64EmbankmentDatWriter.BuildDefaultMesh(opts);
    var dat = P64EmbankmentDatWriter.WriteExplicitProgram64Deck(mesh, opts);
    Assert(dat.Contains("embankment_nx1 20", StringComparison.Ordinal), "parametric embankment nx1 in deck");
    var m = new MeshViewModel();
    var u = new UserDefinedMeshViewModel(m);
    var bc = new BoundaryConditionsViewModel { MeshVM = m };
    Assert(DatMeshBoundaryImporter.TryImportToViewModels(dat, u, bc, out var s), "parametric embankment import");
    Assert(s.NNodes == 1141, $"parametric embankment nodes {s.NNodes}");
    Assert(s.NElements == 350, $"parametric embankment elems {s.NElements}");
    Assert(m.IsDiscretized, "parametric embankment canvas");
    Console.WriteLine($"OK parametric embankment: {s.NNodes} nodes, {s.NElements} elems");
}

// Refined ex2 two-layer embankment (density 1.6 → nx/ny 8): must preserve np_types=2 + etype block.
{
    var ex2Path = Path.Combine(repoRoot, "Verification/slope/Ex2_twolayerslope/ex2_twolayer_slope.dat");
    var ex2 = File.ReadAllText(ex2Path);
    var matVm = new MaterialPropertiesViewModel();
    Assert(DatMaterialImporter.TryImportToProjectMaterials(ex2, matVm), "ex2 refined: import materials");
    var feVm = new FEAnalysisViewModel(new ResultsViewModel());
    Assert(DatSolverControlImporter.TryImportToFeAnalysis(ex2, feVm, srfAnalysisMode: true), "ex2 refined: import SRF tail");

    var opts = P64EmbankmentDatWriter.BuildWriteOptionsFromViewModels(
        8, 8, 8, 8, 1.2, 2, 1.2, 1, 1, matVm, feVm, ex2);
    Assert(opts.NpTypes == 2, $"ex2 refined: np_types {opts.NpTypes}");
    Assert(opts.SrfValues.SequenceEqual(new[] { 1.0, 1.1, 1.2, 1.3, 1.4, 1.5 }),
        $"ex2 refined: SRF ladder [{string.Join(", ", opts.SrfValues)}]");

    var mesh = P64EmbankmentDatWriter.BuildDefaultMesh(opts);
    Assert(mesh.Nels == 192, $"ex2 refined: elems {mesh.Nels}");
    Assert(mesh.Nn == 641, $"ex2 refined: nodes {mesh.Nn}");
    int type2Count = mesh.Elements.Count(e => e.etype == 2);
    Assert(type2Count == 48, $"ex2 refined: type-2 elems {type2Count} (expected 48 for nx=8)");

    var dat = P64EmbankmentDatWriter.WriteExplicitProgram64Deck(mesh, opts);
    Assert(DatMeshBoundaryImporter.TryGetNpTypes(dat, out int np) && np == 2, "ex2 refined deck: np_types=2");
    Assert(dat.Contains("25  1  0  20  100000  0.3", StringComparison.Ordinal), "ex2 refined deck: mat1 row");
    Assert(dat.Contains("15  0.5  0  20  100000  0.3", StringComparison.Ordinal), "ex2 refined deck: mat2 row");

    var m = new MeshViewModel();
    var u = new UserDefinedMeshViewModel(m);
    var bc = new BoundaryConditionsViewModel { MeshVM = m };
    Assert(DatMeshBoundaryImporter.TryImportToViewModels(dat, u, bc, out var s), "ex2 refined deck import");
    Assert(s.NElements == 192, $"ex2 refined import elems {s.NElements}");
    Assert(s.NNodes == 641, $"ex2 refined import nodes {s.NNodes}");
    Assert(u.ElementRows.Count(e => e.Etype == "2") == 48, "ex2 refined import: etype 2 count");
    Console.WriteLine($"OK ex2 refined two-layer: {s.NNodes} nodes, {s.NElements} elems, type2={type2Count}");
}

// Structured rectangular Q8 (serendipity)
{
    var opts = new StructuredRectangularQ8DatWriter.WriteOptions
    {
        XMin = 0, XMax = 60, YMin = 0, YMax = 15,
        ElementsX = 20, ElementsY = 10,
    };
    var mesh = StructuredRectangularQ8DatWriter.BuildMesh(opts);
    Assert(mesh.Nn == 861, $"rect Q8 nodes {mesh.Nn}");
    Assert(mesh.Nels == 200, $"rect Q8 elems {mesh.Nels}");
    Assert(mesh.Nodes.Max(n => n.x) <= 60 + 1e-6 && mesh.Nodes.Max(n => n.y) <= 15 + 1e-6,
        $"builder bounds Xmax={mesh.Nodes.Max(n => n.x)} Ymax={mesh.Nodes.Max(n => n.y)}");
    var dat = StructuredRectangularQ8DatWriter.WriteExplicitDeck(mesh, opts);
    Assert(dat.Contains("q8_connectivity serendipity", StringComparison.Ordinal), "serendipity marker");
    Assert(DatMeshBoundaryImporter.IsSerendipityRectangularQ8Deck(dat), "serendipity deck detect");
    Assert(!DatMeshBoundaryImporter.IsSmithEmbankmentQ8Deck(null, dat), "not Smith embankment");
    var m = new MeshViewModel();
    var u = new UserDefinedMeshViewModel(m);
    var bc = new BoundaryConditionsViewModel { MeshVM = m };
    Assert(DatMeshBoundaryImporter.TryImportToViewModels(dat, u, bc, out var s), "rect Q8 import");
    Assert(s.NNodes == 861, $"rect import nodes {s.NNodes}");
    Assert(s.NElements == 200, $"rect import elems {s.NElements}");
    Assert(m.IsDiscretized, "rect Q8 canvas");
    double minX = u.NodeRows.Min(r => double.Parse(r.X, CultureInfo.InvariantCulture));
    double maxX = u.NodeRows.Max(r => double.Parse(r.X, CultureInfo.InvariantCulture));
    double minY = u.NodeRows.Min(r => double.Parse(r.Y, CultureInfo.InvariantCulture));
    double maxY = u.NodeRows.Max(r => double.Parse(r.Y, CultureInfo.InvariantCulture));
    Assert(minX >= -1e-6 && maxX <= 60 + 1e-6, $"rect X range [{minX},{maxX}]");
    Assert(minY >= -1e-6 && maxY <= 15 + 1e-6, $"rect Y range [{minY},{maxY}]");
    Assert(!DatMeshBoundaryImporter.IsSmithEmbankmentQ8Deck(null, dat), "rect must not be Smith embankment deck");
    Console.WriteLine($"OK rectangular Q8: {s.NNodes} nodes, {s.NElements} elems, bounds X=[{minX:G},{maxX:G}] Y=[{minY:G},{maxY:G}]");
}

// Project-style mesh load: nodes without NodeId must not break SRF export prep.
{
    var mesh = new MeshViewModel();
    for (int i = 0; i < 4; i++)
        mesh.DiscretizationNodes.Add(new GeotechSlopeApp.Models.MeshDiscretizationNodeDisplay
        {
            Location = new System.Windows.Point(i, i)
        });
    mesh.MeshInformationRows.Add(new GeotechSlopeApp.Models.MeshInformationRow
    {
        ElementId = 1, Node1 = 1, Node2 = 2, Node3 = 3,
        N1X = 0, N1Y = 0, N2X = 1, N2Y = 0, N3X = 0, N3Y = 1,
        Kind = "T3", Status = "Active"
    });
    mesh.MeshElements.Add(new GeotechSlopeApp.Models.MeshElementDisplay
    {
        DisplayId = 1, IsActive = true,
        Nodes = new System.Windows.Media.PointCollection
        {
            new(0, 0), new(1, 0), new(0, 1)
        }
    });
    mesh.IsDiscretized = true;
    mesh.EnsureDiscretizationNodeIds();
    Assert(mesh.DiscretizationNodes[0].NodeId == 1, "EnsureDiscretizationNodeIds assigns 1-based ids");
    var mat = new MaterialPropertiesViewModel();
    var bc = new BoundaryConditionsViewModel { MeshVM = mesh };
    var fe = new FEAnalysisViewModel(new ResultsViewModel());
    var dat = P610T3DatContentBuilder.Build(mesh, mat, bc, null, fe);
    Assert(dat.Contains("solver:p610_t3", StringComparison.OrdinalIgnoreCase), "p610 export from zero-id nodes");
    Console.WriteLine("OK zero NodeId mesh export");
}

// Ex4 groundwater: SyncGroundwaterToScript must not corrupt SRF tail (nsrf + F trials).
{
    var ex4Path = Path.Combine(repoRoot,
        "Verification/slope/Ex2_twolayerslope_groundwater/ex4_homogeneous_slope_groundwater.dat");
    var ex4 = File.ReadAllText(ex4Path);
    Assert(GroundwaterDatImporter.TryImportFromDat(ex4, out var gwModel), "ex4 GW import");
    Assert(GroundwaterDatWriter.HasGroundwaterCommentSurf(ex4), "ex4 GW comment surf");
    Assert(!GroundwaterDatWriter.ShouldAppendNumericTail(ex4, gwModel), "ex4 must not append numeric GW tail");
    var merged = GroundwaterDatWriter.MergeGroundwaterSection(ex4, gwModel);
    Assert(merged.TrimEnd().EndsWith("0.5  1  1.25  1.2656", StringComparison.Ordinal),
        "ex4 SRF trial line preserved after GW merge");
    Assert(!GroundwaterDatWriter.HasGroundwaterNumericTail(merged), "ex4 must not gain spurious GW numeric tail");
    var wronglyAppended = GroundwaterDatWriter.AppendNumericTail(ex4, gwModel);
    Assert(string.Equals(wronglyAppended, ex4, StringComparison.Ordinal) ||
           wronglyAppended.TrimEnd().EndsWith("0.5  1  1.25  1.2656", StringComparison.Ordinal),
        "AppendNumericTail no-op on comment-only GW deck");
    Console.WriteLine("OK ex4 GW merge preserves SRF controls");
}

// Ex4 groundwater: phreatic Y must map to mesh-canvas display coords (mesh_coord_y_offset).
{
    var ex4Path = Path.Combine(repoRoot,
        "Verification/slope/Ex2_twolayerslope_groundwater/ex4_homogeneous_slope_groundwater.dat");
    var ex4 = File.ReadAllText(ex4Path);
    Assert(GroundwaterDatImporter.TryImportFromDat(ex4, out var solverModel), "ex4 GW import");
    Assert(solverModel.SurfPoints.Count > 0, "ex4 surf points");
    Assert(solverModel.SurfPoints.All(p => p.Y < 0), "ex4 solver-native phreatic Y negative");

    var displayPts = GroundwaterDatImporter.ToDisplaySurfPoints(solverModel.SurfPoints, ex4);
    Assert(displayPts.All(p => p.Y > 0), "phreatic Y positive on display canvas");
    Assert(displayPts.All(p => p.Y > 0 && p.Y < 6.2), "phreatic inside mesh elevation band");

    var displayModel = new GeotechSlopeApp.Models.GroundwaterModel
    {
        IsEnabled = solverModel.IsEnabled,
        GammaW = solverModel.GammaW,
        DrawMode = solverModel.DrawMode,
        SurfPoints = displayPts.ToList(),
    };
    var roundTrip = GroundwaterDatImporter.CloneForSolverExport(displayModel, ex4);
    Assert(roundTrip.SurfPoints.Count == solverModel.SurfPoints.Count, "GW round-trip count");
    for (int i = 0; i < solverModel.SurfPoints.Count; i++)
    {
        var a = solverModel.SurfPoints[i];
        var b = roundTrip.SurfPoints[i];
        Assert(Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9, $"GW round-trip point {i}");
    }
    Console.WriteLine("OK ex4 GW display/solver coord round-trip");
}

// Ex4 groundwater JSON: pore pressure must parse and map to element stresses.
{
    var jsonPath = Path.Combine(repoRoot,
        "Verification/slope/Ex2_twolayerslope_groundwater/ex4_homogeneous_slope_groundwater.json");
    Assert(File.Exists(jsonPath), "ex4 groundwater JSON exists");
    var run = P610T3JsonParser.ParseFile(jsonPath);
    Assert(run.Stages.Count >= 1, "ex4 stages");
    var stage0 = run.Stages[0];
    Assert(stage0.ElementStresses.Count == run.Elements.Count, "stress/element count");
    double maxU = stage0.ElementStresses.Max(s => s.PorePressure);
    Assert(maxU > 1.0, $"ex4 stage0 max pore pressure {maxU:G} kPa");
    int nz = stage0.ElementStresses.Count(s => s.PorePressure > 0.01);
    Assert(nz > 100, $"ex4 stage0 nonzero u count {nz}");

    var results = new ResultsViewModel();
    results.LoadP610T3Result(run, datContent: File.ReadAllText(
        Path.Combine(repoRoot, "Verification/slope/Ex2_twolayerslope_groundwater/ex4_homogeneous_slope_groundwater.dat")));
    results.SelectedOutputType = ResultsViewModel.OutputPorePressure;
    results.SelectedStageInfo = results.AvailableStages[0];

    double maxContour = 0;
    foreach (var el in results.FEElements)
        maxContour = Math.Max(maxContour, results.GetElementScalarValue(el, ResultsViewModel.OutputPorePressure));
    Assert(maxContour > 1.0, $"ResultsViewModel pore pressure max {maxContour:G}");
    Console.WriteLine($"OK ex4 groundwater pore pressure: JSON max u={maxU:G3} kPa, contour max={maxContour:G3} kPa");
}

static GeometryDefinitionViewModel MakeAxisQuadGeometry(
    double xmin, double xmax, double ymin, double ymax, params double[] layerCuts)
{
    var geom = new GeometryDefinitionViewModel();
    geom.GeometryVertexRows.Clear();
    foreach (var (x, y) in new[] { (xmin, ymin), (xmax, ymin), (xmax, ymax), (xmin, ymax) })
    {
        var v = new GeometryDefinitionViewModel.GeometryVertexDisplay { AxisX = x, AxisY = y };
        v.StablePointId = geom.AllocateStablePointId();
        geom.GeometryVertexRows.Add(v);
    }

    geom.RebuildGeometryAfterProjectLoad();
    if (layerCuts.Length > 0)
        geom.ApplyInferredMaterialLayerCuts(layerCuts);
    return geom;
}

static MaterialPropertiesViewModel MakeTwoMaterialLibrary(GeometryDefinitionViewModel geom)
{
    var matVm = new MaterialPropertiesViewModel { GeometryDefinition = geom };
    matVm.Materials.Add(new MaterialModel { Id = 1, Name = "Mat1", FrictionAngle = 25, Cohesion = 1 });
    matVm.Materials.Add(new MaterialModel { Id = 2, Name = "Mat2", FrictionAngle = 15, Cohesion = 0.5 });
    return matVm;
}

// Ex2 reference deck: imported mesh + synced geometry produce a valid layer plan.
{
    var ex2Path = Path.Combine(repoRoot, "Verification/slope/Ex2_twolayerslope/ex2_twolayer_slope.dat");
    var ex2 = File.ReadAllText(ex2Path);
    var mesh = new MeshViewModel();
    var udm = new UserDefinedMeshViewModel(mesh);
    var bcVm = new BoundaryConditionsViewModel { MeshVM = mesh };
    Assert(DatMeshBoundaryImporter.TryImportToViewModels(ex2, udm, bcVm, out _), "Ex2 validator import");
    var geom = new GeometryDefinitionViewModel { MeshSource = mesh };
    geom.ImportBoundaryFromMesh();
    var matVm = new MaterialPropertiesViewModel { GeometryDefinition = geom };
    Assert(DatMaterialImporter.TryImportToProjectMaterials(ex2, matVm), "Ex2 validator materials");
    Assert(DatEtypeLayeringSync.TrySyncLayeringFromMeshEtype(ex2, mesh, matVm, geom, out _), "Ex2 validator sync");
    Assert(Q8MeshMaterialLayerPlanner.TryCreatePlan(geom, matVm, out var ex2Plan, out var planErr), planErr);
    Assert(ex2Plan.HasLayers, "Ex2 plan has layers");
    Assert(ex2Plan.LayerCount >= 2, $"Ex2 layer count {ex2Plan.LayerCount}");
    Console.WriteLine($"OK Ex2 layer plan: {ex2Plan.LayerCount} bands from synced geometry");
}

// Layer-aligned embankment Q8: horizontal cut in foundation zone aligned to mesh rows.
{
    var geom = MakeAxisQuadGeometry(0, 60, 0, 15, 2.0);
    geom.SetLayerMaterialAssignmentMaterialId(1, 1);
    geom.SetLayerMaterialAssignmentMaterialId(2, 2);
    geom.SetLayerMaterialAssignmentMaterialId(3, 1);
    var matVm = MakeTwoMaterialLibrary(geom);

    var opts = P64EmbankmentDatWriter.BuildWriteOptionsFromViewModels(
        20, 10, 10, 5, 20, 20, 20, 10, 5, matVm, null, null, meshType: "Q8", geometry: geom);
    Assert(
        P64EmbankmentDatWriter.TryBuildLayerAwareMesh(opts, geom, matVm, 1.0, out var embBuilt, out var err),
        err);
    Assert(embBuilt!.Options.NpTypes == 2, $"embankment layered np_types {embBuilt.Options.NpTypes}");
    Assert(embBuilt.ValidationMessage.Contains("passed", StringComparison.OrdinalIgnoreCase), embBuilt.ValidationMessage);
    Assert(embBuilt.Mesh.Elements.Select(e => e.etype).Distinct().Count() == 2, "embankment expects two etypes");
    Console.WriteLine($"OK layered embankment Q8: {embBuilt.ValidationMessage}, ny2={embBuilt.Options.Ny2}");
}

// Layer-aligned rectangular Q8: variable Y rows at cut elevations + multi-material etype block.
{
    var geom = MakeAxisQuadGeometry(0, 60, 0, 15, 5.0);
    geom.SetLayerMaterialAssignmentMaterialId(1, 1);
    geom.SetLayerMaterialAssignmentMaterialId(2, 2);
    var matVm = MakeTwoMaterialLibrary(geom);

    var baseOpts = new StructuredRectangularQ8DatWriter.WriteOptions
    {
        XMin = 0, XMax = 60, YMin = 0, YMax = 15,
        ElementsX = 20, ElementsY = 10,
    };
    Assert(
        StructuredRectangularQ8DatWriter.TryBuildLayerAwareMesh(baseOpts, geom, matVm, 1.0, out var rectBuilt, out var err),
        err);
    Assert(rectBuilt!.Options.NpTypes == 2, $"rect layered np_types {rectBuilt.Options.NpTypes}");
    Assert(rectBuilt.Options.YNodeLevels is { Count: >= 3 }, "rect layered Y node levels");
    Assert(rectBuilt.ValidationMessage.Contains("passed", StringComparison.OrdinalIgnoreCase), rectBuilt.ValidationMessage);
    var dat = StructuredRectangularQ8DatWriter.WriteExplicitDeck(rectBuilt.Mesh, rectBuilt.Options);
    Assert(DatMeshBoundaryImporter.TryGetNpTypes(dat, out int np) && np == 2, "rect layered deck np_types");
    Console.WriteLine($"OK layered rectangular Q8: {rectBuilt.ValidationMessage}, elems={rectBuilt.Mesh.Nels}");
}

// Negative: misaligned subdivisions must fail layer validator.
{
    var geom = MakeAxisQuadGeometry(0, 60, 0, 15, 2.5);
    geom.SetLayerMaterialAssignmentMaterialId(1, 1);
    geom.SetLayerMaterialAssignmentMaterialId(2, 2);
    geom.SetLayerMaterialAssignmentMaterialId(3, 1);
    var matVm = MakeTwoMaterialLibrary(geom);
    Assert(Q8MeshMaterialLayerPlanner.TryCreatePlan(geom, matVm, out var plan, out _), "misaligned plan");

    var misaligned = P64EmbankmentMeshBuilder.BuildQ8Mesh(20, 10, 10, 5, 20, 20, 20, 10, 5);
    double shift = Q8MeshMaterialLayerPlanner.ComputeCoordShift(misaligned);
    var typed = Q8MeshMaterialLayerPlanner.ApplyElementTypes(misaligned, plan, shift);
    var bad = Q8MeshLayerValidator.Validate(typed, plan, shift);
    Assert(!bad.IsValid, "misaligned mesh should cross a layer interface");
    Assert(bad.CrossingElementIds.Count > 0, "misaligned mesh should list crossing elements");
    Console.WriteLine($"OK misaligned layer guard: {bad.CrossingElementIds.Count} crossing element(s)");
}

// Collinear merge: rectangular boundary walk with midside nodes collapses to four corners.
{
    var coords = new (double x, double y)[]
    {
        (0, 0),
        (5, 0),
        (10, 0),
        (10, 2.5),
        (10, 5),
        (5, 5),
        (0, 5),
        (0, 2.5),
    };
    var boundary = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8 };
    Assert(
        MeshBoundaryGeometryExtractor.TryMergeCollinearBoundaryVertices(boundary, coords, out var merged),
        "collinear merge");
    Assert(merged.Count == 4, $"collinear merge count {merged.Count} != 4");
    Assert(merged.SequenceEqual(new[] { 1, 3, 5, 7 }),
        $"collinear merge order [{string.Join(",", merged)}]");
    Console.WriteLine("OK collinear boundary merge: 8-node walk -> 4 corners");
}

Console.WriteLine("All mesh/BC import smoke tests passed.");
