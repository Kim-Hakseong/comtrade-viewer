using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ncv.App.Services;

public sealed class AppSettings
{
    [JsonPropertyName("recentFiles")]
    public List<string> RecentFiles { get; set; } = new();
}

/// <summary>
/// 설정 JSON 저장/로드 (C-14). 위치: %AppData%/ComtradeViewer/settings.json.
/// 손상·부재 시 기본값으로 조용히 복구 (앱 동작을 막지 않는다).
/// </summary>
public static class SettingsStore
{
    public const int MaxRecentFiles = 10;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ComtradeViewer", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                if (loaded is not null)
                {
                    loaded.RecentFiles = loaded.RecentFiles
                        .Where(f => !string.IsNullOrWhiteSpace(f))
                        .Take(MaxRecentFiles)
                        .ToList();
                    return loaded;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 손상된 설정은 무시하고 기본값 사용
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 설정 저장 실패는 치명적이지 않다
        }
    }

    /// <summary>최근 파일 목록 맨 앞에 추가 (중복 제거, 최대 10개).</summary>
    public static void AddRecent(AppSettings settings, string path)
    {
        settings.RecentFiles.RemoveAll(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        settings.RecentFiles.Insert(0, path);
        if (settings.RecentFiles.Count > MaxRecentFiles)
            settings.RecentFiles.RemoveRange(MaxRecentFiles, settings.RecentFiles.Count - MaxRecentFiles);
    }
}
