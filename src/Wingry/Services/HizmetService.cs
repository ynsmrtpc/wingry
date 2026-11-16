using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Win32;
using Wingry.Core;

namespace Wingry.Services;

public sealed class HizmetItem
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public string Description { get; init; } = "";
    public required ServiceControllerStatus Status { get; init; }
    public required string StartType { get; init; }   // auto | delayed | demand | disabled | boot | system
    public string Publisher { get; init; } = "";
    public bool IsMicrosoft { get; init; }
    public bool IsCritical { get; init; }

    public bool Running => Status == ServiceControllerStatus.Running;
    public string StatusText => Status switch
    {
        ServiceControllerStatus.Running => "Çalışıyor",
        ServiceControllerStatus.Stopped => "Durdu",
        ServiceControllerStatus.Paused => "Duraklatıldı",
        ServiceControllerStatus.StartPending => "Başlatılıyor",
        ServiceControllerStatus.StopPending => "Durduruluyor",
        _ => Status.ToString(),
    };
    public string StartTypeText => StartType switch
    {
        "auto" => "Otomatik",
        "delayed" => "Otomatik (gecikmeli)",
        "demand" => "El ile",
        "disabled" => "Devre dışı",
        _ => "Sistem",
    };
}

/// <summary>
/// Windows hizmetleri. Okuma yönetici istemez; başlat/durdur/başlangıç türü yönetici işçisine gider.
/// Windows'un çalışması için gereken hizmetler korunur: durdurulamaz, devre dışı bırakılamaz.
/// </summary>
public static class HizmetService
{
    /// <summary>Durdurulması / kapatılması Windows'u bozan ya da oturumu düşüren hizmetler.</summary>
    public static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "RpcEptMapper", "DcomLaunch", "LSM", "SamSs", "lsass", "Winmgmt", "EventLog", "PlugPlay",
        "Power", "ProfSvc", "BFE", "mpssvc", "WinDefend", "Schedule", "CryptSvc", "Dhcp", "Dnscache", "nsi",
        "AudioEndpointBuilder", "Audiosrv", "BrokerInfrastructure", "CoreMessagingRegistrar", "SystemEventsBroker",
        "TimeBrokerSvc", "UserManager", "gpsvc", "StateRepository", "Appinfo", "AppXSvc", "wuauserv", "TrustedInstaller",
        "WinHttpAutoProxySvc", "SENS", "ShellHWDetection", "Themes", "FontCache", "LanmanWorkstation", "netprofm",
        "NlaSvc", "EventSystem", "SgrmBroker", "KeyIso", "VaultSvc", "WdNisSvc", "SecurityHealthService", "wscsvc",
    };

    public static List<HizmetItem> List()
    {
        var list = new List<HizmetItem>();
        foreach (var sc in ServiceController.GetServices())
        {
            using (sc)
            {
                try
                {
                    var name = sc.ServiceName;
                    string desc = "", image = "";
                    bool delayed = false;
                    using (var k = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}"))
                    {
                        desc = ResolveIndirect(k?.GetValue("Description") as string ?? "");
                        image = k?.GetValue("ImagePath") as string ?? "";
                        delayed = k?.GetValue("DelayedAutostart") is int d && d == 1;
                    }
                    var exe = StartupService.ExtractExe(image);
                    var pub = Publisher(exe);
                    list.Add(new HizmetItem
                    {
                        Name = name,
                        DisplayName = sc.DisplayName,
                        Description = desc,
                        Status = sc.Status,
                        StartType = sc.StartType switch
                        {
                            ServiceStartMode.Automatic => delayed ? "delayed" : "auto",
                            ServiceStartMode.Manual => "demand",
                            ServiceStartMode.Disabled => "disabled",
                            ServiceStartMode.Boot => "boot",
                            _ => "system",
                        },
                        Publisher = pub,
                        IsMicrosoft = pub.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)
                                      || (exe?.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase) ?? false),
                        IsCritical = Critical.Contains(name),
                    });
                }
                catch { /* erişilemeyen hizmet: atla */ }
            }
        }
        return list.OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static readonly Dictionary<string, string> PubCache = new(StringComparer.OrdinalIgnoreCase);

    private static string Publisher(string? exe)
    {
        if (string.IsNullOrEmpty(exe)) return "";
        lock (PubCache)
            if (PubCache.TryGetValue(exe, out var c)) return c;
        string p = "";
        try
        {
            var path = exe;
            if (!Path.IsPathRooted(path)) path = Path.Combine(Environment.SystemDirectory, path);
            if (File.Exists(path)) p = FileVersionInfo.GetVersionInfo(path).CompanyName?.Trim() ?? "";
        }
        catch { }
        lock (PubCache) PubCache[exe] = p;
        return p;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(string source, System.Text.StringBuilder outBuf, int cch, nint reserved);

    /// <summary>"@%SystemRoot%\system32\x.dll,-123" biçimindeki açıklamaları çözer.</summary>
    private static string ResolveIndirect(string s)
    {
        if (!s.StartsWith('@')) return s;
        var sb = new System.Text.StringBuilder(1024);
        return SHLoadIndirectString(s, sb, sb.Capacity, 0) == 0 ? sb.ToString() : "";
    }

    // ---------------------------------------------------------------- işlemler (yönetici)

    public static async Task<string?> RunAsync(string action, HizmetItem s, string? startType = null)
    {
        if (s.IsCritical && (action == "stop" || startType == "disabled"))
            return $"{s.DisplayName} Windows'un çalışması için gerekli; durdurulamaz / kapatılamaz.";
        var cmd = startType is null ? $"svc|{action}|{s.Name}" : $"svc|type|{s.Name}|{startType}";
        var w = await Elevation.RunAsync([cmd]);
        if (!w.Started) return w.Error ?? "Yönetici izni verilmedi.";
        var err = w.Lines.FirstOrDefault(l => l.StartsWith("HATA", StringComparison.Ordinal));
        Log.Write($"Hizmet {cmd}: {(err ?? "tamam")}");
        return err?.Replace("HATA: ", "");
    }

    /// <summary>İşçide çalışır (yönetici).</summary>
    public static string Execute(string action, string name, string? type)
    {
        if (name.Length == 0 || name.IndexOfAny(['"', '|', '\r', '\n', '\\', '/']) >= 0) return "HATA: geçersiz hizmet adı";
        if (Critical.Contains(name) && (action == "stop" || type == "disabled")) return "HATA: korunan hizmet";
        using var sc = new ServiceController(name);
        var wait = TimeSpan.FromSeconds(30);
        switch (action)
        {
            case "start":
                if (sc.Status != ServiceControllerStatus.Running) { sc.Start(); sc.WaitForStatus(ServiceControllerStatus.Running, wait); }
                return $"OK: {name} başladı";
            case "stop":
                if (sc.Status != ServiceControllerStatus.Stopped) { sc.Stop(); sc.WaitForStatus(ServiceControllerStatus.Stopped, wait); }
                return $"OK: {name} durdu";
            case "restart":
                if (sc.Status != ServiceControllerStatus.Stopped) { sc.Stop(); sc.WaitForStatus(ServiceControllerStatus.Stopped, wait); }
                sc.Start(); sc.WaitForStatus(ServiceControllerStatus.Running, wait);
                return $"OK: {name} yeniden başladı";
            case "type":
                var arg = type switch { "auto" => "auto", "delayed" => "delayed-auto", "demand" => "demand", "disabled" => "disabled", _ => null };
                if (arg is null) return "HATA: geçersiz başlangıç türü";
                var (code, o) = ElevatedWorker.RunTool("sc.exe", $"config \"{name}\" start= {arg}", 20_000);
                return code == 0 ? $"OK: {name} {arg}" : $"HATA: {o.Trim()}";
        }
        return "HATA: bilinmeyen işlem";
    }
}
