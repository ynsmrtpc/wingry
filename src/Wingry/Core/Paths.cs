namespace Wingry.Core;

public static class Paths
{
    public static string AppDir { get; } = AppContext.BaseDirectory;
    public static string ExePath { get; } = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Wingry.exe");
    public static string Data(string rel) => Path.Combine(AppDir, "Data", rel);
    public static string Debloat(string rel) => Path.Combine(AppDir, "Data", "Win11Debloat", rel);

    public static string UserDir { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wingry"));

    public static string Backups { get; } = Ensure(Path.Combine(UserDir, "Yedekler"));
    public static string Logs { get; } = Ensure(Path.Combine(UserDir, "Gunluk"));
    public static string Temp { get; } = Ensure(Path.Combine(UserDir, "Gecici"));

    private static string Ensure(string p) { Directory.CreateDirectory(p); return p; }
}

public static class Log
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var file = Path.Combine(Paths.Logs, $"wingry-{DateTime.Now:yyyy-MM}.log");
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch { /* günlük yazılamazsa uygulamayı durdurma */ }
    }

    public static void Error(string where, Exception ex) =>
        Write($"HATA [{where}] {ex.GetType().Name}: {ex.Message}" +
              (ex.StackTrace is { } st ? Environment.NewLine + "    " + string.Join(Environment.NewLine + "    ", st.Split(Environment.NewLine).Take(6).Select(l => l.Trim())) : ""));
}
