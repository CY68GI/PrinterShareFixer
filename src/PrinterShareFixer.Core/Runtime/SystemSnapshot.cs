using Microsoft.Win32;
using PrinterShareFixer.Core.Models;

namespace PrinterShareFixer.Core.Runtime;

public sealed record ServiceInfo(string Name, string DisplayName, string State, string StartMode);

public sealed record NetworkProfileInfo(string Name, string InterfaceAlias, string NetworkCategory, string Ipv4Connectivity);

public sealed record PrinterInfo(string Name, string ShareName, string DriverName);

public sealed record ShareInfo(string Name, string Path);

public sealed record NetbiosInfo(string Description, string Addresses, int? NetbiosOption);

public sealed record FirewallRuleInfo(string DisplayName, string Enabled);

/// <summary>当前机器的打印机共享相关状态快照。</summary>
public sealed class SystemSnapshot
{
    public string ProductName { get; init; } = "未知";

    public string DisplayVersion { get; init; } = string.Empty;

    public int Build { get; init; }

    public int Ubr { get; init; }

    public string Edition { get; init; } = string.Empty;

    public bool IsElevated { get; init; }

    public bool IsWindows11 => Build >= 22000;

    public bool IsWindows11Build26100OrLater => Build >= 26100;

    public string OsSummary => $"{ProductName} {DisplayVersion}（内部版本 {Build}.{Ubr}）".Trim();

    public IReadOnlyList<ServiceInfo> Services { get; init; } = [];

    public IReadOnlyList<NetworkProfileInfo> Networks { get; init; } = [];

    public IReadOnlyList<PrinterInfo> SharedPrinters { get; init; } = [];

    /// <summary>本机已连接的远程（共享）打印机。</summary>
    public IReadOnlyList<PrinterInfo> RemotePrinters { get; init; } = [];

    public IReadOnlyList<ShareInfo> Shares { get; init; } = [];

    public IReadOnlyList<NetbiosInfo> Netbios { get; init; } = [];

    public IReadOnlyList<FirewallRuleInfo> CustomFirewallRules { get; init; } = [];

    public int FileAndPrinterRulesEnabled { get; init; }

    public int FileAndPrinterRulesTotal { get; init; }

    public int DiscoveryRulesEnabled { get; init; }

    public int DiscoveryRulesTotal { get; init; }

    /// <summary>防火墙规则查询失败的原因（通常是缺少管理员权限）。</summary>
    public string? FirewallQueryError { get; init; }

    public bool? ClientInsecureGuestLogons { get; init; }

    public bool? ClientRequireSecuritySignature { get; init; }

    public bool? ServerRequireSecuritySignature { get; init; }

    public bool? ServerEnableSmb1 { get; init; }

    public int? PrintRpcPrivacyEnabled { get; init; }

    public int? RestrictDriverInstallationToAdministrators { get; init; }

    public int? ProtectedPrintMode { get; init; }

    public int? PrintRpcUseNamedPipe { get; init; }

    public int? LimitBlankPasswordUse { get; init; }

    public int? EveryoneIncludesAnonymous { get; init; }

    public int? LocalAccountTokenFilterPolicy { get; init; }

    public int? ServerRequireSecuritySignatureReg { get; init; }

    public int? WorkstationRequireSecuritySignatureReg { get; init; }

