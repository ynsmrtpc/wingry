using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Wingry.Core;
using Wingry.Services;

namespace Wingry.Pages;

public sealed partial class SettingsPage : Page
{
    private static readonly string[] Themes = ["sistem", "acik", "koyu"];
    private bool _init;

    public SettingsPage()
    {
        InitializeComponent();
        Theme.SelectedIndex = Math.Max(0, Array.IndexOf(Themes, Settings.GetString(Settings.Theme, "sistem")));
        Silent.IsOn = Settings.GetBool(Settings.SilentInstall, true);
        Store.IsOn = Settings.GetBool(Settings.IncludeStore, false);
        Batch.IsOn = Settings.GetBool(Settings.ElevatedBatch, true);
        AutoStartToggle.IsOn = AutoStart.IsEnabled;
        AutoMinToggle.IsOn = Settings.GetBool("baslangicta_kucuk", true);
        AutoMinToggle.IsEnabled = AutoStartToggle.IsOn;
        var ver = typeof(App).Assembly.GetName().Version;
        AboutText.Text =
            $"Sürüm {ver?.ToString(3)} · .NET {Environment.Version} · WinUI 3 (Windows App SDK)\n" +
            $"Konum: {Paths.AppDir}\n" +
            $"Ayarlar ve yedekler: {Paths.UserDir}\n\n" +
            $"Paket yönetimi Windows Paket Yöneticisi'nin (winget) resmî COM arayüzüyle yapılır.\n" +
            $"İnce ayar ve gereksiz uygulama tanımları: Win11Debloat, © Raphire, MIT lisansı (yapılandırma sürümü {TweakCatalog.Version}).\n" +
            $"Arayüz bileşenleri: Windows Community Toolkit, MIT lisansı.";
        UpdAuto.IsOn = Settings.GetBool("guncelleme_otomatik", true);
        _init = true;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        BuildIgnored();
        BuildBackups();
        ShowUpdateStatus();
        if (e.Parameter is "guncelle") { UpdExpander.IsExpanded = true; OnUpdCheck(this, new RoutedEventArgs()); }
    }

    // ---------------------------------------------------------------- Wingry güncellemesi

    private UpdateInfo? _update;
    private CancellationTokenSource? _dlCts;

    private void ShowUpdateStatus(string? extra = null)
    {
        var last = UpdateService.LastCheck;
        UpdStatus.Text = $"Yüklü sürüm {UpdateService.Current.ToString(3)}"
            + (last == default ? "" : $" · son denetim {Format.Date(last)}")
            + (extra is null ? "" : " · " + extra);
    }

    private async void OnUpdCheck(object sender, RoutedEventArgs e)
    {
        UpdCheck.IsEnabled = false; UpdRing.IsActive = true;
        try
        {
            _update = await UpdateService.CheckAsync();
            if (_update is null)
            {
                UpdInstall.Visibility = Visibility.Collapsed;
                UpdNotesCard.Visibility = Visibility.Collapsed;
                ShowUpdateStatus("Wingry güncel");
                return;
            }
            ShowUpdateStatus($"yeni sürüm var: {_update.Version.ToString(3)}");
            UpdInstall.Visibility = Visibility.Visible;
            UpdNotesCard.Visibility = Visibility.Visible;
            UpdNotesTitle.Text = $"Sürüm {_update.Version.ToString(3)}" + (_update.Published == default ? "" : $" · {Format.Date(_update.Published)}") + $" · {Format.Bytes(_update.Size)}";
            UpdNotes.Text = string.IsNullOrWhiteSpace(_update.Notes) ? "Sürüm notu yok." : _update.Notes.Trim();
            UpdExpander.IsExpanded = true;
        }
        catch (Exception ex)
        {
            ShowUpdateStatus("denetlenemedi");
            Ui.Error("Güncelleme denetlenemedi", ex.Message);
        }
        finally { UpdCheck.IsEnabled = true; UpdRing.IsActive = false; }
    }

