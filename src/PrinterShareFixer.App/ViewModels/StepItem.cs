using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PrinterShareFixer.Core.Models;

namespace PrinterShareFixer.App.ViewModels;

/// <summary>界面上的一个修复步骤（可绑定）。</summary>
public sealed class StepItem : INotifyPropertyChanged
{
    private string _message = "等待执行…";
    private string _detailText = string.Empty;
    private string _glyph = "\uE738";
    private Brush _accent = new SolidColorBrush(Colors.Gray);

    public StepItem(RepairStep step, int index)
    {
        Step = step;
        Index = index;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RepairStep Step { get; }

    public int Index { get; }

    public string Title => Step.Title;

    public string Description => Step.Description ?? string.Empty;

    public string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    public string DetailText
    {
        get => _detailText;
        private set
        {
            if (Set(ref _detailText, value))
            {
                OnPropertyChanged(nameof(DetailVisibility));
            }
        }
    }

    public Visibility DetailVisibility => string.IsNullOrWhiteSpace(_detailText) ? Visibility.Collapsed : Visibility.Visible;

    public string Glyph
    {
        get => _glyph;
        private set => Set(ref _glyph, value);
    }

    public Brush Accent
    {
        get => _accent;
        private set => Set(ref _accent, value);
    }

    public void Apply(StepState state, string message, IReadOnlyList<string>? details)
    {
        Message = message;
        Glyph = state switch
        {
            StepState.Succeeded => "\uE73E",
            StepState.Warning => "\uE7BA",
            StepState.Failed => "\uE783",
            StepState.Skipped => "\uE738",
            StepState.Running => "\uE72C",
            _ => "\uE738",
        };

        Accent = new SolidColorBrush(state switch
        {
            StepState.Succeeded => Colors.SeaGreen,
            StepState.Warning => Colors.DarkOrange,
            StepState.Failed => Colors.IndianRed,
            StepState.Running => Colors.DodgerBlue,
            StepState.Skipped => Colors.Gray,
            _ => Colors.Gray,
        });

        DetailText = details is { Count: > 0 } ? string.Join(Environment.NewLine, details) : string.Empty;
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
