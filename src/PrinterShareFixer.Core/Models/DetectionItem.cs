namespace PrinterShareFixer.Core.Models;

public enum DetectionStatus
{
    Ok,
    Warning,
    Problem,
    Unknown,
}

/// <summary>检测结果中的一条记录。</summary>
public sealed record DetectionItem(
    string Category,
    string Title,
    DetectionStatus Status,
    string Detail);
