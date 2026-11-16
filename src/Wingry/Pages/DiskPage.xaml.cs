using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Wingry.Core;
using Wingry.Services;

namespace Wingry.Pages;

public sealed class FileRow
{
    public FileRow(FileEntry f)
    {
        Entry = f;
        Name = f.Name;
        Folder = f.Dir.FullPath;
        Kind = DiskService.KindLabel(f.Kind);
        Size = Format.Bytes(f.Size);
        Modified = Format.Date(new DateTime(f.ModifiedTicks, DateTimeKind.Utc).ToLocalTime());
        Glyph = DiskPage.GlyphOf(f.Kind);
    }
    public FileEntry Entry { get; }
    public string Name { get; }
    public string Folder { get; }
    public string Kind { get; }
    public string Size { get; }
    public string Modified { get; }
    public string Glyph { get; }
}

public sealed class DirRow
{
    public DirNode? Dir { get; init; }
    public FileEntry? File { get; init; }
    public FileKind? KindKey { get; init; }
    public required string Name { get; init; }
    public required string Detail { get; init; }
    public required string Size { get; init; }
    public double Percent { get; init; }
    public required string Glyph { get; init; }
}

public sealed partial class DiskPage : Page
{
    private const int MaxRows = 1500;
    private ScanResult? _scan;
    private CancellationTokenSource? _scanCts;
    private DirNode? _current;
    private readonly ObservableCollection<object> _crumbs = [];
    private List<CleanupTarget>? _targets;

