using Microsoft.Win32;
using Wingry.Core;

namespace Wingry.Services;

public static class SystemInfo
{
    public static string OsName { get; } = ReadOs();
    public static string CpuName { get; } = ReadCpu();
    public static string Machine { get; } = Environment.MachineName;
    public static string User { get; } = Environment.UserName;

    public static TimeSpan Uptime => TimeSpan.FromMilliseconds(Native.GetTickCount64());

    private static string ReadOs()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var name = k?.GetValue("ProductName") as string ?? "Windows";
            int build = Environment.OSVersion.Version.Build;
            if (build >= 22000) name = name.Replace("Windows 10", "Windows 11");
            var ver = k?.GetValue("DisplayVersion") as string ?? "";
            var ubr = k?.GetValue("UBR") is int u ? $".{u}" : "";
            return $"{name} {ver} (derleme {build}{ubr})".Trim();
        }
        catch { return Environment.OSVersion.VersionString; }
    }

    private static string ReadCpu()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return ((k?.GetValue("ProcessorNameString") as string) ?? "İşlemci").Trim() + $" · {Environment.ProcessorCount} iş parçacığı";
        }
        catch { return "İşlemci"; }
    }

    public static IEnumerable<DriveInfo> FixedDrives() =>
        DriveInfo.GetDrives().Where(d => { try { return d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable; } catch { return false; } });
}
