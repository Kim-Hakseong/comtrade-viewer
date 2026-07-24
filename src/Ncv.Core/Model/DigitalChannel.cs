namespace Ncv.Core.Model;

/// <summary>
/// CFG 디지털 채널 정의 (1999: Dn,ch_id,ph,ccbm,y).
/// </summary>
public sealed class DigitalChannel
{
    public required int Index { get; init; }
    public required string Id { get; init; }
    public required string Phase { get; init; }
    public required string Ccbm { get; init; }

    /// <summary>정상 상태 (0 또는 1).</summary>
    public required int NormalState { get; init; }
}
