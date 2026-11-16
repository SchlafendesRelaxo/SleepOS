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
}
