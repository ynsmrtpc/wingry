using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Wingry.Core;
using Wingry.Services;

namespace Wingry.Pages;

public sealed partial class TweaksPage : Page
{
    public sealed record Args(string CategoryId, string? Highlight);

    private sealed class Row
    {
        public TweakFeature? Feature;          // tek ayar
        public TweakGroup? Group;              // seçenek grubu
        public bool Applied;                   // tek ayarın şimdiki durumu
        public int CurrentIndex = -1;          // grubun şimdiki seçeneği
        public bool NeedsAdmin;
        public bool Recommended;
        public SettingsCard Card = null!;
        public ToggleSwitch? Toggle;
        public ComboBox? Combo;

        public bool Dirty => Feature is not null ? Toggle!.IsOn != Applied : Combo!.SelectedIndex != CurrentIndex && Combo.SelectedIndex >= 0;
    }

    private readonly List<Row> _rows = [];
    private TweakCategory? _cat;
    private bool _suspend;

    public TweaksPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        var a = e.Parameter as Args;
        TweakCatalog.Load();
        _cat = TweakCatalog.Categories.FirstOrDefault(c => c.Id == a?.CategoryId) ?? TweakCatalog.Categories.FirstOrDefault();
        if (_cat is null) return;
        Title.Text = _cat.Label;
        Subtitle.Text = _cat.Description;
        AdminInfo.IsOpen = !Elevation.IsAdmin;
        await LoadAsync(a?.Highlight);
    }

    private async Task LoadAsync(string? highlight = null)
    {
        LoadingRing.IsActive = true; LoadingRing.Visibility = Visibility.Visible;
        Items.Children.Clear(); _rows.Clear();
        var cat = _cat!;
        var rec = TweakCatalog.Recommended.ToHashSet();

        // Durumları arka planda oku (WMI / Appx sorguları UI'yi kilitlemesin)
        var rows = await Task.Run(() =>
        {
            var list = new List<Row>();
            foreach (var g in TweakCatalog.GroupsOf(cat))
            {
                var r = new Row { Group = g };
                for (int i = 0; i < g.Options.Count; i++)
                {
                    var f = TweakCatalog.Features[g.Options[i].FeatureId];
                    if (r.CurrentIndex < 0 && TweakService.IsApplied(f)) r.CurrentIndex = i;
                    r.NeedsAdmin |= NeedsAdmin(f);
                }
                list.Add(r);
            }
            foreach (var f in TweakCatalog.StandaloneFeatures(cat))
                list.Add(new Row { Feature = f, Applied = TweakService.IsApplied(f), NeedsAdmin = NeedsAdmin(f), Recommended = rec.Contains(f.Id) });
            return list;
        });

        foreach (var r in rows.OrderBy(r => r.Group?.Priority ?? r.Feature!.Priority))
        {
            BuildCard(r);
            _rows.Add(r);
            Items.Children.Add(r.Card);
        }
        RecommendButton.Visibility = _rows.Any(r => r.Recommended && !r.Applied) ? Visibility.Visible : Visibility.Collapsed;
        LoadingRing.IsActive = false; LoadingRing.Visibility = Visibility.Collapsed;
        UpdatePending();

        if (highlight is not null && _rows.FirstOrDefault(r => r.Feature?.Id == highlight || r.Group?.Id == highlight) is { } hit)
        {
            hit.Card.Loaded += (_, _) => hit.Card.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.3 });
            hit.Card.BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            hit.Card.BorderThickness = new Thickness(2);
        }
    }

    private static bool NeedsAdmin(TweakFeature f)
    {
        if (f.Id is "EnableWindowsSandbox" or "EnableWindowsSubsystemForLinux" or "DisableTelemetry") return true;
        bool Check(string? p) => p is not null && File.Exists(p) && RegFile.Load(p).Any(o => o.NeedsAdmin);
        return Check(f.ApplyPath) || Check(f.UndoPath);
    }

    private void BuildCard(Row r)
    {
        var card = new SettingsCard();
        r.Card = card;
        var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };

        if (r.Group is { } g)
        {
            card.Header = g.Label;
            card.Description = Describe(g.ToolTip, badges);
            card.HeaderIcon = new FontIcon { Glyph = _cat!.Glyph };
            var combo = new ComboBox { MinWidth = 260, MaxWidth = 360 };
            foreach (var (_, label) in g.Options) combo.Items.Add(label);
            combo.SelectedIndex = r.CurrentIndex;
            if (r.CurrentIndex < 0) combo.PlaceholderText = "Özel / bilinmiyor";
            combo.SelectionChanged += (_, _) => { if (!_suspend) UpdatePending(); };
            r.Combo = combo;
            card.Content = combo;
        }
        else
        {
            var f = r.Feature!;
            card.Header = f.Label;
            card.Description = Describe(f.ToolTip, badges);
            card.HeaderIcon = new FontIcon { Glyph = _cat!.Glyph };
            var t = new ToggleSwitch { IsOn = r.Applied, OnContent = "Açık", OffContent = "Kapalı" };
            // Geri alınamayan ayarlar uygulandıktan sonra kilitlenir
            if (r.Applied && !TweakService.CanUndo(f)) { t.IsEnabled = false; Badge(badges, "Geri alınamaz", ""); }
            if (f.Id == "ClearStart") { t.IsOn = false; t.OnContent = "Uygulanacak"; }
            t.Toggled += (_, _) => { if (!_suspend) UpdatePending(); };
            r.Toggle = t;
            card.Content = t;
            if (r.Recommended) Badge(badges, "Önerilen", "");
            if (f.RequiresReboot) Badge(badges, "Yeniden başlatma", "");
        }
        if (r.NeedsAdmin) Badge(badges, "Yönetici", "");
        if (badges.Children.Count > 0 && card.Description is StackPanel sp) sp.Children.Add(badges);
    }

    private static object Describe(string text, StackPanel badges)
    {
        var sp = new StackPanel();
        if (!string.IsNullOrWhiteSpace(text))
            sp.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["Caption"] });
        return sp;
    }

    private static void Badge(StackPanel host, string text, string glyph)
    {
        host.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1, 6, 2),
            Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    new FontIcon { Glyph = glyph, FontSize = 10 },
                    new TextBlock { Text = text, FontSize = 11 },
                },
            },
        });
    }

    private List<TweakChange> Changes()
    {
        var list = new List<TweakChange>();
        foreach (var r in _rows.Where(r => r.Dirty))
        {
            if (r.Feature is { } f) list.Add(new TweakChange(f, r.Toggle!.IsOn));
            else if (r.Group is { } g)
                list.Add(new TweakChange(TweakCatalog.Features[g.Options[r.Combo!.SelectedIndex].FeatureId], true));
        }
        return list;
    }

    private void UpdatePending()
    {
        var ch = Changes();
        PendingBar.Visibility = ch.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PendingText.Text = ch.Count == 1 ? "1 değişiklik bekliyor" : $"{ch.Count} değişiklik bekliyor";
        PendingDetail.Text = string.Join(" · ", ch.Select(c => (c.Apply ? "" : "geri al: ") + c.Feature.Label));
    }

    private void OnRecommend(object sender, RoutedEventArgs e)
    {
        foreach (var r in _rows.Where(r => r.Recommended && r.Toggle is { IsEnabled: true })) r.Toggle!.IsOn = true;
        UpdatePending();
    }

    private void OnDiscard(object sender, RoutedEventArgs e)
    {
        _suspend = true;
        foreach (var r in _rows)
        {
            if (r.Toggle is not null) r.Toggle.IsOn = r.Applied && r.Feature!.Id != "ClearStart";
            if (r.Combo is not null) r.Combo.SelectedIndex = r.CurrentIndex;
        }
        _suspend = false;
        UpdatePending();
    }

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        var ch = Changes();
        if (ch.Count == 0) return;
        ApplyButton.IsEnabled = false; ApplyRing.IsActive = true;
        try
        {
            var r = await TweakService.ApplyAsync(ch, RestorePoint.IsChecked == true);
            ReportResult(r);
            RestorePoint.IsChecked = false;
            await LoadAsync();
        }
        finally { ApplyButton.IsEnabled = true; ApplyRing.IsActive = false; }
    }

    /// <summary>Uygulama sonucunu bildirir; gerekirse Gezgin'i yeniden başlatmayı önerir.</summary>
    public static async void ReportResult(TweakResult r)
    {
        if (r.Errors.Count > 0)
        {
            await Ui.AlertAsync($"{r.Errors.Count} işlem tamamlanamadı",
                string.Join("\n", r.Errors.Take(30)) + "\n\nAyrıntılar günlükte: " + Paths.Logs);
        }
        else
        {
            var extra = r.RebootRequired ? " Bazı değişiklikler yeniden başlatmadan sonra etkin olur." : "";
            Ui.Toast("Ayarlar uygulandı", $"{r.Applied} değişiklik uygulandı.{extra}");
        }
        if (r.ExplorerRestart &&
            await Ui.ConfirmAsync("Gezgin yeniden başlatılsın mı?",
                "Görev çubuğu, Başlat menüsü ve Dosya Gezgini ayarlarının hemen görünmesi için Windows Gezgini yeniden başlatılmalı. Açık Gezgin pencereleri kapanır.",
                "Yeniden başlat"))
            TweakService.RestartExplorer();
    }
}
