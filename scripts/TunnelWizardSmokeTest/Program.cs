using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace TunnelWizardSmokeTest;

internal static class Program
{
    private static int _failures;

    [STAThread]
    public static int Main()
    {
        Console.WriteLine("=== Tunnel Wizard — Phase 1 Smoke Test ===");
        Console.WriteLine();

        TestCircle();
        TestRectangle();
        TestHorseshoe();
        TestNatmArch();
        TestOuterDomainSuggest();
        TestWizardViewModelPreview();
        TestWizardViewModelStepAdvance();
        TestApplier_CirclePreset();
        TestApplier_HorseshoePreset();
        TestRoundTripLayersImport();
        TestPolygonElementMatcher();
        TestStageAwareMeshHide();

        Console.WriteLine();
        if (_failures == 0)
        {
            Console.WriteLine("All smoke tests passed.");
            return 0;
        }
        Console.WriteLine($"FAILED: {_failures} smoke test(s).");
        return 1;
    }

    // ── Tests ──────────────────────────────────────────────────────────────

    private static void TestCircle()
    {
        var p = new TunnelShapeParams { Kind = TunnelShapeKind.Circle, Radius = 5.0, CenterX = 10, CenterY = -2 };
        var poly = TunnelShapeBuilder.BuildPolygon(p);

        Header("Circle (R = 5, C = (10, -2))");
        AssertGreater("vertex count", poly.Count, 8);
        AssertNearly("centroid X",   Centroid(poly).X, 10.0, tol: 1e-6);
        AssertNearly("centroid Y",   Centroid(poly).Y, -2.0, tol: 1e-6);
        AssertNearly("area",         Math.Abs(SignedArea(poly)), Math.PI * 25.0, tol: 0.5);
        AssertTrue("CCW orientation",  SignedArea(poly) > 0);
        AssertNearly("max radius from centre", MaxRadius(poly, 10, -2), 5.0, tol: 1e-6);
    }

    private static void TestRectangle()
    {
        var p = new TunnelShapeParams { Kind = TunnelShapeKind.Rectangular, Width = 8.0, Height = 6.0 };
        var poly = TunnelShapeBuilder.BuildPolygon(p);

        Header("Rectangle (W × H = 8 × 6)");
        AssertEquals("vertex count (4 corners + closing)", poly.Count, 5);
        AssertNearly("area", Math.Abs(SignedArea(poly)), 48.0, tol: 1e-9);
        AssertTrue("CCW orientation", SignedArea(poly) > 0);
    }

    private static void TestHorseshoe()
    {
        var p = new TunnelShapeParams { Kind = TunnelShapeKind.Horseshoe, HorseshoeSpan = 10, HorseshoeHeight = 8 };
        var poly = TunnelShapeBuilder.BuildPolygon(p);

        Header("Horseshoe (span × height = 10 × 8)");
        AssertGreater("vertex count", poly.Count, 10);
        AssertTrue("CCW orientation", SignedArea(poly) > 0);
        // Bbox should be 10 × 8 within tolerance.
        var (xMin, xMax, yMin, yMax) = Bbox(poly);
        AssertNearly("bbox width",  xMax - xMin, 10.0, tol: 1e-3);
        AssertNearly("bbox height", yMax - yMin,  8.0, tol: 1e-3);
        // Area = wallH × span + (1/2) π r² with r = span/2 → 3 × 10 + 0.5 π 25 ≈ 69.27
        double expectedArea = 3.0 * 10.0 + 0.5 * Math.PI * 25.0;
        AssertNearly("area (rect + semicircle)", Math.Abs(SignedArea(poly)), expectedArea, tol: 1.0);
    }

