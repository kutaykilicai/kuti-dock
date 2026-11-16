// Gravitone kaynak kodu (https://github.com/lupanostefano/Gravitone), Copyright (c) 2026 Stefano Lupano, MIT Lisansı.
// Kuti Dock için uyarlandı (ad, veri klasörü, Türkçe arayüz, Kuti widget entegrasyonu). Lisans: LICENSE-Gravitone.txt
namespace KutiDock.Mac.Core;

/// <summary>Builds the first-run dock from the apps actually installed on this PC.</summary>
internal static class DefaultApps
{
    // Store / packaged apps by AUMID, desktop apps by Start-menu shortcut name.
    static readonly (string? Aumid, string? Shortcut)[] Candidates =
    [
        ("Microsoft.Windows.Explorer", null),
        ("MSEdge", "Microsoft Edge"),
        (null, "Google Chrome"),
        (null, "Firefox"),
        ("Microsoft.WindowsStore_8wekyb3d8bbwe!App", null),
        ("Microsoft.Windows.Photos_8wekyb3d8bbwe!App", null),
        (null, "Spotify"),
        (null, "Visual Studio Code"),
        ("Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", null),
        ("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", null),
        ("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", null),
        ("windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel", null),
    ];

    public static List<DockItemConfig> Discover()
    {
        var result = new List<DockItemConfig> { StartMenu.CreateConfig() };
        // Kuti Dock: ilk açılışta Windows görev çubuğuna sabitlenmiş uygulamalar (Kutay Dock'taki sabitlenenler) alınır
        try
        {
            var pinned = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
            if (Directory.Exists(pinned))
                foreach (var lnk in Directory.GetFiles(pinned, "*.lnk").OrderBy(File.GetCreationTimeUtc))
                    result.Add(new DockItemConfig { Name = Path.GetFileNameWithoutExtension(lnk), Target = lnk });
        }
        catch (Exception) { }
        if (result.Count > 1) return result;
        foreach (var (aumid, shortcut) in Candidates)
        {
            var item = FromAumid(aumid) ?? FromShortcut(shortcut);
            if (item is not null) result.Add(item);
        }

        if (result.Count == 1)
        {
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            result.Add(new DockItemConfig { Name = Loc.T("File Explorer"), Target = explorer });
        }
        return result;
    }

    static DockItemConfig? FromAumid(string? aumid)
    {
        if (aumid is null) return null;
        var target = ShellItems.AppsFolder + aumid;
        var name = ShellItems.DisplayName(target);
        return string.IsNullOrWhiteSpace(name) ? null : new DockItemConfig { Name = name, Target = target };
    }

    static DockItemConfig? FromShortcut(string? name)
    {
        if (name is null) return null;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                     Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                 })
        {
            if (!Directory.Exists(root)) continue;
            var lnk = Directory.EnumerateFiles(root, name + ".lnk", options).FirstOrDefault();
            if (lnk is not null) return new DockItemConfig { Name = name, Target = lnk };
        }
        return null;
    }
}
