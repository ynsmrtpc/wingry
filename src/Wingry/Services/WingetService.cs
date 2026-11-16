using Microsoft.Management.Deployment;
using Wingry.Core;
using Windows.Foundation;

namespace Wingry.Services;

/// <summary>Arayüzden bağımsız paket özeti. COM nesnesi <see cref="Native"/> ile taşınır.</summary>
public sealed class PackageInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string InstalledVersion { get; init; } = "";
    public string AvailableVersion { get; set; } = "";
    public string Publisher { get; init; } = "";
    public string Source { get; init; } = "";
    public bool IsInstalled => InstalledVersion.Length > 0;
    public bool HasUpdate { get; set; }
    internal CatalogPackage? Native { get; init; }
}

public enum OpKind { Install, Upgrade, Uninstall }

public sealed record OpProgress(string Stage, double Percent, bool Indeterminate);

public sealed record OpResult(bool Ok, string Message, bool RebootRequired = false);

/// <summary>
/// winget'in resmî COM API'si (Microsoft.Management.Deployment). Metin çıktısı ayrıştırılmaz:
/// dil, sütun genişliği ve "…" kırpması sorunları yok. Yüklü liste ~1 sn, güncelleme taraması
/// paralel ~0,3 sn.
/// </summary>
public static class WingetService
{
    private static PackageManager? _pm;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static PackageCatalog? _remote;   // winget kataloğu (arama/kurulum)
    private static PackageCatalog? _searchCat; // yüklü durumunu da bilen birleşik arama kataloğu
    private static readonly object CatGate = new();

    /// <summary>Kurulum/kaldırmadan sonra arama kataloğu yeniden bağlansın (yüklü bilgisi tazelensin).</summary>
    public static void InvalidateSearch() { lock (CatGate) _searchCat = null; }

    private static PackageCatalog SearchCatalog()
    {
        lock (CatGate) return _searchCat ??= Remote();
    }

    /// <summary>Açılışta arka planda çağrılır: ilk aramayı beklemesin.</summary>
    public static Task WarmUpSearchAsync() => Task.Run(() => { try { SearchCatalog(); } catch { } });

    public static bool Available { get; private set; } = true;
    public static string? LastError { get; private set; }

    private static PackageManager Pm => _pm ??= new PackageManager();

    /// <summary>
    /// winget'in arka plan sunucusu yeniden başlarsa (ör. App Installer güncellenince) eldeki
    /// COM bağlantıları ölür ve her çağrı "Arabirim desteklenmiyor" verir. Bağlantıları
    /// bırakıp bir kez daha dener.
    /// </summary>
    private static T Retry<T>(Func<T> f)
    {
        try { return f(); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException)
        {
            Log.Write("winget bağlantısı yenileniyor: " + ex.Message);
            lock (CatGate) { _pm = null; _remote = null; _searchCat = null; }
            return f();
        }
    }

