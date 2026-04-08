Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$envelopeFile = Join-Path $PSScriptRoot "..\test-envelope.json"
if (-not (Test-Path $envelopeFile)) {
  throw "Voorbeeld envelope niet gevonden: $envelopeFile"
}

$envelope = Get-Content -Path $envelopeFile -Raw | ConvertFrom-Json
$nowUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ")

# Gebruik het echte ENGIE voorbeeld als basis met unieke IDs voor deze test-run.
$envelope.id = [guid]::NewGuid().ToString()
$envelope.msgid = "steps-test-001"
$envelope.msgcorrelationid = "steps-test-001"
$envelope.msgcreationtime = $nowUtc
$envelope.createtime = $nowUtc
$envelope.entemtimestamp = $nowUtc
$envelope.msgpayloadid = [guid]::NewGuid().ToString()
$envelope.msgpayload = "<AllocationSeries xmlns='urn:ediel:org:allocation:v4'><DocumentID>DOC-STEPS-001</DocumentID><EAN>871234567890100000</EAN><StartDateTime>$(([DateTime]::UtcNow.AddDays(-5)).ToString('yyyy-MM-ddTHH:mm:ssZ'))</StartDateTime><EndDateTime>$(([DateTime]::UtcNow.AddDays(-1)).ToString('yyyy-MM-ddTHH:mm:ssZ'))</EndDateTime><Quantity>100</Quantity></AllocationSeries>"

$body = $envelope | ConvertTo-Json -Depth 10

Start-Sleep -Seconds 1

Write-Host "Sending request..." -ForegroundColor Cyan
$response = Invoke-WebRequest -Uri "http://localhost:5001/api/messages" `
  -Method POST `
  -ContentType "application/json" `
  -Body $body

Write-Host "`n=== API RESPONSE ===" -ForegroundColor Green
$result = $response.Content | ConvertFrom-Json
Write-Host "Message ID: $($result.messageId)"
Write-Host "Status: $($result.status)"
Write-Host "Response Type: $($result.responseType)"
Write-Host "Error Count: $($result.errorCount)"

Write-Host "`n=== GET ALL STEPS ===" -ForegroundColor Cyan
$stepsResponse = Invoke-WebRequest -Uri "http://localhost:5001/api/messages/steps-test-001/steps" `
  -Method GET

$steps = $stepsResponse.Content | ConvertFrom-Json
Write-Host "Total Steps: $($steps.totalSteps)" -ForegroundColor Green
Write-Host "`nAll Processing Steps:" -ForegroundColor Green
$steps.steps | ForEach-Object {
    $errorMark = if ($_.hasError) { " ❌" } else { " ✓" }
    Write-Host "  $($_.step) [$($_.column)]$errorMark`n    $($_.description)"
}

Write-Host "`n=== STATISTICS ===" -ForegroundColor Cyan
$stats = (Invoke-WebRequest -Uri "http://localhost:5001/api/messages/stats/summary" -Method GET).Content | ConvertFrom-Json
Write-Host "Total Messages Processed: $($stats.totalMessages)"
Write-Host "Delivered: $($stats.delivered)"
Write-Host "Failed: $($stats.failed)"
Write-Host "Success Rate: $($stats.successRate)%"
Write-Host "Total Steps Executed: $($stats.totalStepsExecuted)"
Write-Host "Average Steps Per Message: $($stats.averageStepsPerMessage)"
