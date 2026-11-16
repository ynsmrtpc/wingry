using System.Text.Json;
using Wingry.Core;

namespace Wingry.Services;

public sealed class TweakCategory
{
    public required string Id { get; init; }        // PrivacySuggestedContent
    public required string Name { get; init; }      // "Privacy & Suggested Content" (Features.json'daki anahtar)
    public required string Label { get; init; }     // Türkçe
    public required string Glyph { get; init; }
    public string Description { get; init; } = "";
}

public sealed class TweakFeature
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public int Priority { get; init; } = 100;
    public string? RegistryKey { get; init; }
    public string? RegistryUndoKey { get; init; }
    public int? MinVersion { get; init; }
    public int? MaxVersion { get; init; }
    public bool RequiresReboot { get; init; }
    public bool DisableWhenApplied { get; init; }
    public string Label { get; init; } = "";
    public string ToolTip { get; init; } = "";
    public string UndoLabel { get; init; } = "";

    public string? ApplyPath => RegistryKey is null ? null : Paths.Debloat(Path.Combine("Regfiles", RegistryKey));

    public string? UndoPath
    {
        get
        {
            if (RegistryUndoKey is null) return null;
            var u = Paths.Debloat(Path.Combine("Regfiles", "Undo", RegistryUndoKey));
            return File.Exists(u) ? u : Paths.Debloat(Path.Combine("Regfiles", RegistryUndoKey));
        }
    }
}

public sealed class TweakGroup
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public int Priority { get; init; } = 100;
    public string Label { get; init; } = "";
    public string ToolTip { get; init; } = "";
    public required List<(string FeatureId, string Label)> Options { get; init; }
}

/// <summary>Win11Debloat yapılandırmasını (MIT) okur: Features.json + tr-TR çevirileri.</summary>
public static class TweakCatalog
{
    public static IReadOnlyList<TweakCategory> Categories { get; private set; } = [];
    public static IReadOnlyDictionary<string, TweakFeature> Features { get; private set; } = new Dictionary<string, TweakFeature>();
    public static IReadOnlyList<TweakGroup> Groups { get; private set; } = [];
    public static IReadOnlyList<string> Recommended { get; private set; } = [];
    public static string Version { get; private set; } = "";

    /// <summary>Bu uygulamada (henüz) desteklenmeyen özel işlemler: sqlite / tüm kullanıcı profilleri gerektirenler.</summary>
    private static readonly HashSet<string> Unsupported = ["DisableStoreSearchSuggestions", "ClearStartAllUsers", "ReplaceStart", "ReplaceStartAllUsers"];

    private static readonly Dictionary<string, string> Descriptions = new()
    {
        ["PrivacySuggestedContent"] = "Telemetri, reklam kimliği, öneriler ve Microsoft'a giden veriler",
        ["System"] = "Bildirimler, sağ tık menüsü, fare, hızlı başlatma ve güç ayarları",
        ["StartMenuSearch"] = "Başlat menüsü önerileri, Bing araması ve Tüm uygulamalar görünümü",
        ["AI"] = "Copilot, Recall, Click to Do ve uygulamalardaki yapay zekâ özellikleri",
        ["WindowsUpdate"] = "Otomatik yeniden başlatma, sürücü güncellemeleri ve teslim iyileştirme",
        ["Taskbar"] = "Hizalama, arama kutusu, pencere düğmeleri ve pencere öğeleri (widget)",
        ["Appearance"] = "Koyu tema, saydamlık, animasyonlar ve Windows spot ışığı",
        ["FileExplorer"] = "Dosya uzantıları, gizli dosyalar, gezinti bölmesi ve açılış konumu",
        ["Gaming"] = "Oyun Çubuğu ve arka planda oyun kaydı (DVR)",
        ["MultiTasking"] = "Pencere tutturma, Snap düzenleri ve Alt+Tab sekmeleri",
        ["OptionalWindowsFeatures"] = "Windows Sandbox ve Linux için Windows Alt Sistemi (WSL)",
        ["Other"] = "Ayarlar giriş sayfası ve tarayıcı eklentileri",
    };

    public static void Load()
    {
        if (Categories.Count > 0) return;
        try { LoadCore(); }
        catch (Exception ex) { Log.Error("TweakCatalog.Load", ex); }
    }

