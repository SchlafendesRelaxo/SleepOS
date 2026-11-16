using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using SleepOS.Core;

namespace SleepOS.Monitoring;

public interface IHardwareInventoryService
{
    HardwareInventoryReport Capture();
}

[SupportedOSPlatform("windows")]
public sealed class HardwareInventoryService : IHardwareInventoryService
{
    public HardwareInventoryReport Capture()
    {
        var items = new List<HardwareInventoryItem>();
        var warnings = new List<string>();

        AddCategory(items, warnings, "CPU",
            "SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor",
            device => new HardwareInventoryItem("CPU", GetValue(device, "Name"), FormatDetails(
                ("Manufacturer", GetValue(device, "Manufacturer")),
                ("Cores", GetValue(device, "NumberOfCores")),
                ("Logical processors", GetValue(device, "NumberOfLogicalProcessors")),
                ("Maximum reported clock", FormatMegahertz(device, "MaxClockSpeed")))));

        AddCategory(items, warnings, "GPU",
            "SELECT Name, AdapterCompatibility, DriverVersion FROM Win32_VideoController",
            device => new HardwareInventoryItem("GPU", GetValue(device, "Name"), FormatDetails(
                ("Manufacturer", GetValue(device, "AdapterCompatibility")),
                ("Driver version", GetValue(device, "DriverVersion")))));

        AddCategory(items, warnings, "Motherboard",
            "SELECT Product, Manufacturer FROM Win32_BaseBoard",
            device => new HardwareInventoryItem("Motherboard", GetValue(device, "Product"), FormatDetails(
                ("Manufacturer", GetValue(device, "Manufacturer")))));

        AddCategory(items, warnings, "BIOS",
            "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS",
            device => new HardwareInventoryItem("BIOS", GetValue(device, "SMBIOSBIOSVersion"), FormatDetails(
                ("Manufacturer", GetValue(device, "Manufacturer")),
                ("Release date", FormatDate(device, "ReleaseDate")))));

        AddCategory(items, warnings, "Memory module",
            "SELECT Manufacturer, PartNumber, Capacity, Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory",
            device => new HardwareInventoryItem(
                "Memory module",
                $"{GetValue(device, "Manufacturer")} {GetValue(device, "PartNumber")}".Trim(),
                FormatDetails(
                    ("Capacity", HardwareInventoryFormatting.FormatCapacity(GetUInt64(device, "Capacity"))),
                    ("Reported speed", FormatMegahertz(device, "Speed")),
                    ("Configured speed", FormatMegahertz(device, "ConfiguredClockSpeed")))));

        AddCategory(items, warnings, "Physical drive",
            "SELECT Model, InterfaceType, MediaType, Size, FirmwareRevision FROM Win32_DiskDrive",
            device => new HardwareInventoryItem("Physical drive", GetValue(device, "Model"),
                FormatDetails(
                    ("Capacity", HardwareInventoryFormatting.FormatCapacity(GetUInt64(device, "Size"))),
                    ("Interface", GetValue(device, "InterfaceType")),
                    ("Media type", GetValue(device, "MediaType")),
                    ("Firmware", GetValue(device, "FirmwareRevision")))));

        AddCategory(items, warnings, "Network adapter",
            "SELECT Name, Manufacturer, NetConnectionID, Speed FROM Win32_NetworkAdapter WHERE NetEnabled = TRUE",
            device => new HardwareInventoryItem("Network adapter", GetValue(device, "Name"), FormatDetails(
                ("Connection name", GetValue(device, "NetConnectionID")),
                ("Manufacturer", GetValue(device, "Manufacturer")),
                ("Link speed reported by Windows", FormatBitsPerSecond(device, "Speed")))));

        AddCategory(items, warnings, "Display",
            "SELECT Name, MonitorManufacturer, ScreenWidth, ScreenHeight FROM Win32_DesktopMonitor",
            device => new HardwareInventoryItem("Display", GetValue(device, "Name"),
                FormatDetails(
                    ("Resolution", FormatResolution(device)),
                    ("Refresh rate", "Unavailable"),
                    ("Manufacturer", GetValue(device, "MonitorManufacturer")))));

        AddCategory(items, warnings, "Operating system",
            "SELECT Caption, Version, BuildNumber, OSArchitecture FROM Win32_OperatingSystem",
            device => new HardwareInventoryItem("Operating system", GetValue(device, "Caption"), FormatDetails(
                ("Version", GetValue(device, "Version")),
                ("Build", GetValue(device, "BuildNumber")),
                ("Architecture", GetValue(device, "OSArchitecture")))));

        return new HardwareInventoryReport(DateTimeOffset.Now, items, warnings);
    }

