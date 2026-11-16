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
    public string Value { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Accent { get; set; } = "#A855F7";
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
}

public sealed record HardwareInventoryItem(string Category, string Name, string Details);

public sealed record HardwareInventoryReport(
    DateTimeOffset CapturedAt,
    IReadOnlyList<HardwareInventoryItem> Items,
    IReadOnlyList<string> Warnings);