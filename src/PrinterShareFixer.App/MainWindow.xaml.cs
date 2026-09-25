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
using PrinterShareFixer.Core.Update;
using Windows.Graphics;

namespace PrinterShareFixer.App;

public sealed partial class MainWindow : Window
{
    private readonly ObservableCollection<StepItem> _steps = [];
    private readonly FileLogSink _log;
    private CancellationTokenSource? _cancellation;
    private SystemSnapshot? _snapshot;
    private BitmapImage? _iconImage;
    private UpdateRelease? _availableRelease;
    private RepairRole _role = RepairRole.Provider;
    private bool _running;
    private string? _lastLogFile;

    public MainWindow()
    {
        InitializeComponent();

        Title = AppInfo.ProductName;
        _log = new FileLogSink();
        _lastLogFile = _log.FilePath;
        ApplyAppIcon();
        UpdateRoleUi();

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
            AppWindow.Resize(new SizeInt32(1180, 960));
        }
        catch
        {
            // 忽略窗口尺寸设置失败
        }

        StepList.ItemsSource = _steps;
        RootGrid.Loaded += OnRootGridLoaded;
        try
        {
            // 关闭窗口时立刻取消正在进行的修复，避免后台残留进程
            AppWindow.Closing += (_, _) => _cancellation?.Cancel();
        }
        catch
        {
            // 拿不到 AppWindow 时忽略
        }

