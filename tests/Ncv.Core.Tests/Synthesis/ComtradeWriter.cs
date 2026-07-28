using System.Globalization;
using System.Text;

namespace Ncv.Core.Tests.Synthesis;

/// <summary>합성 아날로그 채널: 수식 Signal(t) → raw = round((v − B)/A), int16 클램프.</summary>
public sealed class SyntheticAnalog
{
    public required string Id { get; init; }
    public string Phase { get; init; } = "";
    public required string Unit { get; init; }
    public required Func<double, double> Signal { get; init; }
    public required double A { get; init; }
    public double B { get; init; }
    public double Primary { get; init; } = 1;
    public double Secondary { get; init; } = 1;
}

public sealed class SyntheticDigital
{
    public required string Id { get; init; }
    public required Func<double, bool> Signal { get; init; }
}

public sealed class SyntheticSpec
{
    public string Station { get; init; } = "SYN_STATION";
    public string DevId { get; init; } = "SYN01";
    public int RevYear { get; init; } = 1999;
    public double LineFrequency { get; init; } = 60;
    public double SampleRate { get; init; } = 1920;
    public required int SampleCount { get; init; }
    public DateTime StartTime { get; init; } = new(2026, 1, 1, 0, 0, 0);
    public double TriggerOffsetSeconds { get; init; }
    /// <summary>ASCII | BINARY | BINARY32 | FLOAT32</summary>
    public string DataType { get; init; } = "ASCII";
    public double TimeMult { get; init; } = 1;
    public required IReadOnlyList<SyntheticAnalog> Analogs { get; init; }
    public IReadOnlyList<SyntheticDigital> Digitals { get; init; } = Array.Empty<SyntheticDigital>();
}

/// <summary>
/// 테스트 전용 COMTRADE 합성 Writer (라운드트립 검증 원칙). 제품 기능 아님.
/// </summary>
public static class ComtradeWriter
{
    public static string BuildCfgText(SyntheticSpec spec)
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        int a = spec.Analogs.Count;
        int d = spec.Digitals.Count;

        sb.Append(spec.Station).Append(',').Append(spec.DevId).Append(',')
            .Append(spec.RevYear.ToString(inv)).Append('\n');
        sb.Append((a + d).ToString(inv)).Append(',').Append(a.ToString(inv)).Append("A,")
            .Append(d.ToString(inv)).Append("D\n");

        for (int i = 0; i < a; i++)
        {
            var ch = spec.Analogs[i];
            sb.Append((i + 1).ToString(inv)).Append(',').Append(ch.Id).Append(',').Append(ch.Phase)
                .Append(",,").Append(ch.Unit).Append(',')
                .Append(Num(ch.A)).Append(',').Append(Num(ch.B))
                .Append(",0,-32767,32767,")
                .Append(Num(ch.Primary)).Append(',').Append(Num(ch.Secondary)).Append(",S\n");
        }

        for (int i = 0; i < d; i++)
            sb.Append((i + 1).ToString(inv)).Append(',').Append(spec.Digitals[i].Id).Append(",,,0\n");

