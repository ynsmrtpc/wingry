using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Wingry.Core;
using Wingry.Services;

namespace Wingry.Pages;

public sealed partial class BloatwarePage : Page
{
    private List<BloatApp> _apps = [];

    public BloatwarePage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await LoadAsync();

    private async Task LoadAsync()
    {
        LoadingRing.IsActive = true; LoadingRing.Visibility = Visibility.Visible;
        try
        {
            _apps = await AppxService.GetInstalledBloatAsync();
            foreach (var a in _apps) a.PropertyChanged += OnAppChanged;
            List.ItemsSource = _apps;
            Empty.Visibility = _apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) { Ui.Error("Liste okunamadı", ex.Message); }
        finally { LoadingRing.IsActive = false; LoadingRing.Visibility = Visibility.Collapsed; }
        UpdateSummary();
    }

    private void OnAppChanged(object? s, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(BloatApp.Selected)) UpdateSummary(); }

    private void UpdateSummary()
    {
        int sel = _apps.Count(a => a.Selected);
        Summary.Text = $"{_apps.Count} uygulama yüklü · {sel} seçili";
        RemoveText.Text = sel > 0 ? $"{sel} uygulamayı kaldır" : "Seçilenleri kaldır";
        RemoveBtn.IsEnabled = sel > 0;
    }

    private void OnSelectRecommended(object sender, RoutedEventArgs e) { foreach (var a in _apps) a.Selected = a.Recommendation == "safe"; }

    private void OnSelectNone(object sender, RoutedEventArgs e) { foreach (var a in _apps) a.Selected = false; }

    private async void OnRemove(object sender, RoutedEventArgs e)
    {
        var sel = _apps.Where(a => a.Selected).ToList();
        if (sel.Count == 0) return;
        var risky = sel.Where(a => a.Recommendation == "unsafe").Select(a => a.FriendlyName).ToList();
        var msg = string.Join("\n", sel.Take(15).Select(a => "• " + a.FriendlyName)) + (sel.Count > 15 ? $"\n… ve {sel.Count - 15} diğer" : "");
        if (risky.Count > 0) msg += $"\n\nDikkat: {string.Join(", ", risky)} riskli olarak işaretli; bazı Windows özellikleri bunlara bağlı olabilir.";
        if (!await Ui.ConfirmAsync($"{sel.Count} uygulama kaldırılsın mı?", msg, "Kaldır", danger: risky.Count > 0)) return;

        RemoveBtn.IsEnabled = false; Ring.IsActive = true;
        int ok = 0; var errors = new List<string>();
        foreach (var a in sel)
        {
            a.Status = "Kaldırılıyor…";
            var errs = new List<string>();
            foreach (var full in a.FullNames)
                if (await AppxService.RemoveAsync(full) is { } err) errs.Add(err);
            a.Status = errs.Count == 0 ? "Kaldırıldı" : "Başarısız";
            if (errs.Count == 0) ok++; else errors.AddRange(errs);
        }
        Ring.IsActive = false;
        Log.Write($"Gereksiz uygulama kaldırma: {ok}/{sel.Count}");
        if (errors.Count > 0) await Ui.AlertAsync($"{sel.Count - ok} uygulama kaldırılamadı", string.Join("\n", errors.Take(20)));
        else Ui.Toast("Kaldırıldı", $"{ok} uygulama kaldırıldı.");
        await LoadAsync();
    }
}
