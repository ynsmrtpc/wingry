using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Wingry.Services;

public sealed record DupGroup(long Size, List<FileEntry> Files)
{
    public long Wasted => Size * (Files.Count - 1);
}

public readonly record struct DupProgress(string Stage, int Done, int Total);

/// <summary>
/// Kopya dosya bulucu (son disk taramasının dosya listesi üzerinde):
///   1) aynı boyut → 2) ilk + son 16 KB özeti → 3) tam SHA-256. Yalnızca birebir aynı içerik gruplanır.
/// </summary>
public static class DuplicateFinder
{
    private const int Chunk = 16 * 1024;

    /// <summary>
    /// Sistem / program klasörleri: oradaki "kopyalar" çoğunlukla Windows'un sabit bağlantıları (WinSxS)
    /// ya da programların kendi dosyalarıdır; silmek sistemi bozar. Varsayılan olarak dışarıda.
    /// </summary>
    public static IReadOnlyList<string> SystemRoots { get; } =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    ];

    /// <summary>Dosyanın klasörü sistem köklerinden birinin altında mı? Klasör başına bir kez hesaplanır.</summary>
    public static Func<FileEntry, bool> NotInSystem()
    {
        var cache = new Dictionary<DirNode, bool>();
        var roots = SystemRoots.Where(r => r.Length > 0).Select(r => r.TrimEnd('\\') + '\\').ToArray();
        return f =>
        {
            lock (cache)
            {
                if (cache.TryGetValue(f.Dir, out var ok)) return ok;
                var p = f.Dir.FullPath.TrimEnd('\\') + '\\';
                ok = !roots.Any(r => p.StartsWith(r, StringComparison.OrdinalIgnoreCase));
                cache[f.Dir] = ok;
                return ok;
            }
        };
    }

    public static Task<List<DupGroup>> FindAsync(IReadOnlyList<FileEntry> files, long minSize, IProgress<DupProgress>? progress, CancellationToken ct) =>
        Task.Run(() =>
        {
            var bySize = files.Where(f => f.Size >= minSize)
                .GroupBy(f => f.Size).Where(g => g.Count() > 1).Select(g => g.ToList()).ToList();
            int total = bySize.Sum(g => g.Count);
            int done = 0;
            progress?.Report(new DupProgress("Dosya başları karşılaştırılıyor", 0, total));

            // 2) kısmi özet
            var partial = new ConcurrentBag<List<FileEntry>>();
            Parallel.ForEach(bySize, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, g =>
            {
                var map = new Dictionary<string, List<FileEntry>>();
                foreach (var f in g)
                {
                    var h = Hash(f.FullPath, partialOnly: true);
                    if (h is not null) { if (!map.TryGetValue(h, out var l)) map[h] = l = []; l.Add(f); }
                    var d = Interlocked.Increment(ref done);
                    if (d % 50 == 0) progress?.Report(new DupProgress("Dosya başları karşılaştırılıyor", d, total));
                }
                foreach (var l in map.Values) if (l.Count > 1) partial.Add(l);
            });

            // 3) tam özet (büyükten küçüğe, en çok yer açacaklar önce)
            var cands = partial.OrderByDescending(l => l[0].Size * (l.Count - 1)).ToList();
            total = cands.Sum(l => l.Count); done = 0;
            var result = new ConcurrentBag<DupGroup>();
            Parallel.ForEach(cands, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = ct }, l =>
            {
                var map = new Dictionary<string, List<FileEntry>>();
                foreach (var f in l)
                {
                    ct.ThrowIfCancellationRequested();
                    var h = f.Size <= 2L * Chunk ? Hash(f.FullPath, true) : Hash(f.FullPath, false);
                    if (h is not null) { if (!map.TryGetValue(h, out var x)) map[h] = x = []; x.Add(f); }
                    var d = Interlocked.Increment(ref done);
                    progress?.Report(new DupProgress("İçerikler doğrulanıyor", d, total));
                }
                foreach (var x in map.Values)
                    if (x.Count > 1) result.Add(new DupGroup(x[0].Size, x.OrderBy(f => f.ModifiedTicks).ToList()));
            });
            return result.OrderByDescending(g => g.Wasted).ToList();
        }, ct);

    private static string? Hash(string path, bool partialOnly)
    {
        try
        {
            // Kısmi özette tampon yok (tek okuma); tam özette 1 MB sıralı okuma
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, partialOnly ? 1 : 1 << 20, partialOnly ? FileOptions.None : FileOptions.SequentialScan);
            if (!partialOnly) return Convert.ToHexString(SHA256.HashData(fs));
            var buf = new byte[Chunk * 2];
            int n = fs.Read(buf, 0, Chunk);
            if (fs.Length > Chunk * 2) { fs.Seek(-Chunk, SeekOrigin.End); n += fs.Read(buf, Chunk, Chunk); }
            else n += fs.Read(buf, n, buf.Length - n);
            return Convert.ToHexString(SHA256.HashData(buf.AsSpan(0, n)));
        }
        catch { return null; } // kilitli / erişilemeyen dosya: atla
    }
}
