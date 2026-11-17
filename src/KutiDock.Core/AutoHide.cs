using System;

namespace KutiDock.Core;

/// <summary>
/// 1.6.3: dock'un kenarda saklanması. Fare dock'un monitöründe ilgili kenara (birkaç piksel) gelince belirir; dock'un
/// üstünde ya da açık paneli varken açık kalır; fare uzaklaşınca <c>delay</c> sonra saklanır.
/// </summary>
public sealed class AutoHide
{
    DateTime? _leftAt;
    DateTime _holdUntil = DateTime.MinValue;
    public bool Shown { get; private set; } = true;

    /// <summary>Durum değiştiyse true.</summary>
    public bool Update(bool atEdge, bool overDock, bool keep, DateTime now, TimeSpan delay)
    {
        if (!Shown)
        {
            if (!atEdge) return false;
            Shown = true; _leftAt = null;
            return true;
        }
        if (atEdge || overDock || keep || now < _holdUntil) { _leftAt = null; return false; }
        _leftAt ??= now;
        if (now - _leftAt.Value < delay) return false;
        Shown = false; _leftAt = null;
        return true;
    }

    /// <summary>Hemen sakla (ör. tam ekran başlayınca): sonra yeniden ancak kenara gelince belirir.</summary>
    public void Reset() { Shown = false; _leftAt = null; _holdUntil = DateTime.MinValue; }

    /// <summary>1.6.10: ilk açılış Genie'si için kısa gösterim: <paramref name="until"/> anına kadar açık kalır, sonra her zamanki gecikmeyle saklanır.</summary>
    public void Peek(DateTime until) { Shown = true; _leftAt = null; if (until > _holdUntil) _holdUntil = until; }

    /// <summary>Fare, monitörün dock tarafındaki <paramref name="band"/> piksellik şeridinde mi.</summary>
    public static bool AtEdge(DockEdge edge, int x, int y, Box monitor, int band = 2)
    {
        if (!monitor.Contains(x, y)) return false;
        return edge switch
        {
            DockEdge.Ust => y < monitor.Top + band,
            DockEdge.Sol => x < monitor.Left + band,
            DockEdge.Sag => x >= monitor.Right - band,
            _ => y >= monitor.Bottom - band,
        };
    }
}
