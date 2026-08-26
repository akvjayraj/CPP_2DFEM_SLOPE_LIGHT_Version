using System;
using System.Linq;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Tests.ViewModels.Support;
using GeotechSlopeApp.ViewModels;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.ViewModels;

/// <summary>ViewModel portion of the TunnelWizardSmokeTest harness (live preview + step machine).</summary>
[TestFixture]
public class TunnelWizardViewModelTests
{
    [SetUp]
    public void SetUp() => RecordingDialogService.Install();

    [Test]
    public void LivePreview_TracksShapeKindAndRadius()
    {
        var vm = new TunnelWizardViewModel();
        Assert.That(vm.PreviewTunnelAxis.Length, Is.GreaterThan(8));
        Assert.That(vm.PreviewDomainAxis.Length, Is.EqualTo(5));

        vm.ShapeKind = TunnelShapeKind.Horseshoe;
        Assert.That(vm.PreviewTunnelAxis.Length, Is.GreaterThan(10));

        vm.Radius = 7.0;
        vm.ShapeKind = TunnelShapeKind.Circle;
        Assert.That(vm.PreviewTunnelAxis.Max(p => Math.Sqrt(p.X * p.X + p.Y * p.Y)),
            Is.EqualTo(7.0).Within(1e-6));
    }

    [Test]
    public void StepMachine_AdvancesAndGatesApplyOnReviewStep()
    {
        var vm = new TunnelWizardViewModel();
        Assert.That(vm.CurrentStepIndex, Is.EqualTo(0));

        for (int i = 0; i < 5; i++) vm.NextStepCommand.Execute(null);
        Assert.That(vm.CurrentStepIndex, Is.EqualTo(5));
        Assert.That(vm.ApplyCommand.CanExecute(null), Is.True, "Apply enabled on Review step");

        vm.PrevStepCommand.Execute(null);
        Assert.That(vm.CurrentStepIndex, Is.EqualTo(4));
        Assert.That(vm.ApplyCommand.CanExecute(null), Is.False, "Apply disabled off Review step");
    }
}
