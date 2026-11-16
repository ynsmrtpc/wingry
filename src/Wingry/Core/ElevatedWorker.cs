using System.Diagnostics;
using Wingry.Services;

namespace Wingry.Core;

/// <summary>
/// <c>Wingry.exe --worker istek.txt</c>: yönetici olarak çalışır, istek dosyasındaki her satırı
/// bir komut olarak yürütür, sonuçları <c>istek.txt.sonuc</c> dosyasına yazar.
/// Komutlar sabit bir listeden gelir; dışarıdan rastgele program çalıştırmaz.
/// </summary>
public static class ElevatedWorker
{
    private static readonly string[] TelemetryTasks =
    [
        @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
        @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser Exp",
        @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
        @"\Microsoft\Windows\Application Experience\StartupAppTask",
        @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
        @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
        @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector",
        @"\Microsoft\Windows\Autochk\Proxy",
    ];

    public static int Run(string[] args)
    {
        if (args.Length == 0 || !File.Exists(args[0])) return 2;
        var req = args[0];
        // İstek dosyası bir Wingry geçici klasöründen gelmeli (UAC başka bir yönetici hesabıyla
        // onaylanırsa %LOCALAPPDATA% farklıdır; bu yüzden klasör adına bakılır).
        if (!Path.GetFullPath(req).Contains(@"\Wingry\Gecici\", StringComparison.OrdinalIgnoreCase)) return 3;
        var lines = Execute(File.ReadAllLines(req));
        File.WriteAllLines(req + ".sonuc", lines);
        return lines.Any(l => l.StartsWith("HATA", StringComparison.Ordinal)) ? 1 : 0;
    }

    public static List<string> Execute(IEnumerable<string> commands)
    {
        var outp = new List<string>();
        foreach (var raw in commands)
        {
            var cmd = raw.Trim();
            if (cmd.Length == 0) continue;
            try { outp.Add(ExecuteOne(cmd)); }
            catch (Exception ex) { outp.Add($"HATA: {cmd}: {ex.Message}"); }
        }
        return outp;
    }

    private static string ExecuteOne(string cmd)
    {
        var p = cmd.Split('|');
        switch (p[0])
        {
            case "reg":
            {
                var file = p[1];
                if (!IsTrustedRegFile(file)) return $"HATA: güvenilmeyen kayıt dosyası: {file}";
                var errs = RegFile.Apply(RegFile.Parse(File.ReadAllText(file)), o => o.NeedsAdmin);
                return errs.Count == 0 ? $"OK: {Path.GetFileName(file)}" : "HATA: " + string.Join("; ", errs);
            }
            case "tasks":
            {
                var flag = p[1] == "disable" ? "/Disable" : "/Enable";
                int fail = 0;
                foreach (var t in TelemetryTasks)
                    if (RunTool("schtasks.exe", $"/Change /TN \"{t}\" {flag}").code != 0) fail++;
                // Bazı görevler sistemde yok; bu hata sayılmaz.
                return $"OK: telemetri görevleri ({TelemetryTasks.Length - fail}/{TelemetryTasks.Length})";
            }
            case "dism":
            {
                var action = p[1] == "enable" ? "/Enable-Feature" : "/Disable-Feature";
                var all = p[1] == "enable" ? " /All" : "";
                var (code, o) = RunTool("dism.exe", $"/Online {action} /FeatureName:{p[2]}{all} /NoRestart /Quiet");
                // 3010 = başarılı, yeniden başlatma gerekli
                return code is 0 or 3010 ? $"OK: {p[2]}" : $"HATA: {p[2]} (dism {code}) {o.Trim()}";
            }
            case "restorepoint":
                return CreateRestorePoint();
            case "ramdeep":
            {
                var r = MemoryService.Clean(true);
                return r.Error is null ? $"OK: {r.FreedBytes}" : $"HATA: {r.Error}";
            }
            case "schtask":
            {
                if (p[1] == "create")
                {
                    var (code, o) = RunTool("schtasks.exe", $"/Create /TN \"{p[2]}\" /XML \"{p[3]}\" /F");
                    return code == 0 ? $"OK: {p[2]}" : $"HATA: görev oluşturulamadı: {o.Trim()}";
                }
                if (p[1] == "delete")
                {
                    var (code, o) = RunTool("schtasks.exe", $"/Delete /TN \"{p[2]}\" /F");
                    return code == 0 ? $"OK: {p[2]}" : $"HATA: görev silinemedi: {o.Trim()}";
                }
                return "HATA: bilinmeyen görev komutu";
            }
            case "arac":
                return ToolCommand(p);
            case "svc":
                return HizmetService.Execute(p[1], p[2], p.Length > 3 ? p[3] : null);
            case "ilerleme":
                _progress = p[1];
                return "OK: ilerleme";
            case "winget":
                return RunWinget(p[1], p[2], p.Length > 3 ? p[3] : "");
            case "clean":
            {
                var freed = CleanupService.CleanAdminTarget(p[1]);
                return $"OK: {freed}";
            }
            default:
                return $"HATA: bilinmeyen komut {p[0]}";
        }
    }

    // ---------------------------------------------------------------- toplu paket işlemi

    private static string? _progress;

    private static void Progress(string line)
    {
        if (_progress is null) return;
        try
        {
            using var fs = new FileStream(_progress, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var w = new StreamWriter(fs);
            w.WriteLine(line);
        }
        catch { }
    }

    private static bool Cancelled(string id)
    {
        try
        {
            var f = _progress + ".iptal";
            return File.Exists(f) && File.ReadAllLines(f).Contains(id, StringComparer.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// winget'i yönetici olarak, sessiz kipte çalıştırır. Kurulum programı zaten yönetici
    /// süreçten başladığı için ayrıca UAC sorulmaz (sorun: COM sunucusu arka planda olduğundan
    /// UAC penceresi öne gelmiyor, görev çubuğunda yanıp sönüp bekliyordu).
    /// </summary>
    private static string RunWinget(string action, string id, string source)
    {
        // Kimlik tırnak içinde geçer; tırnak ya da satır sonu içeren kimlik kabul edilmez.
        if (id.Length == 0 || id.IndexOfAny(['"', '\r', '\n', '|']) >= 0) return $"HATA: geçersiz paket kimliği {id}";
        if (action is not ("install" or "upgrade" or "uninstall")) return $"HATA: bilinmeyen işlem {action}";
        if (Cancelled(id)) { Progress($"SONUC|{id}|iptal|İptal edildi"); return $"OK: {id} iptal"; }

        Progress($"BASLA|{id}");
        var args = new System.Text.StringBuilder($"{action} --id \"{id}\" -e --silent --disable-interactivity --accept-source-agreements");
        if (action != "uninstall") args.Append(" --accept-package-agreements");
        if (source is "winget" or "msstore") args.Append(" --source ").Append(source);

        var (code, output) = RunTool("winget.exe", args.ToString(), 60 * 60 * 1000);
        uint u = unchecked((uint)code);
        string msg = u switch
        {
            0 => action switch { "install" => "Kuruldu", "upgrade" => "Güncellendi", _ => "Kaldırıldı" },
            0x8A15002B => "Zaten güncel",
            0x8A150011 => "Paket bulunamadı",
            0x8A150014 => "Paket bulunamadı",
            0x8A15010D or 0x8A150109 => "Tamamlandı · yeniden başlatma gerekli",
            0x8A150101 => "Uygulama açık, kapatıp tekrar deneyin",
            0x8A15010C => "Paket zaten yüklü",
            _ when code == -1 => "Zaman aşımı",
            _ => $"Başarısız (0x{u:X8})",
        };
        bool ok = u is 0 or 0x8A15010D or 0x8A150109 or 0x8A15010C or 0x8A15002B;
        if (!ok) Log.Write($"winget {action} {id} -> 0x{u:X8}: {Tail(output)}");
        Progress($"SONUC|{id}|{(ok ? "ok" : "hata")}|{msg}");
        return ok ? $"OK: {id}" : $"HATA: {id}: {msg}";
    }

    private static string Tail(string s)
    {
        var lines = s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(" / ", lines.TakeLast(3));
    }

    // ---------------------------------------------------------------- araç komutları (ağ, güç, onarım, hosts, sürücü)

    private static bool InWingryTemp(string path) =>
        Path.GetFullPath(path).Contains(@"\Wingry\Gecici\", StringComparison.OrdinalIgnoreCase);

    private static string ToolCommand(string[] p)
    {
        string Ok(string m) => "OK: " + m;
        string Fail(int code, string o) => $"HATA: ({code}) {o.Trim().Split('\n').LastOrDefault()?.Trim()}";
        switch (p[1])
        {
            case "dns":
            {
                // araç|dns|<bağdaştırıcı>|<birincil>|<ikincil>   ya da   araç|dns|<bağdaştırıcı>|auto
                var name = p[2];
                if (!Tools.SafeArg(name)) return "HATA: geçersiz bağdaştırıcı adı";
                if (p[3] == "auto")
                {
                    var (c, o) = Tools.Cmd($"netsh interface ipv4 set dnsservers name=\"{name}\" source=dhcp");
                    Tools.Cmd($"netsh interface ipv6 set dnsservers name=\"{name}\" source=dhcp");
                    return c == 0 ? Ok($"{name} otomatik DNS") : Fail(c, o);
                }
                if (!System.Net.IPAddress.TryParse(p[3], out _) || !System.Net.IPAddress.TryParse(p[4], out _)) return "HATA: geçersiz DNS adresi";
                var (c1, o1) = Tools.Cmd($"netsh interface ipv4 set dnsservers name=\"{name}\" source=static address={p[3]} register=primary validate=no");
                if (c1 != 0) return Fail(c1, o1);
                Tools.Cmd($"netsh interface ipv4 add dnsservers name=\"{name}\" address={p[4]} index=2 validate=no");
                Tools.Cmd("ipconfig /flushdns");
                return Ok($"{name} DNS {p[3]}, {p[4]}");
            }
            case "agsifirla":
            {
                var (c1, o1) = Tools.Cmd("netsh winsock reset");
                var (c2, o2) = Tools.Cmd("netsh int ip reset");
                Tools.Cmd("ipconfig /flushdns");
                return c1 == 0 && c2 == 0 ? Ok("ağ sıfırlandı") : Fail(c1 != 0 ? c1 : c2, o1 + o2);
            }
            case "wifi":
            {
                var dir = p[2];
                if (!InWingryTemp(dir)) return "HATA: geçersiz klasör";
                Directory.CreateDirectory(dir);
                var (c, o) = Tools.Cmd($"netsh wlan export profile key=clear folder=\"{dir}\"");
                return c == 0 ? Ok("wifi") : Fail(c, o);
            }
            case "guc":
            {
                if (p[2] == "nihai")
                {
                    var (c, o) = Tools.Cmd("powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61");
                    return c == 0 ? Ok("nihai performans planı eklendi") : Fail(c, o);
                }
                if (p[2] is "hibernate-off" or "hibernate-on")
                {
                    var (c, o) = Tools.Cmd($"powercfg /hibernate {(p[2] == "hibernate-off" ? "off" : "on")}");
                    return c == 0 ? Ok(p[2]) : Fail(c, o);
                }
                return "HATA: bilinmeyen güç komutu";
            }
            case "sfc":
            {
                var (c, o) = Tools.Run("sfc.exe", "/scannow", 3 * 60 * 60 * 1000);
                return c == 0 ? Ok("SFC tamamlandı") : $"HATA: SFC çıkış kodu {c}";
            }
            case "dism":
            {
                var arg = p[2] switch
                {
                    "restore" => "/Online /Cleanup-Image /RestoreHealth",
                    "scan" => "/Online /Cleanup-Image /ScanHealth",
                    "cleanup" => "/Online /Cleanup-Image /StartComponentCleanup",
                    _ => null,
                };
                if (arg is null) return "HATA: bilinmeyen DISM komutu";
                var (c, o) = Tools.Run("dism.exe", arg + " /NoRestart", 3 * 60 * 60 * 1000);
                return c is 0 or 3010 ? Ok("DISM tamamlandı") : $"HATA: DISM çıkış kodu {c}";
            }
            case "hosts":
            {
                var src = p[2];
                if (!InWingryTemp(src) || !File.Exists(src)) return "HATA: geçersiz kaynak";
                var hosts = Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts");
                var backupDir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(src)))!, "Yedekler");
                Directory.CreateDirectory(backupDir);
                if (File.Exists(hosts)) File.Copy(hosts, Path.Combine(backupDir, $"hosts-{DateTime.Now:yyyyMMdd-HHmmss}"), true);
                File.Copy(src, hosts, true);
                Tools.Cmd("ipconfig /flushdns");
                return Ok("hosts kaydedildi");
            }
            case "surucu-yedek":
            {
                var dir = p[2];
                if (!Path.IsPathRooted(dir) || !Tools.SafeArg(dir)) return "HATA: geçersiz klasör";
                Directory.CreateDirectory(dir);
                var (c, o) = Tools.Run("dism.exe", $"/Online /Export-Driver /Destination:\"{dir}\"", 60 * 60 * 1000);
                return c == 0 ? Ok("sürücüler yedeklendi") : $"HATA: DISM çıkış kodu {c}";
            }
        }
        return "HATA: bilinmeyen araç komutu";
    }

    private static bool IsTrustedRegFile(string file)
    {
        var full = Path.GetFullPath(file);
        return full.StartsWith(Path.GetFullPath(Paths.Debloat("Regfiles")), StringComparison.OrdinalIgnoreCase)
            || full.Contains(@"\Wingry\Yedekler\", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateRestorePoint()
    {
        // 24 saatte bir sınırını geçici olarak kaldır, Sistem Koruması kapalıysa aç.
        const string script =
            "$k='HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\SystemRestore';" +
            "$old=(Get-ItemProperty $k -Name SystemRestorePointCreationFrequency -ErrorAction SilentlyContinue).SystemRestorePointCreationFrequency;" +
            "Set-ItemProperty $k -Name SystemRestorePointCreationFrequency -Value 0 -Type DWord;" +
            "try { Enable-ComputerRestore -Drive \"$env:SystemDrive\\\" -ErrorAction SilentlyContinue; Checkpoint-Computer -Description 'Wingry' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop; exit 0 }" +
            "catch { Write-Output $_.Exception.Message; exit 1 }" +
            "finally { if ($null -eq $old) { Remove-ItemProperty $k -Name SystemRestorePointCreationFrequency -ErrorAction SilentlyContinue } else { Set-ItemProperty $k -Name SystemRestorePointCreationFrequency -Value $old } }";
        var (code, o) = RunTool("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"");
        return code == 0 ? "OK: geri yükleme noktası" : $"HATA: geri yükleme noktası oluşturulamadı: {o.Trim()}";
    }

    public static (int code, string output) RunTool(string exe, string args, int timeoutMs = 600_000)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var so = p.StandardOutput.ReadToEndAsync();
        var se = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return (-1, "zaman aşımı"); }
        return (p.ExitCode, so.Result + se.Result);
    }
}
