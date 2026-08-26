using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core;

[TestFixture]
public class StaticGroundwaterBbarExportTests
{
    [Test]
    public void BuildBlock_WritesBbarMethodAndCoefficient()
    {
        var model = new GroundwaterModel
        {
            IsEnabled = true,
            Method = GroundwaterMethod.Bbar,
            Bbar = 0.35,
            GammaW = 9.81,
            StaticSettings = new GroundwaterStaticAnalysisSettings
            {
                IncludeRuCoefficients = true,
                IncludeWaterSurfaces = false
            }
        };

        var block = GroundwaterDatWriter.BuildBlock(model);

        Assert.That(block, Does.Contain("! method bbar"));
        Assert.That(block, Does.Contain("! Bbar"));
        Assert.That(block, Does.Contain("0.35"));
        Assert.That(block, Does.Contain("! static_bbar true"));
        Assert.That(block, Does.Contain("! nosurf 0"));
    }
}
