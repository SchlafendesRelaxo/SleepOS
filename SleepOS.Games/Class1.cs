using SleepOS.Core;
using System.Diagnostics;

﻿namespace SleepOS.Games;

public interface IGameService
{
    IReadOnlyList<GameSession> GetDetectedGames();
    IReadOnlyList<GameSession> GetRunningGames(IReadOnlyList<GamePerformanceProfile>? profiles = null);
}

public sealed class GameService : IGameService
{
    private static readonly HashSet<string> KnownGameProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "apex_legends", "cs2", "dota2", "eldenring", "fortniteclient-win64-shipping",
        "fivem", "gta5", "minecraft", "minecraft.windows", "league of legends", "overwatch",
        "valorant-win64-shipping", "witcher3"
    };

    public IReadOnlyList<GameSession> GetDetectedGames()
    {
        return GetRunningGames();
    }

    public IReadOnlyList<GameSession> GetRunningGames(IReadOnlyList<GamePerformanceProfile>? profiles = null)
    {
        var running = new List<GameSession>();
        var profilesByProcessName = (profiles ?? [])
            .Where(profile => profile.Enabled && !string.IsNullOrWhiteSpace(profile.ProcessName))
            .GroupBy(profile => profile.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var isKnownGame = IsKnownGameProcess(process.ProcessName);
                if (!isKnownGame && !profilesByProcessName.TryGetValue(process.ProcessName, out _))
                {
                    continue;
                }

                var executablePath = process.MainModule?.FileName ?? string.Empty;
                var profile = profilesByProcessName.TryGetValue(process.ProcessName, out var matchingProfiles)
                    ? matchingProfiles.FirstOrDefault(candidate =>
                        string.Equals(candidate.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase))
                    : null;
                if (!isKnownGame && profile is null)
                {
                    continue;
                }

                running.Add(new GameSession
                {
                    Name = process.ProcessName,
                    Status = "Running",
                    Path = executablePath,
                    PerformanceMode = profile?.PerformanceMode ?? "Gaming"
                });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Some protected/system processes cannot expose their main module.
            }
            catch (InvalidOperationException)
            {
                // The process may have exited during enumeration.
            }
            catch (NotSupportedException)
            {
                // Some process metadata is unavailable on the current Windows version.
            }
            catch (UnauthorizedAccessException)
            {
                // Protected processes may not expose their executable path.
            }
            finally
            {
                process.Dispose();
            }
        }

        var filtered = running
            .OrderByDescending(game => profiles?.Any(profile =>
                profile.Enabled &&
                string.Equals(profile.ExecutablePath, game.Path, StringComparison.OrdinalIgnoreCase)) == true)
            .ThenBy(p => p.Name)
            .Take(8)
            .ToArray();

        return filtered.Any() ? filtered : new[] { new GameSession { Name = "No active game detected", Status = "Idle", Path = string.Empty } };
    }

    private static bool IsKnownGameProcess(string processName) =>
        KnownGameProcesses.Contains(processName) ||
        processName.StartsWith("fivem_", StringComparison.OrdinalIgnoreCase);
}