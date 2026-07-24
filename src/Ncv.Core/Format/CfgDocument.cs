using Ncv.Core.Model;

namespace Ncv.Core.Format;

/// <summary>DAT 데이터 타입 (1999: ASCII/BINARY, 2013 추가: BINARY32/FLOAT32).</summary>
public enum DataFileType
{
    Ascii,
    Binary,
    Binary32,
    Float32,
}

/// <summary>샘플레이트 구간: samp(Hz), endsamp(해당 구간 마지막 샘플 번호, 1-base).</summary>
public sealed record SampleRateSegment(double SamplesPerSecond, long EndSample);

/// <summary>
/// 파싱된 CFG 메타 전부 (DESIGN §2.1).
/// </summary>
public sealed class CfgDocument
{
    public required string StationName { get; init; }
    public required string RecDevId { get; init; }
    public required int RevYear { get; init; }
    public required int TotalChannelCount { get; init; }
    public required IReadOnlyList<AnalogChannel> AnalogChannels { get; init; }
    public required IReadOnlyList<DigitalChannel> DigitalChannels { get; init; }
    public required double LineFrequency { get; init; }

    /// <summary>nrates. 0이면 샘플레이트 미지정 → 타임스탬프 기반 타임라인.</summary>
    public required int NRates { get; init; }

    public required IReadOnlyList<SampleRateSegment> SampleRates { get; init; }
    public required DateTime StartTime { get; init; }
    public required DateTime TriggerTime { get; init; }
    public required DataFileType DataType { get; init; }
    public required double TimeMult { get; init; }

    public int AnalogCount => AnalogChannels.Count;
    public int DigitalCount => DigitalChannels.Count;

    /// <summary>샘플레이트 지정 시 총 샘플 수 (마지막 endsamp). 미지정(nrates=0)이면 -1.</summary>
    public long DeclaredSampleCount => SampleRates.Count > 0 ? SampleRates[^1].EndSample : -1;

    /// <summary>
    /// 트리거 시각에 해당하는 샘플 인덱스 (0-base). 샘플레이트 미지정이면 -1.
    /// 구간별 t(n) = t(구간시작) + (n - n0)/samp 규칙의 역산 (DESIGN §2.2).
    /// </summary>
    public long TriggerSampleIndex
    {
        get
        {
            if (SampleRates.Count == 0)
                return -1;

            double offset = (TriggerTime - StartTime).TotalSeconds;
            if (offset <= 0)
                return 0;

            double segStartTime = 0;
            long segStartIndex = 0;
            foreach (var seg in SampleRates)
            {
                long segSampleCount = seg.EndSample - segStartIndex;
                double segDuration = segSampleCount / seg.SamplesPerSecond;
                if (offset <= segStartTime + segDuration || seg == SampleRates[^1])
                {
                    long idx = segStartIndex + (long)Math.Round((offset - segStartTime) * seg.SamplesPerSecond);
                    return Math.Min(idx, seg.EndSample - 1);
                }

                segStartTime += segDuration;
                segStartIndex = seg.EndSample;
            }

            return SampleRates[^1].EndSample - 1;
        }
    }
}