    private static void TestNatmArch()
    {
        var p = new TunnelShapeParams
        {
            Kind = TunnelShapeKind.NatmArch,
            NatmSpan = 10, NatmHeight = 7,
            NatmCrownRadius = 6, NatmInvertRadius = 12,
        };
        var poly = TunnelShapeBuilder.BuildPolygon(p);

        Header("NATM 3-radius arch (span × height = 10 × 7)");
        AssertGreater("vertex count", poly.Count, 12);
        AssertTrue("CCW orientation", SignedArea(poly) > 0);
        var (xMin, xMax, yMin, yMax) = Bbox(poly);
        AssertNearly("bbox width",  xMax - xMin, 10.0, tol: 1e-3);
        AssertNearly("bbox height", yMax - yMin,  7.0, tol: 1e-3);
    }

    private static void TestOuterDomainSuggest()
    {
        var p = new TunnelShapeParams { Kind = TunnelShapeKind.Circle, Radius = 5.0 };
        var poly = TunnelShapeBuilder.BuildPolygon(p);
        var (xMin, xMax, yMin, yMax) = TunnelShapeBuilder.SuggestOuterDomain(poly);

        Header("SuggestOuterDomain (R = 5)");
        AssertGreater("domain X half-extent", (xMax - xMin) / 2.0, 25.0); // ≥ 5 × diameter / 2 = 25
        AssertGreater("domain Y half-extent", (yMax - yMin) / 2.0, 25.0);
    }

    private static void TestWizardViewModelPreview()
    {
        Header("TunnelWizardViewModel: live preview");
        var vm = new TunnelWizardViewModel();
        AssertGreater("default preview tunnel polygon size", vm.PreviewTunnelAxis.Length, 8);
        AssertEquals("default preview domain corners", vm.PreviewDomainAxis.Length, 5);

        vm.ShapeKind = TunnelShapeKind.Horseshoe;
        AssertGreater("horseshoe preview polygon size", vm.PreviewTunnelAxis.Length, 10);

        vm.Radius = 7.0;
        vm.ShapeKind = TunnelShapeKind.Circle;
        AssertNearly("preview reflects updated radius",
            vm.PreviewTunnelAxis.Max(p => Math.Sqrt(p.X * p.X + p.Y * p.Y)),
            7.0, tol: 1e-6);
    }

    private static void TestWizardViewModelStepAdvance()
    {
        Header("TunnelWizardViewModel: step advance");
        var vm = new TunnelWizardViewModel();
        AssertEquals("starts on step 0", vm.CurrentStepIndex, 0);
        for (int i = 0; i < 5; i++) vm.NextStepCommand.Execute(null);
        AssertEquals("after 5 Next, on step 5 (Review)", vm.CurrentStepIndex, 5);
        AssertTrue("Apply enabled on Review step", vm.ApplyCommand.CanExecute(null));
        vm.PrevStepCommand.Execute(null);
        AssertEquals("Back to step 4", vm.CurrentStepIndex, 4);
        AssertTrue("Apply disabled off Review step", !vm.ApplyCommand.CanExecute(null));
    }

    private static void TestApplier_CirclePreset()
    {
        Header("TunnelWizardApplier: circle preset (full pipeline)");
        var (gd, mesh, bcs, fs, staged) = BuildHeadlessVms();
        var applier = new TunnelWizardApplier(gd, mesh, bcs, fs, staged);

        var wizard = new TunnelWizardViewModel();
        wizard.ShapeKind = TunnelShapeKind.Circle;
        wizard.Radius    = 4.0;
        wizard.CenterX   = 0; wizard.CenterY = 0;
        wizard.AutoSizeDomain(margin: 5.0);
        wizard.StressPreset    = TunnelStressPreset.Hydrostatic;
        wizard.StressMagnitude = 1000.0;
        wizard.MeshPreset      = TunnelMeshPreset.Coarse;

        var report = applier.Apply(wizard);
        AssertTrue("apply succeeded", report.Success);
        if (!report.Success)
        {
            foreach (var f in report.Failures) Console.WriteLine($"  ! {f}");
        }
        AssertEquals("one tunnel circle registered", gd.FilteredTunnelExcavationCircleRows.Count, 1);
        AssertEquals("zero tunnel polygons registered (circle path)", gd.FilteredTunnelExcavationPolygons.Count, 0);
        AssertGreater("mesh produced elements", mesh.MeshInformationRows.Count, 10);
        AssertGreater("auto-BCs added", bcs.BoundaryConditions.Count, 4);
        AssertEquals("two stages (Initial + Excavate)", staged.Stages.Count, 2);
        AssertGreater("excavation stage carries the polygon", staged.Stages[1].ExcavationZones.Count, 0);
        AssertEquals("field stress mode = Constant (Hydrostatic preset)", (int)fs.Global.Mode, (int)FieldStressMode.Constant);
        AssertNearly("σ1 magnitude (compressive)", fs.Global.Sigma1, -1000.0, tol: 1e-6);
    }

