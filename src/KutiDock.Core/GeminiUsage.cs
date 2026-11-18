using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KutiDock.Core
{
    /// <summary>Gemini uygulamasının kullanım sayfasındaki bir kart: yüzde ve sıfırlanma zamanı (yerel saat).</summary>
    public sealed record GeminiLimit(double? Percent, DateTime? ResetsAt, string ResetText);

    /// <summary>Eklentinin gönderdiği kartlardan çıkan değerler. Source: "sayfa" (Kutay sayfayı açtı) ya da "arka plan".</summary>
    public sealed record GeminiUsageData(GeminiLimit Current, GeminiLimit Weekly, DateTimeOffset At, string Source, bool BackgroundOk)
    {
        public string ToJson() => JsonSerializer.Serialize(this);
        public static GeminiUsageData FromJson(string json)
        {
            try { return JsonSerializer.Deserialize<GeminiUsageData>(json); } catch (Exception) { return null; }
        }
    }

    /// <summary>
    /// 1.6.16: gemini.google.com/usage sayfasındaki "Current usage" ve "Weekly limit" kartlarının metninden değer çıkarır.
    /// Metin Kutay'ın Chrome'undaki Kuti Dock eklentisinden, yalnızca 127.0.0.1 üzerinden gelir. Sayfa saatleri tarayıcının
    /// yerel saatidir (Kasa: Europe/Istanbul), bu yüzden ayrıca çevrilmez.
    /// </summary>
    public static class GeminiUsage
    {
        public const int Port = 47615;
        public const string SecretHeader = "X-KutiDock-Gizli";
        public const int MaxBody = 16 * 1024;
        public const string StoreFile = "gemini-kullanim.json";
        public const string SecretFile = "gemini-gizli.txt";
        /// <summary>Eklenti klasörüne dock'un yazdığı gizli değer dosyası (repoda yok).</summary>
        public const string ExtensionSecretFile = "dock-gizli.json";
        public const string ExtensionFolderName = "KutiDock-Gemini-Eklenti";
        public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

        static readonly Regex CurrentHead = new(@"Current usage|Geçerli kullanım|Mevcut kullanım|Güncel kullanım", RegexOptions.IgnoreCase);
        static readonly Regex WeeklyHead = new(@"Weekly limit|Weekly usage|Haftalık (?:sınır|limit|kullanım)", RegexOptions.IgnoreCase);
        static readonly Regex Pct = new(@"(?:(\d{1,3}(?:[.,]\d+)?)\s*%|%\s*(\d{1,3}(?:[.,]\d+)?))");
        static readonly Regex Time = new(@"(\d{1,2})[:.](\d{2})\s*(AM|PM|ÖÖ|ÖS)?", RegexOptions.IgnoreCase);
        static readonly Regex ResetPart = new(@"(?:Resets?|Sıfırlan\w*|Yenilen\w*)[^\n]*|[^\n]*(?:sıfırlanır|yenilenir)", RegexOptions.IgnoreCase);
        static readonly Regex InRel = new(@"in\s+(\d+)\s*(hours?|hrs?|h|minutes?|mins?|m)\b|(\d+)\s*(saat|dakika|dk)\s*(?:içinde|sonra)", RegexOptions.IgnoreCase);
        static readonly string[] MonEn = { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };
        static readonly string[] MonTr = { "oca", "şub", "mar", "nis", "may", "haz", "tem", "ağu", "eyl", "eki", "kas", "ara" };
        static readonly Regex DateEn = new(@"\b(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\.?\s+(\d{1,2})\b", RegexOptions.IgnoreCase);
        static readonly Regex DateTr = new(@"\b(\d{1,2})\s+(Oca|Şub|Mar|Nis|May|Haz|Tem|Ağu|Eyl|Eki|Kas|Ara)\w*", RegexOptions.IgnoreCase);

        /// <summary>Sayfa metninden iki kartı ayırır ve okur. İki karttan biri bile bulunamazsa null (uydurma yok).</summary>
        public static (GeminiLimit Current, GeminiLimit Weekly)? Parse(string text, DateTime readAtLocal)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var c = CurrentHead.Match(text); var w = WeeklyHead.Match(text);
            if (!c.Success || !w.Success) return null;
            string cur = c.Index < w.Index ? text[c.Index..w.Index] : text[c.Index..Math.Min(text.Length, c.Index + 300)];
            string wk = text[w.Index..Math.Min(text.Length, w.Index + 300)];
            if (w.Index < c.Index) wk = text[w.Index..c.Index];
            var a = Card(cur, readAtLocal); var b = Card(wk, readAtLocal);
            if (a.Percent == null && b.Percent == null) return null;
            return (a, b);
        }

        static GeminiLimit Card(string s, DateTime now)
        {
            double? pct = null;
            var m = Pct.Match(s);
            if (m.Success)
            {
                var v = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Replace(',', '.');
                if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d is >= 0 and <= 100) pct = d;
            }
            var r = ResetPart.Match(s);
            string rtext = r.Success ? r.Value.Trim() : null;
            return new GeminiLimit(pct, rtext != null ? ParseReset(rtext, now) : null, rtext);
        }

        /// <summary>"Resets at 2:30 PM" (bugün; geçtiyse yarın), "Resets Nov 18 at 5:30 PM", "18 Kas 17:30", "in 3 hours".</summary>
        public static DateTime? ParseReset(string s, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var rel = InRel.Match(s);
            if (rel.Success)
            {
                var n = int.Parse(rel.Groups[1].Success ? rel.Groups[1].Value : rel.Groups[3].Value, CultureInfo.InvariantCulture);
                var unit = (rel.Groups[2].Success ? rel.Groups[2].Value : rel.Groups[4].Value).ToLowerInvariant();
                return unit.StartsWith("h") || unit == "saat" ? now.AddHours(n) : now.AddMinutes(n);
            }
            var t = Time.Match(s);
            if (!t.Success) return null;
            int h = int.Parse(t.Groups[1].Value, CultureInfo.InvariantCulture), mi = int.Parse(t.Groups[2].Value, CultureInfo.InvariantCulture);
            var ap = t.Groups[3].Value.ToUpperInvariant();
            if (ap is "PM" or "ÖS" && h < 12) h += 12;
            if (ap is "AM" or "ÖÖ" && h == 12) h = 0;
            if (h > 23 || mi > 59) return null;
            int? month = null, day = null;
            var de = DateEn.Match(s);
            if (de.Success) { month = Array.IndexOf(MonEn, de.Groups[1].Value[..3].ToLowerInvariant()) + 1; day = int.Parse(de.Groups[2].Value, CultureInfo.InvariantCulture); }
            else
            {
                var dt = DateTr.Match(s);
                if (dt.Success) { month = Array.IndexOf(MonTr, dt.Groups[2].Value[..3].ToLower(new CultureInfo("tr-TR"))) + 1; day = int.Parse(dt.Groups[1].Value, CultureInfo.InvariantCulture); }
            }
            if (month is { } mo && mo > 0 && day is { } dd)
            {
                try
                {
                    var res = new DateTime(now.Year, mo, dd, h, mi, 0);
                    if (res < now.AddDays(-180)) res = res.AddYears(1);
                    return res;
                }
                catch (ArgumentOutOfRangeException) { return null; }
            }
            var today = new DateTime(now.Year, now.Month, now.Day, h, mi, 0);
            return today < now.AddMinutes(-1) ? today.AddDays(1) : today;
        }

        /// <summary>Panel satırı: "Güncel %0 · sıfırlanma 14:30" / "Haftalık %0 · sıfırlanma 18 Kas 17:30".</summary>
        public static string Line(string name, GeminiLimit l, DateTime nowLocal)
        {
            if (l == null) return $"{name} —";
            var p = l.Percent is { } v ? "%" + v.ToString("0.#", CultureInfo.InvariantCulture) : "—";
            var r = l.ResetsAt is { } at
                ? (at.Date == nowLocal.Date ? at.ToString("HH:mm", CultureInfo.InvariantCulture) : at.ToString("d MMM HH:mm", new CultureInfo("tr-TR")))
                : null;
            return r == null ? $"{name} {p}" : $"{name} {p} · sıfırlanma {r}";
        }

        public static bool IsStale(GeminiUsageData d, DateTimeOffset now) => d == null || now - d.At > StaleAfter;

        public static string NewSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        public static bool SecretMatches(string expected, string given) =>
            !string.IsNullOrEmpty(expected) && given != null &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(given));
    }
}
