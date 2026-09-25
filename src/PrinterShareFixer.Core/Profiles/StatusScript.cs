using PrinterShareFixer.Core.Models;
using PrinterShareFixer.Core.Runtime;

namespace PrinterShareFixer.Core.Profiles;

/// <summary>PowerShell 脚本用 "状态|名称|说明" 行回报结果。</summary>
internal sealed record StatusLine(string Status, string Name, string Message)
{
    public bool IsProblem => Status is "ERR" or "MISSING";

    public bool IsChange => Status is "FIXED" or "CREATED" or "CONFIG" or "STARTED";

    public string Describe() =>
        $"[{Translate(Status)}] {Name}{(string.IsNullOrWhiteSpace(Message) ? string.Empty : " - " + Message)}";

    private static string Translate(string status) => status switch
    {
        "OK" => "已符合",
        "FIXED" => "已修复",
        "CREATED" => "已创建",
        "CONFIG" => "已调整",
        "STARTED" => "已启动",
        "WARN" => "警告",
        "ERR" => "失败",
        "MISSING" => "缺失",
        _ => status,
    };
}

internal static class StatusScript
{
    public static IReadOnlyList<StatusLine> Parse(ProcessResult result) => result.OutputLines
        .Select(line => line.Split('|'))
        .Where(parts => parts.Length >= 2)
        .Select(parts => new StatusLine(
            parts[0].Trim(),
            parts[1].Trim(),
            parts.Length > 2 ? string.Join("|", parts.Skip(2)).Trim() : string.Empty))
        .ToList();

    /// <summary>把脚本输出汇总成一个步骤结果。</summary>
    public static StepResult Summarize(
        string okMessage,
        IReadOnlyList<StatusLine> lines,
        IReadOnlyCollection<string>? criticalNames = null,
        string? emptyMessage = null)
    {
        if (lines.Count == 0)
        {
            return StepResult.Fail(emptyMessage ?? "脚本没有返回任何结果，请查看日志。");
        }

        var details = lines.Select(l => l.Describe()).ToList();
        var problems = lines.Where(l => l.IsProblem).ToList();
        var changes = lines.Count(l => l.IsChange);

        if (problems.Count == 0)
        {
            var suffix = changes > 0 ? $"（已调整 {changes} 项）" : string.Empty;
            return StepResult.Ok($"{okMessage}{suffix}", [.. details]);
        }

        var critical = criticalNames is null
            ? []
            : problems.Where(p => criticalNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase)).ToList();

        var summary = $"{okMessage}：{problems.Count} 项未完成";
        return critical.Count > 0
            ? StepResult.Fail(summary, [.. details])
            : StepResult.Warn(summary, [.. details]);
    }
}