    public static SystemSnapshot Load(ILogSink log)
    {
        var registry = new RegistryTools(log);
        var os = ReadOs();
        var services = QueryServices(log);
        var firewall = QueryFirewall(log);
        var smb = QuerySmb(log);

        return new SystemSnapshot
        {
            ProductName = os.ProductName,
            DisplayVersion = os.DisplayVersion,
            Build = os.Build,
            Ubr = os.Ubr,
            Edition = os.Edition,
            IsElevated = CheckElevated(),
            Services = services,
            Networks = QueryNetworks(log),
            SharedPrinters = QueryPrinters(log),
            RemotePrinters = QueryRemotePrinters(log),
            Shares = QueryShares(log),
            Netbios = QueryNetbios(log),
            CustomFirewallRules = firewall.CustomRules,
            FileAndPrinterRulesEnabled = firewall.FileAndPrinterEnabled,
            FileAndPrinterRulesTotal = firewall.FileAndPrinterTotal,
            DiscoveryRulesEnabled = firewall.DiscoveryEnabled,
            DiscoveryRulesTotal = firewall.DiscoveryTotal,
            FirewallQueryError = firewall.QueryError,
            ClientInsecureGuestLogons = smb.ClientInsecureGuestLogons,
            ClientRequireSecuritySignature = smb.ClientRequireSecuritySignature,
            ServerRequireSecuritySignature = smb.ServerRequireSecuritySignature,
            ServerEnableSmb1 = smb.ServerEnableSmb1,
            PrintRpcPrivacyEnabled = registry.GetDword(RegistryTools.Print, "RpcAuthnLevelPrivacyEnabled"),
            RestrictDriverInstallationToAdministrators = registry.GetDword(RegistryTools.PointAndPrintPolicy, "RestrictDriverInstallationToAdministrators"),
            ProtectedPrintMode = registry.GetDword(RegistryTools.WppPolicy, "WindowsProtectedPrintMode"),
            PrintRpcUseNamedPipe = registry.GetDword(RegistryTools.PrintRpcPolicy, "RpcUseNamedPipeProtocol"),
            LimitBlankPasswordUse = registry.GetDword(RegistryTools.Lsa, "LimitBlankPasswordUse"),
            EveryoneIncludesAnonymous = registry.GetDword(RegistryTools.Lsa, "everyoneincludesanonymous"),
            LocalAccountTokenFilterPolicy = registry.GetDword(RegistryTools.PoliciesSystem, "LocalAccountTokenFilterPolicy"),
            ServerRequireSecuritySignatureReg = registry.GetDword(RegistryTools.LanmanServer, "RequireSecuritySignature"),
            WorkstationRequireSecuritySignatureReg = registry.GetDword(RegistryTools.LanmanWorkstation, "RequireSecuritySignature"),
        };
    }

    /// <summary>按角色生成检测项：服务端关注“我共享了什么”，客户端关注“我连上了谁”。</summary>
    public IReadOnlyList<DetectionItem> ToDetectionItems(RepairRole role)
    {
        var items = ToDetectionItems().ToList();
        if (role != RepairRole.Consumer)
        {
            return items;
        }

        items.RemoveAll(item =>
            item.Title is "已共享的打印机" or "已共享的文件夹" or "网络路径提示");

        var unc = new string('\\', 2);
        items.Add(new DetectionItem(
            "连接",
            "已连接的共享打印机",
            RemotePrinters.Count > 0 ? DetectionStatus.Ok : DetectionStatus.Warning,
            RemotePrinters.Count > 0
                ? string.Join("；", RemotePrinters.Select(p => $"{p.Name} ← {p.ShareName}（驱动：{p.DriverName}）"))
                : $"本机还没有连接任何共享打印机。可在资源管理器地址栏输入 {unc}<对方电脑名或IP> 找到打印机后连接。"));

        items.Add(new DetectionItem(
            "连接",
            "本机共享状态",
            DetectionStatus.Unknown,
            SharedPrinters.Count > 0
                ? $"提示：本机也共享了 {SharedPrinters.Count} 台打印机；如果这台电脑不是提供打印机的那台，可以忽略这一行。"
                : "本机没有共享任何打印机，符合“本机没有打印机”的角色。"));

        items.Add(new DetectionItem(
            "访问方式",
            "连接提示",
            DetectionStatus.Unknown,
            $"在资源管理器地址栏输入 {unc}<接打印机那台电脑的名称或IP> 即可看到共享打印机；提示输入账号时，用户名写成“对方电脑名{new string('\\', 1)}对方账号”。"));

        return items;
    }

