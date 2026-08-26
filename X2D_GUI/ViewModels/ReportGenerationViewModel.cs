using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using GeotechSlopeApp.Models.Wave3;
using GeotechSlopeApp.Services.Wave3;
using Microsoft.Win32;

namespace GeotechSlopeApp.ViewModels;

public class ReportGenerationViewModel : INotifyPropertyChanged
{
    private MainWindowViewModel? _host;
    private string _reportPreview = "Generate a preview after an SRF run.";

    public ReportGenerationViewModel()
    {
        Options = new FemReportOptions();
        GenerateReportCommand = new RelayCommand(GeneratePreview);
        ExportPdfCommand = new RelayCommand(ExportPdf);
    }

    public void BindHost(MainWindowViewModel host) => _host = host;

    public FemReportOptions Options { get; }

    public string ReportPreview
    {
        get => _reportPreview;
        set { if (_reportPreview == value) return; _reportPreview = value; OnPropertyChanged(); }
    }

    public ICommand GenerateReportCommand { get; }
    public ICommand ExportPdfCommand { get; }

    public FemReportSnapshot BuildSnapshot()
    {
        var host = _host;
        return new FemReportSnapshot
        {
            Options = Options.Clone(),
            ProjectName = host?.ProjectSettingsVM.Project.ProjectName ?? "",
            Author = host?.ProjectSettingsVM.Project.Author ?? "",
            Units = host?.ProjectSettingsVM.Project.Units ?? "SI",
            EstimatedSrf = host?.ResultsVM.EstimatedSrfFoS,
            Materials = host?.MaterialPropertiesVM.Materials.ToList() ?? [],
            GroundwaterSummary = host == null
                ? ""
                : $"Static water surfaces: {host.ProjectSettingsVM.GroundwaterStaticSettings.IncludeWaterSurfaces}; Ru: {host.ProjectSettingsVM.GroundwaterStaticSettings.IncludeRuCoefficients}.",
            ValidationSummary = host == null
                ? ""
                : $"Model health: {host.ModelHealthVM.CriticalCount} critical, {host.ModelHealthVM.WarningCount} warning, {host.ModelHealthVM.InfoCount} info.",
            SensitivityRows = host?.SensitivityAnalysisVM.Results.ToList() ?? [],
            Probabilistic = host?.ProbabilisticAnalysisVM.Results,
            BackAnalysis = host?.BackAnalysisVM.LastResult,
            AnalysisCases = host?.AnalysisCasesVM.CaseSet.Cases.ToList() ?? [],
        };
    }

    private void GeneratePreview()
        => ReportPreview = FemReportBuilder.BuildMarkdown(BuildSnapshot());

    private void ExportPdf()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            FileName = "fe_slope_report.pdf",
        };
        if (dlg.ShowDialog() != true) return;
        var snap = BuildSnapshot();
        var md = FemReportBuilder.BuildMarkdown(snap);
        ReportPreview = md;
        FemPdfReportExporter.Export(dlg.FileName, md, snap);
        MessageBox.Show($"Wrote {dlg.FileName} and {Path.ChangeExtension(dlg.FileName, ".md")}",
            "Export report", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
