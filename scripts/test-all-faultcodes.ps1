# Test all known EDSN fault codes through the API orchestrator.
# Usage: .\scripts\test-all-faultcodes.ps1

$apiUrl = "http://localhost:5000/api/messages"

# Keep this list aligned with FaultCodeCatalog.
$faultCodes = @(
    "650","651","652","653",
    "654","655","656",
    "676","677","678","679","680",
    "686","687","688","689",
    "700","701","702",
    "754","755","756",
    "758","759","760",
    "772","773","774","775",
    "780","781","782",
    "999"
)

function Send-TestMessage {
    param(
        [string]$MessageId,
        [string]$XmlContent
    )

    $body = @{
        messageId = $MessageId
        xmlContent = $XmlContent
    } | ConvertTo-Json

    try {
        $response = Invoke-WebRequest -Uri $apiUrl -Method POST -ContentType "application/json" -Body $body -TimeoutSec 15 -ErrorAction Stop
        return $response.Content | ConvertFrom-Json
    }
    catch {
        return [PSCustomObject]@{
            status = "RequestFailed"
            responseType = "Error"
            errorCount = 0
            errorCodes = @()
            requestError = $_.Exception.Message
        }
    }
}

Write-Host "=== All Fault Codes Test ===" -ForegroundColor Cyan
Write-Host "API: $apiUrl"
Write-Host "Codes to test: $($faultCodes.Count)"
Write-Host ""

$results = @()
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"

foreach ($code in $faultCodes) {
    $messageId = "allcodes-$code-$stamp"

    # Base message stays valid to avoid accidental extra errors.
    $xml = "<AllocationSeries><EAN>871600000001234</EAN><DocumentID>DOC-$code-$stamp</DocumentID><Quantity>42.5</Quantity><StartDateTime>2026-03-20T00:00:00Z</StartDateTime><EndDateTime>2026-03-21T00:00:00Z</EndDateTime><ForceErrorCodes>$code</ForceErrorCodes></AllocationSeries>"

    $response = Send-TestMessage -MessageId $messageId -XmlContent $xml
    $actualCodes = @()
    if ($response.errorCodes) {
        $actualCodes = @($response.errorCodes)
    }

    $hasCode = $actualCodes -contains $code
    $ok = ($response.responseType -eq "Nack") -and $hasCode

    $results += [PSCustomObject]@{
        Code = $code
        ResponseType = $response.responseType
        ErrorCount = $response.errorCount
        ActualCodes = ($actualCodes -join ",")
        Passed = $ok
        RequestError = if ($response.PSObject.Properties.Name -contains "requestError") { $response.requestError } else { "" }
    }

    if ($ok) {
        Write-Host "[PASS] Code $code -> $($response.responseType) [$($actualCodes -join ',')]" -ForegroundColor Green
    }
    else {
        Write-Host "[FAIL] Code $code -> $($response.responseType) [$($actualCodes -join ',')] $($results[-1].RequestError)" -ForegroundColor Red
    }

    Start-Sleep -Milliseconds 250
}

Write-Host ""
$passCount = ($results | Where-Object { $_.Passed }).Count
$failCount = ($results | Where-Object { -not $_.Passed }).Count
Write-Host "=== Summary ===" -ForegroundColor Cyan
Write-Host "Passed: $passCount / $($faultCodes.Count)"
Write-Host "Failed: $failCount / $($faultCodes.Count)"

if ($failCount -gt 0) {
    Write-Host ""
    Write-Host "Failed codes:" -ForegroundColor Yellow
    $results | Where-Object { -not $_.Passed } | Select-Object Code, ResponseType, ErrorCount, ActualCodes, RequestError | Format-Table -AutoSize
}

Write-Host ""
Write-Host "Done."
