using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using GeotechSlopeApp.Models.Geometry;

namespace GeotechSlopeApp.Services.GeometryImport;

/// <summary>Minimal LandXML surface / alignment import into a planar kernel.</summary>
public static class LandXmlGeometryImporter
{
    public static PlanarGeometryKernel Import(string filePath)
    {
        var doc = XDocument.Load(filePath);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var kernel = new PlanarGeometryKernel();
        var nextVid = 1;
        var nextFace = 1;

        foreach (var surface in doc.Descendants(ns + "Surface"))
        {
            var name = (string?)surface.Attribute("name") ?? $"Surface {nextFace}";
            var pts = surface.Descendants(ns + "P")
                .Select(ParsePoint)
                .Where(p => p.HasValue)
                .Select(p => p!.Value)
                .ToList();
            if (pts.Count < 3) continue;

            var face = new PlanarMapFace { Id = nextFace++, Name = name };
            foreach (var (x, y) in pts)
            {
                var id = nextVid++;
                kernel.Vertices.Add(new PlanarMapVertex { StableId = id, X = x, Y = y });
                face.VertexStableIds.Add(id);
            }

            for (var i = 0; i < face.VertexStableIds.Count; i++)
            {
                kernel.Edges.Add(new PlanarMapEdge
                {
                    Id = kernel.Edges.Count + 1,
                    StartStableId = face.VertexStableIds[i],
                    EndStableId = face.VertexStableIds[(i + 1) % face.VertexStableIds.Count],
                    Role = GeometryEdgeRole.Boundary
                });
            }

            kernel.Faces.Add(face);
        }

        // Fallback: CoordGeom / Line strings
        if (kernel.Faces.Count == 0)
        {
            foreach (var line in doc.Descendants(ns + "Line"))
            {
                var start = line.Element(ns + "Start");
                var end = line.Element(ns + "End");
                if (start == null || end == null) continue;
                var a = ParseCoord(start.Value);
                var b = ParseCoord(end.Value);
                if (a == null || b == null) continue;
                var idA = nextVid++;
                var idB = nextVid++;
                kernel.Vertices.Add(new PlanarMapVertex { StableId = idA, X = a.Value.X, Y = a.Value.Y });
                kernel.Vertices.Add(new PlanarMapVertex { StableId = idB, X = b.Value.X, Y = b.Value.Y });
                kernel.Edges.Add(new PlanarMapEdge
                {
                    Id = kernel.Edges.Count + 1,
                    StartStableId = idA,
                    EndStableId = idB,
                    Role = GeometryEdgeRole.Boundary
                });
            }
        }

        if (kernel.Faces.Count == 0 && kernel.Edges.Count == 0)
            throw new InvalidOperationException(
                "No LandXML Surface or Line geometry was found. Export a Surface CgPoints / PntList2D and try again.");

        return kernel;
    }

    public static void Export(PlanarGeometryKernel kernel, string filePath)
    {
        var ns = XNamespace.Get("http://www.landxml.org/schema/LandXML-1.2");
        var surfaces = new XElement(ns + "Surfaces");
        foreach (var face in kernel.SoilFaces.Where(f => f.VertexStableIds.Count >= 3))
        {
            var definition = new XElement(ns + "Definition", new XAttribute("surfType", "TIN"));
            var pnts = new XElement(ns + "Pnts");
            var i = 1;
            foreach (var id in face.VertexStableIds)
            {
                var v = kernel.FindVertex(id);
                if (v == null) continue;
                pnts.Add(new XElement(ns + "P",
                    new XAttribute("id", i++),
                    $"{v.Y.ToString(CultureInfo.InvariantCulture)} {v.X.ToString(CultureInfo.InvariantCulture)} 0"));
            }

            definition.Add(pnts);
            surfaces.Add(new XElement(ns + "Surface", new XAttribute("name", face.Name), definition));
        }

        var root = new XElement(ns + "LandXML",
            new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
            new XElement(ns + "Units",
                new XElement(ns + "Metric", new XAttribute("linearUnit", "meter"))),
            surfaces);
        new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root).Save(filePath);
    }

    private static (double X, double Y)? ParsePoint(XElement el)
    {
        var parts = el.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        // LandXML often stores N E (Y X)
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return null;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var e)) return null;
        return (e, n);
    }

    private static (double X, double Y)? ParseCoord(string value)
    {
        var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var a)) return null;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b)) return null;
        return (b, a);
    }
}
