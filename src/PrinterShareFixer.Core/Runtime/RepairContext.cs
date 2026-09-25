using System.Security.Principal;
using PrinterShareFixer.Core.Models;

namespace PrinterShareFixer.Core.Runtime;

/// <summary>修复步骤运行时可以使用的共享上下文。</summary>
public sealed class RepairContext
{
    private SystemInfo? _osInfo;
    private SystemSnapshot? _snapshot;

    public RepairContext(RepairOptions options, ILogSink log)
    {
        Options = options;
        Log = log;
        PowerShell = new PowerShellRunner(log);
        Registry = new RegistryTools(log);
        IsElevated = CheckElevation();
    }

    public RepairOptions Options { get; }

    public ILogSink Log { get; }

    public PowerShellRunner PowerShell { get; }

    public RegistryTools Registry { get; }

    public bool IsElevated { get; }

    public string BackupDirectory { get; set; } = string.Empty;

    /// <summary>系统信息（只读注册表，毫秒级返回）。</summary>
    public SystemInfo OsInfo => _osInfo ??= SystemInfo.Read();

    /// <summary>缓存的系统状态快照，第一次访问时才读取（耗时，请确保不在界面线程上调用）。</summary>
    public SystemSnapshot Snapshot => _snapshot ??= SystemSnapshot.Load(Log);

    /// <summary>重新读取快照，用于修复结束后的复核。</summary>
    public SystemSnapshot RefreshSnapshot(CancellationToken cancellationToken = default) =>
        _snapshot = SystemSnapshot.Load(Log, cancellationToken);

    /// <summary>丢弃缓存，让下次访问重新读取。</summary>
    public void InvalidateSnapshot() => _snapshot = null;

    private static bool CheckElevation()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