    private async void OnUpdInstall(object sender, RoutedEventArgs e)
    {
        if (_update is null) return;
        if (OperationQueue.ActiveCount > 0 &&
            !await Ui.ConfirmAsync("Süren işlemler var", "Wingry yeniden başlayınca sıradaki kurulum/güncelleme işlemleri iptal olur. Yine de güncellensin mi?", "Güncelle", danger: true))
            return;

        UpdInstall.IsEnabled = UpdCheck.IsEnabled = false;
        UpdProgress.Visibility = Visibility.Visible; UpdProgress.Value = 0;
        _dlCts = new CancellationTokenSource();
        try
        {
            var file = await UpdateService.DownloadAsync(_update, new Progress<double>(p =>
            {
                UpdProgress.Value = p;
                ShowUpdateStatus($"indiriliyor %{p * 100:0}");
            }), _dlCts.Token);
            ShowUpdateStatus("yeniden başlatılıyor…");
            await Task.Delay(300);
            UpdateService.InstallAndRestart(file);
        }
        catch (Exception ex)
        {
            Ui.Error("Güncelleme indirilemedi", ex.Message);
            ShowUpdateStatus("indirme başarısız");
            UpdInstall.IsEnabled = UpdCheck.IsEnabled = true;
            UpdProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void OnUpdAuto(object sender, RoutedEventArgs e) { if (_init) Settings.Set("guncelleme_otomatik", UpdAuto.IsOn); }

    private void OnTheme(object sender, SelectionChangedEventArgs e)
    {
        if (!_init) return;
        var t = Themes[Math.Max(0, Theme.SelectedIndex)];
        Settings.Set(Settings.Theme, t);
        App.Window?.ApplyTheme(t);
    }

    private void OnSilent(object sender, RoutedEventArgs e) { if (_init) Settings.Set(Settings.SilentInstall, Silent.IsOn); }

    private void OnAutoStart(object sender, RoutedEventArgs e)
    {
        if (!_init) return;
        if (AutoStart.Set(AutoStartToggle.IsOn) is { } err)
        {
            Ui.Error("Değiştirilemedi", err);
            _init = false; AutoStartToggle.IsOn = AutoStart.IsEnabled; _init = true;
        }
        AutoMinToggle.IsEnabled = AutoStartToggle.IsOn;
    }

    private void OnAutoMin(object sender, RoutedEventArgs e) { if (_init) Settings.Set("baslangicta_kucuk", AutoMinToggle.IsOn); }

    private void OnBatch(object sender, RoutedEventArgs e) { if (_init) Settings.Set(Settings.ElevatedBatch, Batch.IsOn); }

    private void OnStore(object sender, RoutedEventArgs e)
    {
        if (!_init) return;
        Settings.Set(Settings.IncludeStore, Store.IsOn);
        _ = AppState.RefreshInstalledAsync();
    }

    private void BuildIgnored()
    {
        IgnoredList.Children.Clear();
        var set = Settings.GetSet(Settings.IgnoredUpdates);
        IgnoredExpander.Description = set.Count == 0 ? "Yoksayılan güncelleme yok." : $"{set.Count} paket yoksayılıyor.";
        foreach (var id in set.Order())
        {
            var name = AppState.Installed.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name ?? id;
            var btn = new Button { Content = "Geri al", Tag = id };
            btn.Click += (s, _) =>
            {
                var cur = Settings.GetSet(Settings.IgnoredUpdates);
                cur.Remove((string)((Button)s).Tag);
                Settings.SetSet(Settings.IgnoredUpdates, cur);
                AppState.NotifyChanged();
                BuildIgnored();
            };
            var row = new Grid { ColumnSpacing = 12, MinWidth = 520 };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = $"{name}  ·  {id}", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(btn, 1);
            row.Children.Add(btn);
            IgnoredList.Children.Add(row);
        }
    }

    private void BuildBackups()
    {
        BackupList.Children.Clear();
        var files = new DirectoryInfo(Paths.Backups).EnumerateFiles("*.reg").OrderByDescending(f => f.LastWriteTime).Take(15).ToList();
        BackupExpander.Description = files.Count == 0 ? "Henüz yedek yok." : $"Son yedek: {Format.Date(files[0].LastWriteTime)}";
        foreach (var f in files)
        {
            string title = "";
            try { title = File.ReadLines(f.FullName).Skip(2).FirstOrDefault()?.Replace("; Wingry yedeği:", "").Trim() ?? ""; } catch { }
            var btn = new Button { Content = "Geri yükle", Tag = f.FullName };
            btn.Click += async (s, _) =>
            {
                var path = (string)((Button)s).Tag;
                if (!await Ui.ConfirmAsync("Yedek geri yüklensin mi?", $"{Path.GetFileName(path)} içindeki değerler kayıt defterine geri yazılır.\n\n{title}", "Geri yükle")) return;
                var errs = await TweakService.RestoreBackupAsync(path);
                if (errs.Count == 0) Ui.Toast("Geri yüklendi", "Bazı değişiklikler için Gezgin'i ya da bilgisayarı yeniden başlatın.");
                else await Ui.AlertAsync("Kısmen geri yüklendi", string.Join("\n", errs));
            };
            var row = new Grid { ColumnSpacing = 12, MinWidth = 520 };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = Format.Date(f.LastWriteTime) },
                    new TextBlock { Text = title, Style = (Style)Application.Current.Resources["Caption"], TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 640 },
                },
            });
            Grid.SetColumn(btn, 1);
            row.Children.Add(btn);
            BackupList.Children.Add(row);
        }
    }

    private async void OnRestorePoint(object sender, RoutedEventArgs e)
    {
        RpButton.IsEnabled = false; RpRing.IsActive = true;
        try
        {
            var w = await Elevation.RunAsync(["restorepoint"]);
            if (!w.Started) Ui.Error("Oluşturulamadı", w.Error ?? "");
            else if (w.Failures > 0) await Ui.AlertAsync("Geri yükleme noktası oluşturulamadı", string.Join("\n", w.Lines));
            else Ui.Toast("Geri yükleme noktası oluşturuldu", "Denetim Masası > Kurtarma'dan bu noktaya dönebilirsiniz.");
        }
        finally { RpButton.IsEnabled = true; RpRing.IsActive = false; }
    }

    private void OnOpenBackups(object sender, RoutedEventArgs e) => DiskService.OpenFolder(Paths.Backups);
    private void OnOpenLogs(object sender, RoutedEventArgs e) => DiskService.OpenFolder(Paths.Logs);
}
