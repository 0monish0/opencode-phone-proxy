using System.Net;

namespace OpenCodePhoneProxy.Models;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string? ProxyHost { get; set; }
    public int ProxyPort { get; set; } = 8080;
    public string ProxyScheme { get; set; } = "http";
    public bool AutoDetectProxyHost { get; set; } = true;
    public string? OpenCodePath { get; set; }
    public string OpenCodeArguments { get; set; } = "";
    public bool ProtectDefaultRoute { get; set; } = true;
    public bool TestProxyBeforeLaunch { get; set; } = true;
    public bool CloseExistingOpenCode { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool FirstRunCompleted { get; set; }
    public string? LastAdapterId { get; set; }
    public RouteSnapshot? PendingRoute { get; set; }
}

public sealed class RouteSnapshot
{
    public int InterfaceIndex { get; set; }
    public string AdapterName { get; set; } = "";
    public int? V4Metric { get; set; }
    public bool V4Automatic { get; set; }
    public int? V6Metric { get; set; }
    public bool V6Automatic { get; set; }
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.Now;
}

public sealed record ProxyConfig(string Host, int Port, string Scheme)
{
    private static readonly HashSet<string> AllowedSchemes =
        new(StringComparer.OrdinalIgnoreCase) { "http", "https", "socks5", "socks5h" };

    public string Url => $"{Scheme}://{Host}:{Port}";

    public static ProxyConfig FromSettings(AppSettings settings) =>
        new((settings.ProxyHost ?? "").Trim(), settings.ProxyPort, NormalizeScheme(settings.ProxyScheme));

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            return "Enter the phone's proxy address.";
        }

        if (Host.Contains("://", StringComparison.Ordinal) ||
            Host.Contains('/', StringComparison.Ordinal) ||
            Host.Contains(' ', StringComparison.Ordinal))
        {
            return "Enter only the host or IP address, without http:// or a path.";
        }

        if (!IPAddress.TryParse(Host, out _) && Uri.CheckHostName(Host) == UriHostNameType.Unknown)
        {
            return "That does not look like a valid host name or IP address.";
        }

        if (Port is < 1 or > 65535)
        {
            return "Port must be between 1 and 65535.";
        }

        if (!AllowedSchemes.Contains(Scheme))
        {
            return "Choose HTTP, HTTPS, SOCKS5, or SOCKS5H.";
        }

        return null;
    }

    public static string NormalizeScheme(string? scheme) =>
        (scheme ?? "http").Trim().ToLowerInvariant() switch
        {
            "https" => "https",
            "socks5" => "socks5",
            "socks5h" => "socks5h",
            _ => "http"
        };
}

public sealed record PhoneLink(
    string AdapterId,
    string AdapterName,
    string Description,
    int InterfaceIndex,
    string LocalAddress,
    string GatewayAddress,
    int Score,
    bool LooksLikeUsbTether);

public sealed record OpenCodeCandidate(string Path, string Version, string Source)
{
    public override string ToString()
    {
        var name = System.IO.Path.GetFileName(Path);
        var version = string.IsNullOrWhiteSpace(Version) ? "" : $" - {Version}";
        return $"{name}{version}  ({Path})";
    }
}

public sealed class ProxyTestResult
{
    public bool TcpReachable { get; init; }
    public bool Success { get; init; }
    public string? PublicIp { get; init; }
    public string? DirectIp { get; init; }
    public long LatencyMs { get; init; }
    public string? Error { get; init; }

    public bool SameEgress =>
        Success &&
        !string.IsNullOrWhiteSpace(PublicIp) &&
        !string.IsNullOrWhiteSpace(DirectIp) &&
        string.Equals(PublicIp, DirectIp, StringComparison.OrdinalIgnoreCase);

    public string Summary
    {
        get
        {
            if (!TcpReachable)
            {
                return Error ?? "The phone proxy is not reachable.";
            }

            if (!Success)
            {
                return Error ?? "The proxy did not answer the test request.";
            }

            if (SameEgress)
            {
                return $"Proxy works, but its public IP matches this PC ({PublicIp}). " +
                       "The phone may be on Wi-Fi or the proxy may not be active.";
            }

            return $"Connected. Phone egress IP: {PublicIp} ({LatencyMs} ms).";
        }
    }
}

public sealed record LaunchResult(bool Started, int? ProcessId, string? Error);

public enum RouteChangeResult
{
    Applied,
    Declined,
    Failed
}
