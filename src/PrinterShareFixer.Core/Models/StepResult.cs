namespace PrinterShareFixer.Core.Models;

/// <summary>单个修复步骤的执行结果。</summary>
public sealed record StepResult(StepState State, string Message, IReadOnlyList<string>? Details = null)
{
    public static StepResult Ok(string message, params string[] details) =>
        new(StepState.Succeeded, message, details.Length == 0 ? null : details);

    public static StepResult Warn(string message, params string[] details) =>
        new(StepState.Warning, message, details.Length == 0 ? null : details);

    public static StepResult Fail(string message, params string[] details) =>
        new(StepState.Failed, message, details.Length == 0 ? null : details);

    public static StepResult Skip(string message) => new(StepState.Skipped, message);

    public static StepResult Note(string message, params string[] details) =>
        new(StepState.Info, message, details.Length == 0 ? null : details);
}

/// <summary>修复过程中上报给界面的进度信息。</summary>
public sealed record StepUpdate(
    int Index,
    RepairStep Step,
    StepState State,
    string Message,
    IReadOnlyList<string>? Details = null);

/// <summary>一次修复的汇总结果。</summary>
public sealed class RepairReport
{
    public required string ProfileKey { get; init; }

    public required string ProfileName { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset FinishedAt { get; init; }

    public required IReadOnlyList<StepUpdate> Updates { get; init; }

    public string? LogFile { get; init; }

    public int SucceededCount => Updates.Count(u => u.State == StepState.Succeeded);

    public int WarningCount => Updates.Count(u => u.State == StepState.Warning);

    public int FailedCount => Updates.Count(u => u.State == StepState.Failed);

    public int SkippedCount => Updates.Count(u => u.State == StepState.Skipped);

    public bool HasFailures => FailedCount > 0;
}
