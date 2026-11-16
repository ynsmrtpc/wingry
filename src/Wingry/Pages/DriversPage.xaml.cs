using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Wingry.Core;
using Wingry.Services;

namespace Wingry.Pages;

public sealed class DriverRow
{
    public required string Device { get; init; }
    public required string Provider { get; init; }
    public required string Class { get; init; }
    public required string Version { get; init; }
    public DateTime Date { get; init; }
    public string Inf { get; init; } = "";
    public bool IsMicrosoft => Provider.Contains("Microsoft", StringComparison.OrdinalIgnoreCase);
    public string DateText => Date == default ? "—" : Date.ToString("dd.MM.yyyy");
    public string Sub => string.Join("  ·  ", new[] { Provider, Class, Inf }.Where(s => s.Length > 0));
}

public sealed partial class DriversPage : Page
{
    private List<DriverRow> _all = [];
    private bool _loaded;

    public DriversPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e) { if (!_loaded) await LoadAsync(); }

    private async Task LoadAsync()
    {
        LoadingRing.IsActive = true; LoadingRing.Visibility = Visibility.Visible;
        // Win32_PnPSignedDriver yönetici istemez (~2-4 sn)
        var rows = await Task.Run(() => Tools.PsJson(
            "Get-CimInstance Win32_PnPSignedDriver | Where-Object { $_.DeviceName -and $_.DriverVersion } | " +
            "Select-Object DeviceName, DriverVersion, @{n='Tarih';e={ if ($_.DriverDate) { $_.DriverDate.ToString('yyyy-MM-dd') } else { '' } }}, DriverProviderName, DeviceClass, InfName", 120_000));
        _all = rows.Select(r => new DriverRow
        {
            Device = Tools.Str(r, "DeviceName"),
            Provider = Tools.Str(r, "DriverProviderName"),
            Class = Tools.Str(r, "DeviceClass"),
            Version = Tools.Str(r, "DriverVersion"),
            Inf = Tools.Str(r, "InfName"),
            // Intel çipset INF'leri bilerek 1968 tarihi taşır (Windows'un kendi sürücüsü öncelikli olsun diye): bilinmiyor say
            Date = DateTime.TryParse(Tools.Str(r, "Tarih"), out var d) && d.Year >= 1995 ? d : default,
        })
        .GroupBy(r => (r.Device, r.Version)).Select(g => g.First())
        .OrderBy(r => r.Device, StringComparer.CurrentCultureIgnoreCase).ToList();
        _loaded = true;
        LoadingRing.IsActive = false; LoadingRing.Visibility = Visibility.Collapsed;
        Render();
    }

    private void Render()
    {
        var q = Query.Text.Trim();
        IEnumerable<DriverRow> src = Filter.SelectedIndex switch
        {
            1 => _all.Where(d => !d.IsMicrosoft),
            2 => _all.Where(d => d.Date != default && d.Date < DateTime.Today.AddYears(-3)),
            _ => _all,
        };
        if (q.Length > 0) src = src.Where(d => d.Device.Contains(q, StringComparison.CurrentCultureIgnoreCase) || d.Provider.Contains(q, StringComparison.CurrentCultureIgnoreCase) || d.Class.Contains(q, StringComparison.OrdinalIgnoreCase));
        var list = src.ToList();
        List.ItemsSource = list;
        Summary.Text = $"{list.Count} sürücü · toplam {_all.Count}, {_all.Count(d => !d.IsMicrosoft)} tanesi Microsoft dışı";
    }

    private void OnQuery(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => Render();
    private void OnFilter(object sender, SelectionChangedEventArgs e) { if (_loaded) Render(); }

    private void OnUpdates(object sender, RoutedEventArgs e) => Tools.Open("ms-settings:windowsupdate-optionalupdates");

    private async void OnBackup(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(App.Window!.AppWindow.Id);
            var res = await picker.PickSingleFolderAsync();
            if (res is null || string.IsNullOrEmpty(res.Path)) return;
            var dir = Path.Combine(res.Path, $"Surucu-yedegi-{Environment.MachineName}-{DateTime.Now:yyyyMMdd}");
            BackupBtn.IsEnabled = false; BackupRing.IsActive = true;
            Ui.Toast("Sürücüler yedekleniyor", "Birkaç dakika sürebilir.", InfoBarSeverity.Informational, 5);
            var w = await Elevation.RunAsync([$"arac|surucu-yedek|{dir}"]);
            if (!w.Started) Ui.Error("Yedeklenemedi", w.Error ?? "");
            else if (w.Failures > 0) Ui.Error("Yedeklenemedi", string.Join("\n", w.Lines));
            else
            {
                int n = Directory.Exists(dir) ? Directory.GetDirectories(dir).Length : 0;
                Ui.Toast("Sürücüler yedeklendi", $"{n} sürücü paketi: {dir}", InfoBarSeverity.Success, 10);
                DiskService.OpenFolder(dir);
            }
        }
        catch (Exception ex) { Ui.Error("Yedeklenemedi", ex.Message); }
        finally { BackupBtn.IsEnabled = true; BackupRing.IsActive = false; }
    }
}
