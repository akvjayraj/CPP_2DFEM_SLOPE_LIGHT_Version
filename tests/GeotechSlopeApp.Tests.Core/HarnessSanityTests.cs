using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core;

/// <summary>
/// Proves the NUnit harness builds, references the WPF app assembly, and runs.
/// If this fails to compile/run, the project wiring is wrong (not the app logic).
/// </summary>
[TestFixture]
public class HarnessSanityTests
{
    [Test]
    public void Harness_Runs()
    {
        Assert.That(2 + 2, Is.EqualTo(4));
    }

    [Test]
    public void App_Assembly_Is_Referenced()
    {
        var appAssembly = typeof(GeotechSlopeApp.Backend.MeshElementQualityMetrics).Assembly;
        Assert.That(appAssembly.GetName().Name, Is.EqualTo("GeotechSlopeApp"));
    }
}