    public DiskPage()
    {
        InitializeComponent();
        FKind.Items.Add("Tüm türler");
        foreach (var k in Enum.GetValues<FileKind>()) FKind.Items.Add(DiskService.KindLabel(k));
        FKind.SelectedIndex = 0;
        Crumbs.ItemsSource = _crumbs;
        Loaded += (_, _) => BuildDrives();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is "dups")
            foreach (var i in Tabs.Items) i.IsSelected = (string)i.Tag == "dups";
        if (e.Parameter is "cleanup")
            foreach (var i in Tabs.Items) i.IsSelected = (string)i.Tag == "cleanup";
    }

    public static string GlyphOf(FileKind k) => k switch
    {
        FileKind.Video => "",
        FileKind.Audio => "",
        FileKind.Image => "",
        FileKind.Document => "",
        FileKind.Archive => "",
        FileKind.Program => "",
        FileKind.DiskImage => "",
        FileKind.Code => "",
        FileKind.System => "",
        _ => "",
    };

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var tag = sender.SelectedItem?.Tag as string;
        bool clean = tag == "cleanup", dups = tag == "dups";
        AnalyzeArea.Visibility = clean || dups ? Visibility.Collapsed : Visibility.Visible;
        CleanupArea.Visibility = clean ? Visibility.Visible : Visibility.Collapsed;
        DupArea.Visibility = dups ? Visibility.Visible : Visibility.Collapsed;
        if (clean && _targets is null) _ = MeasureAsync();
        if (dups && DupList.Children.Count == 0)
            DupStatus.Text = _scan is null ? "Önce Analiz sekmesinden bir sürücü ya da klasör tarayın." : $"Taranan: {_scan.Root.FullPath}";
    }

    // ------------------------------------------------------------ kopyalar

    private CancellationTokenSource? _dupCts;
    private readonly List<(FileEntry file, CheckBox box)> _dupBoxes = [];
    private readonly HashSet<int> _dupGroupStarts = [];   // her grubun ilk (korunacak) dosyasının _dupBoxes sırası

    private async void OnFindDups(object sender, RoutedEventArgs e)
    {
        if (_dupCts is not null) { _dupCts.Cancel(); return; }
        if (_scan is null) { DupStatus.Text = "Önce Analiz sekmesinden bir sürücü ya da klasör tarayın."; return; }
        long min = DupMin.SelectedIndex switch { 0 => 100L << 10, 2 => 10L << 20, 3 => 100L << 20, _ => 1L << 20 };
        _dupCts = new CancellationTokenSource();
        DupBtn.Content = "Durdur"; DupRecycle.IsEnabled = false;
        DupList.Children.Clear(); _dupBoxes.Clear(); _dupGroupStarts.Clear(); DupSuggest.IsEnabled = false;
        DupBar.Visibility = Visibility.Visible; DupBar.IsIndeterminate = true;
        var prog = new Progress<DupProgress>(p =>
        {
            DupBar.IsIndeterminate = p.Total == 0;
            if (p.Total > 0) DupBar.Value = (double)p.Done / p.Total;
            DupStatus.Text = $"{p.Stage} · {Format.Number(p.Done)} / {Format.Number(p.Total)}";
        });
        try
        {
            IReadOnlyList<FileEntry> files = _scan.Files;
            if (DupSystem.IsChecked != true)
            {
                var keep = DuplicateFinder.NotInSystem();
                files = await Task.Run(() => _scan.Files.Where(f => f.Size >= min && keep(f)).ToArray());
            }
            var groups = await DuplicateFinder.FindAsync(files, min, prog, _dupCts.Token);
            ShowDups(groups);
        }
        catch (OperationCanceledException) { DupStatus.Text = "Durduruldu."; }
        catch (Exception ex) { DupStatus.Text = "Başarısız: " + ex.Message; }
        finally
        {
            _dupCts = null; DupBtn.Content = "Kopyaları bul";
            DupBar.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowDups(List<DupGroup> groups)
    {
        long wasted = groups.Sum(g => g.Wasted);
        DupStatus.Text = groups.Count == 0
            ? "Kopya dosya bulunamadı."
            : $"{Format.Number(groups.Count)} grup · {Format.Number(groups.Sum(g => g.Files.Count - 1))} fazla kopya · {Format.Bytes(wasted)} boşa"
              + (groups.Count > 200 ? " · en büyük 200 grup gösteriliyor" : "");
        var caption = (Style)Application.Current.Resources["Caption"];
        foreach (var g in groups.Take(200))
        {
            var sp = new StackPanel { Spacing = 2 };
            sp.Children.Add(new TextBlock
            {
                Text = $"{g.Files[0].Name}  ·  {g.Files.Count} kopya × {Format.Bytes(g.Size)}  ·  {Format.Bytes(g.Wasted)} kazanılabilir",
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], TextTrimming = TextTrimming.CharacterEllipsis,
            });
            _dupGroupStarts.Add(_dupBoxes.Count);
            for (int i = 0; i < g.Files.Count; i++)
            {
                var f = g.Files[i];
                var cb = new CheckBox
                {
                    IsChecked = false, // kullanıcı seçer: proje klasörlerindeki kopyalar projenin parçası olabilir
                    Content = new TextBlock { Text = $"{f.FullPath}   ({Format.Date(new DateTime(f.ModifiedTicks, DateTimeKind.Utc).ToLocalTime())}){(i == 0 ? "  · korunuyor" : "")}", Style = caption, TextTrimming = TextTrimming.CharacterEllipsis },
                };
                ToolTipService.SetToolTip(cb, f.FullPath);
                cb.Checked += (_, _) => UpdateDupButton(); cb.Unchecked += (_, _) => UpdateDupButton();
                _dupBoxes.Add((f, cb));
                sp.Children.Add(cb);
            }
            DupList.Children.Add(new Border { Style = (Style)Application.Current.Resources["Card"], Padding = new Thickness(16, 12, 16, 12), Child = sp });
        }
        DupSuggest.IsEnabled = groups.Count > 0;
        UpdateDupButton();
    }

    /// <summary>Her gruptan en eski dosya (ilk) dışındakileri işaretler.</summary>
    private void OnSuggestDups(object sender, RoutedEventArgs e)
    {
        DiskService.KeepFirstOfGroups(_dupBoxes.Select(x => x.box).ToList(), _dupGroupStarts);
        UpdateDupButton();
    }

    private void UpdateDupButton()
    {
        var sel = _dupBoxes.Where(x => x.box.IsChecked == true).ToList();
        DupRecycle.IsEnabled = sel.Count > 0;
        DupRecycle.Content = sel.Count > 0 ? $"{sel.Count} kopyayı taşı ({Format.Bytes(sel.Sum(x => x.file.Size))})" : "Seçilenleri taşı";
    }

    private async void OnRecycleDups(object sender, RoutedEventArgs e)
    {
        var sel = _dupBoxes.Where(x => x.box.IsChecked == true).Select(x => x.file).ToList();
        if (sel.Count == 0) return;
        // Bir grubun tüm kopyaları seçildiyse uyar: hiç kopya kalmaz
        var allGone = _dupBoxes.GroupBy(x => x.file.Size).Where(g => g.All(x => x.box.IsChecked == true)).Count();
        var warn = allGone > 0 ? $"\n\nDikkat: {allGone} grupta tüm kopyalar seçili; o dosyalardan hiç kalmayacak." : "";
        if (!await Ui.ConfirmAsync($"{sel.Count} kopya taşınsın mı?", $"{Format.Bytes(sel.Sum(f => f.Size))} Geri Dönüşüm Kutusu'na taşınır.{warn}", "Taşı", danger: allGone > 0)) return;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Window!);
        bool ok = await Task.Run(() => DiskService.Recycle(sel.Select(f => f.FullPath), hwnd));
        if (ok) Ui.Toast("Kopyalar taşındı", $"{sel.Count} dosya Geri Dönüşüm Kutusu'nda. Alanı açmak için kutuyu boşaltın.");
        else Ui.Error("Bazı dosyalar taşınamadı", "Dosya kullanımda olabilir.");
        foreach (var (f, box) in _dupBoxes.Where(x => x.box.IsChecked == true)) { box.IsEnabled = false; box.IsChecked = false; box.Opacity = 0.5; }
        UpdateDupButton();
    }

    // ------------------------------------------------------------ sürücüler

    private void BuildDrives()
    {
        var cards = new List<UIElement>();
        foreach (var d in SystemInfo.FixedDrives())
        {
            long used = d.TotalSize - d.AvailableFreeSpace;
            double pct = 100.0 * used / Math.Max(1, d.TotalSize);
            var btn = new Button { Content = "Tara", Tag = d.RootDirectory.FullName, HorizontalAlignment = HorizontalAlignment.Right };
            btn.Click += async (s, _) => await ScanAsync((string)((Button)s).Tag);
            var bar = new ProgressBar { Maximum = 100, Value = pct, Margin = new Thickness(0, 10, 0, 6) };
            if (pct > 90) bar.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            cards.Add(new Border
            {
                Style = (Style)Application.Current.Resources["Card"],
                Padding = new Thickness(16),
                Child = new StackPanel
                {
                    Children =
                    {
                        new Grid
                        {
                            Children =
                            {
                                new StackPanel
                                {
                                    Orientation = Orientation.Horizontal, Spacing = 10,
                                    Children =
                                    {
                                        new FontIcon { Glyph = d.DriveType == DriveType.Removable ? "" : "", FontSize = 22 },
                                        new StackPanel
                                        {
                                            Children =
                                            {
                                                new TextBlock { Text = $"{(string.IsNullOrEmpty(d.VolumeLabel) ? "Yerel Disk" : d.VolumeLabel)} ({d.Name.TrimEnd('\\')})", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] },
                                                new TextBlock { Text = $"{d.DriveFormat} · {Format.Bytes(d.TotalSize)}", Style = (Style)Application.Current.Resources["Caption"] },
                                            },
                                        },
                                    },
                                },
                                btn,
                            },
                        },
                        bar,
                        new TextBlock { Text = $"{Format.Bytes(d.AvailableFreeSpace)} boş · %{pct:0} dolu", Style = (Style)Application.Current.Resources["Caption"] },
                    },
                },
            });
        }
        Drives.ItemsSource = cards;
    }

    private async void OnPickFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(App.Window!.AppWindow.Id);
            var res = await picker.PickSingleFolderAsync();
            if (res is not null && !string.IsNullOrEmpty(res.Path)) await ScanAsync(res.Path);
        }
        catch (Exception ex) { Ui.Error("Klasör seçilemedi", ex.Message); }
    }

    private void OnCancelScan(object sender, RoutedEventArgs e) => _scanCts?.Cancel();

    private async Task ScanAsync(string path)
    {
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;
        _scan = null; _current = null;
        FileList.ItemsSource = null; DirList.ItemsSource = null; KindList.ItemsSource = null;
        ScanTitle.Text = $"{path} taranıyor…";
        ScanBar.Visibility = CancelBtn.Visibility = Visibility.Visible;
        PickBtn.IsEnabled = false;
        var prog = new Progress<ScanProgress>(p =>
            ScanDetail.Text = $"{Format.Number(p.Files)} dosya · {Format.Number(p.Directories)} klasör · {Format.Bytes(p.Bytes)} · {p.Current}");
        try
        {
            _scan = await DiskService.ScanAsync(path, prog, ct);
            _current = _scan.Root;
            ScanTitle.Text = $"{_scan.Root.FullPath} · {Format.Bytes(_scan.Root.Size)}";
            ScanDetail.Text = $"{Format.Number(_scan.TotalFiles)} dosya · {Format.Number(_scan.Directories)} klasör · {_scan.Elapsed.TotalSeconds:0.0} sn"
                + (_scan.DeniedDirectories > 0 ? $" · {Format.Number(_scan.DeniedDirectories)} klasöre erişilemedi" + (Elevation.IsAdmin ? "" : " (tam sonuç için yönetici olarak çalıştırın)") : "");
            Placeholder.Visibility = Visibility.Collapsed;
            FilterBar.Visibility = Visibility.Visible;
            ShowView();
        }
        catch (OperationCanceledException) { ScanTitle.Text = "Tarama durduruldu"; ScanDetail.Text = ""; }
        catch (Exception ex) { ScanTitle.Text = "Tarama başarısız"; ScanDetail.Text = ex.Message; }
        finally
        {
            ScanBar.Visibility = CancelBtn.Visibility = Visibility.Collapsed;
            PickBtn.IsEnabled = true;
        }
    }

    // ------------------------------------------------------------ görünümler

    private int Mode => int.Parse((string)(ViewMode.SelectedItem?.Tag ?? "0"));

    private void SetMode(int m) { foreach (var i in ViewMode.Items) i.IsSelected = (string)i.Tag == m.ToString(); }

    private void OnViewMode(SelectorBar sender, SelectorBarSelectionChangedEventArgs e) { if (_scan is not null) ShowView(); }

    private void ShowView()
    {
        int mode = Mode;
        FileList.Visibility = mode == 0 ? Visibility.Visible : Visibility.Collapsed;
        DirList.Visibility = mode == 1 ? Visibility.Visible : Visibility.Collapsed;
        KindList.Visibility = mode == 2 ? Visibility.Visible : Visibility.Collapsed;
        FileFilters.Visibility = mode == 0 ? Visibility.Visible : Visibility.Collapsed;
        Crumbs.Visibility = mode == 1 ? Visibility.Visible : Visibility.Collapsed;
        BtnRecycle.Visibility = BtnReveal.Visibility = mode == 2 ? Visibility.Collapsed : Visibility.Visible;
        if (mode == 0) ApplyFilters();
        else if (mode == 1) ShowDir(_current ?? _scan!.Root);
        else ShowKinds();
        UpdateActions();
    }

    private void OnFilterChanged(object sender, object e) { if (_scan is not null && IsLoaded) ApplyFilters(); }

    private void ApplyFilters()
    {
        if (_scan is null) return;
        var name = FName.Text.Trim();
        bool extOnly = name.StartsWith('.');
        int kindIdx = FKind.SelectedIndex - 1;
        long min = FSize.SelectedIndex switch { 1 => 1L << 20, 2 => 10L << 20, 3 => 100L << 20, 4 => 1L << 30, _ => 0 };
        var now = DateTime.UtcNow;
        long? olderThan = FAge.SelectedIndex switch
        {
            1 => now.AddDays(-30).Ticks, 2 => now.AddMonths(-6).Ticks, 3 => now.AddYears(-1).Ticks, 4 => now.AddYears(-2).Ticks, _ => null,
        };
        long? newerThan = FAge.SelectedIndex == 5 ? now.AddDays(-7).Ticks : null;

        // Dizi zaten büyükten küçüğe sıralı; süzme tek geçişte, ~1M dosyada birkaç ms.
        IEnumerable<FileEntry> q = _scan.Files;
        if (min > 0) q = q.TakeWhile(f => f.Size >= min);
        if (kindIdx >= 0) q = q.Where(f => (int)f.Kind == kindIdx);
        if (olderThan is { } o) q = q.Where(f => f.ModifiedTicks < o);
        if (newerThan is { } n) q = q.Where(f => f.ModifiedTicks >= n);
        if (name.Length > 0)
            q = extOnly ? q.Where(f => f.Name.EndsWith(name, StringComparison.OrdinalIgnoreCase))
                        : q.Where(f => f.Name.Contains(name, StringComparison.CurrentCultureIgnoreCase));

        List<FileEntry> matched;
        long total = 0; int count = 0;
        switch (FSort.SelectedIndex)
        {
            case 1: matched = q.ToList(); matched.Reverse(); break;
            case 2: matched = q.OrderByDescending(f => f.ModifiedTicks).ToList(); break;
            case 3: matched = q.OrderBy(f => f.ModifiedTicks).ToList(); break;
            case 4: matched = q.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList(); break;
            default: matched = q.ToList(); break;
        }
        foreach (var f in matched) { total += f.Size; count++; }

        FileList.ItemsSource = matched.Take(MaxRows).Select(f => new FileRow(f)).ToList();
        ListFooter.Text = $"{Format.Number(count)} dosya · toplam {Format.Bytes(total)}" + (count > MaxRows ? $" · ilk {Format.Number(MaxRows)} tanesi gösteriliyor" : "")
            + (_scan.UnlistedFiles > 0 ? $" · {Format.Bytes(_scan.ListThreshold)} altındaki {Format.Number(_scan.UnlistedFiles)} küçük dosya ({Format.Bytes(_scan.UnlistedBytes)}) yalnızca klasör ve tür toplamlarında" : "");
    }

    private void ShowDir(DirNode dir)
    {
        _current = dir;
        _crumbs.Clear();
        var chain = new List<DirNode>();
        for (var d = dir; d is not null; d = d.Parent) chain.Insert(0, d);
        foreach (var d in chain) _crumbs.Add(new CrumbItem(d));

        long max = Math.Max(1, dir.Size);
        var rows = new List<DirRow>();
        foreach (var c in dir.Children)
            rows.Add(new DirRow
            {
                Dir = c, Name = c.Name, Glyph = c.Denied ? "" : "",
                Detail = c.Denied ? "erişilemedi" : $"{Format.Number(c.Files)} dosya · {c.Children.Count} klasör",
                Size = Format.Bytes(c.Size), Percent = 100.0 * c.Size / max,
            });
        // Bu klasörün kendi dosyaları (büyükler önce)
        foreach (var f in _scan!.Files.Where(f => ReferenceEquals(f.Dir, dir)).Take(300))
            rows.Add(new DirRow
            {
                File = f, Name = f.Name, Glyph = GlyphOf(f.Kind), Detail = DiskService.KindLabel(f.Kind) + " · " + Format.Date(new DateTime(f.ModifiedTicks, DateTimeKind.Utc).ToLocalTime()),
                Size = Format.Bytes(f.Size), Percent = 100.0 * f.Size / max,
            });
        rows.Sort((a, b) => ParseSizeOrder(b).CompareTo(ParseSizeOrder(a)));
        DirList.ItemsSource = rows;
        ListFooter.Text = $"{dir.FullPath} · {Format.Bytes(dir.Size)} · {Format.Number(dir.Files)} dosya";
    }

    private static long ParseSizeOrder(DirRow r) => r.Dir?.Size ?? r.File?.Size ?? 0;

    private sealed record CrumbItem(DirNode Dir)
    {
        public override string ToString() => Dir.Parent is null ? Dir.FullPath : Dir.Name;
    }

    private void OnCrumb(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Item is CrumbItem c) ShowDir(c.Dir);
    }

    private void OnDirClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is DirRow { Dir: { } d } && d.Children.Count + d.OwnFiles > 0) ShowDir(d);
    }

    private void ShowKinds()
    {
        var groups = Enum.GetValues<FileKind>()
            .Select(k => (kind: k, size: _scan!.KindBytes[(int)k], count: _scan.KindCounts[(int)k]))
            .Where(g => g.count > 0)
            .OrderByDescending(g => g.size).ToList();
        long max = Math.Max(1, groups.Count > 0 ? groups[0].size : 1);
        long total = Math.Max(1, _scan.Root.Size);
        KindList.ItemsSource = groups.Select(g => new DirRow
        {
            KindKey = g.kind, Name = DiskService.KindLabel(g.kind), Glyph = GlyphOf(g.kind),
            Detail = $"{Format.Number(g.count)} dosya · toplamın %{100.0 * g.size / total:0.0}'i",
            Size = Format.Bytes(g.size), Percent = 100.0 * g.size / max,
        }).ToList();
        ListFooter.Text = "Bir türe tıklayınca o türün dosyaları büyükten küçüğe listelenir.";
    }

    private void OnKindClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not DirRow { KindKey: { } k }) return;
        FKind.SelectedIndex = (int)k + 1;
        SetMode(0);
    }

    // ------------------------------------------------------------ dosya işlemleri

    private List<string> SelectedPaths()
    {
        var lv = Mode == 1 ? DirList : FileList;
        var list = new List<string>();
        foreach (var o in lv.SelectedItems)
        {
            if (o is FileRow f) list.Add(f.Entry.FullPath);
            else if (o is DirRow { Dir: { } d }) list.Add(d.FullPath);
            else if (o is DirRow { File: { } fe }) list.Add(fe.FullPath);
        }
        return list;
    }

    private void OnFileSelection(object sender, SelectionChangedEventArgs e) => UpdateActions();

    private void UpdateActions()
    {
        var n = SelectedPaths().Count;
        BtnReveal.IsEnabled = n == 1;
        BtnRecycle.IsEnabled = n > 0;
        BtnRecycle.Label = n > 1 ? $"Geri Dönüşüm Kutusu'na taşı ({n})" : "Geri Dönüşüm Kutusu'na taşı";
    }

    private void OnFileDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is FileRow f) DiskService.ShowInExplorer(f.Entry.FullPath);
    }

    private void OnReveal(object sender, RoutedEventArgs e)
    {
        var p = SelectedPaths().FirstOrDefault();
        if (p is not null) DiskService.ShowInExplorer(p);
    }

    private async void OnRecycle(object sender, RoutedEventArgs e)
    {
        var paths = SelectedPaths();
        if (paths.Count == 0) return;
        var sys = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (paths.Any(p => p.StartsWith(sys, StringComparison.OrdinalIgnoreCase) || p.Contains(@"\Program Files", StringComparison.OrdinalIgnoreCase)))
        {
            if (!await Ui.ConfirmAsync("Sistem dosyası seçili", "Windows ya da Program Files altındaki dosyaları silmek programları veya Windows'u bozabilir. Yine de devam edilsin mi?", "Yine de taşı", danger: true))
                return;
        }
        var list = string.Join("\n", paths.Take(10).Select(Path.GetFileName)) + (paths.Count > 10 ? $"\n… ve {paths.Count - 10} öğe daha" : "");
        if (!await Ui.ConfirmAsync($"{paths.Count} öğe Geri Dönüşüm Kutusu'na taşınsın mı?", list + "\n\nGeri Dönüşüm Kutusu'ndan geri alabilirsiniz.", "Taşı")) return;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Window!);
        bool ok = await Task.Run(() => DiskService.Recycle(paths, hwnd));
        if (ok)
        {
            Ui.Toast("Taşındı", $"{paths.Count} öğe Geri Dönüşüm Kutusu'nda. Diskteki alanı açmak için kutuyu boşaltın.");
            // Listeden çıkar (yeniden taramadan)
            var gone = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var keep = _scan!.Files.Where(f => !gone.Contains(f.FullPath)).ToArray();
            _scan = new ScanResult
            {
                Root = _scan.Root, Files = keep, Directories = _scan.Directories, DeniedDirectories = _scan.DeniedDirectories, Elapsed = _scan.Elapsed,
                TotalFiles = _scan.TotalFiles - paths.Count, UnlistedFiles = _scan.UnlistedFiles, UnlistedBytes = _scan.UnlistedBytes,
                ListThreshold = _scan.ListThreshold, KindBytes = _scan.KindBytes, KindCounts = _scan.KindCounts,
            };
            ShowView();
        }
        else Ui.Error("Bazı öğeler taşınamadı", "Dosya kullanımda olabilir ya da yönetici izni gerekebilir.");
    }

    // ------------------------------------------------------------ temizlik

    private async Task MeasureAsync()
    {
        _targets = CleanupService.Targets();
        CleanList.ItemsSource = _targets;
        CleanTotal.Text = "Hesaplanıyor…";
        await Task.WhenAll(_targets.Select(async t => t.Size = await Task.Run(() => CleanupService.Measure(t.Id))));
        CleanTotal.Text = $"Toplam {Format.Bytes(_targets.Sum(t => Math.Max(0, t.Size)))} temizlenebilir (yaklaşık)";
    }

    private async void OnMeasure(object sender, RoutedEventArgs e) => await MeasureAsync();

    private async void OnClean(object sender, RoutedEventArgs e)
    {
        if (_targets is null) return;
        var sel = _targets.Where(t => t.Selected).ToList();
        if (sel.Count == 0) return;
        if (sel.Any(t => t.Id == "recycle") &&
            !await Ui.ConfirmAsync("Geri Dönüşüm Kutusu boşaltılsın mı?", "Kutudaki dosyalar kalıcı olarak silinir.", "Boşalt", danger: true))
            return;

        CleanBtn.IsEnabled = false; CleanRing.IsActive = true; CleanBtnText.Text = "Temizleniyor…";
        try
        {
            long freed = 0;
            foreach (var t in sel.Where(t => !t.NeedsAdmin))
                freed += await Task.Run(() => CleanupService.Clean(t.Id));
            var admin = sel.Where(t => t.NeedsAdmin).Select(t => "clean|" + t.Id).ToList();
            if (admin.Count > 0)
            {
                var w = await Elevation.RunAsync(admin);
                if (!w.Started) Ui.Error("Sistem konumları temizlenmedi", w.Error ?? "");
                foreach (var l in w.Lines)
                    if (l.StartsWith("OK: ", StringComparison.Ordinal) && long.TryParse(l[4..], out var b)) freed += b;
            }
            Ui.Toast("Temizlik tamamlandı", $"{Format.Bytes(freed)} alan açıldı.");
            await MeasureAsync();
        }
        finally { CleanBtn.IsEnabled = true; CleanRing.IsActive = false; CleanBtnText.Text = "Seçilenleri temizle"; }
    }
}
