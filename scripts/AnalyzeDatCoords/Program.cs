using System.Globalization;
using GeotechSlopeApp.Backend;

var path = args.Length > 0 ? args[0] : "Verification/slope/srf_sweep/p64_embankment_srf_multi.dat";
var root = AppContext.BaseDirectory;
while (!string.IsNullOrEmpty(root) && !Directory.Exists(Path.Combine(root, "Verification", "slope")))
    root = Path.GetDirectoryName(root)!;
path = Path.IsPathRooted(path) ? path : Path.Combine(root, path);

var content = File.ReadAllText(path);
var built = P64EmbankmentMeshBuilder.BuildQ8Mesh(20, 10, 10, 5, 20, 20, 20, 10, 5);

int atOrigin = built.Nodes.Count(n => Math.Abs(n.x) < 1e-9 && Math.Abs(n.y) < 1e-9);
double xMin = built.Nodes.Min(n => n.x), xMax = built.Nodes.Max(n => n.x);
double yMin = built.Nodes.Min(n => n.y), yMax = built.Nodes.Max(n => n.y);

Console.WriteLine($"Builder: {built.Nn} nodes, at (0,0): {atOrigin}");
Console.WriteLine($"Builder X [{xMin:G6}, {xMax:G6}]  Y [{yMin:G6}, {yMax:G6}]");

// Parse coords from file like importer
var tokens = new Queue<string>();
foreach (var raw in content.Split('\n'))
{
    var line = raw;
    var bang = line.IndexOf('!');
    if (bang >= 0) line = line[..bang];
    foreach (var tok in line.Split([' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries))
        tokens.Enqueue(tok);
}
int.TryParse(tokens.Dequeue(), out int nels);
int.TryParse(tokens.Dequeue(), out int nn);
int.TryParse(tokens.Dequeue(), out int np);
for (int i = 0; i < 6 * np; i++) tokens.Dequeue();
var fileCoords = new List<(double x, double y)>();
for (int i = 0; i < nn; i++)
{
    double.TryParse(tokens.Dequeue(), NumberStyles.Float, CultureInfo.InvariantCulture, out double x);
    double.TryParse(tokens.Dequeue(), NumberStyles.Float, CultureInfo.InvariantCulture, out double y);
    fileCoords.Add((x, y));
}
int fileOrigin = fileCoords.Count(c => Math.Abs(c.x) < 1e-9 && Math.Abs(c.y) < 1e-9);
Console.WriteLine($"File: {nn} nodes, at (0,0): {fileOrigin}");
Console.WriteLine($"File X [{fileCoords.Min(c => c.x):G6}, {fileCoords.Max(c => c.x):G6}]  Y [{fileCoords.Min(c => c.y):G6}, {fileCoords.Max(c => c.y):G6}]");

// Compare first mismatch
for (int id = 1; id <= Math.Min(built.Nn, fileCoords.Count); id++)
{
    var b = built.Nodes.First(n => n.id == id);
    var f = fileCoords[id - 1];
    if (Math.Abs(b.x - f.x) > 0.01 || Math.Abs(b.y - f.y) > 0.01)
    {
        Console.WriteLine($"Mismatch node {id}: file ({f.x:G4},{f.y:G4}) builder ({b.x:G4},{b.y:G4})");
        if (id > 5) break;
    }
}
