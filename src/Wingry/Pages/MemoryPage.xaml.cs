using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wingry.Core;
using Wingry.Services;

namespace Wingry.Pages;

public sealed class ProcRow
{
    public required string Name { get; init; }
    public required string Size { get; init; }
    public double Percent { get; init; }
}

public sealed partial class MemoryPage : Page
{
    private static readonly int[] Thresholds = [70, 75, 80, 85, 90, 95];
    private DispatcherQueueTimer? _timer;
    private int _tick;
    private bool _init;

    public MemoryPage()
    {
        InitializeComponent();
        int t = Settings.GetInt(Settings.AutoCleanPercent, 0);
        AutoToggle.IsOn = t > 0;
        Threshold.SelectedIndex = Math.Max(0, Array.IndexOf(Thresholds, t > 0 ? t : 85));
        _init = true;
        Loaded += (_, _) =>
        {
            _timer ??= DispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick -= OnTick; _timer.Tick += OnTick;
            _timer.Start();
            OnTick(null, null);
            _ = RefreshProcsAsync();
        };
        Unloaded += (_, _) => _timer?.Stop();
    }

    private void OnTick(DispatcherQueueTimer? t, object? _)
    {
        var m = MemoryService.Snapshot();
        Ring.Value = m.LoadPercent;
        RingValue.Text = Format.Percent(m.LoadPercent);
        VTotal.Text = Format.Bytes(m.Total);
        VUsed.Text = Format.Bytes(m.Used);
        VAvail.Text = Format.Bytes(m.Available);
        VStandby.Text = m.Standby > 0 ? Format.Bytes(m.Standby) : "yönetici gerekli";
        VModified.Text = m.Modified > 0 ? Format.Bytes(m.Modified) : "—";
        VCommit.Text = $"{Format.Bytes(m.CommitTotal)} / {Format.Bytes(m.CommitLimit)}";
        Spark.Push(m.LoadPercent);
        UpdateBreakdown(m);
        if (++_tick % 5 == 0) _ = RefreshProcsAsync();
    }

    private void UpdateBreakdown(MemorySnapshot m)
    {
        // Bekleme listesi okunamıyorsa (ayrıcalık yok) "kullanılabilir"i tek parça göster
        ulong used = m.Used;
        ulong cache = m.Standby > 0 ? Math.Min(m.Standby, m.Available) : 0;
        ulong free = m.Available - cache;
        ColUsed.Width = new GridLength(Math.Max(used, 1), GridUnitType.Star);
        ColCache.Width = new GridLength(cache, GridUnitType.Star);
        ColFree.Width = new GridLength(free, GridUnitType.Star);
        BUsed.Text = $"{Format.Bytes(used)}  ·  %{100.0 * used / Math.Max(1, m.Total):0}";
        BCache.Text = m.Standby > 0 ? $"{Format.Bytes(cache)}  ·  %{100.0 * cache / Math.Max(1, m.Total):0}" : "okunamadı (yönetici gerekli)";
        BFree.Text = $"{Format.Bytes(free)}  ·  %{100.0 * free / Math.Max(1, m.Total):0}";
    }

    private async Task RefreshProcsAsync()
    {
        var list = await Task.Run(() => MemoryService.TopProcesses(12));
        long max = list.Count > 0 ? list[0].WorkingSet : 1;
        Procs.ItemsSource = list.Select(p => new ProcRow
        {
            Name = p.Count > 1 ? $"{p.Name} ({p.Count})" : p.Name,
            Size = Format.Bytes(p.WorkingSet),
            Percent = 100.0 * p.WorkingSet / Math.Max(1, max),
        }).ToList();
    }

    private async void OnQuick(object sender, RoutedEventArgs e) => await CleanAsync(false);

    private async void OnDeep(object sender, RoutedEventArgs e) => await CleanAsync(true);

    private async Task CleanAsync(bool deep)
    {
        QuickBtn.IsEnabled = DeepBtn.IsEnabled = false; CleanRing.IsActive = true;
        LastResult.Text = deep ? "Derin temizlik yapılıyor…" : "Temizleniyor…";
        try
        {
            CleanResult r;
            if (deep && !Elevation.IsAdmin)
            {
                var before = MemoryService.Snapshot().Available;
                var w = await Elevation.RunAsync(["ramdeep"]);
                if (!w.Started) { LastResult.Text = w.Error ?? "İzin verilmedi."; return; }
                var after = MemoryService.Snapshot().Available;
                r = new CleanResult(Math.Max(0, (long)after - (long)before), 0, true, w.Failures > 0 ? string.Join(" ", w.Lines) : null);
            }
            else r = await Task.Run(() => MemoryService.Clean(deep));

            LastResult.Text = $"{DateTime.Now:HH:mm} · {Format.Bytes(r.FreedBytes)} boşaltıldı" + (r.Processes > 0 ? $" · {r.Processes} süreç" : "") + (r.Deep ? " · derin" : "");
            if (r.Error is not null) Ui.Error("Temizlik kısmen yapıldı", r.Error);
            else Ui.Toast("Bellek temizlendi", $"{Format.Bytes(r.FreedBytes)} boşaltıldı.");
            await RefreshProcsAsync();
        }
        finally { QuickBtn.IsEnabled = DeepBtn.IsEnabled = true; CleanRing.IsActive = false; }
    }

    private void OnThreshold(object sender, SelectionChangedEventArgs e)
    {
        if (!_init || !AutoToggle.IsOn) return;
        Settings.Set(Settings.AutoCleanPercent, Thresholds[Math.Max(0, Threshold.SelectedIndex)]);
        AutoCleaner.Restart();
    }

    private void OnAutoToggle(object sender, RoutedEventArgs e)
    {
        if (!_init) return;
        Settings.Set(Settings.AutoCleanPercent, AutoToggle.IsOn ? Thresholds[Math.Max(0, Threshold.SelectedIndex)] : 0);
        AutoCleaner.Restart();
    }

    private void OnSchedule(object sender, RoutedEventArgs e) => App.Window?.Navigate("scheduler");
}

/// <summary>Uygulama açıkken eşik aşılınca hızlı temizlik yapar.</summary>
public static class AutoCleaner
{
    private static DispatcherQueueTimer? _timer;
    private static DateTime _last = DateTime.MinValue;

    public static void Restart()
    {
        _timer?.Stop();
        int t = Settings.GetInt(Settings.AutoCleanPercent, 0);
        if (t <= 0) return;
        _timer ??= DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(10);
        _timer.Tick -= OnTick; _timer.Tick += OnTick;
        _timer.Start();
    }

    private static async void OnTick(DispatcherQueueTimer s, object e)
    {
        int t = Settings.GetInt(Settings.AutoCleanPercent, 0);
        if (t <= 0 || DateTime.Now - _last < TimeSpan.FromMinutes(5)) return;
        if (MemoryService.Snapshot().LoadPercent < t) return;
        _last = DateTime.Now;
        var r = await Task.Run(() => MemoryService.Clean(Elevation.IsAdmin));
        Log.Write($"Otomatik RAM temizliği (eşik %{t}): {Format.Bytes(r.FreedBytes)}");
        Ui.Toast("Otomatik bellek temizliği", $"Kullanım %{t} eşiğini aştı · {Format.Bytes(r.FreedBytes)} boşaltıldı.", InfoBarSeverity.Informational);
    }
}
