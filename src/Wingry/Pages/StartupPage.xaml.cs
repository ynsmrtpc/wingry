using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Wingry.Core;
using Wingry.Services;

namespace Wingry.Pages;

public sealed partial class StartupPage : Page
{
    private List<StartupItem> _all = [];
    private bool _busy;

    public StartupPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await LoadAsync();

    private async Task LoadAsync()
    {
        LoadingRing.IsActive = true; LoadingRing.Visibility = Visibility.Visible;
        _all = await Task.Run(StartupService.List);
        LoadingRing.IsActive = false; LoadingRing.Visibility = Visibility.Collapsed;
        Render();
    }

    private void Render()
    {
        var q = Query.Text.Trim();
        var list = q.Length == 0 ? _all : _all.Where(i =>
            i.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
            i.Publisher.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
            i.Command.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        _busy = true;
        List.ItemsSource = list;
        _busy = false;
        Summary.Text = $"{_all.Count} uygulama · {_all.Count(i => i.Enabled)} tanesi açılışta başlıyor";
    }

    private void OnQuery(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => Render();

    private async void OnRefresh(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void OnToggled(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not ToggleSwitch ts || ts.Tag is not StartupItem item || ts.IsOn == item.Enabled) return;
        bool want = ts.IsOn;
        ts.IsEnabled = false;
        var err = await StartupService.SetEnabledAsync(item, want);
        ts.IsEnabled = true;
        if (err is null)
        {
            item.Enabled = want;
            Log.Write($"Başlangıç {(want ? "açıldı" : "kapatıldı")}: {item.Name} ({item.Location})");
            Ui.Toast(want ? "Açılışta başlayacak" : "Açılışta başlamayacak", item.Name, InfoBarSeverity.Success, 3);
        }
        else
        {
            _busy = true; ts.IsOn = item.Enabled; _busy = false;
            Ui.Error("Değiştirilemedi", err);
        }
        Summary.Text = $"{_all.Count} uygulama · {_all.Count(i => i.Enabled)} tanesi açılışta başlıyor";
    }

    private void OnReveal(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not StartupItem item) return;
        var p = item.FilePath ?? item.ExePath;
        if (p is not null && File.Exists(p)) DiskService.ShowInExplorer(p);
        else Ui.Toast("Dosya bulunamadı", item.Command, InfoBarSeverity.Warning);
    }
}
