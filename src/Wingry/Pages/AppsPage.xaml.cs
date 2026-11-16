using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Wingry.Core;
using Wingry.Services;
using Windows.ApplicationModel.DataTransfer;

namespace Wingry.Pages;

/// <summary>Liste satırı. x:Bind için sabit değerler; durum değişince liste yeniden kurulur (hızlı).</summary>
public sealed class PackageRow
{
    public PackageRow(PackageInfo p, string tab)
    {
        Package = p;
        Name = p.Name;
        Initial = string.IsNullOrEmpty(p.Name) ? "?" : char.ToUpper(p.Name.TrimStart()[0], Format.Tr).ToString();
        Sub = string.IsNullOrEmpty(p.Publisher) ? p.Id : $"{p.Id}  ·  {p.Publisher}";
        if (tab == "discover")
        {
            VersionLabel = p.IsInstalled ? "Yüklü" : "Sürüm";
            Version = p.IsInstalled ? p.InstalledVersion : p.AvailableVersion;
            AvailableLabel = "Depodaki"; Available = p.IsInstalled ? p.AvailableVersion : "";
        }
        else
        {
            VersionLabel = "Yüklü sürüm"; Version = p.InstalledVersion;
            AvailableLabel = "Yeni sürüm"; Available = p.HasUpdate ? p.AvailableVersion : "";
        }
        HasAvailable = Available.Length > 0;
        Source = p.Source;
        HasSource = Source.Length > 0;
        AvatarOpacity = p.Source == "winget" || tab == "discover" ? 1.0 : 0.55;
    }

    public PackageInfo Package { get; }
    public string Name { get; }
    public string Initial { get; }
    public string Sub { get; }
    public string VersionLabel { get; }
    public string Version { get; }
    public string AvailableLabel { get; }
    public string Available { get; }
    public bool HasAvailable { get; }
    public string Source { get; }
    public bool HasSource { get; }
    public double AvatarOpacity { get; }
}

public sealed partial class AppsPage : Page
{
    public sealed record Args(string Tab, string? Query);

    private string _tab = "installed";
    private List<PackageInfo> _search = [];
    private CancellationTokenSource? _searchCts;
    private bool _searching;
    private string _lastSearch = "";
    private readonly ObservableCollection<PackageRow> _rows = [];
    private bool _bundlesBuilt;

    public AppsPage()
    {
        InitializeComponent();
        List.ItemsSource = _rows;
        // Paylaşılan klasöre dosya bırakılınca / paket kaydedilince listeyi yenile
        BundleService.Changed += () => { _bundlesBuilt = false; if (_tab == "bundles") BuildBundles(); };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        AppState.Changed += OnState;
        if (e.Parameter is Args a)
        {
            SelectTab(a.Tab);
            if (a.Query is { Length: > 0 } q) { Query.Text = q; _ = SearchAsync(q); }
        }
        Render();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => AppState.Changed -= OnState;

    private void SelectTab(string tab)
    {
        foreach (var item in Tabs.Items) item.IsSelected = (string)item.Tag == tab;
    }

    private void OnState() => Render();

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        _tab = sender.SelectedItem?.Tag as string ?? "installed";
        Query.Text = _tab == "discover" ? _lastSearch : "";
        Query.PlaceholderText = _tab == "discover" ? "winget deposunda ara (ör. vlc, 7zip)" : "Listede ara";
        bool bundles = _tab == "bundles";
        BundleArea.Visibility = bundles ? Visibility.Visible : Visibility.Collapsed;
        ListArea.Visibility = Toolbar.Visibility = bundles ? Visibility.Collapsed : Visibility.Visible;
        BtnInstall.Visibility = _tab == "discover" ? Visibility.Visible : Visibility.Collapsed;
        BtnUpdate.Visibility = _tab == "discover" ? Visibility.Collapsed : Visibility.Visible;
        BtnUpdateAll.Visibility = BtnIgnore.Visibility = _tab == "updates" ? Visibility.Visible : Visibility.Collapsed;
        BtnUninstall.Visibility = _tab == "discover" ? Visibility.Collapsed : Visibility.Visible;
        BtnMakeBundle.Visibility = _tab == "installed" ? Visibility.Visible : Visibility.Collapsed;
        Sort.Visibility = _tab == "discover" ? Visibility.Collapsed : Visibility.Visible;
        if (bundles) { _bundlesBuilt = false; BundleService.StartWatching(); BuildBundles(); }
        Render();
    }

