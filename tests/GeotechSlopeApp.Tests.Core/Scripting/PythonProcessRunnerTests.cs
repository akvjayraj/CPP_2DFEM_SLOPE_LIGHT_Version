using GeotechSlopeApp.Scripting;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Scripting;

[TestFixture]
public class PythonProcessRunnerTests
{
    [Test]
    public void IsWindowsStoreStub_RejectsWindowsAppsPath()
    {
        Assert.That(
            PythonProcessRunner.IsWindowsStoreStub(
                @"C:\Users\akv\AppData\Local\Microsoft\WindowsApps\python.exe"),
            Is.True);
    }

    [Test]
    public void IsWindowsStoreStub_AllowsNormalInstall()
    {
        Assert.That(
            PythonProcessRunner.IsWindowsStoreStub(
                @"C:\Users\akv\AppData\Local\Programs\Python\Python312\python.exe"),
            Is.False);
    }

    [Test]
    public void WrapUserScript_InjectsProjectImport()
    {
        var wrapped = PythonProcessRunner.WrapUserScript("print(project.fe.srf())", @"C:\repo\scripts\xfem_client");
        Assert.That(wrapped, Does.Contain("from xfem import project"));
        Assert.That(wrapped, Does.Contain(@"C:\\repo\\scripts\\xfem_client"));
        Assert.That(wrapped, Does.Contain("print(project.fe.srf())"));
    }

    [Test]
    public void WrapUserScript_DoesNotDuplicateImport()
    {
        var wrapped = PythonProcessRunner.WrapUserScript(
            "from xfem import project\nprint(1)",
            @"C:\client");
        Assert.That(wrapped, Does.Contain("sys.path.insert"));
        Assert.That(wrapped.Split("from xfem import project").Length, Is.EqualTo(2));
    }

    [Test]
    public void TryResolve_WhenPythonInstalled_Accepts311OrNewer()
    {
        if (!PythonProcessRunner.TryResolve(out var exe, out _))
            Assert.Ignore("CPython 3.11+ is not installed on this machine.");
        Assert.That(exe, Is.Not.Empty);
    }
}