    private static void LoadCore()
    {
        using var fdoc = JsonDocument.Parse(File.ReadAllText(Paths.Debloat("Config/Features.json")));
        using var tdoc = JsonDocument.Parse(File.ReadAllText(Paths.Debloat("Config/tr-TR/Features.json")));
        using var cdoc = JsonDocument.Parse(File.ReadAllText(Paths.Debloat("Config/tr-TR/Categories.json")));
        var root = fdoc.RootElement;
        Version = root.TryGetProperty("Version", out var v) ? v.ToString() : "";

        var tFeat = tdoc.RootElement.GetProperty("Features");
        var tGroups = tdoc.RootElement.TryGetProperty("UiGroups", out var tg) ? tg : default;

        var cats = new List<TweakCategory>();
        foreach (var c in root.GetProperty("Categories").EnumerateArray())
        {
            var id = c.GetProperty("CategoryId").GetString()!;
            var icon = c.GetProperty("Icon").GetString() ?? "";
            cats.Add(new TweakCategory
            {
                Id = id,
                Name = c.GetProperty("Name").GetString()!,
                Label = cdoc.RootElement.TryGetProperty(id, out var cl) ? cl.GetProperty("Label").GetString()! : id,
                Glyph = DecodeGlyph(icon),
                Description = Descriptions.GetValueOrDefault(id, ""),
            });
        }

        var feats = new Dictionary<string, TweakFeature>(StringComparer.Ordinal);
        foreach (var f in root.GetProperty("Features").EnumerateArray())
        {
            var id = f.GetProperty("FeatureId").GetString()!;
            var t = tFeat.TryGetProperty(id, out var te) ? te : default;
            feats[id] = new TweakFeature
            {
                Id = id,
                Category = Str(f, "Category") ?? "",
                Priority = Int(f, "Priority") ?? 100,
                RegistryKey = Str(f, "RegistryKey"),
                RegistryUndoKey = Str(f, "RegistryUndoKey"),
                MinVersion = Int(f, "MinVersion"),
                MaxVersion = Int(f, "MaxVersion"),
                RequiresReboot = Bool(f, "RequiresReboot"),
                DisableWhenApplied = Bool(f, "DisableWhenApplied"),
                Label = Str(t, "Label") ?? id,
                ToolTip = Str(t, "ToolTip") ?? "",
                UndoLabel = Str(t, "UndoLabel") ?? "",
            };
        }

        var groups = new List<TweakGroup>();
        foreach (var g in root.GetProperty("UiGroups").EnumerateArray())
        {
            var gid = g.GetProperty("GroupId").GetString()!;
            if (Unsupported.Contains(gid)) continue;
            var tr = tGroups.ValueKind == JsonValueKind.Object && tGroups.TryGetProperty(gid, out var tge) ? tge : default;
            var trValues = tr.ValueKind == JsonValueKind.Object && tr.TryGetProperty("Values", out var tv) ? tv : default;
            var opts = new List<(string, string)>();
            foreach (var val in g.GetProperty("Values").EnumerateArray())
            {
                var fid = val.GetProperty("FeatureIds")[0].GetString()!;
                if (Unsupported.Contains(fid) || !feats.TryGetValue(fid, out var feat) || !IsSupported(feat)) continue;
                var label = trValues.ValueKind == JsonValueKind.Object && trValues.TryGetProperty(fid, out var lv) ? lv.GetString()! : feat.Label;
                opts.Add((fid, label));
            }
            if (opts.Count < 2) continue;
            groups.Add(new TweakGroup
            {
                Id = gid,
                Category = g.GetProperty("Category").GetString()!,
                Priority = Int(g, "Priority") ?? 100,
                Label = Str(tr, "Label") ?? gid,
                ToolTip = Str(tr, "ToolTip") ?? "",
                Options = opts,
            });
        }

        var rec = new List<string>();
        try
        {
            using var ddoc = JsonDocument.Parse(File.ReadAllText(Paths.Debloat("Config/DefaultSettings.json")));
            foreach (var s in ddoc.RootElement.GetProperty("Settings").EnumerateArray())
                if (s.GetProperty("Value").ValueKind == JsonValueKind.True) rec.Add(s.GetProperty("Name").GetString()!);
        }
        catch { }

        Categories = cats; Features = feats; Groups = groups; Recommended = rec;
    }

    public static int Build { get; } = Environment.OSVersion.Version.Build;

    public static bool IsSupported(TweakFeature f) =>
        !Unsupported.Contains(f.Id) &&
        (f.MinVersion is null || Build >= f.MinVersion) &&
        (f.MaxVersion is null || Build <= f.MaxVersion) &&
        (f.RegistryKey is not null || TweakService.HasCustomLogic(f.Id));

    /// <summary>Bir kategoride grup dışında kalan, tek başına gösterilecek özellikler.</summary>
    public static IEnumerable<TweakFeature> StandaloneFeatures(TweakCategory c)
    {
        var grouped = Groups.SelectMany(g => g.Options.Select(o => o.FeatureId)).ToHashSet();
        return Features.Values
            .Where(f => f.Category == c.Name && !grouped.Contains(f.Id) && IsSupported(f))
            .OrderBy(f => f.Priority).ThenBy(f => f.Label, StringComparer.CurrentCulture);
    }

    public static IEnumerable<TweakGroup> GroupsOf(TweakCategory c) =>
        Groups.Where(g => g.Category == c.Name).OrderBy(g => g.Priority);

    private static string DecodeGlyph(string html)
    {
        // "&#xE72E;" -> ""
        var hex = html.Replace("&#x", "", StringComparison.OrdinalIgnoreCase).TrimEnd(';');
        return int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var cp) ? char.ConvertFromUtf32(cp) : "";
    }

    private static string? Str(JsonElement e, string n) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    private static int? Int(JsonElement e, string n) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;
    private static bool Bool(JsonElement e, string n) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.True;
}
