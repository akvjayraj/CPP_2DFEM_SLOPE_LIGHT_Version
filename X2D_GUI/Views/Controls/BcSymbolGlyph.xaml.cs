using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Views.Controls;

/// <summary>Compact RS2-style BC glyph for DataGrid Symbol column and canvas markers.</summary>
public partial class BcSymbolGlyph : UserControl
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind),
        typeof(BcSymbolKind),
        typeof(BcSymbolGlyph),
        new PropertyMetadata(BcSymbolKind.Fixed, OnKindChanged));

    public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
        nameof(FillBrush),
        typeof(Brush),
        typeof(BcSymbolGlyph),
        new PropertyMetadata(BcSymbolCatalog.GetFillBrush(BcSymbolKind.Fixed)));

    public static readonly DependencyProperty StrokeBrushProperty = DependencyProperty.Register(
        nameof(StrokeBrush),
        typeof(Brush),
        typeof(BcSymbolGlyph),
        new PropertyMetadata(BcSymbolCatalog.GetStrokeBrush(BcSymbolKind.Fixed)));

    public static readonly DependencyProperty HaloBrushProperty = DependencyProperty.Register(
        nameof(HaloBrush),
        typeof(Brush),
        typeof(BcSymbolGlyph),
        new PropertyMetadata(Brushes.White));

    public BcSymbolGlyph()
    {
        InitializeComponent();
        ApplyKindColors(Kind);
    }

    public BcSymbolKind Kind
    {
        get => (BcSymbolKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public Brush FillBrush
    {
        get => (Brush)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public Brush StrokeBrush
    {
        get => (Brush)GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    public Brush HaloBrush
    {
        get => (Brush)GetValue(HaloBrushProperty);
        set => SetValue(HaloBrushProperty, value);
    }

    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BcSymbolGlyph glyph)
            glyph.ApplyKindColors((BcSymbolKind)e.NewValue);
    }

    private void ApplyKindColors(BcSymbolKind kind)
    {
        FillBrush = BcSymbolCatalog.GetFillBrush(kind);
        StrokeBrush = BcSymbolCatalog.GetStrokeBrush(kind);
    }
}
