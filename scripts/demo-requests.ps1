<#
.SYNOPSIS
  Runs every Week 4 demo request against a running ApexPost.Api and prints status, key headers and body for each,
  so each block can be screenshotted. The full output is also saved to docs\screenshots\week4\requests-transcript.txt.
.EXAMPLE
  # API running: dotnet run --project .\ApexPost.Api --launch-profile https
  powershell -ExecutionPolicy Bypass -File .\scripts\demo-requests.ps1 -PartnerKey "<Partner:ApiKeys:0>" -Pause
.NOTES
  -Pause waits for Enter after each request (screenshot it, press Enter for the next).
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'https://localhost:7153',
    [Parameter(Mandatory)][string]$PartnerKey,
    [switch]$Pause
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$http = [System.Net.Http.HttpClient]::new()
$http.Timeout = [TimeSpan]::FromSeconds(30)

$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root 'docs\screenshots\week4'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Start-Transcript -Path (Join-Path $outDir 'requests-transcript.txt') -Force | Out-Null

$tn = "APX-$(Get-Random -Minimum 10000000 -Maximum 99999999)"
$tn2 = "APX-$(Get-Random -Minimum 10000000 -Maximum 99999999)"
$tnV1 = "APX-$(Get-Random -Minimum 10000000 -Maximum 99999999)"
$scanA = [guid]::NewGuid().ToString()
$scanB = [guid]::NewGuid().ToString()
$headersToShow = 'Location', 'api-supported-versions', 'api-deprecated-versions', 'WWW-Authenticate'

function Invoke-Step {
    param([string]$Title, [string]$Method, [string]$Path, [hashtable]$Headers = @{}, [string]$Body = $null, [string]$ContentType = 'application/json')

    Write-Host ''
    Write-Host "### $Title" -ForegroundColor Cyan
    Write-Host "$Method $Path"
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), "$BaseUrl$Path")
    foreach ($k in $Headers.Keys) { [void]$request.Headers.TryAddWithoutValidation($k, $Headers[$k]) }
    if ($null -ne $Body) { $request.Content = [System.Net.Http.StringContent]::new($Body, [System.Text.Encoding]::UTF8, $ContentType) }

    $response = $http.SendAsync($request).GetAwaiter().GetResult()
    $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $code = [int]$response.StatusCode
    $color = if ($code -lt 300) { 'Green' } elseif ($code -lt 500) { 'Yellow' } else { 'Red' }
    Write-Host "HTTP $code $($response.ReasonPhrase)" -ForegroundColor $color
    foreach ($h in $headersToShow) {
        $values = $null
        if ($response.Headers.TryGetValues($h, [ref]$values)) { Write-Host "${h}: $($values -join ', ')" }
    }
    if ($text) {
        try { Write-Host ($text | ConvertFrom-Json | ConvertTo-Json -Depth 8) } catch { Write-Host $text }
    }
    if ($Pause) { [void](Read-Host '(screenshot now, then press Enter)') }
    return $text
}

function Token([string]$courierId, [string]$role) {
    $json = Invoke-Step "Token: $role" 'POST' '/dev/token' -Body (@{ courierId = $courierId; role = $role } | ConvertTo-Json)
    return ($json | ConvertFrom-Json).accessToken
}

