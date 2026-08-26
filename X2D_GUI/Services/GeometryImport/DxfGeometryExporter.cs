using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GeotechSlopeApp.Models.Geometry;
using GeotechSlopeApp.Services.Geometry;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Services.GeometryImport;

/// <summary>Writes the planar kernel / geometry VM to ASCII DXF AutoCAD 2000 (AC1015).</summary>
public static class DxfGeometryExporter
{
    public static void Export(GeometryDefinitionViewModel geometry, string filePath)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        var kernel = PlanarGeometryKernelBuilder.Build(geometry);
        Export(kernel, filePath);
    }

    public static void Export(PlanarGeometryKernel kernel, string filePath)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        var sb = new StringBuilder(8192);
        sb.AppendLine("0");
        sb.AppendLine("SECTION");
        sb.AppendLine("2");
        sb.AppendLine("HEADER");
        sb.AppendLine("9");
        sb.AppendLine("$ACADVER");
        sb.AppendLine("1");
        sb.AppendLine("AC1015");
        sb.AppendLine("0");
        sb.AppendLine("ENDSEC");
        sb.AppendLine("0");
        sb.AppendLine("SECTION");
        sb.AppendLine("2");
        sb.AppendLine("ENTITIES");

        foreach (var face in kernel.SoilFaces.Where(f => f.VertexStableIds.Count >= 2))
        {
            WriteLwPolyline(sb, face.VertexStableIds
                .Select(id => kernel.FindVertex(id))
                .Where(v => v != null)
                .Select(v => (v!.X, v.Y))
                .ToList(), layer: "BOUNDARY", closed: true);
        }

        foreach (var feature in kernel.GeologicalFeatures.Where(f => f.Vertices.Count >= 2))
        {
            var layer = feature.Kind switch
            {
                GeologicalFeatureKind.WeakPlane => "WEAK_PLANE",
                GeologicalFeatureKind.Bedding => "BEDDING",
                GeologicalFeatureKind.TensionCrack => "TENSION_CRACK",
                _ => "GEOLOGY"
            };
            WriteLwPolyline(sb, feature.Vertices, layer, closed: false);
        }

        sb.AppendLine("0");
        sb.AppendLine("ENDSEC");
        sb.AppendLine("0");
        sb.AppendLine("EOF");
        File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(false));
    }

    private static void WriteLwPolyline(
        StringBuilder sb,
        System.Collections.Generic.IReadOnlyList<(double X, double Y)> verts,
        string layer,
        bool closed)
    {
        if (verts.Count < 2) return;
        sb.AppendLine("0");
        sb.AppendLine("LWPOLYLINE");
        sb.AppendLine("8");
        sb.AppendLine(layer);
        sb.AppendLine("90");
        sb.AppendLine(verts.Count.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("70");
        sb.AppendLine(closed ? "1" : "0");
        foreach (var (x, y) in verts)
        {
            sb.AppendLine("10");
            sb.AppendLine(x.ToString("G17", CultureInfo.InvariantCulture));
            sb.AppendLine("20");
            sb.AppendLine(y.ToString("G17", CultureInfo.InvariantCulture));
        }
    }
}
