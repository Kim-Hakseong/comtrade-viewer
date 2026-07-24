using System.Text;
using Ncv.Core.Format;
using Ncv.Core.Model;
using Xunit;

namespace Ncv.Core.Tests;

/// <summary>DESIGN §2.2 타임라인 규칙: 다중 샘플레이트 구간, nrates=0 타임스탬프 기반.</summary>
public class TimelineTests
{
    private static CfgDocument ParseCfg(string text)
    {
        using var ms = new MemoryStream(Encoding.ASCII.GetBytes(text));
        var result = CfgParser.Parse(ms);
        Assert.True(result.Success, result.ToString());
        return result.Value!;
    }

    private const string MultiRateCfg =
        "STN,DEV,1999\n" +
        "1,1A,0D\n" +
        "1,VA,A,,V,1.0,0.0,0,-32767,32767,1,1,S\n" +
        "60\n" +
        "2\n" +
        "1920,1920\n" +
        "960,2880\n" +
        "01/01/2026,00:00:00.000000\n" +
        "01/01/2026,00:00:01.500000\n" +
        "ASCII\n" +
        "1\n";

    [Fact]
    public void MultiRate_SegmentBoundaryTimes()
    {
        var cfg = ParseCfg(MultiRateCfg);
        var tl = Timeline.Build(cfg, 2880, Array.Empty<double>());

        // 구간1: 1920Hz × 1920샘플 → t(1920) = 1.0부터 960Hz
        Assert.Equal(0, tl.TimeAt(0), 12);
        Assert.Equal(1919 / 1920.0, tl.TimeAt(1919), 12);
        Assert.Equal(1.0, tl.TimeAt(1920), 12);
        Assert.Equal(1.0 + 1 / 960.0, tl.TimeAt(1921), 12);
        Assert.Equal(1.0 + 959 / 960.0, tl.TimeAt(2879), 12);
    }

    [Fact]
    public void MultiRate_TriggerIndexInSecondSegment()
    {
        var cfg = ParseCfg(MultiRateCfg);

        // 트리거 오프셋 1.5s: 구간1(1.0s) 지나 구간2에서 0.5s × 960Hz = 480 → 1920+480
        Assert.Equal(2400, cfg.TriggerSampleIndex);
    }

    private const string TimestampCfg =
        "STN,DEV,1999\n" +
        "1,1A,0D\n" +
        "1,VA,A,,V,1.0,0.0,0,-32767,32767,1,1,S\n" +
        "60\n" +
        "0\n" +
        "01/01/2026,00:00:00.000000\n" +
        "01/01/2026,00:00:00.000500\n" +
        "ASCII\n" +
        "2\n";

    [Fact]
    public void NRatesZero_UsesTimestampTimesTimemult()
    {
        var cfg = ParseCfg(TimestampCfg);
        Assert.Equal(0, cfg.NRates);
        Assert.Equal(2, cfg.TimeMult, 12);

        // timestamp × timemult = µs → 초. ts=[0,250,500] × 2µs
        var tl = Timeline.Build(cfg, 3, new double[] { 0, 250, 500 });
        Assert.Equal(0, tl.TimeAt(0), 12);
        Assert.Equal(0.0005, tl.TimeAt(1), 12);
        Assert.Equal(0.001, tl.TimeAt(2), 12);

        // 트리거 오프셋 500µs → 인덱스 1
        Assert.Equal(1, tl.TriggerIndex);
    }

    [Fact]
    public void NRatesZero_StandardStyleDummyRateRow_IsAccepted()
    {
        // 표준형: nrates=0이어도 무의미한 samp,endsamp 행이 존재
        string standardStyle = TimestampCfg.Replace("0\n01/01/2026,00:00:00.000000",
            "0\n0,0\n01/01/2026,00:00:00.000000");
        var cfg = ParseCfg(standardStyle);

        Assert.Equal(0, cfg.NRates);
        Assert.Empty(cfg.SampleRates);
        Assert.Equal(DataFileType.Ascii, cfg.DataType);
    }
}
