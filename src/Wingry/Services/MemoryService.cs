using System.Diagnostics;
using System.Runtime.InteropServices;
using Wingry.Core;

namespace Wingry.Services;

public readonly record struct MemorySnapshot(ulong Total, ulong Available, uint LoadPercent, ulong CommitTotal, ulong CommitLimit,
    ulong Standby, ulong Modified, ulong Free)
{
    public ulong Used => Total - Available;
}

public sealed record ProcessMemory(string Name, long WorkingSet, int Count);

public sealed record CleanResult(long FreedBytes, int Processes, bool Deep, string? Error);

public static class MemoryService
{
    public static MemorySnapshot Snapshot()
    {
        var m = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
        Native.GlobalMemoryStatusEx(ref m);
        var (standby, modified, free) = QueryLists();
        return new MemorySnapshot(m.ullTotalPhys, m.ullAvailPhys, m.dwMemoryLoad,
            m.ullTotalPageFile - m.ullAvailPageFile, m.ullTotalPageFile, standby, modified, free);
    }

    /// <summary>Bekleme (standby) / değiştirilmiş listeler. Ayrıcalık yoksa 0 döner.</summary>
    private static unsafe (ulong standby, ulong modified, ulong free) QueryLists()
    {
        // SYSTEM_MEMORY_LIST_INFORMATION: 5 + 8 + 8 + 1 = 22 adet ULONG_PTR
        const int n = 22;
        var buf = stackalloc nuint[n];
        int st = Native.NtQuerySystemInformation(Native.SystemMemoryListInformation, (nint)buf, n * sizeof(nuint), out _);
        if (st != 0) return (0, 0, 0);
        const ulong page = 4096;
        ulong standby = 0;
        for (int i = 0; i < 8; i++) standby += buf[5 + i];
        return (standby * page, (ulong)buf[2] * page, ((ulong)buf[0] + buf[1]) * page);
    }

    public static List<ProcessMemory> TopProcesses(int take = 12)
    {
        var map = new Dictionary<string, (long ws, int c)>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var name = p.ProcessName;
                var ws = p.WorkingSet64;
                map[name] = map.TryGetValue(name, out var e) ? (e.ws + ws, e.c + 1) : (ws, 1);
            }
            catch { }
            finally { p.Dispose(); }
        }
        return map.OrderByDescending(kv => kv.Value.ws).Take(take)
            .Select(kv => new ProcessMemory(kv.Key, kv.Value.ws, kv.Value.c)).ToList();
    }

    /// <summary>
    /// Hızlı temizlik: erişilebilen her sürecin çalışma kümesini boşaltır (yönetici gerekmez,
    /// yalnızca kendi süreçlerin). Derin temizlik: ayrıca bekleme listesi, değiştirilmiş liste
    /// ve dosya önbelleği — yönetici ister.
    /// </summary>
    public static CleanResult Clean(bool deep)
    {
        var before = Snapshot().Available;
        int count = 0;
        string? error = null;

        if (deep)
        {
            Native.EnablePrivilege("SeProfileSingleProcessPrivilege");
            Native.EnablePrivilege("SeIncreaseQuotaPrivilege");
            Native.EnablePrivilege("SeDebugPrivilege");
        }

        int self = Environment.ProcessId;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id is 0 or 4 || p.Id == self) continue;
                var h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION | Native.PROCESS_SET_QUOTA, false, p.Id);
                if (h == 0) continue;
                if (Native.EmptyWorkingSet(h)) count++;
                Native.CloseHandle(h);
            }
            catch { }
            finally { p.Dispose(); }
        }

        if (deep)
        {
            if (!Command(Native.MemoryEmptyWorkingSets)) error = "Çalışma kümeleri boşaltılamadı (yönetici gerekli).";
            Command(Native.MemoryFlushModifiedList);
            Command(Native.MemoryPurgeStandbyList);
            FlushFileCache();
        }

        Thread.Sleep(300); // sayaçların oturması için
        var after = Snapshot().Available;
        long freed = (long)after - (long)before;
        return new CleanResult(Math.Max(0, freed), count, deep, error);
    }

    private static unsafe bool Command(int cmd)
    {
        int c = cmd;
        return Native.NtSetSystemInformation(Native.SystemMemoryListInformation, (nint)(&c), sizeof(int)) == 0;
    }

    private static unsafe void FlushFileCache()
    {
        var info = new Native.SYSTEM_FILECACHE_INFORMATION
        {
            MinimumWorkingSet = unchecked((nuint)(-1)),
            MaximumWorkingSet = unchecked((nuint)(-1)),
        };
        Native.NtSetSystemInformation(Native.SystemFileCacheInformationEx, (nint)(&info), sizeof(Native.SYSTEM_FILECACHE_INFORMATION));
    }
}

/// <summary>CPU kullanımı: GetSystemTimes farkı. Her çağrıda son çağrıdan bu yana yüzdeyi verir.</summary>
public sealed class CpuMeter
{
    private ulong _idle, _total;

    public double Sample()
    {
        Native.GetSystemTimes(out var idle, out var kernel, out var user);
        ulong i = idle.Value, t = kernel.Value + user.Value;
        ulong di = i - _idle, dt = t - _total;
        _idle = i; _total = t;
        return dt == 0 ? 0 : Math.Clamp(100.0 * (dt - di) / dt, 0, 100);
    }
}
