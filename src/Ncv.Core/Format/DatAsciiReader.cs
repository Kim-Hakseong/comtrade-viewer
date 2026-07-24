using System.Globalization;

namespace Ncv.Core.Format;

/// <summary>
/// DAT ASCII 파서 (DESIGN §2.3): 행당 1샘플, `n, timestamp, a1..aA, d1..dD` 콤마 구분.
/// 아날로그는 raw 정수(실수 허용), 디지털은 0/1.
/// </summary>
public static class DatAsciiReader
{
    public static ParseResult<DatData> Read(Stream stream, CfgDocument cfg, IProgress<double>? progress = null)
    {
        int a = cfg.AnalogCount;
        int d = cfg.DigitalCount;
        int expectedFields = 2 + a + d;

        var sampleNumbers = new List<long>();
        var timestamps = new List<double>();
        var analogRows = new List<double[]>();
        var digitalRows = new List<bool[]>();

        using var reader = new StreamReader(stream, leaveOpen: true);
        string? line;
        int lineNo = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            string[] f = line.Split(',');
            if (f.Length != expectedFields)
                return ParseResult<DatData>.Fail(lineNo,
                    $"필드 수 불일치: 기대 {expectedFields}(n,timestamp,{a}아날로그,{d}디지털), 실제 {f.Length}");

            if (!long.TryParse(f[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long n))
                return ParseResult<DatData>.Fail(lineNo, $"샘플 번호를 해석할 수 없습니다: '{f[0].Trim()}'");

            double ts = double.NaN;
            string tsField = f[1].Trim();
            if (tsField.Length > 0 &&
                !double.TryParse(tsField, NumberStyles.Float, CultureInfo.InvariantCulture, out ts))
                return ParseResult<DatData>.Fail(lineNo, $"타임스탬프를 해석할 수 없습니다: '{tsField}'");

            var analogRow = new double[a];
            for (int i = 0; i < a; i++)
            {
                if (!double.TryParse(f[2 + i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                        out analogRow[i]))
                    return ParseResult<DatData>.Fail(lineNo,
                        $"아날로그 채널 {i + 1} 값을 해석할 수 없습니다: '{f[2 + i].Trim()}'");
            }

            var digitalRow = new bool[d];
            for (int i = 0; i < d; i++)
            {
                string v = f[2 + a + i].Trim();
                if (v == "0")
                    digitalRow[i] = false;
                else if (v == "1")
                    digitalRow[i] = true;
                else
                    return ParseResult<DatData>.Fail(lineNo, $"디지털 채널 {i + 1} 값은 0/1이어야 합니다: '{v}'");
            }

            sampleNumbers.Add(n);
            timestamps.Add(ts);
            analogRows.Add(analogRow);
            digitalRows.Add(digitalRow);

            if (progress is not null && cfg.DeclaredSampleCount > 0 && sampleNumbers.Count % 4096 == 0)
                progress.Report(Math.Min(1.0, (double)sampleNumbers.Count / cfg.DeclaredSampleCount));
        }

        if (sampleNumbers.Count == 0)
            return ParseResult<DatData>.Fail(0, "DAT에 샘플이 없습니다.");

        long declared = cfg.DeclaredSampleCount;
        if (declared >= 0 && sampleNumbers.Count != declared)
            return ParseResult<DatData>.Fail(lineNo,
                $"샘플 수 불일치: CFG 기대 {declared}, DAT 실제 {sampleNumbers.Count}");

        return ParseResult<DatData>.Ok(ToColumnar(sampleNumbers, timestamps, analogRows, digitalRows, a, d));
    }

    /// <summary>행 단위 파싱 결과를 [채널][샘플] 배열로 전치.</summary>
    internal static DatData ToColumnar(
        List<long> sampleNumbers, List<double> timestamps,
        List<double[]> analogRows, List<bool[]> digitalRows, int a, int d)
    {
        int count = sampleNumbers.Count;
        var analog = new double[a][];
        for (int c = 0; c < a; c++)
        {
            analog[c] = new double[count];
            for (int nIdx = 0; nIdx < count; nIdx++)
                analog[c][nIdx] = analogRows[nIdx][c];
        }

        var digital = new bool[d][];
        for (int c = 0; c < d; c++)
        {
            digital[c] = new bool[count];
            for (int nIdx = 0; nIdx < count; nIdx++)
                digital[c][nIdx] = digitalRows[nIdx][c];
        }

        return new DatData
        {
            SampleNumbers = sampleNumbers.ToArray(),
            Timestamps = timestamps.ToArray(),
            AnalogRaw = analog,
            Digital = digital,
        };
    }
}
