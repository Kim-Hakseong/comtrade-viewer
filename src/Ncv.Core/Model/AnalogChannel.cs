namespace Ncv.Core.Model;

/// <summary>
/// CFG 아날로그 채널 정의 (1999 §행3: An,ch_id,ph,ccbm,uu,a,b,skew,min,max,primary,secondary,PS).
/// 실값 변환: actual = A × raw + B. PS(P/S)는 파싱만 하고 P0에서는 환산 미적용 (DESIGN §2.1).
/// </summary>
public sealed class AnalogChannel
{
    public required int Index { get; init; }
    public required string Id { get; init; }
    public required string Phase { get; init; }
    public required string Ccbm { get; init; }
    public required string Unit { get; init; }
    public required double A { get; init; }
    public required double B { get; init; }
    public required double Skew { get; init; }
    public required double Min { get; init; }
    public required double Max { get; init; }
    public required double Primary { get; init; }
    public required double Secondary { get; init; }
    public required string Ps { get; init; }

    public double Scale(double raw) => A * raw + B;
}