    public IReadOnlyList<DetectionItem> ToDetectionItems()
    {
        var items = new List<DetectionItem>
        {
            new("系统", "操作系统", DetectionStatus.Unknown, OsSummary + $"　[{Edition}]"),
            IsElevated
                ? new DetectionItem("系统", "管理员权限", DetectionStatus.Ok, "已以管理员身份运行，可以修改服务、防火墙和注册表。")
                : new DetectionItem("系统", "管理员权限", DetectionStatus.Problem, "未获得管理员权限，无法执行修复。请右键选择“以管理员身份运行”。"),
        };

        var keyServices = Services
            .Where(s => s.Name is "LanmanServer" or "LanmanWorkstation" or "Spooler" or "FDResPub" or "fdPHost" or "RpcSs" or "mpssvc")
            .ToList();
        var stopped = keyServices.Where(s => !string.Equals(s.State, "Running", StringComparison.OrdinalIgnoreCase)).ToList();
        items.Add(new DetectionItem(
            "服务",
            "关键服务运行状态",
            keyServices.Count == 0 ? DetectionStatus.Unknown : stopped.Count == 0 ? DetectionStatus.Ok : DetectionStatus.Problem,
            keyServices.Count == 0
                ? "无法读取服务状态。"
                : string.Join("；", keyServices.Select(s => $"{s.Name}={s.State}/{s.StartMode}"))));

        var publicNetworks = Networks.Where(n => !string.Equals(n.NetworkCategory, "Private", StringComparison.OrdinalIgnoreCase)).ToList();
        items.Add(new DetectionItem(
            "网络",
            "网络位置类型",
            Networks.Count == 0
                ? DetectionStatus.Unknown
                : publicNetworks.Count == 0 ? DetectionStatus.Ok : DetectionStatus.Warning,
            Networks.Count == 0
                ? "未检测到活动网络连接。"
                : string.Join("；", Networks.Select(n => $"{n.Name}[{n.InterfaceAlias}]={n.NetworkCategory}/{n.Ipv4Connectivity}"))));

        items.Add(new DetectionItem(
            "防火墙",
            "文件和打印机共享规则",
            FirewallQueryError is not null
                ? DetectionStatus.Unknown
                : FileAndPrinterRulesTotal == 0
                ? DetectionStatus.Unknown
                : FileAndPrinterRulesEnabled > 0 ? DetectionStatus.Ok : DetectionStatus.Problem,
            FirewallQueryError is not null
                ? $"无法读取防火墙规则（{FirewallQueryError}）。修复时程序会以管理员权限重新读取并放行。"
                : $"已启用 {FileAndPrinterRulesEnabled} / {FileAndPrinterRulesTotal} 条内置规则。"));

        items.Add(new DetectionItem(
            "防火墙",
            "网络发现规则",
            FirewallQueryError is not null
                ? DetectionStatus.Unknown
                : DiscoveryRulesTotal == 0
                ? DetectionStatus.Unknown
                : DiscoveryRulesEnabled > 0 ? DetectionStatus.Ok : DetectionStatus.Problem,
            FirewallQueryError is not null
                ? $"无法读取防火墙规则（{FirewallQueryError}）。"
                : $"已启用 {DiscoveryRulesEnabled} / {DiscoveryRulesTotal} 条内置规则。"));

        items.Add(new DetectionItem(
            "防火墙",
            "本工具放行规则",
            CustomFirewallRules.Count == 0
                ? DetectionStatus.Warning
                : CustomFirewallRules.All(r => string.Equals(r.Enabled, "True", StringComparison.OrdinalIgnoreCase))
                    ? DetectionStatus.Ok
                    : DetectionStatus.Warning,
            CustomFirewallRules.Count == 0
                ? "尚未创建 SMB / RPC / NetBIOS / WSD 放行规则。"
                : string.Join("；", CustomFirewallRules.Select(r => $"{r.DisplayName}={r.Enabled}"))));

        items.Add(new DetectionItem(
            "SMB 客户端",
            "允许不安全的来宾登录",
            ClientInsecureGuestLogons switch
            {
                true => DetectionStatus.Ok,
                false => DetectionStatus.Warning,
                _ => DetectionStatus.Unknown,
            },
            ClientInsecureGuestLogons switch
            {
                true => "已允许，可访问未启用密码保护共享的设备。",
                false => "已禁用，访问无密码共享时会提示“不允许不安全的来宾登录”。",
                _ => "无法读取（通常需要管理员权限）。",
            }));

        items.Add(new DetectionItem(
            "SMB",
            "SMB 签名强制",
            ClientRequireSecuritySignature == false && ServerRequireSecuritySignature == false
                ? DetectionStatus.Ok
                : DetectionStatus.Warning,
            $"客户端 RequireSecuritySignature={Format(ClientRequireSecuritySignature)}；服务端 RequireSecuritySignature={Format(ServerRequireSecuritySignature)}。"));

        items.Add(new DetectionItem(
            "打印",
            "打印 RPC 隐私认证（0x0000011b）",
            PrintRpcPrivacyEnabled switch
            {
                0 => DetectionStatus.Ok,
                1 => DetectionStatus.Problem,
                _ => DetectionStatus.Warning,
            },
            PrintRpcPrivacyEnabled switch
            {
                0 => "已关闭，跨版本连接共享打印机不会再报 0x0000011b。",
                1 => "仍为开启状态（默认值），连接共享打印机时可能出现 0x0000011b。",
                _ => "注册表中未显式设置该值（部分系统默认按开启处理），修复时会写入 0。",
            }));

        items.Add(new DetectionItem(
            "打印",
            "非管理员安装共享打印机驱动",
            RestrictDriverInstallationToAdministrators switch
            {
                0 => DetectionStatus.Ok,
                1 => DetectionStatus.Warning,
                _ => DetectionStatus.Unknown,
            },
            RestrictDriverInstallationToAdministrators switch
            {
                0 => "已允许，普通用户连接共享打印机时不会卡在驱动安装。",
                1 => "仍限制为管理员，普通用户连接共享打印机可能报 0x00000740。",
                _ => "未配置（系统默认按限制处理）。",
            }));

        if (IsWindows11Build26100OrLater)
        {
            items.Add(new DetectionItem(
                "打印",
                "受保护的打印模式 / RPC 传输",
                ProtectedPrintMode == 1 ? DetectionStatus.Problem
                    : PrintRpcUseNamedPipe == 1 ? DetectionStatus.Warning
                    : DetectionStatus.Ok,
                $"WindowsProtectedPrintMode={Format(ProtectedPrintMode)}；RpcUseNamedPipeProtocol={Format(PrintRpcUseNamedPipe)}。"));
        }

        var netbiosDisabled = Netbios.Where(n => n.NetbiosOption == 2).ToList();
        items.Add(new DetectionItem(
            "网络",
            "NetBIOS over TCP/IP",
            Netbios.Count == 0
                ? DetectionStatus.Unknown
                : netbiosDisabled.Count == 0 ? DetectionStatus.Ok : DetectionStatus.Warning,
            Netbios.Count == 0
                ? "未检测到已启用 IP 的网卡。"
                : string.Join("；", Netbios.Select(n => $"{n.Description}={DescribeNetbios(n.NetbiosOption)}"))));

        items.Add(new DetectionItem(
            "共享",
            "已共享的打印机",
            SharedPrinters.Count > 0 ? DetectionStatus.Ok : DetectionStatus.Warning,
            SharedPrinters.Count > 0
                ? string.Join("；", SharedPrinters.Select(p => $"{p.Name} → \\\\{Environment.MachineName}\\{p.ShareName}（驱动：{p.DriverName}）"))
                : "本机当前没有共享任何打印机。请先在“打印机属性 → 共享”中勾选共享。"));

        items.Add(new DetectionItem(
            "共享",
            "已共享的文件夹",
            Shares.Count > 0 ? DetectionStatus.Ok : DetectionStatus.Unknown,
            Shares.Count > 0
                ? string.Join("；", Shares.Select(s => $"{s.Name} → {s.Path}"))
                : "未读取到共享文件夹。"));

        items.Add(new DetectionItem(
            "访问方式",
            "网络路径提示",
            DetectionStatus.Unknown,
            $"其他电脑可通过 \\\\{Environment.MachineName}\\<共享名> 访问本机共享；登录时用户名建议写成 {Environment.MachineName}\\<本机账号>。"));

        return items;
    }

