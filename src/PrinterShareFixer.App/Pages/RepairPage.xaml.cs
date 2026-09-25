using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PrinterShareFixer.App.ViewModels;
using PrinterShareFixer.Core;
using PrinterShareFixer.Core.Models;
using PrinterShareFixer.Core.Profiles;
using PrinterShareFixer.Core.Runtime;

namespace PrinterShareFixer.App.Pages;

/// <summary>修复页面：选角色 → 选系统 → 一键修复，并逐项显示进度。</summary>
public sealed partial class RepairPage : Page
{
    private readonly AppState _state = AppState.Current;
    private readonly ObservableCollection<StepItem> _steps = [];
    private CancellationTokenSource? _cancellation;
    private RepairRole _role = RepairRole.Provider;
    private bool _running;
    private bool _loaded;

    public RepairPage()
    {
        InitializeComponent();
        UpdateRoleUi();
        StepList.ItemsSource = _steps;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _state.SnapshotChanged += OnSnapshotChanged;
        await DetectAsync();
    }

    private void OnSnapshotChanged()
    {
        if (_state.Snapshot is { } snapshot)
        {
            RenderDetection(snapshot);
        }
    }

    /// <summary>窗口关闭时调用，取消正在进行的修复。</summary>
    public void CancelRunningRepair() => _cancellation?.Cancel();

    private static Style? StyleOrDefault(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) ? value as Style : null;

    private static Brush BrushOrDefault(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);

    private static Style? AccentButtonStyle() =>
        Application.Current.Resources.TryGetValue("AccentButtonStyle", out var style) ? style as Style : null;

    // ---------- 角色与文案 ----------

    private void SetRole(RepairRole role)
    {
        if (_running)
        {
            UpdateRoleUi();
            return;
        }

        if (_role != role)
        {
            _role = role;
            _steps.Clear();
            ResultInfoBar.IsOpen = false;
            RunProgress.Value = 0;
            StatusText.Text = "就绪";
            if (_state.Snapshot is { } snapshot)
            {
                RenderDetection(snapshot);
            }
        }

        UpdateRoleUi();
    }

    private void UpdateRoleUi()
    {
        var isProvider = _role == RepairRole.Provider;
        RoleProviderButton.IsChecked = isProvider;
        RoleConsumerButton.IsChecked = !isProvider;
        TargetHostPanel.Visibility = isProvider ? Visibility.Collapsed : Visibility.Visible;

        OsSectionTitle.Text = isProvider
            ? "第二步：这台电脑（接打印机的那台）是什么系统？"
            : "第二步：这台电脑（要连打印机的那台）是什么系统？";

        if (isProvider)
        {
            Win10Title.Text = "本机是 Windows 10";
            Win11Title.Text = "本机是 Windows 11";
            Win10Desc.Text = "开启共享服务、网络发现、防火墙放行与来宾访问，可选启用 SMB1（兼容很老的设备）。";
            Win11Desc.Text = "在 Windows 10 基础上按 24H2 调整打印 RPC 传输与受保护的打印模式。";
            StepsHeaderText.Text = "修复进度 · 开放共享";
            StepsEmptyHint.Text = "选好系统后点击上方按钮，开始为这台电脑开放打印机共享。";
        }
        else
        {
            Win10Title.Text = "本机是 Windows 10";
            Win11Title.Text = "本机是 Windows 11";
            Win10Desc.Text = "放行连接所需服务与端口，解决来宾访问、驱动安装限制，并清理失效的打印缓存。";
            Win11Desc.Text = "在 Windows 10 基础上加 AllowInsecureGuestAuth 策略与 24H2 的打印 RPC 兼容处理。";
            StepsHeaderText.Text = "修复进度 · 连接共享";
            StepsEmptyHint.Text = "选好系统后点击上方按钮开始修复；如果填了对方电脑名，最后一步会自动测试连通性。";
        }

        StepsEmptyHint.Visibility = _steps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRoleProviderClick(object sender, RoutedEventArgs e) => SetRole(RepairRole.Provider);

    private void OnRoleConsumerClick(object sender, RoutedEventArgs e) => SetRole(RepairRole.Consumer);

    // ---------- 检测 ----------

    private async Task DetectAsync()
    {
        DetectRing.IsActive = true;
        RefreshButton.IsEnabled = false;
        Win10Button.IsEnabled = false;
        Win11Button.IsEnabled = false;
        StatusText.Text = "正在检测系统状态…";

        SystemSnapshot snapshot;
        try
        {
            snapshot = await _state.RefreshSnapshotAsync(force: true);
        }
        catch (Exception ex)
        {
            snapshot = new SystemSnapshot();
            _state.Log.Write($"检测失败：{ex.Message}");
        }

        RenderDetection(snapshot);

        var recommendedKey = RepairProfiles.RecommendOsKey(snapshot.Build);
        Win10Badge.Visibility = recommendedKey == "win10" ? Visibility.Visible : Visibility.Collapsed;
        Win11Badge.Visibility = recommendedKey == "win11" ? Visibility.Visible : Visibility.Collapsed;
        Win10Button.Style = recommendedKey == "win10" ? AccentButtonStyle() : null;
        Win11Button.Style = recommendedKey == "win11" ? AccentButtonStyle() : null;

        if (snapshot.IsElevated)
        {
            Win10Button.IsEnabled = true;
            Win11Button.IsEnabled = true;
            StatusText.Text = "就绪";
        }
        else
        {
            StatusText.Text = "未获得管理员权限，无法执行修复（请以管理员身份运行）。";
        }

        DetectRing.IsActive = false;
        RefreshButton.IsEnabled = true;
    }

    private void RenderDetection(SystemSnapshot snapshot)
    {
        var items = snapshot.ToDetectionItems(_role);
        DetectionList.ItemsSource = items.Select(item => new DetectionItemViewModel(item)).ToList();

        var problems = items.Count(i => i.Status == DetectionStatus.Problem);
        var warnings = items.Count(i => i.Status == DetectionStatus.Warning);
        DetectionHeaderText.Text = problems > 0
            ? $"系统状态检测 · {problems} 项需要处理（点击展开）"
            : warnings > 0
                ? $"系统状态检测 · {warnings} 项建议关注（点击展开）"
                : "系统状态检测 · 全部通过（点击展开）";
    }

    // ---------- 修复 ----------

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (_running)
        {
            return;
        }

        await DetectAsync();
    }

