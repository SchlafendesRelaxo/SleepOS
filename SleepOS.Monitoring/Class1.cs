using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SleepOS.Core;

namespace SleepOS.Monitoring;

public interface ISystemMonitorService
{
    SystemMetrics Capture();
}

[SupportedOSPlatform("windows")]
public sealed class SystemMonitorService : ISystemMonitorService
{
    public SystemMetrics Capture()
    {
        var memory = GetMemoryUsage();
        var network = GetNetworkThroughput();

        return new SystemMetrics
        {
            CpuUsagePercent = GetCpuUsage(),
            GpuUsagePercent = GetGpuLoadPercentage(),
            RamUsedGb = memory.usedGb,
            TotalRamGb = memory.totalGb,
            DiskUsagePercent = GetSystemDriveUsage(),
            DownloadMbps = network.downloadMbps,
            UploadMbps = network.uploadMbps,
            UptimeText = TimeSpan.FromMilliseconds(Environment.TickCount64).ToString("d' days 'hh':'mm':'ss"),
            RunningProcesses = GetProcesses()
        };
    }

    private static double? GetCpuUsage()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name = '_Total'");
            foreach (ManagementObject item in searcher.Get())
            {
                using (item)
                {
                    if (TryGetDouble(item["PercentProcessorTime"], out var usage))
                    {
                        return Math.Clamp(usage, 0d, 100d);
                    }
                }
            }
        }
        catch (ManagementException)
        {
            return null;
        }
        catch (COMException)
        {
            return null;
        }

        return null;
    }

    private static (double? usedGb, double? totalGb) GetMemoryUsage()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0)
        {
            return (null, null);
        }

        const double bytesPerGigabyte = 1024d * 1024d * 1024d;
        var totalGb = status.TotalPhys / bytesPerGigabyte;
        return ((status.TotalPhys - status.AvailPhys) / bytesPerGigabyte, totalGb);
    }

    private static double? GetSystemDriveUsage()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            if (!drive.IsReady || drive.TotalSize == 0)
            {
                return null;
            }

            return Math.Clamp((1d - drive.AvailableFreeSpace / (double)drive.TotalSize) * 100d, 0d, 100d);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static (double? downloadMbps, double? uploadMbps) GetNetworkThroughput()
    {
        var receivedBytesPerSecond = 0d;
        var sentBytesPerSecond = 0d;
        var foundInterface = false;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT BytesReceivedPerSec, BytesSentPerSec FROM Win32_PerfFormattedData_Tcpip_NetworkInterface");
            foreach (ManagementObject item in searcher.Get())
            {
                using (item)
                {
                    foundInterface = true;
                    if (TryGetDouble(item["BytesReceivedPerSec"], out var received))
                    {
                        receivedBytesPerSecond += received;
                    }

                    if (TryGetDouble(item["BytesSentPerSec"], out var sent))
                    {
                        sentBytesPerSecond += sent;
                    }
                }
            }
        }
        catch (ManagementException)
        {
            return (null, null);
        }
        catch (COMException)
        {
            return (null, null);
        }

        if (!foundInterface)
        {
            return (null, null);
        }

        return (ToMegabitsPerSecond(receivedBytesPerSecond), ToMegabitsPerSecond(sentBytesPerSecond));
    }

    private static double? GetGpuLoadPercentage()
    {
        double? highestEngineLoad = null;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT LoadPercentage FROM Win32_PerfFormattedData_Counters_GPUEngine");
            foreach (ManagementObject item in searcher.Get())
            {
                using (item)
                {
                    if (TryGetDouble(item["LoadPercentage"], out var load))
                    {
                        highestEngineLoad = Math.Max(highestEngineLoad ?? 0d, load);
                    }
                }
            }
        }
        catch (ManagementException)
        {
            return null;
        }
        catch (COMException)
        {
            return null;
        }

        return highestEngineLoad is null ? null : Math.Clamp(highestEngineLoad.Value, 0d, 100d);
    }

    private static bool TryGetDouble(object? value, out double result)
    {
        return double.TryParse(value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static double ToMegabitsPerSecond(double bytesPerSecond) => bytesPerSecond * 8d / (1024d * 1024d);

    private static IReadOnlyList<ProcessPerformance> GetProcesses()
    {
        var processes = new List<ProcessPerformance>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(process.ProcessName) && process.ProcessName.Length > 1)
                {
                    processes.Add(new ProcessPerformance
                    {
                        Name = process.ProcessName,
                        ProcessId = process.Id,
                        MemoryMb = Math.Round(process.WorkingSet64 / (1024d * 1024d), 1)
                    });
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Protected processes may not expose their memory usage.
            }
            catch (InvalidOperationException)
            {
                // A process can exit while the process list is being read.
            }
            finally
            {
                process.Dispose();
            }
        }

        return processes
            .OrderByDescending(process => process.MemoryMb)
            .Take(8)
            .ToArray();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}
