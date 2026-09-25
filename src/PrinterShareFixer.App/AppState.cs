using System.Security.Principal;
using PrinterShareFixer.Core.Runtime;
using PrinterShareFixer.Core.Update;

namespace PrinterShareFixer.App;

/// <summary>页面之间共享的状态：日志、系统快照、权限信息、更新提示。</summary>
public sealed class AppState : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SystemSnapshot? _snapshot;

    private AppState()
    {
        Log = new FileLogSink();
        MachineName = Environment.MachineName;
        UserName = string.IsNullOrWhiteSpace(Environment.UserDomainName)
            ? Environment.UserName
            : $"{Environment.UserDomainName}\\{Environment.UserName}";
        IsElevated = CheckElevated();
        IsAdminAccount = CheckAdminAccount();
    }

    public static AppState Current { get; } = new();

    public FileLogSink Log { get; }

    public string MachineName { get; }

    public string UserName { get; }

    public bool IsElevated { get; }

    public bool IsAdminAccount { get; }

    public bool UpdateAvailable { get; private set; }

    public string? NewerVersion { get; private set; }

    public SystemSnapshot? Snapshot => _snapshot;

    public event Action? SnapshotChanged;

    public event Action? UpdateStateChanged;

    public string UserTypeText => IsElevated
        ? "管理员账号（已提权）"
        : IsAdminAccount ? "管理员账号（当前未提权）" : "标准用户";

    /// <summary>读取（或强制重新读取）系统快照；整个过程在后台线程执行。</summary>
    public async Task<SystemSnapshot> RefreshSnapshotAsync(bool force = false)
    {
        if (_snapshot is not null && !force)
        {
            return _snapshot;
        }

        await _gate.WaitAsync().ConfigureAwait(true);
        try
        {
            var snapshot = await Task.Run(() => SystemSnapshot.Load(Log)).ConfigureAwait(true);
            _snapshot = snapshot;
            SnapshotChanged?.Invoke();
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void SetSnapshot(SystemSnapshot snapshot)
    {
        _snapshot = snapshot;
        SnapshotChanged?.Invoke();
    }

    /// <summary>启动时静默检查更新（每天最多一次）。</summary>
    public async Task CheckForUpdatesAsync()
    {
        try
        {
            if (!UpdateChecker.ShouldAutoCheck())
            {
                ApplyUpdateState(UpdateChecker.LastKnownNewerVersion());
                return;
            }

            var result = await UpdateChecker.CheckAsync().ConfigureAwait(true);
            Log.Write($"检查更新：{result.Message}");
            ApplyUpdateState(result.Status == UpdateCheckStatus.UpdateAvailable
                ? result.Release?.Version.ToString()
                : null);
        }
        catch (Exception ex)
        {
            Log.Write($"检查更新失败：{ex.Message}");
        }
    }

    public void ApplyUpdateState(string? newerVersion)
    {
        UpdateAvailable = !string.IsNullOrWhiteSpace(newerVersion);
        NewerVersion = newerVersion;
        UpdateStateChanged?.Invoke();
    }

    public void Dispose() => Log.Dispose();

    private static bool CheckElevated()
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

    private static bool CheckAdminAccount()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            return identity.Groups?.Contains(administrators) == true;
        }
        catch
        {
            return false;
        }
    }
}