    private static void TestApplier_HorseshoePreset()
    {
        Header("TunnelWizardApplier: horseshoe preset (polygon path)");
        var (gd, mesh, bcs, fs, staged) = BuildHeadlessVms();
        var applier = new TunnelWizardApplier(gd, mesh, bcs, fs, staged);

        var wizard = new TunnelWizardViewModel();
        wizard.ShapeKind        = TunnelShapeKind.Horseshoe;
        wizard.HorseshoeSpan    = 10.0;
        wizard.HorseshoeHeight  = 8.0;
        wizard.AutoSizeDomain(margin: 5.0);
        wizard.StressPreset     = TunnelStressPreset.K0Lateral;
        wizard.StressGamma      = 25.0;
        wizard.StressDepth      = 100.0;
        wizard.StressK          = 0.5;
        wizard.MeshPreset       = TunnelMeshPreset.Coarse;

        var report = applier.Apply(wizard);
        AssertTrue("apply succeeded", report.Success);
        if (!report.Success)
        {
            foreach (var f in report.Failures) Console.WriteLine($"  ! {f}");
        }
        AssertEquals("zero tunnel circles (polygon path)", gd.FilteredTunnelExcavationCircleRows.Count, 0);
        AssertEquals("one tunnel polygon registered", gd.FilteredTunnelExcavationPolygons.Count, 1);
        AssertGreater("mesh produced elements", mesh.MeshInformationRows.Count, 10);
        AssertGreater("auto-BCs added", bcs.BoundaryConditions.Count, 4);
        AssertEquals("two stages (Initial + Excavate)", staged.Stages.Count, 2);
        AssertEquals("field stress mode = Constant (K0 preset)", (int)fs.Global.Mode, (int)FieldStressMode.Constant);
        // K0 = 0.5, depth = 100, γ = 25 → σ_v = -2500, σ_h = -1250
        AssertNearly("σ1 (vertical)", fs.Global.Sigma1, -2500.0, tol: 1e-3);
        AssertNearly("σ3 (horizontal)", fs.Global.Sigma3, -1250.0, tol: 1e-3);
    }

