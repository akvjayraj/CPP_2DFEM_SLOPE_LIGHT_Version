using System.Windows;
using System.Windows.Documents;
using GeotechSlopeApp.Services.Print;

namespace GeotechSlopeApp.Views.Dialogs;

public partial class PrintPreviewWindow : Window
{
    private FixedDocument _document;
    private PrintSettings _settings;
    private readonly Action? _onPrint;
    private readonly Func<PrintSettings, FixedDocument?>? _rebuild;

    public PrintPreviewWindow(
        FixedDocument document,
        PrintSettings settings,
        Action? onPrint,
        Func<PrintSettings, FixedDocument?>? rebuild = null)
    {
        InitializeComponent();
        _document = document;
        _settings = settings;
        _onPrint = onPrint;
        _rebuild = rebuild;
        PreviewViewer.Document = _document;
    }

    private void OnPrintClick(object sender, RoutedEventArgs e)
    {
        _onPrint?.Invoke();
    }

    private void OnPageSetupClick(object sender, RoutedEventArgs e)
    {
        var dialog = new PageSetupDialog(_settings) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        _settings = dialog.Result;
        PrintSettingsStore.Save(_settings);

        var rebuilt = _rebuild?.Invoke(_settings);
        if (rebuilt is not null)
        {
            _document = rebuilt;
            PreviewViewer.Document = _document;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
