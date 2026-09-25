using System.Reflection;

namespace PrinterShareFixer.Core;

/// <summary>一个版本的更新说明。</summary>
public sealed record ReleaseNote(string Version, string Date, IReadOnlyList<string> Highlights);

/// <summary>程序名称、版本号与各版本更新内容。</summary>
public static class AppInfo
{
    public const string ProductName = "打印机共享修复工具";

    private static readonly Lazy<string> VersionLazy = new(ResolveVersion, isThreadSafe: true);

    public static string Version => VersionLazy.Value;

    /// <summary>发布说明，最新的排在前面。</summary>
    public static IReadOnlyList<ReleaseNote> ReleaseNotes { get; } =
    [
        new("1.1.0", "2026-09-25",
        [
            "界面按角色拆成两个：本机接有打印机（开放共享）、本机没有打印机（连接共享）。",
            "新增“设置”对话框：查看版本号、每个版本的更新内容、日志与备份目录。",
            "客户端模式新增目标电脑连通性测试：ping、TCP 445、TCP 135、net view。",
            "客户端模式新增清理失效打印缓存与卡住的打印队列（打印任务卡死、0x0000007c 一类问题）。",
            "客户端模式新增 AllowInsecureGuestAuth 策略设置，解决 Windows 11 24H2 访问无密码共享被拒。",
            "服务清单按角色拆分：服务端保留 Server/网络发现，客户端只保留连接所需服务。",
        ]),
        new("1.0.0", "2026-09-24",
        [
            "首个版本：Windows 10 / Windows 11 两套一键修复方案。",
            "服务配置、网络位置、NetBIOS、防火墙放行、来宾访问、SMB 兼容性。",
            "修复 0x0000011b（打印 RPC 隐私认证）与 0x00000740（驱动安装限制）。",
            "Windows 11 24H2 专项：打印后台处理程序 RPC 传输、受保护的打印模式。",
            "系统状态检测、逐项进度显示、修复前注册表备份与详细日志。",
            "附带命令行工具 psfix 与应用图标。",
        ]),
    ];

    private static string ResolveVersion()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                var plus = informational.IndexOf('+');
                return plus > 0 ? informational[..plus] : informational;
            }

            return assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        }
        catch
        {
            return "1.0.0";
        }
    }
}