    private IEnumerable<PackageInfo> Source()
    {
        var ign = Settings.GetSet(Settings.IgnoredUpdates);
        return _tab switch
        {
            "updates" => AppState.Installed.Where(p => p.HasUpdate && !ign.Contains(p.Id)),
            "discover" => _search,
            _ => AppState.Installed,
        };
    }

    private void Render()
    {
        // Sekme başlıklarında sayılar
        TabInstalled.Text = AppState.Installed.Count > 0 ? $"Yüklü ({AppState.Installed.Count})" : "Yüklü";
        int u = AppState.UpdateCount;
        TabUpdates.Text = u > 0 ? $"Güncellemeler ({u})" : "Güncellemeler";
        if (_tab == "bundles") { RefreshBundleState(); return; }

        IEnumerable<PackageInfo> src = Source();
        var q = Query.Text.Trim();
        if (_tab != "discover" && q.Length > 0)
            src = src.Where(p => p.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                              || p.Id.Contains(q, StringComparison.OrdinalIgnoreCase)
                              || p.Publisher.Contains(q, StringComparison.CurrentCultureIgnoreCase));
        if (_tab != "discover")
            src = Sort.SelectedIndex switch
            {
                1 => src.OrderBy(p => p.Publisher, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
                2 => src.OrderBy(p => p.Source).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
                _ => src,
            };

        var list = src.Select(p => new PackageRow(p, _tab)).ToList();
        _rows.Clear();
        foreach (var r in list) _rows.Add(r);
        SelectAll.IsChecked = false;

        bool loading = (_tab != "discover" && AppState.Loading && AppState.Installed.Count == 0) || (_tab == "discover" && _searching);
        Busy.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        BusyText.Text = _tab == "discover" ? "Depoda aranıyor…" : "Yüklü uygulamalar okunuyor…";
        Empty.Visibility = !loading && list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (list.Count == 0 && !loading)
        {
            (EmptyIcon.Glyph, EmptyText.Text, EmptyHint.Text) = _tab switch
            {
                "updates" when AppState.Error is null => ("", "Her şey güncel", "Yüklü uygulamaların hepsi son sürümde."),
                "discover" => ("", q.Length == 0 ? "Bir uygulama arayın" : "Sonuç yok", q.Length == 0 ? "Ad, kimlik veya etiketle arayıp Enter'a basın. Ör. \"tarayıcı\" değil \"firefox\"." : "Başka bir anahtar sözcük deneyin."),
                _ when AppState.Error is not null => ("", "winget'e ulaşılamadı", AppState.Error + "\nApp Installer (winget) kurulu ve güncel olmalı."),
                _ => ("", "Eşleşen uygulama yok", ""),
            };
        }

        var age = AppState.LoadedAt == default ? "" : $" · {AppState.LoadedAt:HH:mm} itibarıyla";
        Status.Text = _tab switch
        {
            "discover" => list.Count > 0 ? $"{list.Count} sonuç" : "",
            "updates" => $"{list.Count} güncelleme{age}",
            _ => $"{list.Count} uygulama{age}" + (AppState.Loading && AppState.Installed.Count > 0 ? " · yenileniyor…" : ""),
        };
        UpdateButtons();
    }

    private List<PackageInfo> Selected() => List.SelectedItems.OfType<PackageRow>().Select(r => r.Package).ToList();

    private void OnListSelection(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void UpdateButtons()
    {
        var sel = Selected();
        BtnInstall.IsEnabled = sel.Count > 0;
        BtnUpdate.IsEnabled = sel.Any(p => p.HasUpdate);
        BtnUninstall.IsEnabled = sel.Count > 0;
        BtnIgnore.IsEnabled = sel.Count > 0;
        BtnUpdateAll.IsEnabled = _tab == "updates" && _rows.Count > 0;
        BtnInstall.Label = sel.Count > 1 ? $"Kur ({sel.Count})" : "Kur";
        BtnUpdate.Label = sel.Count(p => p.HasUpdate) > 1 ? $"Güncelle ({sel.Count(p => p.HasUpdate)})" : "Güncelle";
        BtnUninstall.Label = sel.Count > 1 ? $"Kaldır ({sel.Count})" : "Kaldır";
    }

    private void OnSelectAll(object sender, RoutedEventArgs e)
    {
        if (SelectAll.IsChecked == true) List.SelectAll();
        else List.DeselectRange(new Microsoft.UI.Xaml.Data.ItemIndexRange(0, (uint)Math.Max(_rows.Count, 1)));
    }

    private void OnSortChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) Render(); }

