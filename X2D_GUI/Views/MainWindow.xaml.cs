using System;
using System.Collections.Generic;
using System.IO;
using GeotechSlopeApp.ViewModels.Workspace;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Services.Print;
using GeotechSlopeApp.ViewModels;
using GeotechSlopeApp.ViewModels.Dialogs;
using GeotechSlopeApp.Views.Dialogs;
using GeotechSlopeApp.Views.Tabs;
using GeotechSlopeApp.Views.Controls;
using GeotechSlopeApp.Views.Workspace;

namespace GeotechSlopeApp.Views;

public partial class MainWindow : Window
{
    private readonly Dictionary<int, Window> _rightPanelWindows = new();
    private readonly Dictionary<int, Window> _materialToolbarPopupWindows = new();
    private readonly Dictionary<MainWindowViewModel.LoadToolbarPopupKind, Window> _loadToolbarPopupWindows = new();
    private readonly Dictionary<MainWindowViewModel.MeshToolbarPopupKind, Window> _meshToolbarPopupWindows = new();
    private Window? _bcImportBoundariesPopupWindow;
    private int _previousTabIndex = -1;
    private PrintSettings _printSettings = PrintSettingsStore.Load();

    /// <summary>Select Script Editor tab before project reload (automation: avoids right-panel popups).</summary>
    public void PrepareForAutomationProjectLoad()
    {
        CloseAllRightPanelWindows();
        _previousTabIndex = 12;
        MainTabs.SelectedIndex = 12;
    }

    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainWindowViewModel();
        DataContext = vm;

        // Wire the Add / Edit Material dialog factory
        vm.MaterialPropertiesVM.RequestAddMaterialDialog = dialogVm =>
        {
            var dlg = new AddMaterialDialog(dialogVm) { Owner = this };
            return dlg.ShowDialog();
        };

        // Wire the Assign Material to Layer dialog factory
        vm.MaterialPropertiesVM.RequestAssignMaterialDialog = (layers, materials) =>
        {
            var dlg = new AssignMaterialDialog(layers, materials, n => vm.GeometryDefinitionVM.GetLayerMaterialAssignmentMaterialId(n))
            {
                Owner = this
            };
            return dlg.ShowDialog() == true ? dlg.Result : null;
        };

        // Wire the Material Library window
        vm.MaterialPropertiesVM.RequestMaterialLibraryWindow = () =>
        {
            var dlg = new MaterialLibraryDialog(
                vm.MaterialPropertiesVM.Materials,
                vm.MaterialPropertiesVM.Materials.Count == 0 ? 1
                    : vm.MaterialPropertiesVM.Materials.Max(m => m.Id) + 1)
            { Owner = this };
            dlg.ShowDialog();
        };

        // Wire the Tunnel Wizard dialog factory (Tools → Tunnel Wizard).
        vm.RequestTunnelWizardDialog = wizardVm =>
        {
            var dlg = new TunnelWizardDialog(wizardVm) { Owner = this };
            return dlg.ShowDialog() == true && dlg.ApplyConfirmed;
        };

        vm.RequestJointWizardDialog = wizardVm =>
        {
            var dlg = new JointWizardDialog(wizardVm) { Owner = this };
            if (dlg.ShowDialog() != true)
                return JointWizardDialogResult.Cancelled;
            if (dlg.ManualDrawConfirmed)
                return JointWizardDialogResult.ManualDraw;
            return dlg.ApplyConfirmed ? JointWizardDialogResult.Applied : JointWizardDialogResult.Cancelled;
        };

        vm.RequestDefineJointPropertiesDialog = editorVm =>
        {
            var dlg = new DefineJointPropertiesDialog(editorVm) { Owner = this };
            return dlg.ShowDialog() == true && dlg.Confirmed;
        };

