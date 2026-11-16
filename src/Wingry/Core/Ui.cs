using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Wingry.Core;

/// <summary>İletişim kutusu ve uygulama içi bildirim yardımcıları.</summary>
public static class Ui
{
    public static XamlRoot? Root => App.Window?.Content.XamlRoot;

    public static async Task<bool> ConfirmAsync(string title, string message, string primary = "Devam et", bool danger = false)
    {
        if (Root is null) return false;
        var d = new ContentDialog
        {
            XamlRoot = Root,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primary,
            CloseButtonText = "Vazgeç",
            DefaultButton = danger ? ContentDialogButton.Close : ContentDialogButton.Primary,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };
        if (App.Window?.Content is FrameworkElement fe) d.RequestedTheme = fe.ActualTheme;
        return await d.ShowAsync() == ContentDialogResult.Primary;
    }

    public static async Task AlertAsync(string title, string message)
    {
        if (Root is null) return;
        var d = new ContentDialog
        {
            XamlRoot = Root,
            Title = title,
            Content = new ScrollViewer { Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }, MaxHeight = 420 },
            CloseButtonText = "Tamam",
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };
        if (App.Window?.Content is FrameworkElement fe) d.RequestedTheme = fe.ActualTheme;
        await d.ShowAsync();
    }

    /// <summary>Sağ altta beliren, kendiliğinden kapanan bilgi şeridi.</summary>
    public static void Toast(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Success, int seconds = 5)
    {
        var host = App.Window?.ToastHost;
        if (host is null) return;
        var bar = new InfoBar
        {
            Title = title,
            Message = message,
            Severity = severity,
            IsOpen = true,
            MaxWidth = 440,
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SolidBackgroundFillColorTertiaryBrush"],
        };
        bar.Closed += (_, _) => host.Children.Remove(bar);
        host.Children.Insert(0, bar);
        while (host.Children.Count > 4) host.Children.RemoveAt(host.Children.Count - 1);
        var timer = host.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(seconds);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => { bar.IsOpen = false; host.Children.Remove(bar); };
        timer.Start();
    }

    public static void Error(string title, string message) => Toast(title, message, InfoBarSeverity.Error, 9);
}
