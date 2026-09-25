namespace PrinterShareFixer.Core.Models;

/// <summary>本机在打印机共享里扮演的角色，决定使用哪套修复步骤。</summary>
public enum RepairRole
{
    /// <summary>本机接着打印机，目标是让别的电脑能连上来（服务端）。</summary>
    Provider,

    /// <summary>本机没有打印机，目标是能连上别的电脑共享的打印机（客户端）。</summary>
    Consumer,
}

public static class RepairRoleExtensions
{
    public static string ToKey(this RepairRole role) => role == RepairRole.Provider ? "provider" : "consumer";

    public static string ToDisplayName(this RepairRole role) =>
        role == RepairRole.Provider ? "本机接有打印机" : "本机没有打印机";

    public static string ToActionText(this RepairRole role) =>
        role == RepairRole.Provider ? "让别的电脑连上我的打印机" : "连接别人电脑上的共享打印机";

    public static string ToShortHint(this RepairRole role) =>
        role == RepairRole.Provider ? "开放共享" : "连接共享";

    public static RepairRole ParseRole(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "consumer" or "client" or "none" or "noprinter" => RepairRole.Consumer,
        _ => RepairRole.Provider,
    };
}
