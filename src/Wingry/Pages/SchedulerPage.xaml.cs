using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Wingry.Core;
using Wingry.Services;

namespace Wingry.Pages;

public sealed partial class SchedulerPage : Page
{
    private static readonly int[] Intervals = [15, 30, 60, 120, 240, 480];
    private static readonly DayOfWeek[] Days = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    public SchedulerPage()
    {
        InitializeComponent();
        RamInterval.SelectedIndex = Math.Max(0, Array.IndexOf(Intervals, Settings.GetInt("gorev_ram_aralik", 60)));
        RamDeep.IsOn = Settings.GetBool("gorev_ram_derin", false);
        RamNotify.IsOn = Settings.GetBool("ram_bildirim", false);
        UpdFreq.SelectedIndex = Settings.GetInt("gorev_guncelle_siklik", 1);
        UpdDay.SelectedIndex = Settings.GetInt("gorev_guncelle_gun", 0);
        UpdTime.Time = TimeSpan.FromMinutes(Settings.GetInt("gorev_guncelle_saat", 12 * 60));
        UpdMode.SelectedIndex = Settings.GetBool("gorev_guncelle_bildir", false) ? 1 : 0;
        OnFreqChanged(null, null);
        UpdateRamWarning();
    }

    private void OnRamOptionChanged(object sender, object e) => UpdateRamWarning();

    /// <summary>
    /// Temizlik belleği silmez, programların elinden alır; programlar sayfalara döndükçe geri
    /// okunur, önbellek de yeniden diskten dolar. Sık temizlik bu yüzden yavaşlatır.
    /// </summary>
    private void UpdateRamWarning()
    {
        if (RamWarn is null || RamInterval is null || RamDeep is null) return;
        int minutes = Intervals[Math.Max(0, RamInterval.SelectedIndex)];
        bool deep = RamDeep.IsOn;
        string? msg = deep && minutes < 240
            ? $"Derin temizlik Windows'un dosya önbelleğini de boşaltır; önbellek her seferinde diskten yeniden okunur. {RamInterval.SelectedItem} derin temizlik programların ve dosyaların açılışını yavaşlatır, bellek kısa sürede yine dolar. Derin temizlik için 4 saat ya da daha seyrek önerilir; daha iyisi Bellek sayfasındaki 'Bellek dolunca temizle' seçeneği."
            : !deep && minutes < 60
            ? $"Her temizlikte programlar bıraktıkları sayfaları yeniden ister ve kısa bir süre yavaşlar. {RamInterval.SelectedItem} temizlik fayda sağlamadan bu yavaşlamayı sık sık yaşatır. Saatte bir ya da 'Bellek dolunca temizle' seçeneği önerilir."
            : null;
        RamWarn.Message = msg ?? "";
        RamWarn.IsOpen = msg is not null;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        // Genişleticiler açılırken odak alan denetim sayfayı aşağı kaydırmasın
        Loaded += (_, _) => Scroller.ChangeView(null, 0, null, true);
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var (ram, upd) = await Task.Run(() => (SchedulerService.Query(SchedulerService.RamTask), SchedulerService.Query(SchedulerService.UpdateTask)));
        RamStatus.Text = Describe(ram);
        UpdStatus.Text = Describe(upd);
        RamRemove.IsEnabled = RamRun.IsEnabled = ram is not null;
        UpdRemove.IsEnabled = UpdRun.IsEnabled = upd is not null;
    }

    private static string Describe((string next, string last, string status)? q) =>
        q is null ? "Kapalı — görev yok"
                  : $"Etkin · sonraki çalışma: {Clean(q.Value.next)} · son çalışma: {Clean(q.Value.last)}";

    private static string Clean(string s) => string.IsNullOrWhiteSpace(s) || s.StartsWith("N/A", StringComparison.OrdinalIgnoreCase) || s.Contains("1999") || s.Contains("30.11.1999") ? "—" : s;

