using System.Runtime.Versioning;

namespace SleepOS.WindowsSystem;

public interface IWindowsSystemService
{
    string GetActivePowerProfile();
    bool SetPowerProfile(string profileName);
    IReadOnlyList<string> GetNetworkAdapters();
    void ClearDnsCache();
    void RestartExplorer();
    void OpenWindowsUpdateSettings();
    void OpenWindowsStorageSettings();
    void OpenWindowsPowerSettings();
    void OpenWindowsTweakSettings(string tweakId);
}

public static class PowerProfileFormatter
{
    public static string FormatActiveProfile(string output)
    {
        var match = global::System.Text.RegularExpressions.Regex.Match(
            output,
            @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        if (!global::System.Guid.TryParse(match.Value, out var schemeId))
        {
            return "Unavailable";
        }

        return schemeId.ToString("D").ToLowerInvariant() switch
        {
            "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" => "High performance",
            "381b4222-f694-41f0-9685-ff5bb260df2e" => "Balanced",
            "a1841308-3541-4fab-bc81-f71556f20b4a" => "Power saver",
            _ => "Other Windows plan"
        };
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsSystemService : IWindowsSystemService
{
    [SupportedOSPlatform("windows")]
    public string GetActivePowerProfile()
    {
        try
        {
            var output = RunPowerShell("powercfg /getactivescheme");
            return PowerProfileFormatter.FormatActiveProfile(output);
        }
        catch (global::System.ComponentModel.Win32Exception)
        {
            return "Unavailable";
        }
        catch (InvalidOperationException)
        {
            return "Unavailable";
        }
    }

    [SupportedOSPlatform("windows")]
    public bool SetPowerProfile(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return false;
        }

        var normalized = profileName.Trim();
        string scheme = normalized switch
        {
            "Balanced" or "Custom" => "SCHEME_BALANCED",
            "High performance" or "Gaming" or "Performance" or "Extreme Gaming" => "SCHEME_MAX",
            _ => string.Empty
        };

        if (scheme.Length == 0)
        {
            return false;
        }

        return RunCommand("powercfg.exe", $"/setactive {scheme}").ExitCode == 0;
    }

    [SupportedOSPlatform("windows")]
    public IReadOnlyList<string> GetNetworkAdapters()
    {
        var adapters = new List<string>();
        using var searcher = new global::System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_NetworkAdapter WHERE NetEnabled = TRUE");
        foreach (var item in searcher.Get())
        {
            if (item["Name"] is string adapter)
            {
                adapters.Add(adapter);
            }
        }

        return adapters;
    }

    [SupportedOSPlatform("windows")]
    public void ClearDnsCache()
    {
        var result = RunCommand("ipconfig.exe", "/flushdns");
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Output) ? "Windows could not clear the DNS cache." : result.Output.Trim());
        }
    }

    [SupportedOSPlatform("windows")]
    public void RestartExplorer()
    {
        RunCommand("taskkill.exe", "/F /IM explorer.exe");
        var explorer = global::System.Diagnostics.Process.Start(new global::System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true
        });
        if (explorer is null)
        {
            throw new InvalidOperationException("Windows could not restart File Explorer.");
        }
    }

    [SupportedOSPlatform("windows")]
    public void OpenWindowsUpdateSettings() => OpenWindowsSettingsPage("ms-settings:windowsupdate");

    [SupportedOSPlatform("windows")]
    public void OpenWindowsStorageSettings() => OpenWindowsSettingsPage("ms-settings:storagesense");

    [SupportedOSPlatform("windows")]
    public void OpenWindowsPowerSettings() => OpenWindowsSettingsPage("ms-settings:powersleep");

    [SupportedOSPlatform("windows")]
    public void OpenWindowsTweakSettings(string tweakId)
    {
        var settingsPage = tweakId switch
        {
            "game-mode" => "ms-settings:gaming-gamemode",
            "game-dvr" => "ms-settings:gaming-captures",
            "game-bar" => "ms-settings:gaming-gamebar",
            "animations" => "ms-settings:easeofaccess-visualeffects",
            "transparency" => "ms-settings:colors",
            "startup" => "ms-settings:startupapps",
            "telemetry" => "ms-settings:privacy-feedback",
            "ad-id" => "ms-settings:privacy-general",
            "permissions" => "ms-settings:privacy",
            "dns" or "adapter-info" => "ms-settings:network-status",
            _ => null
        };

        if (settingsPage is not null)
        {
            OpenWindowsSettingsPage(settingsPage);
            return;
        }

        if (tweakId is "hidden-files" or "file-extensions")
        {
            var process = global::System.Diagnostics.Process.Start(new global::System.Diagnostics.ProcessStartInfo
            {
                FileName = "control.exe",
                Arguments = "folders",
                UseShellExecute = true
            });
            if (process is null)
            {
                throw new InvalidOperationException("Windows could not open File Explorer Options.");
            }

            return;
        }

        throw new InvalidOperationException($"No Windows settings action is defined for '{tweakId}'.");
    }

    private static void OpenWindowsSettingsPage(string pageUri)
    {
        var process = global::System.Diagnostics.Process.Start(new global::System.Diagnostics.ProcessStartInfo
        {
            FileName = pageUri,
            UseShellExecute = true
        });
        if (process is null)
        {
            throw new InvalidOperationException($"Windows could not open {pageUri}.");
        }
    }

    private static string RunPowerShell(string command)
    {
        return RunCommand("powershell.exe", $"-NoProfile -NonInteractive -Command \"{command}\"").Output;
    }

    private static (int ExitCode, string Output) RunCommand(string fileName, string arguments)
    {
        using var process = new global::System.Diagnostics.Process
        {
            StartInfo = new global::System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, string.Concat(output, error));
    }
}