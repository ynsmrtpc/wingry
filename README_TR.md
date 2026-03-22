# Wingy 📦

**Wingy**, Windows Paket Yöneticisi (**winget**) için özel olarak **Windows 11** tasarım dili (Fluent Design) ile geliştirilmiş modern, hızlı ve şık bir grafik arayüzüdür.

<img width="1100" height="900" alt="wingy" src="https://github.com/user-attachments/assets/61324725-7e7c-4e31-96b2-bec74930d59b" />

## ✨ Özellikler

- **Keşfet**: Hazır paketleri (Oyuncu, Geliştirici, İçerik Üretici) inceleyin veya binlerce uygulama arasından arama yapın.
- **Modern Arayüz**: `PyQt-Fluent-Widgets` kullanılarak tam uyumlu Windows 11 Fluent Design uygulaması.
- **Toplu İşlemler**: Birden fazla uygulama seçip aynı anda kurun, kaldırın veya güncelleyin.
- **Akıllı Takip**: Durum göstergeleri ve gerçek zamanlı ilerleme kartları.
- **Sistem Bilgisi**: İşlemci, RAM, işletim sistemi ve ağ durumu gibi temel PC bilgilerini görüntüleyin.
- **Animasyonlar**: Yumuşak kenarlar, hover (üzerine gelme) efektleri ve akıcı geçişler ile premium bir deneyim.

## 🛠️ Teknolojiler

- **Python 3.11+**
- **PyQt6** - Ana UI çatısı.
- **PyQt-Fluent-Widgets** - Windows 11 temalı bileşenler.
- **WinGet CLI** - Arka plan paket işlemleri.

## 🚀 Başlarken

### Gereksinimler
- Windows 10/11
- Winget yüklü olmalıdır (modern Windows sürümlerinde standarttır)
- Python 3.11+

### Kurulum
1. Repoyu klonlayın:
   ```bash
   git clone https://github.com/kullaniciadi/wingy.git
   ```
2. Bağımlılıkları yükleyin:
   ```bash
   pip install -r requirements.txt
   ```
3. Uygulamayı çalıştırın:
   ```bash
   python main.py
   ```

## 🏗️ Build (.exe oluşturma)
Tek başına çalışan bir `.exe` dosyası oluşturmak için:
```cmd
build.bat
```

## 📄 Lisans
Bu proje kişisel kullanım ve gösterim amaçlıdır. Wingit CLI kullanım şartları için resmi Winget lisansına bakınız.
