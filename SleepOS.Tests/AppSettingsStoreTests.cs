using SleepOS.Core;
using SleepOS.Settings;

namespace SleepOS.Tests;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public void AutoRefreshPreferenceIsPersisted()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sleepos-tests-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "settings.json");

        try
        {
            var store = new JsonAppSettingsStore(filePath);
            Assert.True(store.Load().AutoRefreshEnabled);

            store.Save(new AppSettings { AutoRefreshEnabled = false });

            Assert.False(store.Load().AutoRefreshEnabled);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void AppearanceAndRefreshPreferencesArePersisted()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sleepos-tests-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "settings.json");

        try
        {
            var store = new JsonAppSettingsStore(filePath);
            store.Save(new AppSettings
            {
                AccentTheme = "Mint",
                ColorMode = "Light",
                MetricsRefreshSeconds = 30,
                PageAnimationsEnabled = false,
                PerformanceMode = "Low activity"
            });

            var settings = store.Load();

            Assert.Equal("Mint", settings.AccentTheme);
            Assert.Equal("Light", settings.ColorMode);
            Assert.Equal(30, settings.MetricsRefreshSeconds);
            Assert.False(settings.PageAnimationsEnabled);
            Assert.Equal("Low activity", settings.PerformanceMode);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void MissingAppearancePreferencesUseDefaults()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sleepos-tests-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "settings.json");

        try
        {
            var store = new JsonAppSettingsStore(filePath);
            File.WriteAllText(filePath, """{"AutoRefreshEnabled":false,"LastSelectedProfile":"Balanced"}""");

            var settings = store.Load();

            Assert.False(settings.AutoRefreshEnabled);
            Assert.Equal("Violet", settings.AccentTheme);
            Assert.Equal("Dark", settings.ColorMode);
            Assert.Equal(5, settings.MetricsRefreshSeconds);
            Assert.True(settings.PageAnimationsEnabled);
            Assert.Equal("Balanced", settings.PerformanceMode);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void StartupMemorySaverAndBenchmarkSettingsRoundTrip()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sleepos-tests-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "settings.json");

        try
        {
            var store = new JsonAppSettingsStore(filePath);
            store.Save(new AppSettings
            {
                AutoStartEnabled = true,
                StartMinimized = true,
                StartupDelaySeconds = 30,
                AutomaticMemorySaverEnabled = true,
                MemorySaverLevel = "Maximum",
                AutomaticGamingModeEnabled = true,
                ManagedApplications =
                [
                    new ApplicationPerformanceRule
                    {
                        ExecutablePath = @"C:\Games\Example\game.exe",
                        ProcessName = "game",
                        InactivityMinutes = 20,
                        Enabled = true,
                        TrimWorkingSetOnHighMemory = false
                    }
                ],
                GameProfiles =
                [
                    new GamePerformanceProfile
                    {
                        ExecutablePath = @"C:\Games\Custom\customgame.exe",
                        ProcessName = "customgame",
                        PerformanceMode = "Extreme Performance",
                        Enabled = true
                    }
                ],
                BenchmarkHistory =
                [
                    new PerformanceBenchmarkRecord
                    {
                        CapturedAt = DateTimeOffset.UtcNow,
                        Label = "Baseline",
                        CpuUsagePercent = 18.5,
                        RamUsedGb = 7.25,
                        TotalRamGb = 16,
                        BackgroundProcessCount = 42,
                        UninspectedProcessCount = 2,
                        SystemState = "Nominal"
                    }
                ]
            });

            var settings = store.Load();

            Assert.True(settings.AutoStartEnabled);
            Assert.True(settings.StartMinimized);
            Assert.Equal(30, settings.StartupDelaySeconds);
            Assert.True(settings.AutomaticMemorySaverEnabled);
            Assert.Equal("Maximum", settings.MemorySaverLevel);
            Assert.True(settings.AutomaticGamingModeEnabled);
            Assert.Equal(20, Assert.Single(settings.ManagedApplications).InactivityMinutes);
            Assert.False(Assert.Single(settings.ManagedApplications).TrimWorkingSetOnHighMemory);
            Assert.Equal("Extreme Performance", Assert.Single(settings.GameProfiles).PerformanceMode);
            Assert.Equal(42, Assert.Single(settings.BenchmarkHistory).BackgroundProcessCount);
            Assert.Equal(2, Assert.Single(settings.BenchmarkHistory).UninspectedProcessCount);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
