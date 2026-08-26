using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GeotechSlopeApp.Models.Wave3;
using GeotechSlopeApp.Services.Wave3;

namespace GeotechSlopeApp.ViewModels;

public sealed class AnalysisCasesViewModel : INotifyPropertyChanged
{
    private MainWindowViewModel? _host;
    private FemAnalysisCase? _selectedCase;
    private string _statusMessage = "Add named cases (seismic / load factor), Apply to the live model, or Evaluate All from the last SRF.";

    public AnalysisCasesViewModel()
    {
        CaseSet = new FemAnalysisCaseSet();
        CaseSet.AddDefault();
        SelectedCase = CaseSet.Cases[0];
        AddCaseCommand = new RelayCommand(() => SelectedCase = CaseSet.AddDefault());
        RemoveCaseCommand = new RelayCommand(RemoveSelected, () => SelectedCase != null && CaseSet.Cases.Count > 1);
        ApplyCaseCommand = new RelayCommand(ApplySelected, () => _host != null && SelectedCase != null);
        EvaluateAllCommand = new RelayCommand(EvaluateAll, () => _host != null);
        CaptureCurrentCommand = new RelayCommand(CaptureCurrent, () => _host != null && SelectedCase != null);
    }

    public void BindHost(MainWindowViewModel host) => _host = host;

    public FemAnalysisCaseSet CaseSet { get; }

    public FemAnalysisCase? SelectedCase
    {
        get => _selectedCase;
        set
        {
            if (ReferenceEquals(_selectedCase, value)) return;
            _selectedCase = value;
            OnPropertyChanged();
            (RemoveCaseCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ApplyCaseCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CaptureCurrentCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { if (_statusMessage == value) return; _statusMessage = value; OnPropertyChanged(); }
    }

    public ICommand AddCaseCommand { get; }
    public ICommand RemoveCaseCommand { get; }
    public ICommand ApplyCaseCommand { get; }
    public ICommand EvaluateAllCommand { get; }
    public ICommand CaptureCurrentCommand { get; }

    private void RemoveSelected()
    {
        if (SelectedCase == null || CaseSet.Cases.Count <= 1) return;
        CaseSet.Cases.Remove(SelectedCase);
        SelectedCase = CaseSet.Cases[0];
    }

    private void ApplySelected()
    {
        if (_host == null || SelectedCase == null) return;
        _host.LoadConditionsVM.SeismicAnalysisEnabled = Math.Abs(SelectedCase.SeismicKh) > 1e-12
            || Math.Abs(SelectedCase.SeismicKv) > 1e-12;
        _host.LoadConditionsVM.SetScriptPseudoStaticCoefficients(SelectedCase.SeismicKh, SelectedCase.SeismicKv);
        StatusMessage = $"Applied '{SelectedCase.Name}' (k_h={SelectedCase.SeismicKh:G3}, k_v={SelectedCase.SeismicKv:G3}, load factor={SelectedCase.LoadFactor:G3}). Run FE Analysis to solve this case.";
    }

    private void EvaluateAll()
    {
        if (_host == null) return;
        if (!_host.TryGetFactorEvaluator(out var evaluator, out var error))
        {
            StatusMessage = error;
            return;
        }

        var n = FemAnalysisCaseRunner.EvaluateActive(
            CaseSet.Cases, _host.MaterialPropertiesVM.Materials.ToList(), evaluator);
        StatusMessage = $"Evaluated {n} active case(s) from the last SRF (first-order seismic / load-factor scale).";
    }

    private void CaptureCurrent()
    {
        if (_host == null || SelectedCase == null) return;
        var fos = _host.ResultsVM.EstimatedSrfFoS;
        SelectedCase.LastFoS = fos;
        SelectedCase.Status = fos is { } f && double.IsFinite(f) ? "Captured" : "No SRF";
        StatusMessage = fos is { } v
            ? $"Stored SRF {v:F3} on '{SelectedCase.Name}'."
            : "No SRF is available to capture.";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
