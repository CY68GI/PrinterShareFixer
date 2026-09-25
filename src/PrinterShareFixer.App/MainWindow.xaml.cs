using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PrinterShareFixer.App.ViewModels;
using PrinterShareFixer.Core;
using PrinterShareFixer.Core.Models;
using PrinterShareFixer.Core.Profiles;
using PrinterShareFixer.Core.Runtime;
using Windows.Graphics;

namespace PrinterShareFixer.App;

public sealed partial class MainWindow : Window
{
    private readonly ObservableCollection<StepItem> _steps = [];
    private readonly FileLogSink _log;
    private CancellationTokenSource? _cancellation;
    private SystemSnapshot? _snapshot;
    private bool _running;
    private string? _lastLogFile;

    public MainWindow()
    {
        InitializeComponent();

        Title = "打印机共享修复工具";
        _log = new FileLogSink();
        _lastLogFile = _log.FilePath;
        ApplyAppIcon();

        try
        {
            SystemBackdrop = new MicaBackdrop();
        }
        catch
        {
            // 系统不支持 Mica 时使用默认背景
        }

        try
        {
            AppWindow.Resize(new SizeInt32(1180, 900));
        }
        catch
        {
            // 忽略窗口尺寸设置失败
        }

        StepList.ItemsSource = _steps;
        RootGrid.Loaded += OnRootGridLoaded;
        Closed += (_, _) => _log.Dispose();
    }

    /// <summary>设置任务栏/窗口图标与界面标题栏图标（图标文件随程序一起发布）。</summary>
    private void ApplyAppIcon()
    {
        try
        {
            var assets = Path.Combine(AppContext.BaseDirectory, "Assets");

            var icoPath = Path.Combine(assets, "app.ico");
            if (File.Exists(icoPath))
            {
                AppWindow.SetIcon(icoPath);
            }

            var pngPath = Path.Combine(assets, "app-icon.png");
            if (File.Exists(pngPath))
            {
                HeaderIcon.Source = new BitmapImage(new Uri(pngPath));
            }
        }
        catch (Exception ex)
        {
            _log.Write($"设置应用图标失败：{ex.Message}");
        }
    }

