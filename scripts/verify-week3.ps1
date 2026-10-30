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
    & $action *>&1 | Tee-Object -FilePath $log
    if ($LASTEXITCODE -ne 0) { Write-Host "!! $name failed (exit $LASTEXITCODE). See $log" -ForegroundColor Red; $script:failed += $name }
}

$script:failed = @()
Step "01-build" { dotnet build ApexPost.slnx -c Debug -warnaserror }
Step "02-unit-tests" { dotnet test ApexPost.Tests --no-build --logger "trx;LogFileName=unit.trx" --results-directory $out }
Step "03-terminal-tests" { dotnet test ApexPost.CounterTerminal.Tests --no-build --logger "trx;LogFileName=terminal.trx" --results-directory $out }

$env:APEXPOST_TEST_SQL = "Server=$TestServer;Integrated Security=True;TrustServerCertificate=True"
Step "04-sql-integration-tests" { dotnet test ApexPost.IntegrationTests --no-build --logger "trx;LogFileName=integration.trx" --logger "console;verbosity=normal" --results-directory $out }
$evidence = Join-Path $repo "ApexPost.IntegrationTests\bin\Debug\net10.0\sql-evidence"
if (Test-Path $evidence) { Copy-Item -Recurse -Force $evidence (Join-Path $out "sql-evidence") }

if (-not $SkipPerf) {
    Step "05-performance" { powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "run-perf.ps1") -Server $PerfServer -Rows $Rows }
    Copy-Item -Recurse -Force (Join-Path $repo "database\perf\results") (Join-Path $out "perf-results")
}

"SQL Server: $TestServer (tests), $PerfServer (perf)" | Set-Content (Join-Path $out "environment.txt")
dotnet --info | Select-Object -First 12 | Add-Content (Join-Path $out "environment.txt")

if ($script:failed.Count -gt 0) {
    Write-Host "Failed steps: $($script:failed -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host "All steps passed. Logs and plans: $out" -ForegroundColor Green
