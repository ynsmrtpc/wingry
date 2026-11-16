using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wingry.Core;
using Wingry.Services;

namespace Wingry.Controls;

/// <summary>Hazır paket düzenleyicisi: ad, açıklama, uygulamalar (winget'te arayıp ekle / çıkar).</summary>
public static class BundleEditor
{
    public static async Task<(string title, string desc, List<BundleApp> apps)?> ShowAsync(
        string title, string desc, IReadOnlyList<BundleApp> apps, string dialogTitle)
    {
        if (Ui.Root is null) return null;
        var list = new ObservableCollection<BundleApp>(apps);
        var caption = (Style)Application.Current.Resources["Caption"];

        var nameBox = new TextBox { Header = "Paket adı", Text = title, PlaceholderText = "ör. Muhasebe bilgisayarı" };
        var descBox = new TextBox { Header = "Açıklama (isteğe bağlı)", Text = desc, PlaceholderText = "Bu paket kimler için, ne içeriyor?" };
        var search = new AutoSuggestBox { Header = "Uygulama ekle", PlaceholderText = "winget deposunda ara (ör. chrome, 7zip) ve seçin", QueryIcon = new SymbolIcon(Symbol.Find) };
        var count = new TextBlock { Style = caption };

        // Uygulama listesi: her değişiklikte yeniden çizilen basit yığın (en çok birkaç düzine satır)
        var rows = new StackPanel { Spacing = 2 };
        var lv = new ScrollViewer { MaxHeight = 280, Content = rows };
        void Redraw()
        {
            rows.Children.Clear();
            foreach (var a in list)
            {
                var row = new Grid { ColumnSpacing = 8, Padding = new Thickness(4, 4, 4, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                t.Children.Add(new TextBlock { Text = a.Name, TextTrimming = TextTrimming.CharacterEllipsis });
                t.Children.Add(new TextBlock { Text = a.Id, Style = caption, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis });
                var rm = new Button { Content = new FontIcon { Glyph = "\uE711", FontSize = 12 }, Padding = new Thickness(8), Tag = a };
                ToolTipService.SetToolTip(rm, "Paketten çıkar");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(rm, a.Name + " paketten çıkar");
                rm.Click += (s, _) => list.Remove((BundleApp)((Button)s).Tag);
                Grid.SetColumn(rm, 1);
                row.Children.Add(t); row.Children.Add(rm);
                rows.Children.Add(row);
            }
        }
        list.CollectionChanged += (_, _) => Redraw();
        Redraw();
        var dialog = new ContentDialog
        {
            XamlRoot = Ui.Root,
            Title = dialogTitle,
            PrimaryButtonText = "Kaydet",
            CloseButtonText = "Vazgeç",
            DefaultButton = ContentDialogButton.Primary,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Content = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Spacing = 12, MinWidth = 460,
                    Children = { nameBox, descBox, search, count, new Border { Style = (Style)Application.Current.Resources["Card"], Padding = new Thickness(8, 4, 8, 4), Child = lv } },
                },
            },
        };
        if (App.Window?.Content is FrameworkElement fe) dialog.RequestedTheme = fe.ActualTheme;

        void Validate()
        {
            count.Text = list.Count == 0 ? "Henüz uygulama yok — yukarıdan arayıp ekleyin."
                : nameBox.Text.Trim().Length == 0 ? $"{list.Count} uygulama · kaydetmek için yukarıya bir paket adı yazın"
                : $"{list.Count} uygulama";
            dialog.IsPrimaryButtonEnabled = nameBox.Text.Trim().Length > 0 && list.Count > 0;
        }
        nameBox.TextChanged += (_, _) => Validate();
        list.CollectionChanged += (_, _) => Validate();
        Validate();

        CancellationTokenSource? cts = null;
        search.TextChanged += async (s, a) =>
        {
            if (a.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            cts?.Cancel(); cts = new CancellationTokenSource(); var ct = cts.Token;
            var q = s.Text.Trim();
            if (q.Length < 2) { s.ItemsSource = null; return; }
            try
            {
                await Task.Delay(300, ct);
                var r = await WingetService.SearchAsync(q, 12, ct);
                if (!ct.IsCancellationRequested) s.ItemsSource = r.Select(p => new Suggestion(p.Id, p.Name)).ToList();
            }
            catch { }
        };
        search.SuggestionChosen += (s, a) =>
        {
            if (a.SelectedItem is Suggestion sg && !list.Any(x => x.Id.Equals(sg.Id, StringComparison.OrdinalIgnoreCase)))
                list.Add(new BundleApp(sg.Id, sg.Name));
            s.Text = "";
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        return (nameBox.Text.Trim(), descBox.Text.Trim(), list.ToList());
    }

    private sealed record Suggestion(string Id, string Name)
    {
        public override string ToString() => $"{Name}  ·  {Id}";
    }
}
