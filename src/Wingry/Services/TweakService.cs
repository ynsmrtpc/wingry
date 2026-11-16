using System.Diagnostics;
using Wingry.Core;

namespace Wingry.Services;

public sealed record TweakChange(TweakFeature Feature, bool Apply);

public sealed class TweakResult
{
    public List<string> Errors { get; } = [];
    public bool RebootRequired { get; set; }
    public bool ExplorerRestart { get; set; }
    public string? BackupFile { get; set; }
    public int Applied { get; set; }
}

/// <summary>
/// İnce ayarları uygular / geri alır. HKCU bu süreçte, yönetici isteyen her şey tek UAC ile
/// işçide yapılır. Değişmeden önce etkilenen değerler .reg olarak yedeklenir.
/// </summary>
public static class TweakService
{
    private static readonly string[] WidgetPackages = ["Microsoft.StartExperiencesApp", "MicrosoftWindows.Client.WebExperience", "Microsoft.WidgetsPlatformRuntime"];

    private static readonly Dictionary<string, string[]> OptionalFeatureMap = new()
    {
        ["EnableWindowsSandbox"] = ["Containers-DisposableClientVM"],
        ["EnableWindowsSubsystemForLinux"] = ["VirtualMachinePlatform", "Microsoft-Windows-Subsystem-Linux"],
    };

    private static readonly HashSet<string> ExplorerCategories = ["Taskbar", "File Explorer", "Start Menu & Search", "Appearance", "Multi-tasking"];

    public static bool HasCustomLogic(string id) =>
        id is "DisableWidgets" or "ClearStart" || OptionalFeatureMap.ContainsKey(id);

    /// <summary>Özellik şu an uygulanmış mı? (kayıt defteri / paket / isteğe bağlı özellik durumuna bakar)</summary>
    public static bool IsApplied(TweakFeature f)
    {
        try
        {
            if (OptionalFeatureMap.TryGetValue(f.Id, out var feats)) return feats.All(OptionalFeatureEnabled);
            if (f.Id == "DisableWidgets") return !AnyAppxInstalled(WidgetPackages);
            if (f.Id == "ClearStart") return false; // tek seferlik işlem
            return f.ApplyPath is { } p && File.Exists(p) && RegFile.Matches(RegFile.Load(p));
        }
        catch (Exception ex) { Log.Error("IsApplied " + f.Id, ex); return false; }
    }

    public static bool CanUndo(TweakFeature f) =>
        !f.DisableWhenApplied && (f.RegistryUndoKey is not null || OptionalFeatureMap.ContainsKey(f.Id));

    private static bool AnyAppxInstalled(string[] names)
    {
        var pm = new Windows.Management.Deployment.PackageManager();
        foreach (var n in names)
            if (pm.FindPackagesForUser("").Any(p => string.Equals(p.Id.Name, n, StringComparison.OrdinalIgnoreCase))) return true;
        return false;
    }

    private static readonly Dictionary<string, bool> FeatureStateCache = new();

