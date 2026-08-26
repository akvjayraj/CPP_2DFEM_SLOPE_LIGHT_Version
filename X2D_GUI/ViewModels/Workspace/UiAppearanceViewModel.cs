using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GeotechSlopeApp.Models.Appearance;
using GeotechSlopeApp.Services.Appearance;

namespace GeotechSlopeApp.ViewModels.Workspace;

public sealed class UiAppearanceViewModel : INotifyPropertyChanged
{
    private readonly UiAppearanceService _service;
    private readonly Action<bool>? _onLayoutChanged;
    private string _windowBackground;
    private string _windowForeground;
    private string _secondaryText;
    private string _accent;
    private string _viewportPaper;
    private string _grid;
    private UiAppearancePreset _preset;
    private string _statusMessage = "";

    public UiAppearanceViewModel(UiAppearanceService service, Action<bool>? onLayoutChanged = null)
    {
        _service = service;
        _onLayoutChanged = onLayoutChanged;
        var s = service.Settings;
        _preset = s.Preset;
        _windowBackground = s.WindowBackground;
        _windowForeground = s.WindowForeground;
        _secondaryText = s.SecondaryText;
        _accent = s.Accent;
        _viewportPaper = s.ViewportPaper;
        _grid = s.Grid;
        ApplyCommand = new RelayCommand(Apply);
        ResetPresetCommand = new RelayCommand(ResetPreset);
        ApplyPresetCommand = new RelayCommand<string>(name =>
        {
            if (Enum.TryParse<UiAppearancePreset>(name, out var preset))
                SelectPreset(preset);
        });
    }

    public IReadOnlyList<UiAppearancePreset> Presets { get; } =
        Enum.GetValues<UiAppearancePreset>();

