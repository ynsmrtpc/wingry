using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Wingry.Services;

namespace Wingry.Core;

/// <summary>
/// Görev Zamanlayıcı'nın çağırdığı arayüzsüz işler:
///   --task ram [--deep]          RAM temizliği
///   --task update [--notify]     uygulama güncelleme (ya da yalnızca bildirim)
/// </summary>
public static class HeadlessTasks
{
    public static int Run(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        var job = args.FirstOrDefault() ?? "";
        try
        {
            return job switch
            {
                "ram" => Ram(args.Contains("--deep")),
                "update" => Update(args.Contains("--notify")).GetAwaiter().GetResult(),
                "profil" => ExportProfile(args.ElementAtOrDefault(1)).GetAwaiter().GetResult(),
                _ => 2,
            };
        }
        catch (Exception ex)
        {
            Log.Error("Task " + job, ex);
            return 1;
        }
    }

    private static int Ram(bool deep)
    {
        var r = MemoryService.Clean(deep && Elevation.IsAdmin);
        Log.Write($"Zamanlanmış RAM temizliği: {Format.Bytes(r.FreedBytes)} boşaltıldı, {r.Processes} süreç{(r.Deep ? " (derin)" : "")}");
        if (Settings.GetBool("ram_bildirim", false))
            Notify("RAM temizlendi", $"{Format.Bytes(r.FreedBytes)} bellek boşaltıldı.");
        return 0;
    }

    private static async Task<int> Update(bool notifyOnly)
    {
        var ignored = Settings.GetSet(Settings.IgnoredUpdates);
        var list = (await WingetService.GetInstalledAsync()).Where(p => p.HasUpdate && !ignored.Contains(p.Id)).ToList();
        Log.Write($"Zamanlanmış güncelleme taraması: {list.Count} güncelleme");
        if (list.Count == 0) return 0;

        if (notifyOnly)
        {
            Notify($"{list.Count} uygulama güncellemesi var",
                string.Join(", ", list.Take(4).Select(p => p.Name)) + (list.Count > 4 ? $" ve {list.Count - 4} diğer" : ""));
            return 0;
        }

        int ok = 0; var failed = new List<string>();
        foreach (var p in list)
        {
            var r = await WingetService.RunAsync(OpKind.Upgrade, p, null, CancellationToken.None);
            Log.Write($"  {p.Id}: {r.Message}");
            if (r.Ok) ok++; else failed.Add(p.Name);
        }
        Notify($"{ok} uygulama güncellendi",
            failed.Count == 0 ? "Tüm güncellemeler başarıyla kuruldu." : $"Başarısız: {string.Join(", ", failed.Take(4))}");
        return failed.Count == 0 ? 0 : 1;
    }

    /// <summary>--task profil &lt;dosya.json&gt;: bu bilgisayarın profilini dışa aktarır (betiklerle yedekleme için).</summary>
    private static async Task<int> ExportProfile(string? file)
    {
        if (string.IsNullOrWhiteSpace(file)) return 2;
        var p = await ProfileService.CaptureAsync();
        ProfileService.Save(p, Path.GetFullPath(file));
        Log.Write($"Profil dışa aktarıldı (komut satırı): {file} · {p.Apps.Count} uygulama, {p.Tweaks.Count} ayar");
        return 0;
    }

    public static void Notify(string title, string body)
    {
        try
        {
            var mgr = AppNotificationManager.Default;
            mgr.Register();
            mgr.Show(new AppNotificationBuilder().AddText(title).AddText(body).BuildNotification());
            Thread.Sleep(800);
        }
        catch (Exception ex) { Log.Error("Notify", ex); }
    }
}