    private static PackageCatalogReference[] Catalogs(bool includeStore)
    {
        var list = Pm.GetPackageCatalogs();
        var r = new List<PackageCatalogReference>();
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            var name = c.Info.Name;
            if (name == "winget" || (includeStore && name == "msstore")) r.Add(c);
        }
        return r.ToArray();
    }

    private static PackageCatalog ConnectComposite(CompositeSearchBehavior behavior, bool includeStore)
    {
        var opts = new CreateCompositePackageCatalogOptions { CompositeSearchBehavior = behavior };
        foreach (var c in Catalogs(includeStore)) opts.Catalogs.Add(c);
        var res = Pm.CreateCompositePackageCatalog(opts).Connect();
        if (res.Status != ConnectResultStatus.Ok) throw new InvalidOperationException($"Katalog bağlantısı başarısız: {res.Status}");
        return res.PackageCatalog;
    }

    private static PackageCatalog Remote()
    {
        if (_remote is not null) return _remote;
        var refs = Catalogs(false);
        if (refs.Length == 0) throw new InvalidOperationException("winget kaynağı bulunamadı.");
        var res = refs[0].Connect();
        if (res.Status != ConnectResultStatus.Ok) throw new InvalidOperationException($"winget kaynağına bağlanılamadı: {res.Status}");
        return _remote = res.PackageCatalog;
    }

    private static PackageInfo ToInfo(CatalogPackage p, bool checkUpdate)
    {
        var inst = p.InstalledVersion;
        var def = p.DefaultInstallVersion;
        bool upd = checkUpdate && inst is not null && SafeBool(() => p.IsUpdateAvailable);
        string src = "";
        try { src = def?.PackageCatalog?.Info?.Name ?? ""; } catch { }
        if (src.Length == 0)
        {
            var id = p.Id;
            src = id.StartsWith("MSIX\\", StringComparison.OrdinalIgnoreCase) ? "MSIX" :
                  id.StartsWith("ARP\\", StringComparison.OrdinalIgnoreCase) ? "Yerel" : "";
        }
        return new PackageInfo
        {
            Id = p.Id,
            Name = p.Name,
            InstalledVersion = inst?.Version ?? "",
            AvailableVersion = upd ? def?.Version ?? "" : (inst is null ? def?.Version ?? "" : ""),
            Publisher = SafeStr(() => inst?.Publisher ?? def?.Publisher ?? ""),
            Source = src,
            HasUpdate = upd,
            Native = p,
        };
    }

    private static bool SafeBool(Func<bool> f) { try { return f(); } catch { return false; } }
    private static string SafeStr(Func<string> f) { try { return f(); } catch { return ""; } }

    /// <summary>Yüklü paketler + güncelleme bilgisi (paralel).</summary>
    public static async Task<List<PackageInfo>> GetInstalledAsync(CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            return await Task.Run(() => Retry(() =>
            {
                var cat = ConnectComposite(CompositeSearchBehavior.LocalCatalogs, Settings.GetBool(Settings.IncludeStore, false));
                var res = cat.FindPackages(new FindPackagesOptions());
                int n = res.Matches.Count;
                var pk = new CatalogPackage[n];
                for (int i = 0; i < n; i++) pk[i] = res.Matches[i].CatalogPackage;
                var outp = new PackageInfo[n];
                Parallel.For(0, n, new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = ct },
                    i => outp[i] = ToInfo(pk[i], true));
                Available = true; LastError = null;
                return outp.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            }), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Available = false; LastError = ex.Message;
            Log.Error("Winget.GetInstalled", ex);
            throw;
        }
        finally { Gate.Release(); }
    }

    /// <summary>
    /// winget deposunda arama: ad, kimlik, takma ad, etiket. Doğrudan winget kataloğu kullanılır
    /// (~50 ms). Birleşik katalog her sonucu yerel kayıtlarla eşleştirdiği için "python" gibi
    /// aramalarda 5 sn sürüyordu; yüklü/güncelleme bilgisi bunun yerine bellekteki yüklü
    /// listeden kimlikle eşlenir.
    /// </summary>
    public static Task<List<PackageInfo>> SearchAsync(string query, int limit = 60, CancellationToken ct = default, Action? lateVersions = null) =>
        Task.Run(() =>
        {
            var opts = new FindPackagesOptions { ResultLimit = (uint)limit };
            opts.Selectors.Add(new PackageMatchFilter
            {
                Field = PackageMatchField.CatalogDefault,
                Option = PackageFieldMatchOption.ContainsCaseInsensitive,
                Value = query,
            });
            return Retry(() => FromRemote(SearchCatalog().FindPackages(opts), ct, lateVersions));
        }, ct);

    /// <summary>Kimliğe göre tam eşleşme (hazır paketler için).</summary>
    public static Task<PackageInfo?> FindByIdAsync(string id) =>
        Task.Run(() =>
        {
            var opts = new FindPackagesOptions { ResultLimit = 1 };
            opts.Selectors.Add(new PackageMatchFilter { Field = PackageMatchField.Id, Option = PackageFieldMatchOption.EqualsCaseInsensitive, Value = id });
            return Retry(() => FromRemote(SearchCatalog().FindPackages(opts), default).FirstOrDefault());
        });

    private static List<PackageInfo> FromRemote(FindPackagesResult res, CancellationToken ct, Action? lateVersions = null)
    {
        int n = res.Matches.Count;
        var pk = new CatalogPackage[n];
        for (int i = 0; i < n; i++) pk[i] = res.Matches[i].CatalogPackage;
        var installed = AppState.Installed.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var outp = new PackageInfo[n];
        for (int i = 0; i < n; i++)
        {
            var p = pk[i];
            var id = p.Id;
            installed.TryGetValue(id, out var inst);
            outp[i] = new PackageInfo
            {
                Id = id,
                Name = p.Name,
                InstalledVersion = inst?.InstalledVersion ?? "",
                HasUpdate = inst?.HasUpdate ?? false,
                Publisher = inst?.Publisher ?? "",
                Source = "winget",
                Native = p,
            };
        }
        // Sürüm listesi bazı paketlerde saniyeler sürebiliyor ("zip" aramasında 10 sn).
        // 400 ms bütçe: yetişmeyenler arka planda dolar ve lateVersions ile haber verilir.
        var versions = Task.Run(() => Parallel.For(0, n, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            try { var av = pk[i].AvailableVersions; if (av.Count > 0) outp[i].AvailableVersion = av[0].Version; } catch { }
        }));
        if (!versions.Wait(400, ct) && lateVersions is not null)
            versions.ContinueWith(_ => lateVersions(), TaskScheduler.Default);
        return outp.ToList();
    }
    public static async Task<OpResult> RunAsync(OpKind kind, PackageInfo pkg, IProgress<OpProgress>? progress, CancellationToken ct)
    {
        try
        {
            var native = pkg.Native;
            if (native is null)
            {
                var found = await FindByIdAsync(pkg.Id);
                native = found?.Native ?? throw new InvalidOperationException("Paket depoda bulunamadı.");
            }

            progress?.Report(new OpProgress("Sırada", 0, true));
            bool silent = Settings.GetBool(Settings.SilentInstall, true);

            if (kind == OpKind.Uninstall)
            {
                var uo = new UninstallOptions { PackageUninstallMode = silent ? PackageUninstallMode.Silent : PackageUninstallMode.Interactive };
                var op = Pm.UninstallPackageAsync(native, uo);
                op.Progress = (_, p) => progress?.Report(p.State switch
                {
                    PackageUninstallProgressState.Queued => new OpProgress("Sırada", 0, true),
                    PackageUninstallProgressState.Uninstalling => new OpProgress("Kaldırılıyor", p.UninstallationProgress * 100, p.UninstallationProgress <= 0),
                    _ => new OpProgress("Bitiriliyor", 100, true),
                });
                using var reg = ct.Register(op.Cancel);
                var r = await op;
                return r.Status == UninstallResultStatus.Ok
                    ? new OpResult(true, "Kaldırıldı", r.RebootRequired)
                    : new OpResult(false, Explain(r.Status.ToString(), r.ExtendedErrorCode));
            }

            var io = new InstallOptions
            {
                PackageInstallMode = silent ? PackageInstallMode.Silent : PackageInstallMode.Default,
                AcceptPackageAgreements = true,
                AllowUpgradeToUnknownVersion = true,
            };
            IAsyncOperationWithProgress<InstallResult, InstallProgress> iop =
                kind == OpKind.Upgrade ? Pm.UpgradePackageAsync(native, io) : Pm.InstallPackageAsync(native, io);
            iop.Progress = (_, p) => progress?.Report(p.State switch
            {
                PackageInstallProgressState.Queued => new OpProgress("Sırada", 0, true),
                PackageInstallProgressState.Downloading => new OpProgress(
                    p.BytesRequired > 0 ? $"İndiriliyor · {Format.Bytes((long)p.BytesDownloaded)} / {Format.Bytes((long)p.BytesRequired)}" : "İndiriliyor",
                    p.DownloadProgress * 100, p.DownloadProgress <= 0),
                PackageInstallProgressState.Installing => new OpProgress("Kuruluyor", p.InstallationProgress * 100, p.InstallationProgress <= 0),
                PackageInstallProgressState.PostInstall => new OpProgress("Tamamlanıyor", 100, true),
                _ => new OpProgress("Bitti", 100, false),
            });
            using (ct.Register(iop.Cancel))
            {
                var r = await iop;
                return r.Status == InstallResultStatus.Ok
                    ? new OpResult(true, kind == OpKind.Upgrade ? "Güncellendi" : "Kuruldu", r.RebootRequired)
                    : new OpResult(false, Explain(r.Status.ToString(), r.ExtendedErrorCode, r.InstallerErrorCode));
            }
        }
        catch (OperationCanceledException) { return new OpResult(false, "İptal edildi"); }
        catch (Exception ex)
        {
            Log.Error($"Winget.{kind} {pkg.Id}", ex);
            return new OpResult(false, ex.Message);
        }
    }

    private static string Explain(string status, Exception? ext, uint installerCode = 0)
    {
        var s = status switch
        {
            "BlockedByPolicy" => "İlke tarafından engellendi",
            "CatalogError" => "Katalog hatası",
            "InternalError" => "winget iç hatası",
            "InvalidOptions" => "Geçersiz seçenek",
            "DownloadError" => "İndirme başarısız",
            "InstallError" => "Kurulum başarısız",
            "UninstallError" => "Kaldırma başarısız",
            "ManifestError" => "Paket bildirimi hatalı",
            "NoApplicableInstallers" => "Bu sistem için kurulum dosyası yok",
            "NoApplicableUpgrade" => "Uygulanabilir güncelleme yok",
            "PackageAgreementsNotAccepted" => "Lisans sözleşmesi kabul edilmedi",
            _ => status,
        };
        if (installerCode != 0) s += $" (kurulum kodu {installerCode})";
        else if (ext is not null) s += $" (0x{ext.HResult:X8})";
        return s;
    }
}
