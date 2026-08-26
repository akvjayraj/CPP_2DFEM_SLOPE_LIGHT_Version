using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels.Dialogs;

namespace GeotechSlopeApp.Views.Dialogs;

public partial class AddMaterialDialog : Window
{
    public AddMaterialDialogViewModel ViewModel { get; }

    /// <summary>The committed material, set when the user clicks OK.</summary>
    public MaterialModel? Result { get; private set; }

    public AddMaterialDialog(AddMaterialDialogViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;

        // Wire palette selection → material colour
        ColourListBox.SelectionChanged += OnPaletteSelectionChanged;

        // Pre-select the swatch that matches the current colour (if any)
        var match = ViewModel.ColourPalette.FirstOrDefault(c => c == ViewModel.Material.Colour);
        if (match != default)
            ColourListBox.SelectedItem = match;
    }

    private void OnPaletteSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ColourListBox.SelectedItem is Color colour)
            ViewModel.Material.Colour = colour;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsValid) return;
        Result = ViewModel.Material;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var msg = MessageBox.Show(
            "Reset all fields to default values?",
            "Reset Defaults", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (msg != MessageBoxResult.OK) return;

        var m = ViewModel.Material;
        m.FailureCriterion = FailureCriterion.Elastic;
        m.MaterialType     = MaterialType.Isotropic;
        m.UnitWeight          = 20.0;
        m.SaturatedUnitWeight = 22.0;
        m.IsSaturatedEnabled  = false;
        m.YoungsModulus  = 100_000.0;
        m.PoissonsRatio  = 0.3;
        m.Cohesion       = 0.0;
        m.FrictionAngle  = 30.0;
        m.DilationAngle  = 0.0;
        m.TensileCutoff  = 0.0;
        m.Ucs            = 50.0;
        m.HoekBrownMi    = 10.0;
        m.Gsi            = 50.0;
        m.Disturbance    = 0.0;
        m.ModulusRatio   = 400.0;
        m.PermeabilityX  = 1e-6;
        m.PermeabilityY  = 1e-6;
        m.Porosity       = 0.30;
        m.InitialVoidRatio = 0.50;
        m.SpecificStorage = 1e-4;
        m.ThermalConductivityX = 2.0;
        m.ThermalConductivityY = 2.0;
        m.HeatCapacity   = 1000.0;
        m.ThermalExpansion = 1e-5;
        m.Density        = 2000.0;
        m.DampingRatio   = 0.05;
    }
}
