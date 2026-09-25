using System.Text;
using PrinterShareFixer.Core.Models;
using PrinterShareFixer.Core.Runtime;

namespace PrinterShareFixer.Core.Profiles;

/// <summary>Windows 10 / Windows 11 共用的修复步骤。</summary>
internal static class CommonSteps
{
    public static RepairStep Elevation()
    {
        return new RepairStep
        {
            Id = "env.elevation",
            Title = "检查管理员权限与系统版本",
            Description = "修改服务、防火墙与 HKLM 注册表都需要管理员权限。",
            RequiresElevation = false,
            AbortOnFailure = true,
            Commands = ["whoami /groups"],
            Handler = (context, _) =>
            {
                if (!context.IsElevated)
                {
                    return Task.FromResult(StepResult.Fail(
                        "当前进程不是管理员，修复无法继续。请关闭本程序，右键选择“以管理员身份运行”后重试。"));
                }

                var snapshot = SystemSnapshot.Load(context.Log);
                return Task.FromResult(StepResult.Ok(
                    $"已获得管理员权限，目标系统：{snapshot.OsSummary}",
                    $"检测到活动网络：{snapshot.Networks.Count} 个",
                    $"本机已共享打印机：{snapshot.SharedPrinters.Count} 台"));
            },
        };
    }

