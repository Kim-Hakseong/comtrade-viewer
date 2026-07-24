namespace Ncv.Core.Format;

/// <summary>DAT 파일에서 읽은 원시 데이터 (스케일 미적용).</summary>
public sealed class DatData
{
    public required long[] SampleNumbers { get; init; }

    /// <summary>DAT 타임스탬프 필드 원시값. 비어 있으면 NaN.</summary>
    public required double[] Timestamps { get; init; }

    /// <summary>[채널][샘플] raw 값.</summary>
    public required double[][] AnalogRaw { get; init; }

    /// <summary>[채널][샘플].</summary>
    public required bool[][] Digital { get; init; }

    public int SampleCount => SampleNumbers.Length;
}
