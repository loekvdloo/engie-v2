# Test per-block logging using current API endpoint.
# Usage: .\scripts\test-block-logging.ps1

$apiUrl = "http://localhost:5001/api/messages"
$logDir = "c:\Users\loek\engie\engie-v2\logs\blocks"

function Send-TestMessage {
    param(
        [string]$MessageId,
        [string]$XmlContent
    )

    $body = @{
        id                       = [guid]::NewGuid().ToString()
        type                     = "mma.msg.new"
        source                   = "ENTEM"
        msgtype                  = "AllocationServiceNotification"
        msgsubtype               = "N101"
        msgid                    = $MessageId
        msgcorrelationid         = $MessageId
        msgpayload               = $XmlContent
        entemsendacknowledgement = $true
        entemsendtooutput        = $true
        entemvalidationresult    = @()
    } | ConvertTo-Json

    try {
        $response = Invoke-WebRequest -Uri $apiUrl -Method POST -ContentType "application/json" -Body $body -TimeoutSec 10 -ErrorAction Stop
        $result = $response.Content | ConvertFrom-Json
        Write-Host "Message: $MessageId"
        Write-Host "Status: $($result.status), ResponseType: $($result.responseType), Errors: $($result.errorCount)"
        if ($result.errorCodes) {
            Write-Host "ErrorCodes: $($result.errorCodes -join ',')"
        }
    }
    catch {
        Write-Host "Request failed for ${MessageId}: $($_.Exception.Message)" -ForegroundColor Red
    }
}

function Show-BlockLogs {
    param([string[]]$MessageIds)

    if (-not (Test-Path $logDir)) {
        Write-Host "Log folder not found: $logDir" -ForegroundColor Red
        return
    }

    $files = Get-ChildItem "$logDir\*.log" | Sort-Object Name
    foreach ($file in $files) {
        Write-Host ""
        Write-Host "=== $($file.Name) ===" -ForegroundColor Cyan

        $printed = $false
        foreach ($id in $MessageIds) {
            $matches = Select-String -Path $file.FullName -Pattern $id -ErrorAction SilentlyContinue
            if ($matches) {
                $printed = $true
                Write-Host "-- $id --" -ForegroundColor Yellow
                $matches | ForEach-Object { Write-Host $_.Line }
            }
        }

        if (-not $printed) {
            Write-Host "(no matching lines)" -ForegroundColor DarkGray
        }
    }
}

$validId = "block-valid-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
$invalidId = "block-invalid-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

$validXml = '<?xml version="1.0"?><AllocationSeries><EAN>8714568009996</EAN><Quantity>100</Quantity></AllocationSeries>'
$invalidXml = '<?xml version="1.0"?><AllocationSeries><EAN></EAN><Quantity>100</Quantity></AllocationSeries>'

Write-Host "Running per-block logging test" -ForegroundColor Green
Write-Host "API: $apiUrl"

Write-Host ""
Write-Host "[1/3] Sending VALID message" -ForegroundColor Green
Send-TestMessage -MessageId $validId -XmlContent $validXml

Start-Sleep -Seconds 1

Write-Host ""
Write-Host "[2/3] Sending INVALID message" -ForegroundColor Green
Send-TestMessage -MessageId $invalidId -XmlContent $invalidXml

Start-Sleep -Seconds 2

Write-Host ""
Write-Host "[3/3] Showing per-block logs for both message IDs" -ForegroundColor Green
Show-BlockLogs -MessageIds @($validId, $invalidId)

Write-Host ""
Write-Host "Done. Check each block log shows only its own steps." -ForegroundColor Green
