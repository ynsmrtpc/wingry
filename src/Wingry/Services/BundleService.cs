using System.Text.Json;
using System.Text.Json.Nodes;
using Wingry.Core;

namespace Wingry.Services;

public sealed record BundleApp(string Id, string Name);

public enum BundleOrigin { BuiltIn, User, Folder }

public sealed record Bundle(string Id, string Title, string Description, string Glyph, IReadOnlyList<BundleApp> Apps)
{
    public BundleOrigin Origin { get; init; } = BundleOrigin.BuiltIn;
    /// <summary>Kullanıcı / klasör paketinin dosyası.</summary>
    public string? FilePath { get; init; }
}

/// <summary>
/// Hazır paketler üç kaynaktan gelir:
///   1. Yerleşik: Data\paketler.json
///   2. Kullanıcının oluşturdukları: %LOCALAPPDATA%\Wingry\Paketlerim\*.json
///   3. Paylaşılan klasör: Wingry.exe'nin (başlatıcının) yanındaki "Paketler" klasörü — oraya
///      atılan dosyalar kendiliğinden bulunur, klasör izlenir.
/// Dosya biçimi Wingry paketi ya da winget'in kendi "winget export" çıktısı olabilir.
/// </summary>
public static class BundleService
{
    public const string FileFilterHint = "Wingry paketi (.json) ya da winget export dosyası";
    public static string UserDir { get; } = EnsureDir(Path.Combine(Paths.UserDir, "Paketlerim"));

    private static string EnsureDir(string p) { Directory.CreateDirectory(p); return p; }

    /// <summary>İzlenen paylaşılan klasörler (var olanlar).</summary>
    public static IReadOnlyList<string> SharedDirs()
    {
        var list = new List<string>();
        void Add(string? dir) { if (dir is not null && !list.Contains(dir, StringComparer.OrdinalIgnoreCase)) list.Add(dir); }
        if (UpdateService.LauncherPath is { } l) Add(Path.Combine(Path.GetDirectoryName(l)!, "Paketler"));
        Add(Path.Combine(Paths.AppDir, "Paketler"));
        return list;
    }

    /// <summary>Kullanıcıya gösterilecek "paylaşılan klasör" (başlatıcının yanındaki).</summary>
    public static string PrimarySharedDir => SharedDirs()[0];

    public static event Action? Changed;
    private static readonly List<FileSystemWatcher> Watchers = [];
    private static CancellationTokenSource? _debounce;