    public UiAppearancePreset Preset
    {
        get => _preset;
        set
        {
            if (_preset == value) return;
            _preset = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsClassicLight));
            OnPropertyChanged(nameof(IsLight));
            OnPropertyChanged(nameof(IsDark));
            OnPropertyChanged(nameof(IsCustom));
            if (value != UiAppearancePreset.Custom)
                LoadPresetColors(value);
        }
    }

    public bool IsClassicLight
    {
        get => Preset == UiAppearancePreset.ClassicLight;
        set { if (value) Preset = UiAppearancePreset.ClassicLight; }
    }

    public bool IsLight
    {
        get => Preset == UiAppearancePreset.Light;
        set { if (value) Preset = UiAppearancePreset.Light; }
    }

    public bool IsDark
    {
        get => Preset == UiAppearancePreset.Dark;
        set { if (value) Preset = UiAppearancePreset.Dark; }
    }

    public bool IsCustom
    {
        get => Preset == UiAppearancePreset.Custom;
        set { if (value) Preset = UiAppearancePreset.Custom; }
    }

    public bool UseAnalysisWorkspace
    {
        get => _service.Settings.LayoutMode == UiLayoutMode.AnalysisWorkspace;
        set
        {
            var next = value ? UiLayoutMode.AnalysisWorkspace : UiLayoutMode.ClassicTabs;
            if (_service.Settings.LayoutMode == next)
                return;
            _service.Settings.LayoutMode = next;
            _service.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(UseClassicTabs));
            _onLayoutChanged?.Invoke(value);
        }
    }

    public bool UseClassicTabs
    {
        get => !UseAnalysisWorkspace;
        set { if (value) UseAnalysisWorkspace = false; }
    }

    public void NotifyLayoutBindings()
    {
        OnPropertyChanged(nameof(UseAnalysisWorkspace));
        OnPropertyChanged(nameof(UseClassicTabs));
    }

    public string WindowBackground { get => _windowBackground; set => SetColor(ref _windowBackground, value); }
    public string WindowForeground { get => _windowForeground; set => SetColor(ref _windowForeground, value); }
    public string SecondaryText { get => _secondaryText; set => SetColor(ref _secondaryText, value); }
    public string Accent { get => _accent; set => SetColor(ref _accent, value); }
    public string ViewportPaper { get => _viewportPaper; set => SetColor(ref _viewportPaper, value); }
    public string Grid { get => _grid; set => SetColor(ref _grid, value); }

    public bool HasPoorContrast => _service.HasPoorContrast;
    public string ContrastWarning => _service.ContrastWarning;
    public string StatusMessage { get => _statusMessage; private set { _statusMessage = value; OnPropertyChanged(); } }

    public ICommand ApplyCommand { get; }
    public ICommand ResetPresetCommand { get; }
    public ICommand ApplyPresetCommand { get; }

    public void Apply()
    {
        var next = _service.Settings.Clone();
        next.Preset = Preset;
        next.WindowBackground = WindowBackground;
        next.WindowForeground = WindowForeground;
        next.SecondaryText = SecondaryText;
        next.Accent = Accent;
        next.ViewportPaper = ViewportPaper;
        next.Grid = Grid;
        UiAppearancePalettes.SyncPanelChrome(next);
        _service.ReplaceSettings(next);
        OnPropertyChanged(nameof(HasPoorContrast));
        OnPropertyChanged(nameof(ContrastWarning));
        StatusMessage = HasPoorContrast ? ContrastWarning : "Appearance applied.";
        _onLayoutChanged?.Invoke(_service.Settings.LayoutMode == UiLayoutMode.AnalysisWorkspace);
    }

    public void ResetPreset()
    {
        var keep = Preset == UiAppearancePreset.Custom ? UiAppearancePreset.ClassicLight : Preset;
        SelectPreset(keep);
        Apply();
        StatusMessage = "Reset to preset colors.";
    }

    private void SelectPreset(UiAppearancePreset preset)
    {
        Preset = preset;
        LoadPresetColors(preset);
        _service.ApplyPreset(preset);
        SyncFromService();
        OnPropertyChanged(nameof(HasPoorContrast));
        OnPropertyChanged(nameof(ContrastWarning));
        StatusMessage = HasPoorContrast ? ContrastWarning : $"Preset: {preset}";
        _onLayoutChanged?.Invoke(_service.Settings.LayoutMode == UiLayoutMode.AnalysisWorkspace);
    }

    private void LoadPresetColors(UiAppearancePreset preset)
    {
        var src = preset switch
        {
            UiAppearancePreset.Light => UiAppearancePalettes.Light(),
            UiAppearancePreset.Dark => UiAppearancePalettes.Dark(),
            UiAppearancePreset.Custom => _service.Settings,
            _ => UiAppearancePalettes.ClassicLight()
        };
        _windowBackground = src.WindowBackground;
        _windowForeground = src.WindowForeground;
        _secondaryText = src.SecondaryText;
        _accent = src.Accent;
        _viewportPaper = src.ViewportPaper;
        _grid = src.Grid;
        OnPropertyChanged(nameof(WindowBackground));
        OnPropertyChanged(nameof(WindowForeground));
        OnPropertyChanged(nameof(SecondaryText));
        OnPropertyChanged(nameof(Accent));
        OnPropertyChanged(nameof(ViewportPaper));
        OnPropertyChanged(nameof(Grid));
    }

    private void SyncFromService()
    {
        var s = _service.Settings;
        _preset = s.Preset;
        _windowBackground = s.WindowBackground;
        _windowForeground = s.WindowForeground;
        _secondaryText = s.SecondaryText;
        _accent = s.Accent;
        _viewportPaper = s.ViewportPaper;
        _grid = s.Grid;
        OnPropertyChanged(nameof(Preset));
        OnPropertyChanged(nameof(IsClassicLight));
        OnPropertyChanged(nameof(IsLight));
        OnPropertyChanged(nameof(IsDark));
        OnPropertyChanged(nameof(IsCustom));
        OnPropertyChanged(nameof(WindowBackground));
        OnPropertyChanged(nameof(WindowForeground));
        OnPropertyChanged(nameof(SecondaryText));
        OnPropertyChanged(nameof(Accent));
        OnPropertyChanged(nameof(ViewportPaper));
        OnPropertyChanged(nameof(Grid));
    }

    private void SetColor(ref string field, string value)
    {
        if (field == value) return;
        field = value;
        if (Preset != UiAppearancePreset.Custom)
        {
            _preset = UiAppearancePreset.Custom;
            OnPropertyChanged(nameof(Preset));
            OnPropertyChanged(nameof(IsClassicLight));
            OnPropertyChanged(nameof(IsLight));
            OnPropertyChanged(nameof(IsDark));
            OnPropertyChanged(nameof(IsCustom));
        }
        OnPropertyChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
