using System.IO;
using GeotechSlopeApp.Services.GeometryImport;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Services;

[TestFixture]
public class CsvGeometryImporterTests
{
    private string _tempDir = "";

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GeoCsvTests_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Test]
    public void ExportThenImport_PointsAndLines_RoundTrip()
    {
        var doc = new GeometryImportDocument();
        doc.Points.Add(new GeometryImportPoint { Id = 1, X = 0, Y = 0 });
        doc.Points.Add(new GeometryImportPoint { Id = 2, X = 4.5, Y = 0 });
        doc.Points.Add(new GeometryImportPoint { Id = 3, X = 4.5, Y = 3 });
        doc.Lines.Add(new GeometryImportLine { FromId = 1, ToId = 2, Kind = "external" });

        var path = Path.Combine(_tempDir, "geom.csv");
        CsvGeometryImporter.Export(path, doc);
        var loaded = CsvGeometryImporter.Import(path);

        Assert.That(loaded.Points, Has.Count.EqualTo(3));
        Assert.That(loaded.Lines, Has.Count.EqualTo(1));

        var p2 = loaded.Points.Single(p => p.Id == 2);
        Assert.That(p2.X, Is.EqualTo(4.5).Within(1e-9));
        Assert.That(loaded.Lines[0].Kind, Is.EqualTo("external"));
    }

    [Test]
    public void ExportThenImport_Surfaces_RoundTrip()
    {
        var doc = new GeometryImportDocument();
        doc.Points.Add(new GeometryImportPoint { Id = 1, X = 0, Y = 0 });
        doc.Points.Add(new GeometryImportPoint { Id = 2, X = 4.5, Y = 0 });
        doc.Points.Add(new GeometryImportPoint { Id = 3, X = 4.5, Y = 3 });
        doc.Surfaces.Add(new GeometryImportSurface { Number = 1, Name = "Soil", PointIds = new() { 1, 2, 3 } });

        var path = Path.Combine(_tempDir, "geom.csv");
        CsvGeometryImporter.Export(path, doc);
        var loaded = CsvGeometryImporter.Import(path);

        Assert.That(loaded.Surfaces, Has.Count.EqualTo(1));
        Assert.That(loaded.Surfaces[0].Number, Is.EqualTo(1));
        Assert.That(loaded.Surfaces[0].Name, Is.EqualTo("Soil"));
        Assert.That(loaded.Surfaces[0].PointIds, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void Import_LegacyCoordinateList_PopulatesLegacyBoundaryPoints()
    {
        var path = Path.Combine(_tempDir, "legacy.csv");
        File.WriteAllText(path, "0,0\n1,0\n1,1\n0,1\n");

        var loaded = CsvGeometryImporter.Import(path);

        Assert.That(loaded.LegacyBoundaryPoints, Has.Count.EqualTo(4));
        Assert.That(loaded.Points, Is.Empty);
        Assert.That(loaded.LegacyBoundaryPoints[2], Is.EqualTo((1.0, 1.0)));
    }

    [Test]
    public void Import_SurfaceWithFewerThanThreePoints_IsRejected()
    {
        var path = Path.Combine(_tempDir, "badsurface.csv");
        File.WriteAllText(path,
            "[POINTS]\nId,X,Y\n1,0,0\n2,1,0\n[SURFACES]\nNumber,Name,PointIds\n1,Bad,1;2\n");

        var loaded = CsvGeometryImporter.Import(path);

        Assert.That(loaded.Points, Has.Count.EqualTo(2));
        Assert.That(loaded.Surfaces, Is.Empty, "a surface needs at least 3 point ids");
    }

    [Test]
    public void Import_StructuredPointsSection_ParsesIdAndCoordinates()
    {
        var path = Path.Combine(_tempDir, "points.csv");
        File.WriteAllText(path,
            "[POINTS]\nId,X,Y\n1,0,0\n2,1.25,2.5\n");

        var loaded = CsvGeometryImporter.Import(path);

        Assert.That(loaded.Points, Has.Count.EqualTo(2));
        var p2 = loaded.Points.Single(p => p.Id == 2);
        Assert.That(p2.X, Is.EqualTo(1.25).Within(1e-9));
        Assert.That(p2.Y, Is.EqualTo(2.5).Within(1e-9));
    }
}
