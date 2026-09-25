using System.Diagnostics;
using System.Text;

namespace PrinterShareFixer.Core.Runtime;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, string CommandLine)
{
    public bool Succeeded => ExitCode == 0;

    public IReadOnlyList<string> OutputLines => StandardOutput
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public string ErrorSummary
    {
        get
        {
            var text = string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : StandardError;
            text = text.Trim();
            if (text.Length == 0)
            {
                return $"退出代码 {ExitCode}";
            }

            var firstLine = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? text;
            return firstLine.Length > 400 ? firstLine[..400] : firstLine;
        }
    }
}

/// <summary>以隐藏窗口方式运行外部命令（reg.exe / sc.exe / powershell.exe 等）。</summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default,
        int timeoutMilliseconds = 300_000)
    {
        var argumentList = arguments.ToList();
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in argumentList)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stdoutDone = new TaskCompletionSource();
        var stderrDone = new TaskCompletionSource();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stdoutDone.TrySetResult();
            }
            else
            {
                stdout.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stderrDone.TrySetResult();
            }
            else
            {
                stderr.AppendLine(e.Data);
            }
        };

        var commandLine = $"{fileName} {string.Join(' ', argumentList)}";

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, string.Empty, ex.Message, commandLine);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMilliseconds);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            await Task.WhenAny(Task.WhenAll(stdoutDone.Task, stderrDone.Task), Task.Delay(2000)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new ProcessResult(-1, stdout.ToString(), $"命令执行超时（{timeoutMilliseconds / 1000} 秒）", commandLine);
        }

        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString(), commandLine);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // 忽略终止失败
        }
    }
}
