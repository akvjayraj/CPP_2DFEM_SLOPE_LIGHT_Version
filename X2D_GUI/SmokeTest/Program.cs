using GeotechSlopeApp.Services.GeometryImport;

internal static class Program
{
    private static int Main(string[] args)
    {
        var dxfPath = args.Length > 0
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Verification", "slope", "sample_geometry", "standard_slope_section.dxf");

        if (!File.Exists(dxfPath))
        {
            Console.Error.WriteLine($"DXF not found: {dxfPath}");
            return 1;
        }

        var options = new GeometryImportOptions();
        var result = DxfGeometryImporter.ImportWithDiagnostics(dxfPath, options);
        var doc = result.Document;
        Console.WriteLine($"File: {Path.GetFileName(dxfPath)}");
        Console.WriteLine($"IsEmpty: {doc.IsEmpty}");
        Console.WriteLine($"Points: {doc.Points.Count}, Lines: {doc.Lines.Count}, Surfaces: {doc.Surfaces.Count}, Arcs: {doc.Arcs.Count}");
        Console.WriteLine($"Raw inventory: {result.DxfDiagnostics?.RawInventory.FormatSummary()}");
        return doc.IsEmpty ? 2 : 0;
    }
}
