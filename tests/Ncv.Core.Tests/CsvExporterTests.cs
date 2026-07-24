using System.Globalization;
using Ncv.Core.Export;
using Ncv.Core.Model;
using Ncv.Core.Tests.Synthesis;
using Xunit;

namespace Ncv.Core.Tests;

/// <summary>M9: CSV 내보내기 라운드트립 — 내보낸 CSV를 되읽어 원본 배열과 일치 확인.</summary>
public class CsvExporterTests
{
    private static ComtradeRecord LoadFault()
    {
        var spec = GoldenSpecs.FaultScenario("ASCII");
        using var cfg = ComtradeWriter.ToStream(ComtradeWriter.BuildCfgText(spec));
        using var dat = ComtradeWriter.ToStream(ComtradeWriter.BuildDatAsciiText(spec));
        return ComtradeRecord.Load(cfg, dat).Value!;
    }

    [Fact]
    public void ExportRoundtrip_ValuesMatchRecord()
    {
        var rec = LoadFault();
        using var sw = new StringWriter();
        CsvExporter.Write(sw, rec, 1900, 1950, new[] { 0, 1 }, new[] { 0 });

        string[] lines = sw.ToString().TrimEnd('\n').Split('\n');
        Assert.Equal("time_s,VA [V],IA [A],TRIP52", lines[0]);
        Assert.Equal(51, lines.Length); // 헤더 + 50행

        for (int i = 0; i < 50; i++)
        {
            int n = 1900 + i;
            string[] f = lines[1 + i].Split(',');
            Assert.Equal(4, f.Length);
            Assert.Equal(rec.Time.TimeAt(n), double.Parse(f[0], CultureInfo.InvariantCulture), 12);
            Assert.Equal(rec.Analog[0][n], double.Parse(f[1], CultureInfo.InvariantCulture), 12);
            Assert.Equal(rec.Analog[1][n], double.Parse(f[2], CultureInfo.InvariantCulture), 12);
            Assert.Equal(rec.Digital[0][n] ? "1" : "0", f[3]);
        }
    }

    [Fact]
    public void ExportRelativeToTrigger_TimeShifted()
    {
        var rec = LoadFault();
        using var sw = new StringWriter();
        CsvExporter.Write(sw, rec, 1920, 1922, new[] { 0 }, Array.Empty<int>(), relativeToTrigger: true);

        string[] lines = sw.ToString().TrimEnd('\n').Split('\n');
        Assert.StartsWith("time_from_trigger_s", lines[0]);
        Assert.Equal(0, double.Parse(lines[1].Split(',')[0], CultureInfo.InvariantCulture), 12);
    }

    [Fact]
    public void ExportSubsetChannels_OnlySelectedColumns()
    {
        var rec = LoadFault();
        using var sw = new StringWriter();
        CsvExporter.Write(sw, rec, 0, 3, new[] { 1 }, Array.Empty<int>());

        string[] lines = sw.ToString().TrimEnd('\n').Split('\n');
        Assert.Equal("time_s,IA [A]", lines[0]);
        Assert.Equal(2, lines[1].Split(',').Length);
    }

    [Fact]
    public void RangeClamped_ToRecordBounds()
    {
        var rec = LoadFault();
        using var sw = new StringWriter();
        CsvExporter.Write(sw, rec, -10, int.MaxValue, new[] { 0 }, Array.Empty<int>());

        string[] lines = sw.ToString().TrimEnd('\n').Split('\n');
        Assert.Equal(1 + 3840, lines.Length);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    public void Escape_Rfc4180(string input, string expected)
    {
        Assert.Equal(expected, CsvExporter.Escape(input));
    }
}
