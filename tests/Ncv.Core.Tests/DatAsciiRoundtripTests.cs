using Ncv.Core.Model;
using Ncv.Core.Tests.Synthesis;
using Xunit;

namespace Ncv.Core.Tests;

/// <summary>DESIGN §7.2 ASCII 라운드트립: Writer 생성 → 파서 읽기 → 수식값 일치.</summary>
public class DatAsciiRoundtripTests
{
    private static ComtradeRecord LoadFault()
    {
        var spec = GoldenSpecs.FaultScenario("ASCII");
        using var cfg = ComtradeWriter.ToStream(ComtradeWriter.BuildCfgText(spec));
        using var dat = ComtradeWriter.ToStream(ComtradeWriter.BuildDatAsciiText(spec));
        var result = ComtradeRecord.Load(cfg, dat);
        Assert.True(result.Success, result.ToString());
        return result.Value!;
    }

    [Fact]
    public void Roundtrip_SampleCountAndTrigger()
    {
        var rec = LoadFault();

        Assert.Equal(3840, rec.SampleCount);
        Assert.Equal(1920, rec.Time.TriggerIndex);
    }

    [Fact]
    public void Roundtrip_AnalogValuesMatchFormulaWithinQuantization()
    {
        var rec = LoadFault();

        for (int n = 0; n < rec.SampleCount; n++)
        {
            double t = n / 1920.0;
            Assert.True(Math.Abs(rec.Analog[0][n] - GoldenSpecs.Va(t)) <= GoldenSpecs.VaScale,
                $"VA 샘플 {n}: {rec.Analog[0][n]} vs {GoldenSpecs.Va(t)}");
            Assert.True(Math.Abs(rec.Analog[1][n] - GoldenSpecs.Ia(t)) <= GoldenSpecs.IaScale,
                $"IA 샘플 {n}: {rec.Analog[1][n]} vs {GoldenSpecs.Ia(t)}");
        }
    }

    [Fact]
    public void Roundtrip_FaultStepVisibleAtTrigger()
    {
        var rec = LoadFault();

        // 트리거 이전 1주기 최대 진폭 ≈ 50, 이후 ≈ 500
        double maxBefore = 0, maxAfter = 0;
        for (int n = 1888; n < 1920; n++)
            maxBefore = Math.Max(maxBefore, Math.Abs(rec.Analog[1][n]));
        for (int n = 1920; n < 1952; n++)
            maxAfter = Math.Max(maxAfter, Math.Abs(rec.Analog[1][n]));

        Assert.InRange(maxBefore, 40, 55);
        Assert.InRange(maxAfter, 400, 505);
    }

    [Fact]
    public void Roundtrip_DigitalTripFollowsTime()
    {
        var rec = LoadFault();

        Assert.False(rec.Digital[0][1919]);
        Assert.True(rec.Digital[0][1920]);
        Assert.True(rec.Digital[0][3839]);
    }

    [Fact]
    public void Roundtrip_TimelineFollowsSampleRate()
    {
        var rec = LoadFault();

        Assert.Equal(0, rec.Time.TimeAt(0), 12);
        Assert.Equal(1.0, rec.Time.TimeAt(1920), 12);
        Assert.Equal(3839 / 1920.0, rec.Time.TimeAt(3839), 12);
        Assert.Equal(0, rec.Time.TimeFromTrigger(1920), 12);
    }

    // ---- 에러 처리 ----

    [Fact]
    public void FieldCountMismatch_FailsWithLineNumber()
    {
        var spec = GoldenSpecs.FaultScenario("ASCII");
        string datText = ComtradeWriter.BuildDatAsciiText(spec);
        string[] lines = datText.TrimEnd('\n').Split('\n');
        lines[9] = "10,4687"; // 10번째 행의 채널 필드 제거
        string broken = string.Join('\n', lines) + "\n";

        using var cfg = ComtradeWriter.ToStream(ComtradeWriter.BuildCfgText(spec));
        using var dat = ComtradeWriter.ToStream(broken);
        var result = ComtradeRecord.Load(cfg, dat);

        Assert.False(result.Success);
        Assert.Equal(10, result.LineNumber);
        Assert.Contains("필드 수", result.Error);
    }

    [Fact]
    public void SampleCountMismatch_Fails()
    {
        var spec = GoldenSpecs.FaultScenario("ASCII");
        string datText = ComtradeWriter.BuildDatAsciiText(spec);
        string truncated = string.Join('\n', datText.TrimEnd('\n').Split('\n')[..1000]) + "\n";

        using var cfg = ComtradeWriter.ToStream(ComtradeWriter.BuildCfgText(spec));
        using var dat = ComtradeWriter.ToStream(truncated);
        var result = ComtradeRecord.Load(cfg, dat);

        Assert.False(result.Success);
        Assert.Contains("샘플 수 불일치", result.Error);
    }
}
