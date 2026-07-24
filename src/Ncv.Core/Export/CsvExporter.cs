using System.Globalization;
using Ncv.Core.Model;

namespace Ncv.Core.Export;

/// <summary>
/// CSV 내보내기 (C-13): 표시 중 채널·구간을 스케일 적용 실값으로 기록.
/// 첫 열은 시간(초), 이후 선택한 아날로그(실값)·디지털(0/1) 채널 순.
/// </summary>
public static class CsvExporter
{
    public static void Write(TextWriter writer, ComtradeRecord rec,
        int startIdx, int endIdxExclusive,
        IReadOnlyList<int> analogIndices, IReadOnlyList<int> digitalIndices,
        bool relativeToTrigger = false)
    {
        var inv = CultureInfo.InvariantCulture;
        startIdx = Math.Max(0, startIdx);
        endIdxExclusive = Math.Min(rec.SampleCount, endIdxExclusive);

        double timeOffset = 0;
        if (relativeToTrigger && rec.Time.TriggerIndex >= 0)
            timeOffset = rec.Time.TimeAt((int)rec.Time.TriggerIndex);

        // 헤더
        writer.Write(relativeToTrigger ? "time_from_trigger_s" : "time_s");
        foreach (int a in analogIndices)
        {
            var ch = rec.Cfg.AnalogChannels[a];
            writer.Write(',');
            writer.Write(Escape(ch.Unit.Length > 0 ? $"{ch.Id} [{ch.Unit}]" : ch.Id));
        }

        foreach (int d in digitalIndices)
        {
            writer.Write(',');
            writer.Write(Escape(rec.Cfg.DigitalChannels[d].Id));
        }

        writer.Write('\n');

        // 데이터 행
        for (int n = startIdx; n < endIdxExclusive; n++)
        {
            writer.Write((rec.Time.TimeAt(n) - timeOffset).ToString("R", inv));
            foreach (int a in analogIndices)
            {
                writer.Write(',');
                writer.Write(rec.Analog[a][n].ToString("R", inv));
            }

            foreach (int d in digitalIndices)
            {
                writer.Write(',');
                writer.Write(rec.Digital[d][n] ? '1' : '0');
            }

            writer.Write('\n');
        }
    }

    /// <summary>콤마/따옴표/개행 포함 필드는 RFC 4180 방식으로 인용.</summary>
    public static string Escape(string field)
    {
        if (field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
            return field;
        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }
}
