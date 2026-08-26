using netDxf;
using netDxf.Entities;

namespace GeotechSlopeApp.Services.GeometryImport;

public readonly record struct DxfEntityCounts(
    int Lines = 0,
    int Polylines2D = 0,
    int Polylines3D = 0,
    int Arcs = 0,
    int Circles = 0,
    int Points = 0,
    int Inserts = 0,
    int Splines = 0,
    int Ellipses = 0,
    int Hatches = 0,
    int MLines = 0)
{
    public int Total =>
        Lines + Polylines2D + Polylines3D + Arcs + Circles + Points + Inserts + Splines + Ellipses + Hatches + MLines;

    public static DxfEntityCounts Empty => default;

    public static DxfEntityCounts operator +(DxfEntityCounts a, DxfEntityCounts b) => new(
        a.Lines + b.Lines,
        a.Polylines2D + b.Polylines2D,
        a.Polylines3D + b.Polylines3D,
        a.Arcs + b.Arcs,
        a.Circles + b.Circles,
        a.Points + b.Points,
        a.Inserts + b.Inserts,
        a.Splines + b.Splines,
        a.Ellipses + b.Ellipses,
        a.Hatches + b.Hatches,
        a.MLines + b.MLines);

    public static DxfEntityCounts FromEntities(IEnumerable<EntityObject> entities)
    {
        int lines = 0, polylines2D = 0, polylines3D = 0, arcs = 0, circles = 0, points = 0,
            inserts = 0, splines = 0, ellipses = 0, hatches = 0, mLines = 0;

        foreach (var entity in entities)
        {
            switch (entity)
            {
                case Line: lines++; break;
                case Polyline2D: polylines2D++; break;
                case Polyline3D: polylines3D++; break;
                case Arc: arcs++; break;
                case Circle: circles++; break;
                case Point: points++; break;
                case Insert: inserts++; break;
                case Spline: splines++; break;
                case Ellipse: ellipses++; break;
                case Hatch: hatches++; break;
                case MLine: mLines++; break;
            }
        }

        return new DxfEntityCounts(lines, polylines2D, polylines3D, arcs, circles, points, inserts, splines, ellipses, hatches, mLines);
    }

    public IEnumerable<string> FormatParts()
    {
        if (Lines > 0) yield return $"{Lines} line(s)";
        if (Polylines2D > 0) yield return $"{Polylines2D} polyline(s)";
        if (Polylines3D > 0) yield return $"{Polylines3D} 3D polyline(s)";
        if (Arcs > 0) yield return $"{Arcs} arc(s)";
        if (Circles > 0) yield return $"{Circles} circle(s)";
        if (Points > 0) yield return $"{Points} point(s)";
        if (Inserts > 0) yield return $"{Inserts} block insert(s)";
        if (Splines > 0) yield return $"{Splines} spline(s)";
        if (Ellipses > 0) yield return $"{Ellipses} ellipse(s)";
        if (Hatches > 0) yield return $"{Hatches} hatch(es)";
        if (MLines > 0) yield return $"{MLines} multiline(s)";
    }
}

public sealed class DxfEntityInventory
{
    public DxfEntityCounts ModelSpace { get; init; }
    public DxfEntityCounts BlockDefinitions { get; init; }
    public int BlockDefinitionEntities { get; init; }
    public int Viewports { get; init; }
    public int LayoutEntities { get; init; }

    public DxfEntityCounts Combined => ModelSpace + BlockDefinitions;

    public bool HasImportableEntities => Combined.Total > 0;

    public bool HasAnyContent => HasImportableEntities || Viewports > 0 || LayoutEntities > 0;

    public static DxfEntityInventory FromDocument(DxfDocument dxf)
    {
        var model = DxfEntityCounts.FromEntities(dxf.Entities.All);

        var layoutCount = 0;
        var viewportCount = 0;
        foreach (var layout in dxf.Layouts)
        {
            foreach (var entity in layout.AssociatedBlock.Entities)
            {
                layoutCount++;
                if (entity is Viewport)
                    viewportCount++;
            }
        }

        var blockCounts = DxfEntityCounts.Empty;
        var blockEntityTotal = 0;
        foreach (var block in dxf.Blocks)
        {
            if (DxfGeometrySource.IsSystemBlockName(block.Name))
                continue;

            blockEntityTotal += block.Entities.Count;
            blockCounts += DxfEntityCounts.FromEntities(block.Entities);
        }

        return new DxfEntityInventory
        {
            ModelSpace = model,
            BlockDefinitions = blockCounts,
            BlockDefinitionEntities = blockEntityTotal,
            Viewports = viewportCount,
            LayoutEntities = layoutCount
        };
    }

    public string FormatSummary()
    {
        var parts = new List<string>();
        parts.AddRange(Combined.FormatParts());

        if (BlockDefinitionEntities > 0)
            parts.Add($"{BlockDefinitionEntities} entity(ies) in block definitions");

        if (Viewports > 0)
            parts.Add($"{Viewports} viewport(s)");

        if (parts.Count == 0 && LayoutEntities > 0)
            parts.Add($"{LayoutEntities} non-importable layout object(s)");

        return parts.Count > 0 ? string.Join(", ", parts) : "no supported entities";
    }

    public string FormatDetailedSummary()
    {
        var sections = new List<string>();

        if (ModelSpace.Total > 0)
            sections.Add($"Model space: {string.Join(", ", ModelSpace.FormatParts())}");

        if (BlockDefinitions.Total > 0 || BlockDefinitionEntities > 0)
        {
            var blockParts = BlockDefinitions.FormatParts().ToList();
            if (BlockDefinitionEntities > 0)
                blockParts.Add($"{BlockDefinitionEntities} entity(ies) in block definitions");
            sections.Add($"Block definitions: {string.Join(", ", blockParts)}");
        }

        if (Viewports > 0)
            sections.Add($"Layout: {Viewports} viewport(s)");

        if (sections.Count == 0)
            return FormatSummary();

        return string.Join(". ", sections) + ".";
    }

    public string FormatEmptyMessage(GeometryImportOptions options)
    {
        if (!options.ImportPoints && !options.ImportLines && !options.ImportSurfaces)
        {
            return "No import types selected. Enable at least one of points, lines/polylines, or closed surfaces.";
        }

        if (!HasAnyContent)
        {
            return "No geometry entities were found in the selected file. "
                   + "The file may use unsupported CAD objects, or geometry may be stored in an external reference.";
        }

        var summary = FormatSummary();
        var detail = FormatDetailedSummary();

        if (!HasImportableEntities)
        {
            return "No importable geometry was found. "
                   + (Viewports > 0
                       ? $"The layout contains {Viewports} viewport(s) but model space and block definitions have no supported entities. "
                       : string.Empty)
                   + "Try exporting geometry to model space from your CAD tool.";
        }

        return $"No importable geometry with the current options.{Environment.NewLine}{Environment.NewLine}"
               + $"Found in file: {summary}.{Environment.NewLine}{Environment.NewLine}"
               + $"Breakdown: {detail}{Environment.NewLine}{Environment.NewLine}"
               + "Try enabling all import types (points, lines/polylines, closed surfaces).";
    }
}

public sealed class DxfImportDiagnostics
{
    public DxfEntityInventory RawInventory { get; init; } = new();

    public string FormatEmptyMessage(GeometryImportOptions options) =>
        RawInventory.FormatEmptyMessage(options);

    public string FormatStatusSummary() => RawInventory.FormatSummary();
}

public sealed class GeometryImportParseResult
{
    public GeometryImportDocument Document { get; init; } = new();
    public DxfImportDiagnostics? DxfDiagnostics { get; init; }
}
