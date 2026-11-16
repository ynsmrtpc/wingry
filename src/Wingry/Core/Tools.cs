using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Wingry.Core;

/// <summary>Komut satırı araçlarını Türkçe karakterleri bozmadan çalıştırma yardımcıları.</summary>
public static class Tools
{
    /// <summary>cmd üzerinden, konsol kod sayfası UTF-8'e alınarak çalıştırır (netsh, powercfg, pnputil…).</summary>
    public static (int code, string output) Cmd(string commandLine, int timeoutMs = 120_000) =>
        Run("cmd.exe", "/d /c chcp 65001>nul & " + commandLine, timeoutMs);

    /// <summary>PowerShell betiği; çıktı UTF-8.</summary>
    public static (int code, string output) Ps(string script, int timeoutMs = 120_000) =>
        Run("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"[Console]::OutputEncoding=[Text.Encoding]::UTF8; "
            + script.Replace("\"", "\\\"") + "\"", timeoutMs);

    /// <summary>PowerShell'den JSON (ConvertTo-Json) alır; tek nesneyi de diziye çevirir.</summary>
    public static List<JsonElement> PsJson(string script, int timeoutMs = 120_000)
    {
        var (code, o) = Ps(script + " | ConvertTo-Json -Compress -Depth 3", timeoutMs);
        var list = new List<JsonElement>();
        if (code != 0 || string.IsNullOrWhiteSpace(o)) return list;
        try
        {
            using var doc = JsonDocument.Parse(o.Trim());
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                foreach (var e in doc.RootElement.EnumerateArray()) list.Add(e.Clone());
            else list.Add(doc.RootElement.Clone());
        }
        catch (Exception ex) { Log.Error("PsJson", ex); }
        return list;
    }

    public static (int code, string output) Run(string exe, string args, int timeoutMs)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        using var p = Process.Start(psi)!;
        var so = p.StandardOutput.ReadToEndAsync();
        var se = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return (-1, "zaman aşımı"); }
        return (p.ExitCode, so.Result + se.Result);
    }

    public static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "",
        } : "";

    /// <summary>Yönetici işçisine geçirilen değerlerde tırnak / ayırıcı olmasın.</summary>
    public static bool SafeArg(string s) => s.Length > 0 && s.IndexOfAny(['"', '|', '\r', '\n', '&', '<', '>', '^']) < 0;

    public static void Open(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
}