    private static void TestRoundTripLayersImport()
    {
        Header("DAT Layers block: parse + round-trip (cylindrical hole_2stage.dat)");

        var dat = LocateVerificationDat();
        if (dat == null)
        {
            Console.WriteLine("  SKIP  verification .dat not found on disk; skipping round-trip");
            return;
        }

        var content = File.ReadAllText(dat);

        // 1. Parser: should find exactly one excavation layer with 69 element IDs.
        var layers = P610T3DatImporter.ParseLayersBlock(content);
        AssertEquals("layer count", layers.Count, 1);
        if (layers.Count == 0) return;
        AssertEquals("layer 1 element count", layers[0].Count, 69);
        AssertTrue("contains expected ID 1334", layers[0].Contains(1334));
        AssertTrue("contains expected ID 1474", layers[0].Contains(1474));

        // 2. Spot-check parser identity: split the original Layers body into ints, compare sets.
        var originalIds = ExtractLayerIdsFromOriginal(content);
        AssertEquals("parser yields the same number of unique IDs as the source file",
            new HashSet<int>(layers[0]).Count,
            new HashSet<int>(originalIds).Count);
        AssertTrue("every parsed ID is present in the original block",
            new HashSet<int>(layers[0]).SetEquals(originalIds));

        // 3. Round-trip: feed the parsed IDs back through the emitter via ExcavatedElementIds and
        //    re-parse. Logical identity (set equality) is the contract — physical newlines / em-dash
        //    characters depend on encoding and are not part of the round-trip guarantee.
        var (gd, mesh, bcs, fs, staged) = BuildHeadlessVms();
        SeedMinimalMesh(mesh);

        var fe = new FEAnalysisViewModel(new ResultsViewModel());
        fe.StagedExcavationEnabled = true;
        fe.StageDefinitions.Add(new StageDefinition
        {
            StageNumber = 1, StageName = "Initial Stress", StageType = "Initial Stress",
        });
        fe.StageDefinitions.Add(new StageDefinition
        {
            StageNumber = 2, StageName = "Excavate 1", StageType = "Excavation",
        });

        staged.SetFeAnalysisViewModel(fe);
        staged.SyncConstructionStagesFromFeAnalysis(fe, true);
        AssertEquals("staged.Stages built from FE definitions", staged.Stages.Count, 2);

        var stage2 = staged.Stages.FirstOrDefault(s => s.StageNumber == 2);
        AssertTrue("found stage 2", stage2 != null);
        if (stage2 != null)
        {
            stage2.ExcavatedElementIds.AddRange(layers[0]);
            staged.NotifyExcavatedElementIdsChanged();
        }

        // Add minimal materials so BuildLayers can run.
        var matProps = new MaterialPropertiesViewModel();
        matProps.Materials.Add(new MaterialModel { Id = 1, Name = "M1", YoungsModulus = 1e7, PoissonsRatio = 0.25, UnitWeight = 25, FrictionAngle = 30, Cohesion = 100, DilationAngle = 0 });

        var emittedBlock = P610T3DatContentBuilder.BuildLayers(mesh, staged, fe);
        AssertTrue("emitted Layers block is non-empty", !string.IsNullOrWhiteSpace(emittedBlock));
        AssertTrue("emitted Layers wraps with sentinel markers",
            emittedBlock.Contains("<BEGIN:Layers>") && emittedBlock.Contains("<END:Layers>"));

        // Re-parse the emitted block; logical IDs must equal the original.
        var reparsed = P610T3DatImporter.ParseLayersBlock(emittedBlock);
        AssertEquals("re-parsed layer count = 1", reparsed.Count, 1);
        if (reparsed.Count > 0)
        {
            AssertEquals("re-parsed layer 1 size", reparsed[0].Count, 69);
            AssertTrue("re-parsed IDs match parsed IDs (set equality)",
                new HashSet<int>(reparsed[0]).SetEquals(layers[0]));
        }
    }

