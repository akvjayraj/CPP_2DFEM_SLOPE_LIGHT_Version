using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models.Analysis;

public enum AnalysisIntent
{
    InitialStress,
    Static,
    Srf,
    Seepage,
    Consolidation,
    Dynamic,
    Thermal,
    Coupled
}

/// <summary>
/// Workspace analysis intent and SSR extras. Bound to existing FE / SRF / GW view-models;
/// unused failure-criteria flags stay visible but capability-gated.
/// </summary>
public sealed class AnalysisWorkspaceSettings : INotifyPropertyChanged
{
    private AnalysisIntent _intent = AnalysisIntent.Static;
    private double _initialStrengthReductionFactor = 1.0;
    private double _strengthReductionIncrement = 0.1;
    private bool _checkDisplacementJump = true;
    private bool _checkNonConvergence = true;
    private bool _checkPlasticZoneConnect = false;
    private string _porePressureOption = "None";
    private string _method = "Strength Reduction";
    private string _ssrType = "Global";
    private bool _updatePorePressure;
    private double _shearStrainLimitPercent = 5;
    private string _analysisNotes = "";
    private bool _autoSaveResults = true;
    private int _saveIntervalMinutes = 5;
    private int _keepLogDays = 7;

    public AnalysisIntent Intent
    {
        get => _intent;
        set
        {
            if (_intent == value) return;
            _intent = value;
            OnPropertyChanged();
        }
    }

    public double InitialStrengthReductionFactor
    {
        get => _initialStrengthReductionFactor;
        set
        {
            if (Math.Abs(_initialStrengthReductionFactor - value) < 1e-12) return;
            _initialStrengthReductionFactor = value;
            OnPropertyChanged();
        }
    }

    public double StrengthReductionIncrement
    {
        get => _strengthReductionIncrement;
        set
        {
            if (Math.Abs(_strengthReductionIncrement - value) < 1e-12) return;
            _strengthReductionIncrement = value;
            OnPropertyChanged();
        }
    }

    public bool CheckDisplacementJump
    {
        get => _checkDisplacementJump;
        set
        {
            if (_checkDisplacementJump == value) return;
            _checkDisplacementJump = value;
            OnPropertyChanged();
        }
    }

    public bool CheckNonConvergence
    {
        get => _checkNonConvergence;
        set
        {
            if (_checkNonConvergence == value) return;
            _checkNonConvergence = value;
            OnPropertyChanged();
        }
    }

    public bool CheckPlasticZoneConnect
    {
        get => _checkPlasticZoneConnect;
        set
        {
            if (_checkPlasticZoneConnect == value) return;
            _checkPlasticZoneConnect = value;
            OnPropertyChanged();
        }
    }

    /// <summary>None | Prescribed | Seepage — maps onto existing groundwater participation.</summary>
    public string PorePressureOption
    {
        get => _porePressureOption;
        set
        {
            if (_porePressureOption == value) return;
            _porePressureOption = value ?? "None";
            OnPropertyChanged();
        }
    }

    /// <summary>Strength Reduction is the live path. Limit Equilibrium is capability-gated.</summary>
    public string Method
    {
        get => _method;
        set
        {
            if (_method == value) return;
            _method = value ?? "Strength Reduction";
            OnPropertyChanged();
        }
    }

    /// <summary>Global is the live SSR path. Local is capability-gated.</summary>
    public string SsrType
    {
        get => _ssrType;
        set
        {
            if (_ssrType == value) return;
            _ssrType = value ?? "Global";
            OnPropertyChanged();
        }
    }

    public bool UpdatePorePressure
    {
        get => _updatePorePressure;
        set
        {
            if (_updatePorePressure == value) return;
            _updatePorePressure = value;
            OnPropertyChanged();
        }
    }

    public double ShearStrainLimitPercent
    {
        get => _shearStrainLimitPercent;
        set
        {
            if (Math.Abs(_shearStrainLimitPercent - value) < 1e-12) return;
            _shearStrainLimitPercent = value;
            OnPropertyChanged();
        }
    }

    public string AnalysisNotes
    {
        get => _analysisNotes;
        set
        {
            if (_analysisNotes == value) return;
            _analysisNotes = value ?? "";
            OnPropertyChanged();
        }
    }

    public bool AutoSaveResults
    {
        get => _autoSaveResults;
        set
        {
            if (_autoSaveResults == value) return;
            _autoSaveResults = value;
            OnPropertyChanged();
        }
    }

    public int SaveIntervalMinutes
    {
        get => _saveIntervalMinutes;
        set
        {
            if (_saveIntervalMinutes == value) return;
            _saveIntervalMinutes = value;
            OnPropertyChanged();
        }
    }

    public int KeepLogDays
    {
        get => _keepLogDays;
        set
        {
            if (_keepLogDays == value) return;
            _keepLogDays = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
