namespace GeotechSlopeApp.Services.GeometryImport;

internal sealed class GeometryPointRegistry
{
    private readonly List<(int Id, double X, double Y)> _points = new();
    private int _nextId = 1;

    public GeometryPointRegistry(double mergeTolerance)
    {
        MergeTolerance = Math.Max(1e-12, mergeTolerance);
    }

    public double MergeTolerance { get; }

    public int GetOrAdd(double x, double y, int? preferredId = null)
    {
        foreach (var p in _points)
        {
            if (Math.Abs(p.X - x) <= MergeTolerance && Math.Abs(p.Y - y) <= MergeTolerance)
                return p.Id;
        }

        var id = preferredId ?? _nextId++;
        if (preferredId.HasValue)
            _nextId = Math.Max(_nextId, preferredId.Value + 1);

        _points.Add((id, x, y));
        return id;
    }

    public bool TryGetCoordinates(int id, out double x, out double y)
    {
        foreach (var p in _points)
        {
            if (p.Id != id)
                continue;
            x = p.X;
            y = p.Y;
            return true;
        }

        x = y = 0;
        return false;
    }

    public IReadOnlyList<(int Id, double X, double Y)> AllPoints => _points;

    public void SeedNextId(int nextId) => _nextId = Math.Max(_nextId, nextId);
}
