using System.Text.Json;
using System.Text.Json.Serialization;

namespace KutayDock.Core;

public enum DockStyle { Cam, Siyah, Gece, Beyaz }
public enum DockEdge { Alt, Ust }
public enum AiSource { Kapali, ClaudeCode, Codex }

/// <summary>Tüm ayarlar. %APPDATA%\KutayDock\ayarlar.json içinde durur.</summary>
public sealed class Settings
{
    public DockStyle Style { get; set; } = DockStyle.Cam;
    public DockEdge Edge { get; set; } = DockEdge.Alt;
    public bool FullWidth { get; set; } = false;
    public double Scale { get; set; } = 1.0;
    /// <summary>Pencereler dock'un altına girmesin diye ekran alanı ayır.</summary>
    public bool ReserveSpace { get; set; } = true;
    /// <summary>Windows görev çubuğunu otomatik gizle. Varsayılan KAPALI.</summary>
    public bool AutoHideWindowsTaskbar { get; set; } = false;
    /// <summary>Tam ekran oyun/video varken dock'u gizle.</summary>
    public bool HideOnFullscreen { get; set; } = true;

    public List<string> Widgets { get; set; } = new()
    {
        "start", "apps", "ai", "media", "volume", "system", "network", "screentime", "calendar", "battery", "tray", "clock"
    };

    // Takvim
    public List<string> IcsUrls { get; set; } = new();

    // Hava durumu (varsayılan İstanbul)
    public string City { get; set; } = "İstanbul";
    public double Latitude { get; set; } = 41.01;
    public double Longitude { get; set; } = 28.97;

    // Yapay zekâ kullanımı
    public AiSource AiSource { get; set; } = AiSource.ClaudeCode;
    public long ClaudeSessionTokenLimit { get; set; } = 20_000_000;
    public long ClaudeWeeklyTokenLimit { get; set; } = 300_000_000;
    public DayOfWeek WeeklyResetDay { get; set; } = DayOfWeek.Monday;
    public int WeeklyResetHour { get; set; } = 0;

    // Akıllı uyarılar
    public bool AlertMeeting { get; set; } = true;
    public int MeetingLeadMinutes { get; set; } = 5;
    public bool PauseMediaBeforeMeeting { get; set; } = true;
    public bool AlertAiLimit { get; set; } = true;
    public int AiAlertPercent { get; set; } = 85;
    public bool AlertBattery { get; set; } = true;
    public int BatteryAlertPercent { get; set; } = 15;
    public bool QuietHoursEnabled { get; set; } = true;
    public int QuietStartHour { get; set; } = 23;
    public int QuietEndHour { get; set; } = 8;

    public bool Use24Hour { get; set; } = true;

    static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Settings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Opts) ?? new Settings();
        }
        catch (Exception) { /* bozuk dosya: varsayılanlarla başla */ }
        return new Settings();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Opts));
        File.Move(tmp, path, overwrite: true);
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Opts);
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Opts);
}
