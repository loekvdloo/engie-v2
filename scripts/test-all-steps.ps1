$body = @{
    messageId = "steps-test-001"
    xmlContent = '<?xml version="1.0"?><AllocationSeries><EAN>8714568009996</EAN><Quantity>100</Quantity></AllocationSeries>'
} | ConvertTo-Json

Start-Sleep -Seconds 1

Write-Host "Sending request..." -ForegroundColor Cyan
$response = Invoke-WebRequest -Uri "http://localhost:5000/api/messages" `
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
$stepsResponse = Invoke-WebRequest -Uri "http://localhost:5000/api/messages/steps-test-001/steps" `
  -Method GET

$steps = $stepsResponse.Content | ConvertFrom-Json
Write-Host "Total Steps: $($steps.totalSteps)" -ForegroundColor Green
Write-Host "`nAll Processing Steps:" -ForegroundColor Green
$steps.steps | ForEach-Object {
    $errorMark = if ($_.hasError) { " ❌" } else { " ✓" }
    Write-Host "  $($_.step) [$($_.column)]$errorMark`n    $($_.description)"
}

Write-Host "`n=== STATISTICS ===" -ForegroundColor Cyan
$stats = (Invoke-WebRequest -Uri "http://localhost:5000/api/messages/stats/summary" -Method GET).Content | ConvertFrom-Json
Write-Host "Total Messages Processed: $($stats.totalMessages)"
Write-Host "Delivered: $($stats.delivered)"
Write-Host "Failed: $($stats.failed)"
Write-Host "Success Rate: $($stats.successRate)%"
Write-Host "Total Steps Executed: $($stats.totalStepsExecuted)"
Write-Host "Average Steps Per Message: $($stats.averageStepsPerMessage)"
