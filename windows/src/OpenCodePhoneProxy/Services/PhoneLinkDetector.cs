using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using OpenCodePhoneProxy.Models;

namespace OpenCodePhoneProxy.Services;

public static class PhoneLinkDetector
{
    private static readonly Regex UsbTetherText = new(
        @"(rndis|remote\s+ndis|android|usb|tether)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VirtualText = new(
        @"(virtualbox|vmware|hyper-?v|vethernet|wsl|tailscale|wireguard|openvpn|tap-|tun\d|loopback|bluetooth|wi-fi\s+direct|npcap|zerotier|docker|veth|br-)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<PhoneLink> Detect()
    {
        var found = new List<PhoneLink>();

        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch
        {
            return found;
        }

        foreach (var nic in interfaces)
        {
            try
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var text = $"{nic.Name} {nic.Description}";
                if (VirtualText.IsMatch(text))
                {
                    continue;
                }

                var properties = nic.GetIPProperties();
                var ipv4 = properties.GetIPv4Properties();
                if (ipv4 is null)
                {
                    continue;
                }

                var local = properties.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    ?.Address
                    .ToString();
                if (string.IsNullOrWhiteSpace(local))
                {
                    continue;
                }

                var gateway = properties.GatewayAddresses
                    .FirstOrDefault(g =>
                        g.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !g.Address.Equals(IPAddress.Any))
                    ?.Address
                    .ToString();
                if (string.IsNullOrWhiteSpace(gateway))
                {
                    continue;
                }

                var looksLikeUsb = UsbTetherText.IsMatch(text);
                var score = Score(nic, gateway, looksLikeUsb);
                found.Add(new PhoneLink(
                    nic.Id,
                    nic.Name,
                    nic.Description,
                    ipv4.Index,
                    local,
                    gateway,
                    score,
                    looksLikeUsb));
            }
            catch
            {
                // An adapter can disappear or reject queries while we enumerate.
            }
        }

        return found
            .OrderByDescending(link => link.Score)
            .ThenBy(link => link.InterfaceIndex)
            .ToList();
    }

    public static PhoneLink? Best()
    {
        var links = Detect();
        return links.FirstOrDefault(link => link.LooksLikeUsbTether) ??
               links.FirstOrDefault(link => IsCommonTetherGateway(link.GatewayAddress));
    }

    /// <summary>
    /// Local address of a real uplink (Wi-Fi or Ethernet) that is not the phone.
    /// The direct-IP test binds to this address so it does not follow the USB
    /// tether when that adapter happens to be the Windows default route.
    /// </summary>
    public static string? DirectSourceAddress(string? proxyHost = null, int? excludedInterfaceIndex = null)
    {
        foreach (var link in Detect())
        {
            if (excludedInterfaceIndex is not null && link.InterfaceIndex == excludedInterfaceIndex)
            {
                continue;
            }

            if (link.LooksLikeUsbTether)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(proxyHost) &&
                string.Equals(link.GatewayAddress, proxyHost, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (link.LocalAddress.StartsWith("169.254.", StringComparison.Ordinal))
            {
                continue;
            }

            return link.LocalAddress;
        }

        return null;
    }

    public static IReadOnlyList<string> ProbeCommonHosts() =>
        new[] { "192.168.42.129", "192.168.43.1", "192.168.137.1" };

    private static int Score(NetworkInterface nic, string gateway, bool looksLikeUsb)
    {
        var score = 0;

        if (Regex.IsMatch(nic.Description, @"rndis|remote\s+ndis", RegexOptions.IgnoreCase))
        {
            score += 100;
        }
        else if (Regex.IsMatch(nic.Description, @"android", RegexOptions.IgnoreCase))
        {
            score += 80;
        }
        else if (looksLikeUsb)
        {
            score += 60;
        }

        if (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
        {
            score += 10;
        }

        if (IsCommonTetherGateway(gateway))
        {
            score += 5;
        }

        return score;
    }

    private static bool IsCommonTetherGateway(string gateway) =>
        gateway.StartsWith("192.168.42.", StringComparison.Ordinal) ||
        gateway.StartsWith("192.168.43.", StringComparison.Ordinal) ||
        gateway.StartsWith("192.168.137.", StringComparison.Ordinal);
}
