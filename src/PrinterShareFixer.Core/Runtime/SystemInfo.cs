using Microsoft.Win32;

namespace PrinterShareFixer.Core.Runtime;

/// <summary>
/// 只读注册表就能拿到的系统信息（毫秒级返回，不启动 PowerShell）。
/// 修复步骤里凡是只需要“是不是 Win11 / 是不是 24H2”的地方都用它，避免拖慢主流程。
/// </summary>
public sealed record SystemInfo(string ProductName, string DisplayVersion, int Build, int Ubr, string Edition)
{
    public bool IsWindows11 => Build >= 22000;

    public bool IsWindows11_24H2OrLater => Build >= 26100;

    public string OsSummary => $"{ProductName} {DisplayVersion}（内部版本 {Build}.{Ubr}）".Trim();

    public static SystemInfo Read()
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");

            var product = key?.GetValue("ProductName")?.ToString() ?? "未知";
            var display = key?.GetValue("DisplayVersion")?.ToString()
                          ?? key?.GetValue("ReleaseId")?.ToString()
                          ?? string.Empty;
            var buildText = key?.GetValue("CurrentBuildNumber")?.ToString() ?? "0";
            var ubrText = key?.GetValue("UBR")?.ToString() ?? "0";
            var edition = key?.GetValue("EditionID")?.ToString() ?? string.Empty;

            _ = int.TryParse(buildText, out var build);
            _ = int.TryParse(ubrText, out var ubr);
            return new SystemInfo(product, display, build, ubr, edition);
        }
        catch
        {
            return new SystemInfo("未知", string.Empty, 0, 0, string.Empty);
        }
    }
}
