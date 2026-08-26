using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using GeotechSlopeApp.Models.Wave3;
using GeotechSlopeApp.Services.Wave3;
using Microsoft.Win32;

namespace GeotechSlopeApp.ViewModels;

public sealed class ProbabilisticAnalysisViewModel : INotifyPropertyChanged
{
    private MainWindowViewModel? _host;
    private string _statusMessage = "Enable Project Settings → Probabilistic, mark material RVs, then Run Probabilistic.";
    private FemProbabilisticResults? _results;
    private FemProbabilisticEngine _selectedEngine = FemProbabilisticEngine.LatinHypercube;

    public ProbabilisticAnalysisViewModel()
    {
        Settings = new FemProbabilisticSettings();
        AvailableEngines = new ObservableCollection<FemProbabilisticEngine>
        {
            FemProbabilisticEngine.LatinHypercube,
            FemProbabilisticEngine.MonteCarlo,
            FemProbabilisticEngine.PointEstimate,
        };
        RunProbabilisticCommand = new RelayCommand(Run, () => _host != null);
        ExportCsvCommand = new RelayCommand(ExportCsv, () => Results != null && Results.ValidSampleCount > 0);
    }

    public void BindHost(MainWindowViewModel host) => _host = host;

    public FemProbabilisticSettings Settings { get; }
    public ObservableCollection<FemProbabilisticEngine> AvailableEngines { get; }

    public FemProbabilisticEngine SelectedEngine
    {
        get => _selectedEngine;
        set
        {
            if (_selectedEngine == value) return;
            _selectedEngine = value;
            Settings.Engine = value;
            OnPropertyChanged();
        }
    }

    public FemProbabilisticResults? Results
    {
        get => _results;
        private set
        {
            _results = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SummaryText));
            (ExportCsvCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string SummaryText => Results?.FormatSummary() ?? "No probabilistic results yet.";

    public string StatusMessage
    {
        get => _statusMessage;
        set { if (_statusMessage == value) return; _statusMessage = value; OnPropertyChanged(); }
    }

    public ICommand RunProbabilisticCommand { get; }
    public ICommand ExportCsvCommand { get; }

    private void Run()
    {
        if (_host == null) return;
        Settings.Engine = SelectedEngine;
        if (!_host.ProjectSettingsVM.IsProbabilisticAnalysis)
        {
            StatusMessage = "Enable Project Settings → Analysis Type → Probabilistic first.";
            return;
        }

        if (!_host.TryGetFactorEvaluator(out var evaluator, out var error))
        {
            StatusMessage = error;
            return;
        }

        var materials = _host.MaterialPropertiesVM.Materials.ToList();
        if (!materials.Any(m => m.EnableProbabilisticVariation && m.PropertyStatistics.Values.Any(s => s.IsActiveRv)))
        {
            StatusMessage = "No active material random variables. Enable probabilistic variation on a material and mark c, φ, or γ as an RV.";
            return;
        }

        Results = FemProbabilisticRunner.Run(Settings, materials, evaluator);
        StatusMessage = $"Probabilistic complete: {Results.ValidSampleCount}/{Results.SampleCount} valid samples.";
    }

    private void ExportCsv()
    {
        if (Results == null) return;
        var dlg = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = "srf_probabilistic_samples.csv",
        };
        if (dlg.ShowDialog() != true) return;
        File.WriteAllText(dlg.FileName, FemProbabilisticRunner.BuildSampleCsv(Results));
        MessageBox.Show($"Wrote {dlg.FileName}", "Export samples", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
