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

public sealed class SensitivityAnalysisViewModel : INotifyPropertyChanged
{
    private MainWindowViewModel? _host;
    private string _statusMessage = "Run FE Analysis (SRF) first, then Run Sensitivity.";
    private string _parametricStatusMessage = "Select a parameter and sweep range, then Run Parametric Sweep.";
    private FemSensitivityRunner.ParamKey? _selectedParameter;
    private double _parametricStartValue;
    private double _parametricEndValue = 1.0;
    private double _parametricIncrement = 0.1;

    public SensitivityAnalysisViewModel()
    {
        Settings = new FemSensitivitySettings();
        Results = new ObservableCollection<FemSensitivityRunner.ResultRow>();
        ParametricResults = new ObservableCollection<FemSensitivityRunner.ResultRow>();
        AvailableParameters = new ObservableCollection<FemSensitivityRunner.ParamKey>();
        RunSensitivityCommand = new RelayCommand(RunSensitivity, () => _host != null);
        RunParametricAnalysisCommand = new RelayCommand(RunParametric, CanRunParametric);
        ExportTornadoCsvCommand = new RelayCommand(ExportTornadoCsv, () => Results.Count > 0);
    }

    public void BindHost(MainWindowViewModel host)
    {
        _host = host;
        RefreshParameters();
    }

    public FemSensitivitySettings Settings { get; }
    public ObservableCollection<FemSensitivityRunner.ResultRow> Results { get; }
    public ObservableCollection<FemSensitivityRunner.ResultRow> ParametricResults { get; }
    public ObservableCollection<FemSensitivityRunner.ParamKey> AvailableParameters { get; }

    public FemSensitivityRunner.ParamKey? SelectedParameter
    {
        get => _selectedParameter;
        set
        {
            if (ReferenceEquals(_selectedParameter, value)) return;
            _selectedParameter = value;
            OnPropertyChanged();
            if (value != null)
            {
                ParametricStartValue = value.BaseValue * 0.8;
                ParametricEndValue = value.BaseValue * 1.2;
                ParametricIncrement = Math.Max(Math.Abs(value.BaseValue) * 0.05, 0.01);
            }
            (RunParametricAnalysisCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public double ParametricStartValue
    {
        get => _parametricStartValue;
        set { if (Math.Abs(_parametricStartValue - value) < 1e-15) return; _parametricStartValue = value; OnPropertyChanged(); }
    }

    public double ParametricEndValue
    {
        get => _parametricEndValue;
        set { if (Math.Abs(_parametricEndValue - value) < 1e-15) return; _parametricEndValue = value; OnPropertyChanged(); }
    }

    public double ParametricIncrement
    {
        get => _parametricIncrement;
        set { if (Math.Abs(_parametricIncrement - value) < 1e-15) return; _parametricIncrement = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { if (_statusMessage == value) return; _statusMessage = value; OnPropertyChanged(); }
    }

    public string ParametricStatusMessage
    {
        get => _parametricStatusMessage;
        set { if (_parametricStatusMessage == value) return; _parametricStatusMessage = value; OnPropertyChanged(); }
    }

    public ICommand RunSensitivityCommand { get; }
    public ICommand RunParametricAnalysisCommand { get; }
    public ICommand ExportTornadoCsvCommand { get; }

    public void RefreshParameters()
    {
        AvailableParameters.Clear();
        if (_host == null) return;
        foreach (var p in FemSensitivityRunner.BuildParameterList(_host.MaterialPropertiesVM.Materials))
            AvailableParameters.Add(p);
        if (AvailableParameters.Count > 0 && SelectedParameter == null)
            SelectedParameter = AvailableParameters[0];
    }

    private bool CanRunParametric() =>
        _host != null && SelectedParameter != null
        && double.IsFinite(ParametricStartValue) && double.IsFinite(ParametricEndValue)
        && double.IsFinite(ParametricIncrement) && Math.Abs(ParametricIncrement) > 1e-12;

    private void RunSensitivity()
    {
        if (_host == null) return;
        RefreshParameters();
        if (!_host.TryGetFactorEvaluator(out var evaluator, out var error))
        {
            StatusMessage = error;
            Results.Clear();
            return;
        }

        var rows = FemSensitivityRunner.RunOneAtATime(
            Settings, _host.MaterialPropertiesVM.Materials.ToList(), evaluator);
        Results.Clear();
        foreach (var r in rows)
            Results.Add(r);
        StatusMessage = $"Sensitivity complete: {rows.Count} evaluations (first-order SRF scale from last run).";
        (ExportTornadoCsvCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void RunParametric()
    {
        if (_host == null || SelectedParameter == null) return;
        if (!_host.TryGetFactorEvaluator(out var evaluator, out var error))
        {
            ParametricStatusMessage = error;
            return;
        }

        var rows = FemSensitivityRunner.RunParametric(
            SelectedParameter, ParametricStartValue, ParametricEndValue, ParametricIncrement,
            _host.MaterialPropertiesVM.Materials.ToList(), evaluator);
        ParametricResults.Clear();
        foreach (var r in rows)
            ParametricResults.Add(r);
        ParametricStatusMessage = $"Sweep complete: {rows.Count} steps.";
    }

    private void ExportTornadoCsv()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = "srf_sensitivity.csv",
        };
        if (dlg.ShowDialog() != true) return;
        File.WriteAllText(dlg.FileName, FemSensitivityRunner.BuildTornadoCsv(Results));
        MessageBox.Show($"Wrote {dlg.FileName}", "Export tornado CSV", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
