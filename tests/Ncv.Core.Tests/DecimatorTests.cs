using System.Diagnostics;
using Ncv.Core.Analysis;
using Xunit;
using Xunit.Abstractions;

namespace Ncv.Core.Tests;

/// <summary>DESIGN §7.3 데시메이션 골든 벡터 + M4 성능 DoD (100만 샘플 <100ms).</summary>
public class DecimatorTests
{
    private readonly ITestOutputHelper _output;

    public DecimatorTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void GoldenVector_TwoBuckets()
    {
        // §7.3: data=[0,10,-5,3, 7,2,8,1] buckets=2 → [(-5,10),(1,8)]
        double[] data = { 0, 10, -5, 3, 7, 2, 8, 1 };
        var result = Decimator.Decimate(data, 0, 8, 2);

        Assert.Equal(2, result.Length);
        Assert.Equal(new MinMax(-5, 10), result[0]);
        Assert.Equal(new MinMax(1, 8), result[1]);
    }

    [Fact]
    public void GoldenVector_TwoOrFewerSamplesPerBucket_PassesThroughOriginals()
    {
        // §7.3: 버킷당 2샘플 이하 → 원본 통과
        double[] data = { 0, 10, -5, 3, 7, 2, 8, 1 };
        var result = Decimator.Decimate(data, 0, 8, 4);

        Assert.Equal(8, result.Length);
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(data[i], result[i].Min);
            Assert.Equal(data[i], result[i].Max);
        }
    }

    [Fact]
    public void SubRange_UsesOnlyRequestedWindow()
    {
        double[] data = { 100, 0, 10, -5, 3, 7, 2, 8, 1, -100 };
        var result = Decimator.Decimate(data, 1, 9, 2);

        Assert.Equal(2, result.Length);
        Assert.Equal(new MinMax(-5, 10), result[0]);
        Assert.Equal(new MinMax(1, 8), result[1]);
    }

    [Fact]
    public void OutOfRangeIndices_AreClamped()
    {
        double[] data = { 1, 2, 3 };
        var result = Decimator.Decimate(data, -5, 100, 1);

        // 클램프 후 [0,3), 3샘플 > 2×1버킷 → 버킷 1개 (1,3)
        Assert.Single(result);
        Assert.Equal(new MinMax(1, 3), result[0]);
    }

    [Fact]
    public void EmptyOrInvalid_ReturnsEmpty()
    {
        double[] data = { 1, 2, 3 };
        Assert.Empty(Decimator.Decimate(data, 2, 2, 4));
        Assert.Empty(Decimator.Decimate(data, 3, 1, 4));
        Assert.Empty(Decimator.Decimate(data, 0, 3, 0));
    }

    [Fact]
    public void UnevenBucketSplit_CoversAllSamples()
    {
        // 10샘플 / 3버킷 → 3+3+4 근사 분할, 전 구간 커버
        double[] data = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        var result = Decimator.Decimate(data, 0, 10, 3);

        Assert.Equal(3, result.Length);
        Assert.Equal(0, result[0].Min);
        Assert.Equal(9, result[^1].Max);
        // 버킷 경계 인접성: 이전 max + 1 == 다음 min (단조 증가 데이터)
        Assert.Equal(result[0].Max + 1, result[1].Min);
        Assert.Equal(result[1].Max + 1, result[2].Min);
    }

    [Fact]
    public void Performance_OneMillionSamples_Under100Ms()
    {
        // M4 DoD: 100만 샘플 데시메이션 <100ms (Stopwatch 측정값 로그 기록)
        var data = new double[1_000_000];
        var rng = new Random(42);
        for (int i = 0; i < data.Length; i++)
            data[i] = Math.Sin(i * 0.001) * 100 + rng.NextDouble();

        // 워밍업
        Decimator.Decimate(data, 0, data.Length, 2000);

        var sw = Stopwatch.StartNew();
        var result = Decimator.Decimate(data, 0, data.Length, 2000);
        sw.Stop();

        _output.WriteLine($"100만 샘플 → 2000버킷 데시메이션: {sw.Elapsed.TotalMilliseconds:F3}ms");
        Assert.Equal(2000, result.Length);
        Assert.True(sw.Elapsed.TotalMilliseconds < 100,
            $"데시메이션이 100ms를 초과했습니다: {sw.Elapsed.TotalMilliseconds:F1}ms");
    }
}
