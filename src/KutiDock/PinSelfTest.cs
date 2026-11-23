using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KutiDock.Core;

namespace KutiDock
{
    /// <summary>
    /// 1.6.24: "--sabitleme-testi &lt;çıktı.txt&gt;" pencere açmadan sabitleme mantığını Windows'ta uçtan uca dener: geçici bir
    /// klasörde kısayol yazma, Mağaza uygulaması kısayolu, kopyalama, sıralama (gerçek oluşturulma zamanları) ve kaldırma.
    /// Gerçek dock sabitlemelerine dokunmaz. Çıkış kodu: 0 hepsi geçti, 1 hata var.
    /// </summary>
    internal static class PinSelfTest
    {
        public const string Arg = "--sabitleme-testi";

        public static int Run(string[] args)
        {
            var lines = new List<string>();
            bool ok = true;
            void Check(string name, bool pass, string detail = "") { ok &= pass; lines.Add($"{(pass ? "GECTI" : "KALDI")}  {name}{(detail.Length > 0 ? "  (" + detail + ")" : "")}"); }
            var root = Path.Combine(Path.GetTempPath(), "kutidock-sabitleme-testi-" + Guid.NewGuid().ToString("N")[..8]);
            try
            {
                var pins = new PinFolder(Path.Combine(root, "TaskBar"), Path.Combine(root, "kaldirilan"));
                var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
                var a = pins.AddNew("Not Defteri", p => ShellIcons.CreateLink(p, notepad), int.MaxValue);
                Check("exe'ye kısayol yazıldı", a != null && File.Exists(a));
                Check("kısayol hedefi exe", a != null && string.Equals(ShellIcons.LinkTarget(a), notepad, StringComparison.OrdinalIgnoreCase), a == null ? "" : ShellIcons.LinkTarget(a) ?? "null");
                var b = pins.AddNew("Windows", p => ShellIcons.CreateLink(p, Environment.GetFolderPath(Environment.SpecialFolder.Windows)), int.MaxValue);
                Check("klasöre kısayol yazıldı", b != null && File.Exists(b));
                const string calc = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";
                var c = pins.AddNew("Hesap Makinesi", p => ShellIcons.CreateLink(p, @"shell:AppsFolder\" + calc), 0);
                var aumid = c == null ? null : ShellIcons.LinkAppsFolderAumid(c);
                Check("Mağaza uygulaması kısayolu AUMID'i", c != null && string.Equals(aumid, calc, StringComparison.OrdinalIgnoreCase), aumid ?? "null");
                if (c != null) Check("Mağaza anahtarı pencere anahtarıyla aynı", AppGrouping.SameKey(AppGrouping.PinnedKey(c, null, "", null, aumid), AppGrouping.WindowKey(@"C:\x\CalculatorApp.exe", calc, true)));
                var names = pins.Ordered().Select(Path.GetFileNameWithoutExtension).ToList();
                Check("başa ekleme sırası", string.Join(",", names) == (c != null ? "Hesap Makinesi,Not Defteri,Windows" : "Not Defteri,Windows"), string.Join(",", names));
                if (a != null) pins.Move(a, int.MaxValue);
                names = pins.Ordered().Select(Path.GetFileNameWithoutExtension).ToList();
                Check("sıralama (sona taşıma)", names.LastOrDefault() == "Not Defteri", string.Join(",", names));
                // Başlat menüsünde Not Defteri ya da herhangi bir exe kısayolu: bulunup kopyalanır
                var start = PinFolder.Shortcuts(new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) }).Take(400).ToList();
                var withExe = start.FirstOrDefault(l => ShellIcons.LinkTarget(l) != null);
                if (withExe != null)
                {
                    var key = ShellIcons.LinkTarget(withExe).ToLowerInvariant();
                    var found = PinFolder.FindShortcut(key, start, l => AppGrouping.PinnedKey(l, ShellIcons.LinkTarget(l), null, null));
                    Check("Başlat menüsü kısayolu bulundu", found != null, Path.GetFileName(found ?? "yok"));
                    if (found != null) { var copy = pins.AddCopy(found, 1); Check("kısayol kopyası 2. sırada", pins.Ordered().IndexOf(copy) == 1); }
                }
                if (b != null)
                {
                    var moved = pins.Remove(b);
                    Check("kaldırma: yedeğe taşındı", moved != null && File.Exists(moved) && !File.Exists(b));
                }
                lines.Add("son sıra: " + string.Join(", ", pins.Ordered().Select(Path.GetFileName)));
                // Gerçek sabitlemeler yalnızca okunur: sıra ve dock anahtarları
                var real = new PinFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"), Path.Combine(root, "kullanilmaz"));
                foreach (var l in real.Ordered())
                {
                    var t = ShellIcons.LinkTarget(l); var (ar, au) = ShellIcons.LinkIdentity(l);
                    lines.Add($"sabit: {Path.GetFileName(l)} -> {AppGrouping.PinnedKey(l, t, ar, au, t == null ? ShellIcons.LinkAppsFolderAumid(l) : null)}");
                }
            }
            catch (Exception ex) { Check("beklenmeyen hata", false, ex.GetType().Name + ": " + ex.Message); }
            finally { try { Directory.Delete(root, true); } catch (Exception) { } }
            lines.Add(ok ? "SONUC: GECTI" : "SONUC: KALDI");
            try { if (args.Length > 1) File.WriteAllLines(args[1], lines); } catch (Exception) { }
            return ok ? 0 : 1;
        }
    }
}
