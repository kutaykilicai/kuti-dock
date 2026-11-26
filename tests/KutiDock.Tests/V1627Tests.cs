using KutiDock.Core;
using Xunit;

/// <summary>
/// 1.6.27: dock kenarda saklanırken büyümüş simgeler (ayrı ZoomLayer penceresi) ekranda takılı kalmaz.
/// </summary>
public class V1627ZoomLayerTests
{
    [Theory]
    [InlineData(true, false, 1.0, true)]    // tam görünür
    [InlineData(true, true, 1.0, false)]    // saklanma kayması başladı
    [InlineData(true, false, 0.5, false)]   // solarken / belirirken
    [InlineData(false, false, 1.0, false)]  // gizli (Hide sonrası)
    [InlineData(false, true, 0.0, false)]
    public void LayerOnlyWhenDockSteady(bool visible, bool hiding, double opacity, bool allowed) =>
        Assert.Equal(allowed, ZoomLayerPolicy.LayerAllowed(visible, hiding, opacity));

    [Theory]
    [InlineData(true, true, false, true, true)]
    [InlineData(true, true, false, false, false)] // dock saklanıyor: IsMouseOver takılı kalsa bile büyütme yok
    [InlineData(true, false, false, true, false)] // ayar kapalı
    [InlineData(true, true, true, true, false)]   // sürükleme
    [InlineData(false, true, false, true, false)]
    public void MagnifyNeedsSteadyDock(bool over, bool setting, bool drag, bool layer, bool expect) =>
        Assert.Equal(expect, ZoomLayerPolicy.Magnify(over, setting, drag, layer));

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, false)]  // çizim başarısız (dock gizlendi): eski resim temizlenmeli (hata buydu)
    [InlineData(true, false, true, false)]  // dock saklanıyor
    [InlineData(false, true, true, false)]  // büyüyen simge yok
    public void StaleLayerIsCleared(bool lifted, bool allowed, bool drawn, bool keep) =>
        Assert.Equal(keep, ZoomLayerPolicy.KeepLayer(lifted, allowed, drawn));
}
