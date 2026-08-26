using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Views.Dialogs;

public partial class SnapSettingsDialog : Window
{
    private readonly GeometrySnapSettings _settings;
    private readonly SnapSettingsDialogModel _model;

    public SnapSettingsDialog(GeometrySnapSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _model = new SnapSettingsDialogModel(settings);
        DataContext = _model;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!_model.TryApply(_settings)) return;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

internal sealed class SnapSettingsDialogModel : INotifyPropertyChanged
{
    private bool _snapEnabled;
    private bool _gridSnap;
    private bool _vertexSnap;
    private bool _midpointSnap;
    private bool _intersectionSnap;
    private bool _edgeSnap;
    private bool _orthoEnabled;
    private string _gridSpacingText;
    private string _toleranceText;
    private string _orthoStepText;
    private string _errorMessage = "";

    public SnapSettingsDialogModel(GeometrySnapSettings settings)
    {
        _snapEnabled = settings.SnapEnabled;
        _gridSnap = settings.GridSnap;
        _vertexSnap = settings.VertexSnap;
        _midpointSnap = settings.MidpointSnap;
        _intersectionSnap = settings.IntersectionSnap;
        _edgeSnap = settings.EdgeSnap;
        _orthoEnabled = settings.OrthoEnabled;
        _gridSpacingText = settings.GridSnapSpacing.ToString("G6", CultureInfo.CurrentCulture);
        _toleranceText = settings.SnapTolerancePixels.ToString("G4", CultureInfo.CurrentCulture);
        _orthoStepText = settings.OrthoAngleStepDegrees.ToString("G4", CultureInfo.CurrentCulture);
    }

    public bool SnapEnabled { get => _snapEnabled; set { _snapEnabled = value; OnPropertyChanged(); } }
    public bool GridSnap { get => _gridSnap; set { _gridSnap = value; OnPropertyChanged(); } }
    public bool VertexSnap { get => _vertexSnap; set { _vertexSnap = value; OnPropertyChanged(); } }
    public bool MidpointSnap { get => _midpointSnap; set { _midpointSnap = value; OnPropertyChanged(); } }
    public bool IntersectionSnap { get => _intersectionSnap; set { _intersectionSnap = value; OnPropertyChanged(); } }
    public bool EdgeSnap { get => _edgeSnap; set { _edgeSnap = value; OnPropertyChanged(); } }
    public bool OrthoEnabled { get => _orthoEnabled; set { _orthoEnabled = value; OnPropertyChanged(); } }

    public string GridSpacingText { get => _gridSpacingText; set { _gridSpacingText = value ?? ""; OnPropertyChanged(); } }
    public string ToleranceText { get => _toleranceText; set { _toleranceText = value ?? ""; OnPropertyChanged(); } }
    public string OrthoStepText { get => _orthoStepText; set { _orthoStepText = value ?? ""; OnPropertyChanged(); } }

    public string ErrorMessage { get => _errorMessage; private set { _errorMessage = value ?? ""; OnPropertyChanged(); } }

    public bool TryApply(GeometrySnapSettings settings)
    {
        if (!TryParse(GridSpacingText, out var spacing) || spacing <= 0)
        {
            ErrorMessage = "Grid spacing must be a positive number.";
            return false;
        }

        if (!TryParse(ToleranceText, out var tolerance) || tolerance < 2 || tolerance > 60)
        {
            ErrorMessage = "Snap tolerance must be between 2 and 60 pixels.";
            return false;
        }

        if (!TryParse(OrthoStepText, out var step) || step < 1 || step > 90)
        {
            ErrorMessage = "The ortho angle step must be between 1 and 90 degrees.";
            return false;
        }

        settings.SnapEnabled = SnapEnabled;
        settings.GridSnap = GridSnap;
        settings.GridSnapSpacing = spacing;
        settings.VertexSnap = VertexSnap;
        settings.MidpointSnap = MidpointSnap;
        settings.IntersectionSnap = IntersectionSnap;
        settings.EdgeSnap = EdgeSnap;
        settings.SnapTolerancePixels = tolerance;
        settings.OrthoEnabled = OrthoEnabled;
        settings.OrthoAngleStepDegrees = step;

        ErrorMessage = "";
        return true;
    }

    private static bool TryParse(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
        || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
