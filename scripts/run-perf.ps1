<#
.SYNOPSIS
  Builds a separate ApexPostPerf database, loads ~300k fictional transactions, and measures the five
  reporting workloads BEFORE and AFTER the reporting indexes. Captures STATISTICS IO/TIME and the
  actual execution plans (.sqlplan, open them in SSMS or Azure Data Studio).

.DESCRIPTION
  1. Drops and recreates the perf database (never touches ApexPostDb).
  2. Applies database/schema.sql (the idempotent migration script: schema, seed data, procedures).
  3. Runs database/perf/00-generate-volume.sql with @Rows.
  4. BEFORE: drops the reporting indexes, runs every workload (warm-up + 3 measured runs).
  5. AFTER : recreates the indexes, runs every workload again.
  6. Writes database/perf/results/{before,after}/*.sqlplan, *.txt and results/summary.md + summary.csv.
  All queries are sent with SqlParameters (values come from the "--@ Name Type Size Value" lines).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\run-perf.ps1
  powershell -ExecutionPolicy Bypass -File scripts\run-perf.ps1 -Server "(localdb)\MSSQLLocalDB" -Rows 100000
#>
[CmdletBinding()]
param(
    [string] $Server = ".",
    [string] $Database = "ApexPostPerf",
    [int] $Rows = 300000,
    [int] $MeasuredRuns = 3
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Data
$repo = Split-Path -Parent $PSScriptRoot
$perf = Join-Path $repo "database\perf"
$results = Join-Path $perf "results"
if ($Database -notmatch '^[A-Za-z][A-Za-z0-9_]{0,63}$') { throw "Database name must be letters, digits and underscores." }

function New-Connection([string] $catalog) {
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder["Data Source"] = $Server
    $builder["Initial Catalog"] = $catalog
    $builder["Integrated Security"] = $true
    $builder["TrustServerCertificate"] = $true
    $connection = New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
    $connection.Open()
    return $connection
}

function Invoke-Batch($connection, [string] $sql, [hashtable] $parameters = @{}) {
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 0
    $command.CommandText = $sql
    foreach ($key in $parameters.Keys) { [void] $command.Parameters.AddWithValue("@$key", $parameters[$key]) }
    [void] $command.ExecuteNonQuery()
}

function Invoke-ScriptFile($connection, [string] $path) {
    $text = Get-Content -Raw -Path $path
    foreach ($batch in [regex]::Split($text, '^\s*GO\s*$', 'Multiline')) {
        if ($batch.Trim().Length -gt 0) { Invoke-Batch $connection $batch }
    }
}

function Get-WorkloadParameters([string] $text) {
    # Lines like: --@ BranchCode varchar 10 BR-001
    $list = @()
    foreach ($line in ($text -split "`n")) {
        if ($line -match '^--@\s+(\w+)\s+(\w+)\s+(\d+)\s+(\S+)\s*$') {
            $list += [pscustomobject]@{ Name = $Matches[1]; Type = $Matches[2]; Size = [int]$Matches[3]; Value = $Matches[4] }
        }
    }
    return $list
}

function New-SqlParameter($p) {
    switch ($p.Type) {
        "varchar"   { $param = New-Object System.Data.SqlClient.SqlParameter("@$($p.Name)", [System.Data.SqlDbType]::VarChar, $p.Size); $param.Value = $p.Value }
        "int"       { $param = New-Object System.Data.SqlClient.SqlParameter("@$($p.Name)", [System.Data.SqlDbType]::Int); $param.Value = [int]$p.Value }
        "date"      { $param = New-Object System.Data.SqlClient.SqlParameter("@$($p.Name)", [System.Data.SqlDbType]::Date); $param.Value = [datetime]::Parse($p.Value, [Globalization.CultureInfo]::InvariantCulture) }
        "datetime2" { $param = New-Object System.Data.SqlClient.SqlParameter("@$($p.Name)", [System.Data.SqlDbType]::DateTime2); $param.Value = [datetime]::Parse($p.Value, [Globalization.CultureInfo]::InvariantCulture) }
        default     { throw "Unsupported parameter type $($p.Type)" }
    }
    return $param
}

function Measure-Workload($connection, [System.IO.FileInfo] $file, [string] $phase) {
    $text = Get-Content -Raw -Path $file.FullName
    $parameters = Get-WorkloadParameters $text
    $phaseFolder = Join-Path $results $phase
    $messages = New-Object System.Text.StringBuilder
    $handler = [System.Data.SqlClient.SqlInfoMessageEventHandler] ({ param($s, $e) [void] $messages.AppendLine($e.Message) }.GetNewClosure())
    $connection.add_InfoMessage($handler)
    $connection.FireInfoMessageEventOnUserErrors = $false
    $runs = @()
    try {
        for ($run = 0; $run -le $MeasuredRuns; $run++) {   # run 0 = warm-up (compiles, loads pages)
            [void] $messages.Clear()
            $capturePlan = ($run -eq $MeasuredRuns)
            Invoke-Batch $connection ("SET STATISTICS IO ON; SET STATISTICS TIME ON;" + $(if ($capturePlan) { " SET STATISTICS XML ON;" } else { "" }))
            $command = $connection.CreateCommand()
            $command.CommandTimeout = 0
            $command.CommandText = $text
            foreach ($p in $parameters) { [void] $command.Parameters.Add((New-SqlParameter $p)) }
            $rowsReturned = 0
            $reader = $command.ExecuteReader()
            do {
                $isPlan = ($reader.FieldCount -eq 1 -and $reader.GetName(0) -like "*Showplan*")
                while ($reader.Read()) {
                    if ($isPlan) { Set-Content -Path (Join-Path $phaseFolder ($file.BaseName + ".sqlplan")) -Value $reader.GetString(0) -Encoding UTF8 }
                    else { $rowsReturned++ }
                }
            } while ($reader.NextResult())
            $reader.Close()
            Invoke-Batch $connection "SET STATISTICS XML OFF; SET STATISTICS IO OFF; SET STATISTICS TIME OFF;"

            $log = $messages.ToString()
            $reads = 0; foreach ($m in [regex]::Matches($log, 'logical reads (\d+)')) { $reads += [int]$m.Groups[1].Value }
            $cpu = 0; $elapsed = 0
            foreach ($m in [regex]::Matches($log, 'SQL Server Execution Times:\s*CPU time = (\d+) ms,\s*elapsed time = (\d+) ms')) {
                $cpu += [int]$m.Groups[1].Value; $elapsed += [int]$m.Groups[2].Value
            }
            if ($run -gt 0) { $runs += [pscustomobject]@{ Reads = $reads; Cpu = $cpu; Elapsed = $elapsed; Rows = $rowsReturned } }
            if ($capturePlan) { Set-Content -Path (Join-Path $phaseFolder ($file.BaseName + ".txt")) -Value $log -Encoding UTF8 }
        }
    }
    finally {
        $connection.remove_InfoMessage($handler)
    }

    $sortedCpu = $runs | Sort-Object Cpu
    $sortedElapsed = $runs | Sort-Object Elapsed
    $middle = [int][math]::Floor($runs.Count / 2)
    return [pscustomobject]@{
        Phase = $phase; Workload = $file.BaseName
        LogicalReads = $runs[-1].Reads; CpuMs = $sortedCpu[$middle].Cpu; ElapsedMs = $sortedElapsed[$middle].Elapsed; Rows = $runs[-1].Rows
    }
}

Write-Host "== ApexPost performance run: server '$Server', database '$Database', $Rows transactions"
New-Item -ItemType Directory -Force -Path (Join-Path $results "before"), (Join-Path $results "after") | Out-Null

$master = New-Connection "master"
$dropCreate = @"
IF DB_ID(@name) IS NOT NULL
BEGIN
    DECLARE @drop nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(@name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(@name) + N';';
    EXEC sys.sp_executesql @drop;
END
DECLARE @create nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@name) + N';';
EXEC sys.sp_executesql @create;
"@
Invoke-Batch $master $dropCreate @{ name = $Database }
$version = $master.ServerVersion
$master.Close()

$db = New-Connection $Database
Write-Host "-- applying database/schema.sql (migrations, seed, procedures)"
Invoke-ScriptFile $db (Join-Path $repo "database\schema.sql")
Write-Host "-- generating volume ($Rows transactions; takes a minute)"
$sw = [Diagnostics.Stopwatch]::StartNew()
Invoke-Batch $db (Get-Content -Raw (Join-Path $perf "00-generate-volume.sql")) @{ Rows = $Rows }
Write-Host ("   done in {0:n0} s" -f $sw.Elapsed.TotalSeconds)

$counts = $db.CreateCommand()
$counts.CommandText = "SELECT (SELECT COUNT(*) FROM dbo.CounterTransactions), (SELECT COUNT(*) FROM dbo.TransactionLines), (SELECT COUNT(*) FROM dbo.Shipments), (SELECT COUNT(*) FROM dbo.MoneyOrders), (SELECT COUNT(*) FROM dbo.BranchStock)"
$r = $counts.ExecuteReader(); [void] $r.Read()
$volume = "CounterTransactions $($r.GetInt32(0)), TransactionLines $($r.GetInt32(1)), Shipments $($r.GetInt32(2)), MoneyOrders $($r.GetInt32(3)), BranchStock $($r.GetInt32(4))"
$r.Close()
Write-Host "   $volume"

$workloads = Get-ChildItem (Join-Path $perf "workloads") -Filter "*.sql" | Sort-Object Name
$all = @()

Write-Host "-- BEFORE: reporting indexes dropped"
Invoke-ScriptFile $db (Join-Path $perf "10-drop-reporting-indexes.sql")
Invoke-Batch $db "EXEC sys.sp_updatestats;"
foreach ($w in $workloads) { $m = Measure-Workload $db $w "before"; $all += $m; Write-Host ("   {0,-34} reads {1,9:n0}  cpu {2,6} ms  elapsed {3,6} ms" -f $m.Workload, $m.LogicalReads, $m.CpuMs, $m.ElapsedMs) }

Write-Host "-- AFTER: reporting indexes created"
Invoke-ScriptFile $db (Join-Path $perf "20-create-reporting-indexes.sql")
Invoke-Batch $db "EXEC sys.sp_updatestats;"
foreach ($w in $workloads) { $m = Measure-Workload $db $w "after"; $all += $m; Write-Host ("   {0,-34} reads {1,9:n0}  cpu {2,6} ms  elapsed {3,6} ms" -f $m.Workload, $m.LogicalReads, $m.CpuMs, $m.ElapsedMs) }

# Storage cost of each reporting index (8 KB pages actually used).
$sizes = $db.CreateCommand()
$sizes.CommandText = @"
SELECT OBJECT_NAME(i.object_id) AS TableName, i.name AS IndexName, SUM(s.used_page_count) * 8 AS UsedKb, SUM(s.row_count) AS IndexRows
FROM sys.indexes AS i
JOIN sys.dm_db_partition_stats AS s ON s.object_id = i.object_id AND s.index_id = i.index_id
WHERE i.name IN ('IX_BranchStock_Branch_Covering','IX_CounterTransactions_Branch_BusinessDate','IX_CounterTransactions_ClosedAt_Paid',
                 'IX_Shipments_Zone_Level_QuotedAt','IX_MoneyOrders_Branch_RequestedAt_Audited')
   OR (i.index_id = 1 AND OBJECT_NAME(i.object_id) IN ('CounterTransactions','Shipments','MoneyOrders','BranchStock'))
GROUP BY i.object_id, i.name, i.index_id
ORDER BY TableName, i.index_id
"@
$sizeRows = @()
$r = $sizes.ExecuteReader()
while ($r.Read()) { $sizeRows += [pscustomobject]@{ Table = $r.GetString(0); Index = $r.GetString(1); UsedKb = $r.GetInt64(2); Rows = $r.GetInt64(3) } }
$r.Close()
$db.Close()

$all | Export-Csv -NoTypeInformation -Path (Join-Path $results "summary.csv")
$lines = @(
    "# ApexPost performance run",
    "",
    "- Date: $((Get-Date).ToString('yyyy-MM-dd HH:mm'))",
    "- Server: SQL Server $version ('$Server'), database $Database",
    "- Volume: $volume",
    "- Each workload: 1 warm-up + $MeasuredRuns measured runs; CPU/elapsed are medians; logical reads from the last run.",
    "",
    "| Workload | Reads before | Reads after | CPU ms before | CPU ms after | Elapsed ms before | Elapsed ms after | Rows |",
    "|---|---:|---:|---:|---:|---:|---:|---:|"
)
foreach ($w in $workloads) {
    $b = $all | Where-Object { $_.Phase -eq "before" -and $_.Workload -eq $w.BaseName }
    $a = $all | Where-Object { $_.Phase -eq "after" -and $_.Workload -eq $w.BaseName }
    $lines += "| $($w.BaseName) | $($b.LogicalReads) | $($a.LogicalReads) | $($b.CpuMs) | $($a.CpuMs) | $($b.ElapsedMs) | $($a.ElapsedMs) | $($a.Rows) |"
}
$lines += ""
$lines += "W6/W7 are write workloads (insert 20,000 rows, then roll back): the 'after' minus 'before' difference is the index maintenance cost."
$lines += ""
$lines += "## Index storage (after)"
$lines += ""
$lines += "| Table | Index | Used KB | Rows |"
$lines += "|---|---|---:|---:|"
foreach ($row in $sizeRows) { $lines += "| $($row.Table) | $($row.Index) | $($row.UsedKb) | $($row.Rows) |" }
Set-Content -Path (Join-Path $results "summary.md") -Value $lines -Encoding UTF8
Write-Host ""
Write-Host "Results: $results (summary.md, summary.csv, before\*.sqlplan, after\*.sqlplan)"
