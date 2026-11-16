using System.Text.Json;

namespace KutiDock.Core;

public sealed record WeatherNow(double TempC, int Code, bool IsDay, double MaxC, double MinC);

/// <summary>Open-Meteo (anahtarsız, ücretsiz) yanıtını okur ve WMO kodlarını Türkçeye çevirir.</summary>
public static class Weather
{
    public static string Url(double lat, double lon) =>
        FormattableString.Invariant($"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,weather_code,is_day&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=1");

    public static WeatherNow Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var c = doc.RootElement.GetProperty("current");
        var d = doc.RootElement.GetProperty("daily");
        return new WeatherNow(
            c.GetProperty("temperature_2m").GetDouble(),
            c.GetProperty("weather_code").GetInt32(),
            c.TryGetProperty("is_day", out var day) && day.GetInt32() == 1,
            d.GetProperty("temperature_2m_max")[0].GetDouble(),
            d.GetProperty("temperature_2m_min")[0].GetDouble());
    }

    public static (string Glyph, string Text) Describe(int code, bool isDay) => code switch
    {
        0 => (isDay ? "☀" : "☾", "Açık"),
        1 or 2 => (isDay ? "⛅" : "☁", "Parçalı bulutlu"),
        3 => ("☁", "Kapalı"),
        45 or 48 => ("🌫", "Sisli"),
        >= 51 and <= 57 => ("🌦", "Çiseleme"),
        >= 61 and <= 67 => ("🌧", "Yağmurlu"),
        >= 71 and <= 77 => ("❄", "Karlı"),
        >= 80 and <= 82 => ("🌧", "Sağanak"),
        85 or 86 => ("🌨", "Kar sağanağı"),
        >= 95 => ("⛈", "Fırtına"),
        _ => ("•", "—"),
    };
}
