# Wingry

Windows 11 için uygulama yönetimi, sistem bakımı ve gizlilik ayarları tek pencerede.
`winmanager/wingetui` (Electron) ve `winmanager/wingry` (PyQt, "Wingy") uygulamalarının
yerini alır; Win11Debloat'ın ayarlarını da içerir.

## Teknoloji ve neden

| Seçim | Neden |
|---|---|
| **C# / .NET 10 + WinUI 3** (Windows App SDK 1.8) | Windows 11'in kendi arayüz katmanı: Mica, Fluent denetimler, Ayarlar uygulamasıyla aynı görünüm. Tarayıcı (Electron) ya da yorumlayıcı (Python) taşımaz. |
| **winget COM API** (`Microsoft.Management.Deployment`) | Eski iki uygulama `winget list` metin tablosunu sütun konumundan ayrıştırıyordu: Türkçe başlıklarda, "…" ile kırpılan adlarda bozuluyordu. COM API yapılandırılmış veri verir. 277 yüklü paket + güncelleme taraması ~1,5 sn (CLI ile 15–30 sn). |
| **`.reg` dosyalarını kendi motorumuzla uygulama** | `reg.exe` çağrılmaz; aynı ayrıştırıcı her ayarın **şu anki durumunu** okur, anahtarlar gerçeği gösterir. |
| **ReadyToRun + kırpma** | Ön derlenmiş kod, ~0,3 sn açılış. Yayın klasörü 138 MB (zip 56 MB). |

## Sayfalar

