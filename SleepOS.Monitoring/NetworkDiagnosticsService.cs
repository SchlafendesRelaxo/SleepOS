using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SleepOS.Monitoring;

public sealed record NetworkAdapterInfo(
    string Name,
    string Status,
    string Type,
    string Addresses,
    string Gateway,
    string DnsServers,
    string LinkSpeed);

public sealed record NetworkAdapterScanResult(
    IReadOnlyList<NetworkAdapterInfo> Adapters,
    IReadOnlyList<string> Warnings);

public sealed record NetworkPingSample(
    int Sequence,
    bool Received,
    long? RoundtripMilliseconds,
    string Status)
{
    public string RoundtripDisplay => RoundtripMilliseconds is { } roundtrip
        ? $"{roundtrip} ms"
        : "No reply";
}

public sealed record NetworkPingSummary(
    string Target,
    int PacketsSent,
    int PacketsReceived,
    double? PacketLossPercent,
    double? AverageMilliseconds,
    double? MinimumMilliseconds,
    double? MaximumMilliseconds,
    double? JitterMilliseconds,
    DateTimeOffset TestedAt);

public sealed record NetworkPingTestResult(
    NetworkPingSummary Summary,
    IReadOnlyList<NetworkPingSample> Samples);

public interface INetworkDiagnosticsService
{
    NetworkAdapterScanResult GetAdapters();
    Task<NetworkPingTestResult> TestPingAsync(string target, CancellationToken cancellationToken = default);
}

public sealed class NetworkDiagnosticsService : INetworkDiagnosticsService
{
    private const int ProbeCount = 5;
    private const int TimeoutMilliseconds = 1_000;
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(150);

    public NetworkAdapterScanResult GetAdapters()
    {
        var adapters = new List<NetworkAdapterInfo>();
        var warnings = new List<string>();
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                try
                {
                    var properties = adapter.GetIPProperties();
                    var addresses = properties.UnicastAddresses
                        .Select(address => address.Address.ToString())
                        .ToArray();
                    var gateways = properties.GatewayAddresses
                        .Select(gateway => gateway.Address)
                        .Where(address => !IPAddress.Any.Equals(address) && !IPAddress.IPv6Any.Equals(address))
                        .Select(address => address.ToString())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    var dnsServers = properties.DnsAddresses
                        .Select(address => address.ToString())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    adapters.Add(new NetworkAdapterInfo(
                        adapter.Name,
                        adapter.OperationalStatus.ToString(),
                        adapter.NetworkInterfaceType.ToString(),
                        JoinOrUnavailable(addresses),
                        JoinOrUnavailable(gateways),
                        JoinOrUnavailable(dnsServers),
                        FormatLinkSpeed(adapter.Speed)));
                }
                catch (NetworkInformationException exception)
                {
                    warnings.Add($"{adapter.Name}: {exception.Message}");
                }
            }
        }
        catch (NetworkInformationException exception)
        {
            warnings.Add($"Windows could not enumerate network adapters: {exception.Message}");
        }

        return new NetworkAdapterScanResult(
            adapters.OrderByDescending(adapter => adapter.Status == OperationalStatus.Up.ToString())
                .ThenBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            warnings);
    }

    public async Task<NetworkPingTestResult> TestPingAsync(
        string target,
        CancellationToken cancellationToken = default)
    {
        var normalizedTarget = ValidateTarget(target);
        var samples = new List<NetworkPingSample>(ProbeCount);
        using var ping = new Ping();

        for (var sequence = 1; sequence <= ProbeCount; sequence++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(normalizedTarget, TimeoutMilliseconds).ConfigureAwait(false);
                var received = reply.Status == IPStatus.Success;
                samples.Add(new NetworkPingSample(
                    sequence,
                    received,
                    received ? reply.RoundtripTime : null,
                    reply.Status.ToString()));
            }
            catch (PingException exception)
            {
                samples.Add(new NetworkPingSample(sequence, false, null, exception.InnerException?.Message ?? exception.Message));
            }
            catch (SocketException exception)
            {
                samples.Add(new NetworkPingSample(sequence, false, null, exception.Message));
            }

            if (sequence < ProbeCount)
            {
                await Task.Delay(ProbeInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        var summary = NetworkPingSummarizer.Summarize(normalizedTarget, samples, DateTimeOffset.Now);
        return new NetworkPingTestResult(summary, samples);
    }

    public static string ValidateTarget(string? target)
    {
        var normalizedTarget = target?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedTarget)
            || normalizedTarget.Any(char.IsWhiteSpace)
            || Uri.CheckHostName(normalizedTarget) == UriHostNameType.Unknown)
        {
            throw new ArgumentException("Enter a valid IP address or host name.", nameof(target));
        }

        return normalizedTarget;
    }

    private static string JoinOrUnavailable(IReadOnlyCollection<string> values) =>
        values.Count == 0 ? "Unavailable" : string.Join(", ", values);

    private static string FormatLinkSpeed(long bitsPerSecond)
    {
        if (bitsPerSecond <= 0)
        {
            return "Unavailable";
        }

        var gigabitsPerSecond = bitsPerSecond / 1_000_000_000d;
        return gigabitsPerSecond >= 1
            ? $"{gigabitsPerSecond.ToString("F1", CultureInfo.InvariantCulture)} Gbps"
            : $"{(bitsPerSecond / 1_000_000d).ToString("F0", CultureInfo.InvariantCulture)} Mbps";
    }
}

public static class NetworkPingSummarizer
{
    public static NetworkPingSummary Summarize(
        string target,
        IReadOnlyList<NetworkPingSample> samples,
        DateTimeOffset testedAt)
    {
        var roundtrips = samples
            .Where(sample => sample.Received && sample.RoundtripMilliseconds is >= 0)
            .Select(sample => sample.RoundtripMilliseconds!.Value)
            .ToArray();
        var sent = samples.Count;
        var received = roundtrips.Length;

        double? jitter = null;
        if (roundtrips.Length > 1)
        {
            var totalVariation = 0d;
            for (var index = 1; index < roundtrips.Length; index++)
            {
                totalVariation += Math.Abs(roundtrips[index] - roundtrips[index - 1]);
            }

            jitter = totalVariation / (roundtrips.Length - 1);
        }

        return new NetworkPingSummary(
            target,
            sent,
            received,
            sent == 0 ? null : (sent - received) * 100d / sent,
            roundtrips.Length == 0 ? null : roundtrips.Average(),
            roundtrips.Length == 0 ? null : roundtrips.Min(),
            roundtrips.Length == 0 ? null : roundtrips.Max(),
            jitter,
            testedAt);
    }
}
