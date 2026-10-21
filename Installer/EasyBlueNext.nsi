Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "nsDialogs.nsh"
!ifndef APP_FILES
  !define APP_FILES "..\artifacts\win-x64"
!endif
!ifndef OUTPUT
  !define OUTPUT "..\..\EasyBlue-Next-0.3.5-Setup.exe"
!endif
Name "EasyBlue Next"
OutFile "${OUTPUT}"
InstallDir "$LOCALAPPDATA\Programs\EasyBlueNext"
InstallDirRegKey HKCU "Software\EasyBlueNext" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
BrandingText "EasyBlue Next"
VIProductVersion "0.3.5.0"
VIAddVersionKey "ProductName" "EasyBlue Next Setup"
VIAddVersionKey "FileVersion" "0.3.5"
VIAddVersionKey "FileDescription" "EasyBlue Next per-user installer"
!define MUI_ICON "../EasyBlue.Desktop/Assets/EasyBlue.ico"
!define MUI_UNICON "../EasyBlue.Desktop/Assets/EasyBlue.ico"
!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
Page custom OptionsPage OptionsLeave
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\EasyBlue.Desktop.exe"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Korean"
Var StartBox
Var DesktopBox
Var StartChecked
Var DesktopChecked
Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "64비트 Windows에서 설치할 수 있습니다."
    Abort
  ${EndIf}
  SetRegView 64
  SetShellVarContext current
  StrCpy $StartChecked ${BST_CHECKED}
  StrCpy $DesktopChecked ${BST_CHECKED}
  ClearErrors
  ReadRegDWORD $0 HKCU "Software\EasyBlueNext" "StartupEnabled"
  ${IfNot} ${Errors}
  ${AndIf} $0 == 0
    StrCpy $StartChecked ${BST_UNCHECKED}
  ${EndIf}
  ${If} $0 == 1
    StrCpy $StartChecked ${BST_CHECKED}
  ${EndIf}
  ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "EasyBlueNext"
  ${If} $0 != ""
    StrCpy $StartChecked ${BST_CHECKED}
  ${EndIf}
FunctionEnd
Function CheckRunning
  retry:
  nsExec::ExecToStack '"$PLUGINSDIR\stage\EasyBlue.Desktop.exe" --quit-existing "$INSTDIR\EasyBlue.Desktop.exe"'
  Pop $0
  Pop $1
  ${If} $0 == 3
    MessageBox MB_OKCANCEL|MB_ICONINFORMATION "이전 버전에는 업데이트 종료 기능이 없습니다. 동글 이름 저장 등 진행 중인 작업을 마친 뒤 확인을 누르면 이전 앱을 종료하고 업데이트합니다." IDCANCEL cancel
    nsExec::ExecToStack '"$PLUGINSDIR\stage\EasyBlue.Desktop.exe" --quit-existing "$INSTDIR\EasyBlue.Desktop.exe" --force-legacy'
    Pop $0
    Pop $1
  ${EndIf}
  ${If} $0 != 0
    MessageBox MB_RETRYCANCEL|MB_ICONINFORMATION "앱 종료를 기다리고 있습니다. 진행 중인 장치 작업이 끝난 뒤 다시 시도해 주세요." IDRETRY retry
    Goto cancel
  ${EndIf}
  System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, t "Local\EasyBlueNext.DeviceOwner") p.r0'
  ${If} $0 P<> 0
    System::Call 'kernel32::CloseHandle(p r0)'
    MessageBox MB_RETRYCANCEL|MB_ICONINFORMATION "다른 폴더의 EasyBlue Next가 실행 중입니다. 해당 앱을 종료한 뒤 다시 시도해 주세요." IDRETRY retry
    Goto cancel
  ${EndIf}
  Return
  cancel:
  Abort
FunctionEnd
Function OptionsPage
  nsDialogs::Create 1018
  Pop $0
  ${NSD_CreateLabel} 0 0 100% 30u "관리자 권한 없이 현재 사용자에게 설치합니다. 시작 메뉴와 앱 제거 항목이 추가됩니다."
  Pop $0
  ${NSD_CreateCheckbox} 0 40u 100% 12u "Windows 시작 시 트레이에서 실행"
  Pop $StartBox
  ${NSD_SetState} $StartBox $StartChecked
  ${NSD_CreateCheckbox} 0 65u 100% 12u "바탕 화면 바로 가기 만들기"
  Pop $DesktopBox
  ${NSD_SetState} $DesktopBox $DesktopChecked
  nsDialogs::Show
FunctionEnd
Function OptionsLeave
  ${NSD_GetState} $StartBox $StartChecked
  ${NSD_GetState} $DesktopBox $DesktopChecked
