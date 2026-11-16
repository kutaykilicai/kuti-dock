// Kuti Dock 1.6.1: Mac dock ile widget şeritleri tek birleşik şeritte asla çakışmamalı.
using KutiDock.Mac.Core;
using Xunit;

namespace KutiDock.Tests.Mac;

public class MacStripFitTests
{
    static void AssertNoOverlap(double start, double end, double need, double wantL, double wantR, double gap)
    {
        var (l, r) = StripFit.Sides(end - start, need, wantL, wantR, gap);
        var (a, b) = StripFit.DockRange(start, end, l, r, gap);
        Assert.True(l >= 0 && r >= 0);
        Assert.True(l <= wantL && r <= wantR);
        if (l > 0) Assert.True(start + l + gap <= a + 0.001, "sol şerit Mac dock'a biniyor");
        if (r > 0) Assert.True(b + gap <= end - r + 0.001, "sağ şerit Mac dock'a biniyor");
        Assert.True(b - a >= Math.Min(need, end - start) - 0.001, "Mac dock'a ihtiyacı kadar yer kalmadı");
    }

    [Fact]
    public void WideScreenKeepsFullWidgets()
    {
        // Kasa: 2560 px, ölçek 1,0; Başlat+Ara ~120 px, sağ widget'lar ~1250 px, Mac dock ~900 px
        var (l, r) = StripFit.Sides(2560, 900, 120, 1250, 12);
        Assert.Equal((120.0, 1250.0), (l, r));
        var (a, b) = StripFit.DockRange(0, 2560, l, r, 12);
        Assert.Equal(132, a); Assert.Equal(2560 - 1262, b);
        AssertNoOverlap(0, 2560, 900, 120, 1250, 12);
    }

    [Fact]
    public void NarrowScreenShrinksWidgetsInsteadOfOverlapping()
    {
        var (l, r) = StripFit.Sides(1920, 900, 120, 1250, 12);
        Assert.True(l + r <= 1920 - 900 - 24);
        Assert.True(l < 120 && r < 1250 && l > 0 && r > 0);
        AssertNoOverlap(0, 1920, 900, 120, 1250, 12);
        AssertNoOverlap(-1920, 0, 900, 120, 1250, 12); // birincilin solundaki ikinci monitör
    }

    [Theory]
    [InlineData(1280, 1400, 100, 900)]   // Mac dock ekrandan bile geniş: widget'lara yer yok
    [InlineData(3840, 600, 0, 1500)]     // solda şerit yok
    [InlineData(1366, 700, 150, 0)]      // sağda şerit yok
    [InlineData(1920, 0, 0, 0)]
    public void NeverOverlaps(double width, double need, double wantL, double wantR)
    {
        AssertNoOverlap(0, width, need, wantL, wantR, 12);
        AssertNoOverlap(100, 100 + width, need * 1.5, wantL * 1.5, wantR * 1.5, 18); // %150 ölçek
    }

    [Fact]
    public void NoRoomMeansNoSideStrips()
    {
        Assert.Equal((0.0, 0.0), StripFit.Sides(1280, 1400, 100, 900, 12));
        Assert.Equal((0.0, 0.0), StripFit.DockRange(0, 1280, 0, 0, 12) == (0, 1280) ? (0.0, 0.0) : (1, 1));
    }
}
