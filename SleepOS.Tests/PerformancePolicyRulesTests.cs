using SleepOS.WindowsSystem;

namespace SleepOS.Tests;

public sealed class PerformancePolicyRulesTests
{
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
}
