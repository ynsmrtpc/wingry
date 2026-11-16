using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Wingry.Core;
using Wingry.Services;

namespace Wingry.Pages;

public sealed partial class DashboardPage : Page
{
    private readonly CpuMeter _cpu = new();
    private DispatcherQueueTimer? _timer;

    public DashboardPage()
    {
        InitializeComponent();
        Subtitle.Text = $"{SystemInfo.Machine} · {SystemInfo.OsName}";
        InfoMachine.Text = $"{SystemInfo.Machine} ({SystemInfo.User})";
        InfoOs.Text = SystemInfo.OsName;
        InfoCpu.Text = SystemInfo.CpuName;
        ShowIp();
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        AppState.Changed += OnAppState;
        OnAppState();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => AppState.Changed -= OnAppState;

    private void Start()
    {
        _cpu.Sample();
        _timer ??= DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
        RefreshDisks();
        OnTick(null, null);
    }

    private void Stop() => _timer?.Stop();

    private int _tick;
    private void OnTick(DispatcherQueueTimer? t, object? _)
    {
        var cpu = _cpu.Sample();
        CpuValue.Text = Format.Percent(cpu);
        CpuSpark.Push(cpu);

        var m = MemoryService.Snapshot();
        MemValue.Text = Format.Percent(m.LoadPercent);
        MemSpark.Push(m.LoadPercent);
        MemDetail.Text = $"{Format.Bytes(m.Used)} / {Format.Bytes(m.Total)} kullanımda";

        InfoUptime.Text = Format.Duration(SystemInfo.Uptime);
        if (++_tick % 15 == 0) { RefreshDisks(); ShowIp(); }
    }

    private void RefreshDisks()
    {
        var drives = SystemInfo.FixedDrives().ToList();
        var sys = drives.FirstOrDefault(d => d.Name.StartsWith(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:", StringComparison.OrdinalIgnoreCase)) ?? drives.FirstOrDefault();
        if (sys is not null)
        {
            double used = 100.0 * (sys.TotalSize - sys.AvailableFreeSpace) / sys.TotalSize;
            DiskLabel.Text = $"Sistem diski ({sys.Name.TrimEnd('\\')})";
            DiskValue.Text = Format.Bytes(sys.AvailableFreeSpace) + " boş";
            DiskBar.Value = used;
            DiskDetail.Text = $"{Format.Bytes(sys.TotalSize)} alanın %{used:0}'i dolu";
        }
        InfoDrives.Text = string.Join("   ", drives.Select(d =>
            $"{d.Name.TrimEnd('\\')} {(string.IsNullOrEmpty(d.VolumeLabel) ? "" : d.VolumeLabel + " ")}· {Format.Bytes(d.AvailableFreeSpace)} boş / {Format.Bytes(d.TotalSize)}"));
    }

    private void OnAppState()
    {
        UpdRing.IsActive = AppState.Loading;
        if (AppState.Error is not null && !AppState.Loading)
        {
            UpdValue.Text = "—";
            UpdDetail.Text = "winget'e ulaşılamadı: " + AppState.Error;
            return;
        }
        if (AppState.Installed.Count == 0) { UpdDetail.Text = "Taranıyor…"; return; }
        int n = AppState.UpdateCount;
        UpdValue.Text = n.ToString(Format.Tr);
        UpdDetail.Text = n == 0 ? $"{AppState.Installed.Count} uygulamanın hepsi güncel" : $"{AppState.Installed.Count} yüklü uygulamadan {n} tanesi güncellenebilir";
        UpdButton.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- IP adresi (varsayılan maskeli)

    private bool _ipVisible;

    /// <summary>Etkin, ağ geçidi olan bağlantıların IPv4 adresleri (sanal / filtre arayüzleri hariç).</summary>
    private static List<string> LocalIps() =>
        System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                        && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback
                        && n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254."))
            .Select(a => a.Address.ToString()).Distinct().ToList();

    private static string Mask(string ip) => string.Concat(ip.Select(c => char.IsDigit(c) ? '•' : c));

    private void ShowIp()
    {
        var ips = LocalIps();
        InfoIp.Text = ips.Count == 0 ? "bağlı değil" : string.Join("   ", _ipVisible ? ips : ips.Select(Mask));
        IpEye.Visibility = ips.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        IpEyeIcon.Glyph = _ipVisible ? "\uED1A" : "\uE7B3";   // göz kapalı / göz
        var tip = _ipVisible ? "IP adresini gizle" : "IP adresini göster";
        ToolTipService.SetToolTip(IpEye, tip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(IpEye, tip);
    }

    private void OnIpEye(object sender, RoutedEventArgs e) { _ipVisible = !_ipVisible; ShowIp(); }

    private void OnShowUpdates(object sender, RoutedEventArgs e) => App.Window?.Navigate("apps", new AppsPage.Args("updates", null));
    private void OnBundles(object sender, RoutedEventArgs e) => App.Window?.Navigate("apps", new AppsPage.Args("bundles", null));
    private void OnCleanup(object sender, RoutedEventArgs e) => App.Window?.Navigate("disk", "cleanup");

    private async void OnCleanRam(object sender, RoutedEventArgs e)
    {
        RamCard.IsEnabled = false; RamRing.IsActive = true;
        try
        {
            var r = await Task.Run(() => MemoryService.Clean(false));
            Ui.Toast("Bellek temizlendi", $"{Format.Bytes(r.FreedBytes)} boşaltıldı · {r.Processes} süreç. Daha fazlası için Bellek sayfasındaki derin temizliği kullanın.");
        }
        finally { RamCard.IsEnabled = true; RamRing.IsActive = false; }
    }

    private void OnUpdateAll(object sender, RoutedEventArgs e)
    {
        var ign = Settings.GetSet(Settings.IgnoredUpdates);
        var list = AppState.Installed.Where(p => p.HasUpdate && !ign.Contains(p.Id)).ToList();
        if (list.Count == 0) { Ui.Toast("Güncelleme yok", AppState.Loading ? "Tarama sürüyor, birazdan tekrar deneyin." : "Tüm uygulamalar güncel.", InfoBarSeverity.Informational); return; }
        OperationQueue.Enqueue(OpKind.Upgrade, list);
        Ui.Toast("Güncellemeler sıraya alındı", $"{list.Count} uygulama güncellenecek. İlerlemeyi İşlemler sayfasında izleyebilirsiniz.", InfoBarSeverity.Informational);
    }

    private async void OnRecommended(object sender, RoutedEventArgs e)
    {
        TweakCatalog.Load();
        var feats = TweakCatalog.Recommended
            .Where(id => TweakCatalog.Features.TryGetValue(id, out var f) && TweakCatalog.IsSupported(f))
            .Select(id => TweakCatalog.Features[id])
            .Where(f => !TweakService.IsApplied(f))
            .ToList();
        if (feats.Count == 0) { Ui.Toast("Zaten uygulanmış", "Önerilen ayarların hepsi bu bilgisayarda etkin.", InfoBarSeverity.Informational); return; }

        var text = string.Join("\n", feats.Select(f => "• " + f.Label));
        if (!await Ui.ConfirmAsync("Önerilen ayarlar uygulansın mı?",
                $"Önce bir sistem geri yükleme noktası oluşturulur ve değişen değerler yedeklenir.\n\n{text}", "Uygula")) return;

        var r = await TweakService.ApplyAsync(feats.Select(f => new TweakChange(f, true)).ToList(), createRestorePoint: true);
        TweaksPage.ReportResult(r);
    }
}
