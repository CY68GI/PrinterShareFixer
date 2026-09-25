namespace PrinterShareFixer.Core.Runtime;

/// <summary>需要保证运行的服务定义。</summary>
public sealed record ServiceSpec(string Name, string DisplayName, string StartMode, bool Configurable = true)
{
    public override string ToString() => $"{DisplayName}({Name})";
}

public static class ServiceCatalog
{
    /// <summary>打印机共享、网络发现与防火墙相关的必备服务。</summary>
    public static readonly ServiceSpec[] CommonRequired =
    [
        new("LanmanServer", "Server（提供本机共享）", "Automatic"),
        new("LanmanWorkstation", "Workstation（访问他人共享）", "Automatic"),
        new("Spooler", "Print Spooler（打印后台处理程序）", "Automatic"),
        new("RpcSs", "Remote Procedure Call (RPC)", "Automatic", Configurable: false),
        new("RpcEptMapper", "RPC Endpoint Mapper", "Automatic", Configurable: false),
        new("DcomLaunch", "DCOM Server Process Launcher", "Automatic", Configurable: false),
        new("RpcLocator", "Remote Procedure Call (RPC) Locator", "Manual"),
        new("Dnscache", "DNS Client", "Automatic"),
        new("FDResPub", "Function Discovery Resource Publication", "Automatic"),
        new("fdPHost", "Function Discovery Provider Host", "Manual"),
        new("SSDPSRV", "SSDP Discovery", "Manual"),
        new("upnphost", "UPnP Device Host", "Manual"),
        new("NlaSvc", "Network Location Awareness", "Automatic"),
        new("netprofm", "Network List Service", "Manual"),
        new("Netman", "Network Connections", "Manual"),
        new("mpssvc", "Windows Defender Firewall", "Automatic"),
    ];

    /// <summary>Windows 10 上仍存在、Windows 11 已移除的遗留服务。</summary>
    public static readonly ServiceSpec[] Windows10Extra =
    [
        new("Browser", "Computer Browser（遗留网络浏览）", "Automatic"),
    ];
}
