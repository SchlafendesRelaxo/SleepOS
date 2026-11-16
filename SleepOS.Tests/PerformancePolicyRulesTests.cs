using SleepOS.WindowsSystem;

namespace SleepOS.Tests;

public sealed class PerformancePolicyRulesTests
{
    [Theory]
    [InlineData("Moderate", 10, 20)]
    [InlineData("Balanced", 10, 10)]
    [InlineData("Maximum", 10, 5)]
    [InlineData("Maximum", 1, 1)]
    [InlineData("Moderate", 60, 120)]
    public void MemorySaverLevelScalesTheConfiguredInactivityDelay(
        string level,
        int configuredMinutes,
        int expectedMinutes)
    {
        Assert.Equal(expectedMinutes, PerformancePolicyRules.GetMemorySaverInactivityMinutes(level, configuredMinutes));
    }

    [Theory]
    [InlineData("Balanced", 5, 85d)]
    [InlineData("Gaming", 10, 85d)]
    [InlineData("Extreme Performance", 30, null)]
    [InlineData("Battery Saver", 30, 70d)]
    public void DefinesDistinctPoliciesForEachPerformanceMode(string mode, int refreshSeconds, double? memoryThreshold)
    {
        var policy = PerformancePolicyRules.GetMode(mode);

        Assert.Equal(refreshSeconds, policy.MetricsRefreshSeconds);
        Assert.Equal(memoryThreshold, policy.AutomaticMemoryThresholdPercent);
    }

    [Fact]
    public void EfficiencyRequiresOptInAndAVisibleInactiveNonGameApplication()
    {
        var policy = PerformancePolicyRules.GetMode("Balanced");

        Assert.False(Evaluate(userOptedIn: false));
        Assert.False(Evaluate(isProtectedProcess: true));
        Assert.False(Evaluate(isGame: true));
        Assert.False(Evaluate(isForeground: true));
        Assert.False(Evaluate(hasVisibleWindow: false));
        Assert.False(Evaluate(inactiveFor: TimeSpan.FromMinutes(2)));
        Assert.False(Evaluate(memoryLoadPercent: 84.9));
        Assert.True(Evaluate());

        bool Evaluate(
            bool userOptedIn = true,
            bool isProtectedProcess = false,
            bool isGame = false,
            bool hasVisibleWindow = true,
            bool isForeground = false,
            double? memoryLoadPercent = 85,
            TimeSpan? inactiveFor = null) =>
            PerformancePolicyRules.ShouldEnableEfficiencyMode(
                policy,
                userOptedIn,
                isProtectedProcess,
                isGame,
                hasVisibleWindow,
                isForeground,
                memoryLoadPercent,
                inactiveFor ?? TimeSpan.FromMinutes(10),
                inactivityThresholdMinutes: 10);
    }

    [Fact]
    public void ExtremePerformanceNeverEnablesEcoQosAndGamingNeedsUserOptIn()
    {
        var extreme = PerformancePolicyRules.GetMode("Extreme Performance");
        var gaming = PerformancePolicyRules.GetMode("Gaming");

        Assert.False(PerformancePolicyRules.ShouldEnableEfficiencyMode(
            extreme, true, false, false, true, false, 99, TimeSpan.FromHours(1), 1));
        Assert.False(PerformancePolicyRules.ShouldEnableEfficiencyMode(
            gaming, false, false, false, true, false, 99, TimeSpan.FromHours(1), 1));
        Assert.True(PerformancePolicyRules.ShouldEnableEfficiencyMode(
            gaming, true, false, false, true, false, null, TimeSpan.FromHours(1), 1));
    }

