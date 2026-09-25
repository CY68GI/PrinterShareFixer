using System.Diagnostics;
using System.Runtime.InteropServices;
using PrinterShareFixer.Core.Runtime;

namespace PrinterShareFixer.Core.Update;

/// <summary>更新模式（--apply-update）的请求参数。</summary>
public sealed record UpdaterRequest(
    string StagedAppDirectory,
    string TargetAppDirectory,
    int WaitProcessId,
    bool Relaunch);

/// <summary>
/// 真正执行替换的那部分：等主程序退出 → 复制新版本 → 备份旧版本 → 切换 → 重新启动。
/// 全过程写日志到 %ProgramData%\PrinterShareFixer\Logs\update-*.log，失败会回滚并弹提示。
/// </summary>
public static class UpdateApplier
{
    public static bool TryParse(string[] args, out UpdaterRequest? request)
    {
        request = null;
        string? staged = null;
        string? target = null;
        var waitPid = 0;
        var relaunch = true;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--apply-update":
                    if (i + 1 < args.Length) { staged = args[++i]; }
                    break;
                case "--target":
                    if (i + 1 < args.Length) { target = args[++i]; }
                    break;
                case "--wait-pid":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var pid)) { waitPid = pid; }
                    break;
                case "--no-relaunch":
                    relaunch = false;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(staged) || string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        request = new UpdaterRequest(staged, target, waitPid, relaunch);
        return true;
    }

    /// <summary>从已安装的程序里启动更新程序；用新版本目录里的 exe，避免自己正在被替换。</summary>
    public static Process StartUpdater(string stagedAppDirectory, string targetAppDirectory, bool relaunch)
    {
        var exe = Path.Combine(stagedAppDirectory, "PrinterShareFixer.exe");
        if (!File.Exists(exe))
        {
            throw new InvalidOperationException("暂存目录里没有 PrinterShareFixer.exe。");
        }

        var arguments =
            $"--apply-update \"{stagedAppDirectory}\" --target \"{targetAppDirectory}\" --wait-pid {Environment.ProcessId}" +
            (relaunch ? string.Empty : " --no-relaunch");

        var info = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = false,
            WorkingDirectory = stagedAppDirectory,
        };

        return Process.Start(info) ?? throw new InvalidOperationException("无法启动更新程序。");
    }

    public static int Apply(UpdaterRequest request)
    {
        var logPath = Path.Combine(AppPaths.EnsureLogDirectory(), $"update-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        void Log(string message)
        {
            try
            {
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch
            {
                // 日志失败不影响更新
            }
        }

        Log($"更新开始：暂存={request.StagedAppDirectory} 目标={request.TargetAppDirectory} 等待进程={request.WaitProcessId} 自动重启={request.Relaunch}");

        try
        {
            if (request.WaitProcessId > 0)
            {
                WaitForExit(request.WaitProcessId, TimeSpan.FromMinutes(3), Log);
            }

            var staged = Path.GetFullPath(request.StagedAppDirectory).TrimEnd(Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(request.TargetAppDirectory).TrimEnd(Path.DirectorySeparatorChar);

            if (!File.Exists(Path.Combine(staged, "PrinterShareFixer.exe")))
            {
                throw new InvalidOperationException("暂存目录里没有 PrinterShareFixer.exe。");
            }

            if (!Directory.Exists(target))
            {
                throw new InvalidOperationException($"目标目录不存在：{target}");
            }

            if (target.StartsWith(staged, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("暂存目录不能位于目标目录内部。");
            }

            var parent = Path.GetDirectoryName(target)
                ?? throw new InvalidOperationException("无法确定目标目录的上级目录。");
            var name = Path.GetFileName(target);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var incoming = Path.Combine(parent, $"{name}.new-{stamp}");
            var backup = Path.Combine(parent, $"{name}.old-{stamp}");

            Log($"复制新版本到 {incoming}");
            CopyDirectory(staged, incoming);

            Log($"备份当前版本到 {backup}");
            Directory.Move(target, backup);

            try
            {
                Log("切换到新版本");
                Directory.Move(incoming, target);
            }
            catch
            {
                Log("切换失败，正在回滚");
                if (!Directory.Exists(target) && Directory.Exists(backup))
                {
                    Directory.Move(backup, target);
                }

                throw;
            }

            CleanupOldBackups(parent, name, backup, Log);
            TryCleanStaging(staged, Log);

            if (request.Relaunch)
            {
                var exe = Path.Combine(target, "PrinterShareFixer.exe");
                Log($"重新启动程序：{exe}");
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = target });
            }

            Log("更新完成");
            return 0;
        }
        catch (Exception ex)
        {
            Log($"更新失败：{ex}");
            ShowMessage(
                $"自动更新失败：{ex.Message}\n\n" +
                "原来的程序目录没有被破坏，可以手动下载新版本解压覆盖。\n" +
                $"详细日志：{logPath}",
                error: true);
            return 1;
        }
    }

    private static void WaitForExit(int processId, TimeSpan timeout, Action<string> log)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            log($"等待主程序退出（PID {processId}）…");
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                log("等待超时，继续执行更新（主程序可能仍在运行，文件占用会导致失败）。");
            }
            else
            {
                log("主程序已退出。");
                // 让系统再释放一下文件句柄
                Thread.Sleep(800);
            }
        }
        catch (ArgumentException)
        {
            log("主程序已经不在运行。");
        }
        catch (Exception ex)
        {
            log($"等待主程序退出时出错：{ex.Message}");
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void CleanupOldBackups(string parent, string name, string currentBackup, Action<string> log)
    {
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(parent, $"{name}.old-*"))
            {
                if (string.Equals(directory, currentBackup, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    Directory.Delete(directory, recursive: true);
                    log($"已清理旧备份 {directory}");
                }
                catch (Exception ex)
                {
                    log($"清理旧备份失败 {directory}：{ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            log($"枚举旧备份失败：{ex.Message}");
        }
    }

    private static void TryCleanStaging(string staged, Action<string> log)
    {
        try
        {
            // 更新程序自己就在这个目录里运行，删不掉是正常的（重启后由系统清理临时目录）
            var root = Directory.GetParent(staged)?.Parent?.FullName;
            if (root is not null && root.Contains("PrinterShareFixer-update", StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(root, recursive: true);
                log($"已清理临时目录 {root}");
            }
        }
        catch (Exception ex)
        {
            log($"清理临时目录失败（可忽略）：{ex.Message}");
        }
    }

    private static void ShowMessage(string message, bool error)
    {
        const uint MB_ICONERROR = 0x00000010;
        const uint MB_ICONINFORMATION = 0x00000040;
        try
        {
            MessageBoxW(IntPtr.Zero, message, "打印机共享修复工具 - 更新", error ? MB_ICONERROR : MB_ICONINFORMATION);
        }
        catch
        {
            // 弹不出提示就算了，日志里已经有记录
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
