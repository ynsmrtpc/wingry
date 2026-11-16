using System.Security;
using System.Security.Principal;
using System.Text;
using Wingry.Core;

namespace Wingry.Services;

public enum ScheduleKind { Interval, Daily, Weekly, AtLogon }

public sealed record ScheduleSpec(ScheduleKind Kind, int IntervalMinutes = 60, TimeSpan At = default, DayOfWeek Day = DayOfWeek.Monday);

/// <summary>Windows Görev Zamanlayıcı (schtasks + XML). Görevler "\Wingry\" klasörü altında.</summary>
public static class SchedulerService
{
    public const string RamTask = @"\Wingry\RAM Temizleme";
    public const string UpdateTask = @"\Wingry\Uygulama Guncelleme";

    public static bool Exists(string name) => ElevatedWorker.RunTool("schtasks.exe", $"/Query /TN \"{name}\"", 10_000).code == 0;

    /// <summary>Görevin bir sonraki çalışma zamanı ve durumu (schtasks /Query /FO LIST /V).</summary>
    public static (string next, string last, string status)? Query(string name)
    {
        var (code, o) = ElevatedWorker.RunTool("schtasks.exe", $"/Query /TN \"{name}\" /FO CSV /NH /V", 10_000);
        if (code != 0) return null;
        // CSV: "Ana Bilgisayar","Görev Adı","Sonraki Çalışma Zamanı","Durum","Oturum Açma Modu","Son Çalışma Zamanı","Son Sonuç",...
        var cols = ParseCsvLine(o.Split('\n')[0].Trim());
        if (cols.Count < 7) return ("", "", "");
        return (cols[2], cols[5], cols[3]);
    }

    private static List<string> ParseCsvLine(string line)
    {
        var r = new List<string>(); var sb = new StringBuilder(); bool q = false;
        foreach (var ch in line)
        {
            if (ch == '"') q = !q;
            else if (ch == ',' && !q) { r.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        r.Add(sb.ToString());
        return r;
    }

    public static async Task<string?> CreateAsync(string name, string description, string arguments, ScheduleSpec spec, bool highest)
    {
        var xml = BuildXml(description, arguments, spec, highest);
        var file = Path.Combine(Paths.Temp, $"gorev-{Guid.NewGuid():N}.xml");
        await File.WriteAllTextAsync(file, xml, Encoding.Unicode);
        try
        {
            if (highest && !Elevation.IsAdmin)
            {
                var w = await Elevation.RunAsync([$"schtask|create|{name}|{file}"]);
                if (!w.Started) return w.Error;
                var err = w.Lines.FirstOrDefault(l => l.StartsWith("HATA", StringComparison.Ordinal));
                return err;
            }
            var (code, o) = ElevatedWorker.RunTool("schtasks.exe", $"/Create /TN \"{name}\" /XML \"{file}\" /F", 20_000);
            return code == 0 ? null : o.Trim();
        }
        finally { try { File.Delete(file); } catch { } }
    }

    public static async Task<string?> DeleteAsync(string name, bool highest)
    {
        var (code, o) = ElevatedWorker.RunTool("schtasks.exe", $"/Delete /TN \"{name}\" /F", 20_000);
        if (code == 0) return null;
        if (!Elevation.IsAdmin)
        {
            var w = await Elevation.RunAsync([$"schtask|delete|{name}"]);
            if (!w.Started) return w.Error;
            return w.Lines.FirstOrDefault(l => l.StartsWith("HATA", StringComparison.Ordinal));
        }
        return o.Trim();
    }

    public static async Task<string?> RunNowAsync(string name) =>
        await Task.Run(() =>
        {
            var (code, o) = ElevatedWorker.RunTool("schtasks.exe", $"/Run /TN \"{name}\"", 20_000);
            return code == 0 ? null : o.Trim();
        });

    private static string BuildXml(string description, string arguments, ScheduleSpec spec, bool highest)
    {
        var user = WindowsIdentity.GetCurrent().Name;
        var start = DateTime.Today.Add(spec.At == default ? TimeSpan.FromHours(DateTime.Now.Hour + 1 > 23 ? 0 : DateTime.Now.Hour + 1) : spec.At);
        if (spec.Kind == ScheduleKind.Interval) start = DateTime.Now.AddMinutes(1);
        var startStr = start.ToString("yyyy-MM-dd'T'HH:mm:ss");

        string trigger = spec.Kind switch
        {
            ScheduleKind.Interval => $"""
                <TimeTrigger>
                  <StartBoundary>{startStr}</StartBoundary>
                  <Enabled>true</Enabled>
                  <Repetition><Interval>PT{spec.IntervalMinutes}M</Interval><StopAtDurationEnd>false</StopAtDurationEnd></Repetition>
                </TimeTrigger>
                """,
            ScheduleKind.Daily => $"""
                <CalendarTrigger>
                  <StartBoundary>{startStr}</StartBoundary>
                  <Enabled>true</Enabled>
                  <ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>
                </CalendarTrigger>
                """,
            ScheduleKind.Weekly => $"""
                <CalendarTrigger>
                  <StartBoundary>{startStr}</StartBoundary>
                  <Enabled>true</Enabled>
                  <ScheduleByWeek><DaysOfWeek><{spec.Day} /></DaysOfWeek><WeeksInterval>1</WeeksInterval></ScheduleByWeek>
                </CalendarTrigger>
                """,
            _ => $"""
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                  <Delay>PT2M</Delay>
                </LogonTrigger>
                """,
        };

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Author>Wingry</Author>
                <Description>{SecurityElement.Escape(description)}</Description>
              </RegistrationInfo>
              <Triggers>
            {trigger}
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>{(highest ? "HighestAvailable" : "LeastPrivilege")}</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <ExecutionTimeLimit>PT2H</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(Paths.ExePath)}</Command>
                  <Arguments>{SecurityElement.Escape(arguments)}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }
}
