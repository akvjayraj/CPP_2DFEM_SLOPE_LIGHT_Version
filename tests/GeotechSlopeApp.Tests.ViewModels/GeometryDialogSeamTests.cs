using GeotechSlopeApp.Tests.ViewModels.Support;
using GeotechSlopeApp.ViewModels;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.ViewModels;

/// <summary>
/// Verifies the IDialogService seam: a command path that pops a MessageBox in the app instead
/// routes through the ambient dialog service, so it runs headlessly and is observable in tests.
/// </summary>
[TestFixture]
public class GeometryDialogSeamTests
{
    [Test]
    public void ExportGeometryToCsv_WithNoGeometry_RoutesWarningThroughDialogService()
    {
        var recorder = RecordingDialogService.Install();
        var vm = new GeometryDefinitionViewModel();
        Assume.That(vm.GeometryVertexRows, Is.Empty);

        vm.ExportGeometryToCsvCommand.Execute(null);

        Assert.That(recorder.Messages, Is.Not.Empty,
            "the empty-geometry export warning should be captured by the dialog service stub");
        Assert.That(recorder.Messages[0], Does.Contain("no geometry").IgnoreCase);
    }
}
