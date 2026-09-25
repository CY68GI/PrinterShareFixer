using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PrinterShareFixer.App.Pages;
using PrinterShareFixer.Core;
using PrinterShareFixer.Core.Runtime;
using Windows.Graphics;

namespace PrinterShareFixer.App;

/// <summary>
/// 应用外壳：左侧导航（修复 / 打印机信息 / 设置），顶部展示机器与权限状态，
/// 具体功能都在 ContentFrame 加载的页面里。
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly AppState _state = AppState.Current;
    private BitmapImage? _iconImage;

    public MainWindow()
    {
        InitializeComponent();

        Title = AppInfo.ProductName;
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
            AppWindow.Resize(new SizeInt32(1220, 1000));
        }
        catch
        {
            // 忽略窗口尺寸设置失败
        }

        try
        {
            AppWindow.Closing += (_, _) => (ContentFrame.Content as RepairPage)?.CancelRunningRepair();
        }
        catch
        {
            // 拿不到 AppWindow 时忽略
        }

        _state.UpdateStateChanged += UpdateSettingsBadge;
        Closed += OnWindowClosed;

        UpdateHeader();
    }

    /// <summary>标题栏图标（exe 图标由清单提供，这里设置窗口与任务栏图标）。</summary>
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
            _state.Log.Write($"设置应用图标失败：{ex.Message}");
        }
    }

    /// <summary>顶部信息：系统版本、计算机名、当前 IPv4（都走注册表/网络接口，毫秒级返回）。</summary>
    private void UpdateHeader()
    {
        var os = SystemInfo.Read();
        var ip = LocalNetwork.PrimaryIPv4() ?? "未检测到";
        SubtitleText.Text =
            $"当前系统：{os.OsSummary}　计算机名：{System.Environment.MachineName}　IPv4：{ip}";
    }

    private void UpdateAdminBar()
    {
        if (_state.IsElevated)
        {
            AdminInfoBar.Severity = InfoBarSeverity.Success;
            AdminInfoBar.Title = "已以管理员身份运行";
            AdminInfoBar.Message = "可以修改服务、防火墙和注册表。修复前会自动备份相关注册表项。";
        }
        else
        {
            AdminInfoBar.Severity = InfoBarSeverity.Error;
            AdminInfoBar.Title = "未获得管理员权限";
            AdminInfoBar.Message = "请关闭程序，右键选择“以管理员身份运行”，否则无法修改服务与注册表。";
        }
    }

    private void UpdateSettingsBadge()
    {
        try
        {
            if (Nav.SettingsItem is NavigationViewItem settingsItem)
            {
                settingsItem.InfoBadge = _state.UpdateAvailable ? new InfoBadge { Value = 1 } : null;
            }
        }
        catch
        {
            // 导航项还没生成时忽略
        }
    }

    private async void OnNavLoaded(object sender, RoutedEventArgs e)
    {
        Nav.SelectedItem = Nav.MenuItems[0];
        UpdateAdminBar();
        UpdateHeader();
        UpdateSettingsBadge();

        await _state.CheckForUpdatesAsync();
        UpdateSettingsBadge();
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            Navigate(typeof(SettingsPage));
            return;
        }

        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            Navigate(tag switch
            {
                "printers" => typeof(PrintersPage),
                _ => typeof(RepairPage),
            });
        }
    }

    private void Navigate(Type pageType)
    {
        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        (ContentFrame.Content as RepairPage)?.CancelRunningRepair();
        _state.Dispose();
    }
}
