; ─────────────────────────────────────────────────────────────────────────────
;  Etiket Tasarım — Windows kurulum sihirbazı (Inno Setup 6)
;  v1 ("AKEL Etiket") ile yan yana kurulur: ayrı AppId, klasör, kısayol ve veri klasörleri.
;  Derleme: depo kökünde  powershell -ExecutionPolicy Bypass -File build.ps1
;  veya elle:  ISCC.exe installer\AkelEtiket.iss  (önce yayın klasörü oluşturulmalı)
; ─────────────────────────────────────────────────────────────────────────────

#ifndef AppVersion
  #define AppVersion "2.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\Etiket-Tasarim-win-x64"
#endif

#define AppName "Etiket Tasarım"
#define AppExe "Etiket Tasarim.exe"
#define AppPublisher "husodrn46"
#define AppUrl "https://github.com/husodrn46"

[Setup]
; v1 AppId {6F1C3A52-8D4E-4B7A-9E21-5C0D7A3B9F14} — v2 ayrı ürün olarak kurulur, v1 kaldırılmaz.
AppId={{90BF8735-B85C-461D-996D-21F14E0D409C}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppCopyright=© 2026 {#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoCompany={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Yönetici yetkisi olmadan "yalnızca benim için" kurulabilir; isteğe bağlı tüm kullanıcılar için.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 (1607) ve sonrası, Windows 11
MinVersion=10.0.14393
WizardStyle=modern
SetupIconFile=..\src\AkelEtiket.App\Assets\AkelEtiket.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=..\artifacts\installer
OutputBaseFilename=Etiket-Tasarim-Kurulum-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
ChangesAssociations=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; GroupDescription: "{cm:ExtraIcons}"
; Varsayılan olarak işaretsiz: .akelabel dosyaları v1'de açılmaya devam eder.
Name: "association"; Description: "{cm:FileAssociation}"; GroupDescription: "{cm:AssociationGroup}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\samples\Ornek-Urunler.xlsx"; DestDir: "{app}\Örnekler"; Flags: ignoreversion
Source: "..\samples\ornek-urunler-utf8.csv"; DestDir: "{app}\Örnekler"; Flags: ignoreversion
Source: "..\samples\ornek-urunler-1254.csv"; DestDir: "{app}\Örnekler"; Flags: ignoreversion
Source: "..\samples\ornek-hatali-veri.csv"; DestDir: "{app}\Örnekler"; Flags: ignoreversion
Source: "..\samples\*.akelabel"; DestDir: "{app}\Örnekler"; Flags: ignoreversion
Source: "..\samples\mcp\*"; DestDir: "{app}\Örnekler\mcp"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}\Belgeler"; Flags: ignoreversion
Source: "..\README.tr.md"; DestDir: "{app}\Belgeler"; DestName: "BENİOKU.md"; Flags: ignoreversion
Source: "..\docs\USER-GUIDE.md"; DestDir: "{app}\Belgeler"; Flags: ignoreversion
Source: "..\docs\INTERNATIONALIZATION.md"; DestDir: "{app}\Belgeler"; Flags: ignoreversion
Source: "..\samples\sample-*.csv"; DestDir: "{app}\Örnekler"; Flags: ignoreversion
Source: "..\docs\GENEL-URUN.md"; DestDir: "{app}\Belgeler"; Flags: ignoreversion
Source: "..\docs\MIMARI.md"; DestDir: "{app}\Belgeler"; Flags: ignoreversion
Source: "..\docs\LISANSLAR.md"; DestDir: "{app}\Belgeler"; Flags: ignoreversion

Source: "..\samples\packs\*"; DestDir: "{app}\Örnekler\packs"; Flags: ignoreversion recursesubdirs createallsubdirs

Source: "..\docs\WORKFLOWS.md"; DestDir: "{app}\Belgeler"; Flags: ignoreversion
Source: "..\docs\WORKFLOWS.tr.md"; DestDir: "{app}\Belgeler"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "{cm:AppDescription}"
Name: "{group}\{cm:SampleFiles}"; Filename: "{app}\Örnekler"
Name: "{group}\{cm:Documentation}"; Filename: "{app}\Belgeler"
Name: "{group}\{cm:UninstallApp}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\.akelabel"; ValueType: string; ValueName: ""; ValueData: "EtiketTasarim.Proje"; Flags: uninsdeletevalue; Tasks: association
Root: HKA; Subkey: "Software\Classes\EtiketTasarim.Proje"; ValueType: string; ValueName: ""; ValueData: "Etiket Tasarım projesi"; Flags: uninsdeletekey; Tasks: association
Root: HKA; Subkey: "Software\Classes\EtiketTasarim.Proje\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"; Tasks: association
Root: HKA; Subkey: "Software\Classes\EtiketTasarim.Proje\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: association

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Kullanıcının şablonları (Belgeler\Etiket Tasarım) ve ayarları bilerek SİLİNMEZ.
Type: filesandordirs; Name: "{app}\Örnekler"

[CustomMessages]
english.DesktopShortcut=Create a desktop shortcut
turkish.DesktopShortcut=Masaüstü kısayolu oluştur
english.ExtraIcons=Additional icons:
turkish.ExtraIcons=Ek simgeler:
english.FileAssociation=Open .akelabel project files with Etiket Tasarım
turkish.FileAssociation=.akelabel proje dosyalarını Etiket Tasarım ile aç
english.AssociationGroup=File association:
turkish.AssociationGroup=Dosya ilişkilendirme:
english.AppDescription=Label and barcode designer
turkish.AppDescription=Etiket ve barkod tasarımı
english.SampleFiles=Sample files
turkish.SampleFiles=Örnek dosyalar
english.Documentation=Documentation
turkish.Documentation=Belgeler
english.UninstallApp=Uninstall Etiket Tasarım
turkish.UninstallApp=Etiket Tasarım kaldır
english.LaunchApp=Launch Etiket Tasarım
turkish.LaunchApp=Etiket Tasarım uygulamasını başlat
