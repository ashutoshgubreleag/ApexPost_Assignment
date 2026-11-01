<#
.SYNOPSIS
  One-shot Week 3 verification on a Windows PC with SQL Server: build, unit tests, Couchbase Lite
  terminal tests, SQL Server integration tests, then the before/after performance run.
  Everything is logged to artifacts\verify\ so the results can be reviewed and pasted into PERFORMANCE.md.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\verify-week3.ps1
  powershell -ExecutionPolicy Bypass -File scripts\verify-week3.ps1 -TestServer "." -PerfServer "." -SkipPerf
#>
[CmdletBinding()]
param(
    [string] $TestServer = "(localdb)\MSSQLLocalDB",
    [string] $PerfServer = ".",
    [int] $Rows = 300000,
    [switch] $SkipPerf
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$out = Join-Path $repo "artifacts\verify"
New-Item -ItemType Directory -Force -Path $out | Out-Null
Set-Location $repo

function Step([string] $name, [scriptblock] $action) {
    $log = Join-Path $out ($name + ".log")
    Write-Host "== $name" -ForegroundColor Cyan
    # Native tools write progress to stderr; in Windows PowerShell that must not stop the script.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $global:LASTEXITCODE = 0
    try {
        & $action *>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath $log | Out-Host
        $code = $LASTEXITCODE
    }
    catch {
        "$_" | Tee-Object -FilePath $log -Append | Out-Host
        $code = 1
    }
    finally {
        $ErrorActionPreference = $previous
    }
    if ($code -ne 0) { Write-Host "!! $name failed (exit $code). See $log" -ForegroundColor Red; $script:failed += $name }
    return ($code -eq 0)
}

$script:failed = @()
if (-not (Step "01-build" { dotnet build ApexPost.slnx -c Debug })) {
    Write-Host "Build failed: the remaining steps need a successful build. See $out\01-build.log" -ForegroundColor Red
    exit 1
}
[void] (Step "02-unit-tests" { dotnet test ApexPost.Tests --no-build --logger "trx;LogFileName=unit.trx" --results-directory $out })
[void] (Step "03-terminal-tests" { dotnet test ApexPost.CounterTerminal.Tests --no-build --logger "trx;LogFileName=terminal.trx" --results-directory $out })

$env:APEXPOST_TEST_SQL = "Server=$TestServer;Integrated Security=True;TrustServerCertificate=True"
[void] (Step "04-sql-integration-tests" { dotnet test ApexPost.IntegrationTests --no-build --logger "trx;LogFileName=integration.trx" --logger "console;verbosity=normal" --results-directory $out })
$evidence = Join-Path $repo "ApexPost.IntegrationTests\bin\Debug\net10.0\sql-evidence"
if (Test-Path $evidence) { Copy-Item -Recurse -Force $evidence (Join-Path $out "sql-evidence") }

if (-not $SkipPerf) {
    [void] (Step "05-performance" { powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "run-perf.ps1") -Server $PerfServer -Rows $Rows })
    $perfResults = Join-Path $repo "database\perf\results"
    if (Test-Path $perfResults) { Copy-Item -Recurse -Force $perfResults (Join-Path $out "perf-results") }
}

"SQL Server: $TestServer (tests), $PerfServer (perf)" | Set-Content (Join-Path $out "environment.txt")
dotnet --info | Select-Object -First 12 | Add-Content (Join-Path $out "environment.txt")

if ($script:failed.Count -gt 0) {
    Write-Host "Failed steps: $($script:failed -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host "All steps passed. Logs and plans: $out" -ForegroundColor Green
