using Ncv.Core.Model;
using Xunit;

namespace Ncv.Core.Tests;

/// <summary>
/// 실측 파일 회귀: tests/fixtures/real/에 CFG+DAT가 있으면
/// "파싱 성공 + 채널 수/샘플 수 일치"를 검증한다. 파일이 없으면 데이터 없음으로 통과(skip 상당).
/// </summary>
public class RealFixtureTests
{
    private static string FixturesDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "real"));

    public static TheoryData<string> CfgFiles()
    {
        var data = new TheoryData<string>();
        int count = 0;
        if (Directory.Exists(FixturesDir))
        {
            foreach (var cfg in Directory.EnumerateFiles(FixturesDir, "*.*")
                         .Where(f => Path.GetExtension(f).Equals(".cfg", StringComparison.OrdinalIgnoreCase)))
            {
                data.Add(Path.GetFileName(cfg));
                count++;
            }
        }

        // xunit Theory는 빈 데이터를 실패로 취급 — 파일이 없으면 sentinel로 skip 처리
        if (count == 0)
            data.Add("");
        return data;
    }

    [Theory]
    [MemberData(nameof(CfgFiles))]
    public void RealFile_ParsesWithConsistentCounts(string cfgFileName)
    {
        if (cfgFileName.Length == 0)
            return; // 실측 파일 미확보 — skip 상당

        string cfgPath = Path.Combine(FixturesDir, cfgFileName);
        string? datPath = Directory.EnumerateFiles(FixturesDir)
            .FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).Equals(
                    Path.GetFileNameWithoutExtension(cfgFileName), StringComparison.OrdinalIgnoreCase) &&
                Path.GetExtension(f).Equals(".dat", StringComparison.OrdinalIgnoreCase));

        Assert.False(datPath is null, $"{cfgFileName}에 대응하는 DAT 파일이 없습니다.");

        using var cfgStream = File.OpenRead(cfgPath);
        using var datStream = File.OpenRead(datPath!);
        var result = ComtradeRecord.Load(cfgStream, datStream);

        Assert.True(result.Success, result.ToString());
        var rec = result.Value!;
        Assert.Equal(rec.Cfg.AnalogCount, rec.Analog.Length);
        Assert.Equal(rec.Cfg.DigitalCount, rec.Digital.Length);
        if (rec.Cfg.DeclaredSampleCount >= 0)
            Assert.Equal(rec.Cfg.DeclaredSampleCount, rec.SampleCount);
    }
}
