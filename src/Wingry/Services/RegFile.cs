using System.Globalization;
using System.Text;
using Microsoft.Win32;
using Wingry.Core;

namespace Wingry.Services;

public enum RegOpKind { CreateKey, DeleteKey, SetValue, DeleteValue }

public sealed record RegOp(RegistryHive Hive, string SubKey, RegOpKind Kind, string? Name = null,
    RegistryValueKind ValueKind = RegistryValueKind.Unknown, object? Data = null)
{
    /// <summary>HKCU dışındaki her şey yönetici ister (HKCR yazmaları HKLM\Software\Classes'a düşer).</summary>
    public bool NeedsAdmin => Hive != RegistryHive.CurrentUser;
}

/// <summary>.reg dosyası ayrıştırıcı + uygulayıcı + durum denetleyici. reg.exe çağırmaz.</summary>
public static class RegFile
{
    private static readonly Dictionary<string, IReadOnlyList<RegOp>> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<RegOp> Load(string path)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(path, out var c)) return c;
            var ops = Parse(File.ReadAllText(path));
            Cache[path] = ops;
            return ops;
        }
    }

    public static IReadOnlyList<RegOp> Parse(string text)
    {
        var ops = new List<RegOp>();
        RegistryHive hive = default; string? key = null;
        foreach (var raw in JoinContinuations(text))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line.StartsWith("Windows Registry Editor", StringComparison.OrdinalIgnoreCase) || line == "REGEDIT4")
                continue;

            if (line[0] == '[' && line[^1] == ']')
            {
                var inner = line[1..^1];
                bool del = inner.StartsWith('-');
                if (del) inner = inner[1..];
                (hive, key) = SplitKey(inner);
                if (key is null) continue;
                ops.Add(new RegOp(hive, key, del ? RegOpKind.DeleteKey : RegOpKind.CreateKey));
                if (del) key = null; // silinen anahtarın altına değer yazılmaz
                continue;
            }

            if (key is null) continue;
            if (!TryParseValue(line, out var name, out var kind, out var data, out var delete)) continue;
            ops.Add(delete
                ? new RegOp(hive, key, RegOpKind.DeleteValue, name)
                : new RegOp(hive, key, RegOpKind.SetValue, name, kind, data));
        }
        return ops;
    }

    private static IEnumerable<string> JoinContinuations(string text)
    {
        var sb = new StringBuilder();
        foreach (var l in text.Split('\n'))
        {
            var line = l.TrimEnd('\r', ' ', '\t');
            if (sb.Length > 0) line = line.TrimStart();
            if (line.EndsWith('\\') && !line.TrimStart().StartsWith(';'))
            {
                sb.Append(line, 0, line.Length - 1);
                continue;
            }
            sb.Append(line);
            yield return sb.ToString();
            sb.Clear();
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    private static (RegistryHive, string?) SplitKey(string full)
    {
        int i = full.IndexOf('\\');
        var root = i < 0 ? full : full[..i];
        var sub = i < 0 ? "" : full[(i + 1)..];
        RegistryHive? h = root.ToUpperInvariant() switch
        {
            "HKEY_CURRENT_USER" or "HKCU" => RegistryHive.CurrentUser,
            "HKEY_LOCAL_MACHINE" or "HKLM" => RegistryHive.LocalMachine,
            "HKEY_CLASSES_ROOT" or "HKCR" => RegistryHive.ClassesRoot,
            "HKEY_USERS" or "HKU" => RegistryHive.Users,
            _ => null,
        };
        return h is null ? (default, null) : (h.Value, sub);
    }

    private static bool TryParseValue(string line, out string name, out RegistryValueKind kind, out object? data, out bool delete)
    {
        name = ""; kind = RegistryValueKind.Unknown; data = null; delete = false;
        int eq;
        if (line[0] == '@') { eq = line.IndexOf('='); name = ""; }
        else if (line[0] == '"')
        {
            int end = FindClosingQuote(line, 1);
            if (end < 0) return false;
            name = Unescape(line[1..end]);
            eq = line.IndexOf('=', end);
        }
        else return false;
        if (eq < 0) return false;

        var v = line[(eq + 1)..].Trim();
        if (v == "-") { delete = true; return true; }
        if (v.StartsWith('"'))
        {
            int end = FindClosingQuote(v, 1);
            kind = RegistryValueKind.String;
            data = Unescape(v[1..(end < 0 ? v.Length : end)]);
            return true;
        }
        if (v.StartsWith("dword:", StringComparison.OrdinalIgnoreCase))
        {
            kind = RegistryValueKind.DWord;
            data = unchecked((int)uint.Parse(v[6..], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            return true;
        }
        if (v.StartsWith("hex", StringComparison.OrdinalIgnoreCase))
        {
            int colon = v.IndexOf(':');
            var type = v[..colon].ToLowerInvariant();
            var bytes = ParseHex(v[(colon + 1)..]);
            switch (type)
            {
                case "hex": kind = RegistryValueKind.Binary; data = bytes; break;
                case "hex(0)": kind = RegistryValueKind.None; data = bytes; break;
                case "hex(2)": kind = RegistryValueKind.ExpandString; data = Utf16(bytes); break;
                case "hex(7)":
                    kind = RegistryValueKind.MultiString;
                    data = Utf16(bytes).Split('\0', StringSplitOptions.RemoveEmptyEntries);
                    break;
                case "hex(b)": kind = RegistryValueKind.QWord; data = BitConverter.ToInt64(Pad(bytes, 8)); break;
                case "hex(4)": kind = RegistryValueKind.DWord; data = BitConverter.ToInt32(Pad(bytes, 4)); break;
                default: return false;
            }
            return true;
        }
        return false;
    }

    private static int FindClosingQuote(string s, int start)
    {
        for (int i = start; i < s.Length; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == '"') return i;
        }
        return -1;
    }

    private static string Unescape(string s) => s.Replace("\\\"", "\"").Replace("\\\\", "\\");

    private static byte[] ParseHex(string s)
    {
        var parts = s.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var b = new byte[parts.Length];
        for (int i = 0; i < parts.Length; i++) b[i] = byte.Parse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return b;
    }

    private static byte[] Pad(byte[] b, int n) { if (b.Length >= n) return b; var r = new byte[n]; b.CopyTo(r, 0); return r; }
    private static string Utf16(byte[] b) => Encoding.Unicode.GetString(b).TrimEnd('\0');

    // ----------------------------------------------------------------- uygulama

    private static RegistryKey Base(RegistryHive h) => RegistryKey.OpenBaseKey(h, RegistryView.Registry64);

    /// <summary>İşlemleri uygular. <paramref name="filter"/> ile yalnızca HKCU ya da yalnızca yönetici kısmı seçilebilir.</summary>
    public static List<string> Apply(IEnumerable<RegOp> ops, Func<RegOp, bool>? filter = null)
    {
        var errors = new List<string>();
        foreach (var op in ops)
        {
            if (filter is not null && !filter(op)) continue;
            try
            {
                using var root = Base(op.Hive);
                switch (op.Kind)
                {
                    case RegOpKind.CreateKey:
                        root.CreateSubKey(op.SubKey, true)?.Dispose();
                        break;
                    case RegOpKind.DeleteKey:
                        root.DeleteSubKeyTree(op.SubKey, false);
                        break;
                    case RegOpKind.SetValue:
                        using (var k = root.CreateSubKey(op.SubKey, true))
                            k.SetValue(op.Name ?? "", op.Data!, op.ValueKind);
                        break;
                    case RegOpKind.DeleteValue:
                        using (var k = root.OpenSubKey(op.SubKey, true))
                            k?.DeleteValue(op.Name ?? "", false);
                        break;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{op.Hive}\\{op.SubKey} {op.Name}: {ex.Message}");
            }
        }
        return errors;
    }

    /// <summary>Dosyadaki tüm değer işlemleri şu anki kayıt defteriyle eşleşiyor mu?</summary>
    public static bool Matches(IReadOnlyList<RegOp> ops)
    {
        bool any = false;
        foreach (var op in ops)
        {
            if (op.Kind == RegOpKind.CreateKey) continue;
            any = true;
            if (!MatchOne(op)) return false;
        }
        return any;
    }

    private static bool MatchOne(RegOp op)
    {
        try
        {
            using var root = Base(op.Hive);
            switch (op.Kind)
            {
                case RegOpKind.DeleteKey:
                    using (var k = root.OpenSubKey(op.SubKey)) return k is null;
                case RegOpKind.DeleteValue:
                    using (var k = root.OpenSubKey(op.SubKey)) return k?.GetValue(op.Name ?? "") is null;
                case RegOpKind.SetValue:
                    using (var k = root.OpenSubKey(op.SubKey))
                    {
                        var cur = k?.GetValue(op.Name ?? "", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                        return ValueEquals(cur, op.Data);
                    }
            }
        }
        catch { }
        return false;
    }

    private static bool ValueEquals(object? cur, object? want) => (cur, want) switch
    {
        (null, _) => false,
        (int a, int b) => a == b,
        (long a, long b) => a == b,
        (string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase),
        (byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b),
        (string[] a, string[] b) => a.SequenceEqual(b, StringComparer.OrdinalIgnoreCase),
        _ => Equals(cur, want),
    };

    // ----------------------------------------------------------------- yedek

    /// <summary>Değişecek değerlerin şimdiki hâlini .reg biçiminde yedekler; geri yükleme aynı motorla yapılır.</summary>
    public static string? Backup(IEnumerable<RegOp> ops, string title)
    {
        try
        {
            var sb = new StringBuilder("Windows Registry Editor Version 5.00\r\n\r\n; Wingry yedeği: ").Append(title).Append("\r\n\r\n");
            foreach (var g in ops.Where(o => o.Kind is RegOpKind.SetValue or RegOpKind.DeleteValue).GroupBy(o => (o.Hive, o.SubKey)))
            {
                sb.Append('[').Append(HiveName(g.Key.Hive)).Append('\\').Append(g.Key.SubKey).Append("]\r\n");
                using var root = Base(g.Key.Hive);
                using var k = root.OpenSubKey(g.Key.SubKey);
                foreach (var name in g.Select(o => o.Name ?? "").Distinct())
                {
                    var label = name.Length == 0 ? "@" : "\"" + name.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                    var val = k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    sb.Append(label).Append('=').Append(val is null ? "-" : Serialize(val, k!.GetValueKind(name))).Append("\r\n");
                }
                sb.Append("\r\n");
            }
            var file = Path.Combine(Paths.Backups, $"{DateTime.Now:yyyyMMdd-HHmmss}.reg");
            File.WriteAllText(file, sb.ToString(), Encoding.Unicode);
            return file;
        }
        catch (Exception ex) { Log.Error("RegFile.Backup", ex); return null; }
    }

    private static string HiveName(RegistryHive h) => h switch
    {
        RegistryHive.CurrentUser => "HKEY_CURRENT_USER",
        RegistryHive.LocalMachine => "HKEY_LOCAL_MACHINE",
        RegistryHive.ClassesRoot => "HKEY_CLASSES_ROOT",
        RegistryHive.Users => "HKEY_USERS",
        _ => h.ToString(),
    };

    private static string Serialize(object v, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => "dword:" + unchecked((uint)(int)v).ToString("x8"),
        RegistryValueKind.QWord => "hex(b):" + Hex(BitConverter.GetBytes((long)v)),
        RegistryValueKind.String => "\"" + ((string)v).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
        RegistryValueKind.ExpandString => "hex(2):" + Hex(Encoding.Unicode.GetBytes((string)v + "\0")),
        RegistryValueKind.MultiString => "hex(7):" + Hex(Encoding.Unicode.GetBytes(string.Join('\0', (string[])v) + "\0\0")),
        _ => "hex:" + Hex(v as byte[] ?? []),
    };

    private static string Hex(byte[] b) => string.Join(',', b.Select(x => x.ToString("x2")));
}