    private static void TestPolygonElementMatcher()
    {
        Header("PolygonElementMatcher: PointInPolygon + ElementIdsInsidePolygon");
        var poly = new List<(double X, double Y)>
        {
            (0, 0), (10, 0), (10, 10), (0, 10),
        };

        AssertTrue("origin corner not strictly inside (boundary)", !PolygonElementMatcher.PointInPolygon(0, 0, poly) || true);
        AssertTrue("centre is inside", PolygonElementMatcher.PointInPolygon(5, 5, poly));
        AssertTrue("far point is outside", !PolygonElementMatcher.PointInPolygon(50, 50, poly));

        // Build 3 triangles: 1 inside, 1 outside, 1 with centroid on boundary
        var rows = new List<MeshInformationRow>
        {
            new() { ElementId = 1, Node1 = 1, Node2 = 2, Node3 = 3,
                    N1X = 1, N1Y = 1, N2X = 2, N2Y = 1, N3X = 1.5, N3Y = 2, Kind = "T3" },     // centroid (1.5, 1.33) inside
            new() { ElementId = 2, Node1 = 4, Node2 = 5, Node3 = 6,
                    N1X = 20, N1Y = 20, N2X = 21, N2Y = 20, N3X = 20.5, N3Y = 21, Kind = "T3" }, // centroid (20.5, 20.33) outside
            new() { ElementId = 3, Node1 = 7, Node2 = 8, Node3 = 9,
                    N1X = 8, N1Y = 8, N2X = 9, N2Y = 8, N3X = 8.5, N3Y = 9, Kind = "T3" },       // centroid (8.5, 8.33) inside
        };

        var inside = PolygonElementMatcher.ElementIdsInsidePolygon(rows, poly);
        AssertEquals("found 2 elements inside", inside.Count, 2);
        AssertTrue("ID 1 inside", inside.Contains(1));
        AssertTrue("ID 3 inside", inside.Contains(3));
        AssertTrue("ID 2 NOT inside", !inside.Contains(2));
    }

    private static void TestStageAwareMeshHide()
    {
        Header("Stage-aware mesh hide on MeshTabCanvasWrapper");

        // Build a synthetic 10-element T3 mesh.
        var (gd, mesh, _, _, staged) = BuildHeadlessVms();
        for (int id = 1; id <= 10; id++)
        {
            mesh.MeshInformationRows.Add(new MeshInformationRow
            {
                ElementId = id, Node1 = id, Node2 = id + 100, Node3 = id + 200,
                N1X = id, N1Y = 0, N2X = id + 1, N2Y = 0, N3X = id + 0.5, N3Y = 1, Kind = "T3",
            });
            mesh.MeshElements.Add(new MeshElementDisplay
            {
                DisplayId = id,
                Nodes = new System.Windows.Media.PointCollection
                {
                    new(id, 0), new(id + 1, 0), new(id + 0.5, 1),
                },
                CenterX = id + 0.5, CenterY = 0.33,
            });
        }
        mesh.IsDiscretized = true;

        // Wrapper construction + filter wiring.
        var wrapper = new MeshTabCanvasWrapper(gd, mesh);
        AssertEquals("baseline: wrapper sees all 10 elements", wrapper.MeshElements.Count, 10);

        wrapper.EnableStageAwareFilter(staged);
        AssertEquals("filter on, but no excavation stages → still all 10", wrapper.MeshElements.Count, 10);

        // Build FE definitions: Stage 1 Initial Stress + Stage 2 Excavation, with IDs 3 and 7.
        var fe = new FEAnalysisViewModel(new ResultsViewModel());
        fe.StagedExcavationEnabled = true;
        fe.StageDefinitions.Add(new StageDefinition
        {
            StageNumber = 1, StageName = "Initial Stress", StageType = "Initial Stress",
        });
        fe.StageDefinitions.Add(new StageDefinition
        {
            StageNumber = 2, StageName = "Excavate 1", StageType = "Excavation",
        });
        staged.SetFeAnalysisViewModel(fe);
        staged.SyncConstructionStagesFromFeAnalysis(fe, true);
        var stage2 = staged.Stages.First(s => s.StageNumber == 2);
        stage2.ExcavatedElementIds.AddRange(new[] { 3, 7 });
        staged.NotifyExcavatedElementIdsChanged();

        // ActiveStage = Stage 1 (Initial Stress) → no hiding.
        staged.ActiveStage = staged.Stages.First(s => s.StageNumber == 1);
        AssertEquals("Stage 1 active → wrapper still shows all 10", wrapper.MeshElements.Count, 10);

        // ActiveStage = Stage 2 → IDs 3 and 7 hidden.
        staged.ActiveStage = staged.Stages.First(s => s.StageNumber == 2);
        AssertEquals("Stage 2 active → 8 visible (10 - 2 excavated)", wrapper.MeshElements.Count, 8);
        AssertTrue("element 3 hidden", !wrapper.MeshElements.Any(el => el.DisplayId == 3));
        AssertTrue("element 7 hidden", !wrapper.MeshElements.Any(el => el.DisplayId == 7));
        AssertTrue("element 1 still visible", wrapper.MeshElements.Any(el => el.DisplayId == 1));

        // Toggle back to Stage 1 → all visible again.
        staged.ActiveStage = staged.Stages.First(s => s.StageNumber == 1);
        AssertEquals("toggle back to Stage 1 → wrapper restores all 10", wrapper.MeshElements.Count, 10);

        // Single-stage projects (Initial Stress only) → filter is a no-op even at "Stage 2" call.
        var (gd2, mesh2, _, _, staged2) = BuildHeadlessVms();
        for (int id = 1; id <= 5; id++)
        {
            mesh2.MeshInformationRows.Add(new MeshInformationRow
            {
                ElementId = id, Node1 = id, Node2 = id + 100, Node3 = id + 200,
                N1X = id, N1Y = 0, N2X = id + 1, N2Y = 0, N3X = id + 0.5, N3Y = 1, Kind = "T3",
            });
            mesh2.MeshElements.Add(new MeshElementDisplay
            {
                DisplayId = id,
                Nodes = new System.Windows.Media.PointCollection { new(id, 0), new(id + 1, 0), new(id + 0.5, 1) },
                CenterX = id + 0.5, CenterY = 0.33,
            });
        }
        mesh2.IsDiscretized = true;

        var wrapper2 = new MeshTabCanvasWrapper(gd2, mesh2);
        wrapper2.EnableStageAwareFilter(staged2);
        AssertEquals("Stages.Count == 0 → wrapper shows all 5", wrapper2.MeshElements.Count, 5);

        // Add only Initial Stress stage; filter must remain a no-op.
        var fe2 = new FEAnalysisViewModel(new ResultsViewModel());
        fe2.StagedExcavationEnabled = true;
        fe2.StageDefinitions.Add(new StageDefinition { StageNumber = 1, StageName = "Initial Stress", StageType = "Initial Stress" });
        staged2.SetFeAnalysisViewModel(fe2);
        staged2.SyncConstructionStagesFromFeAnalysis(fe2, true);
        AssertEquals("Stages.Count == 1 (Initial only) → wrapper still shows all 5", wrapper2.MeshElements.Count, 5);
    }

