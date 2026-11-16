using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace SleepOS.Updater;

public sealed class InstalledVersionService
{
    private const string RepositoryUrl = "https://github.com/SchlafendesRelaxo/SleepOS";
    private readonly UpdateManager _updateManager = new(new GithubSource(RepositoryUrl, null, false));
    private UpdateInfo? _pendingUpdate;

    public Version? CurrentVersion => Assembly.GetEntryAssembly()?.GetName().Version;
    public bool CanUpdate => _updateManager.IsInstalled;

    public async Task<string?> CheckForUpdateAsync()
    {
        _pendingUpdate = await _updateManager.CheckForUpdatesAsync();
        return _pendingUpdate?.TargetFullRelease.Version.ToString();
    }

    public async Task DownloadAndRestartAsync()
    {
        var update = _pendingUpdate
            ?? throw new InvalidOperationException("Check for updates before downloading an update.");

        await _updateManager.DownloadUpdatesAsync(update);
        _updateManager.ApplyUpdatesAndRestart(update.TargetFullRelease);
    }
}