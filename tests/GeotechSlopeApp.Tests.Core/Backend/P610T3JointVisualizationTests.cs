using System.Text.Json;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models.FiniteElement;
using GeotechSlopeApp.ViewModels;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Backend;

[TestFixture]
public class P610T3JointVisualizationTests
{
    [Test]
    public void ParseRoot_ReadsTopLevelJoints()
    {
        using var doc = JsonDocument.Parse(MinimalJointJson());
        var run = P610T3JsonParser.ParseRoot(doc.RootElement);

        Assert.That(run.Joints, Has.Count.EqualTo(1));
        var j = run.Joints[0];
        Assert.That(j.N1, Is.EqualTo(0));
        Assert.That(j.N2, Is.EqualTo(1));
        Assert.That(j.N3, Is.EqualTo(2));
        Assert.That(j.N4, Is.EqualTo(3));
        Assert.That(j.Un, Is.EqualTo(-0.1).Within(1e-12));
        Assert.That(j.Closure, Is.EqualTo(0.1).Within(1e-12));
        Assert.That(j.SnKpa, Is.EqualTo(10000).Within(1e-6));
        Assert.That(j.TauKpa, Is.EqualTo(0).Within(1e-12));
    }

    [Test]
    public void ParseRoot_FillsMissingStageJointsFromTopLevel()
    {
        using var doc = JsonDocument.Parse(TwoStageJointJson());
        var run = P610T3JsonParser.ParseRoot(doc.RootElement);

        Assert.That(run.Stages, Has.Count.EqualTo(2));
        Assert.That(run.Stages[0].Joints, Has.Count.EqualTo(1));
        Assert.That(run.Stages[1].Joints, Has.Count.EqualTo(1));
        Assert.That(run.Stages[0].Joints[0].Closure, Is.EqualTo(0.0).Within(1e-9));
        Assert.That(run.Stages[1].Joints[0].Closure, Is.EqualTo(0.1).Within(1e-9));
        Assert.That(run.Stages[1].Joints[0].SnKpa, Is.EqualTo(10000).Within(1e-3));
    }

    [Test]
    public void Kinematics_HorizontalJointClosureMatchesNormalDisplacement()
    {
        var nodes = new[]
        {
            new Node { Id = 0, X = 0, Y = 0.99 },
            new Node { Id = 1, X = 1, Y = 0.99 },
            new Node { Id = 2, X = 1, Y = 1.01 },
            new Node { Id = 3, X = 0, Y = 1.01 },
        };
        var template = new P610T3JointResult
        {
            N1 = 0, N2 = 1, N3 = 2, N4 = 3,
            Un = -0.1, Us = 0, Closure = 0.1, SnKpa = 10000, TauKpa = 0,
        };
        var disp = new (double ux, double uy)[]
        {
            (0, 0), (0, 0), (0, -0.05), (0, -0.05),
        };

        var ev = P610T3JointKinematics.Recompute(template, nodes, disp);
        Assert.That(ev.Un, Is.EqualTo(-0.05).Within(1e-9));
        Assert.That(ev.Closure, Is.EqualTo(0.05).Within(1e-9));
        Assert.That(ev.SnKpa, Is.EqualTo(5000).Within(1e-6));
        Assert.That(ev.Us, Is.EqualTo(0).Within(1e-9));
    }

    [Test]
    public void ResultsViewModel_AddsJointOutputTypesWhenJointsPresent()
    {
        var vm = new ResultsViewModel();
        Assert.That(vm.AvailableOutputTypes, Does.Not.Contain(ResultsViewModel.OutputJointNormalStress));

        vm.JointResults = new[]
        {
            new P610T3JointResult { N1 = 0, N2 = 1, N3 = 2, N4 = 3, Closure = 0.1, SnKpa = 10000 }
        };

        Assert.That(vm.HasJointResults, Is.True);
        Assert.That(vm.AvailableOutputTypes, Does.Contain(ResultsViewModel.OutputJointNormalStress));
        Assert.That(vm.AvailableOutputTypes, Does.Contain(ResultsViewModel.OutputJointShearStress));
        Assert.That(vm.AvailableOutputTypes, Does.Contain(ResultsViewModel.OutputJointOpening));
        Assert.That(vm.AvailableOutputTypes, Does.Contain(ResultsViewModel.OutputJointClosure));
        Assert.That(vm.AvailableOutputTypes, Does.Contain(ResultsViewModel.OutputJointSlip));
        Assert.That(vm.GetJointScalarValue(vm.JointResults[0], ResultsViewModel.OutputJointNormalStress),
            Is.EqualTo(10000).Within(1e-9));
        Assert.That(ResultsViewModel.IsJointOutput(ResultsViewModel.OutputJointClosure), Is.True);
        Assert.That(ResultsViewModel.IsJointOutput(ResultsViewModel.OutputStressYY), Is.False);
    }

    private static string MinimalJointJson() =>
        """
        {
          "nodes":[
            {"id":1,"x":0,"y":0.99,"ux":0,"uy":0},
            {"id":2,"x":1,"y":0.99,"ux":0,"uy":0},
            {"id":3,"x":1,"y":1.01,"ux":0,"uy":-0.1},
            {"id":4,"x":0,"y":1.01,"ux":0,"uy":-0.1}
          ],
          "elements":[{"id":1,"n1":1,"n2":2,"n3":4,"active":1}],
          "joints":[
            {"n1":1,"n2":2,"n3":3,"n4":4,"un":-0.1,"us":0,"closure":0.1,
             "sn_kPa":10000,"tau_kPa":0,"L":1,"nx":0,"ny":1,"slipped":false}
          ]
        }
        """;

    private static string TwoStageJointJson() =>
        """
        {
          "nodes":[
            {"id":1,"x":0,"y":0.99,"ux":0,"uy":0},
            {"id":2,"x":1,"y":0.99,"ux":0,"uy":0},
            {"id":3,"x":1,"y":1.01,"ux":0,"uy":-0.1},
            {"id":4,"x":0,"y":1.01,"ux":0,"uy":-0.1}
          ],
          "elements":[{"id":1,"n1":1,"n2":2,"n3":4,"active":1}],
          "stages":[
            {"index":0,"name":"in_situ_pre_excavation","nodes":[
              {"id":1,"x":0,"y":0.99,"ux":0,"uy":0},
              {"id":2,"x":1,"y":0.99,"ux":0,"uy":0},
              {"id":3,"x":1,"y":1.01,"ux":0,"uy":0},
              {"id":4,"x":0,"y":1.01,"ux":0,"uy":0}
            ]},
            {"index":1,"name":"after_excavation_1","nodes":[
              {"id":1,"x":0,"y":0.99,"ux":0,"uy":0},
              {"id":2,"x":1,"y":0.99,"ux":0,"uy":0},
              {"id":3,"x":1,"y":1.01,"ux":0,"uy":-0.1},
              {"id":4,"x":0,"y":1.01,"ux":0,"uy":-0.1}
            ]}
          ],
          "joints":[
            {"n1":1,"n2":2,"n3":3,"n4":4,"un":-0.1,"us":0,"closure":0.1,
             "sn_kPa":10000,"tau_kPa":0,"L":1,"nx":0,"ny":1,"slipped":false}
          ]
        }
        """;
}