- **Genel bakış** — canlı CPU/RAM grafikleri, sistem diski, güncelleme sayısı, hızlı işlemler.
- **Uygulamalar** — Yüklü · Güncellemeler · Keşfet (winget deposunda arama) · Hazır paketler
  (Temel, Geliştirici, Ofis, Oyuncu, Tasarım, Sistem araçları; yüklü olanlar atlanır).
  Toplu kur/güncelle/kaldır, güncelleme yoksayma.
  **Kendi paketlerin:** Yüklü sekmesinde seçip *Paket oluştur* ya da *Yeni paket* (winget'te ara-ekle); düzenle, dışa aktar (.json), içe aktar.
  `Wingry.exe`'nin yanındaki **Paketler** klasörüne bırakılan .json dosyaları kendiliğinden listelenir (klasör izlenir).
  `winget export` çıktısı da paket olarak tanınır. Kullanıcı paketleri: `%LOCALAPPDATA%\Wingry\Paketlerim`. İşlemler sırayla çalışır, ilerleme **İşlemler** sayfasında.
- **Bellek (RAM)** — hızlı temizlik (yönetici gerekmez), derin temizlik (bekleme listesi +
  dosya önbelleği), eşik aşılınca otomatik temizlik, en çok bellek kullananlar.
- **Disk alanı** — sürücü tarama (C: 2,6 milyon dosya ~15 sn), en büyükten küçüğe dosyalar,
  tür / boyut / tarih / ad filtreleri, klasör ağacında gezinme, türlere göre dağılım,
  Geri Dönüşüm Kutusu'na taşıma. **Temizlik** sekmesi: geçici dosyalar, Windows Update
  önbelleği, hata raporları vb. (son 24 saatte değişen dosyalara dokunmaz).
- **Başlangıç uygulamaları** — Run anahtarları ve Başlangıç klasörleri; aç/kapa Görev Yöneticisi ile aynı `StartupApproved` değeriyle yapılır (silmez).
- **Hizmetler** — arama, süzme (çalışan, devre dışı, Microsoft dışı…), başlat/durdur/yeniden başlat, başlangıç türü. Kritik hizmetler (`HizmetService.Critical`) durdurulamaz / kapatılamaz; işlemler yönetici işçisinde.
- **Araçlar**
  - *Taşıma ve yedek* — bu bilgisayarın profili (winget uygulamaları, uygulanmış ince ayarlar, kapalı başlangıç öğeleri) tek .json; içe aktarınca yalnız eksikler uygulanır. Komut satırı: `Wingry.exe --task profil <dosya.json>`.
  - *Sürücüler* — liste (Microsoft dışı / 3 yıldan eski süzgeci), `DISM /Export-Driver` ile klasöre yedek, isteğe bağlı sürücü güncellemelerine kısayol.
  - *Ağ* — bağlantı başına DNS (Cloudflare, Google, Quad9, AdGuard, otomatik), DNS önbelleği, ağ sıfırlama, kayıtlı Wi-Fi şifreleri.
  - *Güç ve pil* — güç planı, Nihai performans planı, hazırda bekletme (hiberfil.sys), pil durumu ve `powercfg /batteryreport`.
  - *Bağlam menüsü* — sağ tık girdilerini gizle/göster; yönetici gerekmez (HKCU `LegacyDisable` / `Shell Extensions\Blocked`).
  - *Sistem onarımı* — disk sağlığı, DISM /RestoreHealth, SFC /scannow, bileşen deposu temizliği.
  - *Hosts dosyası* — düzenleyici + StevenBlack engelleme listesi (işaretler arasında, düzenleyicide gizli). Kaydetmeden önce yedeklenir.
- **Disk alanı > Kopyalar** — son taramada boyut + SHA-256 ile birebir aynı dosyalar; sistem/program klasörleri varsayılan olarak hariç, hiçbir dosya kendiliğinden seçilmez.
- **Zamanlayıcılar** — RAM temizleme ve uygulama güncelleme için Windows Görev Zamanlayıcı
  görevleri (`\Wingry\` klasörü). Wingry kapalıyken de çalışır.
- **İnce ayarlar** — Win11Debloat'ın her kategorisi ayrı sayfa: Gizlilik ve önerilen içerik
  (telemetri), Sistem, Başlat menüsü ve arama, Yapay zeka, Windows Update, Görev çubuğu,
  Görünüm, Dosya Gezgini, Oyun, Çoklu görev, İsteğe bağlı özellikler, Diğer.
  Değişiklikler toplanır, **Uygula** ile tek seferde yazılır; öncesinde değişen değerler
  `.reg` olarak yedeklenir (Ayarlar > Kayıt defteri yedekleri'nden geri yüklenir).
- **Gereksiz uygulamalar** — Win11Debloat listesindeki, bu bilgisayarda yüklü Store uygulamaları.
- **Ayarlar** — tema, sessiz kurulum, Store güncellemeleri, yoksayılanlar, geri yükleme noktası, yedekler, günlük.

Başlık çubuğundaki arama kutusu sayfaları, ince ayarları ve winget deposunu birlikte arar.

## Yönetici izni

Arayüz **yönetici olmadan** çalışır. Yönetici gerektiren işler (HKLM ayarları, hizmetler,
DISM, derin RAM temizliği, yüksek yetkili görevler) toplanır ve **tek bir UAC onayıyla**
`Wingry.exe --worker` olarak çalıştırılır. Başlık çubuğundaki "Yönetici değil" düğmesi
uygulamayı yönetici olarak yeniden başlatır (o zaman hiç UAC sorulmaz).

Kurulum / güncelleme / kaldırma da aynı yoldan gider: kuyruk başında **tek UAC**, sonra
`winget --silent` yönetici işçide paket paket çalışır. (winget'in COM sunucusu arka planda
olduğu için kurulum programlarının UAC penceresi öne gelmiyor, görev çubuğunda bekliyordu.)
Ayarlar'dan kapatılabilir; kapalıyken eski davranış (COM, paket başına izin) geçerlidir.

## Derleme

```powershell
.\build.ps1                 # dist\Wingry.exe — tek dosya (110 MB), çift tıklayınca açılır
.\build.ps1 -Klasor         # dist\Wingry-win-x64\ + zip (DLL'li klasör)
.\build.ps1 -Mimari arm64
```

Tek dosyalı `Wingry.exe` bir **başlatıcıdır**: uygulama klasörü içine zip olarak gömülü. İlk açılışta
`%LOCALAPPDATA%\Wingry\uygulama` altına bir kez açılır (~5 sn), sonraki açılışlar ~0,5 sn.
Yeni sürüm exe'si çalıştırılınca klasör kendiliğinden yenilenir. Zamanlanmış görevler o klasördeki exe'yi çağırır.

**Neden uygulamanın kendisi tek dosya değil:** winget COM sunucusu .NET tek dosya ana makinesinden
gelen çağrıları `0x80073D54` (Türkçe metni yanıltıcı: *Arabirim desteklenmiyor*) ile reddediyor —
yardımcı DLL'le de, doğrudan `CoCreateInstance` ile de. **Kırpma (PublishTrimmed) da kapalı**:
WinUI/COM projeksiyonlarını bozuyor (açılışta `0xC0000602`).

Gereken: .NET 10 SDK. Çıktı kendine yeterli; hedef bilgisayara .NET ya da Windows App
Runtime kurmak gerekmez. winget (App Installer) Windows 11'de hazır gelir.

Geliştirirken: `dotnet build src\Wingry -c Debug` ve `src\Wingry\bin\x64\Debug\...\Wingry.exe`.

## Yeni sürüm yayımlama (Wingry'nin kendini güncellemesi)

Wingry, Ayarlar > **Wingry güncellemesi**'nden ve açılışta günde bir kez
`github.com/ynsmrtpc/wingry` deposunun **en son Release**'ine bakar. Etiket yüklü sürümden
yüksekse "İndir ve yükle" çıkar; indirip kendini yeniler, yeniden başlar.

1. `src\Wingry\Wingry.csproj` içindeki `<Version>`'ı artırın (ya da `-Surum` verin).
2. `.\build.ps1 -Surum 1.1.0`
3. GitHub'da **v1.1.0** etiketiyle yeni Release açın, `dist\Wingry.exe`'yi **tam bu adla**
   ekleyin. Release açıklaması uygulamada sürüm notu olarak görünür.

Güncelleme nasıl yerleşir: yeni exe, kullanıcının çalıştırdığı `Wingry.exe`'nin (ör. masaüstündeki)
üzerine yazılır ve `--bekle <PID>` ile başlatılır; eski sürüm kapanınca yenisi
`%LOCALAPPDATA%\Wingry\uygulama`'ya açılır. Başlatıcı yalnızca **daha yeni** sürümü açar; eski bir
exe sonradan çalıştırılırsa kurulu yeni sürümü ezmez. Test için `WINGRY_KURULUM` ortam değişkeni
kurulum kökünü değiştirir.
## Komut satırı

| Parametre | İş |
|---|---|
| `--sayfa <ad>` | Belirli sayfayla aç: `dashboard`, `apps`, `memory`, `disk`, `scheduler`, `bloat`, `ops`, `settings`, `tweak:AI`, `tweak:Taskbar`… |
| `--baslangic` | Oturum açılışında (Ayarlar > Windows açılınca başlat) kullanılır; isteğe göre simge durumunda açar |
| `--task ram [--deep]` | Arayüzsüz RAM temizliği (zamanlanmış görev bunu çağırır) |
| `--task update [--notify]` | Arayüzsüz güncelleme ya da yalnızca bildirim |
| `--worker <istek>` | İç kullanım: yönetici işçisi |

## Dosyalar

```
src/Wingry/
  Program.cs                 Giriş: arayüz / --task / --worker
  Core/                      Ayarlar, yönetici köprüsü, P/Invoke, başsız görevler
  Services/
    WingetService.cs         winget COM: liste, güncelleme, arama, kur/güncelle/kaldır
    RegFile.cs               .reg ayrıştır / uygula / durum oku / yedekle
    TweakCatalog.cs          Win11Debloat Features.json + tr-TR çevirileri
    TweakService.cs          İnce ayar uygulama (HKCU burada, HKLM işçide)
    DiskService.cs           Çok iş parçacıklı tarayıcı
    MemoryService.cs         Çalışma kümesi / bekleme listesi temizliği
    SchedulerService.cs      Görev Zamanlayıcı (schtasks + XML)
  Pages/                     Sayfalar (XAML + kod)
  Data/Win11Debloat/         Win11Debloat yapılandırması ve .reg dosyaları (MIT)
  Data/paketler.json         Hazır paket listeleri — yeni paket buraya eklenir
```

Kullanıcı verisi: `%LOCALAPPDATA%\Wingry\` (`ayarlar.json`, `Yedekler\`, `Gunluk\`).

## Bilinen sınırlar

- Win11Debloat'ın şu özellikleri yok: Store arama önerileri (sqlite veritabanı düzenliyor),
  tüm kullanıcılar için Başlat menüsü, Edge'i zorla kaldırma, Sysprep kipi.
- Disk taraması dizin listeleme ile yapılır (yönetici gerekmez). WizTree gibi MFT okuması
  yoktur; erişilemeyen klasörler sayılmaz ve sayısı ekranda yazılır. Liste 500 bin dosyayı
  geçince 16 KB altı dosyalar tek tek tutulmaz (klasör ve tür toplamlarında sayılır), bellek
  ~400 MB'de kalır.
- Kurulum/güncelleme/kaldırma winget'in yaptığı kadar sessizdir; bazı kurulum programları
  yine de kendi penceresini ya da UAC'yi açar.

## Lisanslar

- Win11Debloat yapılandırması: © Raphire, MIT — `Data/Win11Debloat/LICENSE.txt`
- Windows Community Toolkit: MIT
