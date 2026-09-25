using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PrinterShareFixer.App.ViewModels;
using PrinterShareFixer.Core.Runtime;

namespace PrinterShareFixer.App.Pages;

/// <summary>
/// 打印机信息页面：显示本机（计算机名 / 用户与类型 / IPv4 / 网卡），
/// 以及本机安装的全部打印机、哪些已经共享。
/// </summary>
public sealed partial class PrintersPage : Page
{
    private readonly AppState _state = AppState.Current;
    private bool _loaded;

    public PrintersPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _state.SnapshotChanged += OnSnapshotChanged;
        await RefreshAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _state.SnapshotChanged -= OnSnapshotChanged;

    private void OnSnapshotChanged()
    {
        if (_state.Snapshot is { } snapshot)
        {
            RenderPrinters(snapshot);
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void OnOpenPrintersClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("control", "printers") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _state.Log.Write($"打开打印机设置失败：{ex.Message}");
        }
    }

    private async Task RefreshAsync()
    {
        RefreshRing.IsActive = true;
        RefreshButton.IsEnabled = false;

        try
        {
            RenderMachineInfo();
            var snapshot = await _state.RefreshSnapshotAsync(force: true);
            RenderPrinters(snapshot);
        }
        catch (Exception ex)
        {
            _state.Log.Write($"刷新打印机信息失败：{ex.Message}");
            PrinterSummaryText.Text = $"读取打印机信息失败：{ex.Message}";
        }
        finally
        {
            RefreshRing.IsActive = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private void RenderMachineInfo()
    {
        MachineNameText.Text = _state.MachineName;
        UserText.Text = $"{_state.UserName}（{_state.UserTypeText}）";

        var adapters = LocalNetwork.GetIpv4Addresses();
        AdapterList.ItemsSource = adapters.Select(a => new AdapterRow(a)).ToList();

        IpText.Text = adapters.Count == 0
            ? "未检测到 IPv4 地址（网线未插或未获取到 IP）"
            : string.Join("　|　", adapters.Select(a => $"{a.AdapterName}：{a.IPv4}"));
    }

    private void RenderPrinters(SystemSnapshot snapshot)
    {
        var printers = snapshot.Printers;
        var shared = printers.Count(p => p.Shared);
        var remote = printers.Count(p => p.IsRemote);

        PrinterSummaryText.Text = printers.Count == 0
            ? "本机没有安装打印机"
            : $"本机共 {printers.Count} 台打印机：已共享 {shared} 台，网络连接 {remote} 台";

        PrinterList.ItemsSource = printers
            .OrderByDescending(p => p.Shared)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new PrinterRow(p))
            .ToList();
    }
}
