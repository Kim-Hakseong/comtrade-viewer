using System.Buffers.Binary;

namespace Ncv.Core.Format;

/// <summary>
/// DAT BINARY 파서 (DESIGN §2.4, 2013 확장 §2.5). 전부 little-endian:
/// uint32 샘플번호 | uint32 타임스탬프 | 아날로그 ×A (int16/int32/float32) | uint16 × ceil(D/16).
/// 디지털 패킹은 워드당 16채널 LSB-first (d1 = bit0).
/// </summary>
public static class DatBinaryReader
{
    public static ParseResult<DatData> Read(Stream stream, CfgDocument cfg, IProgress<double>? progress = null)
    {
        int a = cfg.AnalogCount;
        int d = cfg.DigitalCount;
        int words = (d + 15) / 16;
        int analogWidth = cfg.DataType switch
        {
            DataFileType.Binary => 2,
            DataFileType.Binary32 or DataFileType.Float32 => 4,
            _ => 0,
        };
        if (analogWidth == 0)
            return ParseResult<DatData>.Fail(0, $"BINARY 리더가 처리할 수 없는 타입: {cfg.DataType}");

        int recordSize = 8 + analogWidth * a + 2 * words;

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            stream.CopyTo(ms);
            bytes = ms.ToArray();
        }

        if (bytes.Length == 0)
            return ParseResult<DatData>.Fail(0, "DAT가 비어 있습니다.");
        if (bytes.Length % recordSize != 0)
            return ParseResult<DatData>.Fail(0,
                $"파일 크기가 레코드 크기의 배수가 아닙니다: 레코드 {recordSize}B(8+{analogWidth}×{a}+2×{words}), " +
                $"파일 {bytes.Length}B (나머지 {bytes.Length % recordSize}B). BINARY 포맷이 아니거나 손상된 파일입니다.");

        int count = bytes.Length / recordSize;
        long declared = cfg.DeclaredSampleCount;
        if (declared >= 0 && count != declared)
            return ParseResult<DatData>.Fail(0,
                $"샘플 수 불일치: CFG 기대 {declared}, DAT 실제 {count} (레코드 {recordSize}B 기준)");

        var sampleNumbers = new long[count];
        var timestamps = new double[count];
        var analog = new double[a][];
        for (int c = 0; c < a; c++)
            analog[c] = new double[count];
        var digital = new bool[d][];
        for (int c = 0; c < d; c++)
            digital[c] = new bool[count];

        var span = bytes.AsSpan();
        for (int n = 0; n < count; n++)
        {
            int off = n * recordSize;
            sampleNumbers[n] = BinaryPrimitives.ReadUInt32LittleEndian(span[off..]);
            timestamps[n] = BinaryPrimitives.ReadUInt32LittleEndian(span[(off + 4)..]);

            int pos = off + 8;
            for (int c = 0; c < a; c++)
            {
                analog[c][n] = cfg.DataType switch
                {
                    DataFileType.Binary => BinaryPrimitives.ReadInt16LittleEndian(span[pos..]),
                    DataFileType.Binary32 => BinaryPrimitives.ReadInt32LittleEndian(span[pos..]),
                    _ => BinaryPrimitives.ReadSingleLittleEndian(span[pos..]),
                };
                pos += analogWidth;
            }

            for (int w = 0; w < words; w++)
            {
                ushort word = BinaryPrimitives.ReadUInt16LittleEndian(span[pos..]);
                pos += 2;
                int baseCh = w * 16;
                int limit = Math.Min(16, d - baseCh);
                for (int bit = 0; bit < limit; bit++)
                    digital[baseCh + bit][n] = (word & (1 << bit)) != 0;
            }

            if (progress is not null && n % 65536 == 0)
                progress.Report((double)n / count);
        }

        return ParseResult<DatData>.Ok(new DatData
        {
            SampleNumbers = sampleNumbers,
            Timestamps = timestamps,
            AnalogRaw = analog,
            Digital = digital,
        });
    }
}
