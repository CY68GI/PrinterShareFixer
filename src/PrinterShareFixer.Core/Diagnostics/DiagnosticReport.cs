using System.Text;
using PrinterShareFixer.Core.Models;
using PrinterShareFixer.Core.Runtime;

namespace PrinterShareFixer.Core.Diagnostics;

/// <summary>诊断报告的生成参数。</summary>
public sealed record DiagnosticOptions
{
    public RepairRole Role { get; init; } = RepairRole.Consumer;

    /// <summary>对方电脑（客户端模式下填了才能做名称解析、连通性与连接尝试）。</summary>
    public string? TargetHost { get; init; }

    /// <summary>客户端模式下是否真的尝试连接对方的共享打印机。</summary>
    public bool TryConnectPrinter { get; init; } = true;

    /// <summary>每类事件日志取多少条。</summary>
    public int EventLogCount { get; init; } = 20;
}

public sealed record DiagnosticSection(string Title, IReadOnlyList<string> Scripts);

public sealed record DiagnosticResult(string FilePath, string Text, IReadOnlyList<string> Highlights);

/// <summary>
/// 生成尽可能完整的诊断报告：系统、网络、名称解析、服务、SMB、注册表、防火墙、
/// 打印机与驱动、共享与权限、安全软件、事件日志、连接尝试，最后给出自动判断。
/// 任何一项查询失败都不会中断报告，只会在对应位置写明原因。
/// </summary>
public static class DiagnosticReport
{
    public static async Task<DiagnosticResult> GenerateAsync(
        DiagnosticOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var runner = new PowerShellRunner(NullLogSink.Instance);
        var target = options.TargetHost?.Trim();
        var report = new StringBuilder();

        WriteHeader(report, options, target);

        foreach (var section in BuildSections(options, target))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(section.Title);

            report.AppendLine();
            report.AppendLine(new string('#', 78));
            report.AppendLine($"# {section.Title}");
            report.AppendLine(new string('#', 78));

            foreach (var script in section.Scripts)
            {
                var friendly = FirstComment(script) ?? "查询";
                report.AppendLine();
                report.AppendLine($"----- {friendly} -----");

                var result = await runner.RunAsync(script, cancellationToken, friendly, 180_000).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(result.StandardOutput))
                {
                    report.AppendLine(result.StandardOutput.TrimEnd());
                }

                if (!result.Succeeded)
                {
                    report.AppendLine($"[此项查询未成功：退出代码 {result.ExitCode}；{result.ErrorSummary}]");
                }
            }
        }

        if (options.Role == RepairRole.Consumer && options.TryConnectPrinter && !string.IsNullOrWhiteSpace(target))
        {
            progress?.Report("尝试连接对方的共享打印机");
            report.AppendLine();
            report.AppendLine(new string('#', 78));
            report.AppendLine("# 12. 连接尝试（直接调用系统接口，错误码最准确）");
            report.AppendLine(new string('#', 78));
            report.AppendLine();
            report.AppendLine(await TryConnectPrinterAsync(runner, target!, cancellationToken).ConfigureAwait(false));
        }

        progress?.Report("汇总判断");
        report.AppendLine();
        report.AppendLine(new string('#', 78));
        report.AppendLine("# 自动判断与建议");
        report.AppendLine(new string('#', 78));
        report.AppendLine();

        var highlights = new List<string>();
        try
        {
            var snapshot = await Task.Run(
                () => SystemSnapshot.Load(NullLogSink.Instance, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            highlights.AddRange(Analyze(options, snapshot));
        }
        catch (Exception ex)
        {
            report.AppendLine($"（自动分析失败：{ex.Message}）");
        }

        if (highlights.Count == 0)
        {
            report.AppendLine(" · 未发现明显的配置问题；建议同时提供对方电脑的报告以便比对。");
        }
        else
        {
            foreach (var line in highlights)
            {
                report.AppendLine($" · {line}");
            }
        }

        var fileName = $"diagnostic-{options.Role.ToKey()}-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
        var path = Path.Combine(AppPaths.EnsureDiagnosticDirectory(), fileName);
        File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));

        return new DiagnosticResult(path, report.ToString(), highlights);
    }

    private static void WriteHeader(StringBuilder report, DiagnosticOptions options, string? target)
    {
        report.AppendLine("================================================================");
        report.AppendLine(" 打印机共享修复工具 —— 详细诊断报告");
        report.AppendLine("================================================================");
        report.AppendLine($"生成时间    : {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
        report.AppendLine($"工具版本    : v{AppInfo.Version}");
        report.AppendLine($"本机角色    : {(options.Role == RepairRole.Provider ? "本机接有打印机（服务端）" : "本机没有打印机（客户端）")}");
        report.AppendLine($"目标电脑    : {(string.IsNullOrWhiteSpace(target) ? "（未填写）" : target)}");
        report.AppendLine($"计算机名    : {Environment.MachineName}");
        report.AppendLine($"当前用户    : {Environment.UserDomainName}\\{Environment.UserName}");
        report.AppendLine($"进程架构    : {(Environment.Is64BitProcess ? "64 位" : "32 位")}");
        report.AppendLine("提示        : 本文件包含计算机名、用户名、IP、共享名等信息，转发给他人前请自行确认。");
    }

    private static IReadOnlyList<DiagnosticSection> BuildSections(DiagnosticOptions options, string? target)
    {
        var eventCount = options.EventLogCount;
        var sections = new List<DiagnosticSection>
        {
            new("1. 操作系统与账号", new[]
            {
                """
                # 系统版本（注册表）
                Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' |
                    Select-Object ProductName,DisplayVersion,CurrentBuild,UBR,EditionID,InstallationType,InstallDate |
                    Format-List
                """,
                """
                # 操作系统与计算机信息
                $os = Get-CimInstance Win32_OperatingSystem
                $cs = Get-CimInstance Win32_ComputerSystem
                [pscustomobject]@{
                    Caption        = $os.Caption
                    Version        = $os.Version
                    BuildNumber    = $os.BuildNumber
                    Architecture   = $os.OSArchitecture
                    LastBootUpTime = $os.LastBootUpTime
                    ComputerName   = $cs.Name
                    Domain         = $cs.Domain
                    PartOfDomain   = $cs.PartOfDomain
                    Workgroup      = $cs.Workgroup
                    Manufacturer   = $cs.Manufacturer
                    Model          = $cs.Model
                } | Format-List
                """,
                """
                # 账号类型与提权状态
                $id = [Security.Principal.WindowsIdentity]::GetCurrent()
                $adminSid = New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')
                [pscustomobject]@{
                    User                  = $id.Name
                    AuthenticationType    = $id.AuthenticationType
                    IsElevated            = (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
                    InAdministratorsGroup = ($id.Groups -contains $adminSid)
                } | Format-List
                """,
                """
                # 最近安装的更新（补丁级别）
                Get-HotFix -ErrorAction SilentlyContinue | Sort-Object InstalledOn -Descending |
                    Select-Object -First 15 HotFixID,Description,InstalledOn | Format-Table -AutoSize
                """
            }),

            new("2. 网络适配器与 IP 配置", new[]
            {
                """
                # 网卡
                Get-NetAdapter -ErrorAction SilentlyContinue |
                    Select-Object Name,InterfaceDescription,Status,LinkSpeed,MacAddress,InterfaceType |
                    Format-Table -AutoSize
                """,
                """
                # IPv4 地址
                Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
                    Select-Object InterfaceAlias,IPAddress,PrefixLength,AddressState,PrefixOrigin,SuffixOrigin |
                    Format-Table -AutoSize
                """,
                """
                # 网关与 DNS
                Get-NetIPConfiguration -ErrorAction SilentlyContinue | ForEach-Object {
                    [pscustomobject]@{
                        Interface   = $_.InterfaceAlias
                        IPv4        = ($_.IPv4Address.IPAddress -join ', ')
                        Gateway     = ($_.IPv4DefaultGateway.NextHop -join ', ')
                        DnsServers  = (($_.DNSServer | Where-Object { $_.AddressFamily -eq 2 } | ForEach-Object { $_.ServerAddresses } | Select-Object -First 4) -join ', ')
                        DhcpEnabled = $_.NetIPv4Interface.Dhcp
                    }
                } | Format-Table -AutoSize
                """,
                """
                # 网络位置（公用 / 专用）
                Get-NetConnectionProfile -ErrorAction SilentlyContinue |
                    Select-Object Name,InterfaceAlias,NetworkCategory,IPv4Connectivity,IPv6Connectivity |
                    Format-Table -AutoSize
                """,
                """
                # 完整 ipconfig（含 NetBIOS / WINS 状态）
                ipconfig /all
                """
            }),

            new("3. 打印机共享相关服务", new[]
            {
                """
                # 服务状态与启动类型
                Get-CimInstance Win32_Service -ErrorAction SilentlyContinue |
                    Where-Object { @('LanmanServer','LanmanWorkstation','Spooler','RpcSs','RpcEptMapper','DcomLaunch','RpcLocator','Dnscache','FDResPub','fdPHost','SSDPSRV','upnphost','NlaSvc','netprofm','Netman','mpssvc','Browser','WdisService','PrintNotify') -contains $_.Name } |
                    Select-Object Name,DisplayName,State,StartMode,StartName |
                    Sort-Object Name | Format-Table -AutoSize -Wrap
                """,
                """
                # 打印后台处理程序的依赖关系
                $spooler = Get-Service -Name Spooler -ErrorAction SilentlyContinue
                if ($spooler) {
                    '依赖 Spooler 的服务:'
                    $spooler.DependentServices | Select-Object Name,DisplayName,Status,StartType | Format-Table -AutoSize
                    'Spooler 依赖的服务:'
                    $spooler.RequiredServices | Select-Object Name,DisplayName,Status,StartType | Format-Table -AutoSize
                }
                """
            }),

            new("4. SMB 客户端配置（本机作为连接方）", new[]
            {
                """
                # Get-SmbClientConfiguration
                Get-SmbClientConfiguration -ErrorAction SilentlyContinue | Format-List
                """,
                """
                # 当前 SMB 连接（方言、签名、加密）
                Get-SmbConnection -ErrorAction SilentlyContinue |
                    Select-Object ServerName,ShareName,Dialect,NumOpens,UserName,CredentialSecurity |
                    Format-Table -AutoSize
                """,
                """
                # net use 现有映射
                net use
                """,
                """
                # 已保存的网络凭据（只显示目标与用户名，不含密码）
                cmdkey /list
                """
            }),

            new("5. SMB 服务端配置（本机作为共享方）", new[]
            {
                """
                # Get-SmbServerConfiguration
                Get-SmbServerConfiguration -ErrorAction SilentlyContinue | Format-List
                """,
                """
                # 当前 SMB 会话与打开的文件
                Get-SmbSession -ErrorAction SilentlyContinue | Select-Object ClientComputerName,ClientUserName,Dialect,NumOpens | Format-Table -AutoSize
                Get-SmbOpenFile -ErrorAction SilentlyContinue | Select-Object ClientComputerName,ClientUserName,Path,ShareRelativePath | Format-Table -AutoSize -Wrap
                """,
                """
                # SMB1 功能是否安装
                $feature = Get-WindowsOptionalFeature -Online -FeatureName SMB1Protocol -ErrorAction SilentlyContinue
                if ($feature) { [pscustomobject]@{ Feature = 'SMB1Protocol'; State = $feature.State } | Format-List } else { 'SMB1 功能查询不可用' }
                """
            }),

            new("6. 注册表关键项", new[]
            {
                """
                # LSA：来宾 / 匿名 / 本地账号远程登录
                Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Lsa' -ErrorAction SilentlyContinue |
                    Select-Object LimitBlankPasswordUse,restrictanonymous,restrictanonymoussam,everyoneincludesanonymous,LmCompatibilityLevel,ForceGuest |
                    Format-List
                Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -ErrorAction SilentlyContinue |
                    Select-Object LocalAccountTokenFilterPolicy | Format-List
                """,
                """
                # LanmanServer（本机作为服务端时的共享策略）
                Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters' -ErrorAction SilentlyContinue |
                    Select-Object RequireSecuritySignature,EnableSecuritySignature,EnableSMB1Protocol,SMB1,AutoDisconnect,RestrictNullSessAccess,NullSessionShares |
                    Format-List
                """,
                """
                # LanmanWorkstation（本机作为客户端时的连接策略）
                Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters' -ErrorAction SilentlyContinue |
                    Select-Object RequireSecuritySignature,EnableSecuritySignature,AllowInsecureGuestAuth,EnablePlainTextPassword |
                    Format-List
                Get-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation' -ErrorAction SilentlyContinue |
                    Select-Object AllowInsecureGuestAuth | Format-List
                """,
                """
                # 网络提供程序加载顺序
                Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\NetworkProvider\Order' -ErrorAction SilentlyContinue | Select-Object ProviderOrder | Format-List
                Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\NetworkProvider\HwOrder' -ErrorAction SilentlyContinue | Select-Object ProviderOrder | Format-List
                """,
                """
                # 打印相关策略：RPC 隐私认证 / 驱动安装限制 / RPC 传输 / 受保护的打印模式
                Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Print' -ErrorAction SilentlyContinue |
                    Select-Object RpcAuthnLevelPrivacyEnabled | Format-List
                Get-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\Printers' -ErrorAction SilentlyContinue |
                    Select-Object RestrictDriverInstallationToAdministrators | Format-List
                Get-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\Printers\PointAndPrint' -ErrorAction SilentlyContinue |
                    Select-Object RestrictDriverInstallationToAdministrators,NoWarningNoElevationOnInstall,UpdatePromptSettings,TrustedServers,ServerList |
                    Format-List
                Get-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\Printers\RPC' -ErrorAction SilentlyContinue |
                    Select-Object RpcUseNamedPipeProtocol,RpcAuthentication,ForceKerberosForRpc | Format-List
                Get-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\Printers\WPP' -ErrorAction SilentlyContinue |
                    Select-Object WindowsProtectedPrintMode | Format-List
                """,
                """
                # 打印提供程序与打印处理器
                '打印提供程序:'
                Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Control\Print\Providers' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty PSChildName
                '打印处理器:'
                Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Control\Print\Environments\Windows x64\Print Processors' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty PSChildName
                """
            }),

            new("7. 防火墙", new[]
            {
                """
                # 三个配置文件状态
                Get-NetFirewallProfile -ErrorAction SilentlyContinue |
                    Select-Object Name,Enabled,DefaultInboundAction,DefaultOutboundAction,AllowInboundRules |
                    Format-Table -AutoSize
                """,
                """
                # 内置共享规则组的启用统计
                $fp = @(Get-NetFirewallRule -Group '@FirewallAPI.dll,-32752' -ErrorAction SilentlyContinue)
                $nd = @(Get-NetFirewallRule -Group '@FirewallAPI.dll,-32753' -ErrorAction SilentlyContinue)
                [pscustomobject]@{
                    文件和打印机共享总数   = $fp.Count
                    文件和打印机共享已启用 = @($fp | Where-Object { $_.Enabled -eq 'True' }).Count
                    网络发现总数           = $nd.Count
                    网络发现已启用         = @($nd | Where-Object { $_.Enabled -eq 'True' }).Count
                } | Format-List
                """,
                """
                # 本工具创建的放行规则
                $rules = @(Get-NetFirewallRule -Group 'PrinterShareFixer' -ErrorAction SilentlyContinue)
                $rules | Select-Object DisplayName,Enabled,Direction,Action,Profile | Format-Table -AutoSize
                $ports = @(foreach ($rule in $rules) {
                    $port = $rule | Get-NetFirewallPortFilter -ErrorAction SilentlyContinue
                    if ($port) { [pscustomobject]@{ Rule = $rule.DisplayName; Protocol = $port.Protocol; LocalPort = $port.LocalPort } }
                })
                $ports | Format-Table -AutoSize
                """,
                """
                # 第三方防火墙产品
                Get-CimInstance -Namespace root\SecurityCenter2 -ClassName FirewallProduct -ErrorAction SilentlyContinue |
                    Select-Object displayName,productState,pathToSignedProductExe | Format-List
                """
            }),

            new("8. 本机打印机、驱动与端口", new[]
            {
                """
                # 所有打印机（含是否共享、共享名、端口、状态）
                Get-Printer -ErrorAction SilentlyContinue |
                    Select-Object Name,DriverName,PortName,Shared,ShareName,Published,PrinterStatus,Type |
                    Format-Table -AutoSize -Wrap
                """,
                """
                # WMI 视角（本地/网络、默认打印机、属性位）
                Get-CimInstance Win32_Printer -ErrorAction SilentlyContinue |
                    Select-Object Name,DriverName,PortName,Shared,ShareName,Local,Network,Default,PrinterStatus,SpoolEnabled,Attributes |
                    Format-List
                """,
                """
                # 已安装的打印机驱动（客户端能否匹配到驱动很关键）
                Get-PrinterDriver -ErrorAction SilentlyContinue |
                    Select-Object Name,Manufacturer,DriverVersion,PrinterEnvironment,InfPath |
                    Format-Table -AutoSize -Wrap
                """,
                """
                # 打印机端口
                Get-PrinterPort -ErrorAction SilentlyContinue |
                    Select-Object Name,Description,PrinterHostAddress,PortNumber,Protocol |
                    Format-Table -AutoSize -Wrap
                """,
                """
                # 打印队列里的任务（卡住的任务会阻塞连接）
                Get-Printer -ErrorAction SilentlyContinue | ForEach-Object {
                    $jobs = @(Get-PrintJob -PrinterName $_.Name -ErrorAction SilentlyContinue)
                    if ($jobs.Count -gt 0) {
                        [pscustomobject]@{ Printer = $_.Name; JobCount = $jobs.Count; FirstSubmitted = ($jobs | Select-Object -First 1).SubmittedTime }
                    }
                } | Format-Table -AutoSize
                """
            }),

            new("9. 共享与共享权限", new[]
            {
                """
                # 共享列表
                Get-SmbShare -ErrorAction SilentlyContinue |
                    Select-Object Name,Path,Description,ShareType,CurrentUsers,EncryptData,ScopeName |
                    Format-Table -AutoSize -Wrap
                """,
                """
                # 每个共享的访问权限（连不上时的关键信息）
                foreach ($share in (Get-SmbShare -ErrorAction SilentlyContinue | Where-Object { -not $_.Name.EndsWith('$') })) {
                    "共享: $($share.Name)   路径: $($share.Path)"
                    Get-SmbShareAccess -Name $share.Name -ErrorAction SilentlyContinue |
                        Select-Object AccountName,AccessControlType,AccessRight | Format-Table -AutoSize
                }
                """,
                """
                # WMI 共享视图
                Get-CimInstance Win32_Share -ErrorAction SilentlyContinue |
                    Select-Object Name,Path,Type,AllowMaximum | Format-Table -AutoSize
                """
            }),

            new("10. 安全软件", new[]
            {
                """
                # Windows Defender 状态
                Get-MpComputerStatus -ErrorAction SilentlyContinue |
                    Select-Object AMServiceEnabled,AntivirusEnabled,RealTimeProtectionEnabled,IsTamperProtected,BehaviorMonitorEnabled,AntivirusSignatureVersion |
                    Format-List
                """,
                """
                # 已注册的杀毒软件
                Get-CimInstance -Namespace root\SecurityCenter2 -ClassName AntiVirusProduct -ErrorAction SilentlyContinue |
                    Select-Object displayName,productState,pathToSignedProductExe | Format-List
                """
            }),

            new("11. 事件日志", new[]
            {
                $$"""
                # 打印服务日志（管理员 + 操作）
                foreach ($logName in @('Microsoft-Windows-PrintService/Admin','Microsoft-Windows-PrintService/Operational')) {
                    "日志: $logName"
                    Get-WinEvent -LogName $logName -MaxEvents {{eventCount}} -ErrorAction SilentlyContinue |
                        Select-Object TimeCreated,Id,LevelDisplayName,Message | Format-List
                }
                """,
                $$"""
                # SMB 客户端 / 服务端日志
                foreach ($logName in @('Microsoft-Windows-SMBClient/Connectivity','Microsoft-Windows-SMBServer/Operational','Microsoft-Windows-SmbClient/Security')) {
                    "日志: $logName"
                    Get-WinEvent -LogName $logName -MaxEvents {{eventCount}} -ErrorAction SilentlyContinue |
                        Select-Object TimeCreated,Id,LevelDisplayName,Message | Format-List
                }
                """,
                $$"""
                # 系统日志中打印 / SMB 相关的错误与警告（最近 3 天）
                Get-WinEvent -FilterHashtable @{ LogName='System'; StartTime=(Get-Date).AddDays(-3); Level=1,2,3 } -MaxEvents 200 -ErrorAction SilentlyContinue |
                    Where-Object { $_.ProviderName -match 'Print|Spool|SMB|Lanman|WSD' } |
                    Select-Object -First {{eventCount}} TimeCreated,ProviderName,Id,LevelDisplayName,Message | Format-List
                """,
                $$"""
                # 应用程序日志中与打印相关的条目（最近 3 天）
                Get-WinEvent -FilterHashtable @{ LogName='Application'; StartTime=(Get-Date).AddDays(-3) } -MaxEvents 200 -ErrorAction SilentlyContinue |
                    Where-Object { $_.ProviderName -match 'spoolsv|Print|Application Error' } |
                    Select-Object -First {{eventCount}} TimeCreated,ProviderName,Id,LevelDisplayName,Message | Format-List
                """
            })
        };

        if (!string.IsNullOrWhiteSpace(target))
        {
            var escaped = Escape(target!);
            sections.Add(new DiagnosticSection("12. 到目标电脑的名称解析与连通性", new[]
            {
                $$"""
                # 名称解析（DNS / NetBIOS）
                try { Resolve-DnsName -Name '{{escaped}}' -ErrorAction Stop | Select-Object Name,Type,IPAddress | Format-Table -AutoSize } catch { "Resolve-DnsName 失败: $($_.Exception.Message)" }
                nbtstat -A {{escaped}}
                """,
                $$"""
                # ping 与端口（445 / 135 / 139）
                ping -n 2 {{escaped}}
                foreach ($port in @(445,135,139)) {
                    $r = Test-NetConnection -ComputerName '{{escaped}}' -Port $port -WarningAction SilentlyContinue -ErrorAction SilentlyContinue
                    [pscustomobject]@{ Port = $port; TcpTestSucceeded = $r.TcpTestSucceeded; RemoteAddress = $r.RemoteAddress } | Format-Table -AutoSize
                }
                """,
                $$"""
                # 枚举对方共享与现有会话
                net view \\{{escaped}}
                "net view 退出代码: $LASTEXITCODE"
                net use
                Get-SmbConnection -ServerName '{{escaped}}' -ErrorAction SilentlyContinue | Format-List
                """,
                """
                # hosts 文件中的自定义解析（非注释行）
                Get-Content "$env:SystemRoot\System32\drivers\etc\hosts" -ErrorAction SilentlyContinue |
                    Where-Object { $_ -and -not $_.TrimStart().StartsWith('#') }
                """
            }));
        }

        return sections;
    }

    /// <summary>客户端：真的调用系统接口连一次，拿到最原始的错误码。</summary>
    private static async Task<string> TryConnectPrinterAsync(
        PowerShellRunner runner,
        string target,
        CancellationToken cancellationToken)
    {
        var escaped = Escape(target);
        var script = $$"""
            $target = '{{escaped}}'
            $view = & net view "\\$target" 2>&1 | Out-String
            if ($LASTEXITCODE -ne 0) {
                "无法枚举对方的共享（net view 退出代码 $LASTEXITCODE），跳过连接尝试。"
                $view
                exit 0
            }

            "对方可见的共享："
            $view

            # 从共享列表里挑出"打印"类型的共享名
            $printerShares = @()
            foreach ($line in ($view -split "`n")) {
                $t = $line.Trim()
                if (-not $t) { continue }
                if ($t -match '^-+' -or $t -match '共享名|Share name|命令成功完成|The command completed') { continue }
                $parts = $t -split '\s+'
                if ($parts.Count -ge 2 -and ($parts[1] -match '打印|Print')) { $printerShares += $parts[0] }
            }

            if ($printerShares.Count -eq 0) {
                "没有从共享列表里识别出打印机共享，请手动执行下面的命令并把错误发给我们："
                '    Add-Printer -ConnectionName "\\<对方电脑>\<共享名>"'
                exit 0
            }

            "识别到的打印机共享：" + ($printerShares -join ', ')
            foreach ($share in $printerShares) {
                $name = "\\$target\$share"
                "尝试连接: $name"
                try {
                    Add-Printer -ConnectionName $name -ErrorAction Stop
                    "结果: 连接成功，已添加该打印机。"
                    break
                } catch {
                    "结果: 连接失败。错误信息: $($_.Exception.Message)"
                    try { "HResult: 0x{0:X8}" -f $_.Exception.InnerException.HResult } catch { }
                    try { "内层错误: $($_.Exception.InnerException.Message)" } catch { }
                }
            }
            """;

        var result = await runner.RunAsync(script, cancellationToken, $"尝试连接 {target} 上的共享打印机", 180_000)
            .ConfigureAwait(false);

        var text = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            text.AppendLine(result.StandardOutput.TrimEnd());
        }

        if (!result.Succeeded)
        {
            text.AppendLine($"[连接尝试脚本未正常结束：退出代码 {result.ExitCode}；{result.ErrorSummary}]");
        }

        return text.ToString();
    }

    /// <summary>基于系统快照做自动判断。</summary>
    private static IReadOnlyList<string> Analyze(DiagnosticOptions options, SystemSnapshot snapshot)
    {
        var notes = new List<string>();

        if (snapshot.Build > 0 && snapshot.Build < 22000)
        {
            notes.Add($"本机是 Windows 10（内部版本 {snapshot.Build}）：Windows 11 客户端对共享名与打印策略更敏感，建议共享名保持简短英文。");
        }

        if (snapshot.ProtectedPrintMode == 1)
        {
            notes.Add("受保护的打印模式已启用：它只允许 IPP 类驱动，传统共享打印机将无法连接，建议关闭。");
        }

        if (snapshot.PrintRpcUseNamedPipe == 1)
        {
            notes.Add("打印 RPC 被限制为命名管道（RpcUseNamedPipeProtocol=1）：建议改为允许 RPC over TCP。");
        }

        if (snapshot.PrintRpcPrivacyEnabled != 0)
        {
            notes.Add("RpcAuthnLevelPrivacyEnabled 不是 0：这是 0x0000011b / 0x00000709 的常见原因，建议设为 0。");
        }

        if (snapshot.RestrictDriverInstallationToAdministrators != 0)
        {
            notes.Add("打印机驱动安装仍限制为管理员：普通用户连接共享打印机可能失败（0x00000740）。");
        }

        foreach (var printer in snapshot.Printers.Where(p => p.Shared))
        {
            var share = printer.ShareName;
            if (string.IsNullOrWhiteSpace(share))
            {
                notes.Add($"打印机【{printer.Name}】没有共享名，客户端无法连接。");
                continue;
            }

            var badChars = share.Any(c => c > 127 || char.IsWhiteSpace(c) || c is '(' or ')' or '+');
            if (share.Length > 32)
            {
                notes.Add($"共享名【{share}】长度为 {share.Length}，超过 32 个字符：部分 Windows 11 客户端会报 0x00000709，建议改成简短英文名。");
            }
            else if (badChars)
            {
                notes.Add($"共享名【{share}】含空格 / 中文 / 括号等字符：Windows 11 客户端报 0x00000709（打印机名称无效）的常见原因，建议改成纯英文名。");
            }
        }

        if (options.Role == RepairRole.Provider && snapshot.Printers.Count(p => p.Shared) == 0)
        {
            notes.Add("本机没有已共享的打印机：请先在【打印机属性 → 共享】里勾选共享。");
        }

        if (options.Role == RepairRole.Consumer)
        {
            if (string.IsNullOrWhiteSpace(options.TargetHost))
            {
                notes.Add("没有填写对方电脑的名称或 IP：名称解析、连通性和连接尝试都无法进行，建议重新生成报告时填上。");
            }

            if (snapshot.RemotePrinters.Count == 0)
            {
                notes.Add("本机当前没有已连接的共享打印机（连接成功后这里会显示）。");
            }
        }

        return notes;
    }

    private static string? FirstComment(string script)
    {
        var line = script
            .Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith('#'));
        return line is null ? null : line.TrimStart('#', ' ').Trim();
    }

    private static string Escape(string value) => value.Replace("'", "''");
}