    private static bool OptionalFeatureEnabled(string name)
    {
        lock (FeatureStateCache)
            if (FeatureStateCache.TryGetValue(name, out var c)) return c;
        // WMI (Win32_OptionalFeature) yönetici istemeden okunabilir. System.Management yerine
        // PowerShell çağrılır: uygulama kırpılabilir (trim) kalır, bu sorgu yalnızca bu sayfada çalışır.
        bool on = false;
        try
        {
            var (code, o) = Core.ElevatedWorker.RunTool("powershell.exe",
                $"-NoProfile -NonInteractive -Command \"(Get-CimInstance Win32_OptionalFeature -Filter \\\"Name='{name}'\\\").InstallState\"", 20_000);
            on = code == 0 && o.Trim() == "1";
        }
        catch (Exception ex) { Log.Error("OptionalFeature " + name, ex); }
        lock (FeatureStateCache) FeatureStateCache[name] = on;
        return on;
    }
    public static async Task<TweakResult> ApplyAsync(IReadOnlyList<TweakChange> changes, bool createRestorePoint = false)
    {
        var result = new TweakResult();
        if (changes.Count == 0 && !createRestorePoint) return result;

        // 1) Kayıt dosyalarını topla
        var regWork = new List<(TweakChange c, string path, IReadOnlyList<RegOp> ops)>();
        foreach (var c in changes)
        {
            var path = c.Apply ? c.Feature.ApplyPath : c.Feature.UndoPath;
            if (path is null || !File.Exists(path)) continue;
            regWork.Add((c, path, RegFile.Load(path)));
        }

        // 2) Yedek (hem HKCU hem HKLM değerlerinin şimdiki hâli)
        if (regWork.Count > 0)
            result.BackupFile = RegFile.Backup(regWork.SelectMany(w => w.ops),
                string.Join(", ", changes.Select(c => (c.Apply ? "+" : "-") + c.Feature.Id)));

        // 3) Yönetici komutları
        var admin = new List<string>();
        if (createRestorePoint) admin.Add("restorepoint");
        foreach (var (_, path, ops) in regWork)
            if (ops.Any(o => o.NeedsAdmin)) admin.Add("reg|" + path);
        foreach (var c in changes)
        {
            if (c.Feature.Id == "DisableTelemetry") admin.Add(c.Apply ? "tasks|disable" : "tasks|enable");
            if (OptionalFeatureMap.TryGetValue(c.Feature.Id, out var feats))
                foreach (var f in c.Apply ? feats : feats.Reverse())
                    admin.Add($"dism|{(c.Apply ? "enable" : "disable")}|{f}");
        }

        // Geri yükleme noktası değişiklikten ÖNCE alınmalı → işçi önce çalışır, HKCU sonra.
        if (admin.Count > 0)
        {
            var w = await Elevation.RunAsync(admin);
            // İzin verilmezse yalnızca kullanıcı (HKCU) kısmı uygulanır; hata olarak bildirilir.
            if (!w.Started) result.Errors.Add((w.Error ?? "Yönetici işlemi yapılamadı.") + " Sistem genelindeki ayarlar uygulanmadı.");
            result.Errors.AddRange(w.Lines.Where(l => l.StartsWith("HATA", StringComparison.Ordinal)));
            lock (FeatureStateCache) FeatureStateCache.Clear();
        }

        // 4) HKCU bu süreçte
        foreach (var (c, _, ops) in regWork)
        {
            result.Errors.AddRange(RegFile.Apply(ops, o => !o.NeedsAdmin));
            result.Applied++;
            if (c.Feature.RequiresReboot) result.RebootRequired = true;
            if (ExplorerCategories.Contains(c.Feature.Category)) result.ExplorerRestart = true;
        }

        // 5) Kayıt dışı yan işler (paket kaldırma, Başlat sabitlemeleri)
        foreach (var c in changes.Where(c => c.Apply))
        {
            switch (c.Feature.Id)
            {
                case "DisableBing":
                    result.Errors.AddRange(await AppxService.RemoveByNamesAsync(["Microsoft.BingSearch"])); break;
                case "DisableCopilot":
                    result.Errors.AddRange(await AppxService.RemoveByNamesAsync(["Microsoft.Copilot"])); break;
                case "DisableWidgets":
                    foreach (var p in Process.GetProcessesByName("Widgets").Concat(Process.GetProcessesByName("WidgetService")))
                        try { p.Kill(); } catch { }
                    result.Errors.AddRange(await AppxService.RemoveByNamesAsync(WidgetPackages));
                    result.Applied++; break;
                case "ClearStart":
                    if (ClearStartPins() is { } err) result.Errors.Add(err);
                    result.Applied++; break;
            }
            if (OptionalFeatureMap.ContainsKey(c.Feature.Id)) { result.RebootRequired = true; result.Applied++; }
        }
        foreach (var c in changes.Where(c => !c.Apply && OptionalFeatureMap.ContainsKey(c.Feature.Id)))
        { result.RebootRequired = true; result.Applied++; }

        Log.Write($"İnce ayar: {changes.Count} değişiklik, {result.Errors.Count} hata, yedek={result.BackupFile}");
        foreach (var e in result.Errors) Log.Write("  " + e);
        return result;
    }

    private static string? ClearStartPins()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages", "Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy", "LocalState");
            if (!Directory.Exists(dir)) return "Başlat menüsü klasörü bulunamadı.";
            var target = Path.Combine(dir, "start2.bin");
            if (File.Exists(target)) File.Copy(target, Path.Combine(Paths.Backups, $"start2-{DateTime.Now:yyyyMMdd-HHmmss}.bin"), true);
            File.Copy(Paths.Debloat("Start/start2.bin"), target, true);
            foreach (var p in Process.GetProcessesByName("StartMenuExperienceHost")) try { p.Kill(); } catch { }
            return null;
        }
        catch (Exception ex) { return "Başlat sabitlemeleri temizlenemedi: " + ex.Message; }
    }

    /// <summary>Kayıtlı bir .reg yedeğini geri yükler.</summary>
    public static async Task<List<string>> RestoreBackupAsync(string file)
    {
        var ops = RegFile.Parse(File.ReadAllText(file));
        var errors = RegFile.Apply(ops, o => !o.NeedsAdmin);
        if (ops.Any(o => o.NeedsAdmin))
        {
            var w = await Elevation.RunAsync(["reg|" + file]);
            if (!w.Started) errors.Add(w.Error ?? "Yönetici izni yok");
            errors.AddRange(w.Lines.Where(l => l.StartsWith("HATA", StringComparison.Ordinal)));
        }
        return errors;
    }

    public static void RestartExplorer()
    {
        foreach (var p in Process.GetProcessesByName("explorer")) { try { p.Kill(); } catch { } finally { p.Dispose(); } }
        Task.Delay(1500).ContinueWith(_ =>
        {
            if (Process.GetProcessesByName("explorer").Length == 0)
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        });
    }
}
