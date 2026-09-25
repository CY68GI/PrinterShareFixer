namespace PrinterShareFixer.Core.Models;

/// <summary>界面上的高级选项。</summary>
public sealed class RepairOptions
{
    /// <summary>启用 SMB1 协议（仅 Windows 10 方案有效）。</summary>
    public bool EnableSmb1 { get; set; }

    /// <summary>允许不安全的来宾登录，配合关闭密码保护的共享。</summary>
    public bool AllowInsecureGuestLogons { get; set; } = true;

    /// <summary>关闭“密码保护的共享”（LSA 策略）。</summary>
    public bool DisablePasswordProtectedSharing { get; set; } = true;

    /// <summary>取消 SMB 签名强制，兼容老设备。</summary>
    public bool DisableSmbSigningRequirement { get; set; } = true;

    /// <summary>放行打印后台处理程序使用的 RPC 动态端口范围。</summary>
    public bool AllowRpcDynamicPorts { get; set; } = true;

    /// <summary>防火墙规则允许任意远程地址（默认仅本地子网）。</summary>
    public bool AllowAnyRemoteAddress { get; set; }

    /// <summary>修复结束后重启相关服务。</summary>
    public bool RestartServicesAfterFix { get; set; } = true;

    /// <summary>修复前备份相关注册表项。</summary>
    public bool BackupRegistry { get; set; } = true;

    /// <summary>关闭 Windows 受保护的打印模式（仅 Windows 11 24H2+）。</summary>
    public bool DisableProtectedPrintMode { get; set; }

    /// <summary>客户端模式：需要连接的打印机所在电脑名称或 IP（可留空）。</summary>
    public string? TargetHost { get; set; }

    /// <summary>客户端模式：清理失效的打印缓存与卡住的打印队列。</summary>
    public bool ClearStalePrintCache { get; set; } = true;

    /// <summary>根据选项键判断某个步骤是否应该执行。</summary>
    public bool IsStepEnabled(string? optionKey) => optionKey switch
    {
        null or "" => true,
        "smb1" => EnableSmb1,
        "insecure-guest" => AllowInsecureGuestLogons,
        "passwordless" => DisablePasswordProtectedSharing,
        "smb-signing" => DisableSmbSigningRequirement,
        "rpc-dynamic" => AllowRpcDynamicPorts,
        "restart-services" => RestartServicesAfterFix,
        "backup" => BackupRegistry,
        "wpp" => DisableProtectedPrintMode,
        "clean-cache" => ClearStalePrintCache,
        _ => true,
    };
}
