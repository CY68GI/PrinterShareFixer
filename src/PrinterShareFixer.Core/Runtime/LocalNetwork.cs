using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PrinterShareFixer.Core.Runtime;

/// <summary>一块网卡的地址信息。</summary>
public sealed record NetworkAddress(string AdapterName, string Description, string IPv4, string Mac, string SpeedText);

/// <summary>读取本机 IPv4 地址（纯 .NET 实现，不需要管理员权限，也不需要启动 PowerShell）。</summary>
public static class LocalNetwork
{
    public static IReadOnlyList<NetworkAddress> GetIpv4Addresses()
    {
        var result = new List<NetworkAddress>();

        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up ||
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var properties = adapter.GetIPProperties();
                var addresses = properties.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString())
                    .Where(ip => !ip.StartsWith("169.254.", StringComparison.Ordinal))
                    .ToList();

                if (addresses.Count == 0)
                {
                    continue;
                }

                var mac = adapter.GetPhysicalAddress().ToString();
                result.Add(new NetworkAddress(
                    adapter.Name,
                    adapter.Description,
                    string.Join(", ", addresses),
                    string.IsNullOrWhiteSpace(mac) ? "—" : FormatMac(mac),
                    FormatSpeed(adapter.Speed)));
            }
        }
        catch
        {
            // 读不到就返回已经拿到的部分
        }

        return result
            .OrderByDescending(a => a.AdapterName.Contains("以太网", StringComparison.Ordinal) ||
                                    a.AdapterName.Contains("Ethernet", StringComparison.OrdinalIgnoreCase))
            .ThenBy(a => a.AdapterName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>主 IPv4（用于界面顶部显示），没有就返回 null。</summary>
    public static string? PrimaryIPv4() => GetIpv4Addresses().FirstOrDefault()?.IPv4.Split(',')[0].Trim();

    private static string FormatMac(string raw)
    {
        if (raw.Length != 12)
        {
            return raw;
        }

        return string.Join('-', Enumerable.Range(0, 6).Select(i => raw.Substring(i * 2, 2)));
    }

    private static string FormatSpeed(long bitsPerSecond)
    {
        if (bitsPerSecond <= 0)
        {
            return "—";
        }

        return bitsPerSecond >= 1_000_000_000
            ? $"{bitsPerSecond / 1_000_000_000d:0.#} Gbps"
            : $"{bitsPerSecond / 1_000_000d:0.#} Mbps";
    }
}
