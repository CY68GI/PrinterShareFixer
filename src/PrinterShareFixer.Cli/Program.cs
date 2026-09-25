using System.Text;
using PrinterShareFixer.Core;
using PrinterShareFixer.Core.Models;
using PrinterShareFixer.Core.Profiles;
using PrinterShareFixer.Core.Runtime;

namespace PrinterShareFixer.Cli;

/// <summary>
/// 命令行验证工具：可以在不打开界面的情况下检测状态、查看修复计划、执行修复。
/// 用法：psfix detect | plan win11 | run win10 --yes
/// 与 WinUI 3 界面共用 PrinterShareFixer.Core 中的同一套修复逻辑。
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
        try
        {
            return command switch
            {
                "detect" => Detect(args),
                "plan" => Plan(args),
                "run" => await RunAsync(args).ConfigureAwait(false),
                "version" => Version(args),
                "help" or "-h" or "--help" => Help(),
                _ => Unknown(command),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"执行失败：{ex.Message}");
            return 1;
        }
    }

    private static int Help()
    {
        Console.WriteLine("""
            打印机共享修复工具 - 命令行版

            psfix detect                     检测当前机器的打印机共享相关状态（只读）
            psfix version [--markdown]       显示版本号与各版本更新内容（--markdown 输出 GitHub 用的 Markdown）
            psfix plan <方案>                打印修复方案包含的步骤与等价命令
            psfix run <方案> [--yes] [--target <电脑名或IP>] [--opt key=value ...]
                                             执行修复（默认只做预演，加 --yes 才真正执行，需要管理员权限）

            方案（本机角色 × 本机系统）：
              win10-provider                   本机接有打印机，为 Windows 10 电脑开放共享
              win11-provider                   本机接有打印机，为 Windows 11 电脑开放共享
              win10-consumer                   本机没有打印机，连接 Windows 10 电脑上的共享打印机
              win11-consumer                   本机没有打印机，连接 Windows 11 电脑上的共享打印机
              （只写 win10 / win11 时按服务端处理，兼容旧用法）

            其它参数：
              --role provider|consumer         detect 时按角色生成检测项
              --target <电脑名或IP>            客户端模式下要连接的电脑，用于连通性测试

            高级选项（--opt）：
              backup=true|false              修复前备份注册表（默认 true）
              passwordless=true|false        关闭密码保护的共享（默认 true）
              guest=true|false               允许不安全来宾登录（默认 true）
              signing=true|false             取消 SMB 签名强制（默认 true）
              rpc-dynamic=true|false         放行 RPC 动态端口（默认 true）
              restart=true|false             修复后重启服务（默认 true）
              clean-cache=true|false         清理失效打印缓存与卡住的队列（默认 true，客户端）
              smb1=true|false                启用 SMB1（默认 false，仅 Windows 10）
              wpp=true|false                 关闭受保护的打印模式（默认 false，仅 Windows 11）
            """);
        return 0;
    }

    private static int Version(string[] args)
    {
        if (args.Any(arg => arg.Equals("--markdown", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("<!-- 本文件由 tools/update-changelog.ps1 自动生成，内容来自 src/PrinterShareFixer.Core/AppInfo.cs -->");
            Console.WriteLine();
            Console.WriteLine("# 更新日志");
            Console.WriteLine();
            foreach (var note in AppInfo.ReleaseNotes)
            {
                Console.WriteLine($"## [{note.Version}] - {note.Date}");
                Console.WriteLine();
                foreach (var line in note.Highlights)
                {
                    Console.WriteLine($"- {line}");
                }

                Console.WriteLine();
            }

            return 0;
        }

        Console.WriteLine($"{AppInfo.ProductName}  v{AppInfo.Version}");
        Console.WriteLine();
        foreach (var note in AppInfo.ReleaseNotes)
        {
            Console.WriteLine($"v{note.Version}（{note.Date}）");
            foreach (var line in note.Highlights)
            {
                Console.WriteLine($"  · {line}");
            }

            Console.WriteLine();
        }

        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"未知命令：{command}");
        Help();
        return 1;
    }

    private static int Detect(string[] args)
    {
        Console.WriteLine("正在读取系统状态…");
        Console.WriteLine();

        var verbose = args.Contains("--verbose", StringComparer.OrdinalIgnoreCase);
        using var log = new FileLogSink(mirror: verbose ? line => Console.WriteLine(line) : null);
        var snapshot = SystemSnapshot.Load(log);
        var role = RepairRoleExtensions.ParseRole(GetOption(args, "--role"));

        Console.WriteLine($"系统：{snapshot.OsSummary}");
        Console.WriteLine($"管理员：{(snapshot.IsElevated ? "是" : "否")}");
        Console.WriteLine($"角色：{role.ToDisplayName()}（{role.ToActionText()}）");
        Console.WriteLine($"推荐方案：{RepairProfiles.Get(role, RepairProfiles.RecommendOsKey(snapshot.Build)).Key}");
        Console.WriteLine();

        foreach (var group in snapshot.ToDetectionItems(role).GroupBy(i => i.Category))
        {
            Console.WriteLine($"[{group.Key}]");
            foreach (var item in group)
            {
                var marker = item.Status switch
                {
                    DetectionStatus.Ok => "✔",
                    DetectionStatus.Warning => "!",
                    DetectionStatus.Problem => "✘",
                    _ => "·",
                };

                Console.WriteLine($"  {marker} {item.Title}");
                Console.WriteLine($"      {item.Detail}");
            }

            Console.WriteLine();
        }

        Console.WriteLine($"日志文件：{log.FilePath}");
        return 0;
    }

    private static int Plan(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("请指定方案：plan win10 或 plan win11");
            return 1;
        }

        var profile = RepairProfiles.Get(args[1]);
        Console.WriteLine($"修复方案：{profile.DisplayName}");
        Console.WriteLine(profile.Description);
        Console.WriteLine();

        var index = 1;
        foreach (var step in profile.Steps)
        {
            var option = step.OptionKey is null ? string.Empty : $"（选项 {step.OptionKey}，默认按设置执行）";
            Console.WriteLine($"{index++,2}. {step.Title}{option}");
            if (step.Description is { Length: > 0 })
            {
                Console.WriteLine($"    {step.Description}");
            }

            foreach (var command in step.Commands)
            {
                Console.WriteLine($"      $ {command}");
            }

            Console.WriteLine();
        }

        return 0;
    }

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("请指定方案：run win10 或 run win11");
            return 1;
        }

        var profile = RepairProfiles.Get(args[1]);
        var options = BuildOptions(args);
        var confirmed = args.Contains("--yes", StringComparer.OrdinalIgnoreCase);

        if (!confirmed)
        {
            Console.WriteLine("预演模式：以下步骤将会执行，实际不会修改系统。确认无误后加 --yes 执行。");
            Console.WriteLine();
            return Plan(["plan", profile.Key]);
        }

        using var log = new FileLogSink(mirror: line => Console.WriteLine(line));
        var engine = new RepairEngine(log);
        var progress = new Progress<StepUpdate>(update =>
        {
            var symbol = update.State switch
            {
                StepState.Running => "…",
                StepState.Succeeded => "✔",
                StepState.Warning => "!",
                StepState.Failed => "✘",
                StepState.Skipped => "-",
                _ => "·",
            };

            if (update.State != StepState.Pending)
            {
                Console.WriteLine($"  {symbol} {update.Step.Title}：{update.Message}");
            }
        });

        var report = await engine.RunAsync(profile, options, progress).ConfigureAwait(false);
        Console.WriteLine();
        Console.WriteLine($"完成：成功 {report.SucceededCount}，警告 {report.WarningCount}，失败 {report.FailedCount}，跳过 {report.SkippedCount}");
        Console.WriteLine($"日志：{report.LogFile}");
        if (report.HasFailures)
        {
            Console.WriteLine("存在失败项，请查看日志中的详细信息，必要时用备份目录中的 .reg 文件回滚。");
        }

        return report.HasFailures ? 2 : 0;
    }

    private static RepairOptions BuildOptions(string[] args)
    {
        var options = new RepairOptions();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!args[i].Equals("--opt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var pair = args[i + 1].Split('=', 2);
            if (pair.Length != 2 || !bool.TryParse(pair[1], out var value))
            {
                continue;
            }

            switch (pair[0].ToLowerInvariant())
            {
                case "backup":
                    options.BackupRegistry = value;
                    break;
                case "passwordless":
                    options.DisablePasswordProtectedSharing = value;
                    break;
                case "guest":
                    options.AllowInsecureGuestLogons = value;
                    break;
                case "signing":
                    options.DisableSmbSigningRequirement = value;
                    break;
                case "rpc-dynamic":
                    options.AllowRpcDynamicPorts = value;
                    break;
                case "restart":
                    options.RestartServicesAfterFix = value;
                    break;
                case "smb1":
                    options.EnableSmb1 = value;
                    break;
                case "wpp":
                    options.DisableProtectedPrintMode = value;
                    break;
                case "anyremote":
                    options.AllowAnyRemoteAddress = value;
                    break;
                case "clean-cache":
                    options.ClearStalePrintCache = value;
                    break;
            }
        }

        options.TargetHost = GetOption(args, "--target");
        return options;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
