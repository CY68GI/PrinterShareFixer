using System.Text;

namespace PrinterShareFixer.Core.Runtime;

/// <summary>
/// 通过 powershell.exe 执行修复脚本。
/// 脚本以 UTF-16LE + Base64 传入（-EncodedCommand），规避引号与中文转义问题。
/// </summary>
public sealed class PowerShellRunner(ILogSink log)
{
    private const string Prologue = """
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        $OutputEncoding = [System.Text.Encoding]::UTF8
        """;

    public async Task<ProcessResult> RunAsync(
        string script,
        CancellationToken cancellationToken,
        string friendlyName,
        int timeoutMilliseconds = 300_000)
    {
        // 用普通插值字符串拼装，{{ }} 表示字面量大括号，避免转义歧义。
        var wrapped = $"{Prologue}\n\ntry {{\n{script}\nexit 0\n}} catch {{\n    [Console]::Error.WriteLine(($_.Exception.Message + ' ' + $_.InvocationInfo.PositionMessage))\n    exit 1\n}}\n";

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(wrapped));
        log.Write($"执行 PowerShell：{friendlyName}");

        var result = await ProcessRunner.RunAsync(
            "powershell.exe",
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded],
            cancellationToken,
            timeoutMilliseconds);

        log.Write(string.IsNullOrWhiteSpace(result.StandardOutput)
            ? $"  → 退出代码 {result.ExitCode}"
            : $"  → 退出代码 {result.ExitCode}{Environment.NewLine}{Indent(result.StandardOutput.Trim())}");

        if (!result.Succeeded && !string.IsNullOrWhiteSpace(result.StandardError))
        {
            log.Write($"  → 错误：{result.ErrorSummary}");
        }

        return result;
    }

    private static string Indent(string text) =>
        string.Join(Environment.NewLine, text.Split('\n').Select(l => "     " + l.TrimEnd()));
}
