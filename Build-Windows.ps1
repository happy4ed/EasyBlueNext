$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$checks = & dotnet run --project EasyBlue.Checks -c Release
if ($LASTEXITCODE -ne 0) { $checks | Write-Host; throw 'Core checks failed' }
$checks | Write-Host
$cases=@($checks | Where-Object { $_.StartsWith('PASS ') } | ForEach-Object { $_.Substring(5) })
@{method='Executed scripted core checks; physical hardware and GUI not verified';passed=$cases.Count;cases=$cases;windows_hardware_verified=$false} | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 verification-core.json
dotnet publish EasyBlue.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/win-x64
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
Copy-Item README.txt,verification-core.json artifacts/win-x64
Copy-Item Notes/0.2.2-protocol-audit.md artifacts/win-x64/protocol-audit.md
Copy-Item Notes/0.2.3-changes.md artifacts/win-x64/changes-0.2.3.md
Copy-Item Notes/0.3.0-changes.md artifacts/win-x64/changes-0.3.0.md
Copy-Item Notes/0.3.1-changes.md artifacts/win-x64/changes-0.3.1.md
Copy-Item Notes/Original-settings-inventory.md artifacts/win-x64/original-settings.md
