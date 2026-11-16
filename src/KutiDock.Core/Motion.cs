// Bu dosyadaki hareket parametreleri ve formüller Gravitone'dan uyarlanmıştır:
// https://github.com/lupanostefano/Gravitone (Core/Bouncer.cs, Core/DockLayout.cs, UI/DockWindow.cs)
// Copyright (c) 2026 Stefano Lupano — MIT Lisansı (tam metin: THIRD-PARTY-NOTICES.md).
namespace KutiDock.Core;

public enum BounceKind { None, Launch, Attention }

/// <summary>
/// macOS Dock sıçraması (Gravitone Bouncer). Launch: uygulama penceresini gösterene kadar düzenli sıçrar;
/// Attention: üçlü yüksek sıçrama + ara. Durdurma isteği o anki sıçramanın inmesini bekler.
/// </summary>
public sealed class Bouncer
{
    const double LaunchPeriod = 0.62, LaunchHeight = 0.5, LaunchTimeout = 12;
    const double AttentionPeriod = 0.5, AttentionHeight = 0.65, AttentionPause = 1.1;
    const int AttentionBounces = 3;

    double _start;
    bool _stopping;
    long _stopAfterBounce;

    public BounceKind Kind { get; private set; }
    public bool IsActive => Kind != BounceKind.None;

    public void Start(BounceKind kind, double now)
    {
        if (kind == BounceKind.None) return;
        if (Kind == kind) { _stopping = false; return; }
        Kind = kind; _start = now; _stopping = false;
    }

    public void Stop(double now)
    {
        if (!IsActive || _stopping) return;
        _stopping = true;
        _stopAfterBounce = BounceIndex(now - _start);
    }

    /// <summary>Anlık yükselme, simge boyunun kesri olarak.</summary>
    public double Offset(double now)
    {
        if (!IsActive) return 0;
        double t = now - _start;
        if (Kind == BounceKind.Launch && t > LaunchTimeout && !_stopping) Stop(now);
        bool resting = Kind == BounceKind.Attention && t % Cycle >= AttentionBounces * AttentionPeriod;
        if (_stopping && (resting || BounceIndex(t) > _stopAfterBounce)) { Kind = BounceKind.None; return 0; }
        if (resting) return 0;
        double period = Kind == BounceKind.Launch ? LaunchPeriod : AttentionPeriod;
        double height = Kind == BounceKind.Launch ? LaunchHeight : AttentionHeight;
        double phase = (Kind == BounceKind.Launch ? t : t % Cycle) % period / period;
        return height * 4 * phase * (1 - phase); // parabol: gerçek bir zıplama
    }

    static double Cycle => AttentionBounces * AttentionPeriod + AttentionPause;

    long BounceIndex(double t) => Kind == BounceKind.Launch
        ? (long)(t / LaunchPeriod)
        : (long)(t / Cycle) * AttentionBounces + Math.Min(AttentionBounces - 1, (long)(t % Cycle / AttentionPeriod));
}

/// <summary>macOS büyütmesi (Gravitone DockLayout): imlecin çevresinde kosinüs tümseği ve üstel yaklaşma.</summary>
public static class Magnify
{
    /// <summary>Büyütmenin etki mesafesi: simge boyunun 3,2 katı.</summary>
    public static double Range(double iconSize) => iconSize * 3.2;

    /// <summary>Simgenin ölçeği: 1 (dinlenme) ile maxSize/iconSize (imlecin tam altında) arası.</summary>
    public static double Scale(double distance, double iconSize, double maxSize, double amount)
    {
        double range = Range(iconSize), d = Math.Abs(distance);
        if (d >= range || amount <= 0) return 1;
        return 1 + (maxSize / iconSize - 1) * (1 + Math.Cos(Math.PI * d / range)) / 2 * amount;
    }

    /// <summary>Hedefe üstel yaklaşma; yeterince yakınsa hedefe oturur. Gravitone: büyürken tau 0,045, sönerken 0,075, gizle/göster 0,065.</summary>
    public static double Approach(double current, double target, double dt, double tau)
    {
        double next = current + (target - current) * (1 - Math.Exp(-dt / tau));
        return Math.Abs(target - next) < 0.002 ? target : next;
    }

    public const double TauGrow = 0.045, TauShrink = 0.075, TauShowHide = 0.065;
}
