using Microsoft.UI.Xaml;
using Wingry.Core;
using Wingry.Services;

namespace Wingry;

public partial class App : Application
{
    public static MainWindow? Window { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => { Log.Error("UI", e.Exception); e.Handled = true; };
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("Task", e.Exception); e.SetObserved(); };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        Window.Activate();
        // Oturum açılışında başlatıldıysa (Run: --baslangic) istenirse simge durumunda bekle
        if (AutoStart.LaunchedAtLogon && Settings.GetBool("baslangicta_kucuk", true)
            && Window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter op)
        {
            // İlk çizimden önce küçültmek WinUI'de E_INVALIDARG ile çöküyor: içerik yüklendikten sonra küçült
            if (Window.Content is FrameworkElement root)
                root.Loaded += (_, _) => Window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => op.Minimize());
        }
        AutoStart.Refresh();

        // --sayfa <ad>: belirli bir sayfayla aç (kısayollar için). Ör. --sayfa apps, --sayfa tweak:AI
        var argv = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(argv, "--sayfa");
        if (i > 0 && i + 1 < argv.Length) Window.Navigate(argv[i + 1]);

        // Isınma: winget bağlantısı ve ince ayar kataloğu arka planda, pencere açıldıktan sonra.
        _ = Task.Run(TweakCatalog.Load);
        _ = AppState.RefreshInstalledAsync().ContinueWith(_ => WingetService.WarmUpSearchAsync());
        Pages.AutoCleaner.Restart();
        _ = UpdateService.LauncherPath; // başlatıcının yerini kaydet (yönetici kipinde ortam değişkeni kaybolabiliyor)
        _ = CheckUpdateQuietlyAsync();
        BundleService.StartWatching(); // Wingry.exe yanındaki Paketler klasörü
    }

    /// <summary>Günde en çok bir kez, açılıştan 15 sn sonra sessizce yeni sürüme bak.</summary>
    private static async Task CheckUpdateQuietlyAsync()
    {
        if (!Settings.GetBool("guncelleme_otomatik", true)) return;
        if (DateTime.Now - UpdateService.LastCheck < TimeSpan.FromHours(20)) return;
        await Task.Delay(15_000);
        try
        {
            if (await UpdateService.CheckAsync() is { } u)
                Ui.Toast("Wingry'nin yeni sürümü var", $"{u.Version.ToString(3)} hazır. Ayarlar > Wingry güncellemesi'nden tek tıkla yükleyebilirsiniz.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational, 15);
        }
        catch (Exception ex) { Log.Error("Güncelleme denetimi", ex); }
    }
}
