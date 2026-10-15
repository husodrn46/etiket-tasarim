<#
.SYNOPSIS
    LabelHarbor: test, yayın (self-contained win-x64) ve Windows kurulum paketi oluşturma.

.DESCRIPTION
    1) .NET 10 SDK'yı bulur (önce kullanıcı klasöründeki %USERPROFILE%\.dotnet, sonra PATH).
    2) Tüm otomatik testleri çalıştırır (-SkipTests ile atlanabilir).
    3) Uygulamayı kendi .NET çalışma zamanıyla birlikte yayınlar:
         artifacts\publish\Etiket-Tasarim-win-x64\Etiket Tasarim.exe
    4) Inno Setup (ISCC.exe) bulunursa kurulum sihirbazını oluşturur:
         artifacts\installer\LabelHarbor-Setup-<sürüm>.exe

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build.ps1
    powershell -ExecutionPolicy Bypass -File build.ps1 -Version 1.0.1 -SkipTests
#>
param(
    [string]$Configuration = "Release",
    [string]$Version = "2.0.0-preview.2",
    [switch]$SkipTests,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

function Step([string]$text) { Write-Host ""; Write-Host "==> $text" -ForegroundColor Cyan }

# ── 1) .NET SDK ────────────────────────────────────────────────────────────
$dotnet = $null
$localDotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (Test-Path $localDotnet) {
    $sdks = & $localDotnet --list-sdks 2>$null
    if ($sdks -match "^10\.") {
        $dotnet = $localDotnet
        $env:DOTNET_ROOT = Split-Path $localDotnet
    }
}
if (-not $dotnet) {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) {
        $sdks = & $cmd.Source --list-sdks 2>$null
        if ($sdks -match "^10\.") { $dotnet = $cmd.Source }
    }
}
if (-not $dotnet) {
    Write-Host ".NET 10 SDK bulunamadı. https://dotnet.microsoft.com/download/dotnet/10.0 adresinden '.NET 10 SDK (x64)' kurun." -ForegroundColor Red
    exit 1
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
Step "Kullanılan SDK: $(& $dotnet --version) ($dotnet)"

# ── 2) Testler ─────────────────────────────────────────────────────────────
if (-not $SkipTests) {
    Step "Otomatik testler çalıştırılıyor"
    # Testler Debug yapılandırmasında çalıştırılır: bazı antivirüs ürünleri (ör. Kaspersky) Release'te derlenen
    # test DLL'lerini sezgisel olarak karantinaya alabiliyor. Uygulamanın kendisi her zaman Release yayınlanır.
    & $dotnet test (Join-Path $root "AkelEtiket.sln") -c Debug
    if ($LASTEXITCODE -ne 0) { Write-Host "Testler başarısız; yayın durduruldu." -ForegroundColor Red; exit $LASTEXITCODE }
}

# ── 3) Yayın ───────────────────────────────────────────────────────────────
$publishDir = Join-Path $root "artifacts\publish\Etiket-Tasarim-win-x64"
Step "Yayınlanıyor: $publishDir"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
# Üç program (masaüstü, MCP sunucusu, Worker) aynı klasörü paylaşır. Her biri önce kendi ara klasörüne yayınlanır,
# sonra birleştirilir: EN SON masaüstü uygulamasının dosyaları kopyalanır ve onun sürümleri kazanır (ör. WPF'nin
# istediği System.IO.Packaging 10.x). Uygulamanın dosya kümesi diğer ikisinin bağımlılıklarını kapsar.
$stage = Join-Path $root "artifacts\publish-stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$projects = @("AkelEtiket.Mcp", "AkelEtiket.Worker", "AkelEtiket.App")
foreach ($project in $projects) {
    Step "Yayınlanıyor: $project"
    & $dotnet publish (Join-Path $root "src\$project\$project.csproj") `
        -c $Configuration -r win-x64 --self-contained true `
        -p:PublishReadyToRun=true -p:Version=$Version -p:DebugType=none -p:DebugSymbols=false `
        -o (Join-Path $stage $project)
    if ($LASTEXITCODE -ne 0) { Write-Host "$project yayını başarısız." -ForegroundColor Red; exit $LASTEXITCODE }
}
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
foreach ($project in $projects) {
    Copy-Item (Join-Path (Join-Path $stage $project) "*") $publishDir -Recurse -Force
}
# Doğrulama: masaüstü uygulamasının bütün dosyaları birleşik klasörde birebir aynı olmalı.
$appStage = Join-Path $stage "AkelEtiket.App"
$mismatch = Get-ChildItem $appStage -Recurse -File | Where-Object {
    $target = Join-Path $publishDir ($_.FullName.Substring($appStage.Length + 1))
    -not (Test-Path $target) -or (Get-FileHash $_.FullName).Hash -ne (Get-FileHash $target).Hash
}
if ($mismatch) { Write-Host "Birleştirme hatası: $($mismatch.Count) dosya uygulamanınkiyle aynı değil." -ForegroundColor Red; exit 1 }
Remove-Item $stage -Recurse -Force
Get-ChildItem $publishDir -Filter *.pdb -Recurse | Remove-Item -Force
$exe = Join-Path $publishDir "Etiket Tasarim.exe"
if (-not (Test-Path $exe)) { Write-Host "Etiket Tasarim.exe oluşmadı." -ForegroundColor Red; exit 1 }
foreach ($name in @("AkelEtiket.Mcp.exe", "AkelEtiket.Worker.exe")) {
    if (-not (Test-Path (Join-Path $publishDir $name))) { Write-Host "$name oluşmadı." -ForegroundColor Red; exit 1 }
}
$sizeMb = [math]::Round(((Get-ChildItem $publishDir -Recurse | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "Yayın tamam: $exe ($sizeMb MB, .NET kurulumu gerektirmez)" -ForegroundColor Green

# ── 4) Kurulum sihirbazı ───────────────────────────────────────────────────
if ($SkipInstaller) { exit 0 }
$isccCandidates = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
)
$iscc = $isccCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) {
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}
if (-not $iscc) {
    Write-Host "Inno Setup bulunamadı; kurulum dosyası oluşturulmadı." -ForegroundColor Yellow
    Write-Host "Kurmak için:  winget install --id JRSoftware.InnoSetup -e --scope user   (sonra bu betiği tekrar çalıştırın)"
    exit 0
}
Step "Kurulum sihirbazı derleniyor ($iscc)"
& $iscc "/DAppVersion=$Version" "/DAppNumericVersion=$($Version.Split('-')[0])" "/DPublishDir=$publishDir" (Join-Path $root "installer\AkelEtiket.iss")
if ($LASTEXITCODE -ne 0) { Write-Host "Kurulum paketi derlenemedi." -ForegroundColor Red; exit $LASTEXITCODE }
$setup = Join-Path $root "artifacts\installer\LabelHarbor-Setup-$Version.exe"
Write-Host "Kurulum dosyası: $setup" -ForegroundColor Green
