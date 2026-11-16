using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wingry.Core;
using Wingry.Services;

namespace Wingry.Pages;

public sealed partial class MigrationPage : Page
{
    private ProfileService.Plan? _plan;

    public MigrationPage() => InitializeComponent();

    private static string Preview(IEnumerable<string> items, int max = 12)
    {
        var l = items.ToList();
        return l.Count == 0 ? "—" : string.Join(", ", l.Take(max)) + (l.Count > max ? $" ve {l.Count - max} diğer" : "");
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Microsoft.Windows.Storage.Pickers.FileSavePicker(App.Window!.AppWindow.Id)
            {
                SuggestedFileName = $"Wingry-profil-{Environment.MachineName}-{DateTime.Now:yyyyMMdd}",
            };
            picker.FileTypeChoices.Add("Wingry profili", [".json"]);
            var res = await picker.PickSaveFileAsync();
            if (res is null || string.IsNullOrEmpty(res.Path)) return;
            ExportBtn.IsEnabled = false; ExportRing.IsActive = true;
            var p = await ProfileService.CaptureAsync();
            ProfileService.Save(p, res.Path);
            Ui.Toast("Profil kaydedildi", $"{p.Apps.Count} uygulama · {p.Tweaks.Count} ince ayar · {p.StartupOff.Count} kapalı başlangıç öğesi → {Path.GetFileName(res.Path)}", InfoBarSeverity.Success, 8);
            Log.Write($"Profil dışa aktarıldı: {res.Path}");
        }
        catch (Exception ex) { Ui.Error("Profil kaydedilemedi", ex.Message); }
        finally { ExportBtn.IsEnabled = true; ExportRing.IsActive = false; }
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(App.Window!.AppWindow.Id);
            picker.FileTypeFilter.Add(".json");
            var res = await picker.PickSingleFileAsync();
            if (res is null || string.IsNullOrEmpty(res.Path)) return;
            var p = ProfileService.Load(res.Path);
            if (p is null) { Ui.Error("Profil okunamadı", "Seçilen dosya bir Wingry profili değil. Paket dosyaları için Uygulamalar > Hazır paketler > İçe aktar'ı kullanın."); return; }
            ImportBtn.IsEnabled = false; ImportRing.IsActive = true;
            if (AppState.Installed.Count == 0) await AppState.RefreshInstalledAsync();
            _plan = await ProfileService.PlanAsync(p);

            PlanTitle.Text = $"{p.Machine} profili";
            PlanSource.Text = $"{p.Os} · {Format.Date(p.Created)} · {Path.GetFileName(res.Path)}";
            ChkApps.Content = $"Eksik {_plan.MissingApps.Count} uygulamayı kur (profilde {p.Apps.Count})";
            AppsList.Text = Preview(_plan.MissingApps.Select(a => a.Name.Length > 0 ? a.Name : a.Id));
            ChkTweaks.Content = $"{_plan.Tweaks.Count} ince ayarı uygula (profilde {p.Tweaks.Count})";
            TweaksList.Text = Preview(_plan.Tweaks.Select(t => t.Label), 8);
            ChkStartup.Content = $"{_plan.Startup.Count} programın açılışta başlamasını kapat";
            StartupList.Text = Preview(_plan.Startup.Select(s => s.Name));
            ChkApps.IsEnabled = ChkApps.IsChecked == true && _plan.MissingApps.Count > 0;
            ChkTweaks.IsEnabled = _plan.Tweaks.Count > 0;
            ChkStartup.IsEnabled = _plan.Startup.Count > 0;
            ChkApps.IsEnabled = _plan.MissingApps.Count > 0;
            ApplyStatus.Text = _plan.MissingApps.Count + _plan.Tweaks.Count + _plan.Startup.Count == 0 ? "Bu bilgisayar profille zaten aynı." : "";
            PlanCard.Visibility = Visibility.Visible;
        }
        catch (Exception ex) { Ui.Error("Profil okunamadı", ex.Message); }
        finally { ImportBtn.IsEnabled = true; ImportRing.IsActive = false; }
    }

    private void OnCancelPlan(object sender, RoutedEventArgs e) { _plan = null; PlanCard.Visibility = Visibility.Collapsed; }

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        if (_plan is null) return;
        ApplyBtn.IsEnabled = false;
        var errors = new List<string>();
        try
        {
            if (ChkTweaks.IsChecked == true && _plan.Tweaks.Count > 0 || ChkRestore.IsChecked == true)
            {
                ApplyStatus.Text = "İnce ayarlar uygulanıyor…";
                var tw = ChkTweaks.IsChecked == true ? _plan.Tweaks.Select(f => new TweakChange(f, true)).ToList() : [];
                var r = await TweakService.ApplyAsync(tw, ChkRestore.IsChecked == true);
                errors.AddRange(r.Errors);
            }
            if (ChkStartup.IsChecked == true)
            {
                ApplyStatus.Text = "Başlangıç programları kapatılıyor…";
                foreach (var s in _plan.Startup)
                    if (await StartupService.SetEnabledAsync(s, false) is { } err) errors.Add($"{s.Name}: {err}");
            }
            if (ChkApps.IsChecked == true && _plan.MissingApps.Count > 0)
            {
                ApplyStatus.Text = "Uygulamalar depoda bulunuyor…";
                var found = new List<PackageInfo>();
                await Task.WhenAll(_plan.MissingApps.Select(async a =>
                {
                    try { var p = await WingetService.FindByIdAsync(a.Id); lock (found) if (p is not null) found.Add(p); else errors.Add($"Depoda yok: {a.Name}"); }
                    catch { lock (found) errors.Add($"Bulunamadı: {a.Name}"); }
                }));
                OperationQueue.Enqueue(OpKind.Install, found);
            }
            ApplyStatus.Text = "Tamamlandı. Kurulumlar İşlemler sayfasında sürüyor.";
            Log.Write($"Profil uygulandı: {errors.Count} hata");
            if (errors.Count > 0) await Ui.AlertAsync($"{errors.Count} öğe uygulanamadı", string.Join("\n", errors.Take(30)));
            else Ui.Toast("Profil uygulandı", "Uygulama kurulumları sırayla devam ediyor.", InfoBarSeverity.Success, 8);
        }
        finally { ApplyBtn.IsEnabled = true; }
    }
}