try {
    Write-Host "Tracking numbers this run: $tn, $tn2, $tnV1" -ForegroundColor DarkGray
    $courier = Token 'C-1001' 'Courier'
    $admin = Token 'A-1' 'Admin'
    $C = @{ Authorization = "Bearer $courier" }
    $A = @{ Authorization = "Bearer $admin" }

    $createBody = { param($n, $name, $city) (@{ trackingNumber = $n; recipientName = $name; recipientPhone = '+61 400 000 111'; address = '12 Example Street'; city = $city; weightKg = 2.5 } | ConvertTo-Json) }

    # ---- Part B ----
    Invoke-Step 'B1. Create a shipment -> 201 + Location' 'POST' '/api/v2/shipments' $C (& $createBody $tn 'Ada Lovelace' 'Melbourne') | Out-Null
    Invoke-Step 'B1b. Create, validation failure -> 400' 'POST' '/api/v2/shipments' $C '{ "trackingNumber": "BAD-1", "recipientName": "", "weightKg": 100 }' | Out-Null
    Invoke-Step 'B1c. Create again, duplicate -> 409' 'POST' '/api/v2/shipments' $C (& $createBody $tn 'Ada Lovelace' 'Melbourne') | Out-Null
    Invoke-Step 'B2. List shipments -> 200' 'GET' '/api/v2/shipments?page=1&pageSize=5&city=Melbourne' $C | Out-Null
    Invoke-Step 'B3b. Get unknown shipment -> 404' 'GET' '/api/v2/shipments/APX-00000000' $C | Out-Null
    Invoke-Step 'B5. Change status to PickedUp -> 200' 'PATCH' "/api/v2/shipments/$tn/status" $C '{ "status": "PickedUp" }' | Out-Null
    Invoke-Step 'B5b. Skip steps to Delivered -> 409' 'PATCH' "/api/v2/shipments/$tn/status" $C '{ "status": "Delivered" }' | Out-Null
    $scans = (@{ scans = @(
        @{ scanId = $scanA; scanType = 'Arrived'; location = 'Melbourne Depot'; scannedAtUtc = '2026-11-08T22:00:00Z' },
        @{ scanId = $scanB; scanType = 'Departed'; location = 'Melbourne Depot'; scannedAtUtc = '2026-11-08T23:00:00Z' }) } | ConvertTo-Json -Depth 5)
    Invoke-Step 'B6. Upload scans (first send) -> accepted 2' 'POST' "/api/v2/shipments/$tn/scans" $C $scans | Out-Null
    Invoke-Step 'B6. Upload the same scans again -> duplicatesSkipped 2' 'POST' "/api/v2/shipments/$tn/scans" $C $scans | Out-Null
    Invoke-Step 'B6b. Upload invalid scans -> 400' 'POST' "/api/v2/shipments/$tn/scans" $C ((@{ scans = @(
        @{ scanId = [guid]::NewGuid().ToString(); scanType = 'Delivered'; location = '12 Example Street'; scannedAtUtc = '2026-11-08T23:30:00Z' },
        @{ scanId = [guid]::NewGuid().ToString(); scanType = 'Arrived'; location = 'Melbourne Depot'; scannedAtUtc = '2099-01-01T00:00:00Z' }) } | ConvertTo-Json -Depth 5)) | Out-Null
    Invoke-Step 'B7. Scan history -> 200' 'GET' "/api/v2/shipments/$tn/scans" $C | Out-Null

    # ---- Part C ----
    Invoke-Step 'C9. v2 shipment: nested recipient, latestScan, string status; see api-supported-versions' 'GET' "/api/v2/shipments/$tn" $C | Out-Null
    Invoke-Step 'C10. v1 (DEPRECATED) shipment: flat fields, numeric status; see api-deprecated-versions' 'GET' "/api/v1/shipments/$tn" $C | Out-Null
    Invoke-Step 'C10c. v1 create -> 201, Location under /api/v1' 'POST' '/api/v1/shipments' $C (& $createBody $tnV1 'Grace Hopper' 'Sydney') | Out-Null
    Invoke-Step 'C11. Old unversioned route -> 404' 'GET' "/api/shipments/$tn" $C | Out-Null

    # ---- Part B delete rule (Admin) ----
    Invoke-Step 'B8. Create a second shipment (to delete)' 'POST' '/api/v2/shipments' $C (& $createBody $tn2 'Alan Turing' 'Perth') | Out-Null
    Invoke-Step 'B8. Delete it as Admin while still Created -> 204' 'DELETE' "/api/v2/shipments/$tn2" $A | Out-Null
    Invoke-Step 'B8b. Delete the picked-up shipment as Admin -> 409' 'DELETE' "/api/v2/shipments/$tn" $A | Out-Null

    # ---- Part D ----
    Invoke-Step 'D14. No token -> 401 + WWW-Authenticate: Bearer' 'GET' '/api/v2/shipments' | Out-Null
    Invoke-Step 'D14b. Invalid token -> 401' 'GET' '/api/v2/shipments' @{ Authorization = 'Bearer not-a-real-token' } | Out-Null
    Invoke-Step 'D15. Courier tries to delete -> 403 (Admin only)' 'DELETE' "/api/v2/shipments/$tn" $C | Out-Null
    $oneScan = { param($id, $courierId) $s = @{ scanId = $id; scanType = 'PickedUp'; location = 'Melbourne Depot'; scannedAtUtc = '2026-11-08T21:00:00Z' }; if ($courierId) { $s.courierId = $courierId }; (@{ scans = @($s) } | ConvertTo-Json -Depth 5) }
    Invoke-Step 'D15b. Admin tries to upload scans -> 403 (Courier only)' 'POST' "/api/v2/shipments/$tn/scans" $A (& $oneScan $scanA $null) | Out-Null
    Invoke-Step 'D15c. courierId in scan does not match the token -> 403' 'POST' "/api/v2/shipments/$tn/scans" $C (& $oneScan $scanB 'C-9999') | Out-Null
    Invoke-Step 'D15d. Courier uploads under own courierId -> 200' 'POST' "/api/v2/shipments/$tn/scans" $C (& $oneScan $scanB 'C-1001') | Out-Null
    Invoke-Step 'D16. Partner route, valid API key -> 200' 'GET' "/api/v2/partner/shipments/$tn" @{ 'X-Api-Key' = $PartnerKey } | Out-Null
    Invoke-Step 'D16b. Partner route, wrong API key -> 401' 'GET' "/api/v2/partner/shipments/$tn" @{ 'X-Api-Key' = 'wrong-key' } | Out-Null
    Invoke-Step 'D16c. Partner route, no API key -> 401' 'GET' "/api/v2/partner/shipments/$tn" | Out-Null
    Write-Host "`nDone. Transcript: $outDir\requests-transcript.txt" -ForegroundColor Green
}
finally {
    Stop-Transcript | Out-Null
    $http.Dispose()
}
