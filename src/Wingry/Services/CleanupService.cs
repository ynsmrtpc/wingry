using Wingry.Core;

namespace Wingry.Services;

public sealed class CleanupTarget : Bindable
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Glyph { get; init; }
    public bool NeedsAdmin { get; init; }

    private long _size = -1;
    public long Size { get => _size; set { if (Set(ref _size, value)) Raise(nameof(SizeText)); } }
    public string SizeText => Size < 0 ? "Hesaplanıyor…" : Format.Bytes(Size);

    private bool _selected = true;
    public bool Selected { get => _selected; set => Set(ref _selected, value); }
}

/// <summary>Güvenli disk temizliği: yalnızca önbellek/geçici konumlar. Son 24 saatte değişen dosyalara dokunulmaz.</summary>
public static class CleanupService
{
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string Win => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    public static List<CleanupTarget> Targets() =>
    [
        new() { Id = "usertemp", Title = "Geçici dosyalar", Description = "Uygulamaların %TEMP% klasörüne bıraktığı dosyalar", Glyph = "\uE7C3" },
        new() { Id = "recycle", Title = "Geri Dönüşüm Kutusu", Description = "Silinmiş ve kutuda bekleyen dosyalar", Glyph = "\uE74D" },
        new() { Id = "crash", Title = "Çökme dökümleri", Description = "Kullanıcı uygulamalarının hata dökümleri (CrashDumps)", Glyph = "\uEBE8" },
        new() { Id = "wintemp", Title = "Windows geçici dosyaları", Description = @"C:\Windows\Temp", Glyph = "\uE770", NeedsAdmin = true },
        new() { Id = "wu", Title = "Windows Update önbelleği", Description = "İndirilip kurulmuş güncelleme dosyaları", Glyph = "\uE895", NeedsAdmin = true },
        new() { Id = "wer", Title = "Hata raporları", Description = "Windows Hata Bildirimi arşivi ve kuyruğu", Glyph = "\uE7BA", NeedsAdmin = true },
        new() { Id = "do", Title = "Teslim İyileştirme önbelleği", Description = "Diğer bilgisayarlarla paylaşılan güncelleme parçaları", Glyph = "\uE704", NeedsAdmin = true },
    ];

    private static string[] Dirs(string id) => id switch
    {
        "usertemp" => [Path.GetTempPath()],
        "crash" => [Path.Combine(Local, "CrashDumps")],
        "wintemp" => [Path.Combine(Win, "Temp")],
        "wu" => [Path.Combine(Win, "SoftwareDistribution", "Download")],
        "wer" => [Path.Combine(ProgramData, "Microsoft", "Windows", "WER", "ReportArchive"), Path.Combine(ProgramData, "Microsoft", "Windows", "WER", "ReportQueue")],
        "do" => [Path.Combine(Win, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache")],
        _ => [],
    };

    public static long Measure(string id)
    {
        if (id == "recycle")
        {
            var info = new Native.SHQUERYRBINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.SHQUERYRBINFO>() };
            return Native.SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
        }
        long total = 0;
        foreach (var d in Dirs(id))
        {
            if (!Directory.Exists(d)) continue;
            try
            {
                foreach (var f in new DirectoryInfo(d).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                    total += f.Length;
            }
            catch { }
        }
        return total;
    }

    /// <summary>Kullanıcı düzeyindeki hedefi temizler. Yönetici hedefleri <see cref="CleanAdminTarget"/> ile işçide.</summary>
    public static long Clean(string id)
    {
        if (id == "recycle")
        {
            var before = Measure("recycle");
            Native.SHEmptyRecycleBin(0, null, 0x1 | 0x2 | 0x4); // onay/ilerleme/ses yok
            return before;
        }
        long freed = 0;
        foreach (var d in Dirs(id)) freed += DeleteContents(d);
        return freed;
    }

    public static long CleanAdminTarget(string id) => id is "wintemp" or "wu" or "wer" or "do" ? Clean(id) : 0;

    private static long DeleteContents(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        long freed = 0;
        var cutoff = DateTime.UtcNow.AddHours(-24);
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", opts))
        {
            try
            {
                if (f.LastWriteTimeUtc > cutoff) continue;
                long len = f.Length;
                if ((f.Attributes & FileAttributes.ReadOnly) != 0) f.Attributes &= ~FileAttributes.ReadOnly;
                f.Delete();
                freed += len;
            }
            catch { /* kullanımda: atla */ }
        }
        // Boş kalan alt klasörleri kaldır (kökü değil)
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(dir, "*", opts).OrderByDescending(s => s.Length))
                try { if (!Directory.EnumerateFileSystemEntries(sub).Any()) Directory.Delete(sub); } catch { }
        }
        catch { }
        return freed;
    }
}
