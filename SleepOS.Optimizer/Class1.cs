using SleepOS.Core;

namespace SleepOS.Optimizer;

public static class OptimizationProfiles
{
    public static IReadOnlyList<OptimizationProfile> GetProfiles() =>
    [
        new OptimizationProfile
        {
            Name = "Balanced",
            PlanName = "Windows Balanced",
            Description = "Windows balances responsiveness and power use for everyday use.",
            ExpectedImpact = "A sensible default, especially on laptops. It does not promise a specific FPS increase.",
            ChangeSummary = "Switches only the active Windows power plan to Balanced. No other tweaks are applied."
        },
        new OptimizationProfile
        {
            Name = "High performance",
            PlanName = "Windows High performance",
            Description = "Selects Windows' built-in High performance power plan.",
            ExpectedImpact = "May use more power and create more heat; the effect depends on your hardware. It does not overclock components or guarantee higher FPS.",
            ChangeSummary = "Switches only the active Windows power plan to High performance. No other tweaks are applied."
        }
    ];
}
