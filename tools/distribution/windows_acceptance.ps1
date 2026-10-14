# Run in an interactive Windows x64 user desktop, never in the production data directory.
param([Parameter(Mandatory=$true)][string]$Installer)
$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitOperatingSystem) { throw 'Windows x64 gerekir.' }
if (Test-Path 'HKCU:\Software\EtiketTasarim') { throw 'Mevcut genel kurulum var. Bu testi temiz bir Windows kullanıcı profilinde çalıştırın.' }
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$root = Join-Path $env:TEMP ('Etiket-Kabul-' + [guid]::NewGuid().ToString('N'))
$program = Join-Path $root 'Program'
$data = Join-Path $root 'Veri'
New-Item -ItemType Directory -Path $data -Force | Out-Null
$oldDataRoot = $env:ETIKET_VERI
$env:ETIKET_VERI = $data
$result = [ordered]@{ root=$root; install=$false; launch=$false; companySettings=$false; templateOpen=$false; uninstall=$false; userFilesPreserved=$false; physicalPrinterTested=$false }
$ownedProcess = $null
function Find-Control($parent, [string]$id) {
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    $until = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $found = $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($found) { return $found }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $until)
    throw "Denetim bulunamadı: $id"
}
function Invoke-Control($element) {
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Set-Value($parent, [string]$id, [string]$value) {
    (Find-Control $parent $id).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
}
function Wait-Window($process) {
    $until = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $process.Refresh()
        if ($process.HasExited) { throw "Uygulama kapandı: $($process.ExitCode)" }
        if ($process.MainWindowHandle -ne 0) { return [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle) }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $until)
    throw 'Ana pencere açılmadı.'
}
function Stop-OwnedProcess($process) {
    if ($process -and -not $process.HasExited) {
        $null = $process.CloseMainWindow()
        if (-not $process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit() }
    }
}
try {
    $setup = Start-Process -FilePath (Resolve-Path $Installer) -ArgumentList "/S /D=$program" -Wait -PassThru
    if ($setup.ExitCode -ne 0) { throw "Kurulum kodu: $($setup.ExitCode)" }
    $app = Join-Path $program 'Etiket Tasarim.exe'
    if (-not (Test-Path $app)) { throw 'Uygulama EXE kurulmadı.' }
    $result.install = $true
    Set-Content -LiteralPath (Join-Path $data 'koru.txt') -Value 'Kullanıcı verisi korunmalı'
    Set-Content -LiteralPath (Join-Path $program 'kullanici-dosyasi.txt') -Value 'Paket dışı dosya korunmalı'
    $ownedProcess = Start-Process -FilePath $app -PassThru
    $main = Wait-Window $ownedProcess
    $result.launch = $true
    Invoke-Control (Find-Control $main 'OpenGeneralSettings')
    $settings = Find-Control ([System.Windows.Automation.AutomationElement]::RootElement) 'GeneralSettingsWindow'
    Set-Value $settings 'CompanyNameInput' 'Deneme Firma'
    Set-Value $settings 'CompanyAddressInput' 'Örnek Sokak 1'
    Set-Value $settings 'CompanyWebsiteInput' 'https://example.com/'
    $toggle = (Find-Control $settings 'CreateCompanyTemplates').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if ($toggle.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) { $toggle.Toggle() }
    Invoke-Control (Find-Control $settings 'SaveGeneralSettings')
    $settingsFile = Join-Path $data 'Ayarlar\ayarlar.json'
    $until = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 250
        if ((Test-Path $settingsFile) -and ((Get-Content $settingsFile -Raw | ConvertFrom-Json).company.name -eq 'Deneme Firma')) { break }
    } while ([DateTime]::UtcNow -lt $until)
    $saved = Get-Content $settingsFile -Raw | ConvertFrom-Json
    if ($saved.company.name -ne 'Deneme Firma' -or $saved.company.address -ne 'Örnek Sokak 1') { throw 'Firma bilgileri UI üzerinden kaydedilmedi.' }
    $template = Join-Path $data 'Belgeler\Şablonlar\Deneme Firma - Ürün Etiketi 100x100.akelabel'
    $until = [DateTime]::UtcNow.AddSeconds(20)
    while (-not (Test-Path $template) -and [DateTime]::UtcNow -lt $until) { Start-Sleep -Milliseconds 250 }
    if (-not (Test-Path $template)) { throw 'Firma şablonu oluşturulmadı.' }
    $result.companySettings = $true
    Stop-OwnedProcess $ownedProcess
    $ownedProcess = Start-Process -FilePath $app -ArgumentList ('"' + $template + '"') -PassThru
    $main = Wait-Window $ownedProcess
    if ($ownedProcess.MainWindowTitle -notlike '*Deneme Firma*') { throw 'Firma şablonu ana pencerede açılmadı.' }
    $result.templateOpen = $true
    Stop-OwnedProcess $ownedProcess
    $uninstall = Start-Process -FilePath (Join-Path $program 'Uninstall.exe') -ArgumentList '/S' -Wait -PassThru
    if ($uninstall.ExitCode -ne 0 -or (Test-Path $app)) { throw 'Kaldırma tamamlanmadı.' }
    $result.uninstall = $true
    $result.userFilesPreserved = (Test-Path (Join-Path $data 'koru.txt')) -and (Test-Path $template) -and (Test-Path (Join-Path $program 'kullanici-dosyasi.txt'))
    if (-not $result.userFilesPreserved) { throw 'Kullanıcı verisi korunmadı.' }
    $result.success = $true
} catch {
    $result.success = $false
    $result.error = $_.Exception.Message
    throw
} finally {
    Stop-OwnedProcess $ownedProcess
    $env:ETIKET_VERI = $oldDataRoot
    $result | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $root 'windows-kabul.json')
    Write-Host "Sonuç: $root\windows-kabul.json"
}
