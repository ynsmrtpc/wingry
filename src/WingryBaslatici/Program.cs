using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;

// Wingry başlatıcısı: gömülü uygulamayı (gerekirse) açar ve çalıştırır. Argümanlar aynen geçer.
// Kendini güncelleme: eski sürüm "--bekle <pid>" ile bizi çalıştırıp kapanır; dosyaları kilitli
// olmasın diye o süreç bitene kadar bekle (en çok 30 sn), argümanı uygulamaya geçirme.
int bi = Array.IndexOf(args, "--bekle");
if (bi >= 0)
{
    if (bi + 1 < args.Length && int.TryParse(args[bi + 1], out var pid))
    {
        try { using var eski = Process.GetProcessById(pid); eski.WaitForExit(30_000); } catch { /* zaten kapanmış */ }
        Thread.Sleep(300);
    }
    args = args.Where((_, i) => i != bi && i != bi + 1).ToArray();
}

var asm = Assembly.GetExecutingAssembly();
string surum;
using (var s = asm.GetManifestResourceStream("surum.txt")!)
using (var r = new StreamReader(s)) surum = r.ReadToEnd().Trim();

// WINGRY_KURULUM: yalnızca test için başka bir kurulum kökü
var kok = Environment.GetEnvironmentVariable("WINGRY_KURULUM") is { Length: > 0 } k ? k : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wingry");
var hedef = Path.Combine(kok, "uygulama");
var exe = Path.Combine(hedef, "Wingry.exe");
var damga = Path.Combine(hedef, ".surum");

// Sürüm damgası yyyyMMddHHmmss: yalnızca daha yeni bir sürüm açılır. Masaüstünde kalmış eski
// bir Wingry.exe çalıştırılırsa kurulu yeni sürümü ezmez, onu başlatır.
bool guncel = File.Exists(exe) && File.Exists(damga) && string.CompareOrdinal(File.ReadAllText(damga).Trim(), surum) >= 0;
if (!guncel)
{
    var gecici = Path.Combine(kok, "uygulama-yeni");
    try
    {
        if (Directory.Exists(gecici)) Directory.Delete(gecici, true);
        Directory.CreateDirectory(gecici);
        using (var z = asm.GetManifestResourceStream("uygulama.zip")!)
            ZipFile.ExtractToDirectory(z, gecici, overwriteFiles: true);
        File.WriteAllText(Path.Combine(gecici, ".surum"), surum);

        // Eski sürümü yeni klasörle değiştir. Eski sürüm açıksa (dosyalar kilitli) eskisini çalıştır.
        if (Directory.Exists(hedef))
        {
            var eski = Path.Combine(kok, "uygulama-eski");
            if (Directory.Exists(eski)) Directory.Delete(eski, true);
            Directory.Move(hedef, eski);
            try { Directory.Delete(eski, true); } catch { /* bir dahaki sefere */ }
        }
        Directory.Move(gecici, hedef);
    }
    catch (Exception ex) when (File.Exists(exe))
    {
        // Wingry şu an açık olabilir: var olanla devam et
        Debug.WriteLine(ex);
    }
    catch (Exception ex)
    {
        MessageBox(0, "Wingry açılamadı:\n" + ex.Message, "Wingry", 0x10);
        return 1;
    }
}

var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = hedef };
// Uygulama kendini güncellerken yeni sürümü bu dosyanın üzerine yazar.
psi.Environment["WINGRY_BASLATICI"] = Environment.ProcessPath ?? "";
foreach (var a in args) psi.ArgumentList.Add(a);
using var p = Process.Start(psi);
// Görevler (--task / --worker) çıkış kodunu bekler; arayüz kipinde hemen çık.
if (p is not null && args.Length > 0 && args[0] is "--task" or "--worker")
{
    p.WaitForExit();
    return p.ExitCode;
}
return 0;

[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern int MessageBox(nint hWnd, string text, string caption, uint type);