    private void OnFreqChanged(object? sender, SelectionChangedEventArgs? e)
    {
        if (UpdDay is null) return;
        UpdDay.Visibility = UpdFreq.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        UpdTime.Visibility = UpdFreq.SelectedIndex == 2 ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------------------------------------------------------------- RAM

    private async void OnRamSave(object sender, RoutedEventArgs e)
    {
        int minutes = Intervals[Math.Max(0, RamInterval.SelectedIndex)];
        Settings.Set("gorev_ram_aralik", minutes);
        Settings.Set("gorev_ram_derin", RamDeep.IsOn);
        Settings.Set("ram_bildirim", RamNotify.IsOn);
        RamRing.IsActive = true;
        try
        {
            var err = await SchedulerService.CreateAsync(SchedulerService.RamTask,
                "Wingry: belirli aralıklarla kullanılmayan belleği boşaltır.",
                RamDeep.IsOn ? "--task ram --deep" : "--task ram",
                new ScheduleSpec(ScheduleKind.Interval, minutes), highest: RamDeep.IsOn);
            if (err is null) Ui.Toast("RAM görevi kaydedildi", $"{RamInterval.SelectedItem} bellek temizlenecek.");
            else Ui.Error("Görev oluşturulamadı", err);
            await RefreshAsync();
        }
        finally { RamRing.IsActive = false; }
    }

    private async void OnRamRemove(object sender, RoutedEventArgs e)
    {
        var err = await SchedulerService.DeleteAsync(SchedulerService.RamTask, RamDeep.IsOn);
        if (err is null) Ui.Toast("Görev kaldırıldı", "Zamanlanmış RAM temizliği kapatıldı.", InfoBarSeverity.Informational);
        else Ui.Error("Görev kaldırılamadı", err);
        await RefreshAsync();
    }

    private async void OnRamRun(object sender, RoutedEventArgs e)
    {
        var err = await SchedulerService.RunNowAsync(SchedulerService.RamTask);
        if (err is null) Ui.Toast("Görev başlatıldı", "RAM temizliği arka planda çalışıyor.", InfoBarSeverity.Informational);
        else Ui.Error("Başlatılamadı", err);
        await Task.Delay(1500);
        await RefreshAsync();
    }

    // ---------------------------------------------------------------- güncelleme

    private async void OnUpdSave(object sender, RoutedEventArgs e)
    {
        Settings.Set("gorev_guncelle_siklik", UpdFreq.SelectedIndex);
        Settings.Set("gorev_guncelle_gun", UpdDay.SelectedIndex);
        Settings.Set("gorev_guncelle_saat", (int)UpdTime.Time.TotalMinutes);
        bool notify = UpdMode.SelectedIndex == 1;
        Settings.Set("gorev_guncelle_bildir", notify);

        var spec = UpdFreq.SelectedIndex switch
        {
            0 => new ScheduleSpec(ScheduleKind.Daily, At: UpdTime.Time),
            1 => new ScheduleSpec(ScheduleKind.Weekly, At: UpdTime.Time, Day: Days[Math.Max(0, UpdDay.SelectedIndex)]),
            _ => new ScheduleSpec(ScheduleKind.AtLogon),
        };
        UpdRing.IsActive = true;
        try
        {
            var err = await SchedulerService.CreateAsync(SchedulerService.UpdateTask,
                "Wingry: winget ile yüklü uygulamaları günceller.",
                notify ? "--task update --notify" : "--task update", spec, highest: false);
            if (err is null) Ui.Toast("Güncelleme görevi kaydedildi", notify ? "Güncelleme olduğunda bildirim alacaksınız." : "Güncellemeler zamanı gelince sessizce kurulacak.");
            else Ui.Error("Görev oluşturulamadı", err);
            await RefreshAsync();
        }
        finally { UpdRing.IsActive = false; }
    }

    private async void OnUpdRemove(object sender, RoutedEventArgs e)
    {
        var err = await SchedulerService.DeleteAsync(SchedulerService.UpdateTask, false);
        if (err is null) Ui.Toast("Görev kaldırıldı", "Zamanlanmış güncelleme kapatıldı.", InfoBarSeverity.Informational);
        else Ui.Error("Görev kaldırılamadı", err);
        await RefreshAsync();
    }

    private async void OnUpdRun(object sender, RoutedEventArgs e)
    {
        var err = await SchedulerService.RunNowAsync(SchedulerService.UpdateTask);
        if (err is null) Ui.Toast("Görev başlatıldı", "Güncelleme denetimi arka planda çalışıyor; bitince bildirim gelir.", InfoBarSeverity.Informational);
        else Ui.Error("Başlatılamadı", err);
        await Task.Delay(1500);
        await RefreshAsync();
    }

    private void OnIgnored(object sender, RoutedEventArgs e) => App.Window?.Navigate("settings");
}
