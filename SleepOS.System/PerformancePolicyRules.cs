namespace SleepOS.WindowsSystem;

public sealed record PerformanceModePolicy(
    string Name,
    int MetricsRefreshSeconds,
    bool PageAnimationsEnabled,
    double? AutomaticMemoryThresholdPercent,
    bool ReduceManagedBackgroundProcessesDuringGaming,
    double? AutomaticCpuThresholdPercent);

public static class PerformancePolicyRules
{
    public static PerformanceModePolicy GetMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        "gaming" or "game focus" => new PerformanceModePolicy("Gaming", 10, false, 85, true, null),
        "extreme performance" => new PerformanceModePolicy("Extreme Performance", 30, false, null, false, null),
        "battery saver" or "low activity" => new PerformanceModePolicy("Battery Saver", 30, false, 70, false, 85),
        _ => new PerformanceModePolicy("Balanced", 5, true, 85, false, 95)
    };

    public static bool ShouldEnableEfficiencyMode(
        PerformanceModePolicy policy,
        bool userOptedIn,
        bool isProtectedProcess,
        bool isGame,
        bool hasVisibleWindow,
        bool isForeground,
        double? memoryLoadPercent,
        TimeSpan inactiveFor,
        int inactivityThresholdMinutes,
        double? cpuLoadPercent = null)
    {
        if (!userOptedIn || isProtectedProcess || isGame || isForeground || !hasVisibleWindow ||
            inactiveFor < TimeSpan.FromMinutes(Math.Clamp(inactivityThresholdMinutes, 1, 120)))
        {
            return false;
        }

        if (policy.Name == "Extreme Performance")
        {
            return false;
        }

        if (policy.Name == "Gaming")
        {
            return policy.ReduceManagedBackgroundProcessesDuringGaming;
        }

        var highMemory = policy.AutomaticMemoryThresholdPercent is { } memoryThreshold &&
            memoryLoadPercent is { } memoryLoad &&
            memoryLoad >= memoryThreshold;
        var highCpu = policy.AutomaticCpuThresholdPercent is { } cpuThreshold &&
            cpuLoadPercent is { } cpuLoad &&
            cpuLoad >= cpuThreshold;
        return highMemory || highCpu;
    }
}
