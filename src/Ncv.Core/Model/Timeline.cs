using Ncv.Core.Format;

namespace Ncv.Core.Model;

/// <summary>
/// 샘플 인덱스 → 시각(초) 매핑 (DESIGN §2.2).
/// nrates ≥ 1: 구간별 t(n) = t(구간시작) + (n − n0)/samp — 샘플레이트가 DAT 타임스탬프보다 우선.
/// nrates = 0: t(n) = timestamp(n) × timemult (µs) → 초 환산.
/// </summary>
public sealed class Timeline
{
    private readonly double[] _times;

    /// <summary>트리거 샘플 인덱스 (0-base).</summary>
    public long TriggerIndex { get; }

    /// <summary>첫 샘플 절대 시각.</summary>
    public DateTime StartTime { get; }

    public int SampleCount => _times.Length;

    /// <summary>첫 샘플 기준 상대 시각(초).</summary>
    public double TimeAt(int index) => _times[index];

    /// <summary>트리거 기준 상대 시각(초).</summary>
    public double TimeFromTrigger(int index) =>
        _times[index] - (TriggerIndex >= 0 && TriggerIndex < _times.Length ? _times[(int)TriggerIndex] : 0);

    public double TotalSpan => _times.Length > 0 ? _times[^1] - _times[0] : 0;

    /// <summary>t ≤ time인 마지막 인덱스 (없으면 0). 팬/줌 가시 구간 → 샘플 범위 변환용.</summary>
    public int IndexAtOrBefore(double time)
    {
        int i = Array.BinarySearch(_times, time);
        if (i >= 0)
            return i;
        int ins = ~i;
        return Math.Max(0, ins - 1);
    }

    /// <summary>time에 가장 가까운 인덱스.</summary>
    public int NearestIndexOf(double time) => (int)NearestIndex(_times, time);

    private Timeline(double[] times, long triggerIndex, DateTime startTime)
    {
        _times = times;
        TriggerIndex = triggerIndex;
        StartTime = startTime;
    }

    public static Timeline Build(CfgDocument cfg, int sampleCount, double[] rawTimestamps)
    {
        var times = new double[sampleCount];
        long triggerIndex;

        if (cfg.NRates >= 1 && cfg.SampleRates.Count > 0)
        {
            int segIdx = 0;
            double segStartTime = 0;
            long segStartIndex = 0;
            var segs = cfg.SampleRates;
            for (int n = 0; n < sampleCount; n++)
            {
                while (segIdx < segs.Count - 1 && n >= segs[segIdx].EndSample)
                {
                    segStartTime += (segs[segIdx].EndSample - segStartIndex) / segs[segIdx].SamplesPerSecond;
                    segStartIndex = segs[segIdx].EndSample;
                    segIdx++;
                }

                times[n] = segStartTime + (n - segStartIndex) / segs[segIdx].SamplesPerSecond;
            }

            triggerIndex = Math.Min(cfg.TriggerSampleIndex, sampleCount - 1);
        }
        else
        {
            // 타임스탬프 기반: timestamp × timemult = µs
            for (int n = 0; n < sampleCount; n++)
                times[n] = rawTimestamps[n] * cfg.TimeMult / 1_000_000.0;

            double offset = (cfg.TriggerTime - cfg.StartTime).TotalSeconds;
            triggerIndex = NearestIndex(times, times.Length > 0 ? times[0] + offset : 0);
        }

        return new Timeline(times, triggerIndex, cfg.StartTime);
    }

    private static long NearestIndex(double[] times, double target)
    {
        if (times.Length == 0)
            return -1;
        int lo = Array.BinarySearch(times, target);
        if (lo >= 0)
            return lo;
        int ins = ~lo;
        if (ins <= 0)
            return 0;
        if (ins >= times.Length)
            return times.Length - 1;
        return target - times[ins - 1] <= times[ins] - target ? ins - 1 : ins;
    }
}
