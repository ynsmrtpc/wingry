using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Wingry.Services;

namespace Wingry.Pages;

public sealed partial class OperationsPage : Page
{
    public OperationsPage()
    {
        InitializeComponent();
        List.ItemsSource = OperationQueue.Items;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) { OperationQueue.Changed += Sync; Sync(); }
    protected override void OnNavigatedFrom(NavigationEventArgs e) => OperationQueue.Changed -= Sync;

    private void Sync() => Empty.Visibility = OperationQueue.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is OpItem i) OperationQueue.Cancel(i);
    }

    private void OnClear(object sender, RoutedEventArgs e) => OperationQueue.ClearFinished();
}
