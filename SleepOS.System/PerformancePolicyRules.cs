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
    public static int GetMemorySaverInactivityMinutes(string? level, int configuredMinutes)
    {
        var minutes = Math.Clamp(configuredMinutes, 1, 120);
        return level?.Trim().ToLowerInvariant() switch
        {
            "moderate" => Math.Min(120, minutes * 2),
            "maximum" => Math.Max(1, (minutes + 1) / 2),
            _ => minutes
        };
    }

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

    public static bool ShouldTrimWorkingSet(
        PerformanceModePolicy policy,
        bool automaticMemorySaverEnabled,
        bool appOptedIn,
        bool isGame,
        bool hasVisibleWindow,
        bool isForeground,
        double? memoryLoadPercent,
        TimeSpan inactiveFor,
        int inactivityThresholdMinutes,
        DateTime? lastTrimUtc,
        DateTime nowUtc,
        TimeSpan cooldown,
        string memorySaverLevel = "Balanced")
    {
        if (!automaticMemorySaverEnabled || !appOptedIn || isGame || !hasVisibleWindow || isForeground ||
            policy.AutomaticMemoryThresholdPercent is not { } memoryThreshold ||
            memoryLoadPercent is not { } memoryLoad ||
            memoryLoad < memoryThreshold ||
            inactiveFor < TimeSpan.FromMinutes(GetMemorySaverInactivityMinutes(memorySaverLevel, inactivityThresholdMinutes)))
        {
            return false;
        }

        return lastTrimUtc is null || nowUtc - lastTrimUtc.Value >= cooldown;
    }

    public static bool ShouldLowerMemoryPriority(
        bool automaticMemorySaverEnabled,
        bool appOptedIn,
        bool isGame,
        bool hasVisibleWindow,
        bool isForeground,
        TimeSpan inactiveFor,
        int inactivityThresholdMinutes,
        string memorySaverLevel = "Balanced")
    {
        if (!automaticMemorySaverEnabled || !appOptedIn || isGame || !hasVisibleWindow || isForeground ||
            inactiveFor < TimeSpan.FromMinutes(GetMemorySaverInactivityMinutes(memorySaverLevel, inactivityThresholdMinutes)))
        {
            return false;
        }

        return true;
    }
}
