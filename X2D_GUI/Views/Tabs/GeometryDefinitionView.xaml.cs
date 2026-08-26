using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Views.Tabs;

public partial class GeometryDefinitionView : UserControl
{
    private Window? _pointsPopup;
    private Window? _lineSegmentsPopup;
    private Window? _surfacePopup;

    public GeometryDefinitionView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => HookViewModel();
    }

    private void HookViewModel()
    {
        if (DataContext is not GeometryDefinitionViewModel vm) return;
        vm.RequestOpenPointsPanel = () => PointsToolButton_Click(this, new RoutedEventArgs());
        vm.RequestOpenLineSegmentsPanel = () => LinesToolButton_Click(this, new RoutedEventArgs());
        vm.RequestOpenArcPanel = () => ArcToolButton_Click(this, new RoutedEventArgs());
        vm.RequestOpenCirclePanel = () => CircleToolButton_Click(this, new RoutedEventArgs());
        vm.RequestOpenSurfacePanel = () => SurfaceToolButton_Click(this, new RoutedEventArgs());
    }

    private static void TryExecute(ICommand? command)
    {
        if (command is not null && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private void PointsToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not GeometryDefinitionViewModel vm)
        {
            return;
        }

        var owner = Window.GetWindow(this);
        if (_pointsPopup is { IsLoaded: true })
        {
            _pointsPopup.DataContext = vm;
            PositionToolPopup(_pointsPopup);
            _pointsPopup.Show();
            _pointsPopup.Activate();
            return;
        }

        var panel = new GeometryVerticesPointPanel { DataContext = vm };
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(12, 8, 12, 12),
            Content = panel
        };

        _pointsPopup = new Window
        {
            Title = "Points",
            Content = scroll,
            Width = 420,
            MinHeight = 320,
            MaxHeight = 640,
            SizeToContent = SizeToContent.Height,
            Owner = owner,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual
        };

        _pointsPopup.Closed += (_, _) => _pointsPopup = null;
        _pointsPopup.Loaded += (_, _) =>
        {
            if (_pointsPopup is not null)
            {
                PositionToolPopup(_pointsPopup);
            }
        };

        _pointsPopup.Show();
    }

    private void LinesToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not GeometryDefinitionViewModel vm)
        {
            return;
        }

        ShowLineSegmentsPopup(vm, "Lines", () => TryExecute(vm.StartDrawLineModeCommand));
    }

    private void ArcToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not GeometryDefinitionViewModel vm)
        {
            return;
        }

        ShowLineSegmentsPopup(vm, "Arc", () => TryExecute(vm.StartDrawArcModeCommand));
    }

    private void CircleToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not GeometryDefinitionViewModel vm)
        {
            return;
        }

        ShowLineSegmentsPopup(vm, "Circle", () => TryExecute(vm.StartDrawCircleModeCommand));
    }

    private void SurfaceToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not GeometryDefinitionViewModel vm)
        {
            return;
        }

        ShowSurfaceExcavationPopup(vm);
    }

    private void ShowLineSegmentsPopup(GeometryDefinitionViewModel vm, string title, Action? afterShow)
    {
        var owner = Window.GetWindow(this);
        if (_lineSegmentsPopup is { IsLoaded: true })
        {
            _lineSegmentsPopup.Title = title;
            _lineSegmentsPopup.DataContext = vm;
            PositionToolPopup(_lineSegmentsPopup);
            _lineSegmentsPopup.Show();
            _lineSegmentsPopup.Activate();
            afterShow?.Invoke();
            return;
        }

        var panel = new GeometryLineSegmentsPanel { DataContext = vm };
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(12, 8, 12, 12),
            Content = panel
        };

        _lineSegmentsPopup = new Window
        {
            Title = title,
            Content = scroll,
            Width = 960,
            MinHeight = 320,
            MaxHeight = 720,
            SizeToContent = SizeToContent.Height,
            Owner = owner,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual
        };

        _lineSegmentsPopup.Closed += (_, _) => _lineSegmentsPopup = null;
        _lineSegmentsPopup.Loaded += (_, _) =>
        {
            if (_lineSegmentsPopup is not null)
            {
                PositionToolPopup(_lineSegmentsPopup);
            }
        };

        _lineSegmentsPopup.Show();
        afterShow?.Invoke();
    }

    private void ShowSurfaceExcavationPopup(GeometryDefinitionViewModel vm)
    {
        var owner = Window.GetWindow(this);
        if (_surfacePopup is { IsLoaded: true })
        {
            _surfacePopup.DataContext = vm;
            PositionToolPopup(_surfacePopup);
            _surfacePopup.Show();
            _surfacePopup.Activate();
            return;
        }

        var panel = new GeometrySurfaceExcavationPanel { DataContext = vm };
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(12, 8, 12, 12),
            Content = panel
        };

        _surfacePopup = new Window
        {
            Title = "Surface & excavation",
            Content = scroll,
            Width = 960,
            MinHeight = 360,
            MaxHeight = 720,
            SizeToContent = SizeToContent.Height,
            Owner = owner,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual
        };

        _surfacePopup.Closed += (_, _) => _surfacePopup = null;
        _surfacePopup.Loaded += (_, _) =>
        {
            if (_surfacePopup is not null)
            {
                PositionToolPopup(_surfacePopup);
            }
        };

        _surfacePopup.Show();
    }

    private void PositionToolPopup(Window w)
    {
        if (GeometryCanvasHost is null)
        {
            return;
        }

        w.UpdateLayout();
        var topLeft = GeometryCanvasHost.PointToScreen(new Point(0, 0));
        const double margin = 8;
        w.Left = topLeft.X + GeometryCanvasHost.ActualWidth - w.ActualWidth - margin;
        w.Top = topLeft.Y + margin;

        var work = SystemParameters.WorkArea;
        if (w.Left + w.ActualWidth > work.Right)
        {
            w.Left = work.Right - w.ActualWidth - margin;
        }

        if (w.Left < work.Left)
        {
            w.Left = work.Left + margin;
        }

        if (w.Top + w.ActualHeight > work.Bottom)
        {
            w.Top = work.Bottom - w.ActualHeight - margin;
        }

        if (w.Top < work.Top)
        {
            w.Top = work.Top + margin;
        }
    }
}
