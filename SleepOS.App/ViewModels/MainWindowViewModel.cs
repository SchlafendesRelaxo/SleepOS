using System.ComponentModel;
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
    private string _performanceMode = "Balanced";
    private string _accentTheme = "Violet";
    private string _colorMode = "Dark";
    private int _metricsRefreshSeconds = 5;
    private bool _pageAnimationsEnabled = true;

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
        IDesktopShortcutService desktopShortcutService)
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

        UpdateSettings(_settingsStore.Load());
        LoadProfiles();
        LoadTweaks();
        RefreshMetrics();
    }

    public ObservableCollection<DashboardMetricCard> HardwareCards { get; } = new();
    public ObservableCollection<RecentOptimizationEntry> RecentOptimizations { get; } = new();
    public ObservableCollection<OptimizationProfile> Profiles { get; } = new();
    public ObservableCollection<TweakCategory> TweakCategories { get; } = new();
    public ObservableCollection<ProcessPerformance> RunningProcesses { get; } = new();
    public ObservableCollection<ManagedProcessCandidate> ManageableProcesses { get; } = new();
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
    public IReadOnlyList<string> PerformanceModeOptions { get; } = ["Balanced", "Game focus", "Low activity", "Custom"];
    public IReadOnlyList<int> MetricsRefreshOptions { get; } = [5, 10, 30];

    public string Greeting { get; set; } = "Good evening, gamer";
    public string SystemStatus { get; set; } = "Nominal";
    public string GamingModeText { get; set; } = "Gaming Mode: Off";
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
            GamingModeText = "Game focus: Off";
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
            GamingModeText = "Game focus: Off";
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
                case "Game focus":
                    settings.AutoRefreshEnabled = false;
                    settings.MetricsRefreshSeconds = 30;
                    settings.PageAnimationsEnabled = false;
                    settings.GamingModeEnabled = true;
                    break;
                case "Low activity":
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
                "Balanced" => "Balanced SleepOS mode enabled: 5-second metrics and page animations.",
                "Game focus" => "Game focus enabled: SleepOS metrics polling and page animations are paused.",
                "Low activity" => "Low activity enabled: 30-second metrics and no page animations.",
                _ => "Custom SleepOS performance settings selected."
            };
            OnPropertyChanged(nameof(ActionStatus));
        }
    }

    public int ManageableProcessCount => ManageableProcesses.Count;
    public int RecommendedProcessCount => ManageableProcesses.Count(process => process.IsSuggestedForReview);
    public IReadOnlyList<ManagedProcessCandidate> SelectedStoppableProcesses =>
        ManageableProcesses.Where(process => process.IsSelected).ToArray();
    public string GamingModeStatus => GamingModeEnabled ? "On for this app" : "Off";
    public string AutoRefreshStatus => AutoRefreshEnabled ? $"On • every {MetricsRefreshSeconds} seconds" : "Off";
    public string PageAnimationsStatus => PageAnimationsEnabled ? "Enabled • page transitions and orbit motion" : "Disabled";
    public string PerformanceModeStatus => _performanceMode switch
    {
        "Game focus" => "Pauses SleepOS polling and motion. No Windows settings are changed.",
        "Low activity" => "Refreshes SleepOS metrics every 30 seconds with animations off.",
        "Custom" => "Your SleepOS refresh and animation settings. Windows is unchanged.",
        _ => "Refreshes SleepOS metrics every 5 seconds with smooth page transitions."
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

        GamingModeEnabled = string.Equals(_performanceMode, "Game focus", StringComparison.Ordinal);
        AutoRefreshEnabled = settings.AutoRefreshEnabled;
        GamingModeText = GamingModeEnabled ? "Game focus: On (SleepOS only)" : "Game focus: Off";
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
        var runningGames = _gameService.GetRunningGames();
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
        PerformanceMode = GamingModeEnabled ? "Balanced" : "Game focus";
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
        GamingModeText = "Game focus: Off";
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
                var activeGameProcessNames = _gameService.GetRunningGames()
                    .Where(game => string.Equals(game.Status, "Running", StringComparison.OrdinalIgnoreCase))
                    .Select(game => game.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                return _processManagementService.GetStoppableProcesses(activeGameProcessNames);
            });
            foreach (var process in result.Processes)
            {
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
    public void RunTweak(string tweakId)
    {
        var tweak = TweakCategories.SelectMany(category => category.Tweaks)
            .FirstOrDefault(candidate => string.Equals(candidate.Id, tweakId, StringComparison.Ordinal));
        if (tweak is null)
        {
            ActionStatus = "That tweak is not available.";
            OnPropertyChanged(nameof(ActionStatus));
            return;
        }

        try
        {
            switch (tweak.Id)
            {
                case "dns-cache":
                    _systemService.ClearDnsCache();
                    ActionStatus = "DNS cache cleared. The DNS server itself was not changed.";
                    break;
                case "explorer":
                    _systemService.RestartExplorer();
                    ActionStatus = "File Explorer restarted. Its windows may briefly close or refresh.";
                    break;
                default:
                    _systemService.OpenWindowsTweakSettings(tweak.Id);
                    ActionStatus = $"{tweak.Name} settings opened. Review and apply changes in Windows.";
                    break;
            }
        }
        catch (InvalidOperationException exception)
        {
            ActionStatus = $"Could not run {tweak.Name}: {exception.Message}";
        }
        catch (Win32Exception exception)
        {
            ActionStatus = $"Could not run {tweak.Name}: {exception.Message}";
        }

        OnPropertyChanged(nameof(ActionStatus));
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
        GamingModeEnabled = string.Equals(_performanceMode, "Game focus", StringComparison.Ordinal);
        AutoRefreshEnabled = settings.AutoRefreshEnabled;
        GamingModeText = GamingModeEnabled ? "Game focus: On (SleepOS only)" : "Game focus: Off";
        CurrentProfile = NormalizeProfileName(settings.LastSelectedProfile);
        _accentTheme = NormalizeAccentTheme(settings.AccentTheme);
        _colorMode = NormalizeColorMode(settings.ColorMode);
        _metricsRefreshSeconds = NormalizeMetricsRefreshSeconds(settings.MetricsRefreshSeconds);
        _pageAnimationsEnabled = settings.PageAnimationsEnabled;
    }

    private static void MarkPerformanceModeCustom(AppSettings settings)
    {
        settings.PerformanceMode = "Custom";
        settings.GamingModeEnabled = false;
    }

    private static string NormalizePerformanceMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        "balanced" => "Balanced",
        "game focus" => "Game focus",
        "low activity" => "Low activity",
        _ => "Custom"
    };

    private static string GetPerformanceMode(AppSettings settings)
    {
        var savedMode = NormalizePerformanceMode(settings.PerformanceMode);
        var matchesMode = savedMode switch
        {
            "Balanced" => settings.AutoRefreshEnabled && settings.MetricsRefreshSeconds == 5 && settings.PageAnimationsEnabled,
            "Game focus" => !settings.AutoRefreshEnabled && settings.MetricsRefreshSeconds == 30 && !settings.PageAnimationsEnabled,
            "Low activity" => settings.AutoRefreshEnabled && settings.MetricsRefreshSeconds == 30 && !settings.PageAnimationsEnabled,
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

    private void LoadTweaks()
    {
        TweakCategories.Clear();
        TweakCategories.Add(new TweakCategory
        {
            Name = "Gaming",
            Description = "Windows features for game focus, overlays, and recording. Review each setting before changing it.",
            Tweaks =
            [
                new TweakDefinition
                {
                    Id = "game-mode", Name = "Game Mode", Category = "Gaming",
                    Description = "Windows can prioritize game-related work. The result depends on Windows and your hardware; extra FPS is not guaranteed.",
                    ActionLabel = "Open Game Mode"
                },
                new TweakDefinition
                {
                    Id = "game-dvr", Name = "Background captures", Category = "Gaming",
                    Description = "Review background recording and capture options. Disabling them can remove clips and capture shortcuts.",
                    ActionLabel = "Open capture settings"
                },
                new TweakDefinition
                {
                    Id = "game-bar", Name = "Xbox Game Bar", Category = "Gaming",
                    Description = "Review the overlay, widgets, and shortcut settings. Turning it off can disable those features.",
                    ActionLabel = "Open Game Bar settings"
                }
            ]
        });

        TweakCategories.Add(new TweakCategory
        {
            Name = "Visual effects",
            Description = "Windows appearance options. These affect the desktop; game performance gains are not guaranteed.",
            Tweaks =
            [
                new TweakDefinition
                {
                    Id = "animations", Name = "Windows animations", Category = "Visual effects",
                    Description = "Review motion and animation effects. Reducing them changes desktop appearance and accessibility behavior.",
                    ActionLabel = "Open animation settings"
                },
                new TweakDefinition
                {
                    Id = "transparency", Name = "Transparency effects", Category = "Visual effects",
                    Description = "Review transparent surfaces in Windows. This is mainly a visual preference, not a guaranteed FPS tweak.",
                    ActionLabel = "Open appearance settings"
                }
            ]
        });

        TweakCategories.Add(new TweakCategory
        {
            Name = "Startup",
            Description = "Choose which apps start when you sign in. Disabling an app can affect sync, notifications, or updates.",
            Tweaks =
            [
                new TweakDefinition
                {
                    Id = "startup", Name = "Startup apps", Category = "Startup",
                    Description = "Windows shows startup entries and their reported impact. Disable only apps you recognize and do not need at sign-in.",
                    ActionLabel = "Review startup apps"
                }
            ]
        });

        TweakCategories.Add(new TweakCategory
        {
            Name = "Privacy",
            Description = "Review Windows privacy controls. SleepOS does not change diagnostic data, advertising, or app permissions.",
            Tweaks =
            [
                new TweakDefinition
                {
                    Id = "telemetry", Name = "Diagnostics & feedback", Category = "Privacy",
                    Description = "Review optional diagnostic data and feedback settings. Required Windows diagnostics may still apply.",
                    ActionLabel = "Open diagnostics settings"
                },
                new TweakDefinition
                {
                    Id = "ad-id", Name = "Advertising ID", Category = "Privacy",
                    Description = "Review the advertising identifier used for app personalization. This does not remove all advertising.",
                    ActionLabel = "Open privacy settings"
                },
                new TweakDefinition
                {
                    Id = "permissions", Name = "App permissions", Category = "Privacy",
                    Description = "Review access such as location, camera, and microphone. Restricting access can break app features.",
                    ActionLabel = "Review permissions"
                }
            ]
        });

        TweakCategories.Add(new TweakCategory
        {
            Name = "Network",
            Description = "Open Windows network settings or clear the DNS cache. Changing DNS does not necessarily lower game ping.",
            Tweaks =
            [
                new TweakDefinition
                {
                    Id = "dns", Name = "DNS server", Category = "Network",
                    Description = "Opens adapter settings so you can review or change the DNS server. Note the current values before changing them.",
                    ActionLabel = "Open network settings"
                },
                new TweakDefinition
                {
                    Id = "dns-cache", Name = "DNS cache", Category = "Network",
                    Description = "Clears saved name lookups. It does not change your DNS server; the first lookup afterward may take longer.",
                    ActionLabel = "Clear DNS cache",
                    ActionType = "SleepOS action"
                },
                new TweakDefinition
                {
                    Id = "adapter-info", Name = "Network adapter", Category = "Network",
                    Description = "Opens Windows network status so you can review adapters and connection details. No setting is changed.",
                    ActionLabel = "Open network status"
                }
            ]
        });

        TweakCategories.Add(new TweakCategory
        {
            Name = "File Explorer",
            Description = "Open File Explorer Options or restart the Windows shell. These actions do not improve FPS.",
            Tweaks =
            [
                new TweakDefinition
                {
                    Id = "hidden-files", Name = "Hidden files", Category = "File Explorer",
                    Description = "Opens Folder Options. Showing protected or hidden files can make system files easier to change accidentally.",
                    ActionLabel = "Open Folder Options"
                },
                new TweakDefinition
                {
                    Id = "file-extensions", Name = "File extensions", Category = "File Explorer",
                    Description = "Opens Folder Options so you can show or hide extensions such as .exe and .txt. No performance effect.",
                    ActionLabel = "Open Folder Options"
                },
                new TweakDefinition
                {
                    Id = "explorer", Name = "Restart File Explorer", Category = "File Explorer",
                    Description = "Stops and relaunches the Windows shell. Taskbar and Explorer windows can briefly disappear or refresh.",
                    ActionLabel = "Restart Explorer",
                    ActionType = "SleepOS action"
                }
            ]
        });
    }
}
