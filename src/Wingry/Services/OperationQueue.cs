using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Wingry.Core;

namespace Wingry.Services;

public enum OpState { Waiting, Running, Done, Failed }

public sealed class OpItem : Bindable
{
    public required PackageInfo Package { get; init; }
    public required OpKind Kind { get; init; }
    public string Name => Package.Name;
    public string KindText => Kind switch { OpKind.Install => "Kurulum", OpKind.Upgrade => "Güncelleme", _ => "Kaldırma" };
    internal CancellationTokenSource Cts { get; } = new();

    private string _stage = "Sırada";
    public string Stage { get => _stage; set => Set(ref _stage, value); }

    private double _percent;
    public double Percent { get => _percent; set => Set(ref _percent, value); }

    private bool _indeterminate = true;
    public bool Indeterminate { get => _indeterminate; set => Set(ref _indeterminate, value); }

    private OpState _state;
    public OpState State
    {
        get => _state;
        set { if (Set(ref _state, value)) { Raise(nameof(IsActive)); Raise(nameof(StateGlyph)); Raise(nameof(CanCancel)); } }
    }

    public bool IsActive => State is OpState.Waiting or OpState.Running;
    public bool CanCancel => IsActive;
    public string StateGlyph => State switch
    {
        OpState.Done => "",
        OpState.Failed => "",
        OpState.Running => "",
        _ => "",
    };
}

/// <summary>Paket işlemleri sırayla çalışır (winget aynı anda iki kurulumu iyi kaldırmaz).</summary>
public static class OperationQueue
{
    public static ObservableCollection<OpItem> Items { get; } = [];
    private static DispatcherQueue? _ui;
    private static bool _running;

    public static event Action? Changed;            // sıra/sayı değişti
    public static event Action<OpItem>? Completed;  // bir paket bitti

    public static int ActiveCount => Items.Count(i => i.IsActive);

    public static void Init(DispatcherQueue ui) => _ui = ui;

    public static void Enqueue(OpKind kind, IEnumerable<PackageInfo> pkgs)
    {
        foreach (var p in pkgs)
        {
            if (Items.Any(i => i.IsActive && i.Package.Id == p.Id)) continue;
            Items.Insert(0, new OpItem { Package = p, Kind = kind });
        }
        Changed?.Invoke();
        if (!_running) _ = PumpAsync();
    }

    public static void Cancel(OpItem item)
    {
        if (item.State == OpState.Waiting)
        {
            // Yönetici toplu işinde sırası gelmemiş paket: işçiye "atla" de
            if (_batchCancelFile is not null && _batchIds.Contains(item.Package.Id))
                try { File.AppendAllLines(_batchCancelFile, [item.Package.Id]); } catch { }
            item.State = OpState.Failed; item.Stage = "İptal edildi"; Changed?.Invoke();
        }
        else item.Cts.Cancel();
    }
    public static void ClearFinished()
    {
        foreach (var i in Items.Where(i => !i.IsActive).ToList()) Items.Remove(i);
        Changed?.Invoke();
    }

    private static string? _batchCancelFile;
    private static readonly HashSet<string> _batchIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Yönetici değilken paket işlerini tek UAC ile yönetici işçide, sessizce çalıştır.</summary>
    public static bool UseElevatedBatch => !Elevation.IsAdmin && Settings.GetBool(Settings.ElevatedBatch, true);

    private static async Task PumpAsync()
    {
        _running = true;
        bool denied = false;
        try
        {
            while (true)
            {
                if (!denied && (UseElevatedBatch || Elevation.IsAdmin))
                {
                    var batch = Items.Where(i => i.State == OpState.Waiting).Reverse().ToList();
                    if (batch.Count == 0) break;
                    if (await RunBatchAsync(batch)) continue;
                    denied = true;
                    // İzin verilmedi: tek tek, eski yolla (paket başına UAC çıkabilir)
                }

                var next = Items.LastOrDefault(i => i.State == OpState.Waiting);
                if (next is null) break;
                next.State = OpState.Running;
                Changed?.Invoke();

                var progress = new Progress<OpProgress>(p =>
                {
                    next.Stage = p.Stage;
                    next.Indeterminate = p.Indeterminate;
                    if (!p.Indeterminate) next.Percent = p.Percent;
                });
                // Progress<T> UI bağlamını yakalar; COM geri çağrıları arka planda gelir.
                var r = await WingetService.RunAsync(next.Kind, next.Package, progress, next.Cts.Token);
                Finish(next, r.Ok, r.Message + (r.RebootRequired ? " · yeniden başlatma gerekli" : ""));
            }
        }
        finally { _running = false; }
    }

    private static void Finish(OpItem item, bool ok, string message)
    {
        item.State = ok ? OpState.Done : OpState.Failed;
        item.Stage = message;
        item.Indeterminate = false;
        item.Percent = 100;
        Log.Write($"{item.KindText} {item.Package.Id}: {message}");
        WingetService.InvalidateSearch();
        Completed?.Invoke(item);
        Changed?.Invoke();
    }

    /// <summary>Dönüş: işçi başladı mı (false = UAC reddedildi, tek tek yola düş).</summary>
    private static async Task<bool> RunBatchAsync(List<OpItem> batch)
    {
        var byId = batch.GroupBy(i => i.Package.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _batchIds.Clear();
        foreach (var i in batch) { _batchIds.Add(i.Package.Id); i.Stage = Elevation.IsAdmin ? "Sırada" : "Yönetici onayı bekleniyor…"; i.Indeterminate = true; }
        Changed?.Invoke();

        var cmds = batch.Select(i =>
        {
            var action = i.Kind switch { OpKind.Install => "install", OpKind.Upgrade => "upgrade", _ => "uninstall" };
            var src = i.Package.Source is "winget" or "msstore" ? i.Package.Source : "";
            return $"winget|{action}|{i.Package.Id}|{src}";
        }).ToList();

        var w = await Elevation.RunWithProgressAsync(cmds, line =>
        {
            var p = line.Split('|');
            if (p.Length < 2 || !byId.TryGetValue(p[1], out var item) || !item.IsActive) return;
            if (p[0] == "BASLA")
            {
                item.State = OpState.Running;
                item.Stage = item.Kind switch { OpKind.Install => "Sessizce kuruluyor…", OpKind.Upgrade => "Sessizce güncelleniyor…", _ => "Kaldırılıyor…" };
                item.Indeterminate = true;
                foreach (var other in batch.Where(o => o.State == OpState.Waiting)) other.Stage = "Sırada";
                Changed?.Invoke();
            }
            else if (p[0] == "SONUC" && p.Length >= 4)
                Finish(item, p[2] == "ok", p[3]);
        }, f => _batchCancelFile = f);
        _batchCancelFile = null;

        if (!w.Started)
        {
            foreach (var i in batch.Where(i => i.IsActive)) { i.State = OpState.Waiting; i.Stage = "Sırada (izin verilmedi, tek tek denenecek)"; }
            Changed?.Invoke();
            if (w.Error is { } e) Ui.Toast("Yönetici izni verilmedi", "Paketler tek tek kurulacak; kurulum programları ayrıca izin isteyebilir.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning, 7);
            return false;
        }
        // İşçinin hiç dokunmadığı (ör. erken çöküş) kalan işler
        foreach (var i in batch.Where(i => i.IsActive)) Finish(i, false, "Yapılamadı (işçi yanıt vermedi)");
        return true;
    }
}