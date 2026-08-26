using System.IO;
using System.Linq;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.ViewModels;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.ViewModels;

[TestFixture]
public class TransientResultsVisualizationTests
{
    private static TransientRunResult SampleRun() => new()
    {
        Solver = "p84",
        Analysis = "transient_consolidation",
        NodeCount = 4,
        ElementCount = 1,
        ElementNodes = 4,
        Nstep = 10,
        Dtim = 0.01,
        Theta = 0.5,
        Npri = 5,
        Nres = 3,
        MonitorNode = 3,
        Nodes = new[]
        {
            new SeepageNodeResult { Id = 1, X = 0, Y = 0 },
            new SeepageNodeResult { Id = 2, X = 1, Y = 0, PressureHead = 40, TotalHead = 40 },
            new SeepageNodeResult { Id = 3, X = 1, Y = 1, PressureHead = 40, TotalHead = 40 },
            new SeepageNodeResult { Id = 4, X = 0, Y = 1 },
        },
        Elements = new[] { new[] { 1, 2, 3, 4 } },
        MonitorHistory = new[]
        {
            new TransientMonitorSample { Step = 0, Time = 0, Pressure = 100 },
            new TransientMonitorSample { Step = 5, Time = 0.05, Pressure = 80 },
            new TransientMonitorSample { Step = 10, Time = 0.1, Pressure = 40 },
        },
        Steps = new[]
        {
            new TransientTimeStepFrame { Step = 0, Time = 0, Pressures = new[] { 0.0, 100, 100, 0 } },
            new TransientTimeStepFrame { Step = 5, Time = 0.05, Pressures = new[] { 0.0, 80, 80, 0 } },
            new TransientTimeStepFrame { Step = 10, Time = 0.1, Pressures = new[] { 0.0, 40, 40, 0 } },
        },
    };

    [Test]
    public void TransientJsonParser_ReadsMonitorHistoryAndSteps()
    {
        var path = Path.Combine(Path.GetTempPath(), "transient_viz_test.json");
        File.WriteAllText(path, """
            {
              "solver":"p84",
              "analysis":"transient_consolidation",
              "node_count":4,
              "element_count":1,
              "element_nodes":4,
              "ndim":2,
              "nstep":10,
              "dtim":0.01,
              "theta":0.5,
              "npri":5,
              "nres":3,
              "ntime":5,
              "monitor_node":3,
              "monitor_history":[
                {"step":0,"time":0,"pressure":100},
                {"step":5,"time":0.05,"pressure":80},
                {"step":10,"time":0.1,"pressure":40}
              ],
              "steps":[
                {"step":0,"time":0,"pressures":[0,100,100,0]},
                {"step":5,"time":0.05,"pressures":[0,80,80,0]},
                {"step":10,"time":0.1,"pressures":[0,40,40,0]}
              ],
              "nodes":[
                {"id":1,"x":0,"y":0,"total_head":0,"pressure_head":0,"flow_rate":0},
                {"id":2,"x":1,"y":0,"total_head":40,"pressure_head":40,"flow_rate":0},
                {"id":3,"x":1,"y":1,"total_head":40,"pressure_head":40,"flow_rate":0},
                {"id":4,"x":0,"y":1,"total_head":0,"pressure_head":0,"flow_rate":0}
              ],
              "elements":[{"id":1,"nodes":[1,2,3,4]}]
            }
            """);

        Assert.That(TransientJsonParser.LooksLikeTransient(path), Is.True);
        var r = TransientJsonParser.ParseFile(path);
        Assert.That(r.IsTransient, Is.True);
        Assert.That(r.MonitorHistory.Count, Is.EqualTo(3));
        Assert.That(r.Steps.Count, Is.EqualTo(3));
        Assert.That(r.Steps[1].Pressures[1], Is.EqualTo(80).Within(1e-9));
        Assert.That(r.MonitorNode, Is.EqualTo(3));
    }

    [Test]
    public void LoadTransientResult_InstallsTimeFramesAndPressureOutput()
    {
        var vm = new ResultsViewModel();
        vm.LoadTransientResult(SampleRun());

        Assert.That(vm.HasTransientResults, Is.True);
        Assert.That(vm.HasStagedExcavationResults, Is.False);
        Assert.That(vm.ShowTransientForceDisplacementPanel, Is.True);
        Assert.That(vm.ShowElasticForceDisplacementControls, Is.False);
        Assert.That(vm.TransientFrameCount, Is.EqualTo(3));
        Assert.That(vm.SelectedOutputType, Is.EqualTo(ResultsViewModel.OutputTransientExcessPressure));
        Assert.That(vm.TransientPlotNodeId, Is.EqualTo(3));
        Assert.That(vm.SelectedTransientPlotQuantity, Is.EqualTo(ResultsViewModel.TransientQuantityExcessPorePressure));
        Assert.That(vm.TransientTimeHistoryPlotModel, Is.Not.Null);
        Assert.That(vm.AvailableStages.Count, Is.EqualTo(3));

        vm.TransientTimeIndex = 0;
        Assert.That(vm.GetNodeScalarValue(vm.FENodes[1], ResultsViewModel.OutputTransientExcessPressure),
            Is.EqualTo(100).Within(1e-9));

        vm.TransientTimeIndex = 2;
        Assert.That(vm.GetNodeScalarValue(vm.FENodes[1], ResultsViewModel.OutputTransientExcessPressure),
            Is.EqualTo(40).Within(1e-9));
    }

