using System.Text;
using Ncv.Core.Format;
using Xunit;

namespace Ncv.Core.Tests;

/// <summary>DESIGN §7.1 CFG 골든 벡터 + §7.5 에러 처리. 골든 벡터 수정 금지.</summary>
public class CfgParserTests
{
    /// <summary>DESIGN §7.1 골든 CFG (리터럴, 수정 금지).</summary>
    public const string GoldenCfg =
        "TEST_STATION,DEV01,1999\n" +
        "4,2A,2D\n" +
        "1,VA,A,,V,0.048828,0.0,0,-32767,32767,13800,115,S\n" +
        "2,IA,A,,A,0.010000,0.0,0,-32767,32767,1200,5,S\n" +
        "1,TRIP52,,,0\n" +
        "2,PICKUP,,,0\n" +
        "60\n" +
        "1\n" +
        "1920,3840\n" +
        "01/01/2026,00:00:00.000000\n" +
        "01/01/2026,00:00:01.000000\n" +
        "ASCII\n" +
        "1\n";

    private static ParseResult<CfgDocument> ParseText(string text)
    {
        using var ms = new MemoryStream(Encoding.ASCII.GetBytes(text));
        return CfgParser.Parse(ms);
    }

    [Fact]
    public void GoldenCfg_ParsesHeaderAndCounts()
    {
        var result = ParseText(GoldenCfg);

        Assert.True(result.Success, result.Error);
        var doc = result.Value!;
        Assert.Equal("TEST_STATION", doc.StationName);
        Assert.Equal("DEV01", doc.RecDevId);
        Assert.Equal(1999, doc.RevYear);
        Assert.Equal(2, doc.AnalogCount);
        Assert.Equal(2, doc.DigitalCount);
        Assert.Equal(4, doc.TotalChannelCount);
    }

    [Fact]
    public void GoldenCfg_ParsesAnalogChannelVa()
    {
        var doc = ParseText(GoldenCfg).Value!;
        var va = doc.AnalogChannels[0];

        Assert.Equal(1, va.Index);
        Assert.Equal("VA", va.Id);
        Assert.Equal(0.048828, va.A, 12);
        Assert.Equal(0.0, va.B, 12);
        Assert.Equal("V", va.Unit);
        Assert.Equal(13800, va.Primary, 9);
        Assert.Equal(115, va.Secondary, 9);
        Assert.Equal("S", va.Ps);
    }

    [Fact]
    public void GoldenCfg_ParsesDigitalChannels()
    {
        var doc = ParseText(GoldenCfg).Value!;

        Assert.Equal("TRIP52", doc.DigitalChannels[0].Id);
        Assert.Equal("PICKUP", doc.DigitalChannels[1].Id);
        Assert.Equal(0, doc.DigitalChannels[0].NormalState);
    }

    [Fact]
    public void GoldenCfg_ParsesRatesAndMeta()
    {
        var doc = ParseText(GoldenCfg).Value!;

        Assert.Equal(60, doc.LineFrequency, 9);
        Assert.Equal(1, doc.NRates);
        Assert.Single(doc.SampleRates);
        Assert.Equal(1920, doc.SampleRates[0].SamplesPerSecond, 9);
        Assert.Equal(3840, doc.SampleRates[0].EndSample);
        Assert.Equal(DataFileType.Ascii, doc.DataType);
        Assert.Equal(1, doc.TimeMult, 9);
    }

    [Fact]
    public void GoldenCfg_TriggerIsOneSecondAfterStart_IndexIs1920()
    {
        var doc = ParseText(GoldenCfg).Value!;

        Assert.Equal(TimeSpan.FromSeconds(1), doc.TriggerTime - doc.StartTime);
        Assert.Equal(1920, doc.TriggerSampleIndex);
    }

    [Fact]
    public void GoldenCfg_ScaleRaw2048()
    {
        var doc = ParseText(GoldenCfg).Value!;
        double actual = doc.AnalogChannels[0].Scale(2048);

        Assert.Equal(99.999744, actual, 9);
    }

    // ---- §7.5 에러 처리 ----

    [Fact]
    public void ChannelCountMismatch_FailsAtLine2()
    {
        string bad = GoldenCfg.Replace("4,2A,2D", "4,3A,2D");
        var result = ParseText(bad);

        Assert.False(result.Success);
        Assert.Equal(2, result.LineNumber);
    }

    [Fact]
    public void MissingRevYear_FailsAtLine1()
    {
        string bad = GoldenCfg.Replace("TEST_STATION,DEV01,1999", "TEST_STATION,DEV01");
        var result = ParseText(bad);

        Assert.False(result.Success);
        Assert.Equal(1, result.LineNumber);
    }

    [Fact]
    public void UnsupportedRevYear_FailsAtLine1()
    {
        string bad = GoldenCfg.Replace("1999\n", "1991\n");
        var result = ParseText(bad);

        Assert.False(result.Success);
        Assert.Equal(1, result.LineNumber);
    }

    [Fact]
    public void MalformedAnalogRow_FailsWithRowNumber()
    {
        string bad = GoldenCfg.Replace(
            "1,VA,A,,V,0.048828,0.0,0,-32767,32767,13800,115,S",
            "1,VA,A,,V,0.048828");
        var result = ParseText(bad);

        Assert.False(result.Success);
        Assert.Equal(3, result.LineNumber);
    }

    [Fact]
    public void TruncatedFile_FailsWithMissingRowMessage()
    {
        // ft 행 이후(timemult) 잘림
        string truncated = GoldenCfg[..GoldenCfg.LastIndexOf("ASCII", StringComparison.Ordinal)];
        var result = ParseText(truncated);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void UnknownFileType_Fails()
    {
        string bad = GoldenCfg.Replace("ASCII", "FLOAT64");
        var result = ParseText(bad);

        Assert.False(result.Success);
        Assert.Contains("FLOAT64", result.Error);
    }

    [Fact]
    public void CrlfLineEndings_ParseIdentically()
    {
        string crlf = GoldenCfg.Replace("\n", "\r\n");
        var result = ParseText(crlf);

        Assert.True(result.Success, result.Error);
        Assert.Equal(1920, result.Value!.TriggerSampleIndex);
    }
}
