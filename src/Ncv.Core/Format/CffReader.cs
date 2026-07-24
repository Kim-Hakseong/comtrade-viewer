using System.Text;
using System.Text.RegularExpressions;
using Ncv.Core.Model;

namespace Ncv.Core.Format;

/// <summary>
/// COMTRADE 2013 CFF 단일파일 리더 (DESIGN §2.5).
/// 섹션 헤더(`--- file type: XXX ---`)는 대소문자 무관. HDR/INF는 파싱만 하고 무시.
/// DAT BINARY 섹션은 헤더의 byte count만큼 바이너리로 취급한다.
/// </summary>
public static partial class CffReader
{
    [GeneratedRegex(
        @"^-{2,}\s*file\s+type\s*:\s*(?<type>CFG|HDR|INF|DAT)(?:\s+(?<sub>ASCII|BINARY32|BINARY|FLOAT32))?\s*(?::\s*(?<count>\d+))?\s*-{2,}\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SectionHeaderRegex();

    public static ParseResult<ComtradeRecord> Read(Stream stream, IProgress<double>? progress = null)
    {
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            stream.CopyTo(ms);
            bytes = ms.ToArray();
        }

        List<string>? cfgLines = null;
        int cfgHeaderLine = 0;
        var datAscii = new StringBuilder();
        bool datAsciiSeen = false;
        byte[]? datBinary = null;
        string? datSubType = null;

        string? currentSection = null;
        int pos = 0;
        int lineNo = 0;

        while (pos < bytes.Length)
        {
            int lineStart = pos;
            int nl = Array.IndexOf(bytes, (byte)'\n', pos);
            int lineEnd = nl >= 0 ? nl : bytes.Length;
            pos = nl >= 0 ? nl + 1 : bytes.Length;
            lineNo++;

            int len = lineEnd - lineStart;
            if (len > 0 && bytes[lineStart + len - 1] == (byte)'\r')
                len--;
            string line = Encoding.ASCII.GetString(bytes, lineStart, len);

            var m = SectionHeaderRegex().Match(line);
            if (m.Success)
            {
                string type = m.Groups["type"].Value.ToUpperInvariant();
                currentSection = type;

                if (type == "CFG")
                {
                    if (cfgLines is not null)
                        return Fail(lineNo, "CFG 섹션이 중복되었습니다.");
                    cfgLines = new List<string>();
                    cfgHeaderLine = lineNo;
                }
                else if (type == "DAT")
                {
                    datSubType = m.Groups["sub"].Success ? m.Groups["sub"].Value.ToUpperInvariant() : null;
                    if (datSubType is "BINARY" or "BINARY32" or "FLOAT32")
                    {
                        if (!m.Groups["count"].Success ||
                            !long.TryParse(m.Groups["count"].Value, out long count) || count < 0)
                            return Fail(lineNo, "DAT BINARY 섹션 헤더에 byte count가 없습니다.");
                        if (pos + count > bytes.Length)
                            return Fail(lineNo,
                                $"DAT BINARY byte count({count})가 남은 파일 크기({bytes.Length - pos})보다 큽니다.");
                        datBinary = bytes.AsSpan(pos, (int)count).ToArray();
                        pos += (int)count;
                        currentSection = null; // 바이너리 블록 종료 후 다음 섹션 스캔 계속
                    }
                    else
                    {
                        datAsciiSeen = true;
                    }
                }

                continue;
            }

            switch (currentSection)
            {
                case "CFG":
                    cfgLines!.Add(line);
                    break;
                case "DAT":
                    datAscii.Append(line).Append('\n');
                    break;
                // HDR/INF/헤더 이전 내용: 무시
            }
        }

        if (cfgLines is null)
            return Fail(0, "CFF에 CFG 섹션이 없습니다.");

        var cfgResult = CfgParser.ParseLines(cfgLines);
        if (!cfgResult.Success)
            return ParseResult<ComtradeRecord>.Fail(
                cfgResult.LineNumber + cfgHeaderLine, $"[CFG 섹션] {cfgResult.Error}");
        var cfg = cfgResult.Value!;

        // 섹션 타입과 CFG ft 교차 검증
        if (cfg.DataType == DataFileType.Ascii)
        {
            if (!datAsciiSeen)
                return Fail(0, "CFG ft=ASCII인데 DAT ASCII 섹션이 없습니다.");
            using var datStream = new MemoryStream(Encoding.ASCII.GetBytes(datAscii.ToString()));
            return ComtradeRecord.LoadDat(cfg, datStream, progress);
        }

        if (datBinary is null)
            return Fail(0, $"CFG ft={cfg.DataType}인데 DAT BINARY 섹션이 없습니다.");
        using var binStream = new MemoryStream(datBinary);
        return ComtradeRecord.LoadDat(cfg, binStream, progress);
    }

    private static ParseResult<ComtradeRecord> Fail(int line, string msg) =>
        ParseResult<ComtradeRecord>.Fail(line, msg);
}