FunctionEnd
Section "EasyBlue Next"
  InitPluginsDir
  SetOutPath "$PLUGINSDIR\stage"
  File "${APP_FILES}/*.exe"
  File "${APP_FILES}/*.dll"
  File "${APP_FILES}/*.txt"
  File "${APP_FILES}/*.json"
  File "${APP_FILES}/*.md"
  Call CheckRunning
  SetOutPath "$INSTDIR"
  ClearErrors
  CopyFiles /SILENT "$PLUGINSDIR\stage\*.*" "$INSTDIR"
  IfErrors 0 +3
    MessageBox MB_ICONSTOP "앱 파일을 복사하지 못했습니다. 앱 종료 상태와 설치 폴더 권한을 확인해 주세요."
    Abort
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\EasyBlue Next"
  CreateShortCut "$SMPROGRAMS\EasyBlue Next\EasyBlue Next.lnk" "$INSTDIR\EasyBlue.Desktop.exe"
  CreateShortCut "$SMPROGRAMS\EasyBlue Next\제거.lnk" "$INSTDIR\Uninstall.exe"
  ${If} $DesktopChecked == ${BST_CHECKED}
    CreateShortCut "$DESKTOP\EasyBlue Next.lnk" "$INSTDIR\EasyBlue.Desktop.exe"
  ${EndIf}
  ${If} $StartChecked == ${BST_CHECKED}
    nsExec::ExecToStack '"$INSTDIR\EasyBlue.Desktop.exe" --startup-enable'
  ${Else}
    nsExec::ExecToStack '"$INSTDIR\EasyBlue.Desktop.exe" --startup-disable'
  ${EndIf}
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONEXCLAMATION "자동실행 설정을 완료하지 못했습니다. 앱의 일반 설정에서 자동실행 등록·복구를 눌러 주세요."
  ${EndIf}
  WriteRegStr HKCU "Software\EasyBlueNext" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\EasyBlueNext" "DisplayName" "EasyBlue Next"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\EasyBlueNext" "DisplayVersion" "0.3.5"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\EasyBlueNext" "Publisher" "EasyBlueNext contributors"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\EasyBlueNext" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\EasyBlueNext" "DisplayIcon" "$INSTDIR\EasyBlue.Desktop.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\EasyBlueNext" "InstallLocation" "$INSTDIR"
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\EasyBlueNext" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\EasyBlueNext" "NoRepair" 1
SectionEnd
Function un.onInit
  SetRegView 64
  SetShellVarContext current
FunctionEnd
Section "Uninstall"
  System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, t "Local\EasyBlueNext.DeviceOwner") p.r0'
  ${If} $0 P<> 0
    System::Call 'kernel32::CloseHandle(p r0)'
    MessageBox MB_ICONINFORMATION "트레이 메뉴에서 EasyBlue Next를 종료한 뒤 제거해 주세요."
    Abort
  ${EndIf}
  nsExec::ExecToStack '"$INSTDIR\EasyBlue.Desktop.exe" --startup-disable'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONEXCLAMATION "자동실행 해제에 실패했습니다. 앱의 일반 설정에서 자동실행을 해제한 뒤 제거해 주세요."
    Abort
  ${EndIf}
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "EasyBlueNext"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\EasyBlueNext"
  DeleteRegKey HKCU "Software\EasyBlueNext"
  Delete "$DESKTOP\EasyBlue Next.lnk"
  Delete "$SMPROGRAMS\EasyBlue Next\EasyBlue Next.lnk"
  Delete "$SMPROGRAMS\EasyBlue Next\제거.lnk"
  RMDir "$SMPROGRAMS\EasyBlue Next"
  Delete "$INSTDIR\EasyBlue.Desktop.exe"
  Delete "$INSTDIR\D3DCompiler_47_cor3.dll"
  Delete "$INSTDIR\PenImc_cor3.dll"
  Delete "$INSTDIR\PresentationNative_cor3.dll"
  Delete "$INSTDIR\vcruntime140_cor3.dll"
  Delete "$INSTDIR\wpfgfx_cor3.dll"
  Delete "$INSTDIR\README.txt"
  Delete "$INSTDIR\SHA256SUMS.txt"
  Delete "$INSTDIR\verification.json"
  Delete "$INSTDIR\verification-core.json"
  Delete "$INSTDIR\protocol-audit.md"
  Delete "$INSTDIR\original-settings.md"
  Delete "$INSTDIR\changes-0.3.0.md"
  Delete "$INSTDIR\changes-0.3.1.md"
  Delete "$INSTDIR\changes-0.3.2.md"
  Delete "$INSTDIR\changes-0.3.3.md"
  Delete "$INSTDIR\changes-0.3.4.md"
  Delete "$INSTDIR\changes-0.3.5.md"
  Delete "$INSTDIR\changes-0.2.3.md"
  Delete "$INSTDIR\dotnet-LICENSE.txt"
  Delete "$INSTDIR\dotnet-ThirdPartyNotices.txt"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  ; User preferences and dongle backups in LocalAppData/EasyBlueNext are retained.
SectionEnd
