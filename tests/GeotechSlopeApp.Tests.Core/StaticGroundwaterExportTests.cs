using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core;

[TestFixture]
public class StaticGroundwaterExportTests
{
    [Test]
    public void BuildBlock_ForcesWaterSurfaces_WhenSurfPointsExist_EvenIfCheckboxOff()
    {
        var model = new GroundwaterModel
        {
            IsEnabled = true,
            Method = GroundwaterMethod.Hydrostatic,
            GammaW = 9.81,
            SurfPoints =
            {
                (0, 15),
                (18, 15),
                (30, 23),
                (48, 29),
                (66, 32)
            },
            StaticSettings = new GroundwaterStaticAnalysisSettings
            {
                IncludeWaterSurfaces = false,
                IncludePondedFreeWater = false,
                IncludeRuCoefficients = false
            }
        };

        var block = GroundwaterDatWriter.BuildBlock(model);

        Assert.That(block, Does.Contain("! include true"));
        Assert.That(block, Does.Contain("! static_water_surfaces true"));
        Assert.That(block, Does.Contain("! method hydrostatic"));
        Assert.That(block, Does.Contain("! nosurf 5"));
        Assert.That(block, Does.Contain("! surf"));
        Assert.That(block, Does.Contain("48"));
        Assert.That(block, Does.Contain("29"));
    }

    [Test]
    public void CloneForSolverExport_MakesSpecActive_ForHydrostaticSurf()
    {
        var display = new GroundwaterModel
        {
            IsEnabled = true,
            Method = GroundwaterMethod.Hydrostatic,
            GammaW = 9.81,
            SurfPoints = { (0, 15), (66, 32) },
            StaticSettings = new GroundwaterStaticAnalysisSettings
            {
                IncludeWaterSurfaces = false
            }
        };

        var export = GroundwaterDatImporter.CloneForSolverExport(display, datContent: null);
        Assert.That(SrfGroundwaterExport.WouldBeActive(export), Is.True);
        Assert.That(export.StaticSettings!.IncludeWaterSurfaces, Is.True);

        var merged = GroundwaterDatWriter.MergeGroundwaterSection(
            "! solver:p610_t3\n! <BEGIN:Mesh>\n! <END:Mesh>\n",
            export);
        Assert.That(merged, Does.Contain("! nosurf 2"));
        Assert.That(merged, Does.Contain("! static_water_surfaces true"));
    }
}
