using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SleepOS.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _metricsTimer;
    private readonly ObservableCollection<CommandCenterItem> _visibleCommands = new();
    private List<CommandCenterItem> _commandActions = new();

    public MainWindow()
    {
        InitializeComponent();
        CommandResultsList.ItemsSource = _visibleCommands;
        BuildCommandActions();
        _metricsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _metricsTimer.Tick += MetricsTimer_Tick;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        UpdateNavigationState(DashboardNav);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void WindowFrame_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Clip = new RectangleGeometry(
                new Rect(0, 0, border.ActualWidth, border.ActualHeight),
                border.CornerRadius.TopLeft,
                border.CornerRadius.TopLeft);
        }
    }

    private void MinimizeWindow(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void ToggleWindowState(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
    }

    private void CloseWindow(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CommandCenter_Click(object sender, RoutedEventArgs e)
    {
        OpenCommandCenter();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.K)
        {
            e.Handled = true;
            if (CommandCenterOverlay.Visibility == Visibility.Visible)
            {
                CloseCommandCenter();
            }
            else
            {
                OpenCommandCenter();
            }

            return;
        }

        if (CommandCenterOverlay.Visibility != Visibility.Visible)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseCommandCenter();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = ExecuteSelectedCommandAsync();
        }
    }

    private void OpenCommandCenter()
    {
        CommandCenterOverlay.Visibility = Visibility.Visible;
        CommandSearchBox.Clear();
        FilterCommandActions();
        CommandSearchBox.Focus();
        Keyboard.Focus(CommandSearchBox);
    }

    private void CloseCommandCenter()
    {
        CommandCenterOverlay.Visibility = Visibility.Collapsed;
        Focus();
    }

    private void CommandCenterOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, CommandCenterOverlay))
        {
            CloseCommandCenter();
        }
    }

    private void CommandSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterCommandActions();
    }

    private void CommandResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _ = ExecuteSelectedCommandAsync();
    }

    private void FilterCommandActions()
    {
        var terms = CommandSearchBox.Text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _visibleCommands.Clear();
        foreach (var command in _commandActions.Where(command =>
                     terms.All(term => command.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase))))
        {
            _visibleCommands.Add(command);
        }

        CommandResultsList.SelectedIndex = _visibleCommands.Count > 0 ? 0 : -1;
        CommandNoResults.Visibility = _visibleCommands.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task ExecuteSelectedCommandAsync()
    {
        if (CommandResultsList.SelectedItem is not CommandCenterItem command)
        {
            return;
        }

        CloseCommandCenter();
        await command.ExecuteAsync();
    }

    private void BuildCommandActions()
    {
        _commandActions =
        [
            NavigationCommand("Open Dashboard", "Live CPU, GPU, RAM, drive, and network readings.", () => NavigateToPage(DashboardPage, DashboardNav)),
            NavigationCommand("Open Optimizer", "Review the Balanced and High performance Windows power plans.", () => NavigateToPage(OptimizerPage, OptimizerNav)),
            NavigationCommand("Open SleepOS tweaks", "Adjust SleepOS performance, refresh, motion, and appearance settings.", () => NavigateToPage(TweaksPage, TweaksNav)),
            NavigationCommand("Open Games & processes", "View the games recognized by SleepOS.", () => NavigateToPage(GamesPage, GamesNav)),
            NavigationCommand("Open Hardware center", "View real hardware details reported by Windows.", () => NavigateToPage(HardwarePage, HardwareNav)),
            NavigationCommand("Open Network analyzer", "Measure ping, jitter, packet loss, and inspect adapters.", () => NavigateToPage(NetworkPage, NetworkNav)),
            NavigationCommand("Open Background apps", "Review process details and choose apps yourself.", () => NavigateToPage(ProcessPage, ProcessNav)),
            NavigationCommand("Open PC care", "Preview safe temporary-file cleanup and Windows maintenance settings.", () => NavigateToPage(CarePage, CareNav)),
            NavigationCommand("Open Settings", "Manage SleepOS preferences.", () => NavigateToPage(SettingsPage, SettingsNav)),
            ActionCommand("Refresh system readings", "Update the system metrics shown in SleepOS.", () =>
            {
                if (DataContext is ViewModels.MainWindowViewModel viewModel)
                {
                    viewModel.RefreshMetrics();
                }

                return Task.CompletedTask;
            }),
            ActionCommand("Scan hardware inventory", "Read component details from Windows without changing settings.", async () =>
            {
                if (DataContext is ViewModels.MainWindowViewModel viewModel)
                {
                    await viewModel.ScanHardwareAsync();
                }

                NavigateToPage(HardwarePage, HardwareNav);
            }),
            ActionCommand("Scan network adapters", "Read adapter addresses and DNS configuration; no network test is sent.", () =>
            {
                if (DataContext is ViewModels.MainWindowViewModel viewModel)
                {
                    viewModel.ScanNetworkAdapters();
                }

                NavigateToPage(NetworkPage, NetworkNav);
                return Task.CompletedTask;
            }),
            ActionCommand("Scan background apps", "Review process names and resource use; nothing is stopped.", async () =>
            {
                if (DataContext is ViewModels.MainWindowViewModel viewModel)
                {
                    await viewModel.ScanStoppableProcessesAsync();
                }

                NavigateToPage(ProcessPage, ProcessNav);
            }),
            ActionCommand("Preview temporary files", "Scan old files in your local Temp folder; nothing is deleted.", () =>
            {
                if (DataContext is ViewModels.MainWindowViewModel viewModel)
                {
                    viewModel.ScanTemporaryFiles();
                }

                NavigateToPage(CarePage, CareNav);
                return Task.CompletedTask;
            })
        ];
    }

    private static CommandCenterItem NavigationCommand(string title, string description, Action action) =>
        ActionCommand(title, description, () =>
        {
            action();
            return Task.CompletedTask;
        });

    private static CommandCenterItem ActionCommand(string title, string description, Func<Task> action) =>
        new(title, description, action);

    private void Dashboard_Click(object sender, RoutedEventArgs e)
    {
        NavigateToPage(DashboardPage, DashboardNav);
    }

    private void Optimizer_Click(object sender, RoutedEventArgs e)
    {
        NavigateToPage(OptimizerPage, OptimizerNav);
    }

    private void Tweaks_Click(object sender, RoutedEventArgs e)
    {
        NavigateToPage(TweaksPage, TweaksNav);
    }

    private void Games_Click(object sender, RoutedEventArgs e)
    {
        NavigateToPage(GamesPage, GamesNav);
    }

    private void Hardware_Click(object sender, RoutedEventArgs e)
    {
        NavigateToPage(HardwarePage, HardwareNav);
    }

    private void Network_Click(object sender, RoutedEventArgs e)
    {
        NavigateToPage(NetworkPage, NetworkNav);
    }

    private void Process_Click(object sender, RoutedEventArgs e)
    {
        NavigateToPage(ProcessPage, ProcessNav);
    }

    private void Care_Click(object sender, RoutedEventArgs e)
    {
        NavigateToPage(CarePage, CareNav);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        NavigateToPage(SettingsPage, SettingsNav);
    }

    private void NavigateToPage(TabItem page, Button selectedNavigationButton)
    {
        if (!ReferenceEquals(MainPages.SelectedItem, page))
        {
            MainPages.SelectedItem = page;
            AnimateSelectedPage();
        }

        UpdateNavigationState(selectedNavigationButton);
    }

    private void UpdateNavigationState(Button selectedButton)
    {
        Button[] buttons = [DashboardNav, OptimizerNav, TweaksNav, GamesNav, HardwareNav, NetworkNav, ProcessNav, CareNav, SettingsNav];
        foreach (var button in buttons)
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            button.Foreground = (Brush)FindResource("MutedBrush");
            button.ApplyTemplate();
            if (button.Template.FindName("ActiveMarker", button) is Border marker)
            {
                marker.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
                {
                    To = ReferenceEquals(button, selectedButton) ? 1 : 0,
                    Duration = TimeSpan.FromMilliseconds(180),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
            }
        }

        selectedButton.Background = new SolidColorBrush(Color.FromRgb(37, 31, 51));
        selectedButton.BorderBrush = new SolidColorBrush(Color.FromRgb(74, 54, 100));
        selectedButton.Foreground = (Brush)FindResource("TextBrush");
    }

    private async void StopSelectedProcesses_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.MainWindowViewModel viewModel)
        {
            return;
        }

        var selectedProcesses = viewModel.SelectedStoppableProcesses;
        if (selectedProcesses.Count == 0)
        {
            await viewModel.StopSelectedProcessesAsync();
            return;
        }

        var processDetails = string.Join(
            Environment.NewLine,
            selectedProcesses.Take(5).Select(process =>
                $"{process.Name} (PID {process.ProcessId}) — {process.ProductDescription}; publisher metadata: {process.Publisher}; {process.MemoryMb:F0} MB RAM{Environment.NewLine}  {process.ExecutablePath}"));
        if (selectedProcesses.Count > 5)
        {
            processDetails += $"{Environment.NewLine}...and {selectedProcesses.Count - 5:N0} more selected process(es).";
        }

        var confirmation = MessageBox.Show(
            this,
            $"Force-stop only these {selectedProcesses.Count:N0} process(es), not their whole app/process tree?\n\n{processDetails}\n\nFile publisher details are not digitally verified. Unsaved work may be lost, and an app or launcher may restart a process. Continue?",
            "Confirm closing apps",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        var button = sender as Button;
        if (button is not null)
        {
            button.IsEnabled = false;
        }

        try
        {
            await viewModel.StopSelectedProcessesAsync();
        }
        finally
        {
            if (button is not null)
            {
                button.IsEnabled = true;
            }
        }
    }

    private async void RemoveDuplicateDesktopShortcuts_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.MainWindowViewModel viewModel)
        {
            return;
        }

        var selected = viewModel.DuplicateDesktopShortcuts.Where(shortcut => shortcut.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            await viewModel.RemoveSelectedDuplicateDesktopShortcutsAsync();
            return;
        }

        var details = string.Join(
            Environment.NewLine,
            selected.Take(8).Select(shortcut =>
                $"{shortcut.Name} → {shortcut.TargetPath}{Environment.NewLine}  Arguments: {shortcut.Arguments}{Environment.NewLine}  Shortcut: {shortcut.ShortcutPath}"));
        if (selected.Length > 8)
        {
            details += $"{Environment.NewLine}...and {selected.Length - 8:N0} more shortcut(s).";
        }

        var confirmation = MessageBox.Show(
            this,
            $"Move these {selected.Length:N0} selected shortcut file(s) to the Recycle Bin?\n\n{details}\n\nThe first shortcut for each target is kept. The target applications and files will not be deleted.",
            "Confirm duplicate shortcut cleanup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        var button = sender as Button;
        if (button is not null)
        {
            button.IsEnabled = false;
        }

        try
        {
            await viewModel.RemoveSelectedDuplicateDesktopShortcutsAsync();
        }
        finally
        {
            if (button is not null)
            {
                button.IsEnabled = true;
            }
        }
    }

    private void AnimateSelectedPage()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (MainPages.SelectedContent is not FrameworkElement page)
            {
                return;
            }

            if (DataContext is ViewModels.MainWindowViewModel viewModel && !viewModel.PageAnimationsEnabled)
            {
                page.BeginAnimation(UIElement.OpacityProperty, null);
                page.Opacity = 1;
                page.RenderTransform = Transform.Identity;
                return;
            }

            page.Opacity = 0;
            var offset = new TranslateTransform(0, 9);
            page.RenderTransform = offset;

            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var slide = new DoubleAnimation(9, 0, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            page.BeginAnimation(UIElement.OpacityProperty, fade);
            offset.BeginAnimation(TranslateTransform.YProperty, slide);
        }));
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.PropertyChanged += MainWindowViewModel_PropertyChanged;
            ApplyVisualTheme(viewModel.ColorMode, viewModel.AccentTheme);
            _metricsTimer.Interval = TimeSpan.FromSeconds(viewModel.MetricsRefreshSeconds);
            SetHeroAnimations(viewModel.PageAnimationsEnabled);
        }

        _metricsTimer.Start();
        AnimateSelectedPage();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _metricsTimer.Stop();
        if (DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.PropertyChanged -= MainWindowViewModel_PropertyChanged;
        }
    }

    private void MainWindowViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ViewModels.MainWindowViewModel viewModel)
        {
            return;
        }

        if (e.PropertyName is nameof(ViewModels.MainWindowViewModel.AccentTheme) or
            nameof(ViewModels.MainWindowViewModel.ColorMode))
        {
            ApplyVisualTheme(viewModel.ColorMode, viewModel.AccentTheme);
        }
        else if (e.PropertyName == nameof(ViewModels.MainWindowViewModel.MetricsRefreshSeconds))
        {
            _metricsTimer.Interval = TimeSpan.FromSeconds(viewModel.MetricsRefreshSeconds);
        }
        else if (e.PropertyName == nameof(ViewModels.MainWindowViewModel.PageAnimationsEnabled))
        {
            SetHeroAnimations(viewModel.PageAnimationsEnabled);
            if (viewModel.PageAnimationsEnabled)
            {
                AnimateSelectedPage();
            }
            else if (MainPages.SelectedContent is FrameworkElement page)
            {
                page.BeginAnimation(UIElement.OpacityProperty, null);
                page.Opacity = 1;
                page.RenderTransform = Transform.Identity;
            }
        }
    }

    private void SetHeroAnimations(bool enabled)
    {
        var orbitTransform = (RotateTransform)HeroOrbit.RenderTransform;
        if (!enabled)
        {
            HeroCore.BeginAnimation(UIElement.OpacityProperty, null);
            HeroCore.Opacity = 1;
            orbitTransform.BeginAnimation(RotateTransform.AngleProperty, null);
            orbitTransform.Angle = 0;
            return;
        }

        var pulse = new DoubleAnimation(0.7, 1, TimeSpan.FromSeconds(2.2))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        var orbit = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(8))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        HeroCore.BeginAnimation(UIElement.OpacityProperty, pulse);
        orbitTransform.BeginAnimation(RotateTransform.AngleProperty, orbit);
    }

    private void ApplyVisualTheme(string colorMode, string accentTheme)
    {
        var light = string.Equals(colorMode, "Light", StringComparison.Ordinal);
        var themeBrushes = light
            ? new Dictionary<string, Color>
            {
                ["BackgroundBrush"] = Color.FromRgb(244, 245, 250),
                ["PanelBrush"] = Color.FromRgb(238, 240, 246),
                ["CardBrush"] = Color.FromRgb(255, 255, 255),
                ["LineBrush"] = Color.FromRgb(221, 225, 236),
                ["TextBrush"] = Color.FromRgb(26, 27, 39),
                ["MutedBrush"] = Color.FromRgb(95, 100, 116),
                ["TopBarBrush"] = Color.FromRgb(236, 238, 246),
                ["SidebarBrush"] = Color.FromRgb(240, 241, 247),
                ["ControlBrush"] = Color.FromRgb(255, 255, 255),
                ["ControlBorderBrush"] = Color.FromRgb(199, 203, 218),
                ["HoverBrush"] = Color.FromRgb(231, 234, 245),
                ["PressedBrush"] = Color.FromRgb(222, 227, 242),
                ["ListBrush"] = Color.FromRgb(248, 249, 252),
                ["HeroBorderBrush"] = Color.FromRgb(213, 220, 238),
                ["HeroSubtitleBrush"] = Color.FromRgb(79, 85, 107),
                ["OverlayBrush"] = Color.FromArgb(150, 25, 28, 42),
                ["CommandSurfaceBrush"] = Color.FromRgb(255, 255, 255),
                ["CommandBorderBrush"] = Color.FromRgb(208, 213, 228),
                ["CommandSearchBorderBrush"] = Color.FromRgb(195, 201, 216),
                ["CommandItemBrush"] = Color.FromRgb(247, 248, 252),
                ["CommandItemHoverBrush"] = Color.FromRgb(233, 237, 247),
                ["CommandItemSelectedBrush"] = Color.FromRgb(223, 229, 244),
                ["CommandHintBrush"] = Color.FromRgb(71, 96, 158),
                ["ReviewBrush"] = Color.FromRgb(143, 101, 0),
                ["DangerBrush"] = Color.FromRgb(252, 232, 238),
                ["DangerBorderBrush"] = Color.FromRgb(231, 187, 200),
                ["DangerTextBrush"] = Color.FromRgb(122, 32, 54),
                ["DashboardTagBrush"] = Color.FromRgb(231, 238, 255),
                ["DashboardTagBorderBrush"] = Color.FromRgb(203, 214, 240),
                ["DashboardMetaBrush"] = Color.FromRgb(246, 247, 251),
                ["DashboardMetaBorderBrush"] = Color.FromRgb(216, 220, 232),
                ["OrbitBrush"] = Color.FromRgb(200, 211, 235),
                ["OrbitAccentBrush"] = Color.FromRgb(218, 204, 238)
            }
            : new Dictionary<string, Color>
            {
                ["BackgroundBrush"] = Color.FromRgb(9, 9, 13),
                ["PanelBrush"] = Color.FromRgb(17, 17, 25),
                ["CardBrush"] = Color.FromRgb(21, 21, 31),
                ["LineBrush"] = Color.FromRgb(40, 40, 53),
                ["TextBrush"] = Color.FromRgb(244, 244, 248),
                ["MutedBrush"] = Color.FromRgb(150, 150, 165),
                ["TopBarBrush"] = Color.FromRgb(16, 16, 23),
                ["SidebarBrush"] = Color.FromRgb(14, 14, 20),
                ["ControlBrush"] = Color.FromRgb(27, 27, 39),
                ["ControlBorderBrush"] = Color.FromRgb(52, 52, 68),
                ["HoverBrush"] = Color.FromRgb(41, 38, 58),
                ["PressedBrush"] = Color.FromRgb(57, 36, 79),
                ["ListBrush"] = Color.FromRgb(16, 16, 24),
                ["HeroBorderBrush"] = Color.FromRgb(61, 78, 120),
                ["HeroSubtitleBrush"] = Color.FromRgb(182, 180, 196),
                ["OverlayBrush"] = Color.FromArgb(181, 9, 12, 21),
                ["CommandSurfaceBrush"] = Color.FromRgb(17, 19, 29),
                ["CommandBorderBrush"] = Color.FromRgb(83, 109, 154),
                ["CommandSearchBorderBrush"] = Color.FromRgb(57, 73, 109),
                ["CommandItemBrush"] = Color.FromRgb(21, 25, 37),
                ["CommandItemHoverBrush"] = Color.FromRgb(32, 42, 66),
                ["CommandItemSelectedBrush"] = Color.FromRgb(27, 33, 49),
                ["CommandHintBrush"] = Color.FromRgb(145, 180, 255),
                ["ReviewBrush"] = Color.FromRgb(251, 191, 36),
                ["DangerBrush"] = Color.FromRgb(53, 27, 42),
                ["DangerBorderBrush"] = Color.FromRgb(116, 51, 77),
                ["DangerTextBrush"] = Color.FromRgb(255, 209, 220),
                ["DashboardTagBrush"] = Color.FromRgb(36, 53, 82),
                ["DashboardTagBorderBrush"] = Color.FromRgb(66, 93, 135),
                ["DashboardMetaBrush"] = Color.FromRgb(23, 28, 42),
                ["DashboardMetaBorderBrush"] = Color.FromRgb(48, 58, 85),
                ["OrbitBrush"] = Color.FromRgb(70, 91, 145),
                ["OrbitAccentBrush"] = Color.FromRgb(117, 85, 169)
            };

        foreach (var (key, color) in themeBrushes)
        {
            Resources[key] = new SolidColorBrush(color);
        }

        var accentPalette = (light, accentTheme) switch
        {
            (true, "Blue") => (
                Accent: Color.FromRgb(46, 100, 197),
                AccentBlue: Color.FromRgb(43, 111, 176),
                NavBackground: Color.FromRgb(225, 234, 250),
                NavBorder: Color.FromRgb(184, 203, 235)),
            (true, "Mint") => (
                Accent: Color.FromRgb(0, 127, 102),
                AccentBlue: Color.FromRgb(8, 127, 132),
                NavBackground: Color.FromRgb(224, 242, 237),
                NavBorder: Color.FromRgb(178, 219, 205)),
            (true, _) => (
                Accent: Color.FromRgb(135, 54, 187),
                AccentBlue: Color.FromRgb(60, 111, 192),
                NavBackground: Color.FromRgb(239, 230, 249),
                NavBorder: Color.FromRgb(211, 190, 233)),
            (false, "Blue") => (
                Accent: Color.FromRgb(91, 140, 255),
                AccentBlue: Color.FromRgb(138, 199, 255),
                NavBackground: Color.FromRgb(25, 41, 65),
                NavBorder: Color.FromRgb(54, 92, 139)),
            (false, "Mint") => (
                Accent: Color.FromRgb(55, 201, 166),
                AccentBlue: Color.FromRgb(122, 232, 205),
                NavBackground: Color.FromRgb(23, 48, 43),
                NavBorder: Color.FromRgb(45, 102, 87)),
            _ => (
                Accent: Color.FromRgb(168, 85, 247),
                AccentBlue: Color.FromRgb(121, 168, 255),
                NavBackground: Color.FromRgb(37, 31, 51),
                NavBorder: Color.FromRgb(74, 54, 100))
        };

        Resources["AccentColor"] = accentPalette.Accent;
        Resources["AccentBlueColor"] = accentPalette.AccentBlue;
        Resources["HeroStartColor"] = light ? Color.FromRgb(234, 240, 255) : Color.FromRgb(28, 42, 74);
        Resources["HeroMiddleColor"] = light ? Color.FromRgb(244, 237, 255) : Color.FromRgb(33, 27, 59);
        Resources["HeroEndColor"] = light ? Color.FromRgb(249, 250, 253) : Color.FromRgb(18, 24, 33);
        Resources["AccentBrush"] = new SolidColorBrush(accentPalette.Accent);
        Resources["AccentBlueBrush"] = new SolidColorBrush(accentPalette.AccentBlue);
        Resources["NavSelectedBrush"] = new SolidColorBrush(accentPalette.NavBackground);
        Resources["NavSelectedBorderBrush"] = new SolidColorBrush(accentPalette.NavBorder);

        if (GetSelectedNavigationButton() is { } selectedButton)
        {
            UpdateNavigationState(selectedButton);
        }

    }

    private Button? GetSelectedNavigationButton()
    {
        if (ReferenceEquals(MainPages.SelectedItem, DashboardPage)) return DashboardNav;
        if (ReferenceEquals(MainPages.SelectedItem, OptimizerPage)) return OptimizerNav;
        if (ReferenceEquals(MainPages.SelectedItem, TweaksPage)) return TweaksNav;
        if (ReferenceEquals(MainPages.SelectedItem, GamesPage)) return GamesNav;
        if (ReferenceEquals(MainPages.SelectedItem, HardwarePage)) return HardwareNav;
        if (ReferenceEquals(MainPages.SelectedItem, NetworkPage)) return NetworkNav;
        if (ReferenceEquals(MainPages.SelectedItem, ProcessPage)) return ProcessNav;
        if (ReferenceEquals(MainPages.SelectedItem, CarePage)) return CareNav;
        if (ReferenceEquals(MainPages.SelectedItem, SettingsPage)) return SettingsNav;
        return null;
    }

    private void MetricsTimer_Tick(object? sender, EventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel vm && vm.AutoRefreshEnabled)
        {
            vm.RefreshMetrics();
        }
    }

    private sealed record CommandCenterItem(string Title, string Description, Func<Task> ExecuteAsync)
    {
        public string SearchText => $"{Title} {Description}";
    }
}