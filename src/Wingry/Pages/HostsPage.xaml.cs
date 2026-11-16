using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Wingry.Core;

namespace Wingry.Pages;

/// <summary>
/// Hosts düzenleyici. Engelleme listesi işaretler arasında tutulur ve düzenleyicide gösterilmez
/// (80 bin satır metin kutusunu ağırlaştırır); kaydederken kullanıcı kısmına geri eklenir.
/// </summary>
public sealed partial class HostsPage : Page
{
    private const string Begin = "# >>> Wingry engelleme listesi (elle düzenlemeyin)";
    private const string End = "# <<< Wingry engelleme listesi";
    private const string ListUrl = "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts";
    private static string HostsPath => Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts");

    private string _userPart = "";
    private string _blockPart = "";
    private bool _loading;

    public HostsPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e) => Load();

    private void Load()
    {
        string text = "";
        try { text = File.ReadAllText(HostsPath); }
        catch (Exception ex) { Ui.Error("Hosts okunamadı", ex.Message); }
        int b = text.IndexOf(Begin, StringComparison.Ordinal), en = text.IndexOf(End, StringComparison.Ordinal);
        if (b >= 0 && en > b)
        {
            _blockPart = text[b..(en + End.Length)];
            _userPart = (text[..b] + text[(en + End.Length)..]).TrimEnd() + Environment.NewLine;
        }
        else { _blockPart = ""; _userPart = text; }
        _loading = true; Editor.Text = _userPart; _loading = false;
        SaveBtn.IsEnabled = false;
        UpdateStatus();
    }

    /// <summary>WinUI TextBox satır sonu olarak yalnız \r verir; dosya \r\n ister. Her karşılaştırma ve yazma bundan geçer.</summary>
    private static string Norm(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');

    private int BlockedCount => Norm(_blockPart).Split('\n').Count(l => l.StartsWith("0.0.0.0 ", StringComparison.Ordinal));

    private void UpdateStatus()
    {
        int n = BlockedCount;
        BlockCard.Description = n > 0
            ? $"Etkin: {Format.Number(n)} alan adı engelleniyor. Bir site bozulursa listeyi kaldırın ya da güncelleyin."
            : "StevenBlack birleşik listesi (reklam + zararlı alan adları). Tüm tarayıcı ve uygulamalarda çalışır. Şu an kapalı.";
        BlockRemove.IsEnabled = n > 0;
        int lines = Norm(Editor.Text).Split('\n').Count(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#'));
        Status.Text = $"{lines} etkin kayıt · {HostsPath}";
    }

    private void OnEdit(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        SaveBtn.IsEnabled = Norm(Editor.Text).TrimEnd() != Norm(_userPart).TrimEnd();
        UpdateStatus();
    }

    private void OnRevert(object sender, RoutedEventArgs e) => Load();

    private async Task<bool> WriteAsync(string userPart, string blockPart)
    {
        var sb = new StringBuilder(Norm(userPart).Replace("\n", "\r\n").TrimEnd()).Append("\r\n");
        if (blockPart.Length > 0) sb.Append("\r\n").Append(Norm(blockPart).Replace("\n", "\r\n")).Append("\r\n");
        var tmp = Path.Combine(Paths.Temp, $"hosts-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(tmp, sb.ToString(), new UTF8Encoding(false));
        try
        {
            var w = await Elevation.RunAsync([$"arac|hosts|{tmp}"]);
            if (!w.Started) { Ui.Error("Kaydedilemedi", w.Error ?? ""); return false; }
            if (w.Failures > 0) { Ui.Error("Kaydedilemedi", string.Join("\n", w.Lines) + "\nVirüs koruması hosts dosyasını kilitliyor olabilir."); return false; }
            Log.Write($"Hosts kaydedildi ({Format.Number(BlockedCountOf(blockPart))} engelli alan adı)");
            return true;
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    private static int BlockedCountOf(string s) => s.Split('\n').Count(l => l.StartsWith("0.0.0.0 ", StringComparison.Ordinal));

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        SaveBtn.IsEnabled = false;
        if (await WriteAsync(Editor.Text, _blockPart)) { Ui.Toast("Hosts kaydedildi", "Eski dosya Ayarlar > Yedekler klasörüne kopyalandı."); Load(); }
        else SaveBtn.IsEnabled = true;
    }

    private async void OnBlockAdd(object sender, RoutedEventArgs e)
    {
        if (Norm(Editor.Text).TrimEnd() != Norm(_userPart).TrimEnd() && !await Ui.ConfirmAsync("Kaydedilmemiş değişiklikler", "Düzenleyicideki değişiklikler de kaydedilecek. Devam edilsin mi?", "Devam et")) return;
        BlockAdd.IsEnabled = false; BlockRing.IsActive = true;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Wingry");
            var list = await http.GetStringAsync(ListUrl);
            // Yalnızca engelleme satırlarını al; localhost ve yorum satırlarını dışarıda bırak
            var lines = list.Split('\n').Select(l => l.Trim())
                .Where(l => l.StartsWith("0.0.0.0 ", StringComparison.Ordinal) && !l.EndsWith(" 0.0.0.0", StringComparison.Ordinal))
                .Select(l => { int h = l.IndexOf('#'); return h > 0 ? l[..h].TrimEnd() : l; })
                .Distinct().ToList();
            if (lines.Count < 1000) { Ui.Error("Liste indirilemedi", "Beklenenden kısa bir yanıt geldi; daha sonra tekrar deneyin."); return; }
            var block = $"{Begin}\n# Kaynak: {ListUrl}\n# Güncellendi: {DateTime.Now:dd.MM.yyyy HH:mm}\n{string.Join('\n', lines)}\n{End}";
            if (await WriteAsync(Editor.Text, block))
            {
                Ui.Toast("Engelleme listesi etkin", $"{Format.Number(lines.Count)} alan adı engelleniyor.");
                Load();
            }
        }
        catch (Exception ex) { Ui.Error("Liste indirilemedi", ex.Message); }
        finally { BlockAdd.IsEnabled = true; BlockRing.IsActive = false; }
    }

    private async void OnBlockRemove(object sender, RoutedEventArgs e)
    {
        BlockRing.IsActive = true;
        try
        {
            if (await WriteAsync(Editor.Text, "")) { Ui.Toast("Engelleme listesi kaldırıldı", "Hosts dosyası yalnızca kendi kayıtlarınızı içeriyor."); Load(); }
        }
        finally { BlockRing.IsActive = false; }
    }
}
