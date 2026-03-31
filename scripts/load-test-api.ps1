param(
    [string]$ApiBaseUrl = "http://localhost:5001",
    [int]$TotalRequests = 50,
    [int]$Concurrency = 10
)

$ErrorActionPreference = "Stop"

function Get-Percentile {
    param(
        [double[]]$Values,
        [double]$Percentile
    )

    if ($Values.Count -eq 0) {
        return 0
    }

    $sorted = $Values | Sort-Object
    $index = [Math]::Ceiling($sorted.Count * $Percentile) - 1
    if ($index -lt 0) { $index = 0 }
    return $sorted[$index]
}

function New-LoadRequestBody {
    param([int]$Index)

    $messageId = "load-$Index-$(Get-Date -Format 'yyyyMMddHHmmssfff')"
    $xml = @"
<AllocationSeries>
  <DocumentID>LOAD-$Index</DocumentID>
  <EAN>8712345678901</EAN>
  <Quantity>100</Quantity>
  <StartDateTime>2026-03-20T10:00:00Z</StartDateTime>
  <EndDateTime>2026-03-20T11:00:00Z</EndDateTime>
</AllocationSeries>
"@

    return @{
        messageId = $messageId
        correlationId = "corr-$messageId"
        xmlContent = $xml
    } | ConvertTo-Json -Depth 5
}

$jobScript = {
    param($ApiBaseUrl, $Index)

    $headers = @{ "Content-Type" = "application/json"; "X-Correlation-ID" = "load-corr-$Index" }
    $body = @{
        messageId = "load-$Index"
        correlationId = "load-corr-$Index"
        xmlContent = "<AllocationSeries><DocumentID>LOAD-$Index</DocumentID><EAN>8712345678901</EAN><Quantity>100</Quantity><StartDateTime>2026-03-20T10:00:00Z</StartDateTime><EndDateTime>2026-03-20T11:00:00Z</EndDateTime></AllocationSeries>"
    } | ConvertTo-Json -Depth 5

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $response = Invoke-RestMethod -Method Post -Uri "$ApiBaseUrl/api/messages" -Headers $headers -Body $body
        $sw.Stop()

        [pscustomobject]@{
            Success = $true
            Status = $response.status
            ErrorCount = $response.errorCount
            DurationMs = [Math]::Round($sw.Elapsed.TotalMilliseconds, 2)
        }
    }
    catch {
        $sw.Stop()
        [pscustomobject]@{
            Success = $false
            Status = "FailedRequest"
            ErrorCount = -1
            DurationMs = [Math]::Round($sw.Elapsed.TotalMilliseconds, 2)
            Error = $_.Exception.Message
        }
    }
}

$allResults = @()

for ($offset = 0; $offset -lt $TotalRequests; $offset += $Concurrency) {
    $jobs = @()

    for ($index = $offset + 1; $index -le [Math]::Min($offset + $Concurrency, $TotalRequests); $index++) {
        $jobs += Start-Job -ScriptBlock $jobScript -ArgumentList $ApiBaseUrl, $index
    }

    $jobs | Wait-Job | Out-Null
    $allResults += $jobs | Receive-Job
    $jobs | Remove-Job | Out-Null
}

$durations = @($allResults | Where-Object Success | Select-Object -ExpandProperty DurationMs)
$successCount = @($allResults | Where-Object Success).Count
$failureCount = @($allResults | Where-Object { -not $_.Success }).Count

Write-Host "Load test completed"
Write-Host "Total requests : $TotalRequests"
Write-Host "Concurrency    : $Concurrency"
Write-Host "Succeeded      : $successCount"
Write-Host "Failed         : $failureCount"

if ($durations.Count -gt 0) {
    $avg = [Math]::Round((($durations | Measure-Object -Average).Average), 2)
    $min = [Math]::Round((($durations | Measure-Object -Minimum).Minimum), 2)
    $max = [Math]::Round((($durations | Measure-Object -Maximum).Maximum), 2)
    $p95 = [Math]::Round((Get-Percentile -Values $durations -Percentile 0.95), 2)

    Write-Host "Min duration   : $min ms"
    Write-Host "Avg duration   : $avg ms"
    Write-Host "P95 duration   : $p95 ms"
    Write-Host "Max duration   : $max ms"
}

$metrics = Invoke-RestMethod -Method Get -Uri "$ApiBaseUrl/api/metrics"
Write-Host ""
Write-Host "Central metrics snapshot"
$metrics | ConvertTo-Json -Depth 6

if ($failureCount -gt 0) {
    exit 1
}