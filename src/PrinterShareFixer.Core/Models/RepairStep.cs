using PrinterShareFixer.Core.Runtime;

namespace PrinterShareFixer.Core.Models;

/// <summary>一个可执行的修复动作。</summary>
public sealed class RepairStep
{
    /// <summary>稳定标识，用于界面选项开关与日志。</summary>
    public required string Id { get; init; }

    /// <summary>界面显示的标题。</summary>
    public required string Title { get; init; }

    /// <summary>为什么需要这一步的简要说明。</summary>
    public string? Description { get; init; }

    /// <summary>关联的高级选项开关；为空表示始终执行。</summary>
    public string? OptionKey { get; init; }

    /// <summary>需要管理员权限才能执行。</summary>
    public bool RequiresElevation { get; init; } = true;

    /// <summary>该步骤失败时中止后续步骤（例如缺少管理员权限）。</summary>
    public bool AbortOnFailure { get; init; }

    /// <summary>等价的手工命令，便于用户审计与复制。</summary>
    public IReadOnlyList<string> Commands { get; init; } = [];

    public required Func<RepairContext, CancellationToken, Task<StepResult>> Handler { get; init; }
}
