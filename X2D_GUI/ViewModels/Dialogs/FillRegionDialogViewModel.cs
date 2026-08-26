using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.ViewModels.Dialogs;

public sealed class FillRegionItemViewModel : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public string DisplayName { get; init; } = "";
    public int RegionIndex { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Dialog: choose which subtracted (Region B) outlines to mark as filled on the sketch.
/// </summary>
public sealed class FillRegionDialogViewModel : INotifyPropertyChanged
{
    public ObservableCollection<FillRegionItemViewModel> Items { get; } = new();

    /// <param name="row">The Subtract excavation row; <paramref name="g"/>.<see cref="GeometryDefinitionViewModel.RegionBPolygons"/> must already be built for this row.</param>
    public FillRegionDialogViewModel(GeometryDefinitionViewModel g, BooleanOperationRow row)
    {
        if (row == null || !row.IsComplete)
        {
            return;
        }

        if (row.TypeEnum != BooleanOperationType.Subtract)
        {
            return;
        }

        if (row.IsMultiRegion && row.ParticipatingSurfaceNumbers.Count >= 2)
        {
            for (var i = 1; i < row.ParticipatingSurfaceNumbers.Count; i++)
            {
                var polyIdx = i - 1;
                if (polyIdx >= g.RegionBPolygons.Count)
                {
                    break;
                }

                var n = row.ParticipatingSurfaceNumbers[i];
                var s = g.GeometrySurfaces.FirstOrDefault(x => x.SurfaceNumber == n);
                var name = s == null ? $"S{n}" : (string.IsNullOrWhiteSpace(s.Tag) ? $"S{n}" : s.Tag.Trim());
                Items.Add(new FillRegionItemViewModel { DisplayName = name, RegionIndex = polyIdx });
            }
        }
        else
        {
            if (g.RegionBPolygons.Count == 0)
            {
                return;
            }

            var name = string.IsNullOrWhiteSpace(row.RegionBName) ? $"S{row.RegionBSurfaceNumber}" : row.RegionBName.Trim();
            var sb = g.GeometrySurfaces.FirstOrDefault(x => x.SurfaceNumber == row.RegionBSurfaceNumber);
            if (sb != null && !string.IsNullOrWhiteSpace(sb.Tag))
            {
                name = sb.Tag.Trim();
            }

            Items.Add(new FillRegionItemViewModel { DisplayName = name, RegionIndex = 0 });
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
