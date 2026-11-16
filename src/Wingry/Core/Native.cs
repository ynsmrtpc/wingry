using System.Runtime.InteropServices;

namespace Wingry.Core;

internal static partial class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [StructLayout(LayoutKind.Sequential)]
    public struct FILETIME { public uint Low; public uint High; public readonly ulong Value => ((ulong)High << 32) | Low; }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint PROCESS_SET_QUOTA = 0x0100;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint h);

    [LibraryImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EmptyWorkingSet(nint hProcess);

    // ---- ntdll: bellek listeleri (yönetici + SeProfileSingleProcessPrivilege)
    public const int SystemFileCacheInformationEx = 81;
    public const int SystemMemoryListInformation = 80;
    public const int MemoryEmptyWorkingSets = 2;
    public const int MemoryFlushModifiedList = 3;
    public const int MemoryPurgeStandbyList = 4;
    public const int MemoryPurgeLowPriorityStandbyList = 5;

    [LibraryImport("ntdll.dll")]
    public static partial int NtSetSystemInformation(int infoClass, nint info, int length);

    [LibraryImport("ntdll.dll")]
    public static partial int NtQuerySystemInformation(int infoClass, nint info, int length, out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_FILECACHE_INFORMATION
    {
        public nuint CurrentSize;
        public nuint PeakSize;
        public uint PageFaultCount;
        public nuint MinimumWorkingSet;
        public nuint MaximumWorkingSet;
        public nuint CurrentSizeIncludingTransitionInPages;
        public nuint PeakSizeIncludingTransitionInPages;
        public uint TransitionRePurposeCount;
        public uint Flags;
    }

    // ---- ayrıcalık
    [StructLayout(LayoutKind.Sequential)]
    public struct LUID { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_PRIVILEGES { public uint Count; public LUID Luid; public uint Attributes; }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LookupPrivilegeValue(string? system, string name, out LUID luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AdjustTokenPrivileges(nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll,
        ref TOKEN_PRIVILEGES state, int len, nint prev, nint retLen);

    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();

    public static bool EnablePrivilege(string name)
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out var tok)) return false;
        try
        {
            if (!LookupPrivilegeValue(null, name, out var luid)) return false;
            var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = 2 };
            return AdjustTokenPrivileges(tok, false, ref tp, 0, 0, 0) && Marshal.GetLastPInvokeError() == 0;
        }
        finally { CloseHandle(tok); }
    }

    // ---- kabuk
    [LibraryImport("shell32.dll", EntryPoint = "SHEmptyRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHEmptyRecycleBin(nint hwnd, string? root, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    public struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

    [LibraryImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHQueryRecycleBin(string? root, ref SHQUERYRBINFO info);

    [LibraryImport("kernel32.dll")]
    public static partial ulong GetTickCount64();
}
