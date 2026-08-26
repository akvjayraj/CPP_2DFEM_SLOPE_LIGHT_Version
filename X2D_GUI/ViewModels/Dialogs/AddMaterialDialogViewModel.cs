using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.ViewModels.Dialogs;

/// <summary>
/// ViewModel for the Add / Edit Material dialog.
/// Drives conditional visibility of parameter groups based on FailureCriterion and MaterialType,
/// and auto-computes derived stiffness values (G, K) for display.
/// </summary>
public class AddMaterialDialogViewModel : INotifyPropertyChanged
{
    // ── Public material being edited ──────────────────────────────────────────
    public MaterialModel Material { get; }

    // ── Enum option lists ─────────────────────────────────────────────────────
    public FailureCriterion[] FailureCriterionOptions => Enum.GetValues<FailureCriterion>();
    public MaterialType[]     MaterialTypeOptions     => Enum.GetValues<MaterialType>();

    // ── Colour palette (RS2 / Plaxis style) ───────────────────────────────────
    public IReadOnlyList<Color> ColourPalette { get; } = new[]
    {
        Color.FromRgb(173, 216, 230), // light blue      – sand
        Color.FromRgb(210, 180, 140), // tan             – fill
        Color.FromRgb(144, 238, 144), // light green     – soft clay
        Color.FromRgb(205, 133,  63), // peru            – gravel
        Color.FromRgb(188, 143, 143), // rosy brown      – stiff clay
        Color.FromRgb(112, 128, 144), // slate gray      – rock
        Color.FromRgb( 70, 130, 180), // steel blue      – dense sand
        Color.FromRgb(189, 183, 107), // dark khaki      – weathered rock
        Color.FromRgb(240, 128, 128), // light coral     – weak zone
        Color.FromRgb(147, 112, 219), // medium purple   – lining
        Color.FromRgb(100, 149, 237), // cornflower blue – concrete
        Color.FromRgb(255, 165,   0), // orange          – structural fill
        Color.FromRgb( 46, 139,  87), // sea green       – peat
        Color.FromRgb(220,  20,  60), // crimson         – fault zone
        Color.FromRgb(245, 245, 220), // beige           – backfill
        Color.FromRgb( 64, 224, 208), // turquoise       – water
    };

    // ── Conditional visibility ────────────────────────────────────────────────
    public bool IsMohrCoulomb    => Material.FailureCriterion == FailureCriterion.MohrCoulomb;
    public bool IsElastoPlastic  => Material.FailureCriterion == FailureCriterion.ElastoPlastic;
    public bool IsMCOrEP         => IsMohrCoulomb || IsElastoPlastic;
    public bool IsHoekBrown      => Material.FailureCriterion is FailureCriterion.HoekBrown
                                                               or FailureCriterion.GeneralizedHoekBrown;
    public bool IsDruckerPrager  => Material.FailureCriterion == FailureCriterion.DruckerPrager;
    public bool IsElastic        => Material.FailureCriterion == FailureCriterion.Elastic;
    public bool HasStrength      => !IsElastic;

    public bool IsIsotropic           => Material.MaterialType == MaterialType.Isotropic;
    public bool IsTransverseIsotropic => Material.MaterialType == MaterialType.TransverselyIsotropic;
    public bool IsOrthotropicOrMore   => Material.MaterialType is MaterialType.Orthotropic
                                                               or MaterialType.DuncanChangHyperbolic;

    // ── Auto-computed stiffness (read-only display) ───────────────────────────
    public string ShearModulusInfo
    {
        get
        {
            if (!IsIsotropic || Material.PoissonsRatio <= -1) return "";
            var G = Material.YoungsModulus / (2 * (1 + Material.PoissonsRatio));
            return $"{G:G5} kPa  (auto: E / 2(1+ν))";
        }
    }

    public string BulkModulusInfo
    {
        get
        {
            var denom = 3 * (1 - 2 * Material.PoissonsRatio);
            if (!IsIsotropic || Math.Abs(denom) < 1e-9) return "";
            var K = Material.YoungsModulus / denom;
            return $"{K:G5} kPa  (auto: E / 3(1−2ν))";
        }
    }

    // ── Validation ────────────────────────────────────────────────────────────
    public bool IsValid      => !string.IsNullOrWhiteSpace(Material.Name);
    public string? NameError => string.IsNullOrWhiteSpace(Material.Name) ? "Name is required." : null;

    // ── Constructor ──────────────────────────────────────────────────────────
    public AddMaterialDialogViewModel() : this(new MaterialModel()) { }

    public AddMaterialDialogViewModel(MaterialModel material)
    {
        Material = material;
        Material.PropertyChanged += OnMaterialPropertyChanged;
    }

    private void OnMaterialPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MaterialModel.FailureCriterion):
                OnPropertyChanged(nameof(IsMohrCoulomb));
                OnPropertyChanged(nameof(IsElastoPlastic));
                OnPropertyChanged(nameof(IsMCOrEP));
                OnPropertyChanged(nameof(IsHoekBrown));
                OnPropertyChanged(nameof(IsDruckerPrager));
                OnPropertyChanged(nameof(IsElastic));
                OnPropertyChanged(nameof(HasStrength));
                break;
            case nameof(MaterialModel.MaterialType):
                OnPropertyChanged(nameof(IsIsotropic));
                OnPropertyChanged(nameof(IsTransverseIsotropic));
                OnPropertyChanged(nameof(IsOrthotropicOrMore));
                OnPropertyChanged(nameof(ShearModulusInfo));
                OnPropertyChanged(nameof(BulkModulusInfo));
                break;
            case nameof(MaterialModel.YoungsModulus):
            case nameof(MaterialModel.PoissonsRatio):
                OnPropertyChanged(nameof(ShearModulusInfo));
                OnPropertyChanged(nameof(BulkModulusInfo));
                break;
            case nameof(MaterialModel.Name):
                OnPropertyChanged(nameof(IsValid));
                OnPropertyChanged(nameof(NameError));
                break;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
