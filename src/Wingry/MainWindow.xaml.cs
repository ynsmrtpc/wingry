using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Wingry.Core;
using Wingry.Pages;
using Wingry.Services;

namespace Wingry;

public sealed partial class MainWindow : Window
{
    private sealed record SearchItem(string Text, string Detail, string Tag, object? Param)
    {
        public override string ToString() => Text;
    }

    private readonly List<SearchItem> _searchIndex = [];

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Path.Combine(Paths.AppDir, "Assets", "wingry.ico"));
        AppWindow.Title = "Wingry";

        if (AppWindow.Presenter is OverlappedPresenter op)
        {
            op.PreferredMinimumWidth = 900;
            op.PreferredMinimumHeight = 620;
        }
        CenterOnScreen(1280, 860);

        ApplyTheme(Settings.GetString(Settings.Theme, "sistem"));
        OperationQueue.Init(DispatcherQueue);

        AdminText.Text = Elevation.IsAdmin ? "Yönetici" : "Yönetici değil";
        AdminButton.IsEnabled = !Elevation.IsAdmin;

        BuildTweakMenu();
        BuildSearchIndex();

        Nav.Loaded += (_, _) => { UpdateNavTooltips(); HideNavScrollBar(); };
        AppState.Changed += UpdateBadges;
        OperationQueue.Changed += UpdateBadges;

        Nav.Loaded += (_, _) =>
        {
            _navReady = true;
            Navigate(_startTag ?? "dashboard", _startParam);
        };
    }

    private bool _navReady;
    private string? _startTag;
    private object? _startParam;

    private void CenterOnScreen(int w, int h)
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        double scale = (Content as FrameworkElement)?.XamlRoot?.RasterizationScale ?? GetDpiScale();
        int pw = Math.Min((int)(w * scale), area.Width - 40), ph = Math.Min((int)(h * scale), area.Height - 40);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(area.X + (area.Width - pw) / 2, area.Y + (area.Height - ph) / 2, pw, ph));
    }

    private double GetDpiScale()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        return GetDpiForWindow(hwnd) / 96.0;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    public void ApplyTheme(string theme)
    {
        if (Content is FrameworkElement fe)
            fe.RequestedTheme = theme switch { "acik" => ElementTheme.Light, "koyu" => ElementTheme.Dark, _ => ElementTheme.Default };
    }

    private void BuildTweakMenu()
    {
        TweakCatalog.Load();
        int at = Nav.MenuItems.IndexOf(TweaksHeader) + 1;
        foreach (var c in TweakCatalog.Categories)
        {
            if (!TweakCatalog.StandaloneFeatures(c).Any() && !TweakCatalog.GroupsOf(c).Any()) continue;
            Nav.MenuItems.Insert(at++, new NavigationViewItem
            {
                Content = c.Label,
                Tag = "tweak:" + c.Id,
                Icon = new FontIcon { Glyph = c.Glyph },
            });
        }
        Nav.MenuItems.Insert(at, new NavigationViewItem
        {
            Content = "Gereksiz uygulamalar",
            Tag = "bloat",
            Icon = new FontIcon { Glyph = "" },
        });
    }

    private void BuildSearchIndex()
    {
        void Page(string text, string tag, string detail = "Sayfa", object? p = null) => _searchIndex.Add(new SearchItem(text, detail, tag, p));
        Page("Genel bakış", "dashboard");
        Page("Yüklü uygulamalar", "apps", "Uygulamalar", new AppsPage.Args("installed", null));
        Page("Uygulama güncellemeleri", "apps", "Uygulamalar", new AppsPage.Args("updates", null));
        Page("Hazır paketler", "apps", "Uygulamalar", new AppsPage.Args("bundles", null));
        Page("Uygulama keşfet / kur", "apps", "Uygulamalar", new AppsPage.Args("discover", null));
        Page("RAM temizleme", "memory");
        Page("Disk alanı analizi", "disk");
        Page("Disk temizliği", "disk", "Disk alanı", "cleanup");
        Page("Zamanlayıcılar", "scheduler");
        Page("Başlangıç uygulamaları", "startup");
        Page("Açılışta başlayan programlar", "startup", "Başlangıç uygulamaları");
        Page("Hizmetler", "services");
        Page("Taşıma ve yedek", "migration", "Araçlar");
        Page("Yeni bilgisayara taşı / profil", "migration", "Araçlar");
        Page("Sürücüler", "drivers", "Araçlar");
        Page("Sürücü yedekle", "drivers", "Sürücüler");
        Page("Ağ", "network", "Araçlar");
        Page("DNS değiştir", "network", "Ağ");
        Page("Wi-Fi şifreleri", "network", "Ağ");
        Page("DNS önbelleğini temizle", "network", "Ağ");
        Page("Ağı sıfırla", "network", "Ağ");
        Page("Güç planı", "power", "Güç ve pil");
        Page("Pil raporu", "power", "Güç ve pil");
        Page("Hazırda bekletme (hiberfil.sys)", "power", "Güç ve pil");
        Page("Sağ tık menüsü", "contextmenu", "Araçlar");
        Page("Bağlam menüsü", "contextmenu", "Araçlar");
        Page("SFC / DISM onarım", "repair", "Sistem onarımı");
        Page("Disk sağlığı", "repair", "Sistem onarımı");
        Page("Hosts dosyası", "hosts", "Araçlar");
        Page("Reklam engelleme", "hosts", "Hosts dosyası");
        Page("Kopya dosyalar", "disk", "Disk alanı", "dups");
        Page("Windows hizmetleri", "services", "Hizmetler");
        Page("Paket oluştur / içe aktar", "apps", "Uygulamalar", new AppsPage.Args("bundles", null));
        Page("Gereksiz uygulamaları kaldır", "bloat");
        Page("Ayarlar", "settings");
        Page("Geri yükleme noktası", "settings", "Ayarlar");
        foreach (var c in TweakCatalog.Categories)
        {
            Page(c.Label, "tweak:" + c.Id, "İnce ayarlar");
            foreach (var f in TweakCatalog.StandaloneFeatures(c))
                _searchIndex.Add(new SearchItem(f.Label, c.Label, "tweak:" + c.Id, f.Id));
            foreach (var g in TweakCatalog.GroupsOf(c))
                _searchIndex.Add(new SearchItem(g.Label, c.Label, "tweak:" + c.Id, g.Id));
        }
    }

    private void UpdateBadges()
    {
        int u = AppState.UpdateCount;
        UpdateBadge.Value = u;
        UpdateBadge.Visibility = u > 0 ? Visibility.Visible : Visibility.Collapsed;
        int a = OperationQueue.ActiveCount;
        OpsBadge.Value = a;
        OpsBadge.Visibility = a > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------- gezinme

    public void Navigate(string tag, object? param = null)
    {
        if (!_navReady) { _startTag = tag; _startParam = param; return; }
        var item = Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>().FirstOrDefault(i => (string?)i.Tag == tag);
        if (tag == "settings") item = Nav.SettingsItem as NavigationViewItem;
        _pendingParam = param;
        if (item is not null && !ReferenceEquals(Nav.SelectedItem, item)) Nav.SelectedItem = item;
        else NavigateTo(tag, param);
    }

    private object? _pendingParam;

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = args.IsSettingsSelected ? "settings" : (args.SelectedItemContainer?.Tag as string ?? "dashboard");
        NavigateTo(tag, _pendingParam);
        _pendingParam = null;
    }

    private void NavigateTo(string tag, object? param)
    {
        Type page; object? arg = param;
        switch (tag)
        {
            case "dashboard": page = typeof(DashboardPage); break;
            case "apps": page = typeof(AppsPage); break;
            case "memory": page = typeof(MemoryPage); break;
            case "disk": page = typeof(DiskPage); break;
            case "scheduler": page = typeof(SchedulerPage); break;
            case "startup": page = typeof(StartupPage); break;
            case "services": page = typeof(ServicesPage); break;
            case "migration": page = typeof(MigrationPage); break;
            case "drivers": page = typeof(DriversPage); break;
            case "network": page = typeof(NetworkPage); break;
            case "power": page = typeof(PowerPage); break;
            case "contextmenu": page = typeof(ContextMenuPage); break;
            case "repair": page = typeof(RepairPage); break;
            case "hosts": page = typeof(HostsPage); break;
            case "bloat": page = typeof(BloatwarePage); break;
            case "ops": page = typeof(OperationsPage); break;
            case "settings": page = typeof(SettingsPage); break;
            default:
                if (tag.StartsWith("tweak:", StringComparison.Ordinal))
                {
                    page = typeof(TweaksPage);
                    arg = new TweaksPage.Args(tag[6..], param as string);
                    break;
                }
                return;
        }
        if (ContentFrame.CurrentSourcePageType == page && page != typeof(TweaksPage) && arg is null) return;
        ContentFrame.Navigate(page, arg, new EntranceNavigationTransitionInfo());
    }

    private void OnNavigated(object sender, NavigationEventArgs e)
    {
        // Geri gidince menü seçimini sayfayla eşitle
        string? tag = e.SourcePageType.Name switch
        {
            nameof(DashboardPage) => "dashboard",
            nameof(AppsPage) => "apps",
            nameof(MemoryPage) => "memory",
            nameof(DiskPage) => "disk",
            nameof(SchedulerPage) => "scheduler",
            nameof(StartupPage) => "startup",
            nameof(ServicesPage) => "services",
            nameof(MigrationPage) => "migration",
            nameof(DriversPage) => "drivers",
            nameof(NetworkPage) => "network",
            nameof(PowerPage) => "power",
            nameof(ContextMenuPage) => "contextmenu",
            nameof(RepairPage) => "repair",
            nameof(HostsPage) => "hosts",
            nameof(BloatwarePage) => "bloat",
            nameof(OperationsPage) => "ops",
            nameof(TweaksPage) => e.Parameter is TweaksPage.Args a ? "tweak:" + a.CategoryId : null,
            _ => null,
        };
        if (e.SourcePageType == typeof(SettingsPage)) { Nav.SelectionChanged -= OnNavSelectionChanged; Nav.SelectedItem = Nav.SettingsItem; Nav.SelectionChanged += OnNavSelectionChanged; return; }
        var item = Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>().FirstOrDefault(i => (string?)i.Tag == tag);
        if (item is not null && !ReferenceEquals(Nav.SelectedItem, item))
        {
            Nav.SelectionChanged -= OnNavSelectionChanged;
            Nav.SelectedItem = item;
            Nav.SelectionChanged += OnNavSelectionChanged;
        }
    }

    private void OnBackRequested(TitleBar sender, object args)
    {
        if (ContentFrame.CanGoBack) ContentFrame.GoBack();
    }

    private void OnPaneToggleRequested(TitleBar sender, object args) => Nav.IsPaneOpen = !Nav.IsPaneOpen;

    private void OnPaneChanged(NavigationView sender, object args) => UpdateNavTooltips();
    private void OnDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args) => UpdateNavTooltips();

    /// <summary>Menü daraltılmışken simgelerin üstüne gelince sağda adı görünsün; açıkken ad zaten yazılı.</summary>
    /// <summary>
    /// Sol menünün kaydırma çubuğunu gizle (dar menüde simgelerin yanında çirkin duruyordu).
    /// Kaydırma fare tekerleği / dokunma ile çalışmaya devam eder.
    /// </summary>
    private void HideNavScrollBar()
    {
        foreach (var sv in Descendants(Nav).OfType<ScrollViewer>().Where(s => s.Name == "MenuItemsScrollViewer"))
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int n = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    private readonly HashSet<NavigationViewItem> _tipHooked = [];

    /// <summary>
    /// Daraltılmış menüde ad ipucu. WinUI'nin kendi gecikmesi (~1 sn) değiştirilemediği için ipucu
    /// fare girince elle açılır, çıkınca kapanır; klavyeyle odaklanınca da görünür.
    /// </summary>
    private void UpdateNavTooltips()
    {
        bool compact = !Nav.IsPaneOpen;
        var items = Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>().ToList();
        if (Nav.SettingsItem is NavigationViewItem s) { s.Content ??= "Ayarlar"; items.Add(s); }
        foreach (var it in items)
        {
            if (compact && it.Content is string text)
            {
                ToolTipService.SetToolTip(it, new ToolTip { Content = text, Placement = Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Right });
                if (_tipHooked.Add(it))
                {
                    void Show(object o, RoutedEventArgs _) { if (!Nav.IsPaneOpen && ToolTipService.GetToolTip(it) is ToolTip t) t.IsOpen = true; }
                    void Hide(object o, RoutedEventArgs _) { if (ToolTipService.GetToolTip(it) is ToolTip t) t.IsOpen = false; }
                    it.PointerEntered += (o, e) => Show(o, e);
                    it.PointerExited += (o, e) => Hide(o, e);
                    it.PointerCanceled += (o, e) => Hide(o, e);
                    it.GotFocus += Show;
                    it.LostFocus += Hide;
                    it.Tapped += (o, e) => Hide(o, e);
                }
            }
            else
            {
                if (ToolTipService.GetToolTip(it) is ToolTip old) old.IsOpen = false;
                ToolTipService.SetToolTip(it, null);
            }
        }
    }

    private void OnAdminClick(object sender, RoutedEventArgs e) => Elevation.RestartAsAdmin();

    // ------------------------------------------------------------- genel arama

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        var q = sender.Text.Trim();
        if (q.Length < 2) { sender.ItemsSource = null; return; }
        var hits = _searchIndex
            .Where(s => s.Text.Contains(q, StringComparison.CurrentCultureIgnoreCase) || s.Detail.Contains(q, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(s => s.Text.StartsWith(q, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1)
            .Take(9)
            .Select(s => $"{s.Text}  ·  {s.Detail}")
            .ToList();
        hits.Add($"“{q}” için uygulama deposunda ara");
        sender.ItemsSource = hits;
    }

    private void OnSearchChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args) { }

    private void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var chosen = args.ChosenSuggestion as string;
        var q = sender.Text.Trim();
        if (chosen is null || chosen.StartsWith('“'))
        {
            if (q.Length == 0) return;
            var hit = _searchIndex.FirstOrDefault(s => s.Text.Equals(q, StringComparison.CurrentCultureIgnoreCase));
            if (hit is not null && chosen is null) Navigate(hit.Tag, hit.Param);
            else Navigate("apps", new AppsPage.Args("discover", q));
            sender.Text = "";
            return;
        }
        var item = _searchIndex.FirstOrDefault(s => $"{s.Text}  ·  {s.Detail}" == chosen);
        if (item is not null) Navigate(item.Tag, item.Param);
        sender.Text = "";
    }
}
