using PrinterShareFixer.Core.Models;
using PrinterShareFixer.Core.Runtime;

namespace PrinterShareFixer.Core.Profiles;

/// <summary>只在某个角色下才有意义的修复步骤。</summary>
internal static class RoleSteps
{
    /// <summary>服务端：确认本机确实共享了打印机（只做检查与提示）。</summary>
    public static RepairStep ProviderShareCheck()
    {
        return new RepairStep
        {
            Id = "provider.share",
            Title = "确认本机已共享打印机",
            Description = "本工具只负责打通连接，需要先在“打印机属性 → 共享”里勾选共享，别的电脑才能看到这台打印机。",
            RequiresElevation = false,
            Commands =
            [
                "Get-Printer | Where-Object { $_.Shared -eq $true }",
                "Get-SmbShare | Where-Object { -not $_.Name.EndsWith('$') }",
            ],
            Handler = async (context, ct) =>
            {
                const string script = """
                    $out = New-Object System.Collections.Generic.List[string]
                    $printers = @(Get-Printer -ErrorAction SilentlyContinue | Where-Object { $_.Shared -eq $true })
                    if ($printers.Count -eq 0) {
                        $out.Add("ERR|本机共享打印机|没有检测到已共享的打印机，请在“打印机属性 → 共享”里勾选共享")
                    } else {
                        foreach ($p in $printers) {
                            $out.Add("OK|$($p.Name)|共享名 $($p.ShareName)，驱动 $($p.DriverName)")
                        }
                    }
                    $folders = @(Get-SmbShare -ErrorAction SilentlyContinue | Where-Object { -not $_.Name.EndsWith('$') })
                    foreach ($s in $folders) {
                        $out.Add("OK|共享文件夹 $($s.Name)|$($s.Path)")
                    }
                    $out -join "`n"
                    """;

                var result = await context.PowerShell.RunAsync(script, ct, "检查本机共享情况");
                return StatusScript.Summarize("共享状态检查完成", StatusScript.Parse(result));
            },
        };
    }

    /// <summary>客户端：允许访问未启用密码保护的共享（策略层，Windows 11 24H2 需要）。</summary>
    public static RepairStep ConsumerGuestPolicy()
    {
        return new RepairStep
        {
            Id = "consumer.guest.policy",
            Title = "允许访问无密码保护的共享（策略层）",
            Description = "Windows 11 24H2 起组策略默认禁止来宾访问共享，这一项的优先级高于 SMB 客户端开关。",
            OptionKey = "insecure-guest",
            Commands =
            [
                "reg add HKLM/SOFTWARE/Policies/Microsoft/Windows/LanmanWorkstation /v AllowInsecureGuestAuth /t REG_DWORD /d 1 /f",
                "reg add HKLM/SYSTEM/CurrentControlSet/Services/LanmanWorkstation/Parameters /v AllowInsecureGuestAuth /t REG_DWORD /d 1 /f",
            ],
            Handler = (context, _) =>
            {
                var changes = new List<string>();
                var policy = context.Registry.SetDword(RegistryTools.LanmanWorkstationPolicy, "AllowInsecureGuestAuth", 1);
                if (policy is not null)
                {
                    changes.Add(policy);
                }

                var parameters = context.Registry.SetDword(RegistryTools.LanmanWorkstation, "AllowInsecureGuestAuth", 1);
                if (parameters is not null)
                {
                    changes.Add(parameters);
                }

                var message = changes.Count == 0
                    ? "已经是允许来宾访问共享的状态。"
                    : $"已允许访问无密码保护的共享（{changes.Count} 项）。";
                return Task.FromResult(StepResult.Ok(message, [.. changes]));
            },
        };
    }