    [Fact]
    public void BatteryAndBalancedModesCanReactToMeasuredCpuPressure()
    {
        var balanced = PerformancePolicyRules.GetMode("Balanced");
        var batterySaver = PerformancePolicyRules.GetMode("Battery Saver");

        Assert.False(PerformancePolicyRules.ShouldEnableEfficiencyMode(
            balanced, true, false, false, true, false, 50, TimeSpan.FromMinutes(10), 10, cpuLoadPercent: 94.9));
        Assert.True(PerformancePolicyRules.ShouldEnableEfficiencyMode(
            balanced, true, false, false, true, false, 50, TimeSpan.FromMinutes(10), 10, cpuLoadPercent: 95));
        Assert.True(PerformancePolicyRules.ShouldEnableEfficiencyMode(
            batterySaver, true, false, false, true, false, 50, TimeSpan.FromMinutes(10), 10, cpuLoadPercent: 85));
    }

    [Fact]
    public void WorkingSetTrimRequiresOptInHighMemoryAnIdleAppAndExpiredCooldown()
    {
        var policy = PerformancePolicyRules.GetMode("Balanced");
        var now = DateTime.UtcNow;

        Assert.True(Evaluate());
        Assert.False(Evaluate(automaticMemorySaverEnabled: false));
        Assert.False(Evaluate(appOptedIn: false));
        Assert.False(Evaluate(isGame: true));
        Assert.False(Evaluate(hasVisibleWindow: false));
        Assert.False(Evaluate(isForeground: true));
        Assert.False(Evaluate(memoryLoadPercent: 84.9));
        Assert.False(Evaluate(inactiveFor: TimeSpan.FromMinutes(4)));
        Assert.False(Evaluate(lastTrimUtc: now.AddMinutes(-29)));
        Assert.False(Evaluate(memorySaverLevel: "Moderate"));
        Assert.True(Evaluate(lastTrimUtc: now.AddMinutes(-30)));
        Assert.True(Evaluate(memorySaverLevel: "Maximum"));

        bool Evaluate(
            bool automaticMemorySaverEnabled = true,
            bool appOptedIn = true,
            bool isGame = false,
            bool hasVisibleWindow = true,
            bool isForeground = false,
            double? memoryLoadPercent = 85,
            TimeSpan? inactiveFor = null,
            DateTime? lastTrimUtc = null,
            string memorySaverLevel = "Balanced") =>
            PerformancePolicyRules.ShouldTrimWorkingSet(
                policy,
                automaticMemorySaverEnabled,
                appOptedIn,
                isGame,
                hasVisibleWindow,
                isForeground,
                memoryLoadPercent,
                inactiveFor ?? TimeSpan.FromMinutes(10),
                inactivityThresholdMinutes: 10,
                lastTrimUtc,
                now,
                cooldown: TimeSpan.FromMinutes(30),
                memorySaverLevel);
    }

    [Fact]
    public void LowerMemoryPriorityRequiresAnOptedInIdleBackgroundApp()
    {
        Assert.True(Evaluate());
        Assert.False(Evaluate(automaticMemorySaverEnabled: false));
        Assert.False(Evaluate(appOptedIn: false));
        Assert.False(Evaluate(isGame: true));
        Assert.False(Evaluate(hasVisibleWindow: false));
        Assert.False(Evaluate(isForeground: true));
        Assert.False(Evaluate(inactiveFor: TimeSpan.FromMinutes(4)));
        Assert.False(Evaluate(memorySaverLevel: "Moderate"));
        Assert.True(Evaluate(memorySaverLevel: "Maximum"));

        bool Evaluate(
            bool automaticMemorySaverEnabled = true,
            bool appOptedIn = true,
            bool isGame = false,
            bool hasVisibleWindow = true,
            bool isForeground = false,
            TimeSpan? inactiveFor = null,
            string memorySaverLevel = "Balanced") =>
            PerformancePolicyRules.ShouldLowerMemoryPriority(
                automaticMemorySaverEnabled,
                appOptedIn,
                isGame,
                hasVisibleWindow,
                isForeground,
                inactiveFor ?? TimeSpan.FromMinutes(10),
                inactivityThresholdMinutes: 10,
                memorySaverLevel);
    }
}
