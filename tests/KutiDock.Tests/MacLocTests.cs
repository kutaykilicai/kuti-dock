// Gravitone LocTests'ten (MIT, © 2026 Stefano Lupano) uyarlandı: İtalyanca yerine Türkçe.
using System.Reflection;
using System.Text.RegularExpressions;
using KutiDock.Mac.Core;
using Xunit;

namespace KutiDock.Tests.Mac;

[Collection("Loc")]
public class MacLocTests
{
    static Dictionary<string, string> Turkish() =>
        (Dictionary<string, string>)typeof(Loc).GetField("Tr", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    [Fact]
    public void English_returns_the_source_text()
    {
        Loc.Init("en");
        try { Assert.Equal("Keep in Dock", Loc.T("Keep in Dock")); }
        finally { Loc.Init("tr"); }
    }

    [Fact]
    public void Turkish_translates_and_formats()
    {
        Loc.Init("tr");
        Assert.Equal("Dock'ta tut", Loc.T("Keep in Dock"));
        Assert.Equal("Kuti Dock'tan çık", Loc.T("Quit {0}", "Kuti Dock"));
        Assert.Equal("Something new", Loc.T("Something new"));
        Loc.Init(""); // Windows dili İngilizce değilse Türkçe
    }

    [Fact]
    public void Every_translation_keeps_the_placeholders_of_its_source()
    {
        var placeholder = new Regex(@"\{\d+\}");
        foreach (var (english, turkish) in Turkish())
        {
            var expected = placeholder.Matches(english).Select(m => m.Value).Order();
            var actual = placeholder.Matches(turkish).Select(m => m.Value).Order();
            Assert.True(expected.SequenceEqual(actual), $"\"{turkish}\" / \"{english}\"");
            Assert.False(string.IsNullOrWhiteSpace(turkish));
        }
    }

    [Fact]
    public void Languages_are_system_English_Turkish() => Assert.Equal(["", "en", "tr"], Loc.Languages);
}
