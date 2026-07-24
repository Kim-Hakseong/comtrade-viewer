using Ncv.Core.Format;

namespace Ncv.Core.Model;

/// <summary>
/// 로드 완료된 COMTRADE 레코드 (DESIGN §3). Analog는 스케일(a·raw+b) 적용 실값.
/// </summary>
public sealed class ComtradeRecord
{
    public required CfgDocument Cfg { get; init; }

    /// <summary>[채널][샘플] 실값 (a × raw + b).</summary>
    public required double[][] Analog { get; init; }

    /// <summary>[채널][샘플].</summary>
    public required bool[][] Digital { get; init; }

    public required Timeline Time { get; init; }

    public int SampleCount => Time.SampleCount;

    /// <summary>CFG + DAT 스트림에서 레코드를 조립한다. DataType에 따라 리더 선택.</summary>
    public static ParseResult<ComtradeRecord> Load(Stream cfgStream, Stream datStream,
        IProgress<double>? progress = null)
    {
        var cfgResult = CfgParser.Parse(cfgStream);
        if (!cfgResult.Success)
            return cfgResult.As<ComtradeRecord>();

        return LoadDat(cfgResult.Value!, datStream, progress);
    }

    /// <summary>이미 파싱된 CFG로 DAT만 읽어 조립한다 (CFF 경로 재사용).</summary>
    public static ParseResult<ComtradeRecord> LoadDat(CfgDocument cfg, Stream datStream,
        IProgress<double>? progress = null)
    {
        ParseResult<DatData> datResult = cfg.DataType switch
        {
            DataFileType.Ascii => DatAsciiReader.Read(datStream, cfg, progress),
            DataFileType.Binary or DataFileType.Binary32 or DataFileType.Float32 =>
                DatBinaryReader.Read(datStream, cfg, progress),
            _ => ParseResult<DatData>.Fail(0, $"지원하지 않는 데이터 타입: {cfg.DataType}"),
        };
        if (!datResult.Success)
            return datResult.As<ComtradeRecord>();

        return Assemble(cfg, datResult.Value!);
    }

    public static ParseResult<ComtradeRecord> Assemble(CfgDocument cfg, DatData dat)
    {
        int count = dat.SampleCount;
        var analog = new double[cfg.AnalogCount][];
        for (int c = 0; c < cfg.AnalogCount; c++)
        {
            var ch = cfg.AnalogChannels[c];
            var raw = dat.AnalogRaw[c];
            var scaled = new double[count];
            for (int n = 0; n < count; n++)
                scaled[n] = ch.A * raw[n] + ch.B;
            analog[c] = scaled;
        }

        var timeline = Timeline.Build(cfg, count, dat.Timestamps);

        return ParseResult<ComtradeRecord>.Ok(new ComtradeRecord
        {
            Cfg = cfg,
            Analog = analog,
            Digital = dat.Digital,
            Time = timeline,
        });
    }
}
