using System.Security.Principal;
using PrinterShareFixer.Core.Models;

namespace PrinterShareFixer.Core.Runtime;

/// <summary>修复步骤运行时可以使用的共享上下文。</summary>
public sealed class RepairContext
{
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
