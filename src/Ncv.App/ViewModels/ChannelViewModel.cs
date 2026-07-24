using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Ncv.App.ViewModels;

/// <summary>채널 트리 항목: 표시 토글 + 자동 배정 색상 + 채널명·단위 (C-08).</summary>
public partial class ChannelViewModel : ViewModelBase
{
    /// <summary>고정 12색 팔레트 순환 (DESIGN §5).</summary>
    public static readonly Color[] Palette =
    {
        Color.Parse("#1F6FEB"), Color.Parse("#D6262E"), Color.Parse("#2DA44E"),
        Color.Parse("#E36209"), Color.Parse("#8250DF"), Color.Parse("#0891B2"),
        Color.Parse("#BF3989"), Color.Parse("#B58900"), Color.Parse("#16171A"),
        Color.Parse("#6E7781"), Color.Parse("#9C4221"), Color.Parse("#58A6FF"),
    };

    public required string Name { get; init; }
    public required string Unit { get; init; }
    public required bool IsDigital { get; init; }

    /// <summary>Record.Analog / Record.Digital 배열 내 인덱스.</summary>
    public required int ChannelIndex { get; init; }

    public required Color Color { get; init; }

    public IBrush Brush => new SolidColorBrush(Color);

    public string DisplayName => Unit.Length > 0 ? $"{Name} [{Unit}]" : Name;

    [ObservableProperty]
    private bool _isVisible = true;
}
