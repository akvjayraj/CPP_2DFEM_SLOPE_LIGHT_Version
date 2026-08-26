using System.Reflection;
using netDxf;
using netDxf.Entities;

var path = args.Length > 0 ? args[0] : throw new ArgumentException("Need DXF path");
var dxf = DxfDocument.Load(path);

Console.WriteLine($"File: {path}");
Console.WriteLine($"Model Entities.All: {dxf.Entities.All.Count()}");
Console.WriteLine($"Lines={dxf.Entities.Lines.Count()} Polys2D={dxf.Entities.Polylines2D.Count()} Inserts={dxf.Entities.Inserts.Count()} Hatches={dxf.Entities.Hatches.Count()} Splines={dxf.Entities.Splines.Count()}");

var blockEntityTotal = 0;
foreach (var block in dxf.Blocks)
{
    var count = block.Entities.Count;
    if (count > 0)
        Console.WriteLine($"Block '{block.Name}': {count} entities");
    blockEntityTotal += count;
}
Console.WriteLine($"Total block definition entities: {blockEntityTotal}");

foreach (var layout in dxf.Layouts)
{
    var count = layout.AssociatedBlock.Entities.Count;
    Console.WriteLine($"Layout '{layout.Name}': {count} entities");
}
