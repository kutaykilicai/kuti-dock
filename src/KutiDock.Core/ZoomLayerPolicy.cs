namespace KutiDock.Core;

/// <summary>
/// 1.6.27: klasik dock'un büyüyen simgeleri ayrı, en üstte duran şeffaf bir pencereye (ZoomLayer) çizilir. Dock kenarda
/// saklanırken bu katman dock ile birlikte kaymıyor/solmuyordu; dock gizlendiğinde çizim başarısız olunca da temizlenmiyordu,
/// büyümüş simgeler ekranda takılı kalıyordu. Kural: katman yalnızca dock tam görünürken (kayma/solma yokken) kullanılır,
/// bir karede çizilmezse hemen temizlenir; dock saklanmaya başlayınca büyütme anında sıfırlanır.
/// </summary>
public static class ZoomLayerPolicy
{
    /// <summary>Dock görünür, saklanma animasyonunda değil ve tam opak mı (katman ancak o zaman dock'un üstüne çizilebilir).</summary>
    public static bool LayerAllowed(bool dockVisible, bool dockHiding, double dockOpacity) =>
        dockVisible && !dockHiding && dockOpacity >= 0.98;

    /// <summary>Büyütme: fare üstte, ayar açık, sürükleme yok ve katman kullanılabilir.</summary>
    public static bool Magnify(bool mouseOver, bool setting, bool dragging, bool layerAllowed) =>
        mouseOver && setting && !dragging && layerAllowed;

    /// <summary>Katman bu karede ekranda kalabilir mi: yalnızca gerçekten çizildiyse. Aksi hâlde eski resim temizlenir.</summary>
    public static bool KeepLayer(bool lifted, bool layerAllowed, bool drawn) => lifted && layerAllowed && drawn;
}
