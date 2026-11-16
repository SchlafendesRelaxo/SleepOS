using SleepOS.WindowsSystem;

namespace SleepOS.Tests;

public sealed class WindowsPowerProfileFormattingTests
{
    [Theory]
    [InlineData("Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c (Höchstleistung)", "High performance")]
    [InlineData("Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e (Balanced)", "Balanced")]
    [InlineData("Power Scheme GUID: a1841308-3541-4fab-bc81-f71556f20b4a (Power saver)", "Power saver")]
    public void FormatsBuiltInPlansFromTheirGuid(string output, string expected)
    {
        Assert.Equal(expected, PowerProfileFormatter.FormatActiveProfile(output));
    }

    [Fact]
    public void DoesNotDisplayANameWhenThePowerSchemeCannotBeIdentified()
    {
        Assert.Equal("Unavailable", PowerProfileFormatter.FormatActiveProfile("Power Scheme output is unavailable."));
    }
}
