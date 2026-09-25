using Microsoft.Win32;

namespace PrinterShareFixer.Core.Runtime;

/// <summary>注册表读写助手（同时兼顾 64 位与 32 位视图）。</summary>
public sealed class RegistryTools(ILogSink log)
{
    public const string Lsa = @"SYSTEM\CurrentControlSet\Control\Lsa";
    public const string LanmanServer = @"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters";
    public const string LanmanWorkstation = @"SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters";
    public const string Print = @"SYSTEM\CurrentControlSet\Control\Print";
    public const string PoliciesSystem = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    public const string PrintersPolicy = @"SOFTWARE\Policies\Microsoft\Windows NT\Printers";
    public const string PointAndPrintPolicy = @"SOFTWARE\Policies\Microsoft\Windows NT\Printers\PointAndPrint";
    public const string PrintRpcPolicy = @"SOFTWARE\Policies\Microsoft\Windows NT\Printers\RPC";
    public const string WppPolicy = @"SOFTWARE\Policies\Microsoft\Windows NT\Printers\WPP";
    public const string LanmanWorkstationPolicy = @"SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation";
    public const string ClientSideRenderingServers =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Print\Providers\Client Side Rendering Print Provider\Servers";

    public static readonly string[] BackupKeys =
    [
        Lsa,
        LanmanServer,
        LanmanWorkstation,
        Print,
        PoliciesSystem,
        PrintersPolicy,
        PointAndPrintPolicy,
        PrintRpcPolicy,
        WppPolicy,
        LanmanWorkstationPolicy,
    ];

    public int? GetDword(string subKey, string valueName, RegistryView view = RegistryView.Registry64)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(valueName) switch
            {
                int i => i,
                long l => (int)l,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => null,
            };
        }
        catch (Exception ex)
        {
            log.Write($"读取注册表失败 {subKey}\\{valueName}：{ex.Message}");
            return null;
        }
    }

    public string? GetString(string subKey, string valueName, RegistryView view = RegistryView.Registry64)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(valueName)?.ToString();
        }
        catch (Exception ex)
        {
            log.Write($"读取注册表失败 {subKey}\\{valueName}：{ex.Message}");
            return null;
        }
    }

    /// <summary>写入 DWORD，返回“旧值 → 新值”的可读描述；值已一致时返回 null。</summary>
    public string? SetDword(string subKey, string valueName, int value, RegistryView view = RegistryView.Registry64)
    {
        var existing = GetDword(subKey, valueName, view);
        if (existing == value)
        {
            return null;
        }

        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
        using var key = baseKey.CreateSubKey(subKey, writable: true)
            ?? throw new InvalidOperationException($"无法创建注册表项 HKLM\\{subKey}");

        key.SetValue(valueName, value, RegistryValueKind.DWord);
        var description = $"HKLM\\{subKey}\\{valueName}: {Describe(existing)} → {value}";
        log.Write($"  注册表 {description}");
        return description;
    }

    public string? DeleteValue(string subKey, string valueName, RegistryView view = RegistryView.Registry64)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
        using var key = baseKey.OpenSubKey(subKey, writable: true);
        if (key?.GetValue(valueName) is null)
        {
            return null;
        }

        key.DeleteValue(valueName, throwOnMissingValue: false);
        var description = $"HKLM\\{subKey}\\{valueName}: 已删除";
        log.Write($"  注册表 {description}");
        return description;
    }

    /// <summary>删除某个注册表项下的所有子项，返回被删除的子项列表。</summary>
    public IReadOnlyList<string> DeleteSubKeys(string subKey, RegistryView view = RegistryView.Registry64)
    {
        var removed = new List<string>();
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = baseKey.OpenSubKey(subKey, writable: true);
            if (key is null)
            {
                return removed;
            }

            foreach (var name in key.GetSubKeyNames())
            {
                try
                {
                    key.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
                    removed.Add($@"HKLM\{subKey}\{name}");
                }
                catch (Exception ex)
                {
                    log.Write($"  删除注册表子项失败 {subKey}\\{name}：{ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            log.Write($"  读取注册表失败 {subKey}：{ex.Message}");
        }

        return removed;
    }

    /// <summary>用 reg.exe 导出注册表项，便于出问题时回滚。</summary>
    public (bool Ok, string Message) TryExportKey(string subKey, string directory)
    {
        var safeName = subKey.Replace('\\', '_');
        var target = Path.Combine(directory, $"HKLM_{safeName}.reg");
        var result = ProcessRunner.RunAsync(
                "reg.exe",
                ["export", $@"HKLM\{subKey}", target, "/y"],
                CancellationToken.None,
                60_000)
            .GetAwaiter()
            .GetResult();

        if (result.Succeeded && File.Exists(target))
        {
            return (true, target);
        }

        return (false, result.ErrorSummary);
    }

    private static string Describe(int? value) => value?.ToString() ?? "(未设置)";
}
