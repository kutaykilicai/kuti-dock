using System;
using System.Text.Json;

namespace KutiDock.Core
{
    /// <summary>
    /// 1.6.17: Lively duvar kağıdı bekçisi kararları. Lively, Shell_TrayWnd'nin sahibi değişince (Kuti Dock görev
    /// çubuğunun yerini aldığı için dock her yeniden başladığında) "Explorer çöktü" sanıp duvar kağıtlarını yeniden
    /// kurar; 30 sn içinde ikinci kez olursa vazgeçer ve WallpaperLayout.json'u boşaltır (Kasa, 2026-11-18 14:01).
    /// Bekçi eksikliği görünce LivelyGeriYukle.ps1 -Mod Kontrol'ü pencere açmadan çalıştırır.
    /// </summary>
    public static class WallpaperGuardPolicy
    {
        public static readonly TimeSpan AfterDisplayChange = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan AfterDockStart = TimeSpan.FromSeconds(40); // Lively'nin kendi yeniden kurulumu + 30 sn penceresi
        public static readonly TimeSpan Periodic = TimeSpan.FromMinutes(2);
        public static readonly TimeSpan MinGap = TimeSpan.FromMinutes(2);

        /// <summary>WallpaperLayout.json'daki kayıt sayısı; okunamazsa null.</summary>
        public static int? LayoutCount(string json)
        {
            if (json == null) return null;
            try
            {
                var r = JsonDocument.Parse(json).RootElement;
                return r.ValueKind == JsonValueKind.Array ? r.GetArrayLength() : null;
            }
            catch (JsonException) { return null; }
        }

        /// <summary>Duvar kağıdı eksik mi: düzen boş ya da düzendeki kayıttan az Lively oynatıcısı (mpv) çalışıyor.</summary>
        public static bool Missing(int? layoutCount, int livelyPlayers) =>
            layoutCount is 0 || livelyPlayers == 0 || (layoutCount is { } n && livelyPlayers < n);

        /// <summary>Geri yükleme betiği çalışsın mı. Tam ekran uygulama/oyun ön plandayken asla.</summary>
        public static bool ShouldRestore(bool livelyRunning, int? layoutCount, int livelyPlayers, bool fullscreen, bool scriptExists,
            DateTime? lastRunUtc, DateTime nowUtc) =>
            livelyRunning && scriptExists && !fullscreen && Missing(layoutCount, livelyPlayers)
            && (lastRunUtc is not { } last || nowUtc - last >= MinGap);
    }
}
