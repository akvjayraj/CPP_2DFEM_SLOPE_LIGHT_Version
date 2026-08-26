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

public sealed class BackAnalysisViewModel : INotifyPropertyChanged
{
    private MainWindowViewModel? _host;
    private string _statusMessage = "Run FE Analysis (SRF), then Run Back Analysis to match a target FoS.";
    private FemBackAnalysisRunner.Result? _lastResult;

    public BackAnalysisViewModel()
    {
        Settings = new FemBackAnalysisSettings();
        MaterialRows = new ObservableCollection<FemBackAnalysisRunner.MaterialRow>();
        IterationRows = new ObservableCollection<FemBackAnalysisRunner.IterationRow>();
        RunBackAnalysisCommand = new RelayCommand(Run, () => _host != null);
        ApplyToMaterialsCommand = new RelayCommand(Apply, () => LastResult != null);
        ExportCsvCommand = new RelayCommand(ExportCsv, () => LastResult != null);
    }

    public void BindHost(MainWindowViewModel host) => _host = host;

    public FemBackAnalysisSettings Settings { get; }
    public ObservableCollection<FemBackAnalysisRunner.MaterialRow> MaterialRows { get; }
    public ObservableCollection<FemBackAnalysisRunner.IterationRow> IterationRows { get; }

    public FemBackAnalysisRunner.Result? LastResult
    {
        get => _lastResult;
        private set
        {
            _lastResult = value;
            OnPropertyChanged();
            (ApplyToMaterialsCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExportCsvCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { if (_statusMessage == value) return; _statusMessage = value; OnPropertyChanged(); }
    }

    public ICommand RunBackAnalysisCommand { get; }
    public ICommand ApplyToMaterialsCommand { get; }
    public ICommand ExportCsvCommand { get; }

    private void Run()
    {
        if (_host == null) return;
        if (!_host.TryGetFactorEvaluator(out var evaluator, out var error))
        {
            StatusMessage = error;
            return;
        }

        LastResult = FemBackAnalysisRunner.Run(
            Settings, _host.MaterialPropertiesVM.Materials.ToList(), evaluator);
        MaterialRows.Clear();
        foreach (var r in LastResult.Materials)
            MaterialRows.Add(r);
        IterationRows.Clear();
        foreach (var r in LastResult.Iterations)
            IterationRows.Add(r);
        StatusMessage = LastResult.Converged
            ? $"Converged: F = {LastResult.Factor:F4}, SRF = {LastResult.AchievedFs:F3}."
            : $"Did not converge. Last F = {LastResult.Factor:F4}, SRF = {LastResult.AchievedFs:F3}.";
    }

    private void Apply()
    {
        if (_host == null || LastResult == null) return;
        FemBackAnalysisRunner.ApplyToMaterials(_host.MaterialPropertiesVM.Materials, LastResult);
        StatusMessage = "Back-figured c and φ written to the material library.";
    }

    private void ExportCsv()
    {
        if (LastResult == null) return;
        var dlg = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = "srf_back_analysis.csv",
        };
        if (dlg.ShowDialog() != true) return;
        File.WriteAllText(dlg.FileName, FemBackAnalysisRunner.BuildCsv(LastResult));
        MessageBox.Show($"Wrote {dlg.FileName}", "Export back analysis", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
