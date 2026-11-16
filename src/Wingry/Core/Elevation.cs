using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Wingry.Core;

/// <summary>
/// Arayüz yönetici izni olmadan çalışır. Yönetici gerektiren işler (HKLM, hizmetler, DISM,
/// derin RAM temizliği, yüksek yetkili görevler) toplanır ve TEK bir UAC onayıyla
/// <c>Wingry.exe --worker istek.txt</c> olarak çalıştırılır.
/// </summary>
public static class Elevation
{
    public static bool IsAdmin { get; } = CheckAdmin();

    private static bool CheckAdmin()
    {
        try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
        catch { return false; }
    }

    public sealed record WorkerResult(bool Started, IReadOnlyList<string> Lines, string? Error)
    {
        public int Failures => Lines.Count(l => l.StartsWith("HATA", StringComparison.Ordinal));
    }

    /// <summary>Komutları yönetici olarak çalıştırır. Zaten yöneticiysek aynı süreçte koşar.</summary>
    public static async Task<WorkerResult> RunAsync(IReadOnlyList<string> commands)
    {
        if (commands.Count == 0) return new WorkerResult(true, [], null);

        if (IsAdmin)
        {
            var lines = await Task.Run(() => ElevatedWorker.Execute(commands));
            return new WorkerResult(true, lines, null);
        }

        var req = Path.Combine(Paths.Temp, $"istek-{Guid.NewGuid():N}.txt");
        var res = req + ".sonuc";
        await File.WriteAllLinesAsync(req, commands);
        try
        {
            var psi = new ProcessStartInfo(Paths.ExePath, $"--worker \"{req}\"")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var p = Process.Start(psi);
            if (p is null) return new WorkerResult(false, [], "Yönetici işlemi başlatılamadı.");
            await p.WaitForExitAsync();
            var lines = File.Exists(res) ? await File.ReadAllLinesAsync(res) : ["HATA: işçi sonuç yazmadı"];
            return new WorkerResult(true, lines, null);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new WorkerResult(false, [], "Yönetici izni verilmedi.");
        }
        catch (Exception ex)
        {
            Log.Error("Elevation.RunAsync", ex);
            return new WorkerResult(false, [], ex.Message);
        }
        finally
        {
            TryDelete(req); TryDelete(res);
        }
    }

    /// <summary>
    /// İşçiyi başlatır ve çalışırken ilerleme dosyasına yazdığı satırları <paramref name="onLine"/>
    /// ile iletir (UI bağlamında). <paramref name="cancelFile"/>: iptal edilecek paket kimliklerinin yazılacağı dosya.
    /// </summary>
    public static async Task<WorkerResult> RunWithProgressAsync(IReadOnlyList<string> commands, Action<string> onLine, Action<string>? cancelFile = null)
    {
        var req = Path.Combine(Paths.Temp, $"istek-{Guid.NewGuid():N}.txt");
        var prog = req + ".ilerleme";
        var res = req + ".sonuc";
        await File.WriteAllLinesAsync(req, new[] { "ilerleme|" + prog }.Concat(commands));
        File.WriteAllText(prog, "");
        cancelFile?.Invoke(prog + ".iptal");
        long pos = 0;

        void Drain()
        {
            try
            {
                using var fs = new FileStream(prog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (fs.Length <= pos) return;
                fs.Seek(pos, SeekOrigin.Begin);
                using var r = new StreamReader(fs);
                var text = r.ReadToEnd();
                int last = text.LastIndexOf('\n');
                if (last < 0) return;               // yarım satırı sonraki turda oku
                pos += System.Text.Encoding.UTF8.GetByteCount(text[..(last + 1)]);
                foreach (var l in text[..last].Split('\n')) if (l.Trim().Length > 0) onLine(l.Trim());
            }
            catch { }
        }

        try
        {
            if (IsAdmin)
            {
                var run = Task.Run(() => ElevatedWorker.Execute(File.ReadAllLines(req)));
                while (!run.IsCompleted) { Drain(); await Task.Delay(400); }
                Drain();
                return new WorkerResult(true, await run, null);
            }

            var psi = new ProcessStartInfo(Paths.ExePath, $"--worker \"{req}\"") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            using var p = Process.Start(psi);
            if (p is null) return new WorkerResult(false, [], "Yönetici işlemi başlatılamadı.");
            while (!p.HasExited) { Drain(); await Task.Delay(400); }
            Drain();
            var lines = File.Exists(res) ? await File.ReadAllLinesAsync(res) : ["HATA: işçi sonuç yazmadı"];
            return new WorkerResult(true, lines, null);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new WorkerResult(false, [], "Yönetici izni verilmedi.");
        }
        catch (Exception ex)
        {
            Log.Error("Elevation.RunWithProgress", ex);
            return new WorkerResult(false, [], ex.Message);
        }
        finally
        {
            TryDelete(req); TryDelete(res); TryDelete(prog); TryDelete(prog + ".iptal");
        }
    }

    public static void RestartAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Paths.ExePath) { UseShellExecute = true, Verb = "runas" });
            Environment.Exit(0);
        }
        catch (Win32Exception) { /* kullanıcı vazgeçti */ }
    }

    private static void TryDelete(string p) { try { File.Delete(p); } catch { } }
}