    private static void AddCategory(
        ICollection<HardwareInventoryItem> items,
        ICollection<string> warnings,
        string category,
        string query,
        Func<ManagementObject, HardwareInventoryItem> map)
    {
        var count = 0;
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            using var results = searcher.Get();
            foreach (ManagementObject device in results)
            {
                using (device)
                {
                    items.Add(map(device));
                    count++;
                }
            }

            if (count == 0)
            {
                warnings.Add($"{category}: Windows returned no device information.");
            }
        }
        catch (ManagementException exception)
        {
            warnings.Add($"{category}: {exception.Message}");
        }
        catch (COMException exception)
        {
            warnings.Add($"{category}: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            warnings.Add($"{category}: {exception.Message}");
        }
        catch (SecurityException exception)
        {
            warnings.Add($"{category}: {exception.Message}");
        }
    }

    private static string GetValue(ManagementBaseObject device, string property)
    {
        var value = device[property];
        if (value is null or DBNull)
        {
            return "Unavailable";
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        return string.IsNullOrWhiteSpace(text) ? "Unavailable" : text;
    }

    private static ulong? GetUInt64(ManagementBaseObject device, string property)
    {
        return ulong.TryParse(GetValue(device, property), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string FormatMegahertz(ManagementBaseObject device, string property)
    {
        var value = GetValue(device, property);
        return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var megahertz)
            ? $"{megahertz.ToString("N0", CultureInfo.InvariantCulture)} MHz"
            : "Unavailable";
    }

    private static string FormatBitsPerSecond(ManagementBaseObject device, string property)
    {
        var value = GetValue(device, property);
        return ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bitsPerSecond)
            ? $"{(bitsPerSecond / 1_000_000_000d).ToString("F1", CultureInfo.InvariantCulture)} Gbps"
            : "Unavailable";
    }

    private static string FormatResolution(ManagementBaseObject device)
    {
        var width = GetValue(device, "ScreenWidth");
        var height = GetValue(device, "ScreenHeight");
        return width == "Unavailable" || height == "Unavailable"
            ? "Resolution unavailable"
            : $"{width} × {height} reported by Windows";
    }

    private static string FormatDate(ManagementBaseObject device, string property)
    {
        var value = GetValue(device, property);
        return value.Length >= 8 && DateOnly.TryParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : "Unavailable";
    }

    private static string FormatDetails(params (string Label, string Value)[] values)
    {
        var details = values
            .Where(item => !string.Equals(item.Value, "Unavailable", StringComparison.OrdinalIgnoreCase))
            .Select(item => $"{item.Label}: {item.Value}");
        var result = string.Join(" • ", details);
        return result.Length == 0 ? "No additional details reported by Windows." : result;
    }
}

public static class HardwareInventoryFormatting
{
    public static string FormatCapacity(ulong? bytes)
    {
        if (bytes is null or 0)
        {
            return "Unavailable";
        }

        const double bytesPerGibibyte = 1024d * 1024d * 1024d;
        return $"{(bytes.Value / bytesPerGibibyte).ToString("F1", CultureInfo.InvariantCulture)} GiB";
    }
}