    private async void OnRootGridLoaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= OnRootGridLoaded;
        await DetectAsync();
    }

    private async Task DetectAsync()
    {
        DetectRing.IsActive = true;
        RefreshButton.IsEnabled = false;
        AdminInfoBar.Severity = InfoBarSeverity.Informational;
        AdminInfoBar.Title = "正在检测系统状态…";
        AdminInfoBar.Message = "读取服务、防火墙与注册表状态，请稍候。";

        SystemSnapshot snapshot;
        try
        {
            snapshot = await Task.Run(() => SystemSnapshot.Load(_log));
        }
        catch (Exception ex)
        {
            snapshot = new SystemSnapshot();
            AdminInfoBar.Severity = InfoBarSeverity.Error;
            AdminInfoBar.Title = "检测失败";
            AdminInfoBar.Message = ex.Message;
        }

        _snapshot = snapshot;
        var detectionItems = snapshot.ToDetectionItems();
        DetectionList.ItemsSource = detectionItems
            .Select(item => new DetectionItemViewModel(item))
            .ToList();

        var problemCount = detectionItems.Count(i => i.Status == DetectionStatus.Problem);
        var warningCount = detectionItems.Count(i => i.Status == DetectionStatus.Warning);
        DetectionHeaderText.Text = problemCount > 0
            ? $"系统状态检测 · {problemCount} 项需要处理（点击展开）"
            : warningCount > 0
                ? $"系统状态检测 · {warningCount} 项建议关注（点击展开）"
                : "系统状态检测 · 全部通过（点击展开）";

        var recommended = RepairProfiles.Recommend(snapshot.Build);
        Win10Badge.Visibility = recommended.Key == "win10" ? Visibility.Visible : Visibility.Collapsed;
        Win11Badge.Visibility = recommended.Key == "win11" ? Visibility.Visible : Visibility.Collapsed;

        Win10Button.Style = recommended.Key == "win10" ? AccentButtonStyle() : null;
        Win11Button.Style = recommended.Key == "win11" ? AccentButtonStyle() : null;

        SubtitleText.Text = $"当前系统：{snapshot.OsSummary}　建议使用「{recommended.DisplayName}」按钮。";

        if (snapshot.IsElevated)
        {
            AdminInfoBar.Severity = InfoBarSeverity.Success;
            AdminInfoBar.Title = "已以管理员身份运行";
            AdminInfoBar.Message = "可以对服务、防火墙和注册表进行修改。修复前会自动备份相关注册表项。";
            Win10Button.IsEnabled = true;
            Win11Button.IsEnabled = true;
        }
        else
        {
            AdminInfoBar.Severity = InfoBarSeverity.Error;
            AdminInfoBar.Title = "未获得管理员权限";
            AdminInfoBar.Message = "请关闭程序，右键选择“以管理员身份运行”，否则无法修改服务与注册表。";
            Win10Button.IsEnabled = false;
            Win11Button.IsEnabled = false;
        }

        DetectRing.IsActive = false;
        RefreshButton.IsEnabled = true;
    }

    private static Style? AccentButtonStyle() =>
        Application.Current.Resources.TryGetValue("AccentButtonStyle", out var style) ? style as Style : null;

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (_running)
        {
            return;
        }

        await DetectAsync();
    }

    private async void OnWindows10Click(object sender, RoutedEventArgs e) =>
        await RunRepairAsync(RepairProfiles.Windows10);

    private async void OnWindows11Click(object sender, RoutedEventArgs e) =>
        await RunRepairAsync(RepairProfiles.Windows11);

    private void OnCancelClick(object sender, RoutedEventArgs e) => _cancellation?.Cancel();

    private void OnOpenLogClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = AppPaths.EnsureLogDirectory();
            Process.Start(new ProcessStartInfo("explorer.exe", directory) { UseShellExecute = true });
        }
        catch
        {
            // 打不开资源管理器时忽略
        }
    }

    private RepairOptions BuildOptions() => new()
    {
        BackupRegistry = BackupCheckBox.IsChecked ?? false,
        DisablePasswordProtectedSharing = PasswordlessCheckBox.IsChecked ?? false,
        AllowInsecureGuestLogons = GuestCheckBox.IsChecked ?? false,
        DisableSmbSigningRequirement = SigningCheckBox.IsChecked ?? false,
        AllowRpcDynamicPorts = RpcDynamicCheckBox.IsChecked ?? false,
        AllowAnyRemoteAddress = AnyRemoteCheckBox.IsChecked ?? false,
        RestartServicesAfterFix = RestartCheckBox.IsChecked ?? false,
        EnableSmb1 = Smb1CheckBox.IsChecked ?? false,
        DisableProtectedPrintMode = WppCheckBox.IsChecked ?? false,
    };

    private async Task RunRepairAsync(RepairProfile profile)
    {
        if (_running)
        {
            return;
        }

        var options = BuildOptions();
        var confirmed = await ConfirmAsync(profile, options);
        if (!confirmed)
        {
            return;
        }

        _running = true;
        _cancellation = new CancellationTokenSource();
        Win10Button.IsEnabled = false;
        Win11Button.IsEnabled = false;
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
        StatusText.Text = $"正在执行「{profile.DisplayName} 打印机共享修复」…";
        StepsHeaderText.Text = $"修复进度 · {profile.DisplayName}";
        StepsCard.StartBringIntoView();

        var completed = 0;
        var progress = new Progress<StepUpdate>(update =>
        {
            if (update.Index < 0 || update.Index >= _steps.Count)
            {
                return;
            }

            _steps[update.Index].Apply(update.State, update.Message, update.Details);
            if (update.State is not (StepState.Running or StepState.Pending))
            {
                completed = Math.Max(completed, update.Index + 1);
                RunProgress.Value = completed;
            }

            StatusText.Text = $"[{update.Index + 1}/{profile.Steps.Count}] {update.Step.Title}：{update.Message}";
        });

        RepairReport? report = null;
        var cancelled = false;
        try
        {
            var engine = new RepairEngine(_log);
            report = await engine.RunAsync(profile, options, progress, _cancellation.Token);
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
            _log.Write($"修复异常：{ex}");
        }
        finally
        {
            _running = false;
            CancelButton.Visibility = Visibility.Collapsed;
            _cancellation.Dispose();
            _cancellation = null;
            RefreshButton.IsEnabled = true;
            _lastLogFile = report?.LogFile ?? _lastLogFile;
        }

        if (report is not null)
        {
            RunProgress.Value = profile.Steps.Count;
            StatusText.Text =
                $"完成：成功 {report.SucceededCount} 项，警告 {report.WarningCount} 项，失败 {report.FailedCount} 项，跳过 {report.SkippedCount} 项。日志：{report.LogFile}";
            ResultInfoBar.Severity = report.FailedCount > 0
                ? InfoBarSeverity.Warning
                : report.WarningCount > 0 ? InfoBarSeverity.Informational : InfoBarSeverity.Success;
            ResultInfoBar.Title = report.FailedCount > 0 ? "修复完成，但存在失败项" : "修复完成";
            ResultInfoBar.Message = report.FailedCount > 0
                ? "请展开上方的失败步骤查看日志详情；如需还原，可使用日志目录同级 Backups 文件夹中的注册表备份。"
                : "建议在客户端电脑上重新连接共享打印机测试；如仍失败，请点击“打开日志”查看完整记录。";
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
        var changes = new StackPanel { Spacing = 6 };
        changes.Children.Add(new TextBlock
        {
            Text = $"将对本机执行「{profile.DisplayName} 打印机共享修复」，包含以下动作：",
            TextWrapping = TextWrapping.Wrap,
        });

        var bullets = new[]
        {
            "启动并配置 LanmanServer / LanmanWorkstation / Print Spooler / Function Discovery 等服务",
            "把当前网络位置设为“专用网络”，启用 NetBIOS over TCP/IP",
            "放行防火墙的“文件和打印机共享”“网络发现”，并创建 SMB / RPC / NetBIOS / WSD 入站规则",
            options.DisablePasswordProtectedSharing ? "关闭“密码保护的共享”（来宾访问策略）" : null,
            "关闭打印 RPC 隐私认证（修复 0x0000011b）",
            "放宽非管理员安装共享打印机驱动的限制（修复 0x00000740）",
            options.EnableSmb1 ? "启用 SMB1 协议（兼容老旧设备）" : null,
            options.DisableProtectedPrintMode ? "关闭 Windows 受保护的打印模式" : null,
            options.BackupRegistry ? "修改前把相关注册表项导出到备份目录" : null,
        };

        foreach (var line in bullets.Where(line => line is not null))
        {
            changes.Children.Add(new TextBlock { Text = "• " + line, TextWrapping = TextWrapping.Wrap });
        }

        changes.Children.Add(new TextBlock
        {
            Text = "修复过程需要 10-60 秒，期间打印服务会短暂重启。建议先保存正在打印的文档。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "确认执行修复？",
            Content = changes,
            PrimaryButtonText = "开始修复",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
