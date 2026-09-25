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
        // PowerShell 会把 “ ” ‘ ’ 这些中文智能引号当成字符串定界符，
        // 脚本里只要嵌了这种引号就会解析失败，这里统一替换掉。
        var safeScript = SanitizeSmartQuotes(script);
        if (!ReferenceEquals(safeScript, script))
        {
            log.Write("  脚本中的智能引号已替换为「」，避免 PowerShell 解析错误。");
        }

        // 用普通插值字符串拼装，{{ }} 表示字面量大括号，避免转义歧义。
        var wrapped = $"{Prologue}\n\ntry {{\n{safeScript}\nexit 0\n}} catch {{\n    [Console]::Error.WriteLine(($_.Exception.Message + ' ' + $_.InvocationInfo.PositionMessage))\n    exit 1\n}}\n";

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
            LogScriptForDiagnostics(safeScript);
        }

        return result;
    }

    /// <summary>脚本失败时把内容写进日志，方便定位是语法问题还是参数问题。</summary>
    private void LogScriptForDiagnostics(string script)
    {
        const int maxLines = 60;
        var lines = script.Split('\n');
        log.Write("  → 出错的脚本内容如下（便于排查）：");
        foreach (var line in lines.Take(maxLines))
        {
            log.Write("     | " + line.TrimEnd());
        }

        if (lines.Length > maxLines)
        {
            log.Write($"     | ……（其余 {lines.Length - maxLines} 行已省略）");
        }
    }

    private static string Indent(string text) =>
        string.Join(Environment.NewLine, text.Split('\n').Select(l => "     " + l.TrimEnd()));

    private static string SanitizeSmartQuotes(string script)
    {
        if (script.IndexOfAny(['\u201c', '\u201d', '\u2018', '\u2019']) < 0)
        {
            return script;
        }

        return script
            .Replace('\u201c', '「')
            .Replace('\u201d', '」')
            .Replace('\u2018', '「')
            .Replace('\u2019', '」');
    }
}