    /// <summary>客户端：清理失效的打印缓存与卡住的打印队列。</summary>
    public static RepairStep ConsumerCleanStale()
    {
        return new RepairStep
        {
            Id = "consumer.clean",
            Title = "清理失效的打印缓存与卡住的打印队列",
            Description = "删除卡死的打印任务和客户端缓存的旧服务端信息，解决“能连上但打不开、提示找不到打印机”一类问题。",
            OptionKey = "clean-cache",
            Commands =
            [
                "Restart-Service -Name Spooler -Force",
                "删除 C:/Windows/System32/spool/PRINTERS 下卡住的打印任务",
                "reg delete HKLM/.../Client Side Rendering Print Provider/Servers /f",
            ],
            Handler = async (context, ct) =>
            {
                var removed = context.Registry.DeleteSubKeys(RegistryTools.ClientSideRenderingServers);
                var details = new List<string>();

                details.Add(removed.Count > 0
                    ? $"已清理 {removed.Count} 条缓存的远程打印机信息"
                    : "没有缓存的远程打印机信息需要清理");

                const string script = """
                    $out = New-Object System.Collections.Generic.List[string]
                    $spool = Join-Path $env:SystemRoot 'System32\spool\PRINTERS'
                    try {
                        Restart-Service -Name Spooler -Force -ErrorAction Stop
                        $out.Add("OK|打印后台处理程序|已重启")
                    } catch {
                        try {
                            Start-Service -Name Spooler -ErrorAction Stop
                            $out.Add("STARTED|打印后台处理程序|已启动")
                        } catch {
                            $out.Add("ERR|打印后台处理程序|$($_.Exception.Message)")
                        }
                    }
                    if (Test-Path -LiteralPath $spool) {
                        $files = @(Get-ChildItem -LiteralPath $spool -File -ErrorAction SilentlyContinue)
                        if ($files.Count -eq 0) {
                            $out.Add("OK|打印队列|没有卡住的任务")
                        } else {
                            $failed = 0
                            foreach ($file in $files) {
                                try { Remove-Item -LiteralPath $file.FullName -Force -ErrorAction Stop } catch { $failed++ }
                            }
                            if ($failed -eq 0) {
                                $out.Add("FIXED|打印队列|已清理 $($files.Count) 个卡住的任务")
                            } else {
                                $out.Add("WARN|打印队列|$($files.Count) 个任务中有 $failed 个未能删除")
                            }
                        }
                    } else {
                        $out.Add("WARN|打印队列|未找到打印队列目录 $spool")
                    }
                    $out -join "`n"
                    """;

                var result = await context.PowerShell.RunAsync(script, ct, "清理打印缓存与队列");
                var lines = StatusScript.Parse(result);
                details.AddRange(lines.Select(l => l.Describe()));

                var problems = lines.Count(l => l.IsProblem);
                return problems == 0
                    ? StepResult.Ok("打印缓存清理完成", [.. details])
                    : StepResult.Warn($"打印缓存清理完成，{problems} 项需要注意", [.. details]);
            },
        };
    }

    /// <summary>客户端：测试到目标电脑的连通性（ping / TCP 端口 / net view）。</summary>
    public static RepairStep ConsumerConnectivity()
    {
        return new RepairStep
        {
            Id = "consumer.connectivity",
            Title = "测试到目标电脑的连通性",
            Description = "用 ping、TCP 445 / 135 和 net view 判断问题出在网络、防火墙还是共享权限。",
            RequiresElevation = false,
            Commands =
            [
                "Test-Connection -ComputerName <目标电脑> -Count 2",
                "Test-NetConnection -ComputerName <目标电脑> -Port 445",
                @"net view \\<目标电脑>",
            ],
            Handler = async (context, ct) =>
            {
                var target = context.Options.TargetHost?.Trim();
                if (string.IsNullOrWhiteSpace(target))
                {
                    return StepResult.Note(
                        "没有填写目标电脑名称或 IP，已跳过连通性测试。",
                        "在界面上填入接打印机的那台电脑的名称或 IP（例如 PC-01 或 192.168.1.10），重新修复时会自动测试。");
                }

                var script = ConnectivityScriptTemplate.Replace("__TARGET__", target.Replace("'", "''"));
                var result = await context.PowerShell.RunAsync(script, ct, $"测试到 {target} 的连通性", 120_000);
                return StatusScript.Summarize("连通性测试完成", StatusScript.Parse(result));
            },
        };
    }

    private const string ConnectivityScriptTemplate = """
        $out = New-Object System.Collections.Generic.List[string]
        $target = '__TARGET__'
        $isIp = $false
        try { $null = [System.Net.IPAddress]::Parse($target); $isIp = $true } catch { }
        if (-not $isIp) {
            try {
                $ips = [System.Net.Dns]::GetHostAddresses($target) | ForEach-Object { $_.IPAddressToString }
                $out.Add("OK|名称解析|$target -> $($ips -join ', ')")
            } catch {
                $out.Add("WARN|名称解析|无法解析 $target，可以先改用 IP 地址试试（$($_.Exception.Message)）")
            }
        }
        try {
            if (Test-Connection -ComputerName $target -Count 2 -Quiet -ErrorAction Stop) {
                $out.Add("OK|ping|$target 可以 ping 通")
            } else {
                $out.Add("WARN|ping|ping 不通；若两台电脑在不同网段，ICMP 被禁用也可能属正常，请看端口测试结果")
            }
        } catch {
            $out.Add("WARN|ping|$($_.Exception.Message)")
        }
        foreach ($port in @(445, 135)) {
            $ok = $false
            try {
                $ok = Test-NetConnection -ComputerName $target -Port $port -InformationLevel Quiet -WarningAction SilentlyContinue -ErrorAction Stop
            } catch {
                $ok = $false
            }
            if ($ok) {
                $out.Add("OK|TCP $port|端口可达")
            } elseif ($port -eq 445) {
                $out.Add("ERR|TCP 445|不通：对方未放行“文件和打印机共享”，或被安全软件、路由器拦截")
            } else {
                $out.Add("ERR|TCP 135|不通：对方的 RPC 端口被拦截，连接共享打印机需要它")
            }
        }
        try {
            $view = & net view "\\$target" 2>&1 | Out-String
            if ($LASTEXITCODE -eq 0 -and $view -match '\S') {
                $out.Add("OK|共享列表|$target 上有可见的共享资源")
            } else {
                $out.Add("ERR|共享列表|无法枚举共享：$($view.Trim())")
            }
        } catch {
            $out.Add("ERR|共享列表|$($_.Exception.Message)")
        }
        $out -join "`n"
        """;
}
