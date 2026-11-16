using System.Collections.Concurrent;
using System.IO.Enumeration;
using System.Runtime;
using System.Runtime.InteropServices;
using Wingry.Core;

namespace Wingry.Services;

public enum FileKind : byte { Video, Audio, Image, Document, Archive, Program, DiskImage, Code, System, Other }

/// <summary>Klasör düğümü. Tam yol önbelleğe alınmaz (573 bin klasörde ~100 MB tutuyordu), istenince üretilir.</summary>
public sealed class DirNode
{
    private static readonly List<DirNode> None = [];

    public required string Name { get; init; }
    public DirNode? Parent { get; init; }
    private List<DirNode>? _children;
    public List<DirNode> Children => _children ?? None;
    public long OwnSize;     // yalnız bu klasördeki dosyalar
    public long Size;        // alt klasörlerle birlikte
    public int OwnFiles;
    public int Files;
    public bool Denied;

    internal void AddChild(DirNode c) => (_children ??= new List<DirNode>(4)).Add(c);

    public string FullPath
    {
        get
        {
            if (Parent is null) return Name;
            var parts = new Stack<string>();
            DirNode? d = this;
            while (d!.Parent is not null) { parts.Push(d.Name); d = d.Parent; }
            return Path.Combine(d.Name, string.Join('\\', parts));
        }
    }
}

/// <summary>32 bayt: ad, klasör, boyut, değişiklik zamanı (2000'den beri saniye), tür.</summary>
public readonly struct FileEntry
{
    private static readonly long Epoch = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

    public FileEntry(string name, DirNode dir, long size, long modifiedTicks, FileKind kind)
    {
        Name = name; Dir = dir; Size = size; Kind = kind;
        _mod = (uint)Math.Clamp((modifiedTicks - Epoch) / TimeSpan.TicksPerSecond, 0, uint.MaxValue);
    }

    private readonly uint _mod;
    public string Name { get; }
    public DirNode Dir { get; }
    public long Size { get; }
    public FileKind Kind { get; }
    public long ModifiedTicks => Epoch + _mod * TimeSpan.TicksPerSecond;
    public string FullPath => Path.Combine(Dir.FullPath, Name);
}

public sealed class ScanResult
{
    public required DirNode Root { get; init; }
    public required FileEntry[] Files { get; init; }      // boyuta göre büyükten küçüğe sıralı
    public int Directories { get; init; }
    public int DeniedDirectories { get; init; }
    public TimeSpan Elapsed { get; init; }
    public long TotalFiles { get; init; }                 // listelenmeyen küçükler dahil
    public long UnlistedFiles { get; init; }              // eşik altında kalıp tek tek tutulmayanlar
    public long UnlistedBytes { get; init; }
    public long ListThreshold { get; init; }
    public required long[] KindBytes { get; init; }        // tüm dosyalar üzerinden
    public required long[] KindCounts { get; init; }
}

public readonly record struct ScanProgress(long Files, long Bytes, int Directories, string Current);

/// <summary>
/// Çok iş parçacıklı dizin tarayıcı. .NET'in FileSystemEnumerable'ı NtQueryDirectoryFile'ı
/// doğrudan kullanır (FindFirstFile'dan hızlı); dizinler bir iş kuyruğunda paylaşılır.
/// Bağlantı noktaları (junction/symlink) izlenmez, bulut yer tutucuları disk alanı saymaz.
/// Bellek: liste 500 bin dosyayı geçince 16 KB altı dosyalar tek tek tutulmaz, yalnızca
/// klasör ve tür toplamlarına girer (2,6 milyon dosyalık C: taramasında ~1,3 GB → birkaç yüz MB).
/// </summary>
public static class DiskService
{
    public const int ListLimitBeforeThreshold = 500_000;
    public const long SmallFileThreshold = 16 * 1024;

    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;

