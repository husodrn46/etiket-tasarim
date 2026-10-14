; -*- coding: utf-8 -*-
; Local cross-platform installer candidate. Product name remains provisional.
Unicode true
!include "MUI2.nsh"
!include "x64.nsh"
!include "WinVer.nsh"
!ifndef PAYLOAD
  !error "PAYLOAD is required"
!endif
!ifndef OUTPUT
  !error "OUTPUT is required"
!endif
!ifndef UNINSTALL_LIST
  !error "UNINSTALL_LIST is required"
!endif
!ifndef APP_VERSION
  !define APP_VERSION "2.0.0"
!endif
!define APP_NAME "Etiket Tasarım"
!define APP_EXE "Etiket Tasarim.exe"
Name "${APP_NAME} (yayın adayı)"
OutFile "${OUTPUT}"
InstallDir "$LOCALAPPDATA\Programs\Etiket Tasarim"
InstallDirRegKey HKCU "Software\EtiketTasarim" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey /LANG=1055 "ProductName" "${APP_NAME}"
VIAddVersionKey /LANG=1055 "FileDescription" "${APP_NAME} kurulum adayı"
VIAddVersionKey /LANG=1055 "FileVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=1055 "LegalCopyright" "Kaynak ve bağımlılık atıfları paket içinde yer alır."
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Turkish"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "Bu paket Windows x64 içindir."
    Abort
  ${EndIf}
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_ICONSTOP "Windows 10 veya sonrası gerekir."
    Abort
  ${EndIf}
  SetShellVarContext current
FunctionEnd

Section "Uygulama"
  SetOutPath "$INSTDIR"
  File /r "${PAYLOAD}/*"
  IfErrors 0 +3
    MessageBox MB_ICONSTOP "Dosyalar kopyalanamadı. Kurulum tamamlanmadı."
    Abort
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\Kaldır.lnk" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\EtiketTasarim" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\EtiketTasarim" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\EtiketTasarim" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\EtiketTasarim" "Publisher" "husodrn46"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\EtiketTasarim" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\EtiketTasarim" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\EtiketTasarim" "NoRepair" 1
SectionEnd

Section "Uninstall"
  SetShellVarContext current
  ; Only package-owned files are removed. User data and unknown files are preserved.
  !include "${UNINSTALL_LIST}"
  Delete "$INSTDIR\Uninstall.exe"
  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\Kaldır.lnk"
  RMDir "$SMPROGRAMS\${APP_NAME}"
  RMDir "$INSTDIR"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\EtiketTasarim"
  DeleteRegKey HKCU "Software\EtiketTasarim"
SectionEnd
