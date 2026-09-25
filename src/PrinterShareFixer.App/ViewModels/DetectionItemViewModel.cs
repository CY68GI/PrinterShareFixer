using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using PrinterShareFixer.Core.Models;

namespace PrinterShareFixer.App.ViewModels;

/// <summary>检测结果的一行。</summary>
public sealed class DetectionItemViewModel
{
    public DetectionItemViewModel(DetectionItem item)
    {
        Category = item.Category;
        Title = item.Title;
        Detail = item.Detail;
        Glyph = item.Status switch
        {
            DetectionStatus.Ok => "\uE73E",
            DetectionStatus.Warning => "\uE7BA",
            DetectionStatus.Problem => "\uE783",
            _ => "\uE946",
        };

        Accent = new SolidColorBrush(item.Status switch
        {
            DetectionStatus.Ok => Colors.SeaGreen,
            DetectionStatus.Warning => Colors.DarkOrange,
            DetectionStatus.Problem => Colors.IndianRed,
            _ => Colors.SlateGray,
        });
    }

    public string Category { get; }

    public string Title { get; }

    public string Detail { get; }

    public string Glyph { get; }

    public Brush Accent { get; }
}