    public static RepairStep RegistryBackup()
    {
        return new RepairStep
        {
            Id = "backup.registry",
            Title = "备份相关注册表项",
            Description = "把将要修改的注册表分支导出到程序数据目录，方便随时回滚。",
            OptionKey = "backup",
            Commands = ["reg export HKLM\\SYSTEM\\CurrentControlSet\\Control\\Lsa <备份目录>"],
            Handler = async (context, ct) =>
            {
                var directory = AppPaths.EnsureBackupDirectory(DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                context.BackupDirectory = directory;
                context.Log.Write($"备份目录：{directory}");

                var details = new List<string>();
                var skipped = 0;
                foreach (var key in RegistryTools.BackupKeys)
                {
                    ct.ThrowIfCancellationRequested();
                    var export = await Task.Run(() => context.Registry.TryExportKey(key, directory), ct);
                    if (export.Ok)
                    {
                        details.Add($"已导出 HKLM\\{key}");
                    }
                    else
                    {
                        skipped++;
                        details.Add($"跳过 HKLM\\{key}（{export.Message}）");
                    }
                }

                var summary = $"备份完成：{details.Count - skipped} 项导出成功，{skipped} 项不存在或跳过";
                return StepResult.Ok(summary, [.. details]);
            },
        };
    }

    public static RepairStep Services(IReadOnlyList<ServiceSpec> specs, string title)
    {
        var serviceList = string.Join("、", specs.Select(s => s.Name));
        return new RepairStep
        {
            Id = "services.core",
            Title = title,
            Description = $"确保以下服务已启动并按需要自动启动：{serviceList}。",
            Commands =
            [
                "Set-Service -Name LanmanServer -StartupType Automatic",
                "Set-Service -Name Spooler -StartupType Automatic",
                "Set-Service -Name FDResPub -StartupType Automatic",
                "Start-Service -Name LanmanServer, Spooler, FDResPub",
            ],
            Handler = async (context, ct) =>
            {
                var script = BuildServiceScript(specs);
                var result = await context.PowerShell.RunAsync(script, ct, $"配置 {specs.Count} 项服务");
                var lines = StatusScript.Parse(result);
                string[] critical = ["LanmanServer", "LanmanWorkstation", "Spooler", "RpcSs"];
                return StatusScript.Summarize("服务配置完成", lines, critical);
            },
        };
    }

    public static RepairStep NetworkProfile()
    {
        return new RepairStep
        {
            Id = "net.profile",
            Title = "把网络位置切换为“专用网络”",
            Description = "“公用网络”会阻止共享与网络发现，只有专用网络才会开放文件与打印机共享。",
            Commands = ["Get-NetConnectionProfile | Set-NetConnectionProfile -NetworkCategory Private"],
            Handler = async (context, ct) =>
            {
                const string script = """
                    $out = New-Object System.Collections.Generic.List[string]
                    $profiles = @(Get-NetConnectionProfile -ErrorAction Stop)
                    foreach ($p in $profiles) {
                        if ($p.NetworkCategory -eq 'Private') {
                            $out.Add("OK|$($p.Name)|已经是专用网络")
                            continue
                        }
                        try {
                            Set-NetConnectionProfile -InterfaceIndex $p.InterfaceIndex -NetworkCategory Private -ErrorAction Stop
                            $out.Add("FIXED|$($p.Name)|$($p.NetworkCategory) -> Private")
                        } catch {
                            $out.Add("ERR|$($p.Name)|$($_.Exception.Message)")
                        }
                    }
                    if ($profiles.Count -eq 0) { $out.Add("MISSING|活动网络|没有检测到活动网络连接") }
                    $out -join "`n"
                    """;

                var result = await context.PowerShell.RunAsync(script, ct, "设置网络位置为专用网络");
                return StatusScript.Summarize("网络位置检查完成", StatusScript.Parse(result));
            },
        };
    }

    public static RepairStep Netbios()
    {
        return new RepairStep
        {
            Id = "net.netbios",
            Title = "启用 NetBIOS over TCP/IP",
            Description = "部分老设备与打印机只支持通过 NetBIOS 名称发现，关闭后会找不到共享。",
            Commands = ["# 对所有已启用 IP 的网卡调用 $nic.SetTcpipNetbios(1)"],
            Handler = async (context, ct) =>
            {
                const string script = """
                    $out = New-Object System.Collections.Generic.List[string]
                    $nics = @(Get-CimInstance -ClassName Win32_NetworkAdapterConfiguration -Filter 'IPEnabled=True' -ErrorAction Stop)
                    if ($nics.Count -eq 0) { $out.Add("MISSING|网卡|没有检测到已启用 IP 的网卡") }
                    foreach ($nic in $nics) {
                        if ($nic.TcpipNetbiosOptions -eq 1) {
                            $out.Add("OK|$($nic.Description)|已启用")
                            continue
                        }
                        try {
                            $rc = $nic.SetTcpipNetbios(1)
                            if ($rc.ReturnValue -eq 0) {
                                $out.Add("FIXED|$($nic.Description)|已启用 NetBIOS over TCP/IP")
                            } else {
                                $out.Add("ERR|$($nic.Description)|返回代码 $($rc.ReturnValue)")
                            }
                        } catch {
                            $out.Add("ERR|$($nic.Description)|$($_.Exception.Message)")
                        }
                    }
                    $out -join "`n"
                    """;

                var result = await context.PowerShell.RunAsync(script, ct, "启用 NetBIOS over TCP/IP");
                return StatusScript.Summarize("NetBIOS 检查完成", StatusScript.Parse(result));
            },
        };
    }

    public static RepairStep Firewall(bool allowAnyRemoteAddress, bool allowRpcDynamicPorts)
    {
        return new RepairStep
        {
            Id = "fw.rules",
            Title = "放行防火墙（文件与打印机共享、网络发现、SMB / RPC / WSD）",
            Description = allowAnyRemoteAddress
                ? "启用系统内置共享规则，并创建 SMB、RPC、NetBIOS、WSD 入站规则（允许任意远程地址）。"
                : "启用系统内置共享规则，并创建 SMB、RPC、NetBIOS、WSD 入站规则（仅允许本地子网）。",
            Commands =
            [
                "Set-NetFirewallRule -Group '@FirewallAPI.dll,-32752' -Enabled True",
                "Set-NetFirewallRule -Group '@FirewallAPI.dll,-32753' -Enabled True",
                "New-NetFirewallRule -DisplayName PSF-SMB-In-TCP -Protocol TCP -LocalPort 445",
                "New-NetFirewallRule -DisplayName PSF-RPC-In-TCP -Protocol TCP -LocalPort 135",
                "New-NetFirewallRule -DisplayName PSF-WSDAPI-In-TCP -Protocol TCP -LocalPort 5357",
            ],
            Handler = async (context, ct) =>
            {
                var script = BuildFirewallScript(allowAnyRemoteAddress, allowRpcDynamicPorts);
                var result = await context.PowerShell.RunAsync(script, ct, "配置防火墙放行规则");
                string[] critical = ["文件和打印机共享", "网络发现", "PSF-SMB-In-TCP"];
                return StatusScript.Summarize("防火墙规则配置完成", StatusScript.Parse(result), critical);
            },
        };
    }

    public static RepairStep GuestAccess()
    {
        return new RepairStep
        {
            Id = "lsa.guest",
            Title = "关闭“密码保护的共享”，允许来宾访问",
            Description = "等价于“网络和共享中心 → 所有网络 → 关闭密码保护的共享”，并放行本地账号的远程身份验证。",
            OptionKey = "passwordless",
            Commands =
            [
                "reg add HKLM\\SYSTEM\\CurrentControlSet\\Control\\Lsa /v LimitBlankPasswordUse /t REG_DWORD /d 0 /f",
                "reg add HKLM\\SYSTEM\\CurrentControlSet\\Control\\Lsa /v everyoneincludesanonymous /t REG_DWORD /d 1 /f",
                "reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v LocalAccountTokenFilterPolicy /t REG_DWORD /d 1 /f",
            ],
            Handler = (context, _) =>
            {
                var changes = new List<string>();
                void Set(string key, string name, int value)
                {
                    var change = context.Registry.SetDword(key, name, value);
                    if (change is not null)
                    {
                        changes.Add(change);
                    }
                }

                Set(RegistryTools.Lsa, "LimitBlankPasswordUse", 0);
                Set(RegistryTools.Lsa, "everyoneincludesanonymous", 1);
                Set(RegistryTools.Lsa, "restrictanonymous", 0);
                Set(RegistryTools.PoliciesSystem, "LocalAccountTokenFilterPolicy", 1);

                var lmCompatibility = context.Registry.GetDword(RegistryTools.Lsa, "LmCompatibilityLevel");
                if (lmCompatibility >= 5)
                {
                    Set(RegistryTools.Lsa, "LmCompatibilityLevel", 3);
                }

                var message = changes.Count == 0
                    ? "来宾访问相关策略已经符合要求，无需修改。"
                    : $"已调整 {changes.Count} 项来宾访问策略。";

                return Task.FromResult(StepResult.Ok(message, [.. changes]));
            },
        };
    }

    public static RepairStep SmbCompatibility()
    {
        return new RepairStep
        {
            Id = "smb.client",
            Title = "SMB 兼容性设置（来宾登录 / 取消签名强制）",
            Description = "解决“不允许不安全的来宾登录”和“因为签名要求无法连接共享”两类常见报错。",
            OptionKey = "smb-signing",
            Commands =
            [
                "Set-SmbClientConfiguration -EnableInsecureGuestLogons $true -Force",
                "Set-SmbClientConfiguration -RequireSecuritySignature $false -Force",
                "Set-SmbServerConfiguration -RequireSecuritySignature $false -Force",
            ],
            Handler = async (context, ct) =>
            {
                const string script = """
                    $out = New-Object System.Collections.Generic.List[string]
                    try {
                        $client = Get-SmbClientConfiguration -ErrorAction Stop
                        if ($client.EnableInsecureGuestLogons) {
                            $out.Add("OK|客户端来宾登录|已允许")
                        } else {
                            Set-SmbClientConfiguration -EnableInsecureGuestLogons $true -Force -ErrorAction Stop
                            $out.Add("FIXED|客户端来宾登录|已允许不安全的来宾登录")
                        }
                        if ($client.RequireSecuritySignature) {
                            Set-SmbClientConfiguration -RequireSecuritySignature $false -Force -ErrorAction Stop
                            $out.Add("FIXED|客户端 SMB 签名|已取消强制要求")
                        } else {
                            $out.Add("OK|客户端 SMB 签名|未强制要求")
                        }
                    } catch {
                        $out.Add("ERR|SMB 客户端配置|$($_.Exception.Message)")
                    }
                    try {
                        $server = Get-SmbServerConfiguration -ErrorAction Stop
                        if ($server.RequireSecuritySignature) {
                            Set-SmbServerConfiguration -RequireSecuritySignature $false -Force -ErrorAction Stop
                            $out.Add("FIXED|服务端 SMB 签名|已取消强制要求")
                        } else {
                            $out.Add("OK|服务端 SMB 签名|未强制要求")
                        }
                    } catch {
                        $out.Add("ERR|SMB 服务端配置|$($_.Exception.Message)")
                    }
                    $out -join "`n"
                    """;

                var result = await context.PowerShell.RunAsync(script, ct, "配置 SMB 客户端与服务端兼容性");
                var lines = StatusScript.Parse(result).ToList();

                var extra = new List<string>();
                var serverReg = context.Registry.SetDword(RegistryTools.LanmanServer, "RequireSecuritySignature", 0);
                if (serverReg is not null)
                {
                    extra.Add(serverReg);
                }

                var workstationReg = context.Registry.SetDword(RegistryTools.LanmanWorkstation, "RequireSecuritySignature", 0);
                if (workstationReg is not null)
                {
                    extra.Add(workstationReg);
                }

                var summary = StatusScript.Summarize("SMB 兼容性设置完成", lines, ["SMB 客户端配置"]);
                return summary with { Details = [.. summary.Details ?? [], .. extra] };
            },
        };
    }

    public static RepairStep PrintRpcPrivacy()
    {
        return new RepairStep
        {
            Id = "print.rpc",
            Title = "关闭打印 RPC 隐私认证（修复 0x0000011b）",
            Description = "2021 年之后的补丁默认开启打印 RPC 数据包的隐私验证，这是共享打印机报 0x0000011b 的最主要原因。",
            Commands =
            [
                "reg add HKLM\\SYSTEM\\CurrentControlSet\\Control\\Print /v RpcAuthnLevelPrivacyEnabled /t REG_DWORD /d 0 /f",
            ],
            Handler = (context, _) =>
            {
                var changes = new List<string>();
                var change = context.Registry.SetDword(RegistryTools.Print, "RpcAuthnLevelPrivacyEnabled", 0);
                if (change is not null)
                {
                    changes.Add(change);
                }

                var message = changes.Count == 0
                    ? "RpcAuthnLevelPrivacyEnabled 已经为 0，不会出现 0x0000011b。"
                    : "已关闭打印 RPC 隐私认证。";
                return Task.FromResult(StepResult.Ok(message, [.. changes]));
            },
        };
    }

    public static RepairStep PointAndPrintPolicy()
    {
        return new RepairStep
        {
            Id = "print.driver",
            Title = "允许非管理员安装共享打印机驱动（修复 0x00000740）",
            Description = "Windows 10 1809+ 与 Windows 11 24H2 默认只允许管理员安装打印机驱动，普通用户连接共享打印机需要放宽该策略。",
            Commands =
            [
                "reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\Printers\\PointAndPrint\" /v RestrictDriverInstallationToAdministrators /t REG_DWORD /d 0 /f",
                "reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\Printers\\PointAndPrint\" /v NoWarningNoElevationOnInstall /t REG_DWORD /d 1 /f",
            ],
            Handler = (context, _) =>
            {
                var changes = new List<string>();
                var views = new[]
                {
                    (View: Microsoft.Win32.RegistryView.Registry64, Name: "64 位"),
                    (View: Microsoft.Win32.RegistryView.Registry32, Name: "32 位"),
                };

                foreach (var (view, viewName) in views)
                {
                    var pairs = new[]
                    {
                        (ValueName: "RestrictDriverInstallationToAdministrators", Value: 0),
                        (ValueName: "NoWarningNoElevationOnInstall", Value: 1),
                        (ValueName: "UpdatePromptSettings", Value: 0),
                    };

                    foreach (var (valueName, value) in pairs)
                    {
                        var change = context.Registry.SetDword(RegistryTools.PointAndPrintPolicy, valueName, value, view);
                        if (change is not null)
                        {
                            changes.Add($"{viewName}视图：{change}");
                        }
                    }
                }

                var message = changes.Count == 0
                    ? "打印机驱动安装策略已经符合要求。"
                    : $"已放宽打印机驱动安装限制（{changes.Count} 项）。";
                return Task.FromResult(StepResult.Ok(message, [.. changes]));
            },
        };
    }

    public static RepairStep EnableSmb1(bool supported)
    {
        return new RepairStep
        {
            Id = "smb1",
            Title = "启用 SMB1 协议（兼容老旧设备）",
            Description = supported
                ? "部分老打印机、老 NAS 只支持 SMBv1。默认不勾选，确认对端设备很旧时再启用。"
                : "Windows 11 已移除 SMB1，本步骤会自动跳过。",
            OptionKey = "smb1",
            Commands =
            [
                "Enable-WindowsOptionalFeature -Online -FeatureName SMB1Protocol -NoRestart",
                "Set-SmbServerConfiguration -EnableSMB1Protocol $true -Force",
            ],
            Handler = async (context, ct) =>
            {
                if (!supported)
                {
                    return StepResult.Skip("Windows 11 已移除 SMB1 功能，无需也无法启用。");
                }

                const string script = """
                    $out = New-Object System.Collections.Generic.List[string]
                    try {
                        $feature = Get-WindowsOptionalFeature -Online -FeatureName SMB1Protocol -ErrorAction Stop
                        if ($feature.State -eq 'Enabled') {
                            $out.Add("OK|SMB1 功能|已启用")
                        } else {
                            Enable-WindowsOptionalFeature -Online -FeatureName SMB1Protocol -All -NoRestart -ErrorAction Stop | Out-Null
                            $out.Add("FIXED|SMB1 功能|已启用（部分设置需重启后生效）")
                        }
                    } catch {
                        $out.Add("WARN|SMB1 功能|$($_.Exception.Message)")
                    }
                    try {
                        $server = Get-SmbServerConfiguration -ErrorAction Stop
                        if ($server.EnableSMB1Protocol) {
                            $out.Add("OK|SMB1 服务端|已启用")
                        } else {
                            Set-SmbServerConfiguration -EnableSMB1Protocol $true -Force -ErrorAction Stop
                            $out.Add("FIXED|SMB1 服务端|已启用")
                        }
                    } catch {
                        $out.Add("WARN|SMB1 服务端|$($_.Exception.Message)")
                    }
                    $out -join "`n"
                    """;

                var result = await context.PowerShell.RunAsync(script, ct, "启用 SMB1 协议", 600_000);
                return StatusScript.Summarize("SMB1 设置完成", StatusScript.Parse(result));
            },
        };
    }

    public static RepairStep Win11PrintRpcPolicy()
    {
        return new RepairStep
        {
            Id = "win11.print.rpc.policy",
            Title = "允许打印后台处理程序使用 RPC over TCP（Windows 11 24H2+）",
            Description = "Windows 11 24H2 起打印后台处理程序默认只监听命名管道，会导致部分客户端无法连接共享打印机。",
            Commands =
            [
                "reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\Printers\\RPC\" /v RpcUseNamedPipeProtocol /t REG_DWORD /d 0 /f",
                "reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\Printers\\RPC\" /v RpcAuthentication /t REG_DWORD /d 0 /f",
                "reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\Printers\\RPC\" /v ForceKerberosForRpc /t REG_DWORD /d 0 /f",
            ],
            Handler = (context, _) =>
            {
                var snapshot = SystemSnapshot.Load(context.Log);
                if (!snapshot.IsWindows11Build26100OrLater)
                {
                    return Task.FromResult(StepResult.Skip("当前系统低于 Windows 11 24H2，无需调整打印 RPC 传输方式。"));
                }

                var changes = new List<string>();
                var pairs = new[]
                {
                    (ValueName: "RpcUseNamedPipeProtocol", Value: 0),
                    (ValueName: "RpcAuthentication", Value: 0),
                    (ValueName: "ForceKerberosForRpc", Value: 0),
                };

                foreach (var (valueName, value) in pairs)
                {
                    var change = context.Registry.SetDword(RegistryTools.PrintRpcPolicy, valueName, value);
                    if (change is not null)
                    {
                        changes.Add(change);
                    }
                }

                var message = changes.Count == 0
                    ? "打印 RPC 传输策略已经允许 TCP，无需修改。"
                    : "已允许打印 RPC 同时使用命名管道与 TCP。";
                return Task.FromResult(StepResult.Ok(message, [.. changes]));
            },
        };
    }

    public static RepairStep ProtectedPrintMode()
    {
        return new RepairStep
        {
            Id = "win11.wpp",
            Title = "关闭 Windows 受保护的打印模式（Windows 11 24H2+）",
            Description = "受保护的打印模式只允许 Microsoft IPP 类驱动，会直接禁掉传统共享打印机。仅在检测到已开启时才需要关闭。",
            OptionKey = "wpp",
            Commands = ["reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\Printers\\WPP\" /v WindowsProtectedPrintMode /t REG_DWORD /d 0 /f"],
            Handler = (context, _) =>
            {
                var snapshot = SystemSnapshot.Load(context.Log);
                if (snapshot.ProtectedPrintMode != 1)
                {
                    return Task.FromResult(StepResult.Skip("未启用受保护的打印模式，无需处理。"));
                }

                var change = context.Registry.SetDword(RegistryTools.WppPolicy, "WindowsProtectedPrintMode", 0);
                return Task.FromResult(StepResult.Ok(
                    "已关闭 Windows 受保护的打印模式，传统共享打印机可以重新工作。",
                    change ?? "注册表值未发生变化"));
            },
        };
    }

    public static RepairStep RestartServices()
    {
        return new RepairStep
        {
            Id = "services.restart",
            Title = "重启关键服务让设置生效",
            Description = "重启 Print Spooler、Server 与 Function Discovery Resource Publication。",
            OptionKey = "restart-services",
            Commands = ["Restart-Service -Name Spooler, LanmanServer, FDResPub -Force"],
            Handler = async (context, ct) =>
            {
                const string script = """
                    $out = New-Object System.Collections.Generic.List[string]
                    foreach ($name in @('Spooler','LanmanServer','FDResPub')) {
                        $svc = Get-Service -Name $name -ErrorAction SilentlyContinue
                        if ($null -eq $svc) { $out.Add("MISSING|$name|服务不存在") ; continue }
                        try {
                            Restart-Service -Name $name -Force -ErrorAction Stop
                            $out.Add("OK|$name|已重启")
                        } catch {
                            try {
                                Start-Service -Name $name -ErrorAction Stop
                                $out.Add("STARTED|$name|已启动")
                            } catch {
                                $out.Add("ERR|$name|$($_.Exception.Message)")
                            }
                        }
                    }
                    $out -join "`n"
                    """;

                var result = await context.PowerShell.RunAsync(script, ct, "重启关键服务");
                string[] critical = ["Spooler", "LanmanServer"];
                return StatusScript.Summarize("服务重启完成", StatusScript.Parse(result), critical);
            },
        };
    }

    public static RepairStep Verify()
    {
        return new RepairStep
        {
            Id = "verify",
            Title = "复核修复结果",
            Description = "重新读取服务、防火墙与注册表状态，确认各项设置已经生效。",
            Commands = ["Get-Service ...", "Get-NetFirewallRule ...", "reg query ..."],
            Handler = (context, _) =>
            {
                var snapshot = SystemSnapshot.Load(context.Log);
                var items = snapshot.ToDetectionItems();
                var problems = items.Where(i => i.Status == DetectionStatus.Problem).ToList();
                var warnings = items.Where(i => i.Status == DetectionStatus.Warning).ToList();
                var details = items
                    .Where(i => i.Status is DetectionStatus.Problem or DetectionStatus.Warning)
                    .Select(i => $"{i.Title}：{i.Detail}")
                    .ToList();

                if (problems.Count == 0)
                {
                    return Task.FromResult(StepResult.Ok(
                        warnings.Count == 0
                            ? "复核完成：所有检查项均通过。"
                            : $"复核完成：{warnings.Count} 项建议关注，没有阻塞性问题。",
                        [.. details]));
                }

                return Task.FromResult(StepResult.Warn(
                    $"复核完成：仍有 {problems.Count} 项需要处理。",
                    [.. details]));
            },
        };
    }

    private static string BuildServiceScript(IReadOnlyList<ServiceSpec> specs)
    {
        var builder = new StringBuilder();
        builder.AppendLine("$out = New-Object System.Collections.Generic.List[string]");
        builder.AppendLine("$targets = @(");
        foreach (var spec in specs)
        {
            var configurable = spec.Configurable ? "$true" : "$false";
            builder.AppendLine(
                $"    [pscustomobject]@{{ Name = '{spec.Name}'; Mode = '{spec.StartMode}'; Configurable = {configurable} }}");
        }

        builder.AppendLine(")");
        builder.AppendLine("""
            foreach ($t in $targets) {
                $svc = Get-Service -Name $t.Name -ErrorAction SilentlyContinue
                if ($null -eq $svc) {
                    $out.Add("MISSING|$($t.Name)|系统未提供该服务")
                    continue
                }
                if ($t.Configurable) {
                    try {
                        if ($svc.StartType.ToString() -ne $t.Mode) {
                            Set-Service -Name $t.Name -StartupType $t.Mode -ErrorAction Stop
                            $out.Add("CONFIG|$($t.Name)|启动类型 $($svc.StartType) -> $($t.Mode)")
                        }
                    } catch {
                        $out.Add("WARN|$($t.Name)|无法修改启动类型：$($_.Exception.Message)")
                    }
                }
                $svc = Get-Service -Name $t.Name -ErrorAction SilentlyContinue
                if ($svc.Status -ne 'Running') {
                    try {
                        Start-Service -Name $t.Name -ErrorAction Stop
                        $out.Add("STARTED|$($t.Name)|已启动")
                    } catch {
                        $out.Add("ERR|$($t.Name)|启动失败：$($_.Exception.Message)")
                    }
                } else {
                    $out.Add("OK|$($t.Name)|正在运行（$($svc.StartType)）")
                }
            }
            $out -join "`n"
            """);
        return builder.ToString();
    }

    private static string BuildFirewallScript(bool allowAnyRemoteAddress, bool allowRpcDynamicPorts)
    {
        var remote = allowAnyRemoteAddress ? "Any" : "LocalSubnet";
        var builder = new StringBuilder();
        builder.AppendLine("$out = New-Object System.Collections.Generic.List[string]");
        builder.AppendLine($"$remote = '{remote}'");
        builder.AppendLine("""
            # 内置规则组的 Group 属性可能是资源字符串，也可能是本地化名称，两种都尝试匹配。
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
            $canQuery = $true
            try {
                $null = Get-NetFirewallRule -ErrorAction Stop
            } catch {
                $canQuery = $false
                $out.Add("ERR|防火墙规则查询|$($_.Exception.Message)")
            }
            if ($canQuery) {
                $groupTargets = [ordered]@{
                    '文件和打印机共享' = @{ Id = '@FirewallAPI.dll,-32752'; Patterns = @('*File and Printer Sharing*','*文件和打印机共享*') }
                    '网络发现'         = @{ Id = '@FirewallAPI.dll,-32753'; Patterns = @('*Network Discovery*','*网络发现*') }
                }
                foreach ($key in $groupTargets.Keys) {
                    $target = $groupTargets[$key]
                    $rules = @(Get-FirewallGroupRules -IndirectId $target.Id -Patterns $target.Patterns)
                    if ($rules.Count -eq 0) {
                        $out.Add("MISSING|$key|系统中未找到该内置规则组")
                        continue
                    }
                    $disabled = @($rules | Where-Object { $_.Enabled -ne 'True' })
                    if ($disabled.Count -eq 0) {
                        $out.Add("OK|$key|$($rules.Count) 条规则均已启用")
                        continue
                    }
                    $fixed = 0
                    foreach ($rule in $disabled) {
                        try {
                            Set-NetFirewallRule -Name $rule.Name -Enabled True -ErrorAction Stop
                            $fixed++
                        } catch {
                            $out.Add("ERR|$key|$($rule.DisplayName)：$($_.Exception.Message)")
                        }
                    }
                    if ($fixed -gt 0) { $out.Add("FIXED|$key|已启用 $fixed 条规则") }
                }
            }
            function Add-PsfRule {
                param([string]$RuleName, [string]$Protocol, [string]$Ports)
                $existing = @(Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue)
                if ($existing.Count -gt 0) {
                    foreach ($rule in $existing) {
                        try {
                            Set-NetFirewallRule -Name $rule.Name -Enabled True -Profile Domain,Private -Action Allow -Direction Inbound -RemoteAddress $remote -ErrorAction Stop
                            $out.Add("OK|$RuleName|规则已存在并启用")
                        } catch {
                            $out.Add("ERR|$RuleName|$($_.Exception.Message)")
                        }
                    }
                    return
                }
                try {
                    New-NetFirewallRule -DisplayName $RuleName -Group 'PrinterShareFixer' -Description '打印机共享修复工具创建的放行规则' -Direction Inbound -Action Allow -Protocol $Protocol -LocalPort $Ports -Profile Domain,Private -RemoteAddress $remote -ErrorAction Stop | Out-Null
                    $out.Add("CREATED|$RuleName|$Protocol $Ports（远程范围：$remote）")
                } catch {
                    $out.Add("ERR|$RuleName|$($_.Exception.Message)")
                }
            }
            """);

        builder.AppendLine("Add-PsfRule -RuleName 'PSF-SMB-In-TCP' -Protocol TCP -Ports '445'");
        builder.AppendLine("Add-PsfRule -RuleName 'PSF-NetBIOS-In-TCP' -Protocol TCP -Ports '137,139'");
        builder.AppendLine("Add-PsfRule -RuleName 'PSF-NetBIOS-In-UDP' -Protocol UDP -Ports '137,138'");
        builder.AppendLine("Add-PsfRule -RuleName 'PSF-RPC-In-TCP' -Protocol TCP -Ports '135'");
        if (allowRpcDynamicPorts)
        {
            builder.AppendLine("Add-PsfRule -RuleName 'PSF-RPC-Dynamic-In-TCP' -Protocol TCP -Ports '49152-65535'");
        }

        builder.AppendLine("Add-PsfRule -RuleName 'PSF-WSDAPI-In-TCP' -Protocol TCP -Ports '5357,5358'");
        builder.AppendLine("Add-PsfRule -RuleName 'PSF-WSD-Discovery-In-UDP' -Protocol UDP -Ports '3702,5353,5355'");
        builder.AppendLine("$out -join \"`n\"");
        return builder.ToString();
    }
}
