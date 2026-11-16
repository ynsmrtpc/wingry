# Wingry dağıtım derlemesi.
#   .\build.ps1              -> dist\Wingry.exe (tek dosya, çift tıklayınca açılır)
#   .\build.ps1 -Klasor      -> dist\Wingry-win-x64\ (DLL'li klasör + zip)
#   .\build.ps1 -Surum 1.2.0 -> sürüm numarasıyla (GitHub'a v1.2.0 etiketiyle yüklenecek)
#   .\build.ps1 -Mimari arm64
param(
    [ValidateSet('x64', 'arm64')] [string]$Mimari = 'x64',
    [string]$Surum = '',
    [switch]$Klasor,
    [switch]$ZipYok
)
$ErrorActionPreference = 'Stop'
$kok = $PSScriptRoot
$proje = Join-Path $kok 'src\Wingry\Wingry.csproj'
if (-not $Surum) { $Surum = ([xml](Get-Content $proje -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
$surumOz = "-p:Version=$Surum"

if (-not $Klasor) {
    # Tek dosya = başlatıcı + içine gömülü uygulama klasörü. Uygulamanın kendisi doğrudan
    # tek dosya yapılamıyor: winget COM sunucusu .NET tek dosya ana makinesini reddediyor (0x80073D54).
    if ($Mimari -ne 'x64') { throw "Tek dosya şimdilik yalnız x64; arm64 için -Klasor kullanın." }
    $yuk = Join-Path $kok 'dist\yuk'
    if (Test-Path $yuk) { Remove-Item -Recurse -Force $yuk }
    dotnet publish $proje -c Release -r win-x64 -p:Platform=x64 -p:DebugType=none -o (Join-Path $yuk 'uygulama') $surumOz
    if ($LASTEXITCODE -ne 0) { throw "Uygulama derlenemedi ($LASTEXITCODE)" }
    if (-not (Test-Path (Join-Path $yuk 'uygulama\Wingry.pri'))) { throw "Wingry.pri yok (EnableMsixTooling kapalı mı?)" }
    Compress-Archive -Path (Join-Path $yuk 'uygulama\*') -DestinationPath (Join-Path $yuk 'uygulama.zip') -CompressionLevel Optimal
    Set-Content (Join-Path $yuk 'surum.txt') (Get-Date -Format 'yyyyMMddHHmmss') -Encoding ascii

    $tek = Join-Path $kok 'dist\tek'
    if (Test-Path $tek) { Remove-Item -Recurse -Force $tek }
    dotnet publish (Join-Path $kok 'src\WingryBaslatici\WingryBaslatici.csproj') -c Release -o $tek $surumOz
    if ($LASTEXITCODE -ne 0) { throw "Başlatıcı derlenemedi ($LASTEXITCODE)" }
    $exe = Join-Path $kok 'dist\Wingry.exe'
    Move-Item (Join-Path $tek 'Wingry.exe') $exe -Force
    Remove-Item -Recurse -Force $tek, $yuk
    Write-Host ("Hazır: {0} ({1:N0} MB)" -f $exe, ((Get-Item $exe).Length / 1MB)) -ForegroundColor Green
    return
}
$cikti = Join-Path $kok "dist\Wingry-win-$Mimari"
if (Test-Path $cikti) { Remove-Item -Recurse -Force $cikti }
dotnet publish $proje -c Release -r "win-$Mimari" "-p:Platform=$Mimari" -o $cikti
if ($LASTEXITCODE -ne 0) { throw "Derleme başarısız ($LASTEXITCODE)" }

# Paketsiz WinUI yayınında uygulamanın kaynak dizini olmazsa pencere açılır açılmaz kapanır.
if (-not (Test-Path (Join-Path $cikti 'Wingry.pri'))) { throw "Wingry.pri yayın klasöründe yok (EnableMsixTooling kapalı mı?)" }

$boyut = (Get-ChildItem $cikti -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Hazır: {0} ({1:N0} MB)" -f $cikti, $boyut) -ForegroundColor Green

if (-not $ZipYok) {
    $zip = "$cikti.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path "$cikti\*" -DestinationPath $zip -CompressionLevel Optimal
    Write-Host ("Zip: {0} ({1:N0} MB)" -f $zip, ((Get-Item $zip).Length / 1MB)) -ForegroundColor Green
}
