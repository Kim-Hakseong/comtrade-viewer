using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ncv.App.Services;
using Ncv.Core.Model;

namespace Ncv.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppSettings _settings;

    public MainWindowViewModel()
    {
        _settings = SettingsStore.Load();
        foreach (var f in _settings.RecentFiles)
            RecentFiles.Add(f);
    }

    /// <summary>최근 파일 (C-14, 최대 10개).</summary>
    public ObservableCollection<string> RecentFiles { get; } = new();

    public bool HasRecentFiles => RecentFiles.Count > 0;

    [RelayCommand]
    private async Task OpenRecentAsync(string? path)
    {
        if (path is null)
            return;
        if (!File.Exists(path))
        {
            ErrorMessage = $"파일이 존재하지 않습니다: {path}";
            RecentFiles.Remove(path);
            _settings.RecentFiles.RemoveAll(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
            SettingsStore.Save(_settings);
            OnPropertyChanged(nameof(HasRecentFiles));
            return;
        }

        await LoadAsync(path);
    }

    private void RememberRecent(string path)
    {
        SettingsStore.AddRecent(_settings, path);
        SettingsStore.Save(_settings);
        RecentFiles.Clear();
        foreach (var f in _settings.RecentFiles)
            RecentFiles.Add(f);
        OnPropertyChanged(nameof(HasRecentFiles));
    }
    [ObservableProperty]
    private string _statusMessage = "CFG 파일을 열거나 창으로 드래그하세요.";

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private double _loadProgress;

    [ObservableProperty]
    private ComtradeRecord? _record;

    /// <summary>레인 모드 (false = 오버레이).</summary>
    [ObservableProperty]
    private bool _laneMode;

    partial void OnRecordChanged(ComtradeRecord? value) => OnPropertyChanged(nameof(CanExportCsv));

    /// <summary>가시 구간 시작 (첫 샘플 기준 초).</summary>
    [ObservableProperty]
    private double _viewStart;

    /// <summary>가시 구간 길이 (초).</summary>
    [ObservableProperty]
    private double _viewSpan;

    /// <summary>커서 1/2 시각 (첫 샘플 기준 초). NaN = 미배치.</summary>
    [ObservableProperty]
    private double _cursor1Time = double.NaN;

    [ObservableProperty]
    private double _cursor2Time = double.NaN;

    /// <summary>시간축을 트리거 t=0 기준 상대로 표시 (C-07).</summary>
    [ObservableProperty]
    private bool _relativeToTrigger;

    /// <summary>커서 Δt 요약 문자열.</summary>
    [ObservableProperty]
    private string _cursorSummary = "";

    public ObservableCollection<CursorReadoutRow> CursorReadouts { get; } = new();

    /// <summary>페이저 패널 표시 (C-12).</summary>
    [ObservableProperty]
    private bool _showPhasor;

    /// <summary>기준 채널(첫 가시 아날로그) 대비 상대각 표시.</summary>
    [ObservableProperty]
    private bool _relativeAngles;

    /// <summary>DFT 창 정보 (N, 창 시작) 상태 표기.</summary>
    [ObservableProperty]
    private string _phasorWindowInfo = "";

    public ObservableCollection<PhasorRow> PhasorRows { get; } = new();

    public ObservableCollection<ChannelViewModel> AnalogChannels { get; } = new();
    public ObservableCollection<ChannelViewModel> DigitalChannels { get; } = new();

    /// <summary>플롯 무효화 요청 (뷰포트/토글 변경 시 View가 구독).</summary>
    public event Action? PlotInvalidated;

    public void RequestPlotInvalidate() => PlotInvalidated?.Invoke();

    partial void OnLaneModeChanged(bool value) => RequestPlotInvalidate();
    partial void OnViewStartChanged(double value)
    {
        if (!CursorsActive)
            UpdatePhasors(); // 커서 미배치 시 페이저 창은 가시 구간 시작 기준
        RequestPlotInvalidate();
    }
    partial void OnViewSpanChanged(double value) => RequestPlotInvalidate();
    partial void OnRelativeToTriggerChanged(bool value)
    {
        UpdateCursorReadouts();
        RequestPlotInvalidate();
    }

    partial void OnCursor1TimeChanged(double value)
    {
        UpdateCursorReadouts();
        UpdatePhasors();
        RequestPlotInvalidate();
    }

    partial void OnShowPhasorChanged(bool value)
    {
        UpdatePhasors();
        RequestPlotInvalidate();
    }

    partial void OnRelativeAnglesChanged(bool value)
    {
        UpdatePhasors();
        RequestPlotInvalidate();
    }

    partial void OnCursor2TimeChanged(double value)
    {
        UpdateCursorReadouts();
        RequestPlotInvalidate();
    }

    public bool CursorsActive => !double.IsNaN(Cursor1Time);

    /// <summary>커서 2개 배치/해제 (C-06). 가시 구간 1/3·2/3 지점에 자유 배치.</summary>
    [RelayCommand]
    private void ToggleCursors()
    {
        if (Record is null)
            return;

        if (double.IsNaN(Cursor1Time))
        {
            Cursor1Time = ViewStart + ViewSpan / 3;
            Cursor2Time = ViewStart + ViewSpan * 2 / 3;
        }
        else
        {
            Cursor1Time = double.NaN;
            Cursor2Time = double.NaN;
        }
    }

    internal void UpdateCursorReadouts()
    {
        CursorReadouts.Clear();
        var rec = Record;
        if (rec is null || double.IsNaN(Cursor1Time) || double.IsNaN(Cursor2Time))
        {
            CursorSummary = "";
            OnPropertyChanged(nameof(CursorsActive));
            return;
        }

        int i1 = rec.Time.NearestIndexOf(Cursor1Time);
        int i2 = rec.Time.NearestIndexOf(Cursor2Time);
        double t1 = rec.Time.TimeAt(i1);
        double t2 = rec.Time.TimeAt(i2);
        CursorSummary =
            $"C1 {FormatSeconds(t1)} · C2 {FormatSeconds(t2)} · Δt {(t2 - t1) * 1000:0.###}ms";

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var ch in AnalogChannels.Where(c => c.IsVisible))
        {
            double v1 = rec.Analog[ch.ChannelIndex][i1];
            double v2 = rec.Analog[ch.ChannelIndex][i2];
            CursorReadouts.Add(new CursorReadoutRow(
                ch.DisplayName, ch.Brush,
                v1.ToString("0.###", inv), v2.ToString("0.###", inv), (v2 - v1).ToString("0.###", inv)));
        }

        foreach (var ch in DigitalChannels.Where(c => c.IsVisible))
        {
            bool v1 = rec.Digital[ch.ChannelIndex][i1];
            bool v2 = rec.Digital[ch.ChannelIndex][i2];
            CursorReadouts.Add(new CursorReadoutRow(
                ch.Name, ch.Brush, v1 ? "1" : "0", v2 ? "1" : "0", v1 == v2 ? "—" : "변화"));
        }

        OnPropertyChanged(nameof(CursorsActive));
    }

    private string FormatSeconds(double t)
    {
        if (RelativeToTrigger && Record is not null && Record.Time.TriggerIndex >= 0)
            t -= Record.Time.TimeAt((int)Record.Time.TriggerIndex);
        return $"{t * 1000:0.###}ms";
    }

    [RelayCommand]
    private void ResetView()
    {
        if (Record is null)
            return;
        ViewStart = Record.Time.TimeAt(0);
        ViewSpan = Math.Max(Record.Time.TotalSpan, 1e-6);
        RequestPlotInvalidate();
    }

    /// <summary>CFG/CFF(또는 DAT) 경로에서 짝 파일을 찾아 백그라운드 로드.</summary>
    public async Task LoadAsync(string path)
    {
        ErrorMessage = "";
        string ext = Path.GetExtension(path);

        if (ext.Equals(".cff", StringComparison.OrdinalIgnoreCase))
        {
            await LoadCffAsync(path);
            return;
        }

        string cfgPath;
        if (ext.Equals(".dat", StringComparison.OrdinalIgnoreCase))
        {
            string? found = FindSibling(path, ".cfg");
            if (found is null)
            {
                ErrorMessage = $"동명 CFG 파일을 찾을 수 없습니다: {Path.GetFileName(path)}";
                return;
            }

            cfgPath = found;
        }
        else if (ext.Equals(".cfg", StringComparison.OrdinalIgnoreCase))
        {
            cfgPath = path;
        }
        else
        {
            ErrorMessage = $"지원하지 않는 확장자입니다: {ext} (CFG/DAT/CFF)";
            return;
        }

        string? datPath = FindSibling(cfgPath, ".dat");
        if (datPath is null)
        {
            ErrorMessage = $"동명 DAT 파일을 찾을 수 없습니다: {Path.GetFileName(cfgPath)}";
            return;
        }

        IsLoading = true;
        LoadProgress = 0;
        StatusMessage = $"로드 중: {Path.GetFileName(cfgPath)}";
        try
        {
            var progress = new Progress<double>(p => LoadProgress = p);
            var result = await Task.Run(() =>
            {
                using var cfgStream = File.OpenRead(cfgPath);
                using var datStream = File.OpenRead(datPath);
                return ComtradeRecord.Load(cfgStream, datStream, progress);
            });

            if (!result.Success)
            {
                ErrorMessage = $"파싱 실패 — {result}";
                StatusMessage = "파일을 열지 못했습니다.";
                return;
            }

            ApplyRecord(result.Value!, Path.GetFileName(cfgPath));
            RememberRecent(Path.GetFullPath(cfgPath));
        }
        catch (IOException ex)
        {
            ErrorMessage = $"파일 읽기 오류: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            LoadProgress = 1;
        }
    }

    private async Task LoadCffAsync(string path)
    {
        IsLoading = true;
        LoadProgress = 0;
        StatusMessage = $"로드 중: {Path.GetFileName(path)}";
        try
        {
            var progress = new Progress<double>(p => LoadProgress = p);
            var result = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                return Ncv.Core.Format.CffReader.Read(stream, progress);
            });

            if (!result.Success)
            {
                ErrorMessage = $"파싱 실패 — {result}";
                StatusMessage = "파일을 열지 못했습니다.";
                return;
            }

            ApplyRecord(result.Value!, Path.GetFileName(path));
            RememberRecent(Path.GetFullPath(path));
        }
        catch (IOException ex)
        {
            ErrorMessage = $"파일 읽기 오류: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            LoadProgress = 1;
        }
    }

    protected void ApplyRecord(ComtradeRecord rec, string fileName)
    {
        Record = rec;
        AnalogChannels.Clear();
        DigitalChannels.Clear();

        int colorIdx = 0;
        for (int i = 0; i < rec.Cfg.AnalogCount; i++)
        {
            var ch = rec.Cfg.AnalogChannels[i];
            var vm = new ChannelViewModel
            {
                Name = ch.Id,
                Unit = ch.Unit,
                IsDigital = false,
                ChannelIndex = i,
                Color = ChannelViewModel.Palette[colorIdx++ % ChannelViewModel.Palette.Length],
            };
            vm.PropertyChanged += OnChannelToggled;
            AnalogChannels.Add(vm);
        }

        for (int i = 0; i < rec.Cfg.DigitalCount; i++)
        {
            var ch = rec.Cfg.DigitalChannels[i];
            var vm = new ChannelViewModel
            {
                Name = ch.Id,
                Unit = "",
                IsDigital = true,
                ChannelIndex = i,
                Color = ChannelViewModel.Palette[colorIdx++ % ChannelViewModel.Palette.Length],
            };
            vm.PropertyChanged += OnChannelToggled;
            DigitalChannels.Add(vm);
        }

        ViewStart = rec.Time.TimeAt(0);
        ViewSpan = Math.Max(rec.Time.TotalSpan, 1e-6);
        Cursor1Time = double.NaN;
        Cursor2Time = double.NaN;
        UpdateCursorReadouts();

        StatusMessage =
            $"{fileName} — {rec.Cfg.StationName} · 아날로그 {rec.Cfg.AnalogCount} / 디지털 {rec.Cfg.DigitalCount} · " +
            $"{rec.SampleCount:N0} 샘플 · {rec.Cfg.LineFrequency:0.#}Hz · {rec.Cfg.DataType}";
        RequestPlotInvalidate();
    }

    private void OnChannelToggled(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChannelViewModel.IsVisible))
        {
            UpdateCursorReadouts();
            UpdatePhasors();
            RequestPlotInvalidate();
        }
    }

    // ---- 페이저 (C-12): 커서1(미배치 시 가시 구간 시작)에서 시작하는 1주기 DFT ----

    internal void UpdatePhasors()
    {
        PhasorRows.Clear();
        var rec = Record;
        if (!ShowPhasor || rec is null || rec.Cfg.LineFrequency <= 0)
        {
            PhasorWindowInfo = "";
            return;
        }

        double windowStartTime = CursorsActive ? Cursor1Time : ViewStart;
        int startIdx = rec.Time.NearestIndexOf(windowStartTime);

        double samp = SampleRateAt(rec, startIdx);
        if (samp <= 0)
        {
            PhasorWindowInfo = "샘플레이트 미지정 — 페이저 계산 불가";
            return;
        }

        double exactN = samp / rec.Cfg.LineFrequency;
        int n = (int)Math.Round(exactN);
        if (n < 2)
        {
            PhasorWindowInfo = "1주기 샘플 수가 부족합니다";
            return;
        }

        if (startIdx + n > rec.SampleCount)
            startIdx = Math.Max(0, rec.SampleCount - n);

        bool rounded = Math.Abs(exactN - n) > 1e-9;
        PhasorWindowInfo = $"창 시작 {FormatSeconds(rec.Time.TimeAt(startIdx))} · N={n}" +
                           (rounded ? $" (반올림, {exactN:0.##})" : "");

        var visible = AnalogChannels.Where(c => c.IsVisible).ToList();
        double refAngle = 0;
        var phasors = new List<(ChannelViewModel Ch, Ncv.Core.Analysis.Phasor P)>();
        foreach (var ch in visible)
        {
            var p = Ncv.Core.Analysis.PhasorDft.Compute(rec.Analog[ch.ChannelIndex], startIdx, n);
            if (p is not null)
                phasors.Add((ch, p.Value));
        }

        if (RelativeAngles && phasors.Count > 0)
            refAngle = phasors[0].P.AngleDegrees;

        foreach (var (ch, p) in phasors)
        {
            double angle = Ncv.Core.Analysis.PhasorDft.NormalizeAngle(p.AngleDegrees - refAngle);
            PhasorRows.Add(new PhasorRow(ch.Name, ch.Brush, p.Magnitude, angle, ch.Unit));
        }
    }

    // ---- CSV 내보내기 (C-13): 커서 활성 시 커서 구간, 아니면 가시 구간 ----

    public bool CanExportCsv => Record is not null;

    /// <summary>내보낼 구간 [start, end) 샘플 인덱스.</summary>
    internal (int Start, int End) ExportRange()
    {
        var rec = Record!;
        double t0, t1;
        if (CursorsActive)
        {
            t0 = Math.Min(Cursor1Time, Cursor2Time);
            t1 = Math.Max(Cursor1Time, Cursor2Time);
        }
        else
        {
            t0 = ViewStart;
            t1 = ViewStart + ViewSpan;
        }

        int start = rec.Time.NearestIndexOf(t0);
        int end = Math.Min(rec.SampleCount, rec.Time.NearestIndexOf(t1) + 1);
        return (start, Math.Max(end, start + 1));
    }

    /// <summary>표시 중 채널·구간을 CSV로 기록. 성공 시 행 수 반환.</summary>
    public async Task<int> ExportCsvAsync(Stream target)
    {
        var rec = Record ?? throw new InvalidOperationException("열린 레코드가 없습니다.");
        var (start, end) = ExportRange();
        int[] analog = AnalogChannels.Where(c => c.IsVisible).Select(c => c.ChannelIndex).ToArray();
        int[] digital = DigitalChannels.Where(c => c.IsVisible).Select(c => c.ChannelIndex).ToArray();
        bool relative = RelativeToTrigger;

        await Task.Run(() =>
        {
            using var writer = new StreamWriter(target, leaveOpen: true);
            Ncv.Core.Export.CsvExporter.Write(writer, rec, start, end, analog, digital, relative);
        });

        StatusMessage = $"CSV 내보내기 완료 — {end - start:N0}행 × {analog.Length + digital.Length}채널";
        return end - start;
    }

    private static double SampleRateAt(ComtradeRecord rec, int sampleIdx)
    {
        foreach (var seg in rec.Cfg.SampleRates)
        {
            if (sampleIdx < seg.EndSample)
                return seg.SamplesPerSecond;
        }

        return rec.Cfg.SampleRates.Count > 0 ? rec.Cfg.SampleRates[^1].SamplesPerSecond : 0;
    }

    // ---- 팬/줌 (C-05) ----

    private double RecordStart => Record?.Time.TimeAt(0) ?? 0;
    private double RecordEnd => Record is { } r ? r.Time.TimeAt(r.SampleCount - 1) : 0;

    /// <summary>커서 중심 휠 줌. factor > 1 = 확대.</summary>
    public void ZoomAt(double timeCenter, double factor)
    {
        if (Record is null || ViewSpan <= 0)
            return;

        double total = Math.Max(RecordEnd - RecordStart, 1e-9);
        double minSpan = Math.Min(total, 16.0 / SampleRateHint()); // 최소 ~16샘플
        double newSpan = Math.Clamp(ViewSpan / factor, minSpan, total);
        double newStart = timeCenter - (timeCenter - ViewStart) * newSpan / ViewSpan;
        ViewStart = Math.Clamp(newStart, RecordStart, RecordEnd - newSpan);
        ViewSpan = newSpan;
    }

    /// <summary>드래그 팬 (초 단위 이동).</summary>
    public void PanBy(double deltaSeconds)
    {
        if (Record is null)
            return;
        ViewStart = Math.Clamp(ViewStart + deltaSeconds, RecordStart, Math.Max(RecordStart, RecordEnd - ViewSpan));
    }

    /// <summary>미니맵 클릭: 가시 구간 중심 이동.</summary>
    public void CenterViewAt(double time)
    {
        if (Record is null)
            return;
        ViewStart = Math.Clamp(time - ViewSpan / 2, RecordStart, Math.Max(RecordStart, RecordEnd - ViewSpan));
    }

    private double SampleRateHint()
    {
        if (Record is { } r && r.Cfg.SampleRates.Count > 0)
            return r.Cfg.SampleRates[0].SamplesPerSecond;
        return 1000;
    }

    /// <summary>같은 폴더에서 동명·다른 확장자 파일을 대소문자 무관으로 찾는다 (C-09).</summary>
    internal static string? FindSibling(string filePath, string wantedExtension)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".";
        string baseName = Path.GetFileNameWithoutExtension(filePath);
        if (!Directory.Exists(dir))
            return null;

        return Directory.EnumerateFiles(dir)
            .FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).Equals(baseName, StringComparison.OrdinalIgnoreCase) &&
                Path.GetExtension(f).Equals(wantedExtension, StringComparison.OrdinalIgnoreCase));
    }
}
