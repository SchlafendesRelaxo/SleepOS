using SleepOS.Core;
using System.Diagnostics;

﻿namespace SleepOS.Games;

public interface IGameService
{
    IReadOnlyList<GameSession> GetDetectedGames();
    IReadOnlyList<GameSession> GetRunningGames();
}

public sealed class GameService : IGameService
{
    private static readonly HashSet<string> KnownGameProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "apex_legends", "cs2", "dota2", "eldenring", "fortniteclient-win64-shipping",
        "gta5", "league of legends", "overwatch", "valorant-win64-shipping", "witcher3"
    };

    public IReadOnlyList<GameSession> GetDetectedGames()
    {
        return GetRunningGames();
    }

    public IReadOnlyList<GameSession> GetRunningGames()
    {
        var running = new List<GameSession>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!KnownGameProcesses.Contains(process.ProcessName))
                {
                    continue;
                }

                running.Add(new GameSession
                {
                    Name = process.ProcessName,
                    Status = "Running",
                    Path = process.MainModule?.FileName ?? string.Empty
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
            finally
            {
                process.Dispose();
            }
        }

        var filtered = running
            .OrderBy(p => p.Name)
            .Take(8)
            .ToArray();

        return filtered.Any() ? filtered : new[] { new GameSession { Name = "No active game detected", Status = "Idle", Path = string.Empty } };
    }
}