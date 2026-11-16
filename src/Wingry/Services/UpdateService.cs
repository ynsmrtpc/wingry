using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Wingry.Core;

namespace Wingry.Services;

public sealed record UpdateInfo(Version Version, string Tag, string Notes, string DownloadUrl, long Size, DateTime Published);

/// <summary>
/// Wingry'nin kendini güncellemesi: GitHub Releases (ynsmrtpc/wingry) üzerinden.
/// Sürüm etiketi "v1.2.0" biçiminde, sürüm dosyası "Wingry.exe" (build.ps1 çıktısı = başlatıcı).
///
/// Akış: yeni Wingry.exe indirilir → kullanıcının çalıştırdığı başlatıcının yerine kopyalanır
/// (başlatıcı uygulamayı açınca kapanmıştır, dosya kilitli değildir) → başlatıcı
/// "--bekle PID" ile çalıştırılır → bu süreç kapanır → başlatıcı eski sürüm kapanınca yeni
/// uygulamayı %LOCALAPPDATA%\Wingry\uygulama altına açar ve başlatır.
/// </summary>
public static class UpdateService
{
    public const string Repo = "ynsmrtpc/wingry";
    public const string AssetName = "Wingry.exe";

    public static Version Current { get; } = Normalize(typeof(UpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0));

    /// <summary>Başlatıcı, uygulamayı açarken kendi yolunu bu değişkenle bildirir.</summary>
    /// Yönetici olarak yeniden başlatınca ortam değişkeni taşınmayabilir; bu yüzden ayara da yazılır.
    public static string? LauncherPath
    {
        get
        {
            var p = Environment.GetEnvironmentVariable("WINGRY_BASLATICI");
            if (p is { Length: > 0 } && File.Exists(p)) { Settings.Set("baslatici_yolu", p); return p; }
            p = Settings.GetString("baslatici_yolu", "");
            return p.Length > 0 && File.Exists(p) ? p : null;
        }
    }

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        h.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Wingry", Current.ToString(3)));
        h.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return h;
    }

    private static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    /// <summary>Yeni sürüm varsa döner; yoksa null. Ağ hatasında istisna fırlatır.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var resp = await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct);
        Settings.Set("guncelleme_son_kontrol", DateTime.Now.ToString("O"));
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // henüz yayımlanmış sürüm yok
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var v)) return null;
        v = Normalize(v);
        if (v <= Current) return null;

        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            if (!string.Equals(a.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase)) continue;
            return new UpdateInfo(v, tag,
                root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                a.GetProperty("browser_download_url").GetString()!,
                a.GetProperty("size").GetInt64(),
                root.TryGetProperty("published_at", out var p) && p.TryGetDateTime(out var d) ? d.ToLocalTime() : default);
        }
        return null; // sürüm var ama Wingry.exe eklenmemiş
    }

    public static DateTime LastCheck =>
        DateTime.TryParse(Settings.GetString("guncelleme_son_kontrol", ""), null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : default;

    /// <summary>İndirir; ilerleme 0–1. Dönüş: indirilen dosyanın yolu.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct)
    {
        var target = Path.Combine(Paths.Temp, $"Wingry-{info.Version}.exe");
        var part = target + ".part";
        using (var resp = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? info.Size;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(part);
            var buf = new byte[1 << 16];
            long done = 0; int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress?.Report((double)done / total);
            }
        }
        if (info.Size > 0 && new FileInfo(part).Length != info.Size)
            throw new IOException("İndirilen dosya eksik; tekrar deneyin.");
        File.Move(part, target, true);
        return target;
    }

    /// <summary>İndirilen sürümü yerine koyar, başlatıcıyı çalıştırır ve bu süreci kapatır.</summary>
    public static void InstallAndRestart(string downloaded)
    {
        string run = downloaded;
        if (LauncherPath is { } launcher)
        {
            try
            {
                File.Copy(downloaded, launcher, overwrite: true); // masaüstündeki exe de yeni sürüm olsun
                run = launcher;
            }
            catch (Exception ex) { Log.Error("Güncelleme: başlatıcı değiştirilemedi", ex); }
        }
        Log.Write($"Wingry güncelleniyor: {Current} → {run}");
        Process.Start(new ProcessStartInfo(run) { UseShellExecute = false, ArgumentList = { "--bekle", Environment.ProcessId.ToString() } });
        Environment.Exit(0);
    }
}
