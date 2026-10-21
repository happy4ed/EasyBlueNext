$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$publish=Join-Path $root 'artifacts/win-x64'
if(-not(Test-Path (Join-Path $publish 'EasyBlue.Desktop.exe'))){throw 'Build-Windows.ps1을 먼저 실행하세요.'}
& makensis "/DAPP_FILES=$publish" "/DOUTPUT=$(Join-Path (Split-Path $root -Parent) 'EasyBlue-Next-0.3.1-Setup.exe')" (Join-Path $PSScriptRoot 'EasyBlueNext.nsi')
if($LASTEXITCODE -ne 0){throw 'NSIS build failed'}
