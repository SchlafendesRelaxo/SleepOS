using System.Diagnostics;
using SleepOS.Core;
using SleepOS.Games;

namespace SleepOS.Tests;

public sealed class GameServiceTests
{
    [Fact]
    public void RecognizesUserConfiguredGameByExecutablePathAndUsesItsSelectedMode()
    {
        using var currentProcess = Process.GetCurrentProcess();
        var executablePath = currentProcess.MainModule?.FileName
            ?? throw new InvalidOperationException("The current process path is unavailable.");
        var profile = new GamePerformanceProfile
        {
            ExecutablePath = executablePath,
            ProcessName = currentProcess.ProcessName,
            PerformanceMode = "Battery Saver",
            Enabled = true
        };

        var result = new GameService().GetRunningGames([profile]);

        var match = Assert.Single(result, game =>
            string.Equals(game.Path, executablePath, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Battery Saver", match.PerformanceMode);
    }

    [Fact]
    public void DisabledGameProfileDoesNotMatchAnUnknownExecutable()
    {
        using var currentProcess = Process.GetCurrentProcess();
        var executablePath = currentProcess.MainModule?.FileName
            ?? throw new InvalidOperationException("The current process path is unavailable.");
        var profile = new GamePerformanceProfile
        {
            ExecutablePath = executablePath,
            ProcessName = currentProcess.ProcessName,
            Enabled = false
        };

        var result = new GameService().GetRunningGames([profile]);

        Assert.DoesNotContain(result, game =>
            string.Equals(game.Path, executablePath, StringComparison.OrdinalIgnoreCase));
    }
}
