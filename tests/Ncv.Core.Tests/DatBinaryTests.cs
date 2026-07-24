using Ncv.Core.Model;
using Ncv.Core.Tests.Synthesis;
using Xunit;

namespace Ncv.Core.Tests;

/// <summary>DESIGN §7.3 디지털 패킹 벡터 + BINARY 라운드트립 + §7.5 에러 처리.</summary>
public class DatBinaryTests
{
    [Fact]
    public void DigitalPacking_D1AndD4_Word0x0009()
    {
        // §7.3: d1=1, d4=1 (나머지 0) → 패킹 워드 0x0009
        var spec = new SyntheticSpec
        {
            SampleRate = 1920,
            SampleCount = 1,
            DataType = "BINARY",
            Analogs = Array.Empty<SyntheticAnalog>(),
            Digitals = new[]
            {
                new SyntheticDigital { Id = "D1", Signal = _ => true },
                new SyntheticDigital { Id = "D2", Signal = _ => false },
                new SyntheticDigital { Id = "D3", Signal = _ => false },
                new SyntheticDigital { Id = "D4", Signal = _ => true },
            },
        };

        byte[] bytes = ComtradeWriter.BuildDatBinary(spec);

        // 레코드 = 8B 헤더 + 워드 1개
        Assert.Equal(10, bytes.Length);
        ushort word = (ushort)(bytes[8] | (bytes[9] << 8));
        Assert.Equal(0x0009, word);

        using var cfg = ComtradeWriter.ToStream(ComtradeWriter.BuildCfgText(spec));
        using var dat = new MemoryStream(bytes);
        var rec = ComtradeRecord.Load(cfg, dat).Value!;
        Assert.True(rec.Digital[0][0]);
        Assert.False(rec.Digital[1][0]);
        Assert.False(rec.Digital[2][0]);
        Assert.True(rec.Digital[3][0]);
    }

    [Fact]
    public void DigitalPacking_MultiWord_LsbFirstPerWord()
    {
        // 18채널 → 워드 2개. d17=워드2 bit0, d18=워드2 bit1
        var digitals = new SyntheticDigital[18];
        for (int i = 0; i < 18; i++)
        {
            bool on = i is 0 or 15 or 16; // d1, d16, d17
            digitals[i] = new SyntheticDigital { Id = $"D{i + 1}", Signal = _ => on };
        }

        var spec = new SyntheticSpec
        {
            SampleRate = 1920,
            SampleCount = 1,
            DataType = "BINARY",
            Analogs = Array.Empty<SyntheticAnalog>(),
            Digitals = digitals,
        };

        byte[] bytes = ComtradeWriter.BuildDatBinary(spec);
        Assert.Equal(12, bytes.Length);
        ushort w1 = (ushort)(bytes[8] | (bytes[9] << 8));
        ushort w2 = (ushort)(bytes[10] | (bytes[11] << 8));
        Assert.Equal(0x8001, w1); // d1(bit0) + d16(bit15)
        Assert.Equal(0x0001, w2); // d17(bit0)

        using var cfg = ComtradeWriter.ToStream(ComtradeWriter.BuildCfgText(spec));
        using var dat = new MemoryStream(bytes);
        var rec = ComtradeRecord.Load(cfg, dat).Value!;
        Assert.True(rec.Digital[0][0]);
        Assert.True(rec.Digital[15][0]);
        Assert.True(rec.Digital[16][0]);
        Assert.False(rec.Digital[1][0]);
        Assert.False(rec.Digital[17][0]);
    }

    [Fact]
    public void BinaryRoundtrip_MatchesFormula()
    {
        var spec = GoldenSpecs.FaultScenario("BINARY");
        using var cfg = ComtradeWriter.ToStream(ComtradeWriter.BuildCfgText(spec));
        using var dat = new MemoryStream(ComtradeWriter.BuildDatBinary(spec));
        var result = ComtradeRecord.Load(cfg, dat);

        Assert.True(result.Success, result.ToString());
        var rec = result.Value!;
        Assert.Equal(3840, rec.SampleCount);
        Assert.Equal(1920, rec.Time.TriggerIndex);

        for (int n = 0; n < rec.SampleCount; n++)
        {
            double t = n / 1920.0;
            Assert.True(Math.Abs(rec.Analog[0][n] - GoldenSpecs.Va(t)) <= GoldenSpecs.VaScale,
                $"VA 샘플 {n}: {rec.Analog[0][n]} vs {GoldenSpecs.Va(t)}");
            Assert.True(Math.Abs(rec.Analog[1][n] - GoldenSpecs.Ia(t)) <= GoldenSpecs.IaScale,
                $"IA 샘플 {n}: {rec.Analog[1][n]} vs {GoldenSpecs.Ia(t)}");
            Assert.Equal(GoldenSpecs.Trip(t), rec.Digital[0][n]);
        }
    }

    [Fact]
    public void BinaryRoundtrip_AsciiAndBinaryProduceSameValues()
    {
        var spec = GoldenSpecs.FaultScenario("BINARY");
        var asciiSpec = GoldenSpecs.FaultScenario("ASCII");

        using var cfgB = ComtradeWriter.ToStream(ComtradeWriter.BuildCfgText(spec));
        using var datB = new MemoryStream(ComtradeWriter.BuildDatBinary(spec));
        var binRec = ComtradeRecord.Load(cfgB, datB).Value!;

        using var cfgA = ComtradeWriter.ToStream(ComtradeWriter.BuildCfgText(asciiSpec));
        using var datA = ComtradeWriter.ToStream(ComtradeWriter.BuildDatAsciiText(asciiSpec));
        var ascRec = ComtradeRecord.Load(cfgA, datA).Value!;

        for (int n = 0; n < binRec.SampleCount; n += 7)
        {
            Assert.Equal(ascRec.Analog[0][n], binRec.Analog[0][n], 9);
            Assert.Equal(ascRec.Analog[1][n], binRec.Analog[1][n], 9);
        }
    }

    // ---- §7.5 에러 처리 ----

    [Fact]
    public void FileSizeNotMultipleOfRecord_FailsWithSizeMessage()
    {
        var spec = GoldenSpecs.FaultScenario("BINARY");
        byte[] bytes = ComtradeWriter.BuildDatBinary(spec);
        byte[] corrupted = bytes.Concat(new byte[] { 0x01, 0x02, 0x03 }).ToArray();

        using var cfg = ComtradeWriter.ToStream(ComtradeWriter.BuildCfgText(spec));
        using var dat = new MemoryStream(corrupted);
        var result = ComtradeRecord.Load(cfg, dat);

        Assert.False(result.Success);
        Assert.Contains("레코드", result.Error);
        Assert.Contains("배수", result.Error);
    }

    [Fact]
    public void BinaryCfgWithTextDat_FailsWithoutCrash()
    {
        // ft=BINARY인데 DAT가 ASCII 텍스트 (§7.5)
        var binSpec = GoldenSpecs.FaultScenario("BINARY");
        var asciiSpec = GoldenSpecs.FaultScenario("ASCII");

        using var cfg = ComtradeWriter.ToStream(ComtradeWriter.BuildCfgText(binSpec));
        using var dat = ComtradeWriter.ToStream(ComtradeWriter.BuildDatAsciiText(asciiSpec));
        var result = ComtradeRecord.Load(cfg, dat);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }
}
