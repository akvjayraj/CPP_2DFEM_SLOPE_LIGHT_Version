using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Views.Dialogs;

public partial class AssignMaterialDialog : Window
{
    /// <summary>Set on OK: (displayLayerNumber, materialId).</summary>
    public (int displayLayerNumber, int materialId)? Result { get; private set; }

    public AssignMaterialDialog(
        IReadOnlyList<LayerCoordinateSummary> layers,
        IReadOnlyList<MaterialModel> materials,
        Func<int, int?> getAssignmentForDisplayLayer)
    {
        InitializeComponent();

        LayerComboBox.ItemsSource = layers;
        MaterialComboBox.ItemsSource = materials;

        LayerComboBox.SelectedIndex = layers.Count > 0 ? 0 : -1;
        MaterialComboBox.SelectedIndex = materials.Count > 0 ? 0 : -1;

        LayerComboBox.SelectionChanged += UpdateCurrentAssignment;
        UpdateCurrentAssignment(null, null);

        void UpdateCurrentAssignment(object? s, SelectionChangedEventArgs? e)
        {
            if (LayerComboBox.SelectedItem is not LayerCoordinateSummary layer)
            {
                CurrentAssignmentText.Text = "";
                return;
            }

            var id = getAssignmentForDisplayLayer(layer.DisplayLayerNumber);
            if (id == null)
            {
                CurrentAssignmentText.Text = "Current assignment: (none)";
                return;
            }

            var mat = materials.FirstOrDefault(m => m.Id == id.Value);
            CurrentAssignmentText.Text = mat != null
                ? $"Current assignment: {mat.Name}"
                : $"Current assignment: Material ID {id.Value}";
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (LayerComboBox.SelectedItem is not LayerCoordinateSummary layer) return;
        if (MaterialComboBox.SelectedItem is not MaterialModel mat) return;
        Result = (layer.DisplayLayerNumber, mat.Id);
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
