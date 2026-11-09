<#
.SYNOPSIS
  Generates the C# SDK (ApexPost.Client\Generated\ApexPostClient.g.cs) from contracts\v2.json with NSwag.
.DESCRIPTION
  Uses the NSwag.ConsoleCore tool pinned in .config\dotnet-tools.json and the settings in
  ApexPost.Client\nswag.json. Run scripts\export-contracts.ps1 first whenever the API changed.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\scripts\generate-client.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$contract = Join-Path $root 'contracts\v2.json'
if (-not (Test-Path $contract)) { throw "contracts\v2.json not found. Run scripts\export-contracts.ps1 first." }

Push-Location $root
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed.' }
    Push-Location (Join-Path $root 'ApexPost.Client')
    try {
        dotnet tool run nswag run nswag.json
        if ($LASTEXITCODE -ne 0) { throw 'NSwag failed.' }
    }
    finally { Pop-Location }
    Write-Host 'Generated ApexPost.Client\Generated\ApexPostClient.g.cs' -ForegroundColor Green
}
finally { Pop-Location }
