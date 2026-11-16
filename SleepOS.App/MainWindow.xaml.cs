using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace SleepOS.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _metricsTimer;
    private readonly ObservableCollection<CommandCenterItem> _visibleCommands = new();
    private List<CommandCenterItem> _commandActions = new();
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private Button? _activeNavigationButton;
    private bool _sidebarCollapsed;

    public MainWindow()
    {
        InitializeComponent();
        CommandResultsList.ItemsSource = _visibleCommands;
        BuildCommandActions();
        _metricsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _metricsTimer.Tick += MetricsTimer_Tick;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        StateChanged += MainWindow_StateChanged;
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

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        _sidebarCollapsed = !_sidebarCollapsed;
        SidebarColumn.Width = new GridLength(_sidebarCollapsed ? 76 : 244);
        SidebarBrandText.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarWorkspaceHeading.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarAutomationHeading.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarToolsHeading.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarStatusText.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarStatusCard.Padding = _sidebarCollapsed ? new Thickness(0) : new Thickness(12);
        SidebarToggleButton.Content = _sidebarCollapsed ? "\uE76B" : "\uE76C";
        SidebarToggleButton.ToolTip = _sidebarCollapsed ? "Expand sidebar" : "Collapse sidebar";

        Button[] buttons =
        [
            DashboardNav, OptimizerNav, ProcessNav, ApplicationManagerNav, GamesNav, BenchmarkNav,
            TweaksNav, AutoStartNav, SettingsNav, CareNav, HardwareNav, NetworkNav
        ];
        foreach (var button in buttons)
        {
            button.HorizontalContentAlignment = _sidebarCollapsed
                ? System.Windows.HorizontalAlignment.Center
                : System.Windows.HorizontalAlignment.Left;
            button.Padding = _sidebarCollapsed ? new Thickness(0) : new Thickness(12, 8, 12, 8);
            if (button.Content is StackPanel content && content.Children.Count > 1)
            {
                content.Children[1].Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        if (SidebarStatusCard.Child is Grid statusGrid && statusGrid.Children.OfType<Ellipse>().FirstOrDefault() is { } indicator)
        {
            indicator.HorizontalAlignment = _sidebarCollapsed
                ? System.Windows.HorizontalAlignment.Center
                : System.Windows.HorizontalAlignment.Left;
            indicator.Margin = _sidebarCollapsed ? new Thickness(0) : new Thickness(1, 3, 0, 0);
        }
    }

    internal void ShowInTray()
    {
        EnsureTrayIcon();
        Hide();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            EnsureTrayIcon();
            Hide();
            WindowState = WindowState.Normal;
        }
    }

    private void EnsureTrayIcon()
    {
        if (_trayIcon is not null)
        {
            return;
        }

        var menu = new System.Windows.Forms.ContextMenuStrip();
        var openItem = new System.Windows.Forms.ToolStripMenuItem("Open SleepOS");
        openItem.Click += (_, _) => RestoreFromTray();
        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => Close();
        menu.Items.Add(openItem);
        menu.Items.Add(exitItem);

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "SleepOS",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
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
            NavigationCommand("Open Performance", "Choose SleepOS performance policies and review measured benchmarks.", () => NavigateToPage(OptimizerPage, OptimizerNav)),
            NavigationCommand("Open Memory Saver", "Manage opt-in application memory and efficiency rules.", () => NavigateToSection(ProcessPage, ProcessNav, MemorySaverCard)),
            NavigationCommand("Open Application Manager", "Review app processes, working sets, CPU, and safe actions.", () => NavigateToSection(ProcessPage, ApplicationManagerNav, ApplicationManagerCard)),
            NavigationCommand("Open Gaming Optimizer", "Review recognized games and per-game profiles.", () => NavigateToPage(GamesPage, GamesNav)),
            NavigationCommand("Open Benchmark", "Capture and compare measured system readings.", () => NavigateToSection(OptimizerPage, BenchmarkNav, BenchmarkCard)),
            NavigationCommand("Open Automations", "Configure SleepOS performance automation.", () => NavigateToSection(TweaksPage, TweaksNav, AutomationOptionsCard)),
            NavigationCommand("Open Autostart", "Configure Windows sign-in startup options.", () => NavigateToSection(SettingsPage, AutoStartNav, StartupSettingsCard)),
            NavigationCommand("Open Hardware center", "View real hardware details reported by Windows.", () => NavigateToPage(HardwarePage, HardwareNav)),
            NavigationCommand("Open Network analyzer", "Measure ping, jitter, packet loss, and inspect adapters.", () => NavigateToPage(NetworkPage, NetworkNav)),
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

    private void MemorySaver_Click(object sender, RoutedEventArgs e)
    {
        NavigateToSection(ProcessPage, ProcessNav, MemorySaverCard);
    }

    private void ApplicationManager_Click(object sender, RoutedEventArgs e)
    {
        NavigateToSection(ProcessPage, ApplicationManagerNav, ApplicationManagerCard);
    }

    private void GamingOptimizer_Click(object sender, RoutedEventArgs e)
    {
        NavigateToPage(GamesPage, GamesNav);
    }

    private void Benchmark_Click(object sender, RoutedEventArgs e)
    {
        NavigateToSection(OptimizerPage, BenchmarkNav, BenchmarkCard);
    }

    private void Automations_Click(object sender, RoutedEventArgs e)
    {
        NavigateToSection(TweaksPage, TweaksNav, AutomationOptionsCard);
    }

    private void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        NavigateToSection(SettingsPage, AutoStartNav, StartupSettingsCard);
    }

    private void SettingsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SettingsContentPanel is null || SettingsSearchNoResults is null)
        {
            return;
        }

        var query = SettingsSearchBox.Text.Trim();
        TextBlock? currentHeading = null;
        var sectionHasMatch = false;
        var anyMatch = false;
        foreach (var child in SettingsContentPanel.Children)
        {
            if (child is TextBlock { Tag: "SettingsCategoryHeading" } heading)
            {
                if (currentHeading is not null)
                {
                    currentHeading.Visibility = sectionHasMatch ? Visibility.Visible : Visibility.Collapsed;
                }

                currentHeading = heading;
                sectionHasMatch = false;
                continue;
            }

            if (child is not Border card)
            {
                continue;
            }

            var searchableText = $"{currentHeading?.Text} {GetSearchableText(card)}";
            var matches = query.Length == 0 || searchableText.Contains(query, StringComparison.OrdinalIgnoreCase);
            card.Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
            if (matches)
            {
                sectionHasMatch = true;
                anyMatch = true;
            }
        }

        if (currentHeading is not null)
        {
            currentHeading.Visibility = sectionHasMatch ? Visibility.Visible : Visibility.Collapsed;
        }

        SettingsSearchNoResults.Visibility = query.Length > 0 && !anyMatch
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static string GetSearchableText(DependencyObject element)
    {
        var values = new List<string>();
        if (element is TextBlock textBlock)
        {
            values.Add(textBlock.Text);
        }

        if (element is ContentControl { Content: string content })
        {
            values.Add(content);
        }

        if (element is ItemsControl itemsControl)
        {
            values.AddRange(itemsControl.Items.Cast<object>().Select(item => item.ToString() ?? string.Empty));
        }

        foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
        {
            values.Add(GetSearchableText(child));
        }

        return string.Join(" ", values);
    }

    private void NavigateToSection(TabItem page, Button selectedNavigationButton, FrameworkElement section)
    {
        if (ReferenceEquals(page, SettingsPage))
        {
            SettingsSearchBox.Clear();
        }

        NavigateToPage(page, selectedNavigationButton);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(section.BringIntoView));
    }

    private void NavigateToPage(TabItem page, Button selectedNavigationButton)
    {
        if (!ReferenceEquals(MainPages.SelectedItem, page))
        {
            MainPages.SelectedItem = page;
            AnimateSelectedPage();
        }

        _activeNavigationButton = selectedNavigationButton;
        UpdateNavigationState(selectedNavigationButton);
    }

    private void UpdateNavigationState(Button selectedButton)
    {
        _activeNavigationButton = selectedButton;
        Button[] buttons =
        [
            DashboardNav, OptimizerNav, ProcessNav, ApplicationManagerNav, GamesNav, BenchmarkNav,
            TweaksNav, AutoStartNav, SettingsNav, CareNav, HardwareNav, NetworkNav
        ];
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

        selectedButton.Background = (Brush)FindResource("NavSelectedBrush");
        selectedButton.BorderBrush = (Brush)FindResource("NavSelectedBorderBrush");
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

    private async void TrimSelectedProcessesMemory_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.MainWindowViewModel viewModel)
        {
            return;
        }

        var selectedProcesses = viewModel.SelectedStoppableProcesses;
        if (selectedProcesses.Count == 0)
        {
            await viewModel.TrimSelectedProcessesMemoryAsync();
            return;
        }

        var processDetails = string.Join(
            Environment.NewLine,
            selectedProcesses.Take(5).Select(process =>
                $"{process.Name} (PID {process.ProcessId}) — {process.MemoryMb:F0} MB working set"));
        if (selectedProcesses.Count > 5)
        {
            processDetails += $"{Environment.NewLine}...and {selectedProcesses.Count - 5:N0} more selected process(es).";
        }

        var confirmation = MessageBox.Show(
            this,
            $"Ask Windows to trim the resident working set of these {selectedProcesses.Count:N0} selected app process(es)?\n\n{processDetails}\n\nThis may cause apps to reload data or stutter, and they can allocate memory again. It does not reduce their committed memory.",
            "Confirm temporary RAM trim",
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
            await viewModel.TrimSelectedProcessesMemoryAsync();
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
        _trayIcon?.ContextMenuStrip?.Dispose();
        _trayIcon?.Dispose();
        _trayIcon = null;
        if (DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.PropertyChanged -= MainWindowViewModel_PropertyChanged;
            viewModel.RestorePerformancePolicies();
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
                ["SuccessBrush"] = Color.FromRgb(22, 163, 74),
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
                ["BackgroundBrush"] = Color.FromRgb(8, 8, 17),
                ["PanelBrush"] = Color.FromRgb(14, 14, 24),
                ["CardBrush"] = Color.FromRgb(17, 17, 31),
                ["LineBrush"] = Color.FromRgb(39, 36, 57),
                ["TextBrush"] = Color.FromRgb(244, 244, 248),
                ["MutedBrush"] = Color.FromRgb(157, 153, 177),
                ["TopBarBrush"] = Color.FromRgb(12, 12, 22),
                ["SidebarBrush"] = Color.FromRgb(10, 10, 19),
                ["ControlBrush"] = Color.FromRgb(25, 24, 39),
                ["ControlBorderBrush"] = Color.FromRgb(57, 51, 78),
                ["HoverBrush"] = Color.FromRgb(39, 31, 57),
                ["PressedBrush"] = Color.FromRgb(56, 32, 84),
                ["ListBrush"] = Color.FromRgb(12, 12, 22),
                ["HeroBorderBrush"] = Color.FromRgb(86, 54, 133),
                ["HeroSubtitleBrush"] = Color.FromRgb(194, 187, 211),
                ["OverlayBrush"] = Color.FromArgb(181, 9, 12, 21),
                ["CommandSurfaceBrush"] = Color.FromRgb(17, 17, 31),
                ["CommandBorderBrush"] = Color.FromRgb(90, 60, 137),
                ["CommandSearchBorderBrush"] = Color.FromRgb(64, 49, 96),
                ["CommandItemBrush"] = Color.FromRgb(22, 21, 37),
                ["CommandItemHoverBrush"] = Color.FromRgb(39, 31, 57),
                ["CommandItemSelectedBrush"] = Color.FromRgb(31, 25, 46),
                ["CommandHintBrush"] = Color.FromRgb(182, 106, 255),
                ["ReviewBrush"] = Color.FromRgb(251, 191, 36),
                ["SuccessBrush"] = Color.FromRgb(34, 197, 94),
                ["DangerBrush"] = Color.FromRgb(53, 27, 42),
                ["DangerBorderBrush"] = Color.FromRgb(116, 51, 77),
                ["DangerTextBrush"] = Color.FromRgb(255, 209, 220),
                ["DashboardTagBrush"] = Color.FromRgb(37, 25, 56),
                ["DashboardTagBorderBrush"] = Color.FromRgb(91, 57, 135),
                ["DashboardMetaBrush"] = Color.FromRgb(23, 21, 37),
                ["DashboardMetaBorderBrush"] = Color.FromRgb(55, 45, 80),
                ["OrbitBrush"] = Color.FromRgb(97, 63, 143),
                ["OrbitAccentBrush"] = Color.FromRgb(153, 82, 219)
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
                Accent: Color.FromRgb(139, 53, 255),
                AccentBlue: Color.FromRgb(182, 106, 255),
                NavBackground: Color.FromRgb(37, 25, 56),
                NavBorder: Color.FromRgb(91, 57, 135))
        };

        Resources["AccentColor"] = accentPalette.Accent;
        Resources["AccentBlueColor"] = accentPalette.AccentBlue;
        Resources["HeroStartColor"] = light ? Color.FromRgb(234, 240, 255) : Color.FromRgb(39, 22, 62);
        Resources["HeroMiddleColor"] = light ? Color.FromRgb(244, 237, 255) : Color.FromRgb(30, 21, 47);
        Resources["HeroEndColor"] = light ? Color.FromRgb(249, 250, 253) : Color.FromRgb(17, 17, 31);
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
        if (_activeNavigationButton is not null)
        {
            return _activeNavigationButton;
        }

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
        else if (DataContext is ViewModels.MainWindowViewModel automationViewModel)
        {
            automationViewModel.EvaluatePerformanceAutomation();
        }
    }

    private sealed record CommandCenterItem(string Title, string Description, Func<Task> ExecuteAsync)
    {
        public string SearchText => $"{Title} {Description}";
    }
}