using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Tests.ViewModels.Support;

/// <summary>
/// Builds a set of wired headless view-models (mirrors the BuildHeadlessVms helper from the
/// TunnelWizardSmokeTest console harness) for use in STA view-model tests.
/// </summary>
internal static class HeadlessVms
{
    public static (
        GeometryDefinitionViewModel Geometry,
        MeshViewModel Mesh,
        BoundaryConditionsViewModel Bcs,
        FieldStressViewModel FieldStress,
        StagedConstructionViewModel Staged) Build()
    {
        var gd = new GeometryDefinitionViewModel();
        var mesh = new MeshViewModel { GeometryDefinitionVM = gd };
        var bcs = new BoundaryConditionsViewModel { GeometryDefinitionVM = gd, MeshVM = mesh };
        var loads = new LoadConditionsViewModel { GeometryDefinition = gd };
        var staged = new StagedConstructionViewModel();
        staged.SetGeometryViewModel(gd);
        gd.StagedConstruction = staged;
        return (gd, mesh, bcs, loads.FieldStress, staged);
    }
}
