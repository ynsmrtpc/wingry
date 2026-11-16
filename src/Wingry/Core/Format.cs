using System.Globalization;

namespace Wingry.Core;

public static class Format
{
    public static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static string Bytes(long bytes)
    {
        if (bytes < 0) bytes = 0;
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{bytes} B" : v.ToString(v >= 100 ? "0" : v >= 10 ? "0.0" : "0.00", Tr) + " " + u[i];
    }

    public static string Bytes(ulong bytes) => Bytes((long)Math.Min(bytes, long.MaxValue));

    public static string Number(long n) => n.ToString("N0", Tr);

    public static string Percent(double v) => v.ToString("0", Tr) + "%";

    public static string Duration(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays} gün {t.Hours} sa" :
        t.TotalHours >= 1 ? $"{(int)t.TotalHours} sa {t.Minutes} dk" :
        $"{t.Minutes} dk";

    public static string Date(DateTime d) => d == default ? "—" : d.ToString("dd.MM.yyyy HH:mm", Tr);
}
