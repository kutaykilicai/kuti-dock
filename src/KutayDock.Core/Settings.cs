using System.Text.Json;
using System.Text.Json.Serialization;

namespace KutayDock.Core;

public enum DockStyle { Cam, Siyah, Gece, Beyaz }
public enum DockEdge { Alt, Ust, Sol, Sag }

/// <summary>Elle girilen limit değeri (yüzde + sıfırlanma zamanı).</summary>
public sealed class ManualLimit
{
    public double? Percent { get; set; }
    public DateTimeOffset? ResetsAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Tüm ayarlar. %APPDATA%\KutayDock\ayarlar.json içinde durur.</summary>
public sealed class Settings
{
    public DockStyle Style { get; set; } = DockStyle.Cam;
    public DockEdge Edge { get; set; } = DockEdge.Alt;
    public bool FullWidth { get; set; } = false;
    public double Scale { get; set; } = 1.0;
    /// <summary>Pencereler dock'un altına girmesin diye ekran alanı ayır.</summary>
    public bool ReserveSpace { get; set; } = false;
    /// <summary>Windows görev çubuğunu otomatik gizle. Varsayılan KAPALI.</summary>
    public bool AutoHideWindowsTaskbar { get; set; } = false;
    /// <summary>Tam ekran oyun/video varken dock'u gizle.</summary>
    public bool HideOnFullscreen { get; set; } = true;

    public List<string> Widgets { get; set; } = new()
    {
        "start", "search", "apps", "ai", "volume", "system", "network", "storage", "screenshot", "battery", "tray", "clock"
    };

    /// <summary>Ayar dosyası sürümü (1.0'da alan yoktu = 0). 2 = Kutay Dock 1.1. Load() her zaman Migrate() çağırır.</summary>
    public int Version { get; set; }

    // ShareX (boşsa otomatik bulunur)
    public string ShareXPath { get; set; } = "";

    // Hava durumu (varsayılan İstanbul)
    public string City { get; set; } = "İstanbul";
    public double Latitude { get; set; } = 41.01;
    public double Longitude { get; set; } = 28.97;

    // Limitler. Grok Bot ve Cursor kişisel planlar için belgelenmiş bir kullanım API'si sunmuyor;
    // değerler elle girilir (Ayarlar ya da widget paneli).
    public ManualLimit GrokBot { get; set; } = new();
    public ManualLimit Cursor { get; set; } = new();

    // Akıllı uyarılar
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
                return (JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Opts) ?? new Settings()).Migrate();
        }
        catch (Exception) { /* bozuk dosya: varsayılanlarla başla */ }
        return new Settings().Migrate();
    }

    /// <summary>1.0 ayarlarını 1.1'e taşır: kaldırılan widget'lar çıkar, Depolama ve Ekran görüntüsü eklenir.</summary>
    public Settings Migrate()
    {
        if (Version < 2) MigrateWidgets();
        if (Version < 3)
        {
            // 1.3.2: RocketDock gibi yüzen dock; ekranda yer ayırma ve tam genişlik varsayılan kapalı
            ReserveSpace = false;
            FullWidth = false;
            Version = 3;
        }
        if (Version < 4)
        {
            // 1.4.1: Everything araması Başlat'ın hemen sonrasına
            if (!Widgets.Contains("search")) Widgets.Insert(Widgets.IndexOf("start") + 1, "search");
            Version = 4;
        }
        return this;
    }

    void MigrateWidgets()
    {
        Widgets.RemoveAll(w => w is "media" or "screentime" or "calendar");
        int at = Widgets.IndexOf("battery");
        if (at < 0) at = Widgets.IndexOf("clock");
        if (at < 0) at = Widgets.Count;
        foreach (var w in new[] { "screenshot", "storage" })
            if (!Widgets.Contains(w)) Widgets.Insert(at, w);
        Version = 2;
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
