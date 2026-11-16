using System.Text.Json;
using Wingry.Core;
using WinPm = Windows.Management.Deployment.PackageManager;

namespace Wingry.Services;

public sealed class BloatApp : Bindable
{
    public required string AppId { get; init; }
    public required string[] PackageNames { get; init; }
    public required string FriendlyName { get; init; }
    public string Description { get; init; } = "";
    public string Recommendation { get; init; } = "optional"; // safe | optional | unsafe
    public bool SelectedByDefault { get; init; }
    public string RemovalMethod { get; init; } = "Appx";      // Appx | WinGet
    public List<string> FullNames { get; } = [];

    public string RecommendationText => Recommendation switch { "safe" => "Güvenle kaldırılabilir", "unsafe" => "Riskli", _ => "İsteğe bağlı" };

    private bool _selected;
    public bool Selected { get => _selected; set => Set(ref _selected, value); }

    private string _status = "";
    public string Status { get => _status; set => Set(ref _status, value); }
}

/// <summary>Win11Debloat Apps.json listesindeki ön yüklü Store uygulamalarını bulur ve kaldırır (geçerli kullanıcı).</summary>
public static class AppxService
{
    public static async Task<List<BloatApp>> GetInstalledBloatAsync() => await Task.Run(() =>
    {
        using var adoc = JsonDocument.Parse(File.ReadAllText(Paths.Debloat("Config/Apps.json")));
        using var tdoc = JsonDocument.Parse(File.ReadAllText(Paths.Debloat("Config/tr-TR/Apps.json")));
        var tr = tdoc.RootElement;

        // Yüklü paketleri bir kez çek: ad -> tam adlar
        var installed = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var pm = new WinPm();
        foreach (var p in pm.FindPackagesForUser(""))
        {
            try
            {
                if (p.IsFramework || p.IsResourcePackage) continue;
                var name = p.Id.Name;
                if (!installed.TryGetValue(name, out var l)) installed[name] = l = [];
                l.Add(p.Id.FullName);
            }
            catch { }
        }

        var list = new List<BloatApp>();
        foreach (var a in adoc.RootElement.GetProperty("Apps").EnumerateArray())
        {
            var idEl = a.GetProperty("AppId");
            string[] names = idEl.ValueKind == JsonValueKind.Array
                ? idEl.EnumerateArray().Select(x => x.GetString()!).ToArray()
                : [idEl.GetString()!];
            var method = a.TryGetProperty("RemovalMethod", out var m) ? m.GetString() ?? "Appx" : "Appx";
            if (method != "Appx") continue; // WinGet ile kaldırılanlar "Uygulamalar > Yüklü" ekranında

            var app = new BloatApp
            {
                AppId = names[0],
                PackageNames = names,
                FriendlyName = tr.TryGetProperty(names[0], out var t) && t.TryGetProperty("FriendlyName", out var fn) ? fn.GetString()! : names[0],
                Description = tr.TryGetProperty(names[0], out var t2) && t2.TryGetProperty("Description", out var d) ? d.GetString()! : "",
                Recommendation = a.TryGetProperty("Recommendation", out var r) ? r.GetString() ?? "optional" : "optional",
                SelectedByDefault = a.TryGetProperty("SelectedByDefault", out var s) && s.ValueKind == JsonValueKind.True,
                RemovalMethod = method,
            };
            foreach (var n in names)
                if (installed.TryGetValue(n, out var fulls)) app.FullNames.AddRange(fulls);
            if (app.FullNames.Count == 0) continue;
            app.Selected = app.SelectedByDefault;
            list.Add(app);
        }
        return list.OrderBy(x => x.Recommendation switch { "safe" => 0, "optional" => 1, _ => 2 })
                   .ThenBy(x => x.FriendlyName, StringComparer.CurrentCulture).ToList();
    });

    /// <summary>Adı verilen paketleri (varsa) kaldırır. Döner: hata metinleri.</summary>
    public static async Task<List<string>> RemoveByNamesAsync(IEnumerable<string> names)
    {
        var set = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pm = new WinPm();
        var fulls = new List<string>();
        foreach (var p in pm.FindPackagesForUser(""))
            try { if (set.Contains(p.Id.Name)) fulls.Add(p.Id.FullName); } catch { }
        var errors = new List<string>();
        foreach (var f in fulls)
        {
            var e = await RemoveAsync(f);
            if (e is not null) errors.Add(e);
        }
        return errors;
    }

    public static async Task<string?> RemoveAsync(string fullName)
    {
        try
        {
            var pm = new WinPm();
            var r = await pm.RemovePackageAsync(fullName);
            if (r.ExtendedErrorCode is not null && r.ExtendedErrorCode.HResult != 0)
                return $"{fullName}: {r.ErrorText}";
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("Appx.Remove " + fullName, ex);
            return $"{fullName}: {ex.Message}";
        }
    }
}
