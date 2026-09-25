namespace PrinterShareFixer.Core.Models;

/// <summary>针对某个 Windows 版本的修复方案。</summary>
public sealed class RepairProfile
{
    /// <summary>win10 / win11。</summary>
    public required string Key { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public required IReadOnlyList<RepairStep> Steps { get; init; }
}
