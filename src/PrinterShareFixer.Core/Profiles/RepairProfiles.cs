using PrinterShareFixer.Core.Models;

namespace PrinterShareFixer.Core.Profiles;

/// <summary>内置的四套修复方案：本机角色（有打印机 / 没有打印机）× 本机系统（Win10 / Win11）。</summary>
public static class RepairProfiles
{
    private static readonly Lazy<IReadOnlyList<RepairProfile>> AllLazy = new(() =>
    [
        RepairProfileFactory.Create(RepairRole.Provider, "win10"),
        RepairProfileFactory.Create(RepairRole.Provider, "win11"),
        RepairProfileFactory.Create(RepairRole.Consumer, "win10"),
        RepairProfileFactory.Create(RepairRole.Consumer, "win11"),
    ]);

    public static IReadOnlyList<RepairProfile> All => AllLazy.Value;

    public static RepairProfile Windows10 => Get(RepairRole.Provider, "win10");

    public static RepairProfile Windows11 => Get(RepairRole.Provider, "win11");

    public static RepairProfile Get(RepairRole role, string osKey) =>
        All.First(p => p.Role == role && p.OsKey.Equals(NormalizeOs(osKey), StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<RepairProfile> ForRole(RepairRole role) => All.Where(p => p.Role == role).ToList();

    /// <summary>支持 "win10-provider"、"win10-consumer"，也兼容旧的 "win10"（按服务端处理）。</summary>
    public static RepairProfile Get(string key)
    {
        var normalized = key.Trim().ToLowerInvariant();
        var role = normalized.Contains("consumer") || normalized.Contains("client")
            ? RepairRole.Consumer
            : RepairRole.Provider;
        return Get(role, normalized);
    }

    /// <summary>根据本机系统内部版本号推荐系统键。</summary>
    public static string RecommendOsKey(int buildNumber) => buildNumber >= 22000 ? "win11" : "win10";

    private static string NormalizeOs(string key)
    {
        var normalized = key.Trim().ToLowerInvariant();
        return normalized.Contains("11") ? "win11" : "win10";
    }
}
