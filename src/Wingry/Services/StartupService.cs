using System.Diagnostics;
using Microsoft.Win32;
using Wingry.Core;

namespace Wingry.Services;

public sealed class StartupItem : Bindable
{
    public required string Name { get; init; }
    public required string Command { get; init; }
    public required string Location { get; init; }       // kullanıcıya gösterilen kaynak
    public required RegistryHive ApprovedHive { get; init; }
    public required string ApprovedKey { get; init; }     // ...\StartupApproved\Run | Run32 | StartupFolder
    public required string ApprovedValue { get; init; }  // Run: değer adı, klasör: dosya adı
    public string? FilePath { get; init; }               // .lnk ya da çalıştırılabilir dosya
    public string? ExePath { get; init; }
    public string Publisher { get; init; } = "";
    public bool NeedsAdmin => ApprovedHive != RegistryHive.CurrentUser;

    private bool _enabled;
    public bool Enabled { get => _enabled; set { if (Set(ref _enabled, value)) Raise(nameof(StateText)); } }
    public string StateText => Enabled ? "Açık" : "Kapalı";
    public string Sub => string.IsNullOrEmpty(Publisher) ? Location : $"{Publisher}  ·  {Location}";
}

/// <summary>
/// Görev Yöneticisi > Başlangıç uygulamaları ile aynı kaynaklar ve aynı açma/kapama yöntemi:
/// öğe silinmez, "StartupApproved" altındaki 12 baytlık değerin ilk baytı 02 (açık) / 03 (kapalı) yapılır.
/// </summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Key = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public static List<StartupItem> List()
    {
        var items = new List<StartupItem>();
        ReadRun(items, RegistryHive.CurrentUser, RunKey, "Run", "Kayıt defteri (kullanıcı)");
        ReadRun(items, RegistryHive.LocalMachine, RunKey, "Run", "Kayıt defteri (tüm kullanıcılar)");
        ReadRun(items, RegistryHive.LocalMachine, Run32Key, "Run32", "Kayıt defteri (tüm kullanıcılar, 32 bit)");
        ReadFolder(items, RegistryHive.CurrentUser, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Başlangıç klasörü (kullanıcı)");
        ReadFolder(items, RegistryHive.LocalMachine, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Başlangıç klasörü (tüm kullanıcılar)");
        return items.OrderByDescending(i => i.Enabled).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void ReadRun(List<StartupItem> items, RegistryHive hive, string key, string approvedSub, string location)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var k = root.OpenSubKey(key);
            if (k is null) return;
            foreach (var name in k.GetValueNames())
            {
                if (string.IsNullOrEmpty(name)) continue;
                var cmd = k.GetValue(name)?.ToString() ?? "";
                var exe = ExtractExe(cmd);
                items.Add(new StartupItem
                {
                    Name = FriendlyName(name, exe),
                    Command = cmd,
                    Location = location,
                    ApprovedHive = hive,
                    ApprovedKey = Approved + approvedSub,
                    ApprovedValue = name,
                    ExePath = exe,
                    Publisher = Company(exe),
                    Enabled = IsEnabled(hive, Approved + approvedSub, name),
                });
            }
        }
        catch (Exception ex) { Log.Error("Başlangıç " + location, ex); }
    }

    private static void ReadFolder(List<StartupItem> items, RegistryHive hive, string dir, string location)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir))
            {
                var file = Path.GetFileName(f);
                if (file.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                var target = f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ShortcutTarget(f) ?? f : f;
                items.Add(new StartupItem
                {
                    Name = FriendlyName(Path.GetFileNameWithoutExtension(file), target),
                    Command = target,
                    Location = location,
                    ApprovedHive = hive,
                    ApprovedKey = Approved + "StartupFolder",
                    ApprovedValue = file,
                    FilePath = f,
                    ExePath = target,
                    Publisher = Company(target),
                    Enabled = IsEnabled(hive, Approved + "StartupFolder", file),
                });
            }
        }
        catch (Exception ex) { Log.Error("Başlangıç klasörü " + dir, ex); }
    }

    private static bool IsEnabled(RegistryHive hive, string key, string value)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var k = root.OpenSubKey(key);
            // Değer yoksa açık sayılır; ilk bayt tekse kapalı (03), çiftse açık (02)
            return k?.GetValue(value) is not byte[] b || b.Length == 0 || (b[0] & 1) == 0;
        }
        catch { return true; }
    }

    /// <summary>Açık/kapalı yapar. HKLM öğeleri yönetici işçisine gider.</summary>
    public static async Task<string?> SetEnabledAsync(StartupItem item, bool enabled)
    {
        // Görev Yöneticisi biçimi: 12 bayt; kapalıyken 03 + kapatılma zamanı (FILETIME)
        var data = new byte[12];
        data[0] = enabled ? (byte)2 : (byte)3;
        if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        var op = new RegOp(item.ApprovedHive, item.ApprovedKey, RegOpKind.SetValue, item.ApprovedValue, RegistryValueKind.Binary, data);

        if (!item.NeedsAdmin)
        {
            var errs = RegFile.Apply([op]);
            return errs.Count == 0 ? null : string.Join("; ", errs);
        }
        // Yönetici işçisi .reg dosyası bekliyor: geçici bir .reg yazıp onu uygula
        var reg = Path.Combine(Paths.Backups, $"baslangic-{Guid.NewGuid():N}.reg");
        var hex = string.Join(',', data.Select(b => b.ToString("x2")));
        File.WriteAllText(reg, $"Windows Registry Editor Version 5.00\r\n\r\n[HKEY_LOCAL_MACHINE\\{item.ApprovedKey}]\r\n\"{item.ApprovedValue.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"=hex:{hex}\r\n", System.Text.Encoding.Unicode);
        try
        {
            var w = await Elevation.RunAsync(["reg|" + reg]);
            if (!w.Started) return w.Error ?? "Yönetici izni verilmedi.";
            return w.Lines.FirstOrDefault(l => l.StartsWith("HATA", StringComparison.Ordinal));
        }
        finally { try { File.Delete(reg); } catch { } }
    }

    // ---------------------------------------------------------------- yardımcılar

    public static string? ExtractExe(string cmd)
    {
        cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
        if (cmd.Length == 0) return null;
        string path;
        if (cmd[0] == '"') { int e = cmd.IndexOf('"', 1); path = e > 0 ? cmd[1..e] : cmd.Trim('"'); }
        else
        {
            // Tırnaksız yollar boşluk içerebilir: .exe'ye kadar al
            int e = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            path = e > 0 ? cmd[..(e + 4)] : cmd.Split(' ')[0];
        }
        return path;
    }

    private static string Company(string? exe)
    {
        try { return exe is not null && File.Exists(exe) ? FileVersionInfo.GetVersionInfo(exe).CompanyName?.Trim() ?? "" : ""; }
        catch { return ""; }
    }

    private static string FriendlyName(string raw, string? exe)
    {
        try
        {
            if (exe is not null && File.Exists(exe))
            {
                var d = FileVersionInfo.GetVersionInfo(exe).FileDescription?.Trim();
                if (!string.IsNullOrEmpty(d) && d.Length < 60) return d;
            }
        }
        catch { }
        return raw;
    }

    /// <summary>.lnk hedefini Shell COM nesnesiyle çözer (WScript.Shell).</summary>
    private static string? ShortcutTarget(string lnk)
    {
        try
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            if (t is null) return null;
            dynamic shell = Activator.CreateInstance(t)!;
            dynamic sc = shell.CreateShortcut(lnk);
            string target = sc.TargetPath;
            return string.IsNullOrEmpty(target) ? null : target;
        }
        catch { return null; }
    }
}
