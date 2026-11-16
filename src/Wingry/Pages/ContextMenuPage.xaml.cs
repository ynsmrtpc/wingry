using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Wingry.Core;
using Wingry.Services;

namespace Wingry.Pages;

public sealed partial class ContextMenuPage : Page
{
    private static readonly string[] ScopeTexts = ["", "Dosyalar", "Dosya ve klasörler", "Klasörler", "Klasör arka planı", "Sürücüler"];
    private List<ContextEntry> _all = [];
    private bool _busy, _changed;

    public ContextMenuPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        LoadingRing.IsActive = true; LoadingRing.Visibility = Visibility.Visible;
        _all = await Task.Run(ContextMenuService.List);
        LoadingRing.IsActive = false; LoadingRing.Visibility = Visibility.Collapsed;
        Render();
    }

    private void Render()
    {
        var q = Query.Text.Trim();
        IEnumerable<ContextEntry> src = _all;
        if (ScopeFilter.SelectedIndex > 0) src = src.Where(e => e.ScopeText == ScopeTexts[ScopeFilter.SelectedIndex]);
        if (q.Length > 0) src = src.Where(e => e.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || e.Detail.Contains(q, StringComparison.OrdinalIgnoreCase));
        _busy = true; List.ItemsSource = src.ToList(); _busy = false;
        Summary.Text = $"{_all.Count} girdi · {_all.Count(e => !e.Enabled)} gizli";
    }

    private void OnQuery(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => Render();
    private void OnFilter(object sender, SelectionChangedEventArgs e) { if (IsLoaded) Render(); }

    private void OnToggled(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not ToggleSwitch ts || ts.Tag is not ContextEntry entry || ts.IsOn == entry.Enabled) return;
        var err = ContextMenuService.SetEnabled(entry, ts.IsOn);
        if (err is null) { entry.Enabled = ts.IsOn; _changed = true; }
        else { _busy = true; ts.IsOn = entry.Enabled; _busy = false; Ui.Error("Değiştirilemedi", err); }
        Summary.Text = $"{_all.Count} girdi · {_all.Count(x => !x.Enabled)} gizli" + (_changed ? " · görmek için Gezgin'i yeniden başlatın" : "");
    }

    private async void OnRestartExplorer(object sender, RoutedEventArgs e)
    {
        if (await Ui.ConfirmAsync("Gezgin yeniden başlatılsın mı?", "Açık Dosya Gezgini pencereleri kapanır; görev çubuğu birkaç saniye kaybolup geri gelir.", "Yeniden başlat"))
            TweakService.RestartExplorer();
    }
}
