using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using PrinterShareFixer.Core.Runtime;

namespace PrinterShareFixer.App.ViewModels;

/// <summary>打印机列表里的一行。</summary>
public sealed class PrinterRow
{
    public PrinterRow(PrinterDetail detail)
    {
        Name = detail.Name;
        DriverName = string.IsNullOrWhiteSpace(detail.DriverName) ? "—" : detail.DriverName;
        PortText = string.IsNullOrWhiteSpace(detail.PortText) ? "—" : detail.PortText;
        StatusText = string.IsNullOrWhiteSpace(detail.Status) ? "—" : detail.Status;
        ShareText = detail.IsRemote
            ? "网络共享打印机"
            : detail.Shared ? "已共享" : "未共享";
        ShareGlyph = detail.IsRemote ? "\uE71B" : detail.Shared ? "\uE73E" : "\uE738";
        ShareBrush = new SolidColorBrush(detail.IsRemote
            ? Colors.SlateGray
            : detail.Shared ? Colors.SeaGreen : Colors.DarkOrange);
    }

    public string Name { get; }

    public string ShareText { get; }

    public string ShareGlyph { get; }

    public Brush ShareBrush { get; }

    public string DriverName { get; }

    public string PortText { get; }

    public string StatusText { get; }
}

/// <summary>网卡列表里的一行。</summary>
public sealed class AdapterRow
{
    public AdapterRow(NetworkAddress address)
    {
        AdapterName = address.AdapterName;
        Description = address.Description;
        IPv4 = address.IPv4;
        Mac = address.Mac;
        SpeedText = address.SpeedText;
    }

    public string AdapterName { get; }

    public string Description { get; }

    public string IPv4 { get; }

    public string Mac { get; }

    public string SpeedText { get; }
}
