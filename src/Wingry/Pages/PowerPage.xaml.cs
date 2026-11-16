using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Wingry.Core;

namespace Wingry.Pages;

public sealed partial class PowerPage : Page
{
    private sealed record Plan(string Guid, string Name, bool Active);
    private List<Plan> _plans = [];
    private bool _init;

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);

    public PowerPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        _init = false;
        await LoadPlansAsync();
        LoadHibernate();
        LoadBattery();
        _init = true;
    }

    private async Task LoadPlansAsync()
    {
        // "GUID: xxxxxxxx-...  (Ad) *" — dil bağımsız: yalnızca GUID, parantez ve yıldız aranır
        var (_, o) = await Task.Run(() => Tools.Cmd("powercfg /list"));
        _plans = Regex.Matches(o, @"([0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12})\s+\((.+?)\)(\s*\*)?")
            .Select(m => new Plan(m.Groups[1].Value, m.Groups[2].Value.Trim(), m.Groups[3].Success)).ToList();
        Plans.Items.Clear();
        foreach (var p in _plans) Plans.Items.Add(p.Name);
        Plans.SelectedIndex = _plans.FindIndex(p => p.Active);
        UltimateCard.Visibility = _plans.Any(p => p.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Nihai", StringComparison.OrdinalIgnoreCase))
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnPlan(object sender, SelectionChangedEventArgs e)
    {
        if (!_init || Plans.SelectedIndex < 0) return;
        var p = _plans[Plans.SelectedIndex];
        var (c, o) = await Task.Run(() => Tools.Cmd($"powercfg /setactive {p.Guid}"));
        if (c == 0) { Ui.Toast("Güç planı değişti", p.Name, InfoBarSeverity.Success, 3); Log.Write("Güç planı: " + p.Name); }
        else Ui.Error("Plan değiştirilemedi", o);
    }

    private async void OnUltimate(object sender, RoutedEventArgs e)
    {
        var w = await Elevation.RunAsync(["arac|guc|nihai"]);
        if (!w.Started || w.Failures > 0) { Ui.Error("Plan eklenemedi", w.Error ?? string.Join("\n", w.Lines)); return; }
        _init = false; await LoadPlansAsync(); _init = true;
        Ui.Toast("Nihai performans planı eklendi", "Etkin plan listesinden seçebilirsiniz.");
    }

    private void LoadHibernate()
    {
        var file = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "hiberfil.sys");
        long size = 0;
        try { var fi = new FileInfo(file); if (fi.Exists) size = fi.Length; } catch { }
        HibToggle.IsOn = size > 0;
        HibCard.Description = size > 0
            ? $"Açık. hiberfil.sys diskte {Format.Bytes(size)} yer kaplıyor. Kapatırsanız bu alan geri kazanılır; 'Hızlı başlatma' da kapanır (açılış birkaç saniye uzayabilir). Dizüstünde pil bitince oturumun korunması için açık kalması önerilir."
            : "Kapalı. Açarsanız oturumunuz kapanmadan diske kaydedilebilir ve 'Hızlı başlatma' kullanılabilir; bellek boyutu kadar disk alanı kullanır.";
    }

    private async void OnHib(object sender, RoutedEventArgs e)
    {
        if (!_init) return;
        bool on = HibToggle.IsOn;
        var w = await Elevation.RunAsync([$"arac|guc|{(on ? "hibernate-on" : "hibernate-off")}"]);
        if (!w.Started || w.Failures > 0)
        {
            Ui.Error("Değiştirilemedi", w.Error ?? string.Join("\n", w.Lines));
            _init = false; HibToggle.IsOn = !on; _init = true;
            return;
        }
        Ui.Toast(on ? "Hazırda bekletme açıldı" : "Hazırda bekletme kapatıldı", on ? "" : "hiberfil.sys silindi, disk alanı geri kazanıldı.");
        LoadHibernate();
    }

    private void LoadBattery()
    {
        GetSystemPowerStatus(out var s);
        bool noBattery = (s.BatteryFlag & 128) != 0 || s.BatteryFlag == 255;
        BatterySection.Visibility = noBattery ? Visibility.Collapsed : Visibility.Visible;
        if (noBattery) return;
        var ac = s.ACLineStatus == 1 ? "şarjda" : "pilde";
        var left = s.BatteryLifeTime > 0 ? $" · yaklaşık {Format.Duration(TimeSpan.FromSeconds(s.BatteryLifeTime))} kaldı" : "";
        BatteryText.Text = $"%{s.BatteryLifePercent} · {ac}{left}";
    }

    private async void OnBatteryReport(object sender, RoutedEventArgs e)
    {
        var file = Path.Combine(Paths.Temp, "pil-raporu.html");
        var (c, o) = await Task.Run(() => Tools.Cmd($"powercfg /batteryreport /output \"{file}\""));
        if (c == 0 && File.Exists(file)) Tools.Open(file);
        else Ui.Error("Rapor oluşturulamadı", o);
    }
}
