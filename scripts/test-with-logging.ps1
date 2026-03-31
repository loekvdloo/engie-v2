Write-Host "=== TESTING UPDATED API WITH FILE LOGGING ===" -ForegroundColor Cyan

# Test 1: Valid message
Write-Host "`n[01/04] Processing VALID message..." -ForegroundColor Yellow
$body1 = @{
    messageId = "test-logging-valid"
    xmlContent = '<?xml version="1.0"?><AllocationSeries><EAN>8714568009996</EAN><Quantity>100</Quantity></AllocationSeries>'
} | ConvertTo-Json

Start-Sleep -Seconds 1
$r1 = Invoke-WebRequest -Uri "http://localhost:5001/api/messages" -Method POST -ContentType "application/json" -Body $body1
$res1 = $r1.Content | ConvertFrom-Json
Write-Host "✓ Valid message: $($res1.status) + $($res1.responseType)" -ForegroundColor Green

# Test 2: Invalid message
Write-Host "`n[02/04] Processing INVALID message (bad EAN)..." -ForegroundColor Yellow
$body2 = @{
    messageId = "test-logging-invalid"
    xmlContent = '<?xml version="1.0"?><AllocationSeries><EAN>BADEAN</EAN></AllocationSeries>'
} | ConvertTo-Json

$r2 = Invoke-WebRequest -Uri "http://localhost:5001/api/messages" -Method POST -ContentType "application/json" -Body $body2
$res2 = $r2.Content | ConvertFrom-Json
Write-Host "✓ Invalid message: $($res2.status) + $($res2.responseType) (Errors: $($res2.errorCount))" -ForegroundColor Green

# Test 3: Get steps for valid
Write-Host "`n[03/04] Getting all steps..." -ForegroundColor Yellow
$r3 = Invoke-WebRequest -Uri "http://localhost:5001/api/messages/test-logging-valid/steps" -Method GET
$res3 = $r3.Content | ConvertFrom-Json
Write-Host "✓ Retrieved $($res3.totalSteps) steps" -ForegroundColor Green

# Test 4: Get statistics
Write-Host "`n[04/04] Getting statistics..." -ForegroundColor Yellow
$r4 = Invoke-WebRequest -Uri "http://localhost:5001/api/messages/stats/summary" -Method GET
$res4 = $r4.Content | ConvertFrom-Json
Write-Host "✓ Total Messages: $($res4.totalMessages) | Success Rate: $($res4.successRate)%" -ForegroundColor Green

# Show log files
Write-Host "`n=== LOG FILES ===" -ForegroundColor Cyan
$logPath = "c:\Users\loek\engie\engie-v2\logs"
if (Test-Path $logPath) {
    Get-Item $logPath\*.log | Select-Object Name, @{N="Size"; E={"{0:N0} bytes" -f $_.Length}}, LastWriteTime | Format-Table -AutoSize
    Write-Host "`n[✓] Logs saved to: $logPath" -ForegroundColor Green
} else {
    Write-Host "[-] Logs directory not found" -ForegroundColor Red
}

Write-Host "`n=== POSTMAN COLLECTION UPDATED ===" -ForegroundColor Cyan
Write-Host "New requests added:"
Write-Host "  • [12] Get All Processing Steps"
Write-Host "  • [13] Get Steps for Invalid Message"
Write-Host "  • [14] Get Processing Statistics"
Write-Host "`nLocation: c:\Users\loek\engie\engie-v2\docs\ENGIE-MCA-API-Postman.json" -ForegroundColor Green
