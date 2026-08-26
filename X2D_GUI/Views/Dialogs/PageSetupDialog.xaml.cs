using System.Globalization;
using System.Windows;
using GeotechSlopeApp.Services.Print;

namespace GeotechSlopeApp.Views.Dialogs;

public partial class PageSetupDialog : Window
{
    private readonly PrintSettings _settings;

    public PageSetupDialog(PrintSettings settings)
    {
        InitializeComponent();
        _settings = settings.Clone();

        OrientationCombo.ItemsSource = new[] { "Portrait", "Landscape" };
        OrientationCombo.SelectedItem = _settings.Orientation == PrintPageOrientation.Portrait ? "Portrait" : "Landscape";

        MarginLeftBox.Text = _settings.MarginLeftMm.ToString(CultureInfo.InvariantCulture);
        MarginRightBox.Text = _settings.MarginRightMm.ToString(CultureInfo.InvariantCulture);
        MarginTopBox.Text = _settings.MarginTopMm.ToString(CultureInfo.InvariantCulture);
        MarginBottomBox.Text = _settings.MarginBottomMm.ToString(CultureInfo.InvariantCulture);

        FitToPageRadio.IsChecked = _settings.ScaleMode == PrintScaleMode.FitToPage;
        ActualSizeRadio.IsChecked = _settings.ScaleMode == PrintScaleMode.ActualSize;

        ShowHeaderCheck.IsChecked = _settings.ShowHeader;
        ShowFooterCheck.IsChecked = _settings.ShowFooter;
        HeaderFormatBox.Text = _settings.HeaderFormat;
        FooterFormatBox.Text = _settings.FooterFormat;
    }

    public PrintSettings Result { get; private set; } = new();

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (!TryParseMargins(out var left, out var right, out var top, out var bottom))
        {
            MessageBox.Show(this, "Enter valid numeric margins (mm).", "Page Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.Orientation = OrientationCombo.SelectedItem as string == "Portrait"
            ? PrintPageOrientation.Portrait
            : PrintPageOrientation.Landscape;
        _settings.MarginLeftMm = left;
        _settings.MarginRightMm = right;
        _settings.MarginTopMm = top;
        _settings.MarginBottomMm = bottom;
        _settings.ScaleMode = ActualSizeRadio.IsChecked == true ? PrintScaleMode.ActualSize : PrintScaleMode.FitToPage;
        _settings.ShowHeader = ShowHeaderCheck.IsChecked == true;
        _settings.ShowFooter = ShowFooterCheck.IsChecked == true;
        _settings.HeaderFormat = HeaderFormatBox.Text.Trim();
        _settings.FooterFormat = FooterFormatBox.Text.Trim();

        Result = _settings;
        DialogResult = true;
    }

    private bool TryParseMargins(out double left, out double right, out double top, out double bottom)
    {
        left = right = top = bottom = 0;
        return double.TryParse(MarginLeftBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out left)
               && double.TryParse(MarginRightBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out right)
               && double.TryParse(MarginTopBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out top)
               && double.TryParse(MarginBottomBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out bottom)
               && left >= 0 && right >= 0 && top >= 0 && bottom >= 0;
    }
}
