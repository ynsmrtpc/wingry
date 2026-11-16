using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Wingry.Core;

namespace Wingry.Pages;

public sealed partial class RepairPage : Page
{
    public RepairPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await LoadDisksAsync();

    private async Task LoadDisksAsync()
    {
        Disks.Children.Clear();
        Disks.Children.Add(new ProgressRing { IsActive = true, HorizontalAlignment = HorizontalAlignment.Left });
        // Yönetici gerektirmez. Sayısal enum değerleri: MediaType 3=HDD 4=SSD; HealthStatus 0=Sağlıklı 1=Uyarı 2=Sağlıksız
        var disks = await Task.Run(() => Tools.PsJson(
            "Get-PhysicalDisk | Select-Object FriendlyName, @{n='Medya';e={[string]$_.MediaType}}, @{n='Saglik';e={[string]$_.HealthStatus}}, @{n='Durum';e={[string]($_.OperationalStatus -join ',')}}, Size, @{n='Yol';e={[string]$_.BusType}}"));
        Disks.Children.Clear();
        if (disks.Count == 0) { Disks.Children.Add(new TextBlock { Text = "Disk bilgisi okunamadı.", Style = (Style)Application.Current.Resources["Caption"] }); return; }
        foreach (var d in disks)
        {
            var media = Tools.Str(d, "Medya") switch { "4" or "SSD" => "SSD", "3" or "HDD" => "HDD", "5" or "SCM" => "SCM", _ => "Disk" };
            var health = Tools.Str(d, "Saglik") switch { "0" or "Healthy" => "Sağlıklı", "1" or "Warning" => "Uyarı", "2" or "Unhealthy" => "Sağlıksız", var x => x };
            var bus = Tools.Str(d, "Yol") switch { "17" or "NVMe" => "NVMe", "11" or "SATA" => "SATA", "7" or "USB" => "USB", "8" or "RAID" => "RAID", var x => x };
            long.TryParse(Tools.Str(d, "Size"), out var size);
            bool ok = health == "Sağlıklı";
            Disks.Children.Add(new SettingsCard
            {
                Header = Tools.Str(d, "FriendlyName"),
                Description = $"{media} · {bus} · {Format.Bytes(size)}",
                HeaderIcon = new FontIcon { Glyph = "" },
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children =
                    {
                        new FontIcon { Glyph = ok ? "" : "", FontSize = 16, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[ok ? "SystemFillColorSuccessBrush" : "SystemFillColorCriticalBrush"] },
                        new TextBlock { Text = health, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            });
        }
    }

    private async Task RunAsync(string cmd, Button btn, ProgressRing ring, string title)
    {
        btn.IsEnabled = false; ring.IsActive = true;
        var started = DateTime.Now;
        Ui.Toast(title + " başladı", "Bitince bildirim göreceksiniz. Bu sırada Wingry'yi kullanabilirsiniz.", InfoBarSeverity.Informational, 6);
        try
        {
            var w = await Elevation.RunAsync([cmd]);
            var took = Format.Duration(DateTime.Now - started);
            Log.Write($"{title}: {string.Join(" ", w.Lines)} ({took})");
            if (!w.Started) Ui.Error(title + " başlatılamadı", w.Error ?? "");
            else if (w.Failures > 0) Ui.Error(title + " sorun bildirdi", string.Join("\n", w.Lines) + "\nAyrıntılar: Onarım günlükleri.");
            else Ui.Toast(title + " tamamlandı", $"Süre: {took}. Ayrıntılar onarım günlüklerinde.", InfoBarSeverity.Success, 10);
        }
        finally { btn.IsEnabled = true; ring.IsActive = false; }
    }

    private async void OnDism(object sender, RoutedEventArgs e) => await RunAsync("arac|dism|restore", DismBtn, DismRing, "DISM onarımı");
    private async void OnSfc(object sender, RoutedEventArgs e) => await RunAsync("arac|sfc", SfcBtn, SfcRing, "SFC taraması");

    private async void OnClean(object sender, RoutedEventArgs e)
    {
        if (!await Ui.ConfirmAsync("Bileşen deposu temizlensin mi?", "Eski güncelleme dosyaları silinir; kurulu güncellemeleri kaldırma seçeneği ortadan kalkar.", "Temizle")) return;
        await RunAsync("arac|dism|cleanup", CleanBtn, CleanRing, "Bileşen temizliği");
    }

    private void OnLogs(object sender, RoutedEventArgs e) => Tools.Open(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs"));
}
