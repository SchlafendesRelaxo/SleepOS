using System.ComponentModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SleepOS.Core;
using SleepOS.Games;
using SleepOS.Monitoring;
using SleepOS.Optimizer;
using SleepOS.Settings;
using SleepOS.WindowsSystem;
using SleepOS.Updater;
using Velopack.Exceptions;

namespace SleepOS.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ISystemMonitorService _monitorService;
    private readonly IHardwareInventoryService _hardwareInventoryService;
    private readonly INetworkDiagnosticsService _networkDiagnosticsService;
    private readonly IWindowsSystemService _systemService;
    private readonly ITemporaryFilesCleanupService _cleanupService;
    private readonly IProcessManagementService _processManagementService;
    private readonly IGameService _gameService;
    private readonly IAppSettingsStore _settingsStore;
    private readonly InstalledVersionService _versionService;
    private readonly IDesktopShortcutService _desktopShortcutService;
    private readonly IWindowsStartupService _startupService;
    private string _performanceMode = "Balanced";
    private string _accentTheme = "Violet";
    private string _colorMode = "Dark";
    private int _metricsRefreshSeconds = 5;
    private bool _pageAnimationsEnabled = true;
    private DateTime _lastAutomationRunUtc = DateTime.MinValue;
    private int _automationInProgress;
    private bool _isClosing;
    private readonly ConcurrentDictionary<(int ProcessId, long StartTimeTicks), ManagedProcessCandidate> _automaticEfficiencyProcesses = new();
    private bool _startMinimized;
    private int _startupDelaySeconds;

    public MainWindowViewModel(
        ISystemMonitorService monitorService,
        IHardwareInventoryService hardwareInventoryService,
        INetworkDiagnosticsService networkDiagnosticsService,
        IWindowsSystemService systemService,
        ITemporaryFilesCleanupService cleanupService,
        IProcessManagementService processManagementService,
        IGameService gameService,
        IAppSettingsStore settingsStore,
        InstalledVersionService versionService,
        IDesktopShortcutService desktopShortcutService,
        IWindowsStartupService startupService)
    {
        _monitorService = monitorService;
        _hardwareInventoryService = hardwareInventoryService;
        _networkDiagnosticsService = networkDiagnosticsService;
        _systemService = systemService;
        _cleanupService = cleanupService;
        _processManagementService = processManagementService;
        _gameService = gameService;
        _settingsStore = settingsStore;
        _versionService = versionService;
        _desktopShortcutService = desktopShortcutService;
        _startupService = startupService;

        UpdateSettings(_settingsStore.Load());
        LoadProfiles();
        RefreshStartupStatus();
        RefreshMetrics();
    }

    public ObservableCollection<DashboardMetricCard> HardwareCards { get; } = new();
    public ObservableCollection<RecentOptimizationEntry> RecentOptimizations { get; } = new();
    public ObservableCollection<OptimizationProfile> Profiles { get; } = new();
    public ObservableCollection<ProcessPerformance> RunningProcesses { get; } = new();
    public ObservableCollection<ManagedProcessCandidate> ManageableProcesses { get; } = new();
    public ObservableCollection<ApplicationPerformanceRule> ManagedApplications { get; } = new();
    public ObservableCollection<PerformanceBenchmarkRecord> BenchmarkHistory { get; } = new();
    public ObservableCollection<GamePerformanceProfile> GameProcessProfiles { get; } = new();
    public ObservableCollection<GameSession> ActiveGames { get; } = new();
    public ObservableCollection<HardwareInventoryItem> HardwareInventory { get; } = new();
    public ObservableCollection<string> HardwareInventoryWarnings { get; } = new();
    public ObservableCollection<NetworkAdapterInfo> NetworkAdapters { get; } = new();
    public ObservableCollection<string> NetworkAdapterWarnings { get; } = new();
    public ObservableCollection<NetworkPingSample> NetworkPingSamples { get; } = new();
    public ObservableCollection<NetworkPingSummary> NetworkPingHistory { get; } = new();
    public ObservableCollection<DesktopShortcutCandidate> DuplicateDesktopShortcuts { get; } = new();
    public ObservableCollection<string> DesktopShortcutWarnings { get; } = new();
    public IReadOnlyList<string> AccentThemeOptions { get; } = ["Violet", "Blue", "Mint"];
    public IReadOnlyList<string> ColorModeOptions { get; } = ["Dark", "Light"];
    public IReadOnlyList<string> PerformanceModeOptions { get; } = ["Balanced", "Gaming", "Extreme Performance", "Battery Saver", "Custom"];
    public IReadOnlyList<int> MetricsRefreshOptions { get; } = [5, 10, 30];
    public IReadOnlyList<int> MemorySaverInactivityOptions { get; } = [1, 5, 10, 20, 30, 60];
    public IReadOnlyList<int> StartupDelayOptions { get; } = [0, 5, 10, 30, 60, 120, 300];
    public IReadOnlyList<string> GamePerformanceModeOptions { get; } = ["Balanced", "Gaming", "Extreme Performance", "Battery Saver"];

    public string Greeting { get; set; } = "Good evening, gamer";
    public string SystemStatus { get; set; } = "Nominal";
    public string GamingModeText { get; set; } = "Gaming policy: Off";
    public string ActivePowerProfile { get; set; } = "Balanced";
    public string CurrentProfile { get; set; } = "Balanced";
    public string UptimeText { get; set; } = "0:00:00";
    public string ActiveVersion { get; set; } = "v1.0.0";
    public string UpdateStatus { get; private set; } = "Updates are available after installing SleepOS with the setup program.";
    public bool GamingModeEnabled { get; set; }
    public bool AutoRefreshEnabled { get; set; }
    public string ActionStatus { get; private set; } = "Ready";
    public string CleanupStatus { get; private set; } = "Scan temporary files to see a safe cleanup recommendation.";
    public string DesktopShortcutStatus { get; private set; } = "Scan your Desktop for duplicate shortcuts. Only shortcut files are considered.";
    public bool CanRemoveDuplicateShortcuts => DuplicateDesktopShortcuts.Any(shortcut => shortcut.IsSelected);
    public bool CanCleanTemporaryFiles { get; private set; }
    public string ProcessManagementStatus { get; private set; } = "Scan to find running non-Windows apps.";
    public string MemorySaverStatus { get; private set; } = "Memory Saver is opt-in and only applies Windows efficiency mode to apps you manage.";
    public string StartupStatus { get; private set; } = "Checking current-user sign-in startup...";
    public string BenchmarkStatus { get; private set; } = "Capture a baseline, make a change, then capture another reading to compare measured values.";
    public string GameProfileStatus { get; private set; } = "Add a running app from Background apps to recognize it as a game.";
    public bool AutoStartEnabled { get; private set; }
    public bool StartMinimized
    {
        get => _startMinimized;
        set
        {
            if (SetProperty(ref _startMinimized, value))
            {
                SaveStartupSettings();
            }
        }
    }

    public int StartupDelaySeconds
    {
        get => _startupDelaySeconds;
        set
        {
            var normalized = StartupDelayOptions.Contains(value) ? value : 0;
            if (SetProperty(ref _startupDelaySeconds, normalized))
            {
                SaveStartupSettings();
            }
        }
    }
    public bool AutomaticMemorySaverEnabled { get; private set; }
    public bool AutomaticGamingModeEnabled { get; private set; }
    public bool AutomaticHighCpuAnalysisEnabled { get; private set; }
    public bool HasPerformanceAutomation => AutomaticMemorySaverEnabled || AutomaticGamingModeEnabled ||
        AutomaticHighCpuAnalysisEnabled || _automaticEfficiencyProcesses.Count > 0;
    public bool IsStartupDelayEnabled => AutoStartEnabled;
    public string HardwareInventoryStatus { get; private set; } = "Scan to read hardware details reported by Windows.";
    public string NetworkAdapterStatus { get; private set; } = "Scan to read network adapters reported by Windows.";
    public string NetworkPingStatus { get; private set; } = "Enter a target and run a test. No network probes are sent automatically.";
    public string NetworkPingTarget { get; set; } = string.Empty;
    public string NetworkThroughputStatus { get; private set; } = "Download N/A • Upload N/A";
    public string NetworkAverageText { get; private set; } = "N/A";
    public string NetworkJitterText { get; private set; } = "N/A";
    public string NetworkPacketLossText { get; private set; } = "N/A";
    public string AccentTheme
    {
        get => _accentTheme;
        set
        {
            var normalizedTheme = NormalizeAccentTheme(value);
            if (string.Equals(_accentTheme, normalizedTheme, StringComparison.Ordinal))
            {
                return;
            }

            var settings = _settingsStore.Load();
            settings.AccentTheme = normalizedTheme;
            if (!SaveSettings(settings))
            {
                OnPropertyChanged(nameof(AccentTheme));
                return;
            }

            SetProperty(ref _accentTheme, normalizedTheme);
            ActionStatus = $"Accent color changed to {normalizedTheme}.";
            OnPropertyChanged(nameof(ActionStatus));
        }
    }

    public int MetricsRefreshSeconds
    {
        get => _metricsRefreshSeconds;
        set
        {
            var normalizedSeconds = NormalizeMetricsRefreshSeconds(value);
            if (_metricsRefreshSeconds == normalizedSeconds)
            {
                return;
            }

            var settings = _settingsStore.Load();
            settings.MetricsRefreshSeconds = normalizedSeconds;
            MarkPerformanceModeCustom(settings);
            if (!SaveSettings(settings))
            {
                OnPropertyChanged(nameof(MetricsRefreshSeconds));
                return;
            }

            SetProperty(ref _metricsRefreshSeconds, normalizedSeconds);
            SetProperty(ref _performanceMode, "Custom", nameof(PerformanceMode));
            GamingModeEnabled = false;
            GamingModeText = "Gaming policy: Off";
            ActionStatus = $"System readings refresh every {normalizedSeconds} seconds.";
            NotifyPerformanceSettingsChanged();
            OnPropertyChanged(nameof(ActionStatus));
        }
    }

    public string ColorMode
    {
        get => _colorMode;
        set
        {
            var normalizedMode = NormalizeColorMode(value);
            if (string.Equals(_colorMode, normalizedMode, StringComparison.Ordinal))
            {
                return;
            }

            var settings = _settingsStore.Load();
            settings.ColorMode = normalizedMode;
            if (!SaveSettings(settings))
            {
                OnPropertyChanged(nameof(ColorMode));
                return;
            }

            SetProperty(ref _colorMode, normalizedMode);
            ActionStatus = $"{normalizedMode} mode enabled.";
            OnPropertyChanged(nameof(ActionStatus));
        }
    }

    public bool PageAnimationsEnabled
    {
        get => _pageAnimationsEnabled;
        set
        {
            if (_pageAnimationsEnabled == value)
            {
                return;
            }

            var settings = _settingsStore.Load();
            settings.PageAnimationsEnabled = value;
            MarkPerformanceModeCustom(settings);
            if (!SaveSettings(settings))
            {
                OnPropertyChanged(nameof(PageAnimationsEnabled));
                return;
            }

            SetProperty(ref _pageAnimationsEnabled, value);
            SetProperty(ref _performanceMode, "Custom", nameof(PerformanceMode));
            GamingModeEnabled = false;
            GamingModeText = "Gaming policy: Off";
            ActionStatus = value ? "Page transitions and orbit motion enabled." : "Page transitions and orbit motion disabled.";
            NotifyPerformanceSettingsChanged();
            OnPropertyChanged(nameof(ActionStatus));
        }
    }

    public string PerformanceMode
    {
        get => _performanceMode;
        set
        {
            var normalizedMode = NormalizePerformanceMode(value);
            if (string.Equals(_performanceMode, normalizedMode, StringComparison.Ordinal))
            {
                return;
            }

            var settings = _settingsStore.Load();
            settings.PerformanceMode = normalizedMode;
            switch (normalizedMode)
            {
                case "Balanced":
                    settings.AutoRefreshEnabled = true;
                    settings.MetricsRefreshSeconds = 5;
                    settings.PageAnimationsEnabled = true;
                    settings.GamingModeEnabled = false;
                    break;
                case "Gaming":
                    settings.AutoRefreshEnabled = true;
                    settings.MetricsRefreshSeconds = 10;
                    settings.PageAnimationsEnabled = false;
                    settings.GamingModeEnabled = true;
                    break;
                case "Extreme Performance":
                    settings.AutoRefreshEnabled = false;
                    settings.MetricsRefreshSeconds = 30;
                    settings.PageAnimationsEnabled = false;
                    settings.GamingModeEnabled = false;
                    break;
                case "Battery Saver":
                    settings.AutoRefreshEnabled = true;
                    settings.MetricsRefreshSeconds = 30;
                    settings.PageAnimationsEnabled = false;
                    settings.GamingModeEnabled = false;
                    break;
            }

            if (!SaveSettings(settings))
            {
                OnPropertyChanged(nameof(PerformanceMode));
                return;
            }

            UpdateSettings(settings);
            NotifyPerformanceSettingsChanged();
            ActionStatus = normalizedMode switch
            {
                "Balanced" => "Balanced performance policy enabled. Automatic process management remains opt-in.",
                "Gaming" => "Gaming policy enabled. Selected background apps may use Windows efficiency mode after their idle threshold.",
                "Extreme Performance" => "Extreme Performance minimizes SleepOS background polling; it does not overclock or change system services.",
                _ => "Battery Saver policy enabled. Selected idle apps may use Windows efficiency mode when memory pressure is high."
            };
            OnPropertyChanged(nameof(ActionStatus));
        }
    }

    public int ManageableProcessCount => ManageableProcesses.Count;
    public int RecommendedProcessCount => ManageableProcesses.Count(process => process.IsSuggestedForReview);
    public IReadOnlyList<ManagedProcessCandidate> SelectedStoppableProcesses =>
        ManageableProcesses.Where(process => process.IsSelected).ToArray();
    public string GamingModeStatus => GamingModeEnabled ? "On • SleepOS polling and display policy" : "Off";
    public string AutoRefreshStatus => AutoRefreshEnabled ? $"On • every {MetricsRefreshSeconds} seconds" : "Off";
    public string PageAnimationsStatus => PageAnimationsEnabled ? "Enabled • page transitions and orbit motion" : "Disabled";
    public string PerformanceModeStatus => _performanceMode switch
    {
        "Gaming" => "Refreshes every 10 seconds. Only explicitly managed background apps can receive EcoQoS.",
        "Extreme Performance" => "Pauses automatic metrics polling and animations. No Windows power plan is changed.",
        "Battery Saver" => "Refreshes every 30 seconds and favors selected idle apps when memory is under pressure.",
        _ => "Refreshes every 5 seconds with page animations. Automatic process optimization is opt-in."
    };

    [RelayCommand]
    public async Task ScanHardwareAsync()
    {
        HardwareInventoryStatus = "Reading hardware details reported by Windows...";
        HardwareInventory.Clear();
        HardwareInventoryWarnings.Clear();
        OnPropertyChanged(nameof(HardwareInventoryStatus));
        OnPropertyChanged(nameof(HardwareInventory));
        OnPropertyChanged(nameof(HardwareInventoryWarnings));

        var report = await Task.Run(_hardwareInventoryService.Capture);
        foreach (var item in report.Items)
        {
            HardwareInventory.Add(item);
        }

        foreach (var warning in report.Warnings)
        {
            HardwareInventoryWarnings.Add(warning);
        }

        HardwareInventoryStatus = report.Items.Count == 0
            ? "Windows did not provide hardware inventory data. Review the details below."
            : $"Read {report.Items.Count:N0} hardware record(s) from Windows at {report.CapturedAt.ToLocalTime():t}.";
        if (report.Warnings.Count > 0)
        {
            HardwareInventoryStatus += $" {report.Warnings.Count:N0} category warning(s) are listed below.";
        }

        OnPropertyChanged(nameof(HardwareInventoryStatus));
        OnPropertyChanged(nameof(HardwareInventory));
        OnPropertyChanged(nameof(HardwareInventoryWarnings));
    }

    [RelayCommand]
    public void RefreshMetrics()
    {
        var metrics = _monitorService.Capture();
        var settings = _settingsStore.Load();

        Greeting = DateTime.Now.Hour switch
        {
            >= 5 and < 12 => "Good morning, gamer",
            >= 12 and < 18 => "Good afternoon, gamer",
            _ => "Good evening, gamer"
        };

        GamingModeEnabled = string.Equals(_performanceMode, "Gaming", StringComparison.Ordinal);
        AutoRefreshEnabled = settings.AutoRefreshEnabled;
        GamingModeText = GamingModeEnabled ? "Gaming policy: On" : "Gaming policy: Off";
        ActivePowerProfile = _systemService.GetActivePowerProfile();
        CurrentProfile = NormalizeProfileName(settings.LastSelectedProfile);
        UptimeText = metrics.UptimeText;
        ActiveVersion = _versionService.CurrentVersion?.ToString(3) ?? "Unavailable";
        NetworkThroughputStatus = $"Download {FormatSpeed(metrics.DownloadMbps)} • Upload {FormatSpeed(metrics.UploadMbps)}";

        HardwareCards.Clear();
        HardwareCards.Add(new DashboardMetricCard { Title = "CPU", Value = FormatPercent(metrics.CpuUsagePercent), Detail = "Usage • temperature unavailable", Accent = "#A855F7" });
        HardwareCards.Add(new DashboardMetricCard { Title = "GPU", Value = FormatPercent(metrics.GpuUsagePercent), Detail = "Highest engine • temperature unavailable", Accent = "#8B5CF6" });
        HardwareCards.Add(new DashboardMetricCard { Title = "RAM", Value = FormatGigabytes(metrics.RamUsedGb), Detail = metrics.TotalRamGb is { } totalRam ? $"of {totalRam:F1} GB" : "Total unavailable", Accent = "#C084FC" });
        HardwareCards.Add(new DashboardMetricCard { Title = "VRAM", Value = "N/A", Detail = "Not exposed by Windows", Accent = "#A78BFA" });
        HardwareCards.Add(new DashboardMetricCard { Title = "System drive", Value = FormatPercent(metrics.DiskUsagePercent), Detail = "Capacity used • not drive health", Accent = "#7C3AED" });
        HardwareCards.Add(new DashboardMetricCard { Title = "Network", Value = FormatSpeed(metrics.DownloadMbps), Detail = $"↑ {FormatSpeed(metrics.UploadMbps)} upload", Accent = "#8B5CF6" });

        RunningProcesses.Clear();
        foreach (var process in metrics.RunningProcesses)
        {
            RunningProcesses.Add(process);
        }

        ActiveGames.Clear();
        var runningGames = GetRunningGames();
        if (runningGames.Count == 0)
        {
            ActiveGames.Add(new GameSession { Name = "No known game process detected", Status = "Idle" });
        }

        foreach (var game in runningGames)
        {
            ActiveGames.Add(game);
        }

        if (RecentOptimizations.Count == 0)
        {
            RecentOptimizations.Add(new RecentOptimizationEntry
            {
                Title = "No actions yet",
                Detail = "Power-plan changes made in SleepOS will appear here."
            });
        }

        SystemStatus = metrics.CpuUsagePercent is null && metrics.GpuUsagePercent is null
            ? "Metrics unavailable"
            : metrics.CpuUsagePercent is >= 85 || metrics.GpuUsagePercent is >= 90 ? "Under load" : "Nominal";

        OnPropertyChanged(nameof(HardwareCards));
        OnPropertyChanged(nameof(RunningProcesses));
        OnPropertyChanged(nameof(ActiveGames));
        OnPropertyChanged(nameof(GamingModeText));
        OnPropertyChanged(nameof(ActivePowerProfile));
        OnPropertyChanged(nameof(CurrentProfile));
        OnPropertyChanged(nameof(UptimeText));
        OnPropertyChanged(nameof(Greeting));
        OnPropertyChanged(nameof(SystemStatus));
        OnPropertyChanged(nameof(GamingModeStatus));
        OnPropertyChanged(nameof(AutoRefreshStatus));
        OnPropertyChanged(nameof(NetworkThroughputStatus));
        SchedulePerformanceAutomation(metrics, runningGames);
    }

    [RelayCommand]
    public async Task UpdateAppAsync()
    {
        if (!_versionService.CanUpdate)
        {
            UpdateStatus = "Install SleepOS with SleepOS-Setup.exe to enable in-app updates.";
            OnPropertyChanged(nameof(UpdateStatus));
            return;
        }

        UpdateStatus = "Checking GitHub Releases...";
        OnPropertyChanged(nameof(UpdateStatus));

        try
        {
            var availableVersion = await _versionService.CheckForUpdateAsync();
            if (availableVersion is null)
            {
                UpdateStatus = "SleepOS is up to date.";
                OnPropertyChanged(nameof(UpdateStatus));
                return;
            }

            UpdateStatus = $"Downloading SleepOS {availableVersion}. The app will restart to finish updating.";
            OnPropertyChanged(nameof(UpdateStatus));
            await _versionService.DownloadAndRestartAsync();
        }
        catch (HttpRequestException exception)
        {
            UpdateStatus = $"Could not reach the update source. Confirm that SchlafendesRelaxo/SleepOS is public and has a release. {exception.Message}";
        }
        catch (IOException exception)
        {
            UpdateStatus = $"Could not download the update: {exception.Message}";
        }
        catch (InvalidOperationException exception)
        {
            UpdateStatus = $"Could not apply the update: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            UpdateStatus = $"Windows denied access while updating: {exception.Message}";
        }
        catch (OperationCanceledException exception)
        {
            UpdateStatus = $"The update operation was interrupted: {exception.Message}";
        }
        catch (AcquireLockFailedException exception)
        {
            UpdateStatus = $"Another SleepOS update operation is already running: {exception.Message}";
        }
        catch (ChecksumFailedException exception)
        {
            UpdateStatus = $"The downloaded update failed its integrity check: {exception.Message}";
        }
        catch (NotInstalledException exception)
        {
            UpdateStatus = $"Install SleepOS with SleepOS-Setup.exe to enable in-app updates: {exception.Message}";
        }

        OnPropertyChanged(nameof(UpdateStatus));
    }

    [RelayCommand]
    public Task ScanDuplicateDesktopShortcutsAsync()
    {
        DesktopShortcutStatus = "Scanning user and shared Desktop shortcuts...";
        DuplicateDesktopShortcuts.Clear();
        DesktopShortcutWarnings.Clear();
        OnPropertyChanged(nameof(DesktopShortcutStatus));
        OnPropertyChanged(nameof(CanRemoveDuplicateShortcuts));

        try
        {
            var result = _desktopShortcutService.FindDuplicates();
            foreach (var duplicate in result.Duplicates)
            {
                DuplicateDesktopShortcuts.Add(duplicate);
            }

            foreach (var warning in result.Warnings)
            {
                DesktopShortcutWarnings.Add(warning);
            }

            DesktopShortcutStatus = result.Duplicates.Count == 0
                ? "No duplicate Windows shortcuts found on the Desktop."
                : $"Found {result.Duplicates.Count:N0} duplicate shortcut(s). The first shortcut for each target is kept.";
            if (result.Warnings.Count > 0)
            {
                DesktopShortcutStatus += $" {result.Warnings.Count:N0} item(s) could not be inspected.";
            }
        }
        catch (InvalidOperationException exception)
        {
            DesktopShortcutStatus = $"Desktop shortcut scan failed: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            DesktopShortcutStatus = $"Desktop shortcut scan failed: {exception.Message}";
        }
        catch (IOException exception)
        {
            DesktopShortcutStatus = $"Desktop shortcut scan failed: {exception.Message}";
        }
        catch (PlatformNotSupportedException exception)
        {
            DesktopShortcutStatus = $"Desktop shortcut scan is unavailable: {exception.Message}";
        }

        OnPropertyChanged(nameof(DesktopShortcutStatus));
        OnPropertyChanged(nameof(DuplicateDesktopShortcuts));
        OnPropertyChanged(nameof(DesktopShortcutWarnings));
        OnPropertyChanged(nameof(CanRemoveDuplicateShortcuts));
        return Task.CompletedTask;
    }

    public async Task RemoveSelectedDuplicateDesktopShortcutsAsync()
    {
        var selected = DuplicateDesktopShortcuts.Where(shortcut => shortcut.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            DesktopShortcutStatus = "Select at least one duplicate shortcut first.";
            OnPropertyChanged(nameof(DesktopShortcutStatus));
            return;
        }

        try
        {
            var result = _desktopShortcutService.RemoveSelectedDuplicates(selected);
            await ScanDuplicateDesktopShortcutsAsync();
            DesktopShortcutStatus = $"{result.RemovedCount:N0} shortcut(s) moved to the Recycle Bin.";
            if (result.Failures.Count > 0)
            {
                DesktopShortcutStatus += $" {result.Failures.Count:N0} shortcut(s) could not be removed.";
                foreach (var failure in result.Failures)
                {
                    DesktopShortcutWarnings.Add(failure);
                }
            }
        }
        catch (InvalidOperationException exception)
        {
            DesktopShortcutStatus = $"Could not remove selected shortcuts: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            DesktopShortcutStatus = $"Could not remove selected shortcuts: {exception.Message}";
        }
        catch (IOException exception)
        {
            DesktopShortcutStatus = $"Could not remove selected shortcuts: {exception.Message}";
        }

        OnPropertyChanged(nameof(DesktopShortcutStatus));
        OnPropertyChanged(nameof(DuplicateDesktopShortcuts));
        OnPropertyChanged(nameof(DesktopShortcutWarnings));
        OnPropertyChanged(nameof(CanRemoveDuplicateShortcuts));
    }

    [RelayCommand]
    public void ScanNetworkAdapters()
    {
        var result = _networkDiagnosticsService.GetAdapters();
        NetworkAdapters.Clear();
        NetworkAdapterWarnings.Clear();
        foreach (var adapter in result.Adapters)
        {
            NetworkAdapters.Add(adapter);
        }

        foreach (var warning in result.Warnings)
        {
            NetworkAdapterWarnings.Add(warning);
        }

        NetworkAdapterStatus = result.Adapters.Count == 0
            ? "Windows did not report any network adapters."
            : $"Found {result.Adapters.Count:N0} network adapter(s).";
        if (result.Warnings.Count > 0)
        {
            NetworkAdapterStatus += $" {result.Warnings.Count:N0} adapter warning(s) are listed below.";
        }

        OnPropertyChanged(nameof(NetworkAdapters));
        OnPropertyChanged(nameof(NetworkAdapterWarnings));
        OnPropertyChanged(nameof(NetworkAdapterStatus));
    }

    [RelayCommand]
    public async Task RunNetworkPingTestAsync()
    {
        string target;
        try
        {
            target = NetworkDiagnosticsService.ValidateTarget(NetworkPingTarget);
        }
        catch (ArgumentException exception)
        {
            NetworkPingStatus = $"Test not started: {exception.Message}";
            OnPropertyChanged(nameof(NetworkPingStatus));
            return;
        }

        NetworkPingStatus = $"Sending five ICMP probes to {target}...";
        NetworkPingSamples.Clear();
        OnPropertyChanged(nameof(NetworkPingStatus));
        OnPropertyChanged(nameof(NetworkPingSamples));

        try
        {
            var result = await _networkDiagnosticsService.TestPingAsync(target);
            foreach (var sample in result.Samples)
            {
                NetworkPingSamples.Add(sample);
            }

            NetworkAverageText = FormatMilliseconds(result.Summary.AverageMilliseconds);
            NetworkJitterText = FormatMilliseconds(result.Summary.JitterMilliseconds);
            NetworkPacketLossText = result.Summary.PacketLossPercent is { } packetLoss
                ? $"{packetLoss:F0}%"
                : "N/A";
            NetworkPingStatus = $"{result.Summary.PacketsReceived} of {result.Summary.PacketsSent} replies from {target}. Jitter is mean variation between successful replies.";
            NetworkPingHistory.Insert(0, result.Summary);
            while (NetworkPingHistory.Count > 10)
            {
                NetworkPingHistory.RemoveAt(NetworkPingHistory.Count - 1);
            }
        }
        catch (PingException exception)
        {
            NetworkPingStatus = $"Ping test failed: {exception.Message}";
        }
        catch (SocketException exception)
        {
            NetworkPingStatus = $"Ping test failed: {exception.Message}";
        }

        OnPropertyChanged(nameof(NetworkPingStatus));
        OnPropertyChanged(nameof(NetworkAverageText));
        OnPropertyChanged(nameof(NetworkJitterText));
        OnPropertyChanged(nameof(NetworkPacketLossText));
        OnPropertyChanged(nameof(NetworkPingSamples));
        OnPropertyChanged(nameof(NetworkPingHistory));
    }

    [RelayCommand]
    public void ToggleGamingMode()
    {
        PerformanceMode = GamingModeEnabled ? "Balanced" : "Gaming";
    }

    [RelayCommand]
    public void ApplyProfile(string profileName)
    {
        var profile = Profiles.FirstOrDefault(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            return;
        }

        if (!_systemService.SetPowerProfile(profile.Name))
        {
            ActionStatus = $"{profile.Name} could not be applied. Choose a Windows power plan profile.";
            OnPropertyChanged(nameof(ActionStatus));
            return;
        }

        var settings = _settingsStore.Load();
        settings.LastSelectedProfile = profile.Name;
        if (!SaveSettings(settings))
        {
            return;
        }

        UpdateSettings(settings);
        RefreshMetrics();
        RecentOptimizations.Clear();
        RecentOptimizations.Add(new RecentOptimizationEntry
        {
            Title = "Power plan applied",
            Detail = $"Selected {profile.Name}; Windows reports {ActivePowerProfile}."
        });
        OnPropertyChanged(nameof(RecentOptimizations));
        ActionStatus = $"Windows power plan changed to {ActivePowerProfile}.";
        OnPropertyChanged(nameof(ActionStatus));
    }

    [RelayCommand]
    public void ToggleAutomaticHighCpuAnalysis()
    {
        var settings = _settingsStore.Load();
        settings.AutomaticHighCpuAnalysisEnabled = !settings.AutomaticHighCpuAnalysisEnabled;
        if (!SaveSettings(settings))
        {
            return;
        }

        AutomaticHighCpuAnalysisEnabled = settings.AutomaticHighCpuAnalysisEnabled;
        OnPropertyChanged(nameof(AutomaticHighCpuAnalysisEnabled));
    }

    [RelayCommand]
    public void RestartExplorer()
    {
        try
        {
            _systemService.RestartExplorer();
            ActionStatus = "File Explorer restarted.";
        }
        catch (InvalidOperationException exception)
        {
            ActionStatus = $"Could not restart File Explorer: {exception.Message}";
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            ActionStatus = $"Could not restart File Explorer: {exception.Message}";
        }

        OnPropertyChanged(nameof(ActionStatus));
    }

    [RelayCommand]
    public void ClearDnsCache()
    {
        try
        {
            _systemService.ClearDnsCache();
            ActionStatus = "DNS cache cleared.";
        }
        catch (InvalidOperationException exception)
        {
            ActionStatus = $"Could not clear DNS cache: {exception.Message}";
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            ActionStatus = $"Could not clear DNS cache: {exception.Message}";
        }

        OnPropertyChanged(nameof(ActionStatus));
    }

    [RelayCommand]
    public void ToggleAutoRefresh()
    {
        var settings = _settingsStore.Load();
        settings.AutoRefreshEnabled = !settings.AutoRefreshEnabled;
        MarkPerformanceModeCustom(settings);
        if (!SaveSettings(settings))
        {
            return;
        }

        AutoRefreshEnabled = settings.AutoRefreshEnabled;
        SetProperty(ref _performanceMode, "Custom", nameof(PerformanceMode));
        GamingModeEnabled = false;
        GamingModeText = "Gaming policy: Off";
        ActionStatus = $"Automatic system refresh {AutoRefreshStatus.ToLowerInvariant()}.";
        NotifyPerformanceSettingsChanged();
        OnPropertyChanged(nameof(ActionStatus));
    }

    [RelayCommand]
    public void TogglePageAnimations()
    {
        PageAnimationsEnabled = !PageAnimationsEnabled;
    }

    [RelayCommand]
    public void ScanTemporaryFiles()
    {
        try
        {
            var summary = _cleanupService.Scan();
            CanCleanTemporaryFiles = summary.Files > 0;
            CleanupStatus = summary.Files == 0
                ? "No temporary files older than 30 days were found."
                : $"{FormatBytes(summary.Bytes)} in {summary.Files:N0} temporary files can be reviewed and cleaned.";
            if (summary.SkippedItems > 0)
            {
                CleanupStatus += $" {summary.SkippedItems:N0} item(s) could not be inspected.";
            }
        }
        catch (IOException exception)
        {
            CleanupStatus = $"Temporary-file scan failed: {exception.Message}";
            CanCleanTemporaryFiles = false;
        }
        catch (UnauthorizedAccessException exception)
        {
            CleanupStatus = $"Temporary-file scan failed: {exception.Message}";
            CanCleanTemporaryFiles = false;
        }

        OnPropertyChanged(nameof(CleanupStatus));
        OnPropertyChanged(nameof(CanCleanTemporaryFiles));
    }

    [RelayCommand]
    public void CleanTemporaryFiles()
    {
        if (!CanCleanTemporaryFiles)
        {
            return;
        }

        try
        {
            var result = _cleanupService.Clean();
            CanCleanTemporaryFiles = false;
            CleanupStatus = $"Cleaned {result.Files:N0} file(s), freeing {FormatBytes(result.Bytes)}.";
            if (result.SkippedItems > 0)
            {
                CleanupStatus += $" {result.SkippedItems:N0} locked or inaccessible item(s) were left alone.";
            }
        }
        catch (IOException exception)
        {
            CleanupStatus = $"Cleanup failed: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            CleanupStatus = $"Cleanup failed: {exception.Message}";
        }

        OnPropertyChanged(nameof(CleanupStatus));
        OnPropertyChanged(nameof(CanCleanTemporaryFiles));
    }

    [RelayCommand]
    public async Task ScanStoppableProcessesAsync()
    {
        ProcessManagementStatus = "Scanning running apps...";
        ManageableProcesses.Clear();
        OnPropertyChanged(nameof(ManageableProcessCount));
        OnPropertyChanged(nameof(RecommendedProcessCount));
        OnPropertyChanged(nameof(ProcessManagementStatus));

        try
        {
            var result = await Task.Run(() =>
            {
                var activeGameProcessNames = GetRunningGames()
                    .Where(game => string.Equals(game.Status, "Running", StringComparison.OrdinalIgnoreCase))
                    .Select(game => game.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                return _processManagementService.GetStoppableProcesses(activeGameProcessNames);
            });
            var managedPaths = (_settingsStore.Load().ManagedApplications ?? [])
                .Where(rule => rule.Enabled)
                .Select(rule => rule.ExecutablePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var process in result.Processes)
            {
                process.IsMemorySaverManaged = managedPaths.Contains(process.ExecutablePath);
                ManageableProcesses.Add(process);
            }

            var recommendedCount = result.Processes.Count(process => process.IsSuggestedForReview);
            ProcessManagementStatus = result.Processes.Count == 0
                ? "No eligible non-Windows apps were found."
                : $"{result.Processes.Count:N0} app process(es) found; {recommendedCount:N0} hidden background app(s) to review. RAM use alone never triggers a suggestion.";
            if (result.SkippedProcesses > 0)
            {
                ProcessManagementStatus += $" {result.SkippedProcesses:N0} process(es) could not be inspected.";
            }
        }
        catch (Win32Exception exception)
        {
            ProcessManagementStatus = $"Process scan failed: {exception.Message}";
        }
        catch (InvalidOperationException exception)
        {
            ProcessManagementStatus = $"Process scan failed: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            ProcessManagementStatus = $"Process scan failed: {exception.Message}";
        }
        catch (IOException exception)
        {
            ProcessManagementStatus = $"Process scan failed: {exception.Message}";
        }

        OnPropertyChanged(nameof(ManageableProcessCount));
        OnPropertyChanged(nameof(RecommendedProcessCount));
        OnPropertyChanged(nameof(ProcessManagementStatus));
    }

    public async Task StopSelectedProcessesAsync()
    {
        var selectedProcesses = SelectedStoppableProcesses;
        if (selectedProcesses.Count == 0)
        {
            ProcessManagementStatus = "Select at least one app process first.";
            OnPropertyChanged(nameof(ProcessManagementStatus));
            return;
        }

        try
        {
            var result = await Task.Run(() => _processManagementService.StopSelectedProcesses(selectedProcesses));
            var summary = $"{result.StoppedCount:N0} process(es) stopped.";
            if (result.Failures.Count > 0)
            {
                summary += $" {result.Failures.Count:N0} could not be stopped: {string.Join(" ", result.Failures.Take(3))}";
            }

            await ScanStoppableProcessesAsync();
            ProcessManagementStatus = $"{summary} {ProcessManagementStatus}";
        }
        catch (Win32Exception exception)
        {
            ProcessManagementStatus = $"Could not stop selected processes: {exception.Message}";
        }
        catch (InvalidOperationException exception)
        {
            ProcessManagementStatus = $"Could not stop selected processes: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            ProcessManagementStatus = $"Could not stop selected processes: {exception.Message}";
        }
        catch (IOException exception)
        {
            ProcessManagementStatus = $"Could not stop selected processes: {exception.Message}";
        }

        OnPropertyChanged(nameof(ManageableProcessCount));
        OnPropertyChanged(nameof(RecommendedProcessCount));
        OnPropertyChanged(nameof(ProcessManagementStatus));
    }

    [RelayCommand]
    public void AddSelectedToMemorySaver()
    {
        var selected = SelectedStoppableProcesses;
        if (selected.Count == 0)
        {
            MemorySaverStatus = "Select one or more app processes in the list first.";
            OnPropertyChanged(nameof(MemorySaverStatus));
            return;
        }

        foreach (var process in selected)
        {
            if (ManagedApplications.Any(rule => string.Equals(rule.ExecutablePath, process.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            ManagedApplications.Add(new ApplicationPerformanceRule
            {
                ExecutablePath = process.ExecutablePath,
                ProcessName = process.Name,
                InactivityMinutes = 10,
                Enabled = true
            });
            process.IsMemorySaverManaged = true;
        }

        SaveMemorySaverRules();
    }

    [RelayCommand]
    public void ApplyEfficiencyModeToSelected()
    {
        var selected = SelectedStoppableProcesses;
        if (selected.Count == 0)
        {
            ProcessManagementStatus = "Select one or more eligible app processes first.";
        }
        else
        {
            var result = _processManagementService.SetEfficiencyMode(selected, enabled: true);
            ProcessManagementStatus = $"{result.UpdatedCount:N0} process(es) now use Windows efficiency mode.";
            if (result.Failures.Count > 0)
            {
                ProcessManagementStatus += $" {result.Failures.Count:N0} could not be changed: {string.Join(" ", result.Failures.Take(2))}";
            }
        }

        OnPropertyChanged(nameof(ProcessManagementStatus));
    }

    [RelayCommand]
    public void RestoreEfficiencyModeForSelected()
    {
        var selected = SelectedStoppableProcesses;
        if (selected.Count == 0)
        {
            ProcessManagementStatus = "Select one or more eligible app processes first.";
        }
        else
        {
            var result = _processManagementService.SetEfficiencyMode(selected, enabled: false);
            ProcessManagementStatus = $"{result.UpdatedCount:N0} process efficiency policy(ies) restored.";
            if (result.Failures.Count > 0)
            {
                ProcessManagementStatus += $" {result.Failures.Count:N0} could not be restored: {string.Join(" ", result.Failures.Take(2))}";
            }
        }

        OnPropertyChanged(nameof(ProcessManagementStatus));
    }

    [RelayCommand]
    public void SetSelectedProcessesToBelowNormal()
    {
        SetSelectedProcessPriority(System.Diagnostics.ProcessPriorityClass.BelowNormal);
    }

    [RelayCommand]
    public void RestoreSelectedProcessPriorities()
    {
        var selected = SelectedStoppableProcesses;
        if (selected.Count == 0)
        {
            ProcessManagementStatus = "Select one or more eligible app processes first.";
        }
        else
        {
            var result = _processManagementService.SetPriority(selected, priorityClass: null);
            ProcessManagementStatus = $"{result.UpdatedCount:N0} process priority(ies) restored.";
            if (result.Failures.Count > 0)
            {
                ProcessManagementStatus += $" {result.Failures.Count:N0} could not be restored: {string.Join(" ", result.Failures.Take(2))}";
            }
        }

        OnPropertyChanged(nameof(ProcessManagementStatus));
    }

    [RelayCommand]
    public void RemoveMemorySaverRule(ApplicationPerformanceRule rule)
    {
        if (ManagedApplications.Remove(rule))
        {
            SaveMemorySaverRules();
        }
    }

    [RelayCommand]
    public void AddSelectedToGameProfiles()
    {
        var selected = SelectedStoppableProcesses;
        if (selected.Count == 0)
        {
            GameProfileStatus = "Select one or more running app processes in the list first.";
            OnPropertyChanged(nameof(GameProfileStatus));
            return;
        }

        var settings = _settingsStore.Load();
        settings.GameProfiles ??= [];
        foreach (var process in selected)
        {
            if (!settings.GameProfiles.Any(profile =>
                    string.Equals(profile.ExecutablePath, process.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
            {
                settings.GameProfiles.Add(new GamePerformanceProfile
                {
                    ExecutablePath = process.ExecutablePath,
                    ProcessName = process.Name,
                    PerformanceMode = "Gaming",
                    Enabled = true
                });
            }
        }

        if (!SaveSettings(settings))
        {
            return;
        }

        UpdateGameProcessProfiles(settings.GameProfiles);
        GameProfileStatus = $"{GameProcessProfiles.Count:N0} custom game profile(s) saved. Automatic profile switching applies only when enabled in Settings.";
        OnPropertyChanged(nameof(GameProfileStatus));
    }

    [RelayCommand]
    public void RemoveGameProcessProfile(GamePerformanceProfile profile)
    {
        var settings = _settingsStore.Load();
        settings.GameProfiles ??= [];
        settings.GameProfiles.RemoveAll(candidate =>
            string.Equals(candidate.ExecutablePath, profile.ExecutablePath, StringComparison.OrdinalIgnoreCase));
        if (!SaveSettings(settings))
        {
            return;
        }

        UpdateGameProcessProfiles(settings.GameProfiles);
        GameProfileStatus = $"Removed {profile.ProcessName} from custom game detection.";
        OnPropertyChanged(nameof(GameProfileStatus));
    }

    [RelayCommand]
    public void SaveGameProfiles()
    {
        var settings = _settingsStore.Load();
        settings.GameProfiles = GameProcessProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.ExecutablePath))
            .Select(profile => new GamePerformanceProfile
            {
                ExecutablePath = Path.GetFullPath(profile.ExecutablePath),
                ProcessName = profile.ProcessName,
                PerformanceMode = NormalizeGamePerformanceMode(profile.PerformanceMode),
                Enabled = profile.Enabled
            })
            .DistinctBy(profile => profile.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (SaveSettings(settings))
        {
            GameProfileStatus = $"{settings.GameProfiles.Count:N0} custom game profile(s) saved.";
            OnPropertyChanged(nameof(GameProfileStatus));
        }
    }

    [RelayCommand]
    public void SaveMemorySaverRules()
    {
        var settings = _settingsStore.Load();
        settings.ManagedApplications = ManagedApplications
            .Where(rule => !string.IsNullOrWhiteSpace(rule.ExecutablePath))
            .Select(rule => new ApplicationPerformanceRule
            {
                ExecutablePath = Path.GetFullPath(rule.ExecutablePath),
                ProcessName = rule.ProcessName,
                InactivityMinutes = MemorySaverInactivityOptions.Contains(rule.InactivityMinutes) ? rule.InactivityMinutes : 10,
                Enabled = rule.Enabled
            })
            .DistinctBy(rule => rule.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!SaveSettings(settings))
        {
            return;
        }

        foreach (var process in ManageableProcesses)
        {
            process.IsMemorySaverManaged = settings.ManagedApplications.Any(rule =>
                rule.Enabled && string.Equals(rule.ExecutablePath, process.ExecutablePath, StringComparison.OrdinalIgnoreCase));
        }

        MemorySaverStatus = $"{settings.ManagedApplications.Count(rule => rule.Enabled):N0} app rule(s) saved. Only selected, eligible apps can be changed.";
        OnPropertyChanged(nameof(ManagedApplications));
        OnPropertyChanged(nameof(MemorySaverStatus));
    }

    [RelayCommand]
    public void ToggleAutomaticMemorySaver()
    {
        var settings = _settingsStore.Load();
        settings.AutomaticMemorySaverEnabled = !settings.AutomaticMemorySaverEnabled;
        if (!SaveSettings(settings))
        {
            return;
        }

        AutomaticMemorySaverEnabled = settings.AutomaticMemorySaverEnabled;
        MemorySaverStatus = AutomaticMemorySaverEnabled
            ? "Automatic Memory Saver is on. It only uses EcoQoS for opted-in, visible apps after their idle threshold and at high CPU or memory load."
            : "Automatic Memory Saver is off. Existing SleepOS-applied efficiency settings will be restored on the next evaluation.";
        OnPropertyChanged(nameof(AutomaticMemorySaverEnabled));
        OnPropertyChanged(nameof(MemorySaverStatus));
    }

    [RelayCommand]
    public void ToggleAutomaticGamingMode()
    {
        var settings = _settingsStore.Load();
        settings.AutomaticGamingModeEnabled = !settings.AutomaticGamingModeEnabled;
        if (!SaveSettings(settings))
        {
            return;
        }

        AutomaticGamingModeEnabled = settings.AutomaticGamingModeEnabled;
        ActionStatus = AutomaticGamingModeEnabled
            ? "Automatic gaming detection is on. SleepOS will apply its selected-app background policy and restore it after the game ends."
            : "Automatic gaming detection is off. SleepOS-applied policies will be restored on the next evaluation.";
        OnPropertyChanged(nameof(AutomaticGamingModeEnabled));
        OnPropertyChanged(nameof(ActionStatus));
    }

    [RelayCommand]
    public void ToggleAutoStart()
    {
        var settings = _settingsStore.Load();
        settings.AutoStartEnabled = !settings.AutoStartEnabled;
        try
        {
            _startupService.Configure(settings.AutoStartEnabled, StartupDelaySeconds, StartMinimized);
            if (!SaveSettings(settings))
            {
                _startupService.Configure(AutoStartEnabled, StartupDelaySeconds, StartMinimized);
                return;
            }

            AutoStartEnabled = settings.AutoStartEnabled;
            StartupStatus = AutoStartEnabled
                ? "SleepOS is registered to start for this Windows user."
                : "SleepOS will not start automatically at Windows sign-in.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or IOException or PlatformNotSupportedException or System.Security.SecurityException)
        {
            StartupStatus = $"Could not update Windows sign-in startup: {exception.Message}";
        }

        OnPropertyChanged(nameof(AutoStartEnabled));
        OnPropertyChanged(nameof(IsStartupDelayEnabled));
        OnPropertyChanged(nameof(StartupStatus));
    }

    [RelayCommand]
    public async Task CapturePerformanceBenchmarkAsync()
    {
        BenchmarkStatus = "Capturing actual Windows CPU, memory, and background-process readings...";
        OnPropertyChanged(nameof(BenchmarkStatus));

        try
        {
            var sample = await Task.Run(CaptureBenchmarkSnapshot);
            var settings = _settingsStore.Load();
            settings.BenchmarkHistory ??= [];
            var previous = settings.BenchmarkHistory.LastOrDefault();
            sample.Label = $"Snapshot {settings.BenchmarkHistory.Count + 1:N0}";
            settings.BenchmarkHistory.Add(sample);
            settings.BenchmarkHistory = settings.BenchmarkHistory.TakeLast(20).ToList();
            if (!SaveSettings(settings))
            {
                return;
            }

            BenchmarkHistory.Add(sample);
            while (BenchmarkHistory.Count > 20)
            {
                BenchmarkHistory.RemoveAt(0);
            }

            BenchmarkStatus = previous is null
                ? $"Baseline captured at {sample.CapturedAt.ToLocalTime():t}: CPU {FormatPercent(sample.CpuUsagePercent)}, RAM {FormatGigabytes(sample.RamUsedGb)} of {FormatGigabytes(sample.TotalRamGb)}, {sample.BackgroundProcessCount:N0} background process(es), {sample.UninspectedProcessCount:N0} uninspected."
                : FormatBenchmarkDifference(previous, sample);
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or IOException or System.ComponentModel.Win32Exception)
        {
            BenchmarkStatus = $"Benchmark capture failed: {exception.Message}";
        }

        OnPropertyChanged(nameof(BenchmarkStatus));
        OnPropertyChanged(nameof(BenchmarkHistory));
    }

    public void RestorePerformancePolicies()
    {
        _isClosing = true;
        var efficiencyResult = _processManagementService.RestoreEfficiencyModes();
        var priorityResult = _processManagementService.RestoreProcessPriorities();
        var failures = efficiencyResult.Failures.Concat(priorityResult.Failures).ToArray();
        if (failures.Length > 0)
        {
            foreach (var failure in failures)
            {
                Trace.TraceError("SleepOS could not restore a temporary process policy: {0}", failure);
            }

            MemorySaverStatus = $"Restored {efficiencyResult.UpdatedCount:N0} efficiency and {priorityResult.UpdatedCount:N0} priority policy(ies); {failures.Length:N0} could not be restored.";
            OnPropertyChanged(nameof(MemorySaverStatus));
        }
    }

    public void EvaluatePerformanceAutomation()
    {
        if (_isClosing || !HasPerformanceAutomation ||
            DateTime.UtcNow - _lastAutomationRunUtc < TimeSpan.FromSeconds(30))
        {
            return;
        }

        try
        {
            var metrics = _monitorService.Capture();
            SchedulePerformanceAutomation(metrics, GetRunningGames());
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or IOException or System.ComponentModel.Win32Exception)
        {
            Trace.TraceError("Automatic SleepOS performance readings failed: {0}", exception);
            MemorySaverStatus = $"Automatic performance readings failed: {exception.Message}";
            OnPropertyChanged(nameof(MemorySaverStatus));
        }
    }

    [RelayCommand]
    public void OpenWindowsUpdateSettings() => OpenWindowsSettings(
        _systemService.OpenWindowsUpdateSettings, "Windows Update");

    [RelayCommand]
    public void OpenWindowsStorageSettings() => OpenWindowsSettings(
        _systemService.OpenWindowsStorageSettings, "Windows Storage settings");

    [RelayCommand]
    public void OpenWindowsPowerSettings() => OpenWindowsSettings(
        _systemService.OpenWindowsPowerSettings, "Windows Power settings");

    private void OpenWindowsSettings(Action openSettings, string pageName)
    {
        try
        {
            openSettings();
            ActionStatus = $"{pageName} opened. Review changes in Windows before applying them.";
        }
        catch (InvalidOperationException exception)
        {
            ActionStatus = $"Could not open {pageName}: {exception.Message}";
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            ActionStatus = $"Could not open {pageName}: {exception.Message}";
        }

        OnPropertyChanged(nameof(ActionStatus));
    }

    private bool SaveSettings(AppSettings settings)
    {
        try
        {
            _settingsStore.Save(settings);
            return true;
        }
        catch (IOException exception)
        {
            ActionStatus = $"Could not save settings: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            ActionStatus = $"Could not save settings: {exception.Message}";
        }

        OnPropertyChanged(nameof(ActionStatus));
        return false;
    }

    private void SaveStartupSettings()
    {
        var settings = _settingsStore.Load();
        var oldStartMinimized = settings.StartMinimized;
        var oldStartupDelay = settings.StartupDelaySeconds;
        settings.StartMinimized = StartMinimized;
        settings.StartupDelaySeconds = StartupDelaySeconds;
        if (!SaveSettings(settings))
        {
            _startMinimized = oldStartMinimized;
            _startupDelaySeconds = oldStartupDelay;
            OnPropertyChanged(nameof(StartMinimized));
            OnPropertyChanged(nameof(StartupDelaySeconds));
            return;
        }

        if (!AutoStartEnabled)
        {
            return;
        }

        try
        {
            _startupService.Configure(true, StartupDelaySeconds, StartMinimized);
            StartupStatus = $"SleepOS starts at sign-in with a {StartupDelaySeconds}-second delay" +
                (StartMinimized ? " and opens in the tray." : ".");
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or IOException or PlatformNotSupportedException or System.Security.SecurityException)
        {
            StartupStatus = $"Could not update Windows sign-in options: {exception.Message}";
        }

        OnPropertyChanged(nameof(StartupStatus));
    }

    private void SetSelectedProcessPriority(System.Diagnostics.ProcessPriorityClass priorityClass)
    {
        var selected = SelectedStoppableProcesses;
        if (selected.Count == 0)
        {
            ProcessManagementStatus = "Select one or more eligible app processes first.";
        }
        else
        {
            var result = _processManagementService.SetPriority(selected, priorityClass);
            ProcessManagementStatus = $"{result.UpdatedCount:N0} process(es) set to {priorityClass}.";
            if (result.Failures.Count > 0)
            {
                ProcessManagementStatus += $" {result.Failures.Count:N0} could not be changed: {string.Join(" ", result.Failures.Take(2))}";
            }
        }

        OnPropertyChanged(nameof(ProcessManagementStatus));
    }

    private void RefreshStartupStatus()
    {
        try
        {
            AutoStartEnabled = _startupService.IsEnabled;
            StartupStatus = AutoStartEnabled
                ? $"SleepOS starts at sign-in with a {StartupDelaySeconds}-second delay" +
                    (StartMinimized ? " and opens in the tray." : ".")
                : "SleepOS is not registered for Windows sign-in startup.";
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException or PlatformNotSupportedException)
        {
            StartupStatus = $"Could not read Windows sign-in startup: {exception.Message}";
        }

        OnPropertyChanged(nameof(AutoStartEnabled));
        OnPropertyChanged(nameof(IsStartupDelayEnabled));
        OnPropertyChanged(nameof(StartupStatus));
    }

    private PerformanceBenchmarkRecord CaptureBenchmarkSnapshot()
    {
        var metrics = _monitorService.Capture();
        var backgroundProcesses = 0;
        var uninspectedProcesses = 0;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id != Environment.ProcessId && process.MainWindowHandle == IntPtr.Zero)
                {
                    backgroundProcesses++;
                }
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
            {
                uninspectedProcesses++;
            }
            finally
            {
                process.Dispose();
            }
        }

        var memoryLoad = metrics.RamUsedGb is { } used && metrics.TotalRamGb is { } total && total > 0
            ? used / total * 100d
            : (double?)null;
        var measuredState = metrics.CpuUsagePercent is >= 85
            ? "High CPU load"
            : memoryLoad is >= 85
                ? "High memory load"
                : metrics.CpuUsagePercent is null && memoryLoad is null
                    ? "Metrics unavailable"
                    : "No high CPU or memory threshold observed";

        return new PerformanceBenchmarkRecord
        {
            CapturedAt = DateTimeOffset.Now,
            CpuUsagePercent = metrics.CpuUsagePercent,
            RamUsedGb = metrics.RamUsedGb,
            TotalRamGb = metrics.TotalRamGb,
            BackgroundProcessCount = backgroundProcesses,
            UninspectedProcessCount = uninspectedProcesses,
            SystemState = $"{measuredState}; mode {_performanceMode}"
        };
    }

    private static string FormatBenchmarkDifference(PerformanceBenchmarkRecord before, PerformanceBenchmarkRecord after)
    {
        var elapsed = after.CapturedAt - before.CapturedAt;
        var cpuDifference = before.CpuUsagePercent is { } beforeCpu && after.CpuUsagePercent is { } afterCpu
            ? $"{afterCpu - beforeCpu:+0.0;-0.0;0.0} percentage points CPU"
            : "CPU unavailable";
        var ramDifference = before.RamUsedGb is { } beforeRam && after.RamUsedGb is { } afterRam
            ? $"{afterRam - beforeRam:+0.00;-0.00;0.00} GB RAM"
            : "RAM unavailable";
        var processDifference = after.BackgroundProcessCount - before.BackgroundProcessCount;
        return $"Observed change since {before.CapturedAt.ToLocalTime():t} ({elapsed.TotalMinutes:F0} min): {cpuDifference}, {ramDifference}, {processDifference:+0;-0;0} background process(es). Latest scan could not inspect {after.UninspectedProcessCount:N0} process(es). Measurements only; this does not attribute changes to SleepOS or imply FPS gains.";
    }

    private void SchedulePerformanceAutomation(SystemMetrics metrics, IReadOnlyList<GameSession> runningGames)
    {
        if (_isClosing || DateTime.UtcNow - _lastAutomationRunUtc < TimeSpan.FromSeconds(30) ||
            Interlocked.CompareExchange(ref _automationInProgress, 1, 0) != 0)
        {
            return;
        }

        var settings = _settingsStore.Load();
        var games = runningGames
            .Where(game => string.Equals(game.Status, "Running", StringComparison.OrdinalIgnoreCase))
            .Select(game => game.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var gameActive = games.Count > 0;
        if (!settings.AutomaticMemorySaverEnabled &&
            !(settings.AutomaticGamingModeEnabled && gameActive) &&
            !settings.AutomaticHighCpuAnalysisEnabled &&
            _automaticEfficiencyProcesses.Count == 0)
        {
            Interlocked.Exchange(ref _automationInProgress, 0);
            return;
        }

        _lastAutomationRunUtc = DateTime.UtcNow;
        _ = Task.Run(() =>
        {
            try
            {
                var scan = _processManagementService.GetStoppableProcesses(games);
                if (settings.AutomaticHighCpuAnalysisEnabled && metrics.CpuUsagePercent is >= 85)
                {
                    var topCpuProcesses = scan.Processes
                        .Where(process => process.CpuUsagePercent is not null)
                        .OrderByDescending(process => process.CpuUsagePercent)
                        .Take(3)
                        .Select(process => $"{process.Name} {process.CpuUsagePercent:F1}%")
                        .ToArray();
                    var cpuMessage = topCpuProcesses.Length == 0
                        ? "High total CPU load detected; per-process CPU readings are not available yet."
                        : $"High total CPU load. Highest eligible process readings: {string.Join(", ", topCpuProcesses)}.";
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        MemorySaverStatus = cpuMessage;
                        OnPropertyChanged(nameof(MemorySaverStatus));
                    });
                }

                var activeGameMode = runningGames
                    .FirstOrDefault(game => string.Equals(game.Status, "Running", StringComparison.OrdinalIgnoreCase))
                    ?.PerformanceMode ?? "Gaming";
                var policyName = settings.AutomaticGamingModeEnabled && gameActive ? activeGameMode : _performanceMode;
                var policy = PerformancePolicyRules.GetMode(policyName);
                var memoryLoad = metrics.RamUsedGb is { } used && metrics.TotalRamGb is { } total && total > 0
                    ? used / total * 100d
                    : (double?)null;
                var seen = new HashSet<(int ProcessId, long StartTimeTicks)>();

                foreach (var process in scan.Processes)
                {
                    if (_isClosing)
                    {
                        break;
                    }

                    var key = (process.ProcessId, process.StartTimeUtcTicks);
                    seen.Add(key);
                    var rule = (settings.ManagedApplications ?? []).FirstOrDefault(candidate =>
                        candidate.Enabled &&
                        string.Equals(candidate.ExecutablePath, process.ExecutablePath, StringComparison.OrdinalIgnoreCase));
                    var userOptedIn = rule is not null &&
                        (settings.AutomaticGamingModeEnabled && gameActive || settings.AutomaticMemorySaverEnabled && policy.Name != "Gaming");
                    var shouldApply = rule is not null && PerformancePolicyRules.ShouldEnableEfficiencyMode(
                        policy,
                        userOptedIn,
                        isProtectedProcess: false,
                        isGame: games.Contains(process.Name),
                        hasVisibleWindow: process.HasVisibleWindow,
                        isForeground: process.IsForeground,
                        memoryLoadPercent: memoryLoad,
                        inactiveFor: process.InactiveFor,
                        inactivityThresholdMinutes: rule.InactivityMinutes,
                        cpuLoadPercent: metrics.CpuUsagePercent);

                    if (shouldApply)
                    {
                        var result = _processManagementService.SetEfficiencyMode([process], enabled: true);
                        if (result.UpdatedCount > 0)
                        {
                            _automaticEfficiencyProcesses[key] = process;
                        }
                    }
                    else if (_automaticEfficiencyProcesses.TryRemove(key, out _))
                    {
                        _processManagementService.SetEfficiencyMode([process], enabled: false);
                    }
                }

                foreach (var key in _automaticEfficiencyProcesses.Keys.Where(key => !seen.Contains(key)).ToArray())
                {
                    _automaticEfficiencyProcesses.TryRemove(key, out _);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or IOException or System.ComponentModel.Win32Exception)
            {
                Trace.TraceError("Automatic SleepOS performance evaluation failed: {0}", exception);
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    MemorySaverStatus = $"Automatic performance evaluation failed: {exception.Message}";
                    OnPropertyChanged(nameof(MemorySaverStatus));
                });
            }
            finally
            {
                if (_isClosing)
                {
                    _processManagementService.RestoreEfficiencyModes();
                }

                Interlocked.Exchange(ref _automationInProgress, 0);
            }
        });
    }

    private static string FormatPercent(double? value) => value is { } percent ? $"{percent:F0}%" : "N/A";

    private static string FormatGigabytes(double? value) => value is { } gigabytes ? $"{gigabytes:F1} GB" : "N/A";

    private static string FormatSpeed(double? value) => value is { } speed ? $"{speed:F1} Mbps" : "N/A";

    private static string FormatMilliseconds(double? value) => value is { } milliseconds ? $"{milliseconds:F1} ms" : "N/A";

    private static string FormatBytes(long bytes)
    {
        const double bytesPerGigabyte = 1024d * 1024d * 1024d;
        const double bytesPerMegabyte = 1024d * 1024d;
        return bytes >= bytesPerGigabyte
            ? $"{bytes / bytesPerGigabyte:F1} GB"
            : bytes >= bytesPerMegabyte
                ? $"{bytes / bytesPerMegabyte:F1} MB"
                : $"{bytes / 1024d:F0} KB";
    }

    private void UpdateSettings(AppSettings settings)
    {
        _performanceMode = GetPerformanceMode(settings);
        GamingModeEnabled = string.Equals(_performanceMode, "Gaming", StringComparison.Ordinal);
        AutoRefreshEnabled = settings.AutoRefreshEnabled;
        GamingModeText = GamingModeEnabled ? "Gaming policy: On" : "Gaming policy: Off";
        CurrentProfile = NormalizeProfileName(settings.LastSelectedProfile);
        _accentTheme = NormalizeAccentTheme(settings.AccentTheme);
        _colorMode = NormalizeColorMode(settings.ColorMode);
        _metricsRefreshSeconds = NormalizeMetricsRefreshSeconds(settings.MetricsRefreshSeconds);
        _pageAnimationsEnabled = settings.PageAnimationsEnabled;
        AutoStartEnabled = settings.AutoStartEnabled;
        _startMinimized = settings.StartMinimized;
        _startupDelaySeconds = StartupDelayOptions.Contains(settings.StartupDelaySeconds) ? settings.StartupDelaySeconds : 0;
        OnPropertyChanged(nameof(StartMinimized));
        OnPropertyChanged(nameof(StartupDelaySeconds));
        AutomaticMemorySaverEnabled = settings.AutomaticMemorySaverEnabled;
        AutomaticGamingModeEnabled = settings.AutomaticGamingModeEnabled;
        AutomaticHighCpuAnalysisEnabled = settings.AutomaticHighCpuAnalysisEnabled;
        ManagedApplications.Clear();
        foreach (var rule in settings.ManagedApplications ?? [])
        {
            if (!string.IsNullOrWhiteSpace(rule.ExecutablePath))
            {
                ManagedApplications.Add(rule);
            }
        }
        UpdateGameProcessProfiles(settings.GameProfiles ?? []);
        GameProfileStatus = GameProcessProfiles.Count == 0
            ? "Add a running app from Background apps to recognize it as a game."
            : $"{GameProcessProfiles.Count:N0} custom game profile(s) loaded. Automatic switching is separately configurable.";
        OnPropertyChanged(nameof(GameProfileStatus));

        BenchmarkHistory.Clear();
        foreach (var record in (settings.BenchmarkHistory ?? []).TakeLast(20).Reverse())
        {
            BenchmarkHistory.Add(record);
        }
    }

    private static void MarkPerformanceModeCustom(AppSettings settings)
    {
        settings.PerformanceMode = "Custom";
        settings.GamingModeEnabled = false;
    }

    private static string NormalizePerformanceMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        "balanced" => "Balanced",
        "gaming" or "game focus" => "Gaming",
        "extreme performance" => "Extreme Performance",
        "battery saver" or "low activity" => "Battery Saver",
        "custom" => "Custom",
        _ => "Balanced"
    };

    private static string GetPerformanceMode(AppSettings settings)
    {
        var savedMode = NormalizePerformanceMode(settings.PerformanceMode);
        var matchesMode = savedMode switch
        {
            "Balanced" => settings.AutoRefreshEnabled && settings.MetricsRefreshSeconds == 5 && settings.PageAnimationsEnabled,
            "Gaming" => settings.AutoRefreshEnabled && settings.MetricsRefreshSeconds == 10 && !settings.PageAnimationsEnabled,
            "Extreme Performance" => !settings.AutoRefreshEnabled && settings.MetricsRefreshSeconds == 30 && !settings.PageAnimationsEnabled,
            "Battery Saver" => settings.AutoRefreshEnabled && settings.MetricsRefreshSeconds == 30 && !settings.PageAnimationsEnabled,
            _ => false
        };
        return matchesMode ? savedMode : "Custom";
    }

    private void NotifyPerformanceSettingsChanged()
    {
        OnPropertyChanged(nameof(PerformanceMode));
        OnPropertyChanged(nameof(PerformanceModeStatus));
        OnPropertyChanged(nameof(MetricsRefreshSeconds));
        OnPropertyChanged(nameof(AutoRefreshEnabled));
        OnPropertyChanged(nameof(AutoRefreshStatus));
        OnPropertyChanged(nameof(PageAnimationsEnabled));
        OnPropertyChanged(nameof(PageAnimationsStatus));
        OnPropertyChanged(nameof(GamingModeEnabled));
        OnPropertyChanged(nameof(GamingModeStatus));
        OnPropertyChanged(nameof(GamingModeText));
    }

    private static string NormalizeAccentTheme(string? accentTheme) => accentTheme switch
    {
        "Blue" => "Blue",
        "Mint" => "Mint",
        _ => "Violet"
    };

    private static string NormalizeColorMode(string? colorMode) =>
        string.Equals(colorMode, "Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";

    private static int NormalizeMetricsRefreshSeconds(int seconds) => seconds switch
    {
        10 or 30 => seconds,
        _ => 5
    };

    private static string NormalizeGamePerformanceMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        "balanced" => "Balanced",
        "extreme performance" => "Extreme Performance",
        "battery saver" => "Battery Saver",
        _ => "Gaming"
    };

    private static string NormalizeProfileName(string? profileName) => profileName switch
    {
        "Balanced" or "Custom" => "Balanced",
        "High performance" or "Gaming" or "Performance" or "Extreme Gaming" => "High performance",
        _ => "Balanced"
    };

    private void LoadProfiles()
    {
        Profiles.Clear();
        foreach (var profile in OptimizationProfiles.GetProfiles())
        {
            Profiles.Add(profile);
        }
    }

    private IReadOnlyList<GameSession> GetRunningGames()
    {
        return _gameService.GetRunningGames(_settingsStore.Load().GameProfiles ?? []);
    }

    private void UpdateGameProcessProfiles(IEnumerable<GamePerformanceProfile> profiles)
    {
        GameProcessProfiles.Clear();
        foreach (var profile in profiles
                     .Where(profile => !string.IsNullOrWhiteSpace(profile.ExecutablePath))
                     .DistinctBy(profile => profile.ExecutablePath, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(profile => profile.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            profile.PerformanceMode = NormalizeGamePerformanceMode(profile.PerformanceMode);
            GameProcessProfiles.Add(profile);
        }

        OnPropertyChanged(nameof(GameProcessProfiles));
    }

}
