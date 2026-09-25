using System.Diagnostics;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PrinterShareFixer.Core;
using PrinterShareFixer.Core.Runtime;
using PrinterShareFixer.Core.Update;

namespace PrinterShareFixer.App.Pages;

/// <summary>设置页面：版本号、检查更新/一键更新、更新日志、日志与备份位置。</summary>
public sealed partial class SettingsPage : Page
{
    private readonly AppState _state = AppState.Current;
    private UpdateRelease? _availableRelease;

    public SettingsPage()
    {
        InitializeComponent();
        LoadInfo();
        BuildChangelog();
    }

    private static Brush BrushOrDefault(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);

    private static Style? StyleOrDefault(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) ? value as Style : null;

    private void LoadInfo()
    {
        try
        {
            var png = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.png");
            if (File.Exists(png))
            {
                AppIcon.Source = new BitmapImage(new Uri(png));
            }
        }
        catch
        {
            // 图标缺失时忽略
        }

        VersionText.Text = $"版本 {AppInfo.Version}（{(UpdateChecker.IsSelfContainedInstall ? "自带运行时" : "需要 .NET 10 运行时")}）";

        var os = SystemInfo.Read();
        EnvironmentText.Text = $"运行环境：{os.OsSummary}　计算机名：{_state.MachineName}　用户：{_state.UserName}（{_state.UserTypeText}）";

        PathsText.Text = $"日志：{AppPaths.LogDirectory}{Environment.NewLine}备份：{AppPaths.BackupDirectory}";

        if (_state.UpdateAvailable)
        {
            UpdateStatusText.Text = $"已知有新版本 v{_state.NewerVersion}，点击「检查更新」查看详情并更新。";
        }
    }

    private void BuildChangelog()
    {
        var blocks = new List<UIElement>();

        foreach (var note in AppInfo.ReleaseNotes)
        {
            var block = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 0) };
            block.Children.Add(new TextBlock
            {
                Text = $"v{note.Version}　{note.Date}",
                FontWeight = FontWeights.SemiBold,
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

            blocks.Add(block);
        }

        ChangelogList.ItemsSource = blocks;
    }

    // ---------- 更新 ----------

    private async void OnCheckUpdateClick(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        ApplyUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "正在检查更新…";

        var result = await UpdateChecker.CheckAsync();
        switch (result.Status)
        {
            case UpdateCheckStatus.UpdateAvailable when result.Release is not null:
                _availableRelease = result.Release;
                _state.ApplyUpdateState(result.Release.Version.ToString());
                UpdateStatusText.Text = $"发现新版本 v{result.Release.Version}（当前 v{AppInfo.Version}），点击「立即更新并重启」即可。";
                UpdateNotesText.Text = FormatNotes(result.Release.Notes);
                UpdateNotesText.Visibility = Visibility.Visible;
                ApplyUpdateButton.Visibility = Visibility.Visible;
                ApplyUpdateButton.IsEnabled = true;
                break;

            case UpdateCheckStatus.UpToDate:
                _availableRelease = null;
                _state.ApplyUpdateState(null);
                UpdateStatusText.Text = result.Message;
                UpdateNotesText.Visibility = Visibility.Collapsed;
                ApplyUpdateButton.Visibility = Visibility.Collapsed;
                break;

            default:
                UpdateStatusText.Text = $"{result.Message}（可以点「打开下载页」手动下载）";
                break;
        }

        CheckUpdateButton.IsEnabled = true;
    }

    private async void OnApplyUpdateClick(object sender, RoutedEventArgs e)
    {
        var release = _availableRelease;
        if (release is null)
        {
            return;
        }

        try
        {
            ApplyUpdateButton.IsEnabled = false;
            CheckUpdateButton.IsEnabled = false;
            UpdateProgress.Visibility = Visibility.Visible;
            UpdateProgress.Value = 0;
            UpdateStatusText.Text = "正在下载更新包…";

            var report = new Progress<UpdateDownloadProgress>(update =>
            {
                UpdateStatusText.Text = update.Percent is null
                    ? $"{update.Stage}…"
                    : $"{update.Stage}… {update.Percent:F0}%（{update.BytesReceived / 1024d / 1024:F1} MB）";

                if (update.Percent is not null)
                {
                    UpdateProgress.Value = update.Percent.Value;
                }
            });

            var staged = await UpdateDownloader.DownloadAndStageAsync(
                release,
                UpdateChecker.IsSelfContainedInstall,
                report);

            _state.Log.Write($"更新包已就绪：{staged.AppDirectory}（来自 {staged.Asset.Name}）");
            UpdateStatusText.Text = $"更新包已下载并校验通过，程序即将重启完成更新到 v{staged.Version}…";
            UpdateProgress.Value = 100;

            UpdateApplier.StartUpdater(
                staged.AppDirectory,
                AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),
                relaunch: true);

            await Task.Delay(800);
            Application.Current.Exit();
        }
        catch (Exception ex)
        {
            _state.Log.Write($"自动更新失败：{ex}");
            UpdateProgress.Visibility = Visibility.Collapsed;
            ApplyUpdateButton.IsEnabled = true;
            CheckUpdateButton.IsEnabled = true;
            UpdateStatusText.Text = $"更新失败：{ex.Message}。可以点「打开下载页」手动下载。";
        }
    }

    private void OnOpenDownloadPageClick(object sender, RoutedEventArgs e) =>
        OpenUrl(_availableRelease?.HtmlUrl ?? UpdateChecker.ReleasesPageUrl);

    private void OnOpenLogFolderClick(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.LogDirectory);

    private void OnOpenBackupFolderClick(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.BackupDirectory);

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // 打不开时忽略
        }
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

    private static string FormatNotes(string notes) => string.IsNullOrWhiteSpace(notes)
        ? string.Empty
        : string.Join(
            Environment.NewLine,
            notes.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Trim().Length > 0).Take(12));
}
