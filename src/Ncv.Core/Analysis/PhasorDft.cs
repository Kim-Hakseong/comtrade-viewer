namespace Ncv.Core.Analysis;

/// <summary>RMS 페이저: 크기(RMS)와 위상각(도).</summary>
public readonly record struct Phasor(double Magnitude, double AngleDegrees);

/// <summary>
/// 풀사이클 DFT 페이저 (DESIGN §6, cosine 기준):
/// X = (√2/N) × Σ_{n=0}^{N-1} x[n]·e^(−j2πn/N), N = 1주기 샘플 수 = round(samp/lf).
/// </summary>
public static class PhasorDft
{
    /// <summary>data[startIdx..startIdx+n)에 대한 1주기 DFT. 범위 밖이면 null.</summary>
    public static Phasor? Compute(double[] data, int startIdx, int n)
    {
        if (n <= 0 || startIdx < 0 || startIdx + n > data.Length)
            return null;

        double re = 0, im = 0;
        for (int k = 0; k < n; k++)
        {
            double ang = 2 * Math.PI * k / n;
            double v = data[startIdx + k];
            re += v * Math.Cos(ang);
            im -= v * Math.Sin(ang);
        }

        double scale = Math.Sqrt(2) / n;
        re *= scale;
        im *= scale;
        return new Phasor(Math.Sqrt(re * re + im * im), Math.Atan2(im, re) * 180 / Math.PI);
    }

    /// <summary>각도를 (−180°, 180°] 범위로 정규화.</summary>
    public static double NormalizeAngle(double degrees)
    {
        double a = degrees % 360;
        if (a > 180)
            a -= 360;
        else if (a <= -180)
            a += 360;
        return a;
    }
}