    private static readonly EnumerationOptions Opts = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = 0,
        BufferSize = 64 * 1024,
    };

    private static readonly Dictionary<string, FileKind> Kinds = BuildKinds();
    private static readonly Dictionary<string, FileKind>.AlternateLookup<ReadOnlySpan<char>> KindLookup = Kinds.GetAlternateLookup<ReadOnlySpan<char>>();

    private static Dictionary<string, FileKind> BuildKinds()
    {
        var d = new Dictionary<string, FileKind>(StringComparer.OrdinalIgnoreCase);
        void Add(FileKind k, string exts) { foreach (var e in exts.Split(' ')) d["." + e] = k; }
        Add(FileKind.Video, "mp4 mkv avi mov wmv flv webm m4v mpg mpeg ts m2ts 3gp vob");
        Add(FileKind.Audio, "mp3 wav flac aac ogg wma m4a opus aiff mid");
        Add(FileKind.Image, "jpg jpeg png gif bmp tiff tif webp heic heif raw cr2 nef arw psd svg ico avif");
        Add(FileKind.Document, "pdf doc docx xls xlsx ppt pptx odt ods odp txt rtf csv md epub xps");
        Add(FileKind.Archive, "zip rar 7z tar gz bz2 xz zst cab tgz");
        Add(FileKind.Program, "exe msi msix msixbundle appx appxbundle dll");
        Add(FileKind.DiskImage, "iso img vhd vhdx vmdk vdi qcow2 wim esd");
        Add(FileKind.Code, "cs js ts py java cpp c h php html css json xml sql go rs");
        Add(FileKind.System, "sys tmp log bak dmp etl old cache db");
        return d;
    }

    public static FileKind KindOf(ReadOnlySpan<char> name)
    {
        int dot = name.LastIndexOf('.');
        return dot >= 0 && KindLookup.TryGetValue(name[dot..], out var k) ? k : FileKind.Other;
    }

    public static string KindLabel(FileKind k) => k switch
    {
        FileKind.Video => "Video",
        FileKind.Audio => "Ses",
        FileKind.Image => "Resim",
        FileKind.Document => "Belge",
        FileKind.Archive => "Arşiv",
        FileKind.Program => "Program",
        FileKind.DiskImage => "Disk kalıbı",
        FileKind.Code => "Kod",
        FileKind.System => "Sistem / geçici",
        _ => "Diğer",
    };

    private readonly struct RawEntry(string? name, bool isDir, long size, long ticks, FileKind kind)
    {
        public readonly string? Name = name;   // küçük dosyalarda null (ad belleğe alınmaz)
        public readonly bool IsDir = isDir;
        public readonly long Size = size;
        public readonly long Ticks = ticks;
        public readonly FileKind Kind = kind;
    }

    public static Task<ScanResult> ScanAsync(string rootPath, IProgress<ScanProgress>? progress, CancellationToken ct) =>
        Task.Factory.StartNew(() => Scan(rootPath, progress, ct), ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private sealed class WorkerState
    {
        public readonly List<FileEntry> Files = new(8192);
        public readonly long[] KindBytes = new long[10];
        public readonly long[] KindCounts = new long[10];
        public long UnlistedFiles, UnlistedBytes;
    }

    private static ScanResult Scan(string rootPath, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        rootPath = Path.GetFullPath(rootPath);
        if (rootPath.Length > 3) rootPath = rootPath.TrimEnd('\\');
        var root = new DirNode { Name = rootPath };
        var queue = new ConcurrentQueue<(DirNode node, string path)>();
        queue.Enqueue((root, rootPath));
        int pending = 1, dirs = 0, denied = 0;
        long files = 0, bytes = 0, listed = 0;
        long threshold = 0;   // 0 = her dosya listelenir
        string current = rootPath;
        var states = new ConcurrentBag<WorkerState>();

        int workers = Math.Clamp(Environment.ProcessorCount, 4, 16);
        var threads = new Thread[workers];
        for (int w = 0; w < workers; w++)
        {
            threads[w] = new Thread(() =>
            {
                var st = new WorkerState();
                states.Add(st);
                var spin = new SpinWait();
                while (Volatile.Read(ref pending) > 0 && !ct.IsCancellationRequested)
                {
                    if (!queue.TryDequeue(out var item)) { spin.SpinOnce(); continue; }
                    spin.Reset();
                    var (dir, path) = item;
                    try
                    {
                        current = path;
                        long th = Volatile.Read(ref threshold);
                        var en = new FileSystemEnumerable<RawEntry>(path, (ref FileSystemEntry e) =>
                        {
                            if (e.IsDirectory) return new RawEntry(e.FileName.ToString(), true, 0, 0, FileKind.Other);
                            var a = e.Attributes;
                            long size = (a & (RecallOnDataAccess | RecallOnOpen | FileAttributes.Offline)) == 0 ? e.Length : 0;
                            var kind = KindOf(e.FileName);
                            // Eşik altındaki dosyanın adı için dize bile üretilmez
                            string? name = size >= th ? e.FileName.ToString() : null;
                            return new RawEntry(name, false, size, e.LastWriteTimeUtc.UtcTicks, kind);
                        }, Opts)
                        {
                            // Bağlantı noktası olan klasörleri atla (döngü ve çift sayım olmasın)
                            ShouldIncludePredicate = (ref FileSystemEntry e) => !(e.IsDirectory && (e.Attributes & FileAttributes.ReparsePoint) != 0),
                        };
                        foreach (var r in en)
                        {
                            if (r.IsDir)
                            {
                                var child = new DirNode { Name = r.Name!, Parent = dir };
                                dir.AddChild(child);
                                Interlocked.Increment(ref pending);
                                queue.Enqueue((child, Path.Join(path, r.Name)));
                                continue;
                            }
                            dir.OwnSize += r.Size;
                            dir.OwnFiles++;
                            st.KindBytes[(int)r.Kind] += r.Size;
                            st.KindCounts[(int)r.Kind]++;
                            if (r.Name is not null)
                            {
                                st.Files.Add(new FileEntry(r.Name, dir, r.Size, r.Ticks, r.Kind));
                                if (Interlocked.Increment(ref listed) == ListLimitBeforeThreshold)
                                    Volatile.Write(ref threshold, SmallFileThreshold);
                            }
                            else { st.UnlistedFiles++; st.UnlistedBytes += r.Size; }
                            Interlocked.Increment(ref files);
                            Interlocked.Add(ref bytes, r.Size);
                        }
                        Interlocked.Increment(ref dirs);
                    }
                    catch (Exception) { dir.Denied = true; Interlocked.Increment(ref denied); }
                    finally { Interlocked.Decrement(ref pending); }
                }
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Wingry tarayıcı" };
            threads[w].Start();
        }

        while (threads.Any(t => t.IsAlive))
        {
            progress?.Report(new ScanProgress(Interlocked.Read(ref files), Interlocked.Read(ref bytes), Volatile.Read(ref dirs), current));
            Thread.Sleep(120);
        }
        ct.ThrowIfCancellationRequested();

        Aggregate(root);

        long th = Volatile.Read(ref threshold);
        long unlistedFiles = states.Sum(s => s.UnlistedFiles), unlistedBytes = states.Sum(s => s.UnlistedBytes);
        // Eşik devreye girmeden önce listelenmiş küçük dosyaları da ayıkla: liste tutarlı olsun
        int total = 0;
        foreach (var s in states)
            foreach (var f in s.Files)
                if (f.Size >= th) total++; else { unlistedFiles++; unlistedBytes += f.Size; }
        var all = new FileEntry[total];
        int pos = 0;
        foreach (var s in states)
        {
            foreach (var f in s.Files) if (f.Size >= th) all[pos++] = f;
            s.Files.Clear(); s.Files.TrimExcess();
        }
        var keys = new long[total];
        for (int i = 0; i < total; i++) keys[i] = -all[i].Size;
        Array.Sort(keys, all);

        var kb = new long[10]; var kc = new long[10];
        foreach (var s in states) for (int i = 0; i < 10; i++) { kb[i] += s.KindBytes[i]; kc[i] += s.KindCounts[i]; }

        var result = new ScanResult
        {
            Root = root, Files = all, Directories = dirs, DeniedDirectories = denied, Elapsed = sw.Elapsed,
            TotalFiles = files, UnlistedFiles = unlistedFiles, UnlistedBytes = unlistedBytes, ListThreshold = th,
            KindBytes = kb, KindCounts = kc,
        };
        states.Clear();
        ReleaseMemory();
        return result;
    }

    /// <summary>Geçici tarama belleğini işletim sistemine geri ver.</summary>
    public static void ReleaseMemory()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
    }

    /// <summary>Alt klasör boyutlarını aşağıdan yukarı toplar (yinelemesiz, derin ağaçlarda yığın taşmasın).</summary>
    private static void Aggregate(DirNode root)
    {
        var order = new List<DirNode>();
        var stack = new Stack<DirNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            order.Add(d);
            foreach (var c in d.Children) stack.Push(c);
        }
        for (int i = order.Count - 1; i >= 0; i--)
        {
            var d = order[i];
            d.Size = d.OwnSize; d.Files = d.OwnFiles;
            foreach (var c in d.Children) { d.Size += c.Size; d.Files += c.Files; }
            if (d.Children.Count > 1) d.Children.Sort((a, b) => b.Size.CompareTo(a.Size));
        }
    }

    // ---------------------------------------------------------------- dosya işlemleri

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public nint hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public nint hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    /// <summary>Geri Dönüşüm Kutusu'na gönderir (geri alınabilir).</summary>
    public static bool Recycle(IEnumerable<string> paths, nint owner)
    {
        var from = string.Join('\0', paths) + "\0\0";
        var op = new SHFILEOPSTRUCT
        {
            hwnd = owner,
            wFunc = 3,              // FO_DELETE
            pFrom = from,
            // FOF_ALLOWUNDO | FOF_NOCONFIRMATION — onayı biz soruyoruz, hata iletileri görünür kalır
            fFlags = 0x0040 | 0x0010,
        };
        return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
    }

    /// <summary>Kopya grupları: grup başlangıcındaki kutu korunur, diğerleri işaretlenir.</summary>
    public static void KeepFirstOfGroups(IList<Microsoft.UI.Xaml.Controls.CheckBox> boxes, ISet<int> groupStarts)
    {
        for (int i = 0; i < boxes.Count; i++) if (boxes[i].IsEnabled) boxes[i].IsChecked = !groupStarts.Contains(i);
    }

    public static void ShowInExplorer(string path) =>
        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");

    public static void OpenFolder(string path) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
}