    private async void OnWindows10Click(object sender, RoutedEventArgs e) => await RunRepairAsync("win10");

    private async void OnWindows11Click(object sender, RoutedEventArgs e) => await RunRepairAsync("win11");

    private void OnCancelClick(object sender, RoutedEventArgs e) => _cancellation?.Cancel();

    private RepairOptions BuildOptions() => new()
    {
        BackupRegistry = BackupCheckBox.IsChecked ?? false,
        DisablePasswordProtectedSharing = PasswordlessCheckBox.IsChecked ?? false,
        AllowInsecureGuestLogons = GuestCheckBox.IsChecked ?? false,
        DisableSmbSigningRequirement = SigningCheckBox.IsChecked ?? false,
        AllowRpcDynamicPorts = RpcDynamicCheckBox.IsChecked ?? false,
        ClearStalePrintCache = CleanCacheCheckBox.IsChecked ?? false,
        CleanSmbSession = SmbSessionCheckBox.IsChecked ?? false,
        AllowAnyRemoteAddress = AnyRemoteCheckBox.IsChecked ?? false,
        RestartServicesAfterFix = RestartCheckBox.IsChecked ?? false,
        EnableSmb1 = Smb1CheckBox.IsChecked ?? false,
        DisableProtectedPrintMode = WppCheckBox.IsChecked ?? false,
        TargetHost = string.IsNullOrWhiteSpace(TargetHostBox.Text) ? null : TargetHostBox.Text.Trim(),
    };

