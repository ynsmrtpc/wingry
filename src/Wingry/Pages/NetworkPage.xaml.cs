using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Xml.Linq;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Wingry.Core;

namespace Wingry.Pages;

public sealed partial class NetworkPage : Page
{
    private sealed record DnsPreset(string Key, string Label, string Primary, string Secondary);

    private static readonly DnsPreset[] Presets =
    [
        new("auto", "Otomatik (ağdan / DHCP)", "", ""),
        new("cloudflare", "Cloudflare · 1.1.1.1 (hızlı, gizlilik odaklı)", "1.1.1.1", "1.0.0.1"),
        new("cloudflare-family", "Cloudflare Aile · zararlı + yetişkin içerik engeli", "1.1.1.3", "1.0.0.3"),
        new("google", "Google · 8.8.8.8", "8.8.8.8", "8.8.4.4"),
        new("quad9", "Quad9 · 9.9.9.9 (zararlı alan adı engeli)", "9.9.9.9", "149.112.112.112"),
        new("adguard", "AdGuard · reklam engelleyen DNS", "94.140.14.14", "94.140.15.15"),
    ];

    public NetworkPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e) => BuildAdapters();

    private void BuildAdapters()
    {
        Adapters.Children.Clear();
        var nics = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                        && !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
                        && !n.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
                        // Filtre sürücülerinin sanal arayüzleri (WFP, QoS, antivirüs NDIS) IP almaz: yalnızca gerçek IPv4 adresi olanlar
                        && n.GetIPProperties().UnicastAddresses.Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254.")))
            .ToList();
        if (nics.Count == 0)
        {
            Adapters.Children.Add(new TextBlock { Text = "Etkin ağ bağlantısı bulunamadı.", Style = (Style)Application.Current.Resources["Caption"] });
            return;
        }
        foreach (var n in nics)
        {
            var props = n.GetIPProperties();
            var ip = string.Join(", ", props.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString()));
            var dns = props.DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).ToList();
            var current = Presets.Skip(1).FirstOrDefault(p => dns.Contains(p.Primary));
            bool staticDns = IsStaticDns(n.Id);

            var combo = new ComboBox { MinWidth = 300 };
            foreach (var p in Presets) combo.Items.Add(p.Label);
            combo.SelectedIndex = current is not null ? Array.IndexOf(Presets, current) : staticDns ? -1 : 0;
            if (combo.SelectedIndex < 0) combo.PlaceholderText = "Özel (elle girilmiş)";
            var apply = new Button { Content = "Uygula", IsEnabled = false };
            int initial = combo.SelectedIndex;
            combo.SelectionChanged += (_, _) => apply.IsEnabled = combo.SelectedIndex != initial;
            var name = n.Name;
            apply.Click += async (_, _) =>
            {
                var p = Presets[combo.SelectedIndex];
                apply.IsEnabled = false;
                var cmd = p.Key == "auto" ? $"arac|dns|{name}|auto" : $"arac|dns|{name}|{p.Primary}|{p.Secondary}";
                var w = await Elevation.RunAsync([cmd]);
                if (!w.Started) Ui.Error("DNS değiştirilemedi", w.Error ?? "");
                else if (w.Failures > 0) Ui.Error("DNS değiştirilemedi", string.Join("\n", w.Lines));
                else { Ui.Toast("DNS değiştirildi", $"{name}: {p.Label}"); Log.Write($"DNS {name} -> {p.Key}"); }
                BuildAdapters();
            };

            var card = new SettingsCard
            {
                Header = $"{n.Name}  ·  {(n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "Kablolu")}",
                Description = $"{n.Description}\nIP: {(ip.Length > 0 ? ip : "—")}   ·   DNS: {(dns.Count > 0 ? string.Join(", ", dns) : "—")}",
                HeaderIcon = new FontIcon { Glyph = n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "" : "" },
                Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { combo, apply } },
            };
            Adapters.Children.Add(card);
        }
    }

    /// <summary>DNS elle girilmişse arayüzün kayıt defterindeki NameServer değeri doludur (DHCP'de boş).</summary>
    private static bool IsStaticDns(string id)
    {
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{id}");
            return !string.IsNullOrWhiteSpace(k?.GetValue("NameServer") as string);
        }
        catch { return false; }
    }

    private async void OnFlush(object sender, RoutedEventArgs e)
    {
        var (c, o) = await Task.Run(() => Tools.Cmd("ipconfig /flushdns"));
        if (c == 0) Ui.Toast("DNS önbelleği temizlendi", "Siteler yeniden adlandırılırken güncel adresler kullanılacak.");
        else Ui.Error("Temizlenemedi", o);
    }

    private async void OnReset(object sender, RoutedEventArgs e)
    {
        if (!await Ui.ConfirmAsync("Ağ ayarları sıfırlansın mı?", "Winsock ve TCP/IP sıfırlanır. İşlemden sonra bilgisayarı yeniden başlatmanız gerekir. Elle girdiğiniz IP, VPN ya da proxy ayarları silinebilir.", "Sıfırla", danger: true)) return;
        var w = await Elevation.RunAsync(["arac|agsifirla"]);
        if (!w.Started) Ui.Error("Sıfırlanamadı", w.Error ?? "");
        else if (w.Failures > 0) Ui.Error("Sıfırlanamadı", string.Join("\n", w.Lines));
        else Ui.Toast("Ağ ayarları sıfırlandı", "Değişikliklerin etkin olması için bilgisayarı yeniden başlatın.", InfoBarSeverity.Warning, 10);
    }

    private async void OnWifi(object sender, RoutedEventArgs e)
    {
        WifiBtn.IsEnabled = false; WifiRing.IsActive = true;
        var dir = Path.Combine(Paths.Temp, "wifi-" + Guid.NewGuid().ToString("N"));
        try
        {
            var w = await Elevation.RunAsync([$"arac|wifi|{dir}"]);
            if (!w.Started) { Ui.Error("Şifreler okunamadı", w.Error ?? ""); return; }
            WifiList.Children.Clear();
            var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.xml") : [];
            if (files.Length == 0) WifiList.Children.Add(new TextBlock { Text = "Kayıtlı Wi-Fi ağı yok." });
            foreach (var f in files.OrderBy(x => x))
            {
                try
                {
                    var x = XDocument.Load(f);
                    XNamespace ns = x.Root!.Name.Namespace;
                    var name = x.Root.Element(ns + "name")?.Value ?? Path.GetFileNameWithoutExtension(f);
                    var key = x.Descendants(ns + "keyMaterial").FirstOrDefault()?.Value;
                    var auth = x.Descendants(ns + "authentication").FirstOrDefault()?.Value ?? "";
                    var row = new Grid { ColumnSpacing = 12 };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var nameTb = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                    var keyTb = new TextBlock { Text = key ?? (auth == "open" ? "(şifresiz ağ)" : "(şifre kayıtlı değil)"), IsTextSelectionEnabled = true, VerticalAlignment = VerticalAlignment.Center, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono,Consolas") };
                    var copy = new Button { Content = new FontIcon { Glyph = "", FontSize = 12 }, Padding = new Thickness(8), IsEnabled = key is not null };
                    ToolTipService.SetToolTip(copy, "Şifreyi kopyala");
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(copy, name + " şifresini kopyala");
                    copy.Click += (_, _) => { var d = new Windows.ApplicationModel.DataTransfer.DataPackage(); d.SetText(key!); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(d); Ui.Toast("Kopyalandı", name, InfoBarSeverity.Success, 2); };
                    Grid.SetColumn(keyTb, 1); Grid.SetColumn(copy, 2);
                    row.Children.Add(nameTb); row.Children.Add(keyTb); row.Children.Add(copy);
                    WifiList.Children.Add(row);
                }
                catch { }
            }
            WifiExpander.IsExpanded = true;
        }
        finally
        {
            // Şifreli dosyaları diskte bırakma
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            WifiBtn.IsEnabled = true; WifiRing.IsActive = false;
        }
    }
}
