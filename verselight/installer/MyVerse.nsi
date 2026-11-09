; Installateur MyVerse (NSIS, compilé sur GitHub Actions)
; Installation par utilisateur : pas besoin d'être administrateur.
Unicode true
!include "MUI2.nsh"

!ifndef VERSION
  !define VERSION "3.0.2"
!endif
!define APP "MyVerse"
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\MyVerse"

Name "${APP}"
OutFile "MyVerse-Setup-${VERSION}.exe"
InstallDir "$LOCALAPPDATA\Programs\MyVerse"
InstallDirRegKey HKCU "Software\MyVerse" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
!define MUI_ICON "myverse.ico"
!define MUI_UNICON "myverse.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\MyVerse.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Lancer MyVerse"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "French"

; mise à jour automatique (lancée par MyVerse avec /S) : on rouvre MyVerse à la fin
Function .onInstSuccess
  IfSilent 0 +2
  Exec '"$INSTDIR\MyVerse.exe"'
FunctionEnd

Function FermerMyVerse
  nsExec::Exec 'taskkill /F /IM MyVerse.exe'
  nsExec::Exec 'taskkill /F /IM verselight-bridge.exe'
  Sleep 1000
FunctionEnd

Section "MyVerse"
  Call FermerMyVerse
  SetOutPath "$INSTDIR"
  File "app\MyVerse.exe"
  File "app\resources.neu"
  File "app\LISEZ-MOI.txt"
  File "myverse.ico"
  SetOutPath "$INSTDIR\extensions\bridge"
  File "app\extensions\bridge\verselight-bridge.exe"
  SetOutPath "$INSTDIR"

  ; pare-feu Windows (une seule autorisation) : pour que le téléphone puisse joindre le PC, même sur un Wi-Fi « public »
  IfSilent +2
  ExecShellWait "runas" "$INSTDIR\extensions\bridge\verselight-bridge.exe" "--parefeu" SW_HIDE

  CreateShortcut "$DESKTOP\MyVerse.lnk" "$INSTDIR\MyVerse.exe" "" "$INSTDIR\myverse.ico"
  CreateShortcut "$SMPROGRAMS\MyVerse.lnk" "$INSTDIR\MyVerse.exe" "" "$INSTDIR\myverse.ico"

  WriteUninstaller "$INSTDIR\Desinstaller.exe"
  WriteRegStr HKCU "Software\MyVerse" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayName" "MyVerse"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINST_KEY}" "Publisher" "MEPC Montréal"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\myverse.ico"
  WriteRegStr HKCU "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "UninstallString" '"$INSTDIR\Desinstaller.exe"'
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoRepair" 1
SectionEnd

; Désinstaller : retire l'app, garde chants, réglages et téléphones jumelés (%APPDATA%)
Section "Uninstall"
  Call un.FermerMyVerse
  Delete "$DESKTOP\MyVerse.lnk"
  Delete "$SMPROGRAMS\MyVerse.lnk"
  RMDir /r "$INSTDIR\extensions"
  Delete "$INSTDIR\MyVerse.exe"
  Delete "$INSTDIR\resources.neu"
  Delete "$INSTDIR\LISEZ-MOI.txt"
  Delete "$INSTDIR\myverse.ico"
  Delete "$INSTDIR\neutralinojs.log"
  Delete "$INSTDIR\Desinstaller.exe"
  RMDir "$INSTDIR"
  DeleteRegKey HKCU "${UNINST_KEY}"
  DeleteRegKey HKCU "Software\MyVerse"
SectionEnd

Function un.FermerMyVerse
  nsExec::Exec 'taskkill /F /IM MyVerse.exe'
  nsExec::Exec 'taskkill /F /IM verselight-bridge.exe'
  Sleep 1000
FunctionEnd
