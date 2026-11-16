using Microsoft.Win32;
using System.Runtime.Versioning;

namespace SleepOS.WindowsSystem;

public interface IWindowsStartupService
{
    bool IsEnabled { get; }
    void Configure(bool enabled, int delaySeconds, bool startMinimized);
}

public static class StartupCommandFormatter
{
    public static string Build(string executablePath, int delaySeconds, bool startMinimized)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("An executable path is required.", nameof(executablePath));
        }

        var arguments = new List<string> { "--startup" };
        if (delaySeconds > 0)
        {
            arguments.Add($"--startup-delay={Math.Clamp(delaySeconds, 0, 300)}");
        }

        if (startMinimized)
        {
            arguments.Add("--minimized");
        }

        return $"\"{Path.GetFullPath(executablePath)}\" {string.Join(" ", arguments)}";
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsStartupService : IWindowsStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SleepOS";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string command && !string.IsNullOrWhiteSpace(command);
        }
    }

    public void Configure(bool enabled, int delaySeconds, bool startMinimized)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows sign-in startup is only available on Windows.");
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Windows could not open the current-user startup registry key.");
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Windows did not provide the SleepOS executable path.");
        }

        key.SetValue(ValueName, StartupCommandFormatter.Build(executablePath, delaySeconds, startMinimized), RegistryValueKind.String);
    }
}
