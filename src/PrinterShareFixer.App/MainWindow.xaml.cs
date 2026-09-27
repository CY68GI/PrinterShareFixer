using Microsoft.UI.Windowing;
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
    private const int PreferredWidth = 1220;
    private const int PreferredHeight = 1000;

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

        CenterOnWorkArea();

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

    /// <summary>
    /// 在“工作区”（屏幕减去任务栏）里居中显示，并保证窗口不置顶。
    /// 这样窗口既不会压住任务栏，也不会因为尺寸超过屏幕而跑到屏幕外。
    /// </summary>
    private void CenterOnWorkArea()
    {
        try
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = false;
            }

            var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            var work = display.WorkArea;

            var width = Math.Min(PreferredWidth, (int)(work.Width * 0.95));
            var height = Math.Min(PreferredHeight, (int)(work.Height * 0.95));
            var x = work.X + Math.Max(0, (work.Width - width) / 2);
            var y = work.Y + Math.Max(0, (work.Height - height) / 2);

            AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        }
        catch (Exception ex)
        {
            _state.Log.Write($"设置窗口位置失败：{ex.Message}");
        }
    }

    /// <summary>窗口图标（exe 图标由清单提供，这里设置窗口与任务栏图标）。</summary>
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