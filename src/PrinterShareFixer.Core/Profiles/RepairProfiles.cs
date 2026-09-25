using PrinterShareFixer.Core.Models;

namespace PrinterShareFixer.Core.Profiles;

/// <summary>内置的两个修复方案。</summary>
public static class RepairProfiles
{
    private static readonly Lazy<RepairProfile> Windows10Lazy = new(Windows10Profile.Create);

    private static readonly Lazy<RepairProfile> Windows11Lazy = new(Windows11Profile.Create);

    public static RepairProfile Windows10 => Windows10Lazy.Value;

    public static RepairProfile Windows11 => Windows11Lazy.Value;

    public static IReadOnlyList<RepairProfile> All => [Windows10, Windows11];

    public static RepairProfile Get(string key) => key.ToLowerInvariant() switch
    {
        "win10" or "windows10" or "10" => Windows10,
        "win11" or "windows11" or "11" => Windows11,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "未知的修复方案，请使用 win10 或 win11。"),
    };

    /// <summary>根据系统内部版本号推荐方案。</summary>
    public static RepairProfile Recommend(int buildNumber) => buildNumber >= 22000 ? Windows11 : Windows10;
}