    private static string Format(int? value) => value?.ToString() ?? "未设置";

    private static string Format(bool? value) => value?.ToString() ?? "未设置";

    private static string DescribeNetbios(int? option) => option switch
    {
        1 => "已启用",
        2 => "已禁用",
        0 => "由 DHCP 决定",
        _ => "未知",
    };

    private static bool CheckElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static (string ProductName, string DisplayVersion, int Build, int Ubr, string Edition) ReadOs()
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var product = key?.GetValue("ProductName")?.ToString() ?? "未知";
            var display = key?.GetValue("DisplayVersion")?.ToString()
                          ?? key?.GetValue("ReleaseId")?.ToString()
                          ?? string.Empty;
            var buildText = key?.GetValue("CurrentBuildNumber")?.ToString() ?? "0";
            var ubrText = key?.GetValue("UBR")?.ToString() ?? "0";
            var edition = key?.GetValue("EditionID")?.ToString() ?? string.Empty;
            _ = int.TryParse(buildText, out var build);
            _ = int.TryParse(ubrText, out var ubr);
            return (product, display, build, ubr, edition);
        }
        catch
        {
            return ("未知", string.Empty, 0, 0, string.Empty);
        }
    }

    private static IReadOnlyList<ServiceInfo> QueryServices(ILogSink log)
    {
        const string script = """
            Get-CimInstance -ClassName Win32_Service -ErrorAction Stop |
                Where-Object { @('LanmanServer','LanmanWorkstation','Spooler','RpcSs','RpcEptMapper','DcomLaunch','RpcLocator','Dnscache','FDResPub','fdPHost','SSDPSRV','upnphost','NlaSvc','netprofm','Netman','mpssvc','Browser') -contains $_.Name } |
                Select-Object Name,DisplayName,State,StartMode |
                ConvertTo-Json -Compress
            """;

        var json = RunScript(log, script, "读取服务状态");
        return JsonHelpers.ParseObjects(json)
            .Select(o => new ServiceInfo(
                JsonHelpers.String(o, "Name"),
                JsonHelpers.String(o, "DisplayName"),
                JsonHelpers.String(o, "State"),
                JsonHelpers.String(o, "StartMode")))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<NetworkProfileInfo> QueryNetworks(ILogSink log)
    {
        const string script = """
            Get-NetConnectionProfile -ErrorAction Stop |
                Select-Object Name,InterfaceAlias,
                    @{n='NetworkCategory';e={ [string]$_.NetworkCategory }},
                    @{n='IPv4Connectivity';e={ [string]$_.IPv4Connectivity }} |
                ConvertTo-Json -Compress
            """;

        var json = RunScript(log, script, "读取网络配置");
        return JsonHelpers.ParseObjects(json)
            .Select(o => new NetworkProfileInfo(
                JsonHelpers.String(o, "Name"),
                JsonHelpers.String(o, "InterfaceAlias"),
                JsonHelpers.String(o, "NetworkCategory"),
                JsonHelpers.String(o, "IPv4Connectivity")))
            .ToList();
    }

    private static (int FileAndPrinterEnabled, int FileAndPrinterTotal, int DiscoveryEnabled, int DiscoveryTotal, IReadOnlyList<FirewallRuleInfo> CustomRules, string? QueryError) QueryFirewall(ILogSink log)
    {
        const string script = """
            $result = [ordered]@{
                fileAndPrinterEnabled = 0
                fileAndPrinterTotal   = 0
                discoveryEnabled      = 0
                discoveryTotal        = 0
                custom                = @()
                queryError            = $null
            }
            # 内置规则组的 Group 属性既可能是资源字符串，也可能是本地化名称，两种都尝试。
            function Get-FirewallGroupRules {
                param([string]$IndirectId, [string[]]$Patterns)
                $rules = @(Get-NetFirewallRule -Group $IndirectId -ErrorAction SilentlyContinue)
                if ($rules.Count -gt 0) { return $rules }
                return @(Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object {
                    $group = [string]$_.Group
                    if (-not $group) { return $false }
                    if ($group -eq $IndirectId) { return $true }
                    foreach ($pattern in $Patterns) { if ($group -like $pattern) { return $true } }
                    return $false
                })
            }
            try {
                $all = @(Get-NetFirewallRule -ErrorAction Stop)
                $fp = @(Get-FirewallGroupRules -IndirectId '@FirewallAPI.dll,-32752' -Patterns @('*File and Printer Sharing*','*文件和打印机共享*'))
                $nd = @(Get-FirewallGroupRules -IndirectId '@FirewallAPI.dll,-32753' -Patterns @('*Network Discovery*','*网络发现*'))
                $result.fileAndPrinterEnabled = @($fp | Where-Object { $_.Enabled -eq 'True' }).Count
                $result.fileAndPrinterTotal = $fp.Count
                $result.discoveryEnabled = @($nd | Where-Object { $_.Enabled -eq 'True' }).Count
                $result.discoveryTotal = $nd.Count
                $result.custom = @($all | Where-Object { [string]$_.Group -eq 'PrinterShareFixer' } | Select-Object DisplayName,Enabled)
            } catch {
                $result.queryError = $_.Exception.Message
            }
            $result | ConvertTo-Json -Compress -Depth 4
            """;

        var json = RunScript(log, script, "读取防火墙规则");
        var root = JsonHelpers.ParseSingle(json);
        if (root is null)
        {
            return (0, 0, 0, 0, [], "无法执行防火墙查询");
        }

        var custom = JsonHelpers.Array(root.Value, "custom")
            .Select(o => new FirewallRuleInfo(
                JsonHelpers.String(o, "DisplayName"),
                JsonHelpers.String(o, "Enabled")))
            .ToList();

        var queryError = JsonHelpers.String(root.Value, "queryError");
        return (
            JsonHelpers.Int(root.Value, "fileAndPrinterEnabled") ?? 0,
            JsonHelpers.Int(root.Value, "fileAndPrinterTotal") ?? 0,
            JsonHelpers.Int(root.Value, "discoveryEnabled") ?? 0,
            JsonHelpers.Int(root.Value, "discoveryTotal") ?? 0,
            custom,
            string.IsNullOrWhiteSpace(queryError) ? null : queryError);
    }

    private static (bool? ClientInsecureGuestLogons, bool? ClientRequireSecuritySignature, bool? ServerRequireSecuritySignature, bool? ServerEnableSmb1) QuerySmb(ILogSink log)
    {
        const string script = """
            $client = $null
            $server = $null
            try { $client = Get-SmbClientConfiguration -ErrorAction Stop } catch { }
            try { $server = Get-SmbServerConfiguration -ErrorAction Stop } catch { }
            [ordered]@{
                clientInsecureGuestLogons      = if ($client) { [bool]$client.EnableInsecureGuestLogons } else { $null }
                clientRequireSecuritySignature = if ($client) { [bool]$client.RequireSecuritySignature } else { $null }
                serverRequireSecuritySignature = if ($server) { [bool]$server.RequireSecuritySignature } else { $null }
                serverEnableSmb1              = if ($server) { [bool]$server.EnableSMB1Protocol } else { $null }
            } | ConvertTo-Json -Compress
            """;

        var json = RunScript(log, script, "读取 SMB 配置");
        var root = JsonHelpers.ParseSingle(json);
        if (root is null)
        {
            return (null, null, null, null);
        }

        return (
            JsonHelpers.Bool(root.Value, "clientInsecureGuestLogons"),
            JsonHelpers.Bool(root.Value, "clientRequireSecuritySignature"),
            JsonHelpers.Bool(root.Value, "serverRequireSecuritySignature"),
            JsonHelpers.Bool(root.Value, "serverEnableSmb1"));
    }

    private static IReadOnlyList<NetbiosInfo> QueryNetbios(ILogSink log)
    {
        const string script = """
            Get-CimInstance -ClassName Win32_NetworkAdapterConfiguration -Filter 'IPEnabled=True' -ErrorAction Stop |
                Select-Object Description,@{n='Addresses';e={ ($_.IPAddress -join ',') }},TcpipNetbiosOptions |
                ConvertTo-Json -Compress
            """;

        var json = RunScript(log, script, "读取网卡 NetBIOS 设置");
        return JsonHelpers.ParseObjects(json)
            .Select(o => new NetbiosInfo(
                JsonHelpers.String(o, "Description"),
                JsonHelpers.String(o, "Addresses"),
                JsonHelpers.Int(o, "TcpipNetbiosOptions")))
            .ToList();
    }

    private static IReadOnlyList<PrinterInfo> QueryPrinters(ILogSink log)
    {
        const string script = """
            Get-Printer -ErrorAction SilentlyContinue |
                Where-Object { $_.Shared -eq $true } |
                Select-Object Name,ShareName,DriverName |
                ConvertTo-Json -Compress
            """;

        var json = RunScript(log, script, "读取已共享打印机");
        return JsonHelpers.ParseObjects(json)
            .Select(o => new PrinterInfo(
                JsonHelpers.String(o, "Name"),
                JsonHelpers.String(o, "ShareName"),
                JsonHelpers.String(o, "DriverName")))
            .ToList();
    }

    /// <summary>本机通过网络连接的共享打印机（端口以 \\\\ 开头的那些）。</summary>
    private static IReadOnlyList<PrinterInfo> QueryRemotePrinters(ILogSink log)
    {
        const string script = """
            Get-Printer -ErrorAction SilentlyContinue |
                Where-Object { $_.PortName -like '\\*' } |
                Select-Object Name,@{n='ShareName';e={ $_.PortName }},DriverName |
                ConvertTo-Json -Compress
            """;

        var json = RunScript(log, script, "读取已连接的共享打印机");
        return JsonHelpers.ParseObjects(json)
            .Select(o => new PrinterInfo(
                JsonHelpers.String(o, "Name"),
                JsonHelpers.String(o, "ShareName"),
                JsonHelpers.String(o, "DriverName")))
            .ToList();
    }

    private static IReadOnlyList<ShareInfo> QueryShares(ILogSink log)
    {
        const string script = """
            Get-SmbShare -ErrorAction SilentlyContinue |
                Where-Object { -not $_.Name.EndsWith('$') } |
                Select-Object Name,Path |
                ConvertTo-Json -Compress
            """;

        var json = RunScript(log, script, "读取共享文件夹");
        return JsonHelpers.ParseObjects(json)
            .Select(o => new ShareInfo(
                JsonHelpers.String(o, "Name"),
                JsonHelpers.String(o, "Path")))
            .ToList();
    }

    internal static string RunScript(ILogSink log, string script, string friendlyName)
    {
        var runner = new PowerShellRunner(log);
        try
        {
            var result = runner.RunAsync(script, CancellationToken.None, friendlyName, 120_000)
                .GetAwaiter()
                .GetResult();
            if (!result.Succeeded)
            {
                log.Write($"  {friendlyName} 失败：{result.ErrorSummary}");
            }

            return result.StandardOutput;
        }
        catch (Exception ex)
        {
            log.Write($"  {friendlyName} 异常：{ex.Message}");
            return string.Empty;
        }
    }
}
