using SleepOS.Monitoring;

namespace SleepOS.Tests;

public sealed class NetworkPingSummarizerTests
{
    [Fact]
    public void CalculatesLatencyJitterAndLossFromActualReplies()
    {
        var samples = new[]
        {
            new NetworkPingSample(1, true, 10, "Success"),
            new NetworkPingSample(2, true, 20, "Success"),
            new NetworkPingSample(3, false, null, "TimedOut"),
            new NetworkPingSample(4, true, 30, "Success")
        };

        var summary = NetworkPingSummarizer.Summarize("127.0.0.1", samples, DateTimeOffset.UtcNow);

        Assert.Equal(4, summary.PacketsSent);
        Assert.Equal(3, summary.PacketsReceived);
        Assert.Equal(25, summary.PacketLossPercent);
        Assert.Equal(20, summary.AverageMilliseconds);
        Assert.Equal(10, summary.MinimumMilliseconds);
        Assert.Equal(30, summary.MaximumMilliseconds);
        Assert.Equal(10, summary.JitterMilliseconds);
    }

    [Fact]
    public void ReportsNoLatencyWhenNoProbeReplies()
    {
        var samples = new[]
        {
            new NetworkPingSample(1, false, null, "TimedOut"),
            new NetworkPingSample(2, false, null, "TimedOut")
        };

        var summary = NetworkPingSummarizer.Summarize("192.0.2.1", samples, DateTimeOffset.UtcNow);

        Assert.Equal(100, summary.PacketLossPercent);
        Assert.Null(summary.AverageMilliseconds);
        Assert.Null(summary.JitterMilliseconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.com")]
    [InlineData("not a host")]
    public void RejectsInvalidTargetsBeforeSendingPings(string target)
    {
        Assert.Throws<ArgumentException>(() => NetworkDiagnosticsService.ValidateTarget(target));
    }
}