        vm.RequestPrint = PrintCurrentView;
        vm.RequestPrintPreview = ShowPrintPreview;
        vm.RequestPageSetup = ShowPageSetup;
        vm.RequestOpenMaterialPopupSubtab = subtabIndex => OpenMaterialPopupSubtab(subtabIndex, vm);
        vm.RequestOpenLoadToolbarPopup = kind => OpenLoadToolbarPopup(kind, vm);
        vm.RequestOpenMeshToolbarPopup = kind => OpenMeshToolbarPopup(kind, vm);
        vm.RequestOpenBcImportBoundariesPopup = () => OpenBcImportBoundariesPopup(vm);
        vm.ZoomInCanvases = () => ZoomInMenuItem_Click(null!, new RoutedEventArgs());
        vm.ZoomOutCanvases = () => ZoomOutMenuItem_Click(null!, new RoutedEventArgs());
        vm.ZoomToFitCanvases = () => ZoomToFitMenuItem_Click(null!, new RoutedEventArgs());
        vm.ResetZoomCanvases = () => ResetZoomMenuItem_OnClick(null!, new RoutedEventArgs());
        vm.RequestPreferencesDialog = () =>
        {
            var dlg = new PreferencesWindow
            {
                Owner = this,
                DataContext = vm.AppearanceVM
            };
            dlg.ShowDialog();
        };
        vm.RequestAnalysisTypeWizardDialog = () =>
        {
            var wizard = new AnalysisTypeWizardViewModel(vm);
            var dlg = new AnalysisTypeWizardDialog(wizard) { Owner = this };
            dlg.ShowDialog();
        };
        vm.RequestProjectSettingsDialog = _ => ShowProjectSettingsDialog(vm);

