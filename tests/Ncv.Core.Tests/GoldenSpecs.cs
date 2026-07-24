using Ncv.Core.Tests.Synthesis;

namespace Ncv.Core.Tests;

/// <summary>
/// DESIGN §7.2 합성 시나리오: VA=100·sin(2π60t), IA=50·sin(2π60t−120°),
/// 1920Hz × 2초(3840샘플), 트리거 t=1.0s에서 IA 진폭 50→500 스텝. TRIP은 t≥1.0에서 1.
/// </summary>
public static class GoldenSpecs
{
    public const double VaScale = 100.0 / 32767;
    public const double IaScale = 500.0 / 32767;

    public static double Va(double t) => 100 * Math.Sin(2 * Math.PI * 60 * t);

    public static double Ia(double t) =>
        (t < 1.0 ? 50 : 500) * Math.Sin(2 * Math.PI * 60 * t - 2 * Math.PI / 3);

    public static bool Trip(double t) => t >= 1.0;

    public static SyntheticSpec FaultScenario(string dataType = "ASCII", int revYear = 1999) => new()
    {
        Station = "NEXYS_SYN",
        DevId = "SYN01",
        RevYear = revYear,
        SampleRate = 1920,
        SampleCount = 3840,
        LineFrequency = 60,
        TriggerOffsetSeconds = 1.0,
        DataType = dataType,
        Analogs = new[]
        {
            new SyntheticAnalog { Id = "VA", Phase = "A", Unit = "V", Signal = Va, A = VaScale },
            new SyntheticAnalog { Id = "IA", Phase = "A", Unit = "A", Signal = Ia, A = IaScale },
        },
        Digitals = new[]
        {
            new SyntheticDigital { Id = "TRIP52", Signal = Trip },
        },
    };
}
