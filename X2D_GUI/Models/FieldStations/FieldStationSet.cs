using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace GeotechSlopeApp.Models.FieldStations;

/// <summary>Project field-station evidence plus unit→material mapping. Not LEM geometry source of truth.</summary>
public sealed class FieldStationSet : INotifyPropertyChanged
{
    private DateTimeOffset? _lastCommitUtc;

    public ObservableCollection<FieldStation> Stations { get; } = new();
    public ObservableCollection<FieldUnitMaterialMapRow> UnitMaterialMap { get; } = new();

    public DateTimeOffset? LastCommitUtc
    {
        get => _lastCommitUtc;
        set => Set(ref _lastCommitUtc, value);
    }

    public int StationCount => Stations.Count;

    public int DistinctUnitCount => UnitMaterialMap.Count;

    public int UnmappedUnitCount => UnitMaterialMap.Count(r => !r.IsMapped);

    public void Clear()
    {
        Stations.Clear();
        UnitMaterialMap.Clear();
        LastCommitUtc = null;
        NotifyCounts();
    }

    public void ReplaceFrom(FieldStationSet other)
    {
        Stations.Clear();
        foreach (var s in other.Stations)
        {
            var copy = new FieldStation
            {
                Id = s.Id,
                X = s.X,
                CollarY = s.CollarY,
                Label = s.Label
            };
            foreach (var interval in s.Intervals)
            {
                copy.Intervals.Add(new FieldUnitInterval
                {
                    TopY = interval.TopY,
                    BottomY = interval.BottomY,
                    UnitCode = interval.UnitCode,
                    Note = interval.Note
                });
            }
            Stations.Add(copy);
        }

        UnitMaterialMap.Clear();
        foreach (var row in other.UnitMaterialMap)
        {
            UnitMaterialMap.Add(new FieldUnitMaterialMapRow
            {
                UnitCode = row.UnitCode,
                MaterialId = row.MaterialId,
                Colour = row.Colour
            });
        }

        LastCommitUtc = other.LastCommitUtc;
        NotifyCounts();
    }

    /// <summary>Rebuilds the unit map from station logs, preserving material ids and colours.</summary>
    public void RebuildUnitMapPreservingAssignments()
    {
        var previous = UnitMaterialMap
            .Where(r => !string.IsNullOrWhiteSpace(r.UnitCode))
            .ToDictionary(
                r => r.UnitCode,
                r => (r.MaterialId, r.Colour),
                StringComparer.OrdinalIgnoreCase);

        var codes = Stations
            .SelectMany(s => s.Intervals)
            .Select(i => i.UnitCode.Trim())
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var used = new List<Color>();
        foreach (var code in codes)
        {
            if (previous.TryGetValue(code, out var prior) && !FieldStationUnitColours.IsUnset(prior.Colour))
                used.Add(prior.Colour);
        }

        UnitMaterialMap.Clear();
        foreach (var code in codes)
        {
            int? materialId = null;
            Color colour;
            if (previous.TryGetValue(code, out var prior))
            {
                materialId = prior.MaterialId;
                colour = FieldStationUnitColours.IsUnset(prior.Colour)
                    ? FieldStationUnitColours.NextUnused(used)
                    : prior.Colour;
            }
            else
            {
                colour = FieldStationUnitColours.NextUnused(used);
            }

            if (!used.Contains(colour))
                used.Add(colour);

            UnitMaterialMap.Add(new FieldUnitMaterialMapRow
            {
                UnitCode = code,
                MaterialId = materialId,
                Colour = colour
            });
        }

        NotifyCounts();
    }

    public int? MaterialIdForUnit(string unitCode)
    {
        var id = RowForUnit(unitCode)?.MaterialId;
        return id is > 0 ? id : null;
    }

    public FieldUnitMaterialMapRow? RowForUnit(string unitCode)
    {
        if (string.IsNullOrWhiteSpace(unitCode)) return null;
        return UnitMaterialMap.FirstOrDefault(r =>
            string.Equals(r.UnitCode, unitCode, StringComparison.OrdinalIgnoreCase));
    }

    public void NotifyCounts()
    {
        OnPropertyChanged(nameof(StationCount));
        OnPropertyChanged(nameof(DistinctUnitCount));
        OnPropertyChanged(nameof(UnmappedUnitCount));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
