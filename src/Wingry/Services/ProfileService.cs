using System.Text.Json;
using System.Text.Json.Nodes;
using Wingry.Core;

namespace Wingry.Services;

public sealed class WingryProfile
{
    public string Machine { get; init; } = "";
    public string Os { get; init; } = "";
    public DateTime Created { get; init; }
    public List<BundleApp> Apps { get; init; } = [];
    public List<string> Tweaks { get; init; } = [];
    public List<string> StartupOff { get; init; } = [];   // StartupApproved değer adları
}

/// <summary>
/// "Bu bilgisayarın profili": winget/Store kaynaklı uygulamalar, uygulanmış ince ayarlar,
/// açılışta başlaması kapatılmış programlar. Yeni bilgisayarda içe aktarınca yalnızca eksikler uygulanır.
/// </summary>
public static class ProfileService
{
    public static async Task<WingryProfile> CaptureAsync()
    {
        // Arayüzsüz kipte (--task profil) AppState yok: listeyi doğrudan winget'ten al
        IReadOnlyList<PackageInfo> installed = AppState.Installed.Count > 0 ? AppState.Installed : await WingetService.GetInstalledAsync();
        var apps = installed.Where(p => p.Source is "winget" or "msstore")
            .Select(p => new BundleApp(p.Id, p.Name)).DistinctBy(a => a.Id).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var (tweaks, startup) = await Task.Run(() =>
        {
            TweakCatalog.Load();
            var t = TweakCatalog.Features.Values
                .Where(f => TweakCatalog.IsSupported(f) && f.Id != "ClearStart" && TweakService.IsApplied(f))
                .Select(f => f.Id).ToList();
            var s = StartupService.List().Where(i => !i.Enabled).Select(i => i.ApprovedValue).Distinct().ToList();
            return (t, s);
        });
        return new WingryProfile
        {
            Machine = Environment.MachineName, Os = SystemInfo.OsName, Created = DateTime.Now,
            Apps = apps, Tweaks = tweaks, StartupOff = startup,
        };
    }

    public static void Save(WingryProfile p, string file)
    {
        var o = new JsonObject
        {
            ["wingryProfil"] = 1,
            ["makine"] = p.Machine,
            ["windows"] = p.Os,
            ["tarih"] = p.Created.ToString("O"),
            ["uygulamalar"] = new JsonArray(p.Apps.Select(a => (JsonNode)new JsonObject { ["id"] = a.Id, ["name"] = a.Name }).ToArray()),
            ["inceAyarlar"] = new JsonArray(p.Tweaks.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray()),
            ["baslangicKapali"] = new JsonArray(p.StartupOff.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray()),
        };
        File.WriteAllText(file, o.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    public static WingryProfile? Load(string file)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var r = doc.RootElement;
            if (!r.TryGetProperty("wingryProfil", out _)) return null;
            List<string> Strs(string n) => r.TryGetProperty(n, out var a) ? a.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList() : [];
            return new WingryProfile
            {
                Machine = r.TryGetProperty("makine", out var m) ? m.GetString() ?? "" : "",
                Os = r.TryGetProperty("windows", out var w) ? w.GetString() ?? "" : "",
                Created = r.TryGetProperty("tarih", out var t) && t.TryGetDateTime(out var d) ? d : default,
                Apps = r.TryGetProperty("uygulamalar", out var ap)
                    ? ap.EnumerateArray().Select(x => new BundleApp(x.GetProperty("id").GetString()!, x.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "")).ToList() : [],
                Tweaks = Strs("inceAyarlar"),
                StartupOff = Strs("baslangicKapali"),
            };
        }
        catch (Exception ex) { Log.Error("Profil okunamadı", ex); return null; }
    }

    public sealed record Plan(List<BundleApp> MissingApps, List<TweakFeature> Tweaks, List<StartupItem> Startup);

    /// <summary>Bu bilgisayarda eksik olanları hesaplar.</summary>
    public static Task<Plan> PlanAsync(WingryProfile p) => Task.Run(() =>
    {
        TweakCatalog.Load();
        var installed = AppState.Installed.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var apps = p.Apps.Where(a => !installed.Contains(a.Id)).ToList();
        var tweaks = p.Tweaks.Select(id => TweakCatalog.Features.GetValueOrDefault(id)).OfType<TweakFeature>()
            .Where(f => TweakCatalog.IsSupported(f) && !TweakService.IsApplied(f)).ToList();
        var off = p.StartupOff.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var startup = StartupService.List().Where(i => i.Enabled && off.Contains(i.ApprovedValue)).ToList();
        return new Plan(apps, tweaks, startup);
    });
}
