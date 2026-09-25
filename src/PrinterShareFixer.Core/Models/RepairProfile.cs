namespace PrinterShareFixer.Core.Models;

/// <summary>针对某个 Windows 版本的修复方案。</summary>
public sealed class RepairProfile
{
    /// <summary>win10 / win11。</summary>
    public required string Key { get; init; }

    /// <summary>本机角色：服务端 / 客户端。</summary>
    public required RepairRole Role { get; init; }

    /// <summary>本机系统版本键：win10 / win11。</summary>
    public required string OsKey { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>按钮上的动作标题，例如“开放共享（让别的电脑连上来）”。</summary>
    public required string ActionTitle { get; init; }

    public required string Description { get; init; }

    public required IReadOnlyList<RepairStep> Steps { get; init; }
}
