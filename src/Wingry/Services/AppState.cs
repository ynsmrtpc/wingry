using Wingry.Core;

namespace Wingry.Services;

/// <summary>Sayfalar arası paylaşılan yüklü paket önbelleği. Sayfa her açıldığında winget'e gidilmez.</summary>
public static class AppState
{
    private static Task? _refresh;
    private static readonly object Gate = new();

    public static IReadOnlyList<PackageInfo> Installed { get; private set; } = [];
    public static bool Loading { get; private set; }
    public static string? Error { get; private set; }
    public static DateTime LoadedAt { get; private set; }

    public static int UpdateCount
    {
        get
        {
            var ign = Settings.GetSet(Settings.IgnoredUpdates);
            return Installed.Count(p => p.HasUpdate && !ign.Contains(p.Id));
        }
    }

    /// <summary>UI iş parçacığında tetiklenir (SynchronizationContext ile).</summary>
    public static event Action? Changed;

    public static void NotifyChanged() => Changed?.Invoke();

    public static Task RefreshInstalledAsync()
    {
        lock (Gate)
        {
            if (_refresh is { IsCompleted: false }) return _refresh;
            return _refresh = RefreshCore();
        }
    }

    private static async Task RefreshCore()
    {
        Loading = true; Changed?.Invoke();
        try
        {
            Installed = await WingetService.GetInstalledAsync();
            Error = null;
            LoadedAt = DateTime.Now;
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Loading = false; Changed?.Invoke(); }
    }

    static AppState()
    {
        // Paket işlemleri bitince listeyi tazele (art arda bitenleri tek tazelemede topla).
        CancellationTokenSource? debounce = null;
        OperationQueue.Completed += item =>
        {
            debounce?.Cancel();
            debounce = new CancellationTokenSource();
            var t = debounce.Token;
            Task.Delay(1200, t).ContinueWith(prev => { if (!t.IsCancellationRequested) RefreshInstalledAsync(); },
                t, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.FromCurrentSynchronizationContext());
        };
    }
}
