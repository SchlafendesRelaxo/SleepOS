using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.VisualBasic.FileIO;

namespace SleepOS.WindowsSystem;

public sealed class ManagedProcessCandidate
{
    public int ProcessId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public string ProductDescription { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public long StartTimeUtcTicks { get; init; }
    public double MemoryMb { get; init; }
    public bool HasVisibleWindow { get; init; }
    public bool IsForeground { get; init; }
    public TimeSpan InactiveFor { get; init; }
    public string WindowActivity => IsForeground ? "Foreground" : HasVisibleWindow ? "Background" : "No visible window";
    public double? CpuUsagePercent { get; init; }
    public string CpuUsageText => CpuUsagePercent is { } cpu ? $"{cpu:F1}% CPU" : "CPU N/A";
    public string Priority { get; init; } = "Unavailable";
    public bool HasPublisherMetadata { get; init; }
    public string Recommendation { get; init; } = string.Empty;
    public string RecommendationDetails { get; init; } = string.Empty;
    public bool IsSuggestedForReview { get; init; }
    public bool IsSelected { get; set; }
    public bool IsMemorySaverManaged { get; set; }
}

public sealed record ProcessScanResult(IReadOnlyList<ManagedProcessCandidate> Processes, int SkippedProcesses);

public sealed record ProcessStopResult(int StoppedCount, IReadOnlyList<string> Failures);
public sealed record ProcessEfficiencyResult(int UpdatedCount, IReadOnlyList<string> Failures);
public sealed record ProcessPriorityResult(int UpdatedCount, IReadOnlyList<string> Failures);

public sealed record ProcessRecommendation(string Title, string Details, bool IsSuggestedForReview);

public static class ProcessRecommendationRules
{
    public static ProcessRecommendation Evaluate(
        bool isRecognizedGame,
        bool hasVisibleWindow,
        bool hasPublisherMetadata)
    {
        if (isRecognizedGame)
        {
            return new ProcessRecommendation(
                "Active game",
                "Not recommended to close while you are playing.",
                false);
        }

        if (hasVisibleWindow)
        {
            return new ProcessRecommendation(
                "Visible app",
                "This app has a visible window, so it is not suggested for review. Memory use alone does not make an app unnecessary.",
                false);
        }

        return hasPublisherMetadata
            ? new ProcessRecommendation(
                "Hidden background app",
                "No visible window. It may still be needed by another app; review its name and path before closing.",
                true)
            : new ProcessRecommendation(
                "Unknown background app",
                "No visible window and publisher metadata is missing. This does not mean it is unsafe or unnecessary; check its description and path before closing.",
                true);
    }
}

public interface IProcessManagementService
{
    ProcessScanResult GetStoppableProcesses(IReadOnlySet<string>? activeGameProcessNames = null);
    ProcessStopResult StopSelectedProcesses(IEnumerable<ManagedProcessCandidate> candidates);
    ProcessEfficiencyResult SetEfficiencyMode(IEnumerable<ManagedProcessCandidate> candidates, bool enabled);
    ProcessEfficiencyResult RestoreEfficiencyModes();
    ProcessPriorityResult SetPriority(IEnumerable<ManagedProcessCandidate> candidates, ProcessPriorityClass? priorityClass);
    ProcessPriorityResult RestoreProcessPriorities();
}

public sealed class ProcessManagementService : IProcessManagementService
{
    private const uint ProcessSetInformation = 0x0200;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint ExecutionSpeedThrottling = 0x1;
    private const int ProcessPowerThrottlingInformation = 4;
    private readonly object _cpuSamplesLock = new();
    private readonly Dictionary<int, (long StartTimeTicks, TimeSpan CpuTime, DateTime SampledAt)> _cpuSamples = [];
    private readonly Dictionary<(int ProcessId, long StartTimeTicks), DateTime> _inactiveSince = [];
    private readonly Dictionary<(int ProcessId, long StartTimeTicks), ProcessPowerThrottlingState> _originalPowerStates = [];
    private readonly Dictionary<(int ProcessId, long StartTimeTicks), ProcessPriorityClass> _originalPriorities = [];
    private readonly object _powerStatesLock = new();

    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "audiodg", "conhost", "csrss", "dwm", "explorer", "fontdrvhost", "lsass",
        "memory compression", "registry", "services", "sihost", "smss", "svchost",
        "system", "taskhostw", "wininit", "winlogon"
    };