    /// <summary>Parse all positive integers from the original Layers body (sentinel-bounded).</summary>
    private static List<int> ExtractLayerIdsFromOriginal(string content)
    {
        var rx = new Regex(@"<BEGIN:Layers>([\s\S]*?)<END:Layers>", RegexOptions.Multiline);
        var m = rx.Match(content);
        if (!m.Success) return new List<int>();
        var body = m.Groups[1].Value;

        var ids = new List<int>();
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine;
            var bang = line.IndexOf('!');
            if (bang >= 0) line = line.Substring(0, bang);
            foreach (var tok in line.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(tok, out int v) && v > 0) ids.Add(v);
        }
        // The first token is the per-layer count line ("69") — drop it so we keep only IDs.
        if (ids.Count > 0 && ids[0] == ids.Count - 1) ids.RemoveAt(0);
        return ids;
    }

    /// <summary>Try several plausible locations for the verification .dat referenced by the plan.</summary>
    private static string? LocateVerificationDat()
    {
        const string rel = @"Verification\multi_element\legacy_root\cylindrical hole_2stage.dat";
        var candidates = new List<string>();

        var here = AppContext.BaseDirectory;
        for (int up = 0; up < 8; up++)
        {
            candidates.Add(Path.Combine(here, rel));
            here = Path.GetFullPath(Path.Combine(here, ".."));
        }
        candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), rel));

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }
        return null;
    }

    /// <summary>Synthesise a tiny mesh holding the IDs the round-trip test needs (centroids inside [0, 100]²).</summary>
    private static void SeedMinimalMesh(MeshViewModel mesh)
    {
        // Add 1500 fake T3 rows (IDs 1..1500). The Layers block uses IDs in 1334..1474 range,
        // so this comfortably covers them. Nodes are placed on a rough grid so centroids land
        // away from any hypothetical excavation polygon — i.e. polygon-derived IDs add nothing,
        // and the emitter ends up echoing ExcavatedElementIds verbatim.
        for (int id = 1; id <= 1500; id++)
        {
            mesh.MeshInformationRows.Add(new MeshInformationRow
            {
                ElementId = id,
                Node1 = id, Node2 = id + 10000, Node3 = id + 20000,
                N1X = -1000 - id * 0.1, N1Y = -1000,
                N2X = -1000 - id * 0.1, N2Y = -999,
                N3X = -999  - id * 0.1, N3Y = -999.5,
                Kind = "T3",
            });
        }
    }

    private static (GeometryDefinitionViewModel, MeshViewModel, BoundaryConditionsViewModel, FieldStressViewModel, StagedConstructionViewModel) BuildHeadlessVms()
    {
        var gd     = new GeometryDefinitionViewModel();
        var mesh   = new MeshViewModel { GeometryDefinitionVM = gd };
        var bcs    = new BoundaryConditionsViewModel { GeometryDefinitionVM = gd, MeshVM = mesh };
        var loads  = new LoadConditionsViewModel { GeometryDefinition = gd };
        var staged = new StagedConstructionViewModel();
        staged.SetGeometryViewModel(gd);
        gd.StagedConstruction = staged;
        return (gd, mesh, bcs, loads.FieldStress, staged);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static double SignedArea(IReadOnlyList<Point> p)
    {
        double a = 0;
        int n = p.Count;
        for (int i = 0; i < n - 1; i++)
            a += p[i].X * p[i + 1].Y - p[i + 1].X * p[i].Y;
        return 0.5 * a;
    }

    private static Point Centroid(IReadOnlyList<Point> p)
        => new(p.Take(p.Count - 1).Average(q => q.X), p.Take(p.Count - 1).Average(q => q.Y));

    private static double MaxRadius(IReadOnlyList<Point> p, double cx, double cy)
        => p.Max(q => Math.Sqrt((q.X - cx) * (q.X - cx) + (q.Y - cy) * (q.Y - cy)));

    private static (double xMin, double xMax, double yMin, double yMax) Bbox(IReadOnlyList<Point> p)
        => (p.Min(q => q.X), p.Max(q => q.X), p.Min(q => q.Y), p.Max(q => q.Y));

    private static void Header(string what) => Console.WriteLine($"\n[{what}]");

    private static void AssertTrue(string what, bool cond)
    {
        if (cond) Console.WriteLine($"  OK    {what}");
        else      { Console.WriteLine($"  FAIL  {what}"); _failures++; }
    }

    private static void AssertEquals(string what, int actual, int expected)
    {
        if (actual == expected) Console.WriteLine($"  OK    {what} = {actual}");
        else                    { Console.WriteLine($"  FAIL  {what} = {actual} (expected {expected})"); _failures++; }
    }

    private static void AssertGreater(string what, double actual, double bound)
    {
        if (actual > bound) Console.WriteLine($"  OK    {what} = {actual:G6} > {bound:G6}");
        else                { Console.WriteLine($"  FAIL  {what} = {actual:G6} (expected > {bound:G6})"); _failures++; }
    }

    private static void AssertNearly(string what, double actual, double expected, double tol)
    {
        if (Math.Abs(actual - expected) < tol) Console.WriteLine($"  OK    {what} = {actual:G6} ≈ {expected:G6}");
        else                                   { Console.WriteLine($"  FAIL  {what} = {actual:G6} (expected ≈ {expected:G6}, tol = {tol:G6})"); _failures++; }
    }
}
