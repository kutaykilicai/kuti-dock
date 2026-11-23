using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KutiDock.Core;

namespace KutiDock
{
    /// <summary>
    /// 1.6.25: "--pencere-secici-testi &lt;çıktı.txt&gt;" pencere açmadan, şu an açık pencereleri dock gibi gruplar (görünür üst
    /// düzey pencere başına) ve birden çok penceresi olan her uygulamada tıklamanın seçiciyi açacağını, seçicinin kaç kart
    /// listeleyeceğini yazar. Gezgin pencereleri (CabinetWClass) tek explorer.exe sürecinde olsa da ayrı kart olmalı.
    /// Çıkış kodu: 0 geçti, 1 hata var.
    /// </summary>
    internal static class PickerSelfTest
    {
        public const string Arg = "--pencere-secici-testi";

        public static int Run(string[] args)
        {
            var lines = new List<string>();
            bool ok = true;
            void Check(string name, bool pass, string detail = "") { ok &= pass; lines.Add($"{(pass ? "GECTI" : "KALDI")}  {name}{(detail.Length > 0 ? "  (" + detail + ")" : "")}"); }
            try
            {
                var groups = new Dictionary<string, List<IntPtr>>();
                foreach (var h in Native.TaskbarWindows(IntPtr.Zero))
                {
                    Native.GetWindowThreadProcessId(h, out uint pid);
                    var path = Native.ProcessPath(pid);
                    if (path == null) continue;
                    bool packaged = Path.GetFileName(path).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase)
                        || path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);
                    var key = AppGrouping.WindowKey(path, Native.AppUserModelId(h), packaged);
                    if (!groups.TryGetValue(key, out var l)) groups[key] = l = new List<IntPtr>();
                    l.Add(h);
                }
                foreach (var (key, wins) in groups)
                {
                    var alive = wins.Where(Native.IsWindowVisible).ToList();
                    var decision = WindowPicker.Decide(alive.Count, false);
                    lines.Add($"{Path.GetFileName(key)}: {alive.Count} pencere -> {decision}");
                    if (alive.Count > 1)
                    {
                        foreach (var h in alive) lines.Add($"    kart: {WindowPicker.CardTitle(Native.WindowTitle(h))} [{Native.ClassName(h)}]");
                        Check($"{Path.GetFileName(key)}: tıklama seçiciyi açar, {alive.Count} kart", decision == AppClick.Picker && WindowPicker.Decide(alive.Count, true) == AppClick.Picker);
                    }
                }
                int cabinets = groups.Values.SelectMany(w => w).Count(h => Native.ClassName(h) == "CabinetWClass");
                var explorer = groups.FirstOrDefault(g => g.Key.EndsWith(@"\explorer.exe", StringComparison.OrdinalIgnoreCase)).Value ?? new List<IntPtr>();
                Check("Gezgin pencereleri ayrı kart", explorer.Count(h => Native.ClassName(h) == "CabinetWClass") == cabinets, $"{cabinets} Gezgin penceresi");
                if (cabinets >= 2) lines.Add($"GEZGIN: {cabinets} pencere açık, seçici {cabinets} kart listeler");
                else lines.Add("GEZGIN: 2'den az Gezgin penceresi açık, iki pencereli durum canlı denenemedi");
            }
            catch (Exception ex) { Check("beklenmeyen hata", false, ex.GetType().Name + ": " + ex.Message); }
            lines.Add(ok ? "SONUC: GECTI" : "SONUC: KALDI");
            try { if (args.Length > 1) File.WriteAllLines(args[1], lines); } catch (Exception) { }
            return ok ? 0 : 1;
        }
    }
}
