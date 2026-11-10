; Installateur de MyChurch 4 (logiciel Windows natif), compilé sur GitHub Actions.
; Installation par utilisateur (pas besoin d'être administrateur), au même endroit que la 3.x : la mise à jour la remplace.
; Chants, réglages, Bibles importées et téléphones jumelés (%APPDATA%\VerseLight) sont gardés.
Unicode true
!include "MUI2.nsh"

!ifndef VERSION
  !define VERSION "1.2.0-beta"
!endif
!define APP "MyChurch"
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\MyVerse"

Name "${APP}"
OutFile "MyChurch-Setup-${VERSION}.exe"
InstallDir "$LOCALAPPDATA\Programs\MyVerse"
InstallDirRegKey HKCU "Software\MyVerse" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
!define MUI_ICON "myverse.ico"
!define MUI_UNICON "myverse.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\MyChurch.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Lancer MyChurch"

VIProductVersion "1.2.0.0"
VIAddVersionKey /LANG=1036 "ProductName" "MyChurch"
VIAddVersionKey /LANG=1036 "FileDescription" "Installateur de MyChurch"
VIAddVersionKey /LANG=1036 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1036 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=1036 "CompanyName" "MEPC Montréal"
VIAddVersionKey /LANG=1036 "LegalCopyright" "MEPC Montréal"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "French"

Var ANCIENNE3

; mise à jour automatique (lancée par MyVerse avec /S) : on rouvre MyVerse à la fin
Function .onInstSuccess
  IfSilent 0 +2
  Exec '"$INSTDIR\MyChurch.exe"'
FunctionEnd

Function FermerMyVerse
  nsExec::Exec 'taskkill /F /IM MyVerse.exe'
  nsExec::Exec 'taskkill /F /IM MyChurch.exe'
  nsExec::Exec 'taskkill /F /IM verselight-bridge.exe'
  Sleep 1200
FunctionEnd

; Écarte un fichier du dossier d'installation (renommé en .old) pour que sa nouvelle version puisse s'écrire,
; même si un autre logiciel le garde ouvert. Si un ancien .old est encore utilisé, un autre nom libre est pris.
Function LibererFichier
  Exch $R0
  Push $R1
  IfFileExists "$INSTDIR\$R0" 0 liberer_fin
  ClearErrors
  Rename "$INSTDIR\$R0" "$INSTDIR\$R0.old"
  IfErrors 0 liberer_fin
  GetTempFileName $R1 "$INSTDIR"
  Delete "$R1"
  ClearErrors
  Rename "$INSTDIR\$R0" "$R1.old"
  liberer_fin:
  ClearErrors
  Pop $R1
  Pop $R0
FunctionEnd

Section "MyVerse"
  Call FermerMyVerse
  Delete "$INSTDIR\MyVerse.exe"
  Delete "$INSTDIR\MyVerse.dll"
  Delete "$INSTDIR\MyVerse.deps.json"
  Delete "$INSTDIR\MyVerse.runtimeconfig.json"
  Delete "$DESKTOP\MyVerse.lnk"
  Delete "$SMPROGRAMS\MyVerse.lnk"

  ; MyVerse 3.x (HTML / Neutralino) : ses fichiers sont retirés, ses données restent dans %APPDATA%
  StrCpy $ANCIENNE3 "0"
  IfFileExists "$INSTDIR\resources.neu" 0 +2
    StrCpy $ANCIENNE3 "1"
  Delete "$INSTDIR\resources.neu"
  Delete "$INSTDIR\neutralinojs.log"
  Delete "$INSTDIR\WebView2Loader.dll"
  RMDir /r "$INSTDIR\extensions"
  RMDir /r "$INSTDIR\.tmp"
  RMDir /r "$INSTDIR\.storage"

  ; MyVirtualCam : quand un logiciel (OBS, Zoom…) utilise la caméra virtuelle, Windows garde ses deux fichiers ouverts
  ; et refuse de les remplacer (« Erreur lors de l'ouverture du fichier en écriture »). Un fichier ouvert peut par contre
  ; être renommé : les anciens passent en .old, les nouveaux s'écrivent à leur place, et les .old sont supprimés
  ; tout de suite s'ils sont libres, sinon au prochain redémarrage ou à la prochaine installation.
  Delete "$INSTDIR\MyVirtualCam.dll.old"
  Delete "$INSTDIR\MyVirtualCam32.dll.old"
  Delete "$INSTDIR\ns*.tmp.old"
  Push "MyVirtualCam.dll"
  Call LibererFichier
  Push "MyVirtualCam32.dll"
  Call LibererFichier

  SetOutPath "$INSTDIR"
  File /r "app\*.*"

  Delete /REBOOTOK "$INSTDIR\MyVirtualCam.dll.old"
  Delete /REBOOTOK "$INSTDIR\MyVirtualCam32.dll.old"
  Delete "$INSTDIR\ns*.tmp.old"
  ; aucun redémarrage n'est demandé : le logiciel qui utilise la caméra garde l'ancienne version jusqu'à sa fermeture
  SetRebootFlag false

  ; pare-feu Windows (une seule autorisation) : pour que le téléphone puisse joindre le PC, même sur un Wi-Fi « public ».
  ; Mise à jour silencieuse : seulement en venant de la 3.x (le programme qui écoute a changé).
  IfSilent 0 parefeu
  StrCmp $ANCIENNE3 "1" parefeu fin_parefeu
  parefeu:
    ExecShellWait "runas" "$INSTDIR\MyChurch.exe" "--parefeu" SW_HIDE
  fin_parefeu:

  ; Register the persistent virtual camera. Windows asks for elevation once.
  ExecShellWait "runas" "$INSTDIR\MyChurch.exe" "--virtualcam-install" SW_HIDE

  CreateShortcut "$DESKTOP\MyChurch.lnk" "$INSTDIR\MyChurch.exe" "" "$INSTDIR\MyChurch.exe" 0
  CreateShortcut "$SMPROGRAMS\MyChurch.lnk" "$INSTDIR\MyChurch.exe" "" "$INSTDIR\MyChurch.exe" 0

  WriteUninstaller "$INSTDIR\Desinstaller.exe"
  WriteRegStr HKCU "Software\MyVerse" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayName" "MyChurch"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINST_KEY}" "Publisher" "MEPC Montréal"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\MyChurch.exe"
  WriteRegStr HKCU "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "UninstallString" '"$INSTDIR\Desinstaller.exe"'
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoRepair" 1
SectionEnd

; Désinstaller : retire le programme, garde chants, réglages et téléphones jumelés (%APPDATA%\VerseLight)
Section "Uninstall"
  Call un.FermerMyVerse
  ExecShellWait "runas" "$INSTDIR\MyChurch.exe" "--virtualcam-uninstall" SW_HIDE
  Delete "$DESKTOP\MyChurch.lnk"
  Delete "$SMPROGRAMS\MyChurch.lnk"
  ; par sécurité : on ne vide le dossier que si c'est bien celui de MyVerse
  IfFileExists "$INSTDIR\MyChurch.exe" 0 +2
    RMDir /r "$INSTDIR"
  RMDir "$INSTDIR"
  DeleteRegKey HKCU "${UNINST_KEY}"
  DeleteRegKey HKCU "Software\MyVerse"
SectionEnd

Function un.FermerMyVerse
  nsExec::Exec 'taskkill /F /IM MyVerse.exe'
  nsExec::Exec 'taskkill /F /IM MyChurch.exe'
  Sleep 1000
FunctionEnd
