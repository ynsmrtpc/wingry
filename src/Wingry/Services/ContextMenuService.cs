using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Wingry.Core;

namespace Wingry.Services;

public sealed class ContextEntry : Bindable
{
    public required string Scope { get; init; }        // *, Directory, Directory\Background, Drive
    public required string ScopeText { get; init; }
    public required string Key { get; init; }          // komut ya da işleyici anahtar adı
    public required string Name { get; init; }
    public required bool IsHandler { get; init; }      // shellex işleyicisi (CLSID) mi, kabuk komutu mu
    public string Clsid { get; init; } = "";
    public string Detail { get; init; } = "";
    public bool Locked { get; init; }                  // sistem tarafından (HKLM) kapatılmış, kullanıcı düzeyinde açılamaz

    private bool _enabled;
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public bool CanToggle => !Locked;
}

/// <summary>
/// Sağ tık menüsü girdileri. Yönetici gerektirmez, her şey geri alınabilir:
///  - Kabuk komutu (shell\X): HKCU\Software\Classes\...\shell\X altına "LegacyDisable" değeri yazılır
///    (HKCR görünümünde HKCU, HKLM'nin üstüne biner).
///  - Kabuk uzantısı (shellex\ContextMenuHandlers): CLSID, HKCU "Shell Extensions\Blocked" listesine eklenir.
/// </summary>
public static class ContextMenuService
{
    private const string BlockedKey = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";

    private static readonly (string scope, string text)[] Scopes =
    [
        ("*", "Dosyalar"),
        ("AllFilesystemObjects", "Dosya ve klasörler"),
        ("Directory", "Klasörler"),
        ("Directory\\Background", "Klasör arka planı"),
        ("Drive", "Sürücüler"),
    ];

    // Windows'un kendi, gizlenmesi menüyü bozan girdileri listede gösterme
    private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "open", "opennewwindow", "opennewprocess", "opennewtab", "explore", "find", "runas", "runasuser", "properties",
        "cmd", "Powershell", "pintohome", "find", "edit", "print", "printto", "openas",
    };

    public static List<ContextEntry> List()
    {
        var list = new List<ContextEntry>();
        var blockedU = BlockedSet(Registry.CurrentUser);
        var blockedM = BlockedSet(Registry.LocalMachine);
        foreach (var (scope, text) in Scopes)
        {
            try
            {
                using (var shell = Registry.ClassesRoot.OpenSubKey($@"{scope}\shell"))
                    if (shell is not null)
                        foreach (var verb in shell.GetSubKeyNames())
                        {
                            if (Hidden.Contains(verb)) continue;
                            using var k = shell.OpenSubKey(verb);
                            if (k is null) continue;
                            var disabledHkcu = HkcuHasLegacyDisable(scope, verb);
                            using var m = Registry.LocalMachine.OpenSubKey($@"Software\Classes\{scope}\shell\{verb}");
                            bool lockedByMachine = m?.GetValue("LegacyDisable") is not null && !disabledHkcu;
                            var name = Label(k.GetValue("MUIVerb") as string) ?? Label(k.GetValue(null) as string) ?? verb;
                            using var cmd = k.OpenSubKey("command");
                            list.Add(new ContextEntry
                            {
                                Scope = scope, ScopeText = text, Key = verb, Name = Clean(name), IsHandler = false,
                                Detail = (cmd?.GetValue(null) as string ?? "").Trim(),
                                Enabled = k.GetValue("LegacyDisable") is null,
                                Locked = lockedByMachine,
                            });
                        }

                using (var hx = Registry.ClassesRoot.OpenSubKey($@"{scope}\shellex\ContextMenuHandlers"))
                    if (hx is not null)
                        foreach (var h in hx.GetSubKeyNames())
                        {
                            using var k = hx.OpenSubKey(h);
                            var clsid = (k?.GetValue(null) as string)?.Trim() ?? "";
                            if (!clsid.StartsWith('{')) clsid = h.StartsWith('{') ? h : "";
                            if (clsid.Length == 0) continue;
                            using var ck = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}");
                            var name = ck?.GetValue(null) as string;
                            using var inproc = ck?.OpenSubKey("InprocServer32");
                            var dll = inproc?.GetValue(null) as string ?? "";
                            list.Add(new ContextEntry
                            {
                                Scope = scope, ScopeText = text, Key = h, Clsid = clsid, IsHandler = true,
                                Name = string.IsNullOrWhiteSpace(name) ? h : name!,
                                Detail = dll.Length > 0 ? dll : clsid,
                                Enabled = !blockedU.Contains(clsid) && !blockedM.Contains(clsid),
                                Locked = blockedM.Contains(clsid),
                            });
                        }
            }
            catch (Exception ex) { Log.Error("Bağlam menüsü " + scope, ex); }
        }
        return list.OrderBy(e => e.ScopeText).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static HashSet<string> BlockedSet(RegistryKey root)
    {
        var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try { using var k = root.OpenSubKey(BlockedKey); if (k is not null) foreach (var v in k.GetValueNames()) s.Add(v); } catch { }
        return s;
    }

    private static bool HkcuHasLegacyDisable(string scope, string verb)
    {
        using var k = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{scope}\shell\{verb}");
        return k?.GetValue("LegacyDisable") is not null;
    }

    public static string? SetEnabled(ContextEntry e, bool enabled)
    {
        try
        {
            if (e.IsHandler)
            {
                using var k = Registry.CurrentUser.CreateSubKey(BlockedKey, true);
                if (enabled) k.DeleteValue(e.Clsid, false);
                else k.SetValue(e.Clsid, e.Name, RegistryValueKind.String);
            }
            else
            {
                using var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{e.Scope}\shell\{e.Key}", true);
                if (enabled) k.DeleteValue("LegacyDisable", false);
                else k.SetValue("LegacyDisable", "", RegistryValueKind.String);
            }
            Log.Write($"Bağlam menüsü {(enabled ? "açıldı" : "kapatıldı")}: {e.ScopeText} / {e.Name}");
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(string source, StringBuilder outBuf, int cch, nint reserved);

    private static string? Label(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (!s.StartsWith('@')) return s;
        var sb = new StringBuilder(512);
        return SHLoadIndirectString(s, sb, sb.Capacity, 0) == 0 && sb.Length > 0 ? sb.ToString() : null;
    }

    private static string Clean(string s) => s.Replace("&", "").Trim();
}
