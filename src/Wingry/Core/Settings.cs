using System.Text.Json.Nodes;

namespace Wingry.Core;

/// <summary>%LOCALAPPDATA%\Wingry\ayarlar.json — JsonNode ile, yansımasız.</summary>
public static class Settings
{
    private static readonly string FilePath = Path.Combine(Paths.UserDir, "ayarlar.json");
    private static readonly object Gate = new();
    private static JsonObject _root = Load();

    private static JsonObject Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject o) return o;
        }
        catch (Exception ex) { Log.Error("Settings.Load", ex); }
        return new JsonObject();
    }

    private static void Save()
    {
        try
        {
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, _root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, true);
        }
        catch (Exception ex) { Log.Error("Settings.Save", ex); }
    }

    public static string GetString(string key, string def)
    {
        lock (Gate) return _root[key]?.GetValue<string>() ?? def;
    }

    public static bool GetBool(string key, bool def)
    {
        lock (Gate) return _root[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : def;
    }

    public static int GetInt(string key, int def)
    {
        lock (Gate) return _root[key] is JsonValue v && v.TryGetValue<int>(out var b) ? b : def;
    }

    public static void Set(string key, string value) { lock (Gate) { _root[key] = value; Save(); } }
    public static void Set(string key, bool value) { lock (Gate) { _root[key] = value; Save(); } }
    public static void Set(string key, int value) { lock (Gate) { _root[key] = value; Save(); } }

    public static HashSet<string> GetSet(string key)
    {
        lock (Gate)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_root[key] is JsonArray a)
                foreach (var n in a) if (n?.GetValue<string>() is { } s) set.Add(s);
            return set;
        }
    }

    public static void SetSet(string key, IEnumerable<string> values)
    {
        lock (Gate)
        {
            var a = new JsonArray();
            foreach (var v in values.Order()) a.Add((JsonNode?)JsonValue.Create(v));
            _root[key] = a;
            Save();
        }
    }

    // Sık kullanılan anahtarlar
    public const string Theme = "tema";                 // sistem | acik | koyu
    public const string IncludeStore = "msstore_dahil"; // Store güncellemeleri de taransın mı
    public const string IgnoredUpdates = "yoksayilan_guncellemeler";
    public const string AutoCleanPercent = "otomatik_ram_esik"; // 0 = kapalı
    public const string SilentInstall = "sessiz_kurulum";
    public const string ElevatedBatch = "toplu_yonetici"; // paket işleri tek UAC ile, sessiz
}
