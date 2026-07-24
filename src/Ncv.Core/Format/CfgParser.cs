using System.Globalization;
using Ncv.Core.Model;

namespace Ncv.Core.Format;

/// <summary>
/// COMTRADE CFG 파서 (1999 리비전, DESIGN §2.1). Stream 기반, 실패는 ParseResult로 반환.
/// </summary>
public static class CfgParser
{
    public static ParseResult<CfgDocument> Parse(Stream stream)
    {
        var lines = new List<string>();
        using (var reader = new StreamReader(stream, leaveOpen: true))
        {
            string? line;
            while ((line = reader.ReadLine()) is not null)
                lines.Add(line);
        }

        return ParseLines(lines);
    }

    /// <summary>CFF에서 섹션 분리된 CFG 행 목록을 그대로 파싱할 때도 사용한다 (M7).</summary>
    public static ParseResult<CfgDocument> ParseLines(IReadOnlyList<string> lines)
    {
        // 후행 공백 행 제거 (파일 끝 개행 허용)
        int count = lines.Count;
        while (count > 0 && string.IsNullOrWhiteSpace(lines[count - 1]))
            count--;

        int lineNo = 0; // 현재 처리 중인 행 (1-base)

        string? Next()
        {
            if (lineNo >= count)
                return null;
            return lines[lineNo++];
        }

        // 행1: station_name,rec_dev_id,rev_year
        string? l1 = Next();
        if (l1 is null)
            return Fail(1, "CFG가 비어 있습니다.");
        string[] f1 = l1.Split(',');
        if (f1.Length < 2)
            return Fail(1, "행1은 station_name,rec_dev_id[,rev_year] 형식이어야 합니다.");
        string station = f1[0].Trim();
        string devId = f1[1].Trim();
        int revYear;
        if (f1.Length < 3 || string.IsNullOrWhiteSpace(f1[2]))
        {
            // rev_year 없으면 1991로 간주 — P0/P1 미지원
            return Fail(1, "rev_year가 없습니다 (1991 구리비전으로 간주). 1999/2013 리비전만 지원합니다.");
        }

        if (!int.TryParse(f1[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out revYear))
            return Fail(1, $"rev_year를 해석할 수 없습니다: '{f1[2].Trim()}'");
        if (revYear is not (1999 or 2013))
            return Fail(1, $"지원하지 않는 rev_year: {revYear} (1999/2013만 지원)");

        // 행2: TT,##A,##D
        string? l2 = Next();
        if (l2 is null)
            return Fail(2, "행2(채널 수)가 없습니다.");
        string[] f2 = l2.Split(',');
        if (f2.Length != 3)
            return Fail(2, "행2는 TT,##A,##D 형식이어야 합니다.");
        if (!int.TryParse(f2[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int totalCh))
            return Fail(2, $"총 채널 수를 해석할 수 없습니다: '{f2[0].Trim()}'");
        var (analogCount, aErr) = ParseChannelCount(f2[1], 'A');
        if (aErr is not null)
            return Fail(2, aErr);
        var (digitalCount, dErr) = ParseChannelCount(f2[2], 'D');
        if (dErr is not null)
            return Fail(2, dErr);
        if (analogCount + digitalCount != totalCh)
            return Fail(2, $"채널 수 불일치: TT={totalCh}, A+D={analogCount + digitalCount}");

        // 아날로그 채널 행
        var analogs = new List<AnalogChannel>(analogCount);
        for (int i = 0; i < analogCount; i++)
        {
            int thisLine = lineNo + 1;
            string? row = Next();
            if (row is null)
                return Fail(thisLine, $"아날로그 채널 행이 부족합니다 ({i + 1}/{analogCount}번째 누락).");
            var r = ParseAnalogRow(row, thisLine);
            if (!r.Success)
                return r.As<CfgDocument>();
            analogs.Add(r.Value!);
        }

        // 디지털 채널 행
        var digitals = new List<DigitalChannel>(digitalCount);
        for (int i = 0; i < digitalCount; i++)
        {
            int thisLine = lineNo + 1;
            string? row = Next();
            if (row is null)
                return Fail(thisLine, $"디지털 채널 행이 부족합니다 ({i + 1}/{digitalCount}번째 누락).");
            var r = ParseDigitalRow(row, thisLine);
            if (!r.Success)
                return r.As<CfgDocument>();
            digitals.Add(r.Value!);
        }

        // lf
        int lfLine = lineNo + 1;
        string? lfRow = Next();
        if (lfRow is null)
            return Fail(lfLine, "계통 주파수(lf) 행이 없습니다.");
        if (!TryParseDouble(lfRow, out double lf))
            return Fail(lfLine, $"계통 주파수를 해석할 수 없습니다: '{lfRow.Trim()}'");

        // nrates
        int nratesLine = lineNo + 1;
        string? nratesRow = Next();
        if (nratesRow is null)
            return Fail(nratesLine, "nrates 행이 없습니다.");
        if (!int.TryParse(nratesRow.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int nrates) || nrates < 0)
            return Fail(nratesLine, $"nrates를 해석할 수 없습니다: '{nratesRow.Trim()}'");

        // samp,endsamp × nrates.
        // nrates=0: 표준(C37.111)은 무의미한 samp,endsamp 행 1개를 두지만 DESIGN 문면은 ×nrates.
        // 두 형태 모두 수용 — 다음 행이 samp,endsamp 꼴이면 소비하고 값은 무시한다.
        var rates = new List<SampleRateSegment>(nrates);
        for (int i = 0; i < nrates; i++)
        {
            int thisLine = lineNo + 1;
            string? row = Next();
            if (row is null)
                return Fail(thisLine, $"samp,endsamp 행이 부족합니다 ({i + 1}/{nrates}번째 누락).");
            string[] rf = row.Split(',');
            if (rf.Length != 2)
                return Fail(thisLine, "samp,endsamp 형식이어야 합니다.");
            if (!TryParseDouble(rf[0], out double samp))
                return Fail(thisLine, $"samp를 해석할 수 없습니다: '{rf[0].Trim()}'");
            if (!long.TryParse(rf[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long endSamp))
                return Fail(thisLine, $"endsamp를 해석할 수 없습니다: '{rf[1].Trim()}'");
            if (samp <= 0)
                return Fail(thisLine, $"samp는 양수여야 합니다: {samp}");
            if (endSamp <= 0)
                return Fail(thisLine, $"endsamp는 양수여야 합니다: {endSamp}");
            if (rates.Count > 0 && endSamp <= rates[^1].EndSample)
                return Fail(thisLine, $"endsamp는 증가해야 합니다: {rates[^1].EndSample} → {endSamp}");
            rates.Add(new SampleRateSegment(samp, endSamp));
        }

        if (nrates == 0 && lineNo < count && IsRateLikeRow(lines[lineNo]))
            lineNo++; // 표준형 파일의 무의미한 samp,endsamp 행 소비

        // 첫 샘플 시각 / 트리거 시각
        int startLine = lineNo + 1;
        string? startRow = Next();
        if (startRow is null)
            return Fail(startLine, "첫 샘플 시각 행이 없습니다.");
        if (!TryParseTimestamp(startRow, out DateTime startTime))
            return Fail(startLine, $"첫 샘플 시각을 해석할 수 없습니다: '{startRow.Trim()}' (dd/mm/yyyy,hh:mm:ss.ssssss)");

        int trigLine = lineNo + 1;
        string? trigRow = Next();
        if (trigRow is null)
            return Fail(trigLine, "트리거 시각 행이 없습니다.");
        if (!TryParseTimestamp(trigRow, out DateTime trigTime))
            return Fail(trigLine, $"트리거 시각을 해석할 수 없습니다: '{trigRow.Trim()}' (dd/mm/yyyy,hh:mm:ss.ssssss)");

        // ft
        int ftLine = lineNo + 1;
        string? ftRow = Next();
        if (ftRow is null)
            return Fail(ftLine, "파일 타입(ft) 행이 없습니다.");
        DataFileType dataType;
        switch (ftRow.Trim().ToUpperInvariant())
        {
            case "ASCII":
                dataType = DataFileType.Ascii;
                break;
            case "BINARY":
                dataType = DataFileType.Binary;
                break;
            case "BINARY32" when revYear >= 2013:
                dataType = DataFileType.Binary32;
                break;
            case "FLOAT32" when revYear >= 2013:
                dataType = DataFileType.Float32;
                break;
            default:
                return Fail(ftLine, $"지원하지 않는 파일 타입: '{ftRow.Trim()}' (rev {revYear})");
        }

        // timemult
        int tmLine = lineNo + 1;
        string? tmRow = Next();
        if (tmRow is null)
            return Fail(tmLine, "timemult 행이 없습니다.");
        if (!TryParseDouble(tmRow, out double timeMult))
            return Fail(tmLine, $"timemult를 해석할 수 없습니다: '{tmRow.Trim()}'");

        var doc = new CfgDocument
        {
            StationName = station,
            RecDevId = devId,
            RevYear = revYear,
            TotalChannelCount = totalCh,
            AnalogChannels = analogs,
            DigitalChannels = digitals,
            LineFrequency = lf,
            NRates = nrates,
            SampleRates = rates,
            StartTime = startTime,
            TriggerTime = trigTime,
            DataType = dataType,
            TimeMult = timeMult,
        };
        return ParseResult<CfgDocument>.Ok(doc);
    }

    private static ParseResult<CfgDocument> Fail(int line, string msg) => ParseResult<CfgDocument>.Fail(line, msg);

    /// <summary>samp,endsamp 꼴(숫자 2필드)인지 — 타임스탬프 행(dd/mm/yyyy,…)과 구분.</summary>
    private static bool IsRateLikeRow(string row)
    {
        string[] f = row.Split(',');
        return f.Length == 2 && TryParseDouble(f[0], out _) && TryParseDouble(f[1], out _);
    }

    private static (int Count, string? Error) ParseChannelCount(string field, char suffix)
    {
        string s = field.Trim();
        if (s.Length == 0)
            return (0, $"채널 수 필드가 비어 있습니다 (##{suffix}).");
        if (char.ToUpperInvariant(s[^1]) == char.ToUpperInvariant(suffix))
            s = s[..^1];
        if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 0)
            return (0, $"채널 수를 해석할 수 없습니다: '{field.Trim()}'");
        return (n, null);
    }

    private static ParseResult<AnalogChannel> ParseAnalogRow(string row, int lineNo)
    {
        string[] f = row.Split(',');
        if (f.Length != 13)
            return ParseResult<AnalogChannel>.Fail(lineNo,
                $"아날로그 채널 행은 13개 필드여야 합니다 (현재 {f.Length}개): An,ch_id,ph,ccbm,uu,a,b,skew,min,max,primary,secondary,PS");
        if (!int.TryParse(f[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx))
            return ParseResult<AnalogChannel>.Fail(lineNo, $"채널 인덱스(An)를 해석할 수 없습니다: '{f[0].Trim()}'");
        if (!TryParseDouble(f[5], out double a))
            return ParseResult<AnalogChannel>.Fail(lineNo, $"스케일 a를 해석할 수 없습니다: '{f[5].Trim()}'");
        if (a == 0)
            return ParseResult<AnalogChannel>.Fail(lineNo, "스케일 a는 0일 수 없습니다.");
        if (!TryParseDouble(f[6], out double b))
            return ParseResult<AnalogChannel>.Fail(lineNo, $"오프셋 b를 해석할 수 없습니다: '{f[6].Trim()}'");
        if (!TryParseDoubleOrDefault(f[7], 0, out double skew))
            return ParseResult<AnalogChannel>.Fail(lineNo, $"skew를 해석할 수 없습니다: '{f[7].Trim()}'");
        if (!TryParseDoubleOrDefault(f[8], 0, out double min))
            return ParseResult<AnalogChannel>.Fail(lineNo, $"min을 해석할 수 없습니다: '{f[8].Trim()}'");
        if (!TryParseDoubleOrDefault(f[9], 0, out double max))
            return ParseResult<AnalogChannel>.Fail(lineNo, $"max를 해석할 수 없습니다: '{f[9].Trim()}'");
        if (!TryParseDoubleOrDefault(f[10], 0, out double primary))
            return ParseResult<AnalogChannel>.Fail(lineNo, $"primary를 해석할 수 없습니다: '{f[10].Trim()}'");
        if (!TryParseDoubleOrDefault(f[11], 0, out double secondary))
            return ParseResult<AnalogChannel>.Fail(lineNo, $"secondary를 해석할 수 없습니다: '{f[11].Trim()}'");

        return ParseResult<AnalogChannel>.Ok(new AnalogChannel
        {
            Index = idx,
            Id = f[1].Trim(),
            Phase = f[2].Trim(),
            Ccbm = f[3].Trim(),
            Unit = f[4].Trim(),
            A = a,
            B = b,
            Skew = skew,
            Min = min,
            Max = max,
            Primary = primary,
            Secondary = secondary,
            Ps = f[12].Trim(),
        });
    }

    private static ParseResult<DigitalChannel> ParseDigitalRow(string row, int lineNo)
    {
        string[] f = row.Split(',');
        if (f.Length != 5)
            return ParseResult<DigitalChannel>.Fail(lineNo,
                $"디지털 채널 행은 5개 필드여야 합니다 (현재 {f.Length}개): Dn,ch_id,ph,ccbm,y");
        if (!int.TryParse(f[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx))
            return ParseResult<DigitalChannel>.Fail(lineNo, $"채널 인덱스(Dn)를 해석할 수 없습니다: '{f[0].Trim()}'");
        string yField = f[4].Trim();
        int normal = 0;
        if (yField.Length > 0 && !int.TryParse(yField, NumberStyles.Integer, CultureInfo.InvariantCulture, out normal))
            return ParseResult<DigitalChannel>.Fail(lineNo, $"정상 상태(y)를 해석할 수 없습니다: '{yField}'");
        if (normal is not (0 or 1))
            return ParseResult<DigitalChannel>.Fail(lineNo, $"정상 상태(y)는 0 또는 1이어야 합니다: {normal}");

        return ParseResult<DigitalChannel>.Ok(new DigitalChannel
        {
            Index = idx,
            Id = f[1].Trim(),
            Phase = f[2].Trim(),
            Ccbm = f[3].Trim(),
            NormalState = normal,
        });
    }

    private static bool TryParseDouble(string s, out double value) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool TryParseDoubleOrDefault(string s, double defaultValue, out double value)
    {
        string t = s.Trim();
        if (t.Length == 0)
        {
            value = defaultValue;
            return true;
        }

        return TryParseDouble(t, out value);
    }

    /// <summary>dd/mm/yyyy,hh:mm:ss.ssssss (소수부 0~9자리 허용, µs 정밀도로 절사).</summary>
    internal static bool TryParseTimestamp(string s, out DateTime result)
    {
        result = default;
        string[] parts = s.Trim().Split(',');
        if (parts.Length != 2)
            return false;

        string[] d = parts[0].Trim().Split('/');
        if (d.Length != 3)
            return false;
        if (!int.TryParse(d[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int day) ||
            !int.TryParse(d[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int month) ||
            !int.TryParse(d[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int year))
            return false;

        string[] t = parts[1].Trim().Split(':');
        if (t.Length != 3)
            return false;
        if (!int.TryParse(t[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hour) ||
            !int.TryParse(t[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int minute) ||
            !double.TryParse(t[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
            return false;

        if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month) ||
            hour is < 0 or > 23 || minute is < 0 or > 59 || seconds is < 0 or >= 60)
            return false;

        try
        {
            var date = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
            long secondTicks = (long)Math.Round(seconds * TimeSpan.TicksPerSecond);
            result = date.AddTicks(secondTicks);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
