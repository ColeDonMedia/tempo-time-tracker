<#
  Tempo's test runner: builds the app, then runs the built-in self-test (SelfTest.cs) against a
  throw-away data folder. Exit code 0 = all green.

    powershell -File scripts/test.ps1            # build + self-test
    powershell -File scripts/test.ps1 -Full      # + Release build, warnings-as-errors and single-file publish (pre-ship sweep)
    ... -Hook                                    # used by the Claude Code PostToolUse hook: reads the hook JSON
                                                 # on stdin and only runs when a .cs / .xaml / .csproj file changed
#>
param([switch]$Hook, [switch]$Full)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

if ($Hook) {
    $raw = [Console]::In.ReadToEnd()
    try { $path = ($raw | ConvertFrom-Json).tool_input.file_path } catch { $path = '' }
    if ($path -notmatch '\.(cs|xaml|csproj)$') { exit 0 }
}

function Step($name, [scriptblock]$body) {
    Write-Host "== $name"
    $out = & $body 2>&1
    if ($LASTEXITCODE -ne 0) {
        $out | Select-String -Pattern 'error|FAIL' | Select-Object -First 25 | ForEach-Object { Write-Host $_ }
        Write-Host "RED: $name failed"
        exit 1
    }
    return $out
}

Step 'build (Debug)' { dotnet build Tempo.csproj -c Debug -nologo -v q } | Out-Null

$data = Join-Path ([IO.Path]::GetTempPath()) ("tempo-test-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $data | Out-Null
$result = Join-Path $data 'selftest.txt'
$env:TEMPO_DATA_DIR = $data
$exe = Join-Path $root 'bin\Debug\net8.0-windows\Tempo.exe'
$p = Start-Process $exe -ArgumentList '--selftest', "`"$result`"" -PassThru -Wait
Remove-Item Env:TEMPO_DATA_DIR

if (-not (Test-Path $result)) { Write-Host 'RED: self-test produced no result file'; exit 1 }
$lines = Get-Content $result
$failed = $lines | Where-Object { $_ -like 'FAIL*' }
$passed = ($lines | Where-Object { $_ -like 'PASS*' }).Count
$failed | ForEach-Object { Write-Host $_ }
Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue
if ($failed) { Write-Host "RED: $($failed.Count) failed, $passed passed"; exit 1 }
Write-Host "GREEN: $passed self-test checks passed"

# UI test: drives every page, tab and main dialog in all languages (skipped by the edit hook to keep it fast)
if (-not $Hook) {
    $ui = Join-Path ([IO.Path]::GetTempPath()) ("tempo-uitest-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory $ui | Out-Null
    $env:TEMPO_DATA_DIR = $ui
    $uiResult = Join-Path $ui 'uitest.txt'
    Start-Process $exe -ArgumentList '--demo', '--uitest', "`"$uiResult`"" -Wait | Out-Null
    Remove-Item Env:TEMPO_DATA_DIR
    if (-not (Test-Path $uiResult)) { Write-Host 'RED: UI test produced no result file'; exit 1 }
    $uiLines = Get-Content $uiResult
    $uiFailed = $uiLines | Where-Object { $_ -like 'FAIL*' }
    $uiPassed = ($uiLines | Where-Object { $_ -like 'PASS*' }).Count
    $uiFailed | Select-Object -First 25 | ForEach-Object { Write-Host $_ }
    Remove-Item $ui -Recurse -Force -ErrorAction SilentlyContinue
    if ($uiFailed) { Write-Host "RED: UI test $($uiFailed.Count) failed, $uiPassed passed"; exit 1 }
    Write-Host "GREEN: $uiPassed UI checks passed"
}

if ($Full) {
    Step 'build (Release, warnings as errors)' { dotnet build Tempo.csproj -c Release -nologo -v q -warnaserror } | Out-Null
    Step 'publish single-file exe' { dotnet publish Tempo.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish -nologo -v q } | Out-Null
    Write-Host 'GREEN: full sweep passed'
}
exit 0