    public ProcessScanResult GetStoppableProcesses(IReadOnlySet<string>? activeGameProcessNames = null)
    {
        var candidates = new List<ManagedProcessCandidate>();
        var skipped = 0;
        var observedProcessIds = new HashSet<int>();
        var serviceProcessIds = GetRunningServiceProcessIds();
        var foregroundProcessId = GetForegroundProcessId();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                observedProcessIds.Add(process.Id);
                if (serviceProcessIds.Contains(process.Id))
                {
                    continue;
                }

                var executablePath = GetEligibleExecutablePath(process);
                if (executablePath is null)
                {
                    continue;
                }

                var memoryMb = Math.Round(process.WorkingSet64 / (1024d * 1024d), 1);
                var hasVisibleWindow = process.MainWindowHandle != IntPtr.Zero;
                var startTimeTicks = process.StartTime.ToUniversalTime().Ticks;
                var isForeground = process.Id == foregroundProcessId;
                var inactiveFor = GetInactiveDuration(process.Id, startTimeTicks, isForeground);
                var fileVersion = FileVersionInfo.GetVersionInfo(executablePath);
                var productDescription = FirstNonEmpty(
                    fileVersion.ProductName,
                    fileVersion.FileDescription,
                    process.ProcessName);
                var hasPublisherMetadata = !string.IsNullOrWhiteSpace(fileVersion.CompanyName);
                var publisher = hasPublisherMetadata ? fileVersion.CompanyName! : "Not listed in file metadata";
                var recommendation = ProcessRecommendationRules.Evaluate(
                    activeGameProcessNames?.Contains(process.ProcessName) == true,
                    hasVisibleWindow,
                    hasPublisherMetadata);

                candidates.Add(new ManagedProcessCandidate
                {
                    ProcessId = process.Id,
                    Name = process.ProcessName,
                    ExecutablePath = executablePath,
                    ProductDescription = productDescription,
                    Publisher = publisher,
                    StartTimeUtcTicks = startTimeTicks,
                    MemoryMb = memoryMb,
                    HasVisibleWindow = hasVisibleWindow,
                    IsForeground = isForeground,
                    InactiveFor = inactiveFor,
                    CpuUsagePercent = GetCpuUsage(process, startTimeTicks),
                    Priority = GetPriority(process),
                    HasPublisherMetadata = hasPublisherMetadata,
                    Recommendation = recommendation.Title,
                    RecommendationDetails = recommendation.Details,
                    IsSuggestedForReview = recommendation.IsSuggestedForReview
                });
            }
            catch (Win32Exception)
            {
                skipped++;
            }
            catch (InvalidOperationException)
            {
                skipped++;
            }
            catch (NotSupportedException)
            {
                skipped++;
            }
            catch (UnauthorizedAccessException)
            {
                skipped++;
            }
            catch (ArgumentException)
            {
                skipped++;
            }
            catch (IOException)
            {
                skipped++;
            }
            finally
            {
                process.Dispose();
            }
        }

        lock (_cpuSamplesLock)
        {
            foreach (var processId in _cpuSamples.Keys.Where(processId => !observedProcessIds.Contains(processId)).ToArray())
            {
                _cpuSamples.Remove(processId);
            }

            foreach (var key in _inactiveSince.Keys.Where(key => !observedProcessIds.Contains(key.ProcessId)).ToArray())
            {
                _inactiveSince.Remove(key);
            }
        }

        return new ProcessScanResult(
            candidates.OrderByDescending(candidate => candidate.IsSuggestedForReview)
                .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.ProcessId)
                .ToArray(),
            skipped);
    }

    public ProcessStopResult StopSelectedProcesses(IEnumerable<ManagedProcessCandidate> candidates)
    {
        var stopped = 0;
        var failures = new List<string>();
        var serviceProcessIds = GetRunningServiceProcessIds();
        foreach (var candidate in candidates.DistinctBy(candidate => candidate.ProcessId))
        {
            try
            {
                if (serviceProcessIds.Contains(candidate.ProcessId))
                {
                    failures.Add($"{candidate.Name}: Windows service processes are protected.");
                    continue;
                }

                using var process = Process.GetProcessById(candidate.ProcessId);
                if (process.HasExited ||
                    !string.Equals(process.ProcessName, candidate.Name, StringComparison.OrdinalIgnoreCase) ||
                    process.StartTime.ToUniversalTime().Ticks != candidate.StartTimeUtcTicks ||
                    !string.Equals(GetEligibleExecutablePath(process), candidate.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"{candidate.Name}: process changed or is no longer eligible.");
                    continue;
                }

                process.Kill();
                if (!process.WaitForExit(3000))
                {
                    failures.Add($"{candidate.Name}: did not exit in time.");
                    continue;
                }

                stopped++;
            }
            catch (Win32Exception exception)
            {
                failures.Add($"{candidate.Name}: {exception.Message}");
            }
            catch (InvalidOperationException)
            {
                failures.Add($"{candidate.Name}: process already exited.");
            }
            catch (NotSupportedException exception)
            {
                failures.Add($"{candidate.Name}: {exception.Message}");
            }
            catch (UnauthorizedAccessException)
            {
                failures.Add($"{candidate.Name}: access denied.");
            }
            catch (ArgumentException)
            {
                failures.Add($"{candidate.Name}: process no longer exists.");
            }
            catch (IOException exception)
            {
                failures.Add($"{candidate.Name}: {exception.Message}");
            }
        }

        return new ProcessStopResult(stopped, failures);
    }

    [SupportedOSPlatform("windows")]
    public ProcessEfficiencyResult SetEfficiencyMode(IEnumerable<ManagedProcessCandidate> candidates, bool enabled)
    {
        var updated = 0;
        var failures = new List<string>();
        var serviceProcessIds = GetRunningServiceProcessIds();

        foreach (var candidate in candidates.DistinctBy(candidate => (candidate.ProcessId, candidate.StartTimeUtcTicks)))
        {
            try
            {
                if (candidate.ProcessId == Environment.ProcessId || serviceProcessIds.Contains(candidate.ProcessId))
                {
                    failures.Add($"{candidate.Name}: SleepOS and Windows service processes are protected.");
                    continue;
                }

                using var process = Process.GetProcessById(candidate.ProcessId);
                var startTimeTicks = process.StartTime.ToUniversalTime().Ticks;
                if (process.HasExited ||
                    !string.Equals(process.ProcessName, candidate.Name, StringComparison.OrdinalIgnoreCase) ||
                    startTimeTicks != candidate.StartTimeUtcTicks ||
                    !string.Equals(GetEligibleExecutablePath(process), candidate.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"{candidate.Name}: process changed or is no longer eligible.");
                    continue;
                }

                var processHandle = OpenProcess(
                    ProcessSetInformation | ProcessQueryLimitedInformation,
                    false,
                    candidate.ProcessId);
                if (processHandle == IntPtr.Zero)
                {
                    failures.Add($"{candidate.Name}: Windows denied access to change the process efficiency policy.");
                    continue;
                }

                try
                {
                    var state = new ProcessPowerThrottlingState { Version = 1 };
                    if (!GetProcessInformation(processHandle, ProcessPowerThrottlingInformation, ref state, (uint)Marshal.SizeOf<ProcessPowerThrottlingState>()))
                    {
                        failures.Add($"{candidate.Name}: Windows does not expose a restorable efficiency policy for this process.");
                        continue;
                    }

                    var key = (candidate.ProcessId, startTimeTicks);
                    ProcessPowerThrottlingState target;
                    var hasPreviousState = false;
                    lock (_powerStatesLock)
                    {
                        if (enabled)
                        {
                            _originalPowerStates.TryAdd(key, state);
                            target = state;
                            target.ControlMask |= ExecutionSpeedThrottling;
                            target.StateMask |= ExecutionSpeedThrottling;
                        }
                        else if (_originalPowerStates.TryGetValue(key, out var previousState))
                        {
                            target = previousState;
                            hasPreviousState = true;
                        }
                        else
                        {
                            failures.Add($"{candidate.Name}: no SleepOS efficiency policy is available to restore.");
                            continue;
                        }
                    }

                    if (!SetProcessInformation(processHandle, ProcessPowerThrottlingInformation, ref target, (uint)Marshal.SizeOf<ProcessPowerThrottlingState>()))
                    {
                        failures.Add($"{candidate.Name}: Windows could not apply the efficiency policy.");
                        continue;
                    }

                    updated++;
                    if (!enabled && hasPreviousState)
                    {
                        lock (_powerStatesLock)
                        {
                            _originalPowerStates.Remove(key);
                        }
                    }
                }
                finally
                {
                    CloseHandle(processHandle);
                }
            }
            catch (Win32Exception exception)
            {
                failures.Add($"{candidate.Name}: {exception.Message}");
            }
            catch (InvalidOperationException)
            {
                failures.Add($"{candidate.Name}: process already exited.");
            }
            catch (UnauthorizedAccessException)
            {
                failures.Add($"{candidate.Name}: access denied.");
            }
            catch (ArgumentException)
            {
                failures.Add($"{candidate.Name}: process no longer exists.");
            }
        }

        return new ProcessEfficiencyResult(updated, failures);
    }

    [SupportedOSPlatform("windows")]
    public ProcessEfficiencyResult RestoreEfficiencyModes()
    {
        KeyValuePair<(int ProcessId, long StartTimeTicks), ProcessPowerThrottlingState>[] states;
        lock (_powerStatesLock)
        {
            states = _originalPowerStates.ToArray();
        }

        var restored = 0;
        var failures = new List<string>();
        foreach (var (key, previousState) in states)
        {
            try
            {
                using var process = Process.GetProcessById(key.ProcessId);
                if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != key.StartTimeTicks)
                {
                    lock (_powerStatesLock)
                    {
                        _originalPowerStates.Remove(key);
                    }

                    continue;
                }

                var processHandle = OpenProcess(
                    ProcessSetInformation | ProcessQueryLimitedInformation,
                    false,
                    key.ProcessId);
                if (processHandle == IntPtr.Zero)
                {
                    failures.Add($"{process.ProcessName}: Windows denied access to restore its efficiency policy.");
                    continue;
                }

                try
                {
                    var target = previousState;
                    if (!SetProcessInformation(processHandle, ProcessPowerThrottlingInformation, ref target, (uint)Marshal.SizeOf<ProcessPowerThrottlingState>()))
                    {
                        failures.Add($"{process.ProcessName}: Windows could not restore its previous efficiency policy.");
                        continue;
                    }

                    restored++;
                    lock (_powerStatesLock)
                    {
                        _originalPowerStates.Remove(key);
                    }
                }
                finally
                {
                    CloseHandle(processHandle);
                }
            }
            catch (ArgumentException)
            {
                lock (_powerStatesLock)
                {
                    _originalPowerStates.Remove(key);
                }
            }
            catch (Win32Exception exception)
            {
                failures.Add($"PID {key.ProcessId}: {exception.Message}");
            }
            catch (InvalidOperationException)
            {
                lock (_powerStatesLock)
                {
                    _originalPowerStates.Remove(key);
                }
            }
            catch (UnauthorizedAccessException)
            {
                failures.Add($"PID {key.ProcessId}: access denied while restoring its efficiency policy.");
            }
        }

        return new ProcessEfficiencyResult(restored, failures);
    }

    [SupportedOSPlatform("windows")]
    public ProcessPriorityResult SetPriority(IEnumerable<ManagedProcessCandidate> candidates, ProcessPriorityClass? priorityClass)
    {
        if (priorityClass is not null &&
            priorityClass is not (ProcessPriorityClass.Normal or ProcessPriorityClass.BelowNormal))
        {
            throw new ArgumentOutOfRangeException(nameof(priorityClass), "SleepOS supports only Normal and BelowNormal process priorities.");
        }

        var updated = 0;
        var failures = new List<string>();
        var serviceProcessIds = GetRunningServiceProcessIds();
        foreach (var candidate in candidates.DistinctBy(candidate => (candidate.ProcessId, candidate.StartTimeUtcTicks)))
        {
            try
            {
                if (candidate.ProcessId == Environment.ProcessId || serviceProcessIds.Contains(candidate.ProcessId))
                {
                    failures.Add($"{candidate.Name}: SleepOS and Windows service processes are protected.");
                    continue;
                }

                using var process = Process.GetProcessById(candidate.ProcessId);
                var startTimeTicks = process.StartTime.ToUniversalTime().Ticks;
                if (process.HasExited ||
                    !string.Equals(process.ProcessName, candidate.Name, StringComparison.OrdinalIgnoreCase) ||
                    startTimeTicks != candidate.StartTimeUtcTicks ||
                    !string.Equals(GetEligibleExecutablePath(process), candidate.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"{candidate.Name}: process changed or is no longer eligible.");
                    continue;
                }

                var key = (candidate.ProcessId, startTimeTicks);
                if (priorityClass is null)
                {
                    ProcessPriorityClass originalPriority;
                    lock (_powerStatesLock)
                    {
                        if (!_originalPriorities.TryGetValue(key, out originalPriority))
                        {
                            failures.Add($"{candidate.Name}: no SleepOS priority change is available to restore.");
                            continue;
                        }
                    }

                    process.PriorityClass = originalPriority;
                    lock (_powerStatesLock)
                    {
                        _originalPriorities.Remove(key);
                    }
                }
                else
                {
                    var currentPriority = process.PriorityClass;
                    lock (_powerStatesLock)
                    {
                        _originalPriorities.TryAdd(key, currentPriority);
                    }

                    process.PriorityClass = priorityClass.Value;
                }

                updated++;
            }
            catch (Win32Exception exception)
            {
                failures.Add($"{candidate.Name}: {exception.Message}");
            }
            catch (InvalidOperationException)
            {
                failures.Add($"{candidate.Name}: process already exited.");
            }
            catch (UnauthorizedAccessException)
            {
                failures.Add($"{candidate.Name}: access denied.");
            }
            catch (ArgumentException)
            {
                failures.Add($"{candidate.Name}: process no longer exists.");
            }
        }

        return new ProcessPriorityResult(updated, failures);
    }

    [SupportedOSPlatform("windows")]
    public ProcessPriorityResult RestoreProcessPriorities()
    {
        KeyValuePair<(int ProcessId, long StartTimeTicks), ProcessPriorityClass>[] priorities;
        lock (_powerStatesLock)
        {
            priorities = _originalPriorities.ToArray();
        }

        var restored = 0;
        var failures = new List<string>();
        foreach (var (key, priority) in priorities)
        {
            try
            {
                using var process = Process.GetProcessById(key.ProcessId);
                if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != key.StartTimeTicks)
                {
                    lock (_powerStatesLock)
                    {
                        _originalPriorities.Remove(key);
                    }

                    continue;
                }

                process.PriorityClass = priority;
                restored++;
                lock (_powerStatesLock)
                {
                    _originalPriorities.Remove(key);
                }
            }
            catch (Win32Exception exception)
            {
                failures.Add($"PID {key.ProcessId}: {exception.Message}");
            }
            catch (InvalidOperationException)
            {
                lock (_powerStatesLock)
                {
                    _originalPriorities.Remove(key);
                }
            }
            catch (UnauthorizedAccessException)
            {
                failures.Add($"PID {key.ProcessId}: access denied while restoring its priority.");
            }
            catch (ArgumentException)
            {
                lock (_powerStatesLock)
                {
                    _originalPriorities.Remove(key);
                }
            }
        }

        return new ProcessPriorityResult(restored, failures);
    }

    private double? GetCpuUsage(Process process, long startTimeTicks)
    {
        var now = DateTime.UtcNow;
        var cpuTime = process.TotalProcessorTime;
        lock (_cpuSamplesLock)
        {
            if (_cpuSamples.TryGetValue(process.Id, out var previous) &&
                previous.StartTimeTicks == startTimeTicks &&
                now > previous.SampledAt)
            {
                var elapsed = (now - previous.SampledAt).TotalMilliseconds;
                var cpuMilliseconds = (cpuTime - previous.CpuTime).TotalMilliseconds;
                _cpuSamples[process.Id] = (startTimeTicks, cpuTime, now);
                return Math.Clamp(cpuMilliseconds / elapsed / Environment.ProcessorCount * 100d, 0d, 100d);
            }

            _cpuSamples[process.Id] = (startTimeTicks, cpuTime, now);
            return null;
        }
    }

    private TimeSpan GetInactiveDuration(int processId, long startTimeTicks, bool isForeground)
    {
        var key = (processId, startTimeTicks);
        var now = DateTime.UtcNow;
        lock (_cpuSamplesLock)
        {
            if (isForeground)
            {
                _inactiveSince[key] = now;
                return TimeSpan.Zero;
            }

            if (!_inactiveSince.TryGetValue(key, out var inactiveSince))
            {
                inactiveSince = now;
                _inactiveSince[key] = inactiveSince;
            }

            return now - inactiveSince;
        }
    }

    private static int GetForegroundProcessId()
    {
        var foregroundWindow = GetForegroundWindow();
        return foregroundWindow == IntPtr.Zero ||
            GetWindowThreadProcessId(foregroundWindow, out var processId) == 0
                ? 0
                : unchecked((int)processId);
    }

    private static string GetPriority(Process process)
    {
        try
        {
            return process.PriorityClass.ToString();
        }
        catch (Win32Exception)
        {
            return "Unavailable";
        }
        catch (InvalidOperationException)
        {
            return "Unavailable";
        }
        catch (UnauthorizedAccessException)
        {
            return "Unavailable";
        }
    }

    private static HashSet<int> GetRunningServiceProcessIds()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new HashSet<int>();
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId FROM Win32_Service WHERE State = 'Running'");
            using var services = searcher.Get();
            var processIds = new HashSet<int>();
            foreach (ManagementObject service in services)
            {
                using (service)
                {
                    if (service["ProcessId"] is uint processId && processId > 0)
                    {
                        processIds.Add((int)processId);
                    }
                }
            }

            return processIds;
        }
        catch (ManagementException exception)
        {
            throw new InvalidOperationException("Windows could not verify running service processes.", exception);
        }
    }

    private static string? GetEligibleExecutablePath(Process process)
    {
        if (process.Id == Environment.ProcessId || ProtectedProcessNames.Contains(process.ProcessName))
        {
            return null;
        }

        var executablePath = process.MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(executablePath);
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDirectory))
        {
            return null;
        }

        var windowsRoot = Path.GetFullPath(windowsDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(windowsRoot, StringComparison.OrdinalIgnoreCase) ? null : fullPath;
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "No file description available";

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessInformation(
        IntPtr process,
        int processInformationClass,
        ref ProcessPowerThrottlingState processInformation,
        uint processInformationSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessInformation(
        IntPtr process,
        int processInformationClass,
        ref ProcessPowerThrottlingState processInformation,
        uint processInformationSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessPowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}

public sealed class DesktopShortcutCandidate
{
    public string Name { get; init; } = string.Empty;
    public string ShortcutPath { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public string Arguments { get; init; } = string.Empty;
    public long LastWriteTimeUtcTicks { get; init; }
    public bool IsSelected { get; set; } = true;
}

public sealed record DesktopShortcutScanResult(
    IReadOnlyList<DesktopShortcutCandidate> Duplicates,
    IReadOnlyList<string> Warnings);

public sealed record DesktopShortcutCleanupResult(int RemovedCount, IReadOnlyList<string> Failures);

public static class DesktopShortcutRules
{
    public static IReadOnlyList<DesktopShortcutCandidate> FindDuplicateCandidates(
        IEnumerable<DesktopShortcutCandidate> shortcuts) =>
        shortcuts
            .Where(shortcut =>
                !string.IsNullOrWhiteSpace(shortcut.ShortcutPath) &&
                !string.IsNullOrWhiteSpace(shortcut.TargetPath))
            .GroupBy(shortcut => shortcut.TargetPath, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group
                .GroupBy(shortcut => shortcut.Arguments.Trim(), StringComparer.Ordinal)
                .Where(argumentGroup => argumentGroup.Count() > 1)
                .SelectMany(argumentGroup => argumentGroup
                    .OrderBy(shortcut => shortcut.ShortcutPath, StringComparer.OrdinalIgnoreCase)
                    .Skip(1)))
            .OrderBy(shortcut => shortcut.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

public interface IDesktopShortcutService
{
    DesktopShortcutScanResult FindDuplicates();
    DesktopShortcutCleanupResult RemoveSelectedDuplicates(IEnumerable<DesktopShortcutCandidate> candidates);
}

[SupportedOSPlatform("windows")]
public sealed class DesktopShortcutService : IDesktopShortcutService
{
    public DesktopShortcutScanResult FindDuplicates()
    {
        var shortcuts = new List<DesktopShortcutCandidate>();
        var warnings = new List<string>();
        var desktopFolders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in desktopFolders)
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            try
            {
                foreach (var shortcutPath in Directory.EnumerateFiles(folder, "*.lnk", System.IO.SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var details = ReadShortcut(shortcutPath);
                        if (!string.IsNullOrWhiteSpace(details.TargetPath))
                        {
                            shortcuts.Add(new DesktopShortcutCandidate
                            {
                                Name = Path.GetFileNameWithoutExtension(shortcutPath),
                                ShortcutPath = shortcutPath,
                                TargetPath = ResolveTargetPath(shortcutPath, details.TargetPath),
                                Arguments = details.Arguments,
                                LastWriteTimeUtcTicks = File.GetLastWriteTimeUtc(shortcutPath).Ticks
                            });
                        }
                    }
                    catch (Exception exception) when (
                        exception is COMException or IOException or UnauthorizedAccessException or InvalidOperationException or
                            PlatformNotSupportedException or ArgumentException)
                    {
                        warnings.Add($"{Path.GetFileName(shortcutPath)} could not be inspected: {exception.Message}");
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"{folder} could not be scanned: {exception.Message}");
            }
        }

        var duplicates = DesktopShortcutRules.FindDuplicateCandidates(shortcuts);

        return new DesktopShortcutScanResult(duplicates, warnings);
    }

    public DesktopShortcutCleanupResult RemoveSelectedDuplicates(IEnumerable<DesktopShortcutCandidate> candidates)
    {
        var removed = 0;
        var failures = new List<string>();
        foreach (var candidate in candidates.Where(candidate => candidate.IsSelected).DistinctBy(candidate => candidate.ShortcutPath))
        {
            try
            {
                if (!File.Exists(candidate.ShortcutPath) ||
                    File.GetLastWriteTimeUtc(candidate.ShortcutPath).Ticks != candidate.LastWriteTimeUtcTicks)
                {
                    failures.Add($"{candidate.Name}: shortcut changed since the scan; scan again before removing it.");
                    continue;
                }

                var details = ReadShortcut(candidate.ShortcutPath);
                if (!string.Equals(
                        ResolveTargetPath(candidate.ShortcutPath, details.TargetPath),
                        candidate.TargetPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(details.Arguments.Trim(), candidate.Arguments.Trim(), StringComparison.Ordinal))
                {
                    failures.Add($"{candidate.Name}: shortcut changed since the scan; scan again before removing it.");
                    continue;
                }

                FileSystem.DeleteFile(
                    candidate.ShortcutPath,
                    UIOption.OnlyErrorDialogs,
                    RecycleOption.SendToRecycleBin);
                removed++;
            }
            catch (Exception exception) when (
                exception is COMException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                failures.Add($"{candidate.Name}: {exception.Message}");
            }
        }

        return new DesktopShortcutCleanupResult(removed, failures);
    }

    private static string ResolveTargetPath(string shortcutPath, string targetPath)
    {
        var shortcutDirectory = Path.GetDirectoryName(shortcutPath)
            ?? throw new InvalidOperationException("Shortcut directory is unavailable.");
        return Path.GetFullPath(targetPath, shortcutDirectory);
    }

    private static ShortcutDetails ReadShortcut(string shortcutPath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new PlatformNotSupportedException("Windows Script Host is unavailable.");
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("Windows could not create the shortcut reader.");
            dynamic shellObject = shell;
            shortcut = shellObject.CreateShortcut(shortcutPath);
            dynamic shortcutObject = shortcut;
            return new ShortcutDetails(
                (string?)shortcutObject.TargetPath ?? string.Empty,
                (string?)shortcutObject.Arguments ?? string.Empty);
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                Marshal.FinalReleaseComObject(shortcut);
            }

            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private sealed record ShortcutDetails(string TargetPath, string Arguments);
}
