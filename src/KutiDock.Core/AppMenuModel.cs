namespace KutiDock.Core;

/// <summary>Bir menü öğesinin yaptığı iş (1.6.6 Finder tarzı üst çubuk).</summary>
public enum MenuCommand { None, Keys, About, Hide, HideOthers, Quit, CloseWindow, Minimize, Zoom, BringAllToFront, Open }

/// <summary>Menü öğesi. Keys: Arg kısayolu öndeki pencereye gönderilir; Open: Arg kabuk yolu açılır.</summary>
public sealed record MenuEntry(string Text, MenuCommand Command = MenuCommand.None, string? Arg = null, string? Shortcut = null)
{
    public static readonly MenuEntry Separator = new("-");
    public bool IsSeparator => Text == "-";
    public static MenuEntry Key(string text, string keys) => new(text, MenuCommand.Keys, keys, keys);
}

public sealed record MenuSpec(string Title, IReadOnlyList<MenuEntry> Items);

/// <summary>
/// Kendi menü çubuğu olmayan uygulamalar (Chrome, Electron, UWP…) için macOS benzeri işlevli genel menüler. Kısayollar
/// Windows'taki karşılıklarıdır ve öndeki pencereye gönderilir; masaüstü öndeyken Finder karşılığı Gezgin menüleri.
/// </summary>
public static class GenericMenus
{
    public static MenuSpec AppMenu(string app) => new(app,
    [
        new($"{app} hakkında (dosya konumu)", MenuCommand.About),
        MenuEntry.Separator,
        new($"{app} uygulamasını gizle", MenuCommand.Hide),
        new("Diğerlerini gizle", MenuCommand.HideOthers),
        MenuEntry.Separator,
        new($"{app} uygulamasından çık", MenuCommand.Quit, Shortcut: "Alt+F4"),
    ]);

    public static readonly MenuSpec Go = new("Git",
    [
        new("Masaüstü", MenuCommand.Open, "shell:Desktop"),
        new("Belgeler", MenuCommand.Open, "shell:Personal"),
        new("İndirilenler", MenuCommand.Open, "shell:Downloads"),
        new("Resimler", MenuCommand.Open, "shell:My Pictures"),
        MenuEntry.Separator,
        new("Bu Bilgisayar", MenuCommand.Open, "shell:MyComputerFolder"),
        new("Geri Dönüşüm Kutusu", MenuCommand.Open, "shell:RecycleBinFolder"),
    ]);

    /// <summary>Uygulama adı menüsünden sonraki başlıklar (uygulama menüsü <see cref="AppMenu"/> ayrı).</summary>
    public static List<MenuSpec> For(string app, bool desktop, bool explorer)
    {
        var help = new MenuSpec("Yardım", [MenuEntry.Key($"{app} yardımı", "F1"), new("Windows'ta ara", MenuCommand.Keys, "Win+S")]);
        if (desktop)
            return
            [
                new("Dosya", [new("Yeni Gezgin penceresi", MenuCommand.Open, "explorer"), new("Geri Dönüşüm Kutusu", MenuCommand.Open, "shell:RecycleBinFolder")]),
                Go,
                help,
            ];
        var list = new List<MenuSpec>
        {
            new("Dosya",
            [
                MenuEntry.Key("Yeni pencere", "Ctrl+N"), MenuEntry.Key("Aç…", "Ctrl+O"),
                MenuEntry.Separator,
                MenuEntry.Key("Kaydet", "Ctrl+S"), MenuEntry.Key("Yazdır…", "Ctrl+P"),
                MenuEntry.Separator,
                new("Pencereyi kapat", MenuCommand.CloseWindow),
            ]),
            new("Düzen",
            [
                MenuEntry.Key("Geri al", "Ctrl+Z"), MenuEntry.Key("Yinele", "Ctrl+Y"),
                MenuEntry.Separator,
                MenuEntry.Key("Kes", "Ctrl+X"), MenuEntry.Key("Kopyala", "Ctrl+C"), MenuEntry.Key("Yapıştır", "Ctrl+V"),
                MenuEntry.Key("Tümünü seç", "Ctrl+A"),
                MenuEntry.Separator,
                MenuEntry.Key("Bul…", "Ctrl+F"),
            ]),
            new("Görüntü",
            [
                MenuEntry.Key("Tam ekran", "F11"),
                MenuEntry.Separator,
                MenuEntry.Key("Yakınlaştır", "Ctrl+Plus"), MenuEntry.Key("Uzaklaştır", "Ctrl+Minus"), MenuEntry.Key("Gerçek boyut", "Ctrl+0"),
            ]),
        };
        if (explorer) list.Add(Go);
        list.Add(new("Pencere",
        [
            new("Küçült", MenuCommand.Minimize), new("Büyüt / eski boyut", MenuCommand.Zoom),
            MenuEntry.Separator,
            new("Sola yasla", MenuCommand.Keys, "Win+Left", "Win+Left"), new("Sağa yasla", MenuCommand.Keys, "Win+Right", "Win+Right"),
            MenuEntry.Separator,
            new("Tümünü öne getir", MenuCommand.BringAllToFront),
        ]));
        list.Add(help);
        return list;
    }

    /// <summary>Win32 menü metni: "&amp;Aç...\tCtrl+O" → ("_Aç...", "Ctrl+O"); "&amp;&amp;" düz "&amp;" olur, "_" korunur.</summary>
    public static (string Text, string? Shortcut) SplitWin32(string raw)
    {
        string? shortcut = null;
        int tab = raw.IndexOf('\t');
        if (tab >= 0) { shortcut = raw[(tab + 1)..].Trim(); raw = raw[..tab]; if (shortcut.Length == 0) shortcut = null; }
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c == '_') sb.Append("__");
            else if (c == '&' && i + 1 < raw.Length && raw[i + 1] == '&') { sb.Append('&'); i++; }
            else if (c == '&') sb.Append('_');
            else sb.Append(c);
        }
        return (sb.ToString(), shortcut);
    }
}

/// <summary>"Ctrl+Shift+N" gibi kısayolları sanal tuş kodlarına çevirir (basma sırasıyla).</summary>
public static class Shortcut
{
    public static ushort[]? VirtualKeys(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var keys = new List<ushort>();
        foreach (var raw in text.Split('+'))
        {
            var t = raw.Trim();
            ushort vk = t.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => 0x11, "SHIFT" => 0x10, "ALT" => 0x12, "WIN" => 0x5B,
                "PLUS" => 0xBB, "MINUS" => 0xBD, "LEFT" => 0x25, "UP" => 0x26, "RIGHT" => 0x27, "DOWN" => 0x28,
                "ESC" => 0x1B, "ENTER" => 0x0D, "TAB" => 0x09, "DEL" or "DELETE" => 0x2E, "SPACE" => 0x20,
                var k when k.Length == 1 && (char.IsAsciiLetterUpper(k[0]) || char.IsAsciiDigit(k[0])) => k[0],
                var k when k.Length is 2 or 3 && k[0] == 'F' && int.TryParse(k[1..], out int f) && f is >= 1 and <= 24 => (ushort)(0x70 + f - 1),
                _ => 0,
            };
            if (vk == 0) return null;
            keys.Add(vk);
        }
        return keys.Count == 0 ? null : keys.ToArray();
    }

    /// <summary>Genişletilmiş tuş (SendInput'ta KEYEVENTF_EXTENDEDKEY gerekir).</summary>
    public static bool Extended(ushort vk) => vk is 0x25 or 0x26 or 0x27 or 0x28 or 0x2E or 0x5B;
}
