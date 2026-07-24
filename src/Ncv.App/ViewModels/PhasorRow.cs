using Avalonia.Media;

namespace Ncv.App.ViewModels;

/// <summary>페이저 표 한 행 + 극좌표 벡터 데이터 (C-12).</summary>
public sealed record PhasorRow(
    string Name, IBrush Brush, double Magnitude, double AngleDegrees, string Unit)
{
    public string MagnitudeText =>
        Magnitude.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
        (Unit.Length > 0 ? $" {Unit}" : "");

    public string AngleText =>
        AngleDegrees.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "°";
}
