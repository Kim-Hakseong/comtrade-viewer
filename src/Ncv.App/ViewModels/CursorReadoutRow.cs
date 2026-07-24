using Avalonia.Media;

namespace Ncv.App.ViewModels;

/// <summary>커서 측정 표 한 행: 채널명 + C1/C2 값 + Δ (C-06).</summary>
public sealed record CursorReadoutRow(string Name, IBrush Brush, string V1, string V2, string Delta);
