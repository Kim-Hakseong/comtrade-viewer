namespace Ncv.Core.Analysis;

public readonly record struct MinMax(double Min, double Max);

/// <summary>
/// 픽셀 버킷당 (min,max) 봉투 데시메이션 (DESIGN §4). 순수 함수 — 골든 벡터 테스트 대상.
/// </summary>
public static class Decimator
{
    /// <summary>
    /// [startIdx, endIdx) 구간을 buckets개로 나눠 버킷별 (min,max)를 산출한다.
    /// 버킷당 샘플이 2개 이하면 원본을 그대로 통과시킨다 (샘플당 min=max 1개, 줌인 시 실선 연결).
    /// </summary>
    public static MinMax[] Decimate(double[] data, int startIdx, int endIdx, int buckets)
    {
        startIdx = Math.Max(0, startIdx);
        endIdx = Math.Min(data.Length, endIdx);
        int count = endIdx - startIdx;
        if (count <= 0 || buckets <= 0)
            return Array.Empty<MinMax>();

        if (count <= buckets * 2)
        {
            var passthrough = new MinMax[count];
            for (int i = 0; i < count; i++)
            {
                double v = data[startIdx + i];
                passthrough[i] = new MinMax(v, v);
            }

            return passthrough;
        }

        var result = new MinMax[buckets];
        for (int b = 0; b < buckets; b++)
        {
            int from = startIdx + (int)((long)count * b / buckets);
            int to = startIdx + (int)((long)count * (b + 1) / buckets);
            double min = data[from], max = data[from];
            for (int i = from + 1; i < to; i++)
            {
                double v = data[i];
                if (v < min)
                    min = v;
                else if (v > max)
                    max = v;
            }

            result[b] = new MinMax(min, max);
        }

        return result;
    }
}
