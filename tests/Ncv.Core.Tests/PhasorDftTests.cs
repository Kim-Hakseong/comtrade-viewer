using Ncv.Core.Analysis;
using Xunit;

namespace Ncv.Core.Tests;

/// <summary>DESIGN §7.4 페이저 DFT 골든 벡터 (N=32). 허용오차: 크기 1e-3, 각도 1e-3°.</summary>
public class PhasorDftTests
{
    private static double[] Cosine(double amplitude, double phaseDeg, int n)
    {
        var data = new double[n];
        double phase = phaseDeg * Math.PI / 180;
        for (int k = 0; k < n; k++)
            data[k] = amplitude * Math.Cos(2 * Math.PI * k / n + phase);
        return data;
    }

    [Fact]
    public void Golden_Amp100_Phase0()
    {
        var p = PhasorDft.Compute(Cosine(100, 0, 32), 0, 32)!.Value;

        Assert.Equal(70.7107, p.Magnitude, 3);
        Assert.Equal(0.0, p.AngleDegrees, 3);
    }

    [Fact]
    public void Golden_Amp100_Phase30()
    {
        var p = PhasorDft.Compute(Cosine(100, 30, 32), 0, 32)!.Value;

        Assert.Equal(70.7107, p.Magnitude, 3);
        Assert.Equal(30.0, p.AngleDegrees, 3);
    }

    [Fact]
    public void Golden_Amp70_7_PhaseMinus45()
    {
        var p = PhasorDft.Compute(Cosine(70.7, -45, 32), 0, 32)!.Value;

        Assert.Equal(49.9924, p.Magnitude, 3);
        Assert.Equal(-45.0, p.AngleDegrees, 3);
    }

    [Fact]
    public void WindowOutOfRange_ReturnsNull()
    {
        var data = new double[40];
        Assert.Null(PhasorDft.Compute(data, 20, 32));
        Assert.Null(PhasorDft.Compute(data, -1, 32));
        Assert.Null(PhasorDft.Compute(data, 0, 0));
    }

    [Fact]
    public void OffsetWindow_SameMagnitude()
    {
        // 1920Hz/60Hz → N=32, 커서 위치가 달라도 정현파 크기는 동일, 각도는 창 시작 위상만큼 이동
        int n = 32;
        var data = new double[96];
        for (int k = 0; k < data.Length; k++)
            data[k] = 100 * Math.Cos(2 * Math.PI * k / n);

        var p0 = PhasorDft.Compute(data, 0, n)!.Value;
        var p8 = PhasorDft.Compute(data, 8, n)!.Value; // 1/4주기 → +90°

        Assert.Equal(p0.Magnitude, p8.Magnitude, 6);
        Assert.Equal(90.0, PhasorDft.NormalizeAngle(p8.AngleDegrees - p0.AngleDegrees), 6);
    }

    [Theory]
    [InlineData(190, -170)]
    [InlineData(-190, 170)]
    [InlineData(180, 180)]
    [InlineData(-180, 180)]
    [InlineData(540, 180)]
    public void NormalizeAngle_Range(double input, double expected)
    {
        Assert.Equal(expected, PhasorDft.NormalizeAngle(input), 9);
    }
}
