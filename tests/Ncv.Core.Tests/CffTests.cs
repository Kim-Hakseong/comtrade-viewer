using System.Text;
using Ncv.Core.Format;
using Ncv.Core.Model;
using Ncv.Core.Tests.Synthesis;
using Xunit;

namespace Ncv.Core.Tests;

/// <summary>M7: CFF(2013) 라운드트립 + FLOAT32/BINARY32 (DESIGN §2.5).</summary>
public class CffTests
{
    private static ComtradeRecord LoadCff(byte[] cff)
    {
        using var ms = new MemoryStream(cff);
        var result = CffReader.Read(ms);
        Assert.True(result.Success, result.ToString());
        return result.Value!;
    }

    private static void AssertFaultValues(ComtradeRecord rec, double tolVa, double tolIa)
    {
        Assert.Equal(3840, rec.SampleCount);
        Assert.Equal(1920, rec.Time.TriggerIndex);
        for (int n = 0; n < rec.SampleCount; n++)
        {
            double t = n / 1920.0;
            Assert.True(Math.Abs(rec.Analog[0][n] - GoldenSpecs.Va(t)) <= tolVa,
                $"VA 샘플 {n}: {rec.Analog[0][n]} vs {GoldenSpecs.Va(t)}");
            Assert.True(Math.Abs(rec.Analog[1][n] - GoldenSpecs.Ia(t)) <= tolIa,
                $"IA 샘플 {n}: {rec.Analog[1][n]} vs {GoldenSpecs.Ia(t)}");
            Assert.Equal(GoldenSpecs.Trip(t), rec.Digital[0][n]);
        }
    }

    [Fact]
    public void CffAsciiRoundtrip()
    {
        var spec = GoldenSpecs.FaultScenario("ASCII", revYear: 2013);
        var rec = LoadCff(ComtradeWriter.BuildCff(spec));

        Assert.Equal(2013, rec.Cfg.RevYear);
        AssertFaultValues(rec, GoldenSpecs.VaScale, GoldenSpecs.IaScale);
    }

    [Fact]
    public void CffBinaryRoundtrip_WithByteCount()
    {
        var spec = GoldenSpecs.FaultScenario("BINARY", revYear: 2013);
        var rec = LoadCff(ComtradeWriter.BuildCff(spec));

        AssertFaultValues(rec, GoldenSpecs.VaScale, GoldenSpecs.IaScale);
    }

    [Fact]
    public void CffFloat32Roundtrip()
    {
        var spec = GoldenSpecs.FaultScenario("FLOAT32", revYear: 2013);
        var rec = LoadCff(ComtradeWriter.BuildCff(spec));

        Assert.Equal(DataFileType.Float32, rec.Cfg.DataType);
        // float32 정밀도: 상대 오차 ~1e-7 × 값 크기 → 1e-3 여유
        AssertFaultValues(rec, 1e-3, 1e-3);
    }

    [Fact]
    public void CffBinary32Roundtrip()
    {
        var spec = GoldenSpecs.FaultScenario("BINARY32", revYear: 2013);
        var rec = LoadCff(ComtradeWriter.BuildCff(spec));

        Assert.Equal(DataFileType.Binary32, rec.Cfg.DataType);
        AssertFaultValues(rec, GoldenSpecs.VaScale, GoldenSpecs.IaScale);
    }

    [Fact]
    public void SectionHeaders_CaseInsensitive()
    {
        var spec = GoldenSpecs.FaultScenario("ASCII", revYear: 2013);
        var rec = LoadCff(ComtradeWriter.BuildCff(spec, lowercaseHeaders: true));

        Assert.Equal(3840, rec.SampleCount);
    }

    [Fact]
    public void MissingDatSection_FailsClearly()
    {
        var spec = GoldenSpecs.FaultScenario("ASCII", revYear: 2013);
        string cff = "--- file type: CFG ---\n" + ComtradeWriter.BuildCfgText(spec);

        using var ms = new MemoryStream(Encoding.ASCII.GetBytes(cff));
        var result = CffReader.Read(ms);

        Assert.False(result.Success);
        Assert.Contains("DAT", result.Error);
    }

    [Fact]
    public void MissingCfgSection_FailsClearly()
    {
        using var ms = new MemoryStream("--- file type: HDR ---\nhello\n"u8.ToArray());
        var result = CffReader.Read(ms);

        Assert.False(result.Success);
        Assert.Contains("CFG", result.Error);
    }

    [Fact]
    public void CfgSectionError_ReportsAbsoluteLineNumber()
    {
        var spec = GoldenSpecs.FaultScenario("ASCII", revYear: 2013);
        string cfgText = ComtradeWriter.BuildCfgText(spec).Replace("3,2A,1D", "3,9A,1D");
        string cff = "--- file type: CFG ---\n" + cfgText;

        using var ms = new MemoryStream(Encoding.ASCII.GetBytes(cff));
        var result = CffReader.Read(ms);

        Assert.False(result.Success);
        // CFG 섹션 헤더가 1행 → CFG 행2는 파일 기준 3행
        Assert.Equal(3, result.LineNumber);
        Assert.Contains("CFG 섹션", result.Error);
    }

    [Fact]
    public void ByteCountLargerThanFile_Fails()
    {
        var spec = GoldenSpecs.FaultScenario("BINARY", revYear: 2013);
        byte[] cff = ComtradeWriter.BuildCff(spec);
        byte[] truncated = cff[..(cff.Length - 100)];

        using var ms = new MemoryStream(truncated);
        var result = CffReader.Read(ms);

        Assert.False(result.Success);
        Assert.Contains("byte count", result.Error);
    }
}
