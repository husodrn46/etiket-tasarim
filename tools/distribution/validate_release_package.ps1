# Runs only on a clean Windows x64 test user; never point this at production data.
param([Parameter(Mandatory=$true)][string]$Manifest, [string]$Output='windows-results/package')
$ErrorActionPreference='Stop'
if (-not $IsWindows -or -not [Environment]::Is64BitOperatingSystem) { throw 'A Windows x64 test machine is required.' }
if (Test-Path 'HKCU:\Software\EtiketTasarim') { throw 'An existing installation was found; use a clean test user.' }
$candidate=Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
$repo=$env:GITHUB_REPOSITORY
if ($repo -ne 'husodrn46/etiket-tasarim') { throw 'Package checks are scoped to the public product repository.' }
$Output=[IO.Path]::GetFullPath($Output); New-Item -ItemType Directory -Path $Output -Force | Out-Null
$root=Join-Path $env:TEMP ('LabelHarborPackage-'+[guid]::NewGuid().ToString('N'))
$program=Join-Path $root 'Program';$data=Join-Path $root 'Data';$downloads=Join-Path $root 'Downloads'
New-Item -ItemType Directory -Path $data,$downloads -Force | Out-Null
$oldData=$env:ETIKET_VERI;$env:ETIKET_VERI=$data;$owned=$null
$result=[ordered]@{success=$false;version=$candidate.version;installerHash=$candidate.installerSha256;portableHash=$candidate.portableSha256;checks=@();physicalPrinterTested=$false;physicalDpiTested=$false}
function Check([bool]$passed,[string]$name) {
    $script:result.checks+=@{name=$name;passed=$passed}
    Write-Host "$(if($passed){'PASS'}else{'FAIL'}) $name"
    if(-not $passed){throw $name}
}
function Download([string]$tag,[string]$name,[string]$hash) {
    & gh release download $tag --repo $repo --pattern $name --dir $downloads
    if($LASTEXITCODE -ne 0){throw "Could not download $name"}
    $path=Join-Path $downloads $name
    Check ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -eq $hash) "SHA256 $name"
    return $path
}
function Install([string]$path) {
    $process=Start-Process -FilePath $path -ArgumentList "/S /D=$program" -PassThru
    if(-not $process.WaitForExit(120000)){ $process.Kill();throw 'Installer timeout' }
    Check ($process.ExitCode -eq 0) 'Silent installer exit code'
    Check (Test-Path (Join-Path $program 'Etiket Tasarim.exe')) 'Installed desktop app exists'
}
function Launch([string]$path) {
    $script:owned=Start-Process -FilePath $path -PassThru
    $until=[DateTime]::UtcNow.AddSeconds(40)
    do {
        Start-Sleep -Milliseconds 200;$script:owned.Refresh()
        if($script:owned.HasExited){throw "App exited during startup ($($script:owned.ExitCode))"}
        if($script:owned.MainWindowHandle -ne 0 -and $script:owned.MainWindowTitle -like '*LabelHarbor*'){break}
    } while([DateTime]::UtcNow -lt $until)
    Check ($script:owned.MainWindowHandle -ne 0 -and $script:owned.MainWindowTitle -like '*LabelHarbor*') 'Packaged native window starts'
    Check ($script:owned.CloseMainWindow()) 'Packaged window accepts close'
    Check ($script:owned.WaitForExit(15000) -and $script:owned.ExitCode -eq 0) 'Packaged app exits cleanly'
    $script:owned=$null
}
try {
    $setup=Download $candidate.tag $candidate.installer $candidate.installerSha256
    $zip=Download $candidate.tag $candidate.portable $candidate.portableSha256
    $previous=Download 'v2.0.0-preview.2' 'LabelHarbor-Setup-2.0.0-preview.2.exe' 'fb6605508d762bbce6e736d85971f3c7f82116d43bbfa50c142fe59f25be386c'
    Install $previous
    New-Item -ItemType Directory -Path (Join-Path $data 'Ayarlar') -Force | Out-Null
    $settings=Join-Path $data 'Ayarlar\ayarlar.json'
    '{"firstRunCompleted":true,"onboardingCompleted":true,"uiLanguage":"en","company":{"name":"Package Test Company"}}' | Set-Content -LiteralPath $settings -Encoding utf8
    $sentinel=Join-Path $data 'keep-user-data.txt';'Synthetic user data' | Set-Content $sentinel
    $unknown=Join-Path $program 'keep-custom-file.txt';'Unknown file' | Set-Content $unknown
    Install $setup
    $display=Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\EtiketTasarim'
    Check ($display.DisplayVersion -eq $candidate.version) 'Upgrade registers the new display version'
    Launch (Join-Path $program 'Etiket Tasarim.exe')
    Check ((Get-Content $settings -Raw | ConvertFrom-Json).company.name -eq 'Package Test Company') 'Upgrade preserves company settings'
    Install $previous
    Check ((Test-Path $sentinel) -and (Test-Path $unknown)) 'Rollback preserves user and unknown files'
    Install $setup
    $portable=Join-Path $root 'Portable';Expand-Archive -LiteralPath $zip -DestinationPath $portable
    Launch (Join-Path $portable 'LabelHarbor\Etiket Tasarim.exe')
    $uninstaller=Start-Process -FilePath (Join-Path $program 'Uninstall.exe') -ArgumentList "/S _?=$program" -PassThru
    Check ($uninstaller.WaitForExit(120000) -and $uninstaller.ExitCode -eq 0) 'Uninstaller exits cleanly'
    $until=[DateTime]::UtcNow.AddSeconds(10)
    while((Test-Path (Join-Path $program 'Etiket Tasarim.exe')) -and [DateTime]::UtcNow -lt $until){Start-Sleep -Milliseconds 200}
    Check (-not (Test-Path (Join-Path $program 'Etiket Tasarim.exe'))) 'Uninstaller removes package-owned executable'
    Check ((Test-Path $sentinel) -and (Test-Path $unknown) -and (Test-Path $settings)) 'Uninstaller preserves user data, settings and unknown files'
    $result.success=$true
} catch { $result.error=$_.Exception.ToString();throw }
finally {
    if($owned -and -not $owned.HasExited){$owned.Kill();$owned.WaitForExit()}
    $env:ETIKET_VERI=$oldData
    $result | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $Output 'results.json') -Encoding utf8
}
