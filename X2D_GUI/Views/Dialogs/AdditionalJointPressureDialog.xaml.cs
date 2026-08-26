using System.Globalization;
using System.Windows;

namespace GeotechSlopeApp.Views.Dialogs;

public partial class AdditionalJointPressureDialog : Window
{
    public double PressureKpa { get; private set; }

    public AdditionalJointPressureDialog(double pressureKpa)
    {
        InitializeComponent();
        PressureKpa = pressureKpa;
        PressureBox.Text = pressureKpa.ToString("G10", CultureInfo.InvariantCulture);
        PressureBox.SelectAll();
        Loaded += (_, _) => PressureBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(PressureBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) &&
            !double.TryParse(PressureBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out v))
        {
            MessageBox.Show(this, "Enter a numeric pressure in kPa.", "Additional Pressure",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        PressureKpa = v;
        DialogResult = true;
        Close();
    }
}