    private async Task RunRepairAsync(string osKey)
    {
        if (_running)
        {
            return;
        }

        var profile = RepairProfiles.Get(_role, osKey);
        var options = BuildOptions();
        if (!await ConfirmAsync(profile, options))
        {
            return;
        }

        _running = true;
        _cancellation = new CancellationTokenSource();
        Win10Button.IsEnabled = false;
        Win11Button.IsEnabled = false;
        RoleProviderButton.IsEnabled = false;
        RoleConsumerButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        CancelButton.Visibility = Visibility.Visible;
        ResultInfoBar.IsOpen = false;
        StepsEmptyHint.Visibility = Visibility.Collapsed;

        _steps.Clear();
        for (var i = 0; i < profile.Steps.Count; i++)
        {
            _steps.Add(new StepItem(profile.Steps[i], i));
        }

        RunProgress.Maximum = profile.Steps.Count;
        RunProgress.Value = 0;
        StatusText.Text = $"正在执行「{profile.ActionTitle}」…";
        StepsHeaderText.Text = $"修复进度 · {profile.ActionTitle}";
        StepsCard.StartBringIntoView();

        var progress = new Progress<StepUpdate>(update =>
        {
            if (update.Index < 0 || update.Index >= _steps.Count)
            {
                return;
            }

            _steps[update.Index].Apply(update.State, update.Message, update.Details);
            if (update.State is not (StepState.Running or StepState.Pending))
            {
                RunProgress.Value = Math.Max(RunProgress.Value, update.Index + 1);
            }

            StatusText.Text = $"[{update.Index + 1}/{profile.Steps.Count}] {update.Step.Title}：{update.Message}";
        });

        RepairReport? report = null;
        var cancelled = false;
        try
        {
            var engine = new RepairEngine(_state.Log);
            var token = _cancellation.Token;

            // 修复步骤里有大量同步等待（PowerShell、WMI、注册表），必须在后台线程执行，
            // 否则界面会卡死、取消和关闭都会失效。
            report = await Task.Run(() => engine.RunAsync(profile, options, progress, token), token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            ResultInfoBar.Severity = InfoBarSeverity.Error;
            ResultInfoBar.Title = "修复过程中出现异常";
            ResultInfoBar.Message = ex.Message;
            ResultInfoBar.IsOpen = true;
            _state.Log.Write($"修复异常：{ex}");
        }
        finally
        {
            _running = false;
            CancelButton.Visibility = Visibility.Collapsed;
            _cancellation.Dispose();
            _cancellation = null;
            RoleProviderButton.IsEnabled = true;
            RoleConsumerButton.IsEnabled = true;
            RefreshButton.IsEnabled = true;
        }

        if (report is not null)
        {
            RunProgress.Value = profile.Steps.Count;
            StatusText.Text =
                $"完成：成功 {report.SucceededCount} 项，警告 {report.WarningCount} 项，失败 {report.FailedCount} 项，跳过 {report.SkippedCount} 项。";
            ResultInfoBar.Severity = report.FailedCount > 0
                ? InfoBarSeverity.Warning
                : report.WarningCount > 0 ? InfoBarSeverity.Informational : InfoBarSeverity.Success;
            ResultInfoBar.Title = report.FailedCount > 0 ? "修复完成，但存在失败项" : "修复完成";
            ResultInfoBar.Message = report.FailedCount > 0
                ? $"请展开上方的失败步骤查看详情；日志：{report.LogFile}"
                : _role == RepairRole.Provider
                    ? "请到客户端的电脑上重新连接这台电脑的共享打印机进行测试。"
                    : "请回到资源管理器重新连接共享打印机测试；若仍失败，把对方电脑名填到上面再修复一次，会自动测试连通性。";
            ResultInfoBar.IsOpen = true;
        }
        else if (cancelled)
        {
            StatusText.Text = "已取消修复。部分步骤可能已经生效，可点击“重新检测”查看当前状态。";
            ResultInfoBar.Severity = InfoBarSeverity.Warning;
            ResultInfoBar.Title = "已取消";
            ResultInfoBar.Message = "修复过程被中断，建议重新检测并再执行一次完整修复。";
            ResultInfoBar.IsOpen = true;
        }

        await DetectAsync();
        DetectionExpander.IsExpanded = true;
    }

    private async Task<bool> ConfirmAsync(RepairProfile profile, RepairOptions options)
    {
        var isProvider = profile.Role == RepairRole.Provider;
        var changes = new StackPanel { Spacing = 6 };
        changes.Children.Add(new TextBlock
        {
            Text = $"将对本机执行「{profile.Description}」，包含：",
            TextWrapping = TextWrapping.Wrap,
        });

        var bullets = new List<string?>
        {
            isProvider
                ? "启动并配置 Server / Workstation / Print Spooler / Function Discovery 等服务"
                : "启动并配置 Workstation / Print Spooler / 网络发现等客户端服务",
            "把当前网络位置设为“专用网络”，启用 NetBIOS over TCP/IP",
            isProvider
                ? "放行防火墙的“文件和打印机共享”“网络发现”，并创建 SMB / RPC / NetBIOS / WSD 入站规则"
                : "启用内置“文件和打印机共享”“网络发现”规则，保证客户端能访问对方共享",
            options.DisablePasswordProtectedSharing ? "关闭“密码保护的共享”（来宾访问策略）" : null,
            options.AllowInsecureGuestLogons ? "允许 SMB 来宾访问（含 AllowInsecureGuestAuth 策略）" : null,
            "关闭打印 RPC 隐私认证（修复 0x0000011b）",
            "放宽非管理员安装共享打印机驱动的限制（修复 0x00000740）",
            !isProvider && options.ClearStalePrintCache ? "清理失效的打印缓存与卡住的打印队列" : null,
            !isProvider && options.CleanSmbSession ? "清理本机到目标电脑的旧 SMB 连接，并刷新 DNS / NetBIOS 名称缓存" : null,
            options.EnableSmb1 ? "启用 SMB1 协议（兼容老旧设备）" : null,
            options.DisableProtectedPrintMode ? "关闭 Windows 受保护的打印模式" : null,
            options.BackupRegistry ? "修改前把相关注册表项导出到备份目录" : null,
            !isProvider && !string.IsNullOrWhiteSpace(options.TargetHost)
                ? $"修复结束后测试到 {options.TargetHost} 的连通性"
                : null,
        };

        foreach (var line in bullets.Where(line => line is not null))
        {
            changes.Children.Add(new TextBlock { Text = "• " + line, TextWrapping = TextWrapping.Wrap });
        }

        changes.Children.Add(new TextBlock
        {
            Text = "修复过程需要 10-60 秒，期间打印服务会短暂重启。建议先保存正在打印的文档。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = BrushOrDefault("TextFillColorSecondaryBrush"),
        });

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "确认执行修复？",
            Content = changes,
            PrimaryButtonText = "开始修复",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
