using System.Windows.Media;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Models;

/// <summary>One swatch row in the floating BC marker legend.</summary>
public sealed class BcMarkerLegendItem
{
    public BcMarkerLegendItem(BcSymbolKind kind, int count)
    {
        Kind = kind;
        Count = count;
        Label = BcSymbolCatalog.ShortLabel(kind);
        AnalysisCategory = BcSymbolCatalog.AnalysisFamily(kind);
        Fill = BcSymbolCatalog.GetFillBrush(kind);
    }

    public BcSymbolKind Kind { get; }
    public string Label { get; }
    public string AnalysisCategory { get; }
    public int Count { get; }
    public Brush Fill { get; }
    public string CountLabel => Count.ToString();
}