        Closed += OnMainWindowClosed;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainWindowViewModel.UseAnalysisWorkspace))
                ApplyWorkspaceRibbonHeight();
        };
        Loaded += (_, _) =>
        {
            _previousTabIndex = MainTabs.SelectedIndex;
            InitializeRightPanel();
            ApplyWorkspaceRibbonHeight();
        };
    }

    private void ApplyWorkspaceRibbonHeight()
    {
        if (DataContext is not MainWindowViewModel vm)
            return;
        WorkspaceRibbonRow.Height = vm.UseAnalysisWorkspace
            ? GridLength.Auto
            : new GridLength(0);
    }

    private void InitializeRightPanel()
    {
        if (DataContext is not MainWindowViewModel viewModel || MainTabs.SelectedIndex < 1)
        {
            return;
        }

        OpenRightPanelWindow(MainTabs.SelectedIndex, viewModel);
    }

    private void MainTabs_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not TabControl tabControl || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        // ComboBox, DataGrid, and nested TabControl raise SelectionChanged that bubbles
        // here. Handling those as a main-tab change flips the workspace to Materials.
        if (e.OriginalSource is not TabControl and not TabItem)
            return;
        if (e.OriginalSource is TabControl inner && !ReferenceEquals(inner, tabControl))
            return;

        var newIndex = tabControl.SelectedIndex;

        if (newIndex < 1)
        {
            if (_previousTabIndex == 2)
            {
                viewModel.MaterialPropertiesVM.GeometryDefinition.UseLayerNamesOnMaterialCanvas = false;
                viewModel.MaterialPropertiesVM.GeometryDefinition.CopyGeometryTo(viewModel.GeometryDefinitionVM);
            }
            _previousTabIndex = newIndex;
            CloseAllRightPanelWindows();
            return;
        }

        if (newIndex == 12)
        {
            _previousTabIndex = newIndex;
            CloseAllRightPanelWindows();
            return;
        }

        if (_previousTabIndex == 2 && newIndex != 2)
        {
            viewModel.MaterialPropertiesVM.GeometryDefinition.UseLayerNamesOnMaterialCanvas = false;
            viewModel.MaterialPropertiesVM.GeometryDefinition.CopyGeometryTo(viewModel.GeometryDefinitionVM);
        }

        if (newIndex == 2)
        {
            viewModel.MaterialPropertiesVM.GeometryDefinition.CopyGeometryFrom(viewModel.GeometryDefinitionVM);
            viewModel.MaterialPropertiesVM.GeometryDefinition.UseLayerNamesOnMaterialCanvas = true;
            viewModel.MaterialPropertiesVM.SelectedLayerSummaryForMaterials = null;
            viewModel.MaterialPropertiesVM.SynchronizeMaterialAssignmentCanvasPreview();
        }

        _previousTabIndex = newIndex;
        OpenRightPanelWindow(newIndex, viewModel);
    }

    private void OpenRightPanelWindow(int tabIndex, MainWindowViewModel viewModel)
    {
        if (!IsLoaded || tabIndex == 12)
            return;

        // Workspace chrome owns explorer / table / inspector. Classic tab popups
        // (especially Material Properties) must not stay over Geometry.
        if (viewModel.UseAnalysisWorkspace)
        {
            CloseAllRightPanelWindows();
            return;
        }

        if (_rightPanelWindows.TryGetValue(tabIndex, out var existingWindow))
        {
            if (!existingWindow.IsVisible)
            {
                if (IsLoaded)
                    existingWindow.Owner = this;
                PositionWindowBottomRight(existingWindow);
                if (tabIndex == 2)
                {
                    viewModel.MaterialPropertiesVM.GeometryDefinition.CopyGeometryFrom(viewModel.GeometryDefinitionVM);
                    viewModel.MaterialPropertiesVM.GeometryDefinition.UseLayerNamesOnMaterialCanvas = true;
                    viewModel.MaterialPropertiesVM.SelectedLayerSummaryForMaterials = null;
                    viewModel.MaterialPropertiesVM.SynchronizeMaterialAssignmentCanvasPreview();
                }
                if (tabIndex == 7)
                    viewModel.FEAnalysisVM.RefreshResolvedRunRoutingDisplay();
                existingWindow.Show();
            }

            existingWindow.Activate();
            CloseOtherRightPanelWindows(tabIndex);
            return;
        }

        var window = CreateRightPanelWindow(tabIndex, viewModel);
        if (window is null)
        {
            return;
        }

        if (IsLoaded)
            window.Owner = this;
        window.Tag = tabIndex;
        window.Closed += OnRightPanelWindowClosed;
        _rightPanelWindows[tabIndex] = window;
        CloseOtherRightPanelWindows(tabIndex);
        if (tabIndex == 7)
            viewModel.FEAnalysisVM.RefreshResolvedRunRoutingDisplay();
        window.Show();
    }

    private void OpenMaterialPopupSubtab(int subtabIndex, MainWindowViewModel viewModel)
    {
        var preservedMainTabIndex = MainTabs.SelectedIndex;
        viewModel.MaterialPropertiesVM.SelectedMaterialSubtabIndex = subtabIndex;
        viewModel.MaterialPropertiesVM.GeometryDefinition.CopyGeometryFrom(viewModel.GeometryDefinitionVM);
        viewModel.MaterialPropertiesVM.GeometryDefinition.UseLayerNamesOnMaterialCanvas = true;
        viewModel.MaterialPropertiesVM.SelectedLayerSummaryForMaterials = null;
        viewModel.MaterialPropertiesVM.SynchronizeMaterialAssignmentCanvasPreview();

        if (_materialToolbarPopupWindows.TryGetValue(subtabIndex, out var existingWindow))
        {
            if (!existingWindow.IsVisible)
            {
                existingWindow.Owner = this;
                PositionWindowBottomRight(existingWindow);
                existingWindow.Show();
            }

            existingWindow.Activate();
            RestoreMainTabSelection(preservedMainTabIndex);
            return;
        }

        var window = CreateMaterialToolbarPopupWindow(subtabIndex, viewModel);
        window.Owner = this;
        window.Tag = subtabIndex;
        window.Closed += OnMaterialToolbarPopupClosed;
        _materialToolbarPopupWindows[subtabIndex] = window;
        PositionWindowBottomRight(window);
        window.Show();
        RestoreMainTabSelection(preservedMainTabIndex);
    }

    private static Window CreateMaterialToolbarPopupWindow(int subtabIndex, MainWindowViewModel viewModel)
    {
        var title = subtabIndex switch
        {
            MaterialPropertiesViewModel.LayeringControlsSubtabIndex => "Layering Controls",
            MaterialPropertiesViewModel.ProjectMaterialsSubtabIndex => "Project Materials",
            MaterialPropertiesViewModel.LayerMaterialAssignmentSubtabIndex => "Layer-Material Assignment",
            _ => "Material Properties"
        };

        return new Window
        {
            Title = title,
            Content = new MaterialPropertiesView
            {
                DataContext = viewModel.MaterialPropertiesVM,
                ShowCanvas = false,
                FocusedSubtabIndex = subtabIndex
            },
            Width = 600,
            Height = 700,
            WindowStartupLocation = WindowStartupLocation.Manual
        };
    }

    private void OpenLoadToolbarPopup(MainWindowViewModel.LoadToolbarPopupKind kind, MainWindowViewModel viewModel)
    {
        var preservedMainTabIndex = MainTabs.SelectedIndex;
        viewModel.SelectedTabIndex = 4;

        if (_loadToolbarPopupWindows.TryGetValue(kind, out var existingWindow))
        {
            if (!existingWindow.IsVisible)
            {
                existingWindow.Owner = this;
                PositionWindowBottomRight(existingWindow);
                existingWindow.Show();
            }

            existingWindow.Activate();
            RestoreMainTabSelection(preservedMainTabIndex);
            return;
        }

        var window = CreateLoadToolbarPopupWindow(kind, viewModel);
        window.Owner = this;
        window.Tag = kind;
        window.Closed += OnLoadToolbarPopupClosed;
        _loadToolbarPopupWindows[kind] = window;
        PositionWindowBottomRight(window);
        window.Show();
        RestoreMainTabSelection(preservedMainTabIndex);
    }

    private static Window CreateLoadToolbarPopupWindow(
        MainWindowViewModel.LoadToolbarPopupKind kind,
        MainWindowViewModel viewModel)
    {
        var (title, section, width, height) = kind switch
        {
            MainWindowViewModel.LoadToolbarPopupKind.FieldStress =>
                ("Field Stress", LoadConditionsFocusedSection.FieldStress, 600d, 700d),
            MainWindowViewModel.LoadToolbarPopupKind.SeismicLoad =>
                ("Seismic Load", LoadConditionsFocusedSection.SeismicLoad, 600d, 420d),
            MainWindowViewModel.LoadToolbarPopupKind.Loads =>
                ("Loads", LoadConditionsFocusedSection.Loads, 900d, 700d),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        return new Window
        {
            Title = title,
            Content = new LoadConditionsRightPanelView
            {
                DataContext = viewModel.LoadConditionsVM,
                FocusedSection = section
            },
            Width = width,
            Height = height,
            WindowStartupLocation = WindowStartupLocation.Manual
        };
    }

    private void OpenMeshToolbarPopup(MainWindowViewModel.MeshToolbarPopupKind kind, MainWindowViewModel viewModel)
    {
        var preservedMainTabIndex = MainTabs.SelectedIndex;
        viewModel.SelectedTabIndex = 3;

        if (_meshToolbarPopupWindows.TryGetValue(kind, out var existingWindow))
        {
            if (!existingWindow.IsVisible)
            {
                existingWindow.Owner = this;
                PositionWindowBottomRight(existingWindow);
                existingWindow.Show();
            }

            existingWindow.Activate();
            RestoreMainTabSelection(preservedMainTabIndex);
            return;
        }

        var window = CreateMeshToolbarPopupWindow(kind, viewModel);
        window.Owner = this;
        window.Tag = kind;
        window.Closed += OnMeshToolbarPopupClosed;
        _meshToolbarPopupWindows[kind] = window;
        PositionWindowBottomRight(window);
        window.Show();
        RestoreMainTabSelection(preservedMainTabIndex);
    }

    private static Window CreateMeshToolbarPopupWindow(
        MainWindowViewModel.MeshToolbarPopupKind kind,
        MainWindowViewModel viewModel)
    {
        UserControl content;
        string title;
        double width = 600;
        double height = 700;

        switch (kind)
        {
            case MainWindowViewModel.MeshToolbarPopupKind.Information:
                content = new MeshInformationPopupView { DataContext = viewModel.MeshVM };
                title = "Mesh Information";
                width = 900;
                height = 560;
                break;
            case MainWindowViewModel.MeshToolbarPopupKind.Unstructured:
                viewModel.MeshVM.MeshPopupSelectedTabIndex = MeshViewModel.MeshPopupRs2dTabIndex;
                content = new Rs2dMeshSetupView { DataContext = viewModel.MeshVM };
                title = "Unstructured Mesh";
                break;
            case MainWindowViewModel.MeshToolbarPopupKind.Structured:
                viewModel.MeshVM.MeshPopupSelectedTabIndex = MeshViewModel.MeshPopupUserDefinedTabIndex;
                content = new UserDefinedMeshView { DataContext = viewModel.MeshVM.UserDefinedMeshVM };
                title = "Structured Mesh";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        return new Window
        {
            Title = title,
            Content = content,
            Width = width,
            Height = height,
            WindowStartupLocation = WindowStartupLocation.Manual
        };
    }

    private void OpenBcImportBoundariesPopup(MainWindowViewModel viewModel)
    {
        var preservedMainTabIndex = MainTabs.SelectedIndex;
        viewModel.SelectedTabIndex = 5;

        if (_bcImportBoundariesPopupWindow != null)
        {
            if (!_bcImportBoundariesPopupWindow.IsVisible)
            {
                _bcImportBoundariesPopupWindow.Owner = this;
                PositionWindowBottomRight(_bcImportBoundariesPopupWindow);
                _bcImportBoundariesPopupWindow.Show();
            }

            _bcImportBoundariesPopupWindow.Activate();
            RestoreMainTabSelection(preservedMainTabIndex);
            return;
        }

        var window = new Window
        {
            Title = "Apply BC to Outer Boundary",
            Content = new BoundaryConditionsImportPopupView { DataContext = viewModel.BoundaryConditionsVM },
            Width = 900,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Owner = this
        };
        window.Closed += OnBcImportBoundariesPopupClosed;
        _bcImportBoundariesPopupWindow = window;
        PositionWindowBottomRight(window);
        window.Show();
        RestoreMainTabSelection(preservedMainTabIndex);
    }

    private void OnBcImportBoundariesPopupClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        window.Closed -= OnBcImportBoundariesPopupClosed;
        _bcImportBoundariesPopupWindow = null;
    }

    private void RestoreMainTabSelection(int preservedMainTabIndex)
    {
        if (MainTabs.SelectedIndex != preservedMainTabIndex)
        {
            MainTabs.SelectedIndex = preservedMainTabIndex;
        }
    }

    private Window? CreateRightPanelWindow(int tabIndex, MainWindowViewModel viewModel)
    {
        // Each tab opens only its own popup - no tab should open popups from other tabs
        // Tab indices: 0=Project Settings, 1=Geometry Definition, 2=Material Properties,
        // 3=Mesh, 4=Load Conditions, 5=Boundary conditions, 6=Groundwater, 7=FE Analysis,
        // 8=Verification, 9=Results Visualization, 10=Sensitivity & Probabilistic, 11=Report Generation, 12=Script Editor
        
        UserControl? content = null;
        string title = string.Empty;

        switch (tabIndex)
        {
            case 1: // Geometry Definition tab -> Geometry Definition Details popup
                content = new GeometryDefinitionDetailsView { DataContext = viewModel.GeometryDefinitionVM };
                title = "Geometry Definition Details";
                break;
            case 2: // Material Properties tab -> Material Properties popup
                content = new MaterialPropertiesView 
                { 
                    DataContext = viewModel.MaterialPropertiesVM,
                    ShowCanvas = false
                };
                title = "Material Properties";
                break;
            case 3: // Mesh tab -> Mesh popup
                content = new MeshPopupView { DataContext = viewModel.MeshVM };
                title = "Mesh";
                break;
            case 4: // Load Conditions tab -> Load Conditions popup
                content = new LoadConditionsRightPanelView { DataContext = viewModel.LoadConditionsVM };
                title = "Load Conditions";
                break;
            case 5: // Boundary conditions tab -> Boundary Conditions Details popup
                content = new BoundaryConditionsDetailsView { DataContext = viewModel.BoundaryConditionsVM };
                title = "Boundary Conditions";
                break;
            case 6: // Groundwater tab — details are docked in GroundwaterView (no floating popup)
                return null;
            case 7: // FE Analysis tab -> FE Analysis Execution Details popup
                content = new FEAnalysisExecutionDetailsView { DataContext = viewModel.FEAnalysisVM };
                title = "FE Analysis Execution Details";
                break;
            case 8: // Verification tab -> no popup
                return null;
            case 9: // Results Visualization tab -> no popup
                return null;
            case 10: // Sensitivity & Probabilistic tab -> Sensitivity & Probabilistic popup
                content = new SensitivityProbabilisticView { DataContext = viewModel };
                title = "Sensitivity & Probabilistic";
                break;
            case 11: // Report Generation tab -> Report Generation popup
                content = new ReportGenerationView { DataContext = viewModel.ReportGenerationVM };
                title = "Report Generation";
                break;
            case 12: // Script Editor tab -> no popup
                return null;
            default:
                // Unknown tab index - no popup should open
                return null;
        }

        if (content is null)
        {
            return null;
        }

        var window = new Window
        {
            Title = title,
            Content = content,
            Width = 600,
            Height = 700,
            Owner = IsLoaded ? this : null,
            WindowStartupLocation = WindowStartupLocation.Manual
        };

        if (tabIndex == 2)
        {
            window.Loaded += (_, _) =>
            {
                viewModel.MaterialPropertiesVM.GeometryDefinition.CopyGeometryFrom(viewModel.GeometryDefinitionVM);
                viewModel.MaterialPropertiesVM.GeometryDefinition.UseLayerNamesOnMaterialCanvas = true;
                viewModel.MaterialPropertiesVM.SelectedLayerSummaryForMaterials = null;
                viewModel.MaterialPropertiesVM.SynchronizeMaterialAssignmentCanvasPreview();
            };
        }

        PositionWindowBottomRight(window);
        return window;
    }

    private void PositionWindowBottomRight(Window window)
    {
        if (this.WindowState == WindowState.Maximized)
        {
            var workingArea = SystemParameters.WorkArea;
            window.Left = workingArea.Right - window.Width - 16;
            window.Top = workingArea.Bottom - window.Height - 16;
        }
        else
        {
            window.Left = this.Left + this.Width - window.Width - 16;
            window.Top = this.Top + this.Height - window.Height - 16;
        }
    }

    private void CloseAllRightPanelWindows()
    {
        foreach (var window in _rightPanelWindows.Values.ToList())
        {
            window.Closed -= OnRightPanelWindowClosed;
            window.Close();
        }

        _rightPanelWindows.Clear();
        CloseAllMaterialToolbarPopupWindows();
        CloseAllLoadToolbarPopupWindows();
        CloseAllMeshToolbarPopupWindows();
    }

    private void CloseAllMaterialToolbarPopupWindows()
    {
        foreach (var window in _materialToolbarPopupWindows.Values.ToList())
        {
            window.Closed -= OnMaterialToolbarPopupClosed;
            window.Close();
        }

        _materialToolbarPopupWindows.Clear();
    }

    private void CloseAllLoadToolbarPopupWindows()
    {
        foreach (var window in _loadToolbarPopupWindows.Values.ToList())
        {
            window.Closed -= OnLoadToolbarPopupClosed;
            window.Close();
        }

        _loadToolbarPopupWindows.Clear();
    }

    private void CloseAllMeshToolbarPopupWindows()
    {
        foreach (var window in _meshToolbarPopupWindows.Values.ToList())
        {
            window.Closed -= OnMeshToolbarPopupClosed;
            window.Close();
        }

        _meshToolbarPopupWindows.Clear();
    }

    private void CloseOtherRightPanelWindows(int activeTabIndex)
    {
        foreach (var kvp in _rightPanelWindows.ToList())
        {
            if (kvp.Key == activeTabIndex)
            {
                continue;
            }

            kvp.Value.Closed -= OnRightPanelWindowClosed;
            kvp.Value.Close();
            _rightPanelWindows.Remove(kvp.Key);
        }
    }

    private void OnRightPanelWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        window.Closed -= OnRightPanelWindowClosed;

        if (window.Tag is int tabIndex && tabIndex == 2 && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.MaterialPropertiesVM.GeometryDefinition.CopyGeometryTo(viewModel.GeometryDefinitionVM);
            viewModel.GeometryDefinitionVM.SetMaterialAssignmentSurfacePreview(null, null);
        }

        if (window.Tag is int idx)
        {
            _rightPanelWindows.Remove(idx);
        }
    }

    private void OnMaterialToolbarPopupClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        window.Closed -= OnMaterialToolbarPopupClosed;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.MaterialPropertiesVM.GeometryDefinition.CopyGeometryTo(viewModel.GeometryDefinitionVM);
            viewModel.GeometryDefinitionVM.SetMaterialAssignmentSurfacePreview(null, null);
        }

        if (window.Tag is int subtabIndex)
        {
            _materialToolbarPopupWindows.Remove(subtabIndex);
        }
    }

    private void OnLoadToolbarPopupClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        window.Closed -= OnLoadToolbarPopupClosed;
        if (window.Tag is MainWindowViewModel.LoadToolbarPopupKind kind)
        {
            _loadToolbarPopupWindows.Remove(kind);
        }
    }

    private void OnMeshToolbarPopupClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        window.Closed -= OnMeshToolbarPopupClosed;
        if (window.Tag is MainWindowViewModel.MeshToolbarPopupKind kind)
        {
            _meshToolbarPopupWindows.Remove(kind);
        }
    }

    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        CloseAllRightPanelWindows();
    }

    private void ResetZoomMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var canvasView in FindVisualChildren<SlopeCanvasView>(this))
        {
            canvasView.ResetView();
        }
        foreach (var contourView in FindVisualChildren<FEContourCanvasView>(this))
        {
            contourView.ResetView();
        }
    }

    private void PrintCurrentView()
    {
        if (!TryBuildPrintDocument(out var document))
            return;

        CanvasPrintService.ShowPrintDialog(document);
    }

    private void ShowPrintPreview()
    {
        if (!TryBuildPrintDocument(out var document))
            return;

        var preview = new PrintPreviewWindow(
            document,
            _printSettings,
            PrintCurrentView,
            settings =>
            {
                _printSettings = settings;
                return TryBuildPrintDocument(out var rebuilt) ? rebuilt : null;
            })
        {
            Owner = this
        };
        preview.ShowDialog();
    }

    private void ShowPageSetup()
    {
        var dialog = new PageSetupDialog(_printSettings) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        _printSettings = dialog.Result;
        PrintSettingsStore.Save(_printSettings);
    }

    private bool TryBuildPrintDocument(out FixedDocument document)
    {
        document = null!;
        var tabContent = MainTabs.SelectedContent as DependencyObject;
        var canvas = CanvasPrintService.TryResolveCanvas(tabContent);
        if (canvas is null)
        {
            MessageBox.Show(
                "Nothing to print on this tab. Open Geometry, Mesh, or Results view.",
                "Print",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        document = CanvasPrintService.BuildFixedDocument(
            canvas,
            _printSettings,
            GetPrintProjectName(),
            GetPrintTabName());
        return true;
    }

    private string GetPrintProjectName()
    {
        if (DataContext is MainWindowViewModel vm && !string.IsNullOrWhiteSpace(vm.CurrentXfemProjectPath))
            return Path.GetFileNameWithoutExtension(vm.CurrentXfemProjectPath);

        return "Untitled Project";
    }

    private string GetPrintTabName()
    {
        if (MainTabs.SelectedItem is TabItem { Header: { } header })
            return header.ToString() ?? "View";

        return "View";
    }

    // ── View menu ──────────────────────────────────────────────────────────────
    private void ZoomInMenuItem_Click(object sender, RoutedEventArgs e)
    {
        foreach (var canvas in FindVisualChildren<SlopeCanvasView>(this))
            canvas.ZoomBy(1.25);
    }

    private void ZoomOutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        foreach (var canvas in FindVisualChildren<SlopeCanvasView>(this))
            canvas.ZoomBy(0.8);
    }

    private void ZoomToFitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        foreach (var canvas in FindVisualChildren<SlopeCanvasView>(this))
            canvas.ResetView();
        foreach (var contour in FindVisualChildren<FEContourCanvasView>(this))
            contour.ResetView();
    }

    private void ToggleFullScreenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    // ── Window menu ────────────────────────────────────────────────────────────
    private void CloseAllPopupsMenuItem_Click(object sender, RoutedEventArgs e)
        => CloseAllRightPanelWindows();

    // ── Generic stub ──────────────────────────────────────────────────────────
    private void StubMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var header = (sender as MenuItem)?.Header?.ToString() ?? "This feature";
        MessageBox.Show($"{header} is not yet implemented.", header, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ProjectSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel mainViewModel)
            return;
        var projectSettingsVM = mainViewModel.ProjectSettingsVM;
        projectSettingsVM.RefreshStagedAnalysisBindings();
        if (mainViewModel.FEAnalysisVM.StagedExcavationEnabled)
            projectSettingsVM.SelectSettingsTab(ProjectSettingsTabKind.AnalysisType);
        else
            projectSettingsVM.SelectSettingsTab(ProjectSettingsTabKind.GridAxes);
        ShowProjectSettingsDialog(mainViewModel);
    }

    private void ShowProjectSettingsDialog(MainWindowViewModel mainViewModel)
    {
        var projectSettingsVM = mainViewModel.ProjectSettingsVM;
        var popup = new Window
        {
            Title = "Project Settings",
            Owner = this,
            DataContext = mainViewModel,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 720,
            Height = 620,
            MinWidth = 560,
            MinHeight = 480,
            Content = new ProjectSettingsView { DataContext = projectSettingsVM }
        };
        popup.ShowDialog();
        mainViewModel.SyncFooterWithStagedAnalysisGrid();
    }

    private void NavigateToMainTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string tagString && int.TryParse(tagString, out int tabIndex))
        {
            if (DataContext is MainWindowViewModel mainViewModel)
            {
                // Set the selected tab index to navigate to the main tab
                mainViewModel.SelectedTabIndex = tabIndex;
            }
        }
    }

    private void WorkspaceFooterTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel mainViewModel)
            return;
        if (mainViewModel.ProjectWorkspace.IsSwitching)
            return;
        if (WorkspaceFooterTabsControl.SelectedIndex is int idx && idx >= 0)
            mainViewModel.ProjectWorkspace.SwitchTo(mainViewModel, idx);
    }

    private void StageFooterTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel mainViewModel)
            mainViewModel.RestoreCurrentStageFromStageFooter();
    }

    private void CloseWorkspaceTab_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not ProjectWorkspaceSlot slot)
            return;
        if (DataContext is not MainWindowViewModel vm)
            return;

        var index = vm.ProjectWorkspace.Slots.IndexOf(slot);
        if (index < 0)
            return;

        if (index != vm.ProjectWorkspace.ActiveIndex)
            vm.ProjectWorkspace.SwitchTo(vm, index);

        vm.CloseProjectCommand.Execute(null);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is null)
        {
            yield break;
        }

        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
