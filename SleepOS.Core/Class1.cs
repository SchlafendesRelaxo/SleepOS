namespace SleepOS.Core;

public sealed class AppSettings
{
    public bool GamingModeEnabled { get; set; }
    public bool AutoRefreshEnabled { get; set; } = true;
    public string LastSelectedProfile { get; set; } = "Balanced";
    public string AccentTheme { get; set; } = "Violet";
    public string ColorMode { get; set; } = "Dark";
    public int MetricsRefreshSeconds { get; set; } = 5;
    public bool PageAnimationsEnabled { get; set; } = true;
    public string PerformanceMode { get; set; } = "Balanced";
    public bool AutoStartEnabled { get; set; }
    public bool StartMinimized { get; set; }
    public int StartupDelaySeconds { get; set; }
    public bool AutomaticMemorySaverEnabled { get; set; }
    public bool AutomaticGamingModeEnabled { get; set; }
    public bool AutomaticHighCpuAnalysisEnabled { get; set; }
    public List<ApplicationPerformanceRule> ManagedApplications { get; set; } = [];
    public List<GamePerformanceProfile> GameProfiles { get; set; } = [];
    public List<PerformanceBenchmarkRecord> BenchmarkHistory { get; set; } = [];
}

public sealed class ApplicationPerformanceRule
{
    public string ExecutablePath { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public int InactivityMinutes { get; set; } = 10;
    public bool Enabled { get; set; } = true;
    public bool TrimWorkingSetOnHighMemory { get; set; } = true;
}

public sealed class PerformanceBenchmarkRecord
{
    public DateTimeOffset CapturedAt { get; set; }
    public string Label { get; set; } = string.Empty;
    public double? CpuUsagePercent { get; set; }
    public double? RamUsedGb { get; set; }
    public double? TotalRamGb { get; set; }
    public int BackgroundProcessCount { get; set; }
    public int UninspectedProcessCount { get; set; }
    public string SystemState { get; set; } = string.Empty;
}

public sealed class GamePerformanceProfile
{
    public string ExecutablePath { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string PerformanceMode { get; set; } = "Gaming";
    public bool Enabled { get; set; } = true;
}

public sealed class SystemMetrics
{
    public double? CpuUsagePercent { get; set; }
    public double? CpuTemperatureC { get; set; }
    public double? GpuUsagePercent { get; set; }
    public double? GpuTemperatureC { get; set; }
    public double? RamUsedGb { get; set; }
    public double? TotalRamGb { get; set; }
    public double? VramUsedGb { get; set; }
    public double? TotalVramGb { get; set; }
    public double? DiskUsagePercent { get; set; }
    public double? DownloadMbps { get; set; }
    public double? UploadMbps { get; set; }
    public string UptimeText { get; set; } = "0:00:00";
    public IReadOnlyList<ProcessPerformance> RunningProcesses { get; set; } = Array.Empty<ProcessPerformance>();
}

public sealed class ProcessPerformance
{
    public string Name { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public double MemoryMb { get; set; }
    public double? CpuUsagePercent { get; set; }
    public string CpuUsageText => CpuUsagePercent is { } cpu ? $"{cpu:F1}% CPU" : "CPU N/A";
    public string Priority { get; set; } = "Unavailable";
    public bool HasVisibleWindow { get; set; }
}

public sealed class OptimizationProfile
{
    public string Name { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExpectedImpact { get; set; } = string.Empty;
    public string ChangeSummary { get; set; } = string.Empty;
}

public sealed class DashboardMetricCard
{
    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Accent { get; set; } = "#A855F7";
    public double? Percent { get; set; }
    public IReadOnlyList<double> Trend { get; set; } = Array.Empty<double>();
    public string TrendSummary { get; set; } = "Waiting for readings.";
}

public sealed class RecentOptimizationEntry
{
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

public sealed class GameSession
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Running";
    public string Path { get; set; } = string.Empty;
    public string PerformanceMode { get; set; } = "Gaming";
}

public sealed record HardwareInventoryItem(string Category, string Name, string Details);

public sealed record HardwareInventoryReport(
    DateTimeOffset CapturedAt,
    IReadOnlyList<HardwareInventoryItem> Items,
    IReadOnlyList<string> Warnings);