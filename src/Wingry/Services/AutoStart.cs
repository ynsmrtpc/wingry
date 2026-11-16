using Microsoft.Win32;
using Wingry.Core;

namespace Wingry.Services;

/// <summary>
/// "Windows açılınca başlat": HKCU\...\Run\Wingry. Görev Yöneticisi > Başlangıç'ta da görünür;
/// orada kapatılırsa (StartupApproved) burada da kapalı görünür. Yönetici gerekmez.
/// </summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string Name = "Wingry";
    public const string Arg = "--baslangic";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var run = Registry.CurrentUser.OpenSubKey(RunKey);
                if (run?.GetValue(Name) is not string) return false;
                using var ap = Registry.CurrentUser.OpenSubKey(ApprovedKey);
                return ap?.GetValue(Name) is not byte[] b || b.Length == 0 || (b[0] & 1) == 0;
            }
            catch { return false; }
        }
    }

    public static string? Set(bool enabled)
    {
        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (enabled)
            {
                // Kurulu uygulama klasörü güncellemelerde değişmez; başlatıcı (masaüstündeki exe) taşınabilir
                run.SetValue(Name, $"\"{Paths.ExePath}\" {Arg}", RegistryValueKind.String);
                // Görev Yöneticisi'nde daha önce kapatıldıysa yeniden aç
                using var ap = Registry.CurrentUser.CreateSubKey(ApprovedKey, true);
                ap.SetValue(Name, new byte[12] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
            }
            else run.DeleteValue(Name, false);
            Log.Write($"Windows açılınca başlat: {(enabled ? "açık" : "kapalı")} ({Paths.ExePath})");
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>Uygulama taşındı / güncellendiyse kayıttaki yolu tazele.</summary>
    public static void Refresh()
    {
        // Yalnızca kurulu kopya (başlatıcının açtığı klasör) kaydı günceller; geliştirme / test kopyaları dokunmaz
        if (!Paths.ExePath.Contains(@"\Wingry\uygulama\", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            if (run?.GetValue(Name) is string v && !v.Contains(Paths.ExePath, StringComparison.OrdinalIgnoreCase)) Set(true);
        }
        catch { }
    }

    public static bool LaunchedAtLogon => Environment.GetCommandLineArgs().Contains(Arg);
}