    private CancellationTokenSource? _typeCts;
    private async void OnQueryChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        if (_tab != "discover") { Render(); return; }
        // Keşfet: yazmayı bırakınca ara
        _typeCts?.Cancel();
        _typeCts = new CancellationTokenSource();
        var t = _typeCts.Token;
        try { await Task.Delay(300, t); } catch { return; }
        if (sender.Text.Trim().Length >= 2) await SearchAsync(sender.Text.Trim());
    }

    private async void OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_tab == "discover" && sender.Text.Trim().Length > 0) await SearchAsync(sender.Text.Trim());
    }

    private async Task SearchAsync(string q)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        _searching = true; _lastSearch = q; Render();
        try
        {
            var r = await WingetService.SearchAsync(q, 80, ct, () => DispatcherQueue.TryEnqueue(() => { if (!ct.IsCancellationRequested) Render(); }));
            if (ct.IsCancellationRequested) return;
            _search = r;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { Ui.Error("Arama başarısız", ex.Message); _search = []; }
        finally { if (!ct.IsCancellationRequested) _searching = false; }
        Render();
    }

    // ---------------------------------------------------------------- işlemler

    private void Queue(OpKind kind, List<PackageInfo> pkgs)
    {
        if (pkgs.Count == 0) return;
        OperationQueue.Enqueue(kind, pkgs);
        var verb = kind switch { OpKind.Install => "kurulum", OpKind.Upgrade => "güncelleme", _ => "kaldırma" };
        Ui.Toast("Sıraya alındı", $"{pkgs.Count} {verb} işlemi başladı. İlerleme: İşlemler sayfası.", InfoBarSeverity.Informational, 4);
    }

    private void OnInstall(object sender, RoutedEventArgs e) => Queue(OpKind.Install, Selected().Where(p => !p.IsInstalled).ToList());

    private void OnUpdate(object sender, RoutedEventArgs e) => Queue(OpKind.Upgrade, Selected().Where(p => p.HasUpdate).ToList());

    private void OnUpdateAll(object sender, RoutedEventArgs e) => Queue(OpKind.Upgrade, Source().ToList());

    private async void OnUninstall(object sender, RoutedEventArgs e)
    {
        var sel = Selected();
        if (sel.Count == 0) return;
        var names = string.Join("\n", sel.Take(12).Select(p => "• " + p.Name)) + (sel.Count > 12 ? $"\n… ve {sel.Count - 12} diğer" : "");
        if (await Ui.ConfirmAsync($"{sel.Count} uygulama kaldırılsın mı?", names, "Kaldır", danger: true))
            Queue(OpKind.Uninstall, sel);
    }

    private void OnIgnore(object sender, RoutedEventArgs e)
    {
        var set = Settings.GetSet(Settings.IgnoredUpdates);
        foreach (var p in Selected()) set.Add(p.Id);
        Settings.SetSet(Settings.IgnoredUpdates, set);
        Ui.Toast("Yoksayıldı", "Bu uygulamaların güncellemeleri artık listelenmeyecek. Ayarlar'dan geri alabilirsiniz.", InfoBarSeverity.Informational);
        AppState.NotifyChanged();
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (_tab == "discover") { if (Query.Text.Trim().Length > 0) _ = SearchAsync(Query.Text.Trim()); }
        else _ = AppState.RefreshInstalledAsync();
    }

    private void OnRowMenu(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not PackageRow row) return;
        var p = row.Package;
        var menu = new MenuFlyout();
        void Add(string text, string glyph, Action a, bool enabled = true)
        {
            var it = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph }, IsEnabled = enabled };
            it.Click += (_, _) => a();
            menu.Items.Add(it);
        }
        if (!p.IsInstalled) Add("Kur", "", () => Queue(OpKind.Install, [p]));
        if (p.HasUpdate) Add($"{p.AvailableVersion} sürümüne güncelle", "", () => Queue(OpKind.Upgrade, [p]));
        if (p.IsInstalled)
            Add("Kaldır", "", async () =>
            {
                if (await Ui.ConfirmAsync($"{p.Name} kaldırılsın mı?", p.Id, "Kaldır", danger: true)) Queue(OpKind.Uninstall, [p]);
            });
        if (p.HasUpdate)
            Add("Bu güncellemeyi yoksay", "", () =>
            {
                var s = Settings.GetSet(Settings.IgnoredUpdates); s.Add(p.Id); Settings.SetSet(Settings.IgnoredUpdates, s); AppState.NotifyChanged();
            });
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Kimliği kopyala", "", () =>
        {
            var d = new DataPackage(); d.SetText(p.Id); Clipboard.SetContent(d);
        });
        Add("winget.run sayfasını aç", "", () =>
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://winget.run/pkg/" + p.Id.Replace('.', '/'))), p.Source == "winget");
        menu.ShowAt((FrameworkElement)sender);
    }

    // ---------------------------------------------------------------- hazır paketler

    private sealed class BundleView
    {
        public required Bundle Bundle;
        public readonly List<(BundleApp app, CheckBox box)> Items = [];
        public Button Install = null!;
        public TextBlock Summary = null!;
    }

    private readonly List<BundleView> _bundles = [];
    private static T Res<T>(string key) => (T)Application.Current.Resources[key];

    private void BuildBundles()
    {
        if (_bundlesBuilt) { RefreshBundleState(); return; }
        _bundlesBuilt = true;
        BundleCol0.Children.Clear(); BundleCol1.Children.Clear(); _bundles.Clear();

        var shared = BundleService.PrimarySharedDir;
        SharedInfo.Message = $"Paket dosyalarını (.json) şu klasöre bırakın, kendiliğinden burada görünür: {shared}  ·  winget export dosyaları da tanınır.";

        var cards = new List<UIElement>();
        foreach (var b in BundleService.LoadAll())
        {
            var v = new BundleView { Bundle = b };
            var apps = new StackPanel { Spacing = 0, Margin = new Thickness(0, 8, 0, 8) };
            foreach (var a in b.Apps)
            {
                var cb = new CheckBox { Content = a.Name, IsChecked = true, MinHeight = 32, Padding = new Thickness(8, 4, 0, 0) };
                cb.Checked += (_, _) => UpdateBundle(v);
                cb.Unchecked += (_, _) => UpdateBundle(v);
                ToolTipService.SetToolTip(cb, a.Id);
                v.Items.Add((a, cb));
                apps.Children.Add(cb);
            }
            v.Install = new Button { Style = Res<Style>("AccentButtonStyle"), HorizontalAlignment = HorizontalAlignment.Stretch };
            v.Install.Click += async (_, _) => await InstallBundleAsync(v);
            v.Summary = new TextBlock { Style = Res<Style>("Caption") };

            var originText = b.Origin switch { BundleOrigin.User => "Benim paketim", BundleOrigin.Folder => "Klasörden", _ => "Yerleşik" };
            var menuBtn = new Button { Padding = new Thickness(6), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), Content = new FontIcon { Glyph = "\uE712", FontSize = 14 }, VerticalAlignment = VerticalAlignment.Top };
            ToolTipService.SetToolTip(menuBtn, "Paket işlemleri");
            menuBtn.Click += (s, _) => ShowBundleMenu(b, (FrameworkElement)s);

            var header = new Grid { ColumnSpacing = 12 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(10),
                Background = Res<Microsoft.UI.Xaml.Media.Brush>("AccentFillColorDefaultBrush"),
                Child = new FontIcon { Glyph = b.Glyph, FontSize = 20, Foreground = Res<Microsoft.UI.Xaml.Media.Brush>("TextOnAccentFillColorPrimaryBrush") },
            });
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = b.Title, Style = Res<Style>("SubtitleTextBlockStyle"), FontSize = 18, TextTrimming = TextTrimming.CharacterEllipsis });
            var sub = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            sub.Children.Add(new Border
            {
                Padding = new Thickness(6, 0, 6, 1), CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center,
                Background = Res<Microsoft.UI.Xaml.Media.Brush>("SubtleFillColorSecondaryBrush"),
                Child = new TextBlock { Text = originText, FontSize = 11 },
            });
            sub.Children.Add(v.Summary);
            titles.Children.Add(sub);
            Grid.SetColumn(titles, 1); header.Children.Add(titles);
            Grid.SetColumn(menuBtn, 2); header.Children.Add(menuBtn);

            var body = new StackPanel();
            body.Children.Add(header);
            if (b.Description.Length > 0)
                body.Children.Add(new TextBlock { Text = b.Description, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, Style = Res<Style>("Caption") });
            body.Children.Add(apps);
            body.Children.Add(v.Install);
            _bundles.Add(v);
            cards.Add(new Border { Style = Res<Style>("Card"), Child = body });
        }
        // Kartları yükseklik dengesiyle iki sütuna dağıt (uygulama sayısına göre)
        int h0 = 0, h1 = 0;
        for (int i = 0; i < cards.Count; i++)
        {
            int weight = _bundles[i].Items.Count + 4;
            if (h0 <= h1) { BundleCol0.Children.Add(cards[i]); h0 += weight; }
            else { BundleCol1.Children.Add(cards[i]); h1 += weight; }
        }
        RefreshBundleState();
    }

    private void ShowBundleMenu(Bundle b, FrameworkElement anchor)
    {
        var menu = new MenuFlyout();
        void Add(string text, string glyph, Action a)
        {
            var it = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
            it.Click += (_, _) => a();
            menu.Items.Add(it);
        }
        if (b.Origin == BundleOrigin.User) Add("Düzenle", "\uE70F", async () => await EditBundleAsync(b.Title, b.Description, b.Apps, b.FilePath));
        else Add("Kopyasını düzenle", "\uE8C8", async () => await EditBundleAsync(b.Title + " (kopya)", b.Description, b.Apps, null));
        Add("Dışa aktar…", "\uEDE1", async () => await ExportBundleAsync(b));
        if (b.FilePath is not null) Add("Dosyanın konumunu aç", "\uE838", () => DiskService.ShowInExplorer(b.FilePath));
        if (b.Origin == BundleOrigin.User)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            Add("Sil", "\uE74D", async () =>
            {
                if (await Ui.ConfirmAsync($"\"{b.Title}\" silinsin mi?", "Paket dosyası Geri Dönüşüm Kutusu'na taşınır. Kurulu uygulamalar etkilenmez.", "Sil", danger: true))
                    BundleService.DeleteUser(b);
            });
        }
        menu.ShowAt(anchor);
    }

    private async Task EditBundleAsync(string title, string desc, IReadOnlyList<BundleApp> apps, string? file)
    {
        var r = await Controls.BundleEditor.ShowAsync(title, desc, apps, file is null ? "Paket oluştur" : "Paketi düzenle");
        if (r is null) return;
        var path = BundleService.SaveUser(r.Value.title, r.Value.desc, r.Value.apps, file);
        Ui.Toast("Paket kaydedildi", $"\"{r.Value.title}\" · {r.Value.apps.Count} uygulama. Dışa aktarıp başkalarıyla paylaşabilirsiniz.");
        if (_tab != "bundles") SelectTab("bundles");
    }

    private async void OnNewBundle(object sender, RoutedEventArgs e) => await EditBundleAsync("", "", [], null);

    private async void OnMakeBundle(object sender, RoutedEventArgs e)
    {
        var sel = Selected().Where(p => p.Source is "winget" or "msstore").ToList();
        int skipped = Selected().Count - sel.Count;
        if (sel.Count == 0)
        {
            Ui.Toast("Uygulama seçin", "Listeden paket yapmak istediğiniz uygulamaları işaretleyin (yalnızca winget/Store kaynaklı olanlar pakete girebilir).", InfoBarSeverity.Informational);
            return;
        }
        if (skipped > 0) Ui.Toast($"{skipped} uygulama atlandı", "Yerel / MSIX kaynaklı uygulamalar depoda olmadığı için başka bilgisayara kurulamaz.", InfoBarSeverity.Warning);
        await EditBundleAsync("", "", sel.Select(p => new BundleApp(p.Id, p.Name)).ToList(), null);
    }

    private async void OnImportBundle(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(App.Window!.AppWindow.Id);
            picker.FileTypeFilter.Add(".json");
            var res = await picker.PickSingleFileAsync();
            if (res is null || string.IsNullOrEmpty(res.Path)) return;
            var b = BundleService.Import(res.Path);
            if (b is null) { Ui.Error("İçe aktarılamadı", $"{Path.GetFileName(res.Path)} bir Wingry paketi ya da winget export dosyası değil."); return; }
            Ui.Toast("Paket içe aktarıldı", $"\"{b.Title}\" · {b.Apps.Count} uygulama.");
        }
        catch (Exception ex) { Ui.Error("İçe aktarılamadı", ex.Message); }
    }

    private async Task ExportBundleAsync(Bundle b)
    {
        try
        {
            var picker = new Microsoft.Windows.Storage.Pickers.FileSavePicker(App.Window!.AppWindow.Id)
            {
                SuggestedFileName = Path.GetFileNameWithoutExtension(BundleService.UniquePath(Path.GetTempPath(), b.Title)),
            };
            picker.FileTypeChoices.Add("Wingry paketi", [".json"]);
            var res = await picker.PickSaveFileAsync();
            if (res is null || string.IsNullOrEmpty(res.Path)) return;
            BundleService.Export(b, res.Path);
            Ui.Toast("Dışa aktarıldı", $"{Path.GetFileName(res.Path)} · Bu dosyayı alan kişi İçe aktar ile ya da Wingry'nin yanındaki Paketler klasörüne bırakarak kullanabilir.");
        }
        catch (Exception ex) { Ui.Error("Dışa aktarılamadı", ex.Message); }
    }

    private void OnOpenSharedFolder(object sender, RoutedEventArgs e)
    {
        try { DiskService.OpenFolder(BundleService.EnsureSharedDir()); }
        catch (Exception ex) { Ui.Error("Klasör açılamadı", ex.Message); }
    }

    private void RefreshBundleState()
    {
        var installed = AppState.Installed.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var active = OperationQueue.Items.Where(i => i.IsActive).Select(i => i.Package.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var v in _bundles)
        {
            foreach (var (app, box) in v.Items)
            {
                bool inst = installed.Contains(app.Id);
                box.IsEnabled = !inst && !active.Contains(app.Id);
                if (inst) { box.IsChecked = true; box.Content = $"{app.Name}  ✓ yüklü"; }
                else if (active.Contains(app.Id)) box.Content = $"{app.Name}  · sırada";
                else box.Content = app.Name;
            }
            UpdateBundle(v);
        }
    }

    private void UpdateBundle(BundleView v)
    {
        var installed = AppState.Installed.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int inst = v.Items.Count(i => installed.Contains(i.app.Id));
        int todo = v.Items.Count(i => i.box.IsEnabled && i.box.IsChecked == true);
        v.Summary.Text = $"{v.Items.Count} uygulama · {inst} yüklü";
        v.Install.Content = todo == 0 ? (inst == v.Items.Count ? "Hepsi yüklü" : "Seçili uygulama yok") : $"{todo} uygulamayı kur";
        v.Install.IsEnabled = todo > 0;
    }

    private async Task InstallBundleAsync(BundleView v)
    {
        var todo = v.Items.Where(i => i.box.IsEnabled && i.box.IsChecked == true).Select(i => i.app).ToList();
        if (todo.Count == 0) return;
        v.Install.IsEnabled = false;
        v.Install.Content = "Paketler bulunuyor…";
        var found = new List<PackageInfo>(); var missing = new List<string>();
        await Task.WhenAll(todo.Select(async a =>
        {
            try
            {
                var p = await WingetService.FindByIdAsync(a.Id);
                lock (found) { if (p is null) missing.Add(a.Name); else found.Add(p); }
            }
            catch { lock (found) missing.Add(a.Name); }
        }));
        // Paketteki sırayı koru
        found = found.OrderBy(p => todo.FindIndex(a => a.Id.Equals(p.Id, StringComparison.OrdinalIgnoreCase))).ToList();
        Queue(OpKind.Install, found.Where(p => !p.IsInstalled).ToList());
        if (missing.Count > 0) Ui.Error("Bazı paketler bulunamadı", string.Join(", ", missing));
        RefreshBundleState();
    }
}
