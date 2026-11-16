using System.Diagnostics;
using SleepOS.WindowsSystem;

namespace SleepOS.Tests;

public sealed class ProcessManagementServiceTests
{
    [Fact]
    public void ScanNeverOffersTheCurrentProcessForStopping()
    {
        var result = new ProcessManagementService().GetStoppableProcesses();

        Assert.DoesNotContain(result.Processes, process => process.ProcessId == Environment.ProcessId);
    }

    [Fact]
    public void StopSelectedProcessesRejectsTheCurrentProcessEvenIfManuallySupplied()
    {
        using var currentProcess = Process.GetCurrentProcess();
        var candidate = new ManagedProcessCandidate
        {
            ProcessId = currentProcess.Id,
            Name = currentProcess.ProcessName,
            ExecutablePath = currentProcess.MainModule?.FileName ?? throw new InvalidOperationException("Current process path is unavailable."),
            StartTimeUtcTicks = currentProcess.StartTime.ToUniversalTime().Ticks
        };

        var result = new ProcessManagementService().StopSelectedProcesses([candidate]);

        Assert.Equal(0, result.StoppedCount);
        Assert.NotEmpty(result.Failures);
        Assert.False(currentProcess.HasExited);
    }

    [Fact]
    public void RecommendsHiddenBackgroundAppsWithoutMemoryThreshold()
    {
        var recommendation = ProcessRecommendationRules.Evaluate(
            isRecognizedGame: false,
            hasVisibleWindow: false,
            hasPublisherMetadata: true);

        Assert.True(recommendation.IsSuggestedForReview);
        Assert.Equal("Hidden background app", recommendation.Title);
        Assert.Contains("may still be needed", recommendation.Details);
    }

    [Fact]
    public void DoesNotRecommendClosingAVisibleAppOrRecognizedGame()
    {
        var visibleApp = ProcessRecommendationRules.Evaluate(
            isRecognizedGame: false,
            hasVisibleWindow: true,
            hasPublisherMetadata: true);
        var activeGame = ProcessRecommendationRules.Evaluate(
            isRecognizedGame: true,
            hasVisibleWindow: false,
            hasPublisherMetadata: false);

        Assert.False(visibleApp.IsSuggestedForReview);
        Assert.False(activeGame.IsSuggestedForReview);
        Assert.Equal("Active game", activeGame.Title);
    }

    [Fact]
    public void FlagsUnknownBackgroundPublisherForReviewButExplainsTheLimitation()
    {
        var recommendation = ProcessRecommendationRules.Evaluate(
            isRecognizedGame: false,
            hasVisibleWindow: false,
            hasPublisherMetadata: false);

        Assert.True(recommendation.IsSuggestedForReview);
        Assert.Equal("Unknown background app", recommendation.Title);
        Assert.Contains("does not mean it is unsafe or unnecessary", recommendation.Details);
    }
}