        sb.Append(Num(spec.LineFrequency)).Append('\n');
        sb.Append("1\n");
        sb.Append(Num(spec.SampleRate)).Append(',').Append(spec.SampleCount.ToString(inv)).Append('\n');
        sb.Append(Stamp(spec.StartTime)).Append('\n');
        sb.Append(Stamp(spec.StartTime.AddTicks((long)Math.Round(spec.TriggerOffsetSeconds * TimeSpan.TicksPerSecond))))
            .Append('\n');
        sb.Append(spec.DataType).Append('\n');
        sb.Append(Num(spec.TimeMult)).Append('\n');
        return sb.ToString();
    }

    public static string BuildDatAsciiText(SyntheticSpec spec)
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        for (int n = 0; n < spec.SampleCount; n++)
        {
            double t = n / spec.SampleRate;
            sb.Append((n + 1).ToString(inv)).Append(',')
                .Append(((long)Math.Round(t * 1_000_000)).ToString(inv));
            foreach (var ch in spec.Analogs)
                sb.Append(',').Append(RawValue(ch, t).ToString(inv));
            foreach (var ch in spec.Digitals)
                sb.Append(',').Append(ch.Signal(t) ? '1' : '0');
            sb.Append('\n');
        }

        return sb.ToString();
    }

    public static byte[] BuildDatBinary(SyntheticSpec spec)
    {
        bool wide = spec.DataType is "BINARY32" or "FLOAT32";
        bool isFloat = spec.DataType == "FLOAT32";
        int a = spec.Analogs.Count;
        int d = spec.Digitals.Count;
        int words = (d + 15) / 16;
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        for (int n = 0; n < spec.SampleCount; n++)
        {
            double t = n / spec.SampleRate;
            bw.Write((uint)(n + 1));
            bw.Write((uint)Math.Round(t * 1_000_000));
            for (int i = 0; i < a; i++)
            {
                var ch = spec.Analogs[i];
                if (isFloat)
                    bw.Write((float)((ch.Signal(t) - ch.B) / ch.A));
                else if (wide)
                    bw.Write((int)RawValue(ch, t, wide: true));
                else
                    bw.Write((short)RawValue(ch, t));
            }

            for (int w = 0; w < words; w++)
            {
                ushort word = 0;
                for (int bit = 0; bit < 16; bit++)
                {
                    int chIdx = w * 16 + bit;
                    if (chIdx < d && spec.Digitals[chIdx].Signal(t))
                        word |= (ushort)(1 << bit); // LSB-first: d1 = bit0
                }

                bw.Write(word);
            }
        }

        bw.Flush();
        return ms.ToArray();
    }

    /// <summary>CFF 2013 단일파일 생성 (HDR 섹션 포함 — 리더가 무시하는지 검증용).</summary>
    public static byte[] BuildCff(SyntheticSpec spec, bool lowercaseHeaders = false)
    {
        string H(string s) => lowercaseHeaders ? s.ToLowerInvariant() : s;

        var head = new StringBuilder();
        head.Append(H("--- file type: CFG ---")).Append('\n');
        head.Append(BuildCfgText(spec));
        head.Append(H("--- file type: HDR ---")).Append('\n');
        head.Append("synthetic fixture, not a real recording\n");

        if (spec.DataType == "ASCII")
        {
            head.Append(H("--- file type: DAT ASCII ---")).Append('\n');
            head.Append(BuildDatAsciiText(spec));
            return Encoding.ASCII.GetBytes(head.ToString());
        }

        byte[] bin = BuildDatBinary(spec);
        head.Append(H($"--- file type: DAT {spec.DataType}: {bin.Length} ---")).Append('\n');
        byte[] headBytes = Encoding.ASCII.GetBytes(head.ToString());
        var result = new byte[headBytes.Length + bin.Length];
        headBytes.CopyTo(result, 0);
        bin.CopyTo(result, headBytes.Length);
        return result;
    }

    /// <summary>raw = round((v − B)/A), 필드 폭에 맞게 클램프 (테스트도 동일 양자화로 기대값 산출).</summary>
    public static long RawValue(SyntheticAnalog ch, double t, bool wide = false)
    {
        double raw = Math.Round((ch.Signal(t) - ch.B) / ch.A);
        return wide
            ? (long)Math.Clamp(raw, int.MinValue, int.MaxValue)
            : (long)Math.Clamp(raw, -32767, 32767);
    }

    public static MemoryStream ToStream(string text) => new(Encoding.ASCII.GetBytes(text));

    private static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private static string Stamp(DateTime dt) =>
        dt.ToString("dd/MM/yyyy,HH:mm:ss.ffffff", CultureInfo.InvariantCulture);
}
