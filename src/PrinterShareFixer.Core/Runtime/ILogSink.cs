namespace PrinterShareFixer.Core.Runtime;

/// <summary>修复过程的日志输出目标。</summary>
public interface ILogSink
{
    void Write(string message);
}

public sealed class NullLogSink : ILogSink
{
    public static readonly NullLogSink Instance = new();

    private NullLogSink()
    {
    }

    public void Write(string message)
    {
    }
}

/// <summary>把日志同时写到文件与界面回调。</summary>
public sealed class FileLogSink : ILogSink, IDisposable
{
    private readonly StreamWriter _writer;
    private readonly Action<string>? _mirror;
    private readonly object _gate = new();

    public FileLogSink(string? filePath = null, Action<string>? mirror = null)
    {
        var path = filePath ?? Path.Combine(
            AppPaths.EnsureLogDirectory(),
            $"printer-share-fix-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _writer = new StreamWriter(path, append: true) { AutoFlush = true };
        FilePath = path;
        _mirror = mirror;
    }

    public string FilePath { get; }

    public void Write(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        lock (_gate)
        {
            _writer.WriteLine(line);
        }

        _mirror?.Invoke(line);
    }

    public void Dispose() => _writer.Dispose();
}

/// <summary>把日志写回内存，供命令行工具使用。</summary>
public sealed class MemoryLogSink : ILogSink
{
    private readonly List<string> _lines = [];
    private readonly Action<string>? _mirror;

    public MemoryLogSink(Action<string>? mirror = null) => _mirror = mirror;

    public IReadOnlyList<string> Lines => _lines;

    public void Write(string message)
    {
        lock (_lines)
        {
            _lines.Add(message);
        }

        _mirror?.Invoke(message);
    }
}
