using SleepOS.WindowsSystem;

namespace SleepOS.Tests;

public sealed class WindowsStartupServiceTests
{
    [Fact]
    public void StartupCommandQuotesExecutableAndIncludesOnlyRequestedOptions()
    {
        var executable = Path.Combine(Path.GetTempPath(), "SleepOS App", "SleepOS.exe");

        var command = StartupCommandFormatter.Build(executable, 10, startMinimized: true);

        Assert.StartsWith($"\"{Path.GetFullPath(executable)}\"", command);
        Assert.EndsWith("--startup --startup-delay=10 --minimized", command);
    }

    [Fact]
    public void StartupDelayIsBoundedAndZeroDelayIsOmitted()
    {
        var command = StartupCommandFormatter.Build("SleepOS.exe", 900, startMinimized: false);

        Assert.Contains("--startup-delay=300", command);
        Assert.DoesNotContain("--minimized", command);
    }

    [Fact]
    public void StartupCommandRejectsAnEmptyExecutablePath()
    {
        Assert.Throws<ArgumentException>(() => StartupCommandFormatter.Build(" ", 0, startMinimized: false));
    }
}
