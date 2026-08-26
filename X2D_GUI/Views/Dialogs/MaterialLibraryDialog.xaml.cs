using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Views.Dialogs;

public partial class MaterialLibraryDialog : Window
{
    private readonly ObservableCollection<MaterialModel> _projectMaterials;
    private readonly List<MaterialModel> _allPresets;
    private int _nextId;

    public MaterialLibraryDialog(ObservableCollection<MaterialModel> projectMaterials, int nextId)
    {
        InitializeComponent();
        _projectMaterials = projectMaterials;
        _nextId           = nextId;
        _allPresets       = BuildPresets();
        LibraryGrid.ItemsSource = _allPresets;
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = FilterBox.Text.Trim();
        LibraryGrid.ItemsSource = string.IsNullOrEmpty(q)
            ? _allPresets
            : _allPresets.Where(m => m.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                  || m.Description.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void AddToProjectButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = LibraryGrid.SelectedItems.Cast<MaterialModel>().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Select at least one preset material.", "Add to Project",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        foreach (var preset in selected)
        {
            var copy  = preset.Clone();
            copy.Id   = _nextId++;
            _projectMaterials.Add(copy);
        }
        MessageBox.Show($"Added {selected.Count} material(s) to the project.", "Add to Project",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    // ── Built-in preset catalog ────────────────────────────────────────────────
    private static List<MaterialModel> BuildPresets() => new()
    {
        new MaterialModel { Name = "Loose Sand",           FailureCriterion = FailureCriterion.MohrCoulomb, MaterialType = MaterialType.Isotropic,
            UnitWeight = 17.0, YoungsModulus =  30_000, PoissonsRatio = 0.30, Cohesion =  0, FrictionAngle = 28, DilationAngle = 0,
            Colour = Color.FromRgb(210,180,140), Description = "Loose cohesionless sand" },

        new MaterialModel { Name = "Dense Sand",           FailureCriterion = FailureCriterion.MohrCoulomb, MaterialType = MaterialType.Isotropic,
            UnitWeight = 19.0, YoungsModulus =  80_000, PoissonsRatio = 0.28, Cohesion =  0, FrictionAngle = 38, DilationAngle = 8,
            Colour = Color.FromRgb( 70,130,180), Description = "Well-graded dense sand" },

        new MaterialModel { Name = "Soft Clay",            FailureCriterion = FailureCriterion.MohrCoulomb, MaterialType = MaterialType.Isotropic,
            UnitWeight = 16.0, YoungsModulus =   5_000, PoissonsRatio = 0.40, Cohesion = 10, FrictionAngle = 20, DilationAngle = 0,
            Colour = Color.FromRgb(144,238,144), Description = "Low plasticity soft clay (CL)" },

        new MaterialModel { Name = "Stiff Clay",           FailureCriterion = FailureCriterion.MohrCoulomb, MaterialType = MaterialType.Isotropic,
            UnitWeight = 19.0, YoungsModulus =  50_000, PoissonsRatio = 0.35, Cohesion = 50, FrictionAngle = 25, DilationAngle = 0,
            Colour = Color.FromRgb(188,143,143), Description = "Overconsolidated stiff clay (CH)" },

        new MaterialModel { Name = "Gravel",               FailureCriterion = FailureCriterion.MohrCoulomb, MaterialType = MaterialType.Isotropic,
            UnitWeight = 20.0, YoungsModulus = 100_000, PoissonsRatio = 0.25, Cohesion =  0, FrictionAngle = 40, DilationAngle = 5,
            Colour = Color.FromRgb(205,133, 63), Description = "Well-graded gravel (GW)" },

        new MaterialModel { Name = "Weathered Rock",       FailureCriterion = FailureCriterion.MohrCoulomb, MaterialType = MaterialType.Isotropic,
            UnitWeight = 22.0, YoungsModulus = 500_000, PoissonsRatio = 0.25, Cohesion = 30, FrictionAngle = 35, DilationAngle = 5,
            Colour = Color.FromRgb(189,183,107), Description = "Weathered / fractured rock" },

        new MaterialModel { Name = "Intact Rock",          FailureCriterion = FailureCriterion.HoekBrown,   MaterialType = MaterialType.Isotropic,
            UnitWeight = 26.0, YoungsModulus = 5_000_000, PoissonsRatio = 0.20,
            Ucs = 80, HoekBrownMi = 12, Gsi = 70, Disturbance = 0, ModulusRatio = 400,
            Colour = Color.FromRgb(112,128,144), Description = "Strong intact rock (sandstone)" },

        new MaterialModel { Name = "Jointed Rock Mass",    FailureCriterion = FailureCriterion.HoekBrown,   MaterialType = MaterialType.Isotropic,
            UnitWeight = 25.0, YoungsModulus = 1_000_000, PoissonsRatio = 0.22,
            Ucs = 50, HoekBrownMi = 10, Gsi = 45, Disturbance = 0.2, ModulusRatio = 300,
            Colour = Color.FromRgb( 64,224,208), Description = "Jointed / disturbed rock mass" },

        new MaterialModel { Name = "Peat / Organic",       FailureCriterion = FailureCriterion.MohrCoulomb, MaterialType = MaterialType.Isotropic,
            UnitWeight = 12.0, YoungsModulus =   1_000, PoissonsRatio = 0.45, Cohesion =  5, FrictionAngle = 10, DilationAngle = 0,
            Colour = Color.FromRgb( 46,139, 87), Description = "Highly organic peat / muskeg" },

        new MaterialModel { Name = "Concrete (structural)",FailureCriterion = FailureCriterion.Elastic,     MaterialType = MaterialType.Isotropic,
            UnitWeight = 24.0, YoungsModulus = 30_000_000, PoissonsRatio = 0.20,
            Colour = Color.FromRgb(100,149,237), Description = "Normal-strength concrete (C25/30)" },

        new MaterialModel { Name = "Steel",                FailureCriterion = FailureCriterion.ElastoPlastic, MaterialType = MaterialType.Isotropic,
            UnitWeight = 78.5, YoungsModulus = 200_000_000, PoissonsRatio = 0.30, Cohesion = 250_000, FrictionAngle = 0,
            Colour = Color.FromRgb(147,112,219), Description = "Structural steel (elastic-perfectly plastic, σ_y≈250 MPa)" },

        new MaterialModel { Name = "Compacted Fill",       FailureCriterion = FailureCriterion.MohrCoulomb, MaterialType = MaterialType.Isotropic,
            UnitWeight = 18.0, YoungsModulus =  40_000, PoissonsRatio = 0.30, Cohesion =  5, FrictionAngle = 30, DilationAngle = 0,
            Colour = Color.FromRgb(255,165,  0), Description = "Compacted structural fill" },

        new MaterialModel { Name = "Weak Fault Zone",      FailureCriterion = FailureCriterion.MohrCoulomb, MaterialType = MaterialType.Isotropic,
            UnitWeight = 20.0, YoungsModulus =  10_000, PoissonsRatio = 0.35, Cohesion =  2, FrictionAngle = 15, DilationAngle = 0,
            Colour = Color.FromRgb(220, 20, 60), Description = "Shear zone / fault gouge" },

        new MaterialModel { Name = "Silt",                 FailureCriterion = FailureCriterion.MohrCoulomb, MaterialType = MaterialType.Isotropic,
            UnitWeight = 17.5, YoungsModulus =  15_000, PoissonsRatio = 0.35, Cohesion =  5, FrictionAngle = 25, DilationAngle = 0,
            Colour = Color.FromRgb(245,245,220), Description = "Silty soil (ML)" },

        new MaterialModel { Name = "Elastic Isotropic",    FailureCriterion = FailureCriterion.Elastic,     MaterialType = MaterialType.Isotropic,
            UnitWeight = 20.0, YoungsModulus = 100_000, PoissonsRatio = 0.30,
            Colour = Color.FromRgb(173,216,230), Description = "Generic linear-elastic isotropic material" },
    };
}
