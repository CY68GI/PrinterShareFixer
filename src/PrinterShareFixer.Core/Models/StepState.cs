namespace PrinterShareFixer.Core.Models;

/// <summary>修复步骤的执行状态。</summary>
public enum StepState
{
    Pending,
    Running,
    Succeeded,
    Warning,
    Failed,
    Skipped,
    Info,
}
