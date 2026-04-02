$body = @{
    id                       = [guid]::NewGuid().ToString()
    type                     = "mma.msg.new"
    createtime               = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ")
    source                   = "ENTEM"
    msgsender                = "8716867000016"
    msgsenderrole            = "ZV"
    msgreceiver              = "8716800000085"
    msgreceiverrole          = "LV"
    msgtype                  = "AllocationSeries"
    msgsubtype               = "E35"
    msgid                    = "steps-test-001"
    msgcorrelationid         = "steps-test-001"
    msgcreationtime          = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ")
    msgversion               = "4.0"
    msgpayloadid             = [guid]::NewGuid().ToString()
    msgcontenttype           = "application/xml"
    msgpayload               = "<AllocationSeries xmlns='urn:ediel:org:allocation:v4'><DocumentID>DOC-STEPS-001</DocumentID><EAN>871234567890100000</EAN><StartDateTime>$(([DateTime]::UtcNow.AddDays(-5)).ToString('yyyy-MM-ddTHH:mm:ssZ'))</StartDateTime><EndDateTime>$(([DateTime]::UtcNow.AddDays(-1)).ToString('yyyy-MM-ddTHH:mm:ssZ'))</EndDateTime><Quantity>100</Quantity></AllocationSeries>"
    entemsendacknowledgement = $true
    entemsendtooutput        = $true
    entemvalidationresult    = @()
    entemtimestamp           = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ")
} | ConvertTo-Json

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