    [Test]
    public void TransientPlotNodeId_RebuildsSeriesFromSteps()
    {
        var vm = new ResultsViewModel();
        vm.LoadTransientResult(SampleRun());

        vm.TransientPlotNodeId = 2;
        Assert.That(vm.TryBuildNodeTimeSeries(
            2, ResultsViewModel.TransientQuantityExcessPorePressure, out var series, out _), Is.True);
        Assert.That(series.Select(p => p.Value).ToArray(), Is.EqualTo(new[] { 100.0, 80.0, 40.0 }).Within(1e-9));
        Assert.That(vm.TransientTimeHistoryPlotModel, Is.Not.Null);
        Assert.That(vm.TransientPlotStatus, Is.Empty);
    }

    [Test]
    public void DegreeOfConsolidation_QuantityIsInPercentRange()
    {
        var vm = new ResultsViewModel();
        vm.LoadTransientResult(SampleRun());

        vm.SelectedTransientPlotQuantity = ResultsViewModel.TransientQuantityDegreeOfConsolidation;
        Assert.That(vm.TryBuildNodeTimeSeries(
            2, ResultsViewModel.TransientQuantityDegreeOfConsolidation, out var series, out _), Is.True);

        Assert.That(series[0].Value, Is.EqualTo(0).Within(1e-9));
        Assert.That(series[^1].Value, Is.EqualTo(60).Within(1e-9)); // 100*(1-40/100)
        Assert.That(series.All(p => p.Value >= 0 && p.Value <= 100), Is.True);
        Assert.That(vm.TransientTimeHistoryPlotModel, Is.Not.Null);
    }

    [Test]
    public void InvalidTransientPlotNodeId_KeepsLastValidPlotAndSetsStatus()
    {
        var vm = new ResultsViewModel();
        vm.LoadTransientResult(SampleRun());
        Assert.That(vm.TransientPlotNodeId, Is.EqualTo(3));

        var before = vm.TransientTimeHistoryPlotModel;
        Assert.That(before, Is.Not.Null);

        vm.TransientPlotNodeId = 99;
        Assert.That(vm.TransientTimeHistoryPlotModel, Is.Not.Null);
        Assert.That(vm.TransientPlotStatus, Does.Contain("out of range"));
        Assert.That(vm.TransientPlotStatus, Does.Contain("99"));
    }

    [Test]
    public void TransientSelected_ShowsForceDisplacementOutputSettingsOnSharedInstance()
    {
        var geometry = new GeometryDefinitionViewModel();
        var materials = new MaterialPropertiesViewModel
        {
            SelectedAnalysisCategory = MaterialPropertiesViewModel.GroundwaterAnalysisCategory,
            SelectedDetailedAnalysisType = MaterialPropertiesViewModel.GroundwaterStaticType,
        };
        var settings = new ProjectSettingsViewModel();
        settings.SetAxisSyncTargets(geometry, materials);

        var results = new ResultsViewModel();
        results.SetProjectSettingsViewModel(settings);

        Assert.That(results.ShowTransientOutputSettings, Is.False);
        Assert.That(results.ShowElasticForceDisplacementControls, Is.True);

        settings.IsTransientP88Checked = true;
        Assert.That(results.ShowTransientOutputSettings, Is.True);
        Assert.That(results.IsTransientAnalysisSelected, Is.True);
        Assert.That(results.ShowElasticForceDisplacementControls, Is.False);
        Assert.That(results.TransientSettings, Is.SameAs(settings.TransientSettings));

        settings.TransientSettings.MonitorNode = 42;
        settings.TransientSettings.ContourStep = 25;
        settings.TransientSettings.ContourIntervals = 8;
        Assert.That(results.TransientSettings!.MonitorNode, Is.EqualTo(42));
        Assert.That(results.TransientSettings.ContourStep, Is.EqualTo(25));
        Assert.That(results.TransientSettings.ContourIntervals, Is.EqualTo(8));

        results.TransientSettings.MonitorNode = 7;
        Assert.That(settings.TransientSettings.MonitorNode, Is.EqualTo(7));
    }
}
