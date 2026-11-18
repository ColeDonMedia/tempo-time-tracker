<#
  CI helper: signs one file with the code-signing certificate from the SIGNING_CERT_BASE64 / SIGNING_CERT_PASSWORD
  environment variables. The .pfx exists on disk only for the duration of this script.
    ./scripts/sign.ps1 <file> <description>
#>
param([Parameter(Mandatory)] [string]$File, [string]$Description = "Tempo")
$ErrorActionPreference = 'Stop'
$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $signtool) { throw "signtool.exe not found" }
$pfx = Join-Path $env:RUNNER_TEMP ("cert-" + [Guid]::NewGuid().ToString('N') + ".pfx")
try {
    [IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:SIGNING_CERT_BASE64))
    & $signtool.FullName sign /f $pfx /p $env:SIGNING_CERT_PASSWORD /fd sha256 /tr http://timestamp.digicert.com /td sha256 /d $Description $File
    if ($LASTEXITCODE -ne 0) { throw "signtool failed ($LASTEXITCODE)" }
}
finally {
    Remove-Item $pfx -Force -ErrorAction SilentlyContinue
}