    public static void StartWatching()
    {
        foreach (var dir in SharedDirs().Append(UserDir))
        {
            if (!Directory.Exists(dir) || Watchers.Any(x => string.Equals(x.Path, dir, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                var w = new FileSystemWatcher(dir, "*.json") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite, EnableRaisingEvents = true };
                w.Created += (_, _) => Raise(); w.Changed += (_, _) => Raise(); w.Deleted += (_, _) => Raise(); w.Renamed += (_, _) => Raise();
                Watchers.Add(w);
            }
            catch (Exception ex) { Log.Error("Paket klasörü izlenemedi " + dir, ex); }
        }
    }

    private static void Raise()
    {
        // Dosya kopyalanırken art arda gelen olayları tek bildirime indir; UI iş parçacığına geç
        _debounce?.Cancel();
        _debounce = new CancellationTokenSource();
        var t = _debounce.Token;
        _ = Task.Delay(500, t).ContinueWith(_ => App.Window?.DispatcherQueue.TryEnqueue(() => Changed?.Invoke()),
            t, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    public static IReadOnlyList<Bundle> LoadAll()
    {
        var list = new List<Bundle>();
        list.AddRange(LoadBuiltIn());
        foreach (var f in SafeFiles(UserDir)) if (TryRead(f, BundleOrigin.User, out var b)) list.Add(b);
        foreach (var dir in SharedDirs())
            foreach (var f in SafeFiles(dir)) if (TryRead(f, BundleOrigin.Folder, out var b)) list.Add(b);
        return list;
    }

    private static IEnumerable<string> SafeFiles(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json").OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToArray() : []; }
        catch { return []; }
    }

    private static IEnumerable<Bundle> LoadBuiltIn()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Paths.Data("paketler.json")));
            return doc.RootElement.EnumerateArray().Select(b => new Bundle(
                b.GetProperty("id").GetString()!, b.GetProperty("title").GetString()!,
                b.GetProperty("description").GetString()!, b.GetProperty("glyph").GetString()!,
                b.GetProperty("apps").EnumerateArray().Select(a => new BundleApp(a.GetProperty("id").GetString()!, a.GetProperty("name").GetString()!)).ToList())).ToList();
        }
        catch (Exception ex) { Log.Error("Yerleşik paketler", ex); return []; }
    }

    /// <summary>Wingry paketi ya da winget export dosyasını okur.</summary>
    public static bool TryRead(string file, BundleOrigin origin, out Bundle bundle)
    {
        bundle = null!;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var r = doc.RootElement;
            var fallbackTitle = Path.GetFileNameWithoutExtension(file);
            List<BundleApp> apps = [];
            string title = fallbackTitle, desc = "", glyph = "";

            if (r.TryGetProperty("Sources", out var sources)) // winget export biçimi
            {
                foreach (var s in sources.EnumerateArray())
                    if (s.TryGetProperty("Packages", out var pk))
                        foreach (var p in pk.EnumerateArray())
                            if (p.TryGetProperty("PackageIdentifier", out var id) && id.GetString() is { Length: > 0 } sid)
                                apps.Add(new BundleApp(sid, sid));
                desc = "winget export dosyasından";
            }
            else
            {
                if (r.TryGetProperty("title", out var t) && t.GetString() is { Length: > 0 } st) title = st;
                if (r.TryGetProperty("description", out var d)) desc = d.GetString() ?? "";
                if (r.TryGetProperty("glyph", out var g) && g.GetString() is { Length: > 0 } sg) glyph = sg;
                foreach (var a in r.GetProperty("apps").EnumerateArray())
                {
                    var id = a.ValueKind == JsonValueKind.String ? a.GetString()! : a.GetProperty("id").GetString()!;
                    var name = a.ValueKind == JsonValueKind.Object && a.TryGetProperty("name", out var n) ? n.GetString() ?? id : id;
                    if (id.Length > 0) apps.Add(new BundleApp(id, name));
                }
            }
            apps = apps.DistinctBy(a => a.Id, StringComparer.OrdinalIgnoreCase).ToList();
            if (apps.Count == 0) return false;
            bundle = new Bundle("dosya:" + file, title, desc, glyph, apps) { Origin = origin, FilePath = file };
            return true;
        }
        catch (Exception ex) { Log.Error("Paket okunamadı " + file, ex); return false; }
    }

    public static string ToJson(string title, string description, IEnumerable<BundleApp> apps)
    {
        var o = new JsonObject
        {
            ["wingry"] = 1,
            ["title"] = title,
            ["description"] = description,
            ["apps"] = new JsonArray(apps.Select(a => (JsonNode)new JsonObject { ["id"] = a.Id, ["name"] = a.Name }).ToArray()),
        };
        return o.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>Kullanıcı paketini kaydeder (yeni ya da var olanın üzerine). Dönüş: dosya yolu.</summary>
    public static string SaveUser(string title, string description, IEnumerable<BundleApp> apps, string? existingFile = null)
    {
        var file = existingFile ?? UniquePath(UserDir, title);
        File.WriteAllText(file, ToJson(title, description, apps));
        Raise();
        return file;
    }

    public static void Export(Bundle b, string target) => File.WriteAllText(target, ToJson(b.Title, b.Description, b.Apps));

    /// <summary>Dışarıdaki bir dosyayı kullanıcı paketlerine kopyalar. Dönüş: okunan paket ya da null.</summary>
    public static Bundle? Import(string file)
    {
        if (!TryRead(file, BundleOrigin.User, out var b)) return null;
        var target = SaveUser(b.Title, b.Description, b.Apps);
        return b with { FilePath = target, Id = "dosya:" + target };
    }

    /// <summary>Kullanıcı paketini siler (dosya Geri Dönüşüm Kutusu'na gider).</summary>
    public static bool DeleteUser(Bundle b)
    {
        if (b.Origin != BundleOrigin.User || b.FilePath is null) return false;
        var ok = DiskService.Recycle([b.FilePath], 0);
        Raise();
        return ok;
    }

    public static string UniquePath(string dir, string title)
    {
        var safe = string.Concat(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        if (safe.Length == 0) safe = "paket";
        var p = Path.Combine(dir, safe + ".json");
        for (int i = 2; File.Exists(p); i++) p = Path.Combine(dir, $"{safe} ({i}).json");
        return p;
    }

    public static string EnsureSharedDir()
    {
        var d = PrimarySharedDir;
        Directory.CreateDirectory(d);
        StartWatching();
        return d;
    }
}
