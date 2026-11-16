using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Wingry.Core;
using Wingry.Services;

namespace Wingry.Pages;

public sealed partial class ServicesPage : Page
{
    private List<HizmetItem> _all = [];
    private bool _loaded;

    public ServicesPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        if (!_loaded) await LoadAsync();
    }

    private async Task LoadAsync()
    {
        LoadingRing.IsActive = true; LoadingRing.Visibility = Visibility.Visible;
        try { _all = await Task.Run(HizmetService.List); _loaded = true; }
        catch (Exception ex) { Ui.Error("Hizmetler okunamadı", ex.Message); }
        LoadingRing.IsActive = false; LoadingRing.Visibility = Visibility.Collapsed;
        Render();
    }

    private void Render()
    {
        var q = Query.Text.Trim();
        IEnumerable<HizmetItem> src = Filter.SelectedIndex switch
        {
            1 => _all.Where(s => s.Running),
            2 => _all.Where(s => !s.Running),
            3 => _all.Where(s => s.StartType is "auto" or "delayed"),
            4 => _all.Where(s => s.StartType == "disabled"),
            5 => _all.Where(s => !s.IsMicrosoft),
            _ => _all,
        };
        if (q.Length > 0)
            src = src.Where(s => s.DisplayName.Contains(q, StringComparison.CurrentCultureIgnoreCase) || s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                              || s.Description.Contains(q, StringComparison.CurrentCultureIgnoreCase) || s.Publisher.Contains(q, StringComparison.CurrentCultureIgnoreCase));
        var list = src.ToList();
        List.ItemsSource = list;
        Summary.Text = $"{list.Count} hizmet · toplam {_all.Count(s => s.Running)} çalışıyor";
    }

    private void OnQuery(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => Render();
    private void OnFilter(object sender, SelectionChangedEventArgs e) { if (IsLoaded) Render(); }
    private async void OnRefresh(object sender, RoutedEventArgs e) => await LoadAsync();

    private void OnMenu(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not HizmetItem s) return;
        var menu = new MenuFlyout();
        void Add(MenuFlyoutItemBase host, string text, string glyph, Func<Task> a, bool enabled = true)
        {
            var it = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph }, IsEnabled = enabled };
            it.Click += async (_, _) => await a();
            if (host is MenuFlyoutSubItem sub) sub.Items.Add(it); else menu.Items.Add(it);
        }
        Add(null!, "Başlat", "", () => Do("start", s), !s.Running);
        Add(null!, "Durdur", "", () => Do("stop", s), s.Running && !s.IsCritical);
        Add(null!, "Yeniden başlat", "", () => Do("restart", s), s.Running);

        var types = new MenuFlyoutSubItem { Text = "Başlangıç türü", Icon = new FontIcon { Glyph = "" } };
        foreach (var (key, label) in new[] { ("auto", "Otomatik"), ("delayed", "Otomatik (gecikmeli)"), ("demand", "El ile"), ("disabled", "Devre dışı") })
            Add(types, (s.StartType == key ? "✓ " : "") + label, "", () => Do("type", s, key),
                s.StartType != key && !(s.IsCritical && key == "disabled") && s.StartType is not ("boot" or "system"));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(types);
        menu.ShowAt((FrameworkElement)sender);
    }

    private async Task Do(string action, HizmetItem s, string? type = null)
    {
        if (action == "stop" || type == "disabled")
        {
            var what = action == "stop" ? "durdurulsun" : "devre dışı bırakılsın";
            if (!await Ui.ConfirmAsync($"{s.DisplayName} {what} mı?",
                    (string.IsNullOrEmpty(s.Description) ? "" : s.Description + "\n\n") +
                    "Bu hizmete bağlı özellikler çalışmayabilir. Emin değilseniz 'El ile' seçeneği daha güvenlidir.", "Devam et", danger: true))
                return;
        }
        var err = await HizmetService.RunAsync(action, s, type);
        if (err is null) Ui.Toast("Tamamlandı", s.DisplayName, InfoBarSeverity.Success, 3);
        else Ui.Error("İşlem yapılamadı", err);
        await LoadAsync();
    }
}
