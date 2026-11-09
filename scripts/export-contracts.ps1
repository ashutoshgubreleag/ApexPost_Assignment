<#
.SYNOPSIS
  Exports the OpenAPI contract of every API version to contracts\v1.json and contracts\v2.json.
.DESCRIPTION
  Builds ApexPost.Api, starts it in Development on a private HTTP port, downloads /openapi/v1.json and
  /openapi/v2.json, and stops it. No database is touched (the connection string is a harmless placeholder
  unless you already set one). Commit the two files: they are the reviewed contract, and
  scripts\generate-client.ps1 builds the SDK from contracts\v2.json.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\scripts\export-contracts.ps1
#>
[CmdletBinding()]
param(
    [int]$Port = 5299
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'ApexPost.Api'
$contracts = Join-Path $root 'contracts'
New-Item -ItemType Directory -Force -Path $contracts | Out-Null

Write-Host '== Building ApexPost.Api' -ForegroundColor Cyan
dotnet build $project -c Debug --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = "http://localhost:$Port"
if (-not $env:ConnectionStrings__ApexPostDb) {
    $env:ConnectionStrings__ApexPostDb = 'Server=.;Database=ApexPostDb;Trusted_Connection=True;TrustServerCertificate=True'
}

Write-Host "== Starting the API on http://localhost:$Port" -ForegroundColor Cyan
$process = Start-Process dotnet -ArgumentList @('run', '--project', $project, '--no-build', '--no-launch-profile') -PassThru -WindowStyle Hidden
try {
    $ready = $false
    for ($i = 0; $i -lt 60 -and -not $ready; $i++) {
        Start-Sleep -Milliseconds 500
        try {
            Invoke-WebRequest "http://localhost:$Port/openapi/v2.json" -UseBasicParsing -TimeoutSec 2 | Out-Null
            $ready = $true
        } catch { }
    }
    if (-not $ready) { throw 'The API did not start within 30 seconds.' }

    foreach ($version in 'v1', 'v2') {
        $target = Join-Path $contracts "$version.json"
        Invoke-WebRequest "http://localhost:$Port/openapi/$version.json" -UseBasicParsing -OutFile $target
        Write-Host "   wrote $target" -ForegroundColor Green
    }
}
finally {
    if ($process -and -not $process.HasExited) {
        # dotnet run starts the app as a child process: stop the whole tree
        & taskkill /PID $process.Id /T /F 2>$null | Out-Null
    }
}
