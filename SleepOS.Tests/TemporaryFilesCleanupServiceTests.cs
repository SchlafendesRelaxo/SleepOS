using SleepOS.WindowsSystem;

namespace SleepOS.Tests;

public sealed class TemporaryFilesCleanupServiceTests
{
    [Fact]
    public void CleanupRemovesOnlyFilesOlderThanSevenDays()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sleepos-cleanup-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var oldFile = Path.Combine(directory, "old.tmp");
        var recentFile = Path.Combine(directory, "recent.tmp");

        try
        {
            File.WriteAllText(oldFile, "old temporary data");
            File.WriteAllText(recentFile, "recent temporary data");
            File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-31));

            var service = new TemporaryFilesCleanupService(directory);
            var preview = service.Scan();

            Assert.Equal(1, preview.Files);
            Assert.Equal(new FileInfo(oldFile).Length, preview.Bytes);

            var result = service.Clean();

            Assert.Equal(1, result.Files);
            Assert.False(File.Exists(oldFile));
            Assert.True(File.Exists(recentFile));
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
