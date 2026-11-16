using SleepOS.WindowsSystem;

namespace SleepOS.Tests;

public sealed class DesktopShortcutServiceTests
{
    [Fact]
    public void DesktopShortcutServiceCanScanWithoutChangingShortcuts()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var result = new DesktopShortcutService().FindDuplicates();

        Assert.NotNull(result.Duplicates);
        Assert.NotNull(result.Warnings);
    }

    [Fact]
    public void FindDuplicateCandidatesKeepsFirstPathForSameTargetIgnoringCase()
    {
        var shortcuts = new[]
        {
            new DesktopShortcutCandidate { Name = "Later", ShortcutPath = @"C:\Desktop\Later.lnk", TargetPath = @"C:\Apps\Game.exe" },
            new DesktopShortcutCandidate { Name = "First", ShortcutPath = @"C:\Desktop\First.lnk", TargetPath = @"c:\apps\game.exe" },
            new DesktopShortcutCandidate { Name = "Other", ShortcutPath = @"C:\Desktop\Other.lnk", TargetPath = @"C:\Apps\Other.exe" }
        };

        var duplicates = DesktopShortcutRules.FindDuplicateCandidates(shortcuts);

        var duplicate = Assert.Single(duplicates);
        Assert.Equal("Later", duplicate.Name);
    }

    [Fact]
    public void FindDuplicateCandidatesDoesNotMergeShortcutsWithDifferentArguments()
    {
        var shortcuts = new[]
        {
            new DesktopShortcutCandidate
            {
                Name = "Game",
                ShortcutPath = @"C:\Desktop\Game.lnk",
                TargetPath = @"C:\Apps\Launcher.exe",
                Arguments = "--profile main"
            },
            new DesktopShortcutCandidate
            {
                Name = "Game test",
                ShortcutPath = @"C:\Desktop\Game test.lnk",
                TargetPath = @"c:\apps\launcher.exe",
                Arguments = "--profile test"
            }
        };

        var duplicates = DesktopShortcutRules.FindDuplicateCandidates(shortcuts);

        Assert.Empty(duplicates);
    }
}