        Closed += OnWindowClosed;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _cancellation?.Cancel();
        _log.Dispose();
    }

    // ---------- 启动与图标 ----------

    private async void OnRootGridLoaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= OnRootGridLoaded;
        await DetectAsync();
        _ = CheckForUpdatesInBackgroundAsync();
    }

    /// <summary>启动时静默检查一次更新（每天最多一次），发现新版本就在设置按钮上点个提示点。</summary>
    private async Task CheckForUpdatesInBackgroundAsync()
    {
        try
        {
            if (!UpdateChecker.ShouldAutoCheck())
            {
                if (UpdateChecker.LastKnownNewerVersion() is not null)
                {
                    SettingsBadge.Visibility = Visibility.Visible;
                }

                return;
            }

            var result = await UpdateChecker.CheckAsync();
            if (result.Status == UpdateCheckStatus.UpdateAvailable && result.Release is not null)
            {
                _availableRelease = result.Release;
                SettingsBadge.Visibility = Visibility.Visible;
                _log.Write($"检查更新：发现新版本 v{result.Release.Version}（当前 v{AppInfo.Version}）");
            }
            else
            {
                _log.Write($"检查更新：{result.Message}");
            }
        }
        catch (Exception ex)
        {
            _log.Write($"检查更新失败：{ex.Message}");
        }
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
                _iconImage = new BitmapImage(new Uri(pngPath));
                HeaderIcon.Source = _iconImage;
            }
        }
        catch (Exception ex)
        {
            _log.Write($"设置应用图标失败：{ex.Message}");
        }
    }

    private static Style? AccentButtonStyle() =>
        Application.Current.Resources.TryGetValue("AccentButtonStyle", out var style) ? style as Style : null;

    /// <summary>安全读取主题资源，取不到时退回默认值，避免设置窗口因资源缺失报错。</summary>
    private static Style? StyleOrDefault(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) ? value as Style : null;

    private static Brush BrushOrDefault(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);

    // ---------- 角色与界面文案 ----------

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
            if (_snapshot is not null)
            {
                RenderDetection(_snapshot);
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
        // 检测期间先禁用修复按钮，避免和检测同时读取系统状态
        Win10Button.IsEnabled = false;
        Win11Button.IsEnabled = false;
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
        RenderDetection(snapshot);

        var recommendedKey = RepairProfiles.RecommendOsKey(snapshot.Build);
        Win10Badge.Visibility = recommendedKey == "win10" ? Visibility.Visible : Visibility.Collapsed;
        Win11Badge.Visibility = recommendedKey == "win11" ? Visibility.Visible : Visibility.Collapsed;
        Win10Button.Style = recommendedKey == "win10" ? AccentButtonStyle() : null;
        Win11Button.Style = recommendedKey == "win11" ? AccentButtonStyle() : null;

        SubtitleText.Text =
            $"当前系统：{snapshot.OsSummary}　角色：{_role.ToDisplayName()}（{_role.ToActionText()}）　" +
            $"推荐系统：{(recommendedKey == "win11" ? "Windows 11" : "Windows 10")}";

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

    // ---------- 设置 ----------

    private async void OnSettingsClick(object sender, RoutedEventArgs e) => await ShowSettingsAsync();

    /// <summary>设置对话框里的"更新"区块：检查更新、一键更新、下载页兜底。</summary>
    private StackPanel BuildUpdateSection()
    {
        var section = new StackPanel { Spacing = 8 };

        section.Children.Add(new TextBlock
        {
            Text = "更新",
            Style = StyleOrDefault("BodyStrongTextBlockStyle"),
        });

        section.Children.Add(new TextBlock
        {
            Text = $"当前版本 v{AppInfo.Version}（{(UpdateChecker.IsSelfContainedInstall ? "自带运行时" : "需要 .NET 运行时")}）",
            Style = StyleOrDefault("CaptionTextBlockStyle"),
            Foreground = BrushOrDefault("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
        });

        var statusText = new TextBlock
        {
            Text = _availableRelease is null
                ? "点击「检查更新」从 GitHub 获取最新版本。"
                : $"发现新版本 v{_availableRelease.Version}，可以直接更新（程序会自动重启）。",
            TextWrapping = TextWrapping.Wrap,
        };
        section.Children.Add(statusText);

        var notesText = new TextBlock
        {
            Text = _availableRelease is null ? string.Empty : FormatNotes(_availableRelease.Notes),
            Visibility = _availableRelease is null ? Visibility.Collapsed : Visibility.Visible,
            Style = StyleOrDefault("CaptionTextBlockStyle"),
            Foreground = BrushOrDefault("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
        };
        section.Children.Add(notesText);

        var progress = new ProgressBar
        {
            Visibility = Visibility.Collapsed,
            Maximum = 100,
            Value = 0,
        };
        section.Children.Add(progress);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var checkButton = new Button { Content = "检查更新" };
        var updateButton = new Button
        {
            Content = "立即更新并重启",
            Visibility = _availableRelease is null ? Visibility.Collapsed : Visibility.Visible,
            IsEnabled = _availableRelease is not null,
        };
        var pageButton = new Button { Content = "打开下载页" };
        buttons.Children.Add(checkButton);
        buttons.Children.Add(updateButton);
        buttons.Children.Add(pageButton);
        section.Children.Add(buttons);

        checkButton.Click += async (_, _) =>
        {
            checkButton.IsEnabled = false;
            updateButton.IsEnabled = false;
            statusText.Text = "正在检查更新…";

            var result = await UpdateChecker.CheckAsync();
            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable when result.Release is not null:
                    _availableRelease = result.Release;
                    SettingsBadge.Visibility = Visibility.Visible;
                    statusText.Text = $"发现新版本 v{result.Release.Version}（当前 v{AppInfo.Version}），点击「立即更新并重启」即可。";
                    notesText.Text = FormatNotes(result.Release.Notes);
                    notesText.Visibility = Visibility.Visible;
                    updateButton.Visibility = Visibility.Visible;
                    updateButton.IsEnabled = true;
                    break;
                case UpdateCheckStatus.UpToDate:
                    statusText.Text = result.Message;
                    updateButton.Visibility = Visibility.Collapsed;
                    SettingsBadge.Visibility = Visibility.Collapsed;
                    break;
                default:
                    statusText.Text = $"{result.Message}（可以点「打开下载页」手动下载）";
                    break;
            }

            checkButton.IsEnabled = true;
        };

        updateButton.Click += async (_, _) => await RunUpdateAsync(updateButton, statusText, progress);
        pageButton.Click += (_, _) => OpenUrl(_availableRelease?.HtmlUrl ?? UpdateChecker.ReleasesPageUrl);

        return section;
    }

    private async Task RunUpdateAsync(Button updateButton, TextBlock statusText, ProgressBar progress)
    {
        var release = _availableRelease;
        if (release is null)
        {
            return;
        }

        try
        {
            updateButton.IsEnabled = false;
            progress.Visibility = Visibility.Visible;
            progress.Value = 0;
            statusText.Text = "正在下载更新包…";

            var report = new Progress<UpdateDownloadProgress>(update =>
            {
                statusText.Text = update.Percent is null
                    ? $"{update.Stage}…"
                    : $"{update.Stage}… {update.Percent:F0}%（{update.BytesReceived / 1024d / 1024:F1} MB）";

                if (update.Percent is not null)
                {
                    progress.Value = update.Percent.Value;
                }
            });

            var staged = await UpdateDownloader.DownloadAndStageAsync(
                release,
                UpdateChecker.IsSelfContainedInstall,
                report);

            _log.Write($"更新包已就绪：{staged.AppDirectory}（来自 {staged.Asset.Name}）");
            statusText.Text = $"更新包已下载并校验通过，程序即将重启完成更新到 v{staged.Version}…";
            progress.Value = 100;

            UpdateApplier.StartUpdater(staged.AppDirectory, AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), relaunch: true);

            await Task.Delay(800);
            Application.Current.Exit();
        }
        catch (Exception ex)
        {
            _log.Write($"自动更新失败：{ex}");
            progress.Visibility = Visibility.Collapsed;
            updateButton.IsEnabled = true;
            statusText.Text = $"更新失败：{ex.Message}。可以点「打开下载页」手动下载。";
        }
    }

    private static string FormatNotes(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return string.Empty;
        }

        var lines = notes
            .Split('\n')
            .Select(line => line.TrimEnd())
            .Where(line => line.Trim().Length > 0)
            .Take(10);

        return string.Join(Environment.NewLine, lines);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // 打不开浏览器时忽略
        }
    }

    private async Task ShowSettingsAsync()
    {
        var panel = new StackPanel { Spacing = 14, MinWidth = 480 };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        if (_iconImage is not null)
        {
            header.Children.Add(new Image { Width = 52, Height = 52, Source = _iconImage, VerticalAlignment = VerticalAlignment.Top });
        }

        var titleStack = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock
        {
            Text = AppInfo.ProductName,
            Style = StyleOrDefault("SubtitleTextBlockStyle"),
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = $"版本 {AppInfo.Version}",
            Foreground = BrushOrDefault("TextFillColorSecondaryBrush"),
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = _snapshot is null ? string.Empty : $"运行环境：{_snapshot.OsSummary}",
            Style = StyleOrDefault("CaptionTextBlockStyle"),
            Foreground = BrushOrDefault("TextFillColorTertiaryBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        header.Children.Add(titleStack);
        panel.Children.Add(header);

        panel.Children.Add(new Border
        {
            Height = 1,
            Background = BrushOrDefault("CardStrokeColorDefaultBrush"),
        });

        panel.Children.Add(BuildUpdateSection());

        panel.Children.Add(new Border
        {
            Height = 1,
            Background = BrushOrDefault("CardStrokeColorDefaultBrush"),
        });

        panel.Children.Add(new TextBlock
        {
            Text = "更新内容",
            Style = StyleOrDefault("BodyStrongTextBlockStyle"),
        });

        var notes = new StackPanel { Spacing = 14 };
        foreach (var note in AppInfo.ReleaseNotes)
        {
            var block = new StackPanel { Spacing = 4 };
            block.Children.Add(new TextBlock
            {
                Text = $"v{note.Version}　{note.Date}",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });

            foreach (var line in note.Highlights)
            {
                block.Children.Add(new TextBlock
                {
                    Text = "· " + line,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = BrushOrDefault("TextFillColorSecondaryBrush"),
                });
            }

            notes.Children.Add(block);
        }

        panel.Children.Add(new ScrollViewer
        {
            Content = notes,
            MaxHeight = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 8, 0),
        });

        panel.Children.Add(new Border
        {
            Height = 1,
            Background = BrushOrDefault("CardStrokeColorDefaultBrush"),
        });

        var paths = new StackPanel { Spacing = 4 };
        paths.Children.Add(new TextBlock
        {
            Text = "日志与备份位置（点击“打开日志”可直接打开）",
            Style = StyleOrDefault("CaptionTextBlockStyle"),
            Foreground = BrushOrDefault("TextFillColorSecondaryBrush"),
        });
        paths.Children.Add(new TextBlock
        {
            Text = $"日志：{AppPaths.LogDirectory}\n备份：{AppPaths.BackupDirectory}",
            Style = StyleOrDefault("CaptionTextBlockStyle"),
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(paths);

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "设置",
            Content = panel,
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close,
        };

        await dialog.ShowAsync();
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
        ClearStalePrintCache = CleanCacheCheckBox.IsChecked ?? false,
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
        var confirmed = await ConfirmAsync(profile, options);
        if (!confirmed)
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
        StatusText.Text = $"正在执行「{profile.Description}」…";
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
            var engine = new RepairEngine(_log);
            var token = _cancellation.Token;

            // 关键：整个修复流程放到后台线程执行。
            // 修复步骤里包含大量同步等待（启动 PowerShell、读 WMI、读写注册表），
            // 如果在界面线程上跑，窗口会完全卡死、进度不刷新、取消和关闭都失效。
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
            _log.Write($"修复异常：{ex}");
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
                ? "请展开上方的失败步骤查看详情；如需还原，可使用日志目录同级 Backups 文件夹中的注册表备份。"
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
