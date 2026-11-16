using SleepOS.Monitoring;

namespace SleepOS.Tests;

public sealed class HardwareInventoryFormattingTests
{
    [Fact]
    public void FormatsWmiByteCapacityAsGibibytes()
    {
        Assert.Equal("16.0 GiB", HardwareInventoryFormatting.FormatCapacity(16UL * 1024 * 1024 * 1024));
    }

    [Fact]
    public void DoesNotInventCapacityWhenWindowsDoesNotReportIt()
    {
        Assert.Equal("Unavailable", HardwareInventoryFormatting.FormatCapacity(null));
        Assert.Equal("Unavailable", HardwareInventoryFormatting.FormatCapacity(0));
    }
}
