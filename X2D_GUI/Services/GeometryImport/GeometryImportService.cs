using System;
using System.IO;
using GeotechSlopeApp.Models.Geometry;
using Microsoft.Win32;

namespace GeotechSlopeApp.Services.GeometryImport;

public static class GeometryImportService
{
    public const string FileFilter =
        "Geometry files (*.csv;*.dxf;*.xml;*.geojson;*.json)|*.csv;*.dxf;*.xml;*.geojson;*.json|" +
        "CSV (*.csv)|*.csv|" +
        "DXF (*.dxf)|*.dxf|" +
        "LandXML (*.xml)|*.xml|" +
        "GeoJSON (*.geojson;*.json)|*.geojson;*.json|" +
        "DWG (*.dwg)|*.dwg|" +
        "All files (*.*)|*.*";

    public const string ExportFilter =
        "DXF (*.dxf)|*.dxf|" +
        "CSV (*.csv)|*.csv|" +
        "LandXML (*.xml)|*.xml|" +
        "GeoJSON (*.geojson)|*.geojson|" +
        "All files (*.*)|*.*";

    public static bool TryPickImportFile(out string? filePath)
    {
        var dialog = new OpenFileDialog
        {
            Filter = FileFilter,
            Title = "Import Geometry (CSV / DXF / LandXML / GeoJSON)"
        };

        if (dialog.ShowDialog() != true)
        {
            filePath = null;
            return false;
        }

        filePath = dialog.FileName;
        return true;
    }

    public static bool TryPickExportFile(out string? filePath)
    {
        var dialog = new SaveFileDialog
        {
            Filter = ExportFilter,
            Title = "Export Geometry",
            FileName = "geometry.dxf",
            AddExtension = true
        };
        if (dialog.ShowDialog() != true)
        {
            filePath = null;
            return false;
        }

        filePath = dialog.FileName;
        return true;
    }

    public static GeometryImportParseResult ParseFileWithDiagnostics(string filePath, GeometryImportOptions options)
    {
        var ext = Path.GetExtension(filePath);
        if (ext.Equals(".dwg", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "DWG files are not read natively.\n" +
                "In AutoCAD / BricsCAD / DraftSight use Save As → AutoCAD DXF (*.dxf), AutoCAD 2000 or later, then import the DXF.\n" +
                "Optional: convert with the ODA File Converter, then import the resulting DXF.");
        }

        if (ext.Equals(".dxf", StringComparison.OrdinalIgnoreCase))
            return DxfGeometryImporter.ImportWithDiagnostics(filePath, options);

        if (ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return new GeometryImportParseResult
            {
                Document = CsvGeometryImporter.Import(filePath)
            };
        }

        if (ext.Equals(".xml", StringComparison.OrdinalIgnoreCase))
        {
            var kernel = LandXmlGeometryImporter.Import(filePath);
            return new GeometryImportParseResult { Document = KernelToImportDocument(kernel) };
        }

        if (ext.Equals(".geojson", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            var kernel = GeoJsonGeometryInterchange.Import(filePath, options.CoordinateSystem);
            return new GeometryImportParseResult { Document = KernelToImportDocument(kernel) };
        }

        throw new NotSupportedException($"Unsupported geometry file type: {ext}");
    }

    public static GeometryImportDocument ParseFile(string filePath, GeometryImportOptions options) =>
        ParseFileWithDiagnostics(filePath, options).Document;

    public static bool IsDxfFile(string filePath) =>
        Path.GetExtension(filePath).Equals(".dxf", StringComparison.OrdinalIgnoreCase);

    public static bool IsKernelFormat(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return ext.Equals(".xml", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".geojson", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static GeometryImportDocument KernelToImportDocument(PlanarGeometryKernel kernel)
    {
        var doc = new GeometryImportDocument();
        foreach (var face in kernel.SoilFaces)
        {
            var pts = new System.Collections.Generic.List<(double X, double Y)>();
            foreach (var id in face.VertexStableIds)
            {
                var v = kernel.FindVertex(id);
                if (v != null) pts.Add((v.X, v.Y));
            }

            if (pts.Count >= 3)
            {
                doc.ClosedPolygons.Add(new GeometryImportClosedPolygon
                {
                    Vertices = pts,
                    Name = face.Name
                });
            }
        }

        foreach (var feature in kernel.GeologicalFeatures)
        {
            if (feature.Vertices.Count >= 2)
                doc.AddOpenPolyline(feature.Vertices);
        }

        return doc;
    }
}
