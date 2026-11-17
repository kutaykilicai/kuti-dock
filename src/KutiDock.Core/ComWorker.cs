using System.Collections.Concurrent;

namespace KutiDock.Core;

/// <summary>
/// 1.6.6 (ses kökü): COM işlerini tek bir adanmış iş parçacığında (Windows'ta MTA) sırayla çalıştırır. Arayüz
/// iş parçacığının COM durumundan bağımsızdır; iş hata verirse ya da süre aşılırsa yedek değer döner ve hata bildirilir.
/// </summary>
public sealed class ComWorker
{
    readonly BlockingCollection<Action> _work = new();
    readonly Thread _thread;
    readonly int _timeoutMs;

    public ComWorker(string name, int timeoutMs = 1500)
    {
        _timeoutMs = timeoutMs;
        _thread = new Thread(() => { foreach (var a in _work.GetConsumingEnumerable()) a(); }) { IsBackground = true, Name = name };
        if (OperatingSystem.IsWindows()) _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public bool IsWorkerThread => Thread.CurrentThread == _thread;

    /// <param name="onError">İş hata verdiğinde (iş parçacığında) çağrılır: bozuk COM nesnesini sıfırlamak ve loglamak için.</param>
    public T Run<T>(Func<T> f, T fallback, Action<Exception>? onError = null)
    {
        if (IsWorkerThread) return f();
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Add(() =>
        {
            try { done.SetResult(f()); }
            catch (Exception ex) { try { onError?.Invoke(ex); } catch (Exception) { } done.SetResult(fallback); }
        });
        return done.Task.Wait(_timeoutMs) ? done.Task.Result : fallback;
    }
}
