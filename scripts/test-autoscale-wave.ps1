param(
    [string]$ApiBase = "http://engie-mca-event-handler-loek-engie.apps.experience.ilionx-ocp.com",
    [int]$High1Minutes = 3,
    [int]$LowMinutes = 3,
    [int]$High2Minutes = 3
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$oc = "$env:USERPROFILE\bin\oc.exe"
$root = Split-Path -Parent $PSScriptRoot
$streamScript = Join-Path $PSScriptRoot "stream-standard-testdata.ps1"

function Show-Replicas([string]$label) {
    Write-Host ""
    Write-Host "[$label] Replicas" -ForegroundColor Cyan
    & $oc get deployment -n loek-engie -o custom-columns=NAME:.metadata.name,REPLICAS:.spec.replicas,READY:.status.readyReplicas --no-headers
}

function Show-Metrics([string]$label) {
    Write-Host ""
    Write-Host "[$label] Metrics" -ForegroundColor Cyan
    try {
        $m = (Invoke-WebRequest -Uri "$ApiBase/api/metrics" -UseBasicParsing -TimeoutSec 20).Content | ConvertFrom-Json
        Write-Host ("total={0} ack={1} nack={2} successRate={3}%" -f $m.totalMessages, $m.ackMessages, $m.nackMessages, [math]::Round([double]$m.successRate,1))
    }
    catch {
        Write-Host "metrics ophalen mislukt: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

Write-Host "=== Autoscale Wave Test (hoog-laag-hoog) ===" -ForegroundColor Green
Write-Host "ApiBase: $ApiBase"
Write-Host "Fase 1 hoog: $High1Minutes min | Fase 2 laag: $LowMinutes min | Fase 3 hoog: $High2Minutes min"

Show-Replicas "Start"
Show-Metrics "Start"

Write-Host ""
Write-Host "Fase 1 (HOOG): load starten..." -ForegroundColor Yellow
& $streamScript -ApiBase $ApiBase -MinPerMinute 240 -MaxPerMinute 360 -DuplicateRatePercent 22 -DurationMinutes $High1Minutes | Out-Host
Start-Sleep -Seconds 70
Show-Replicas "Na hoog-1"
Show-Metrics "Na hoog-1"

Write-Host ""
Write-Host "Fase 2 (LAAG): geen extra load, alleen afkoelen..." -ForegroundColor Yellow
for ($i = 1; $i -le $LowMinutes; $i++) {
    Start-Sleep -Seconds 70
    Show-Replicas "Laag minute $i"
}
Show-Metrics "Na laag"

Write-Host ""
Write-Host "Fase 3 (HOOG): load opnieuw starten..." -ForegroundColor Yellow
& $streamScript -ApiBase $ApiBase -MinPerMinute 220 -MaxPerMinute 340 -DuplicateRatePercent 15 -DurationMinutes $High2Minutes | Out-Host
Start-Sleep -Seconds 70
Show-Replicas "Na hoog-2"
Show-Metrics "Na hoog-2"

Write-Host ""
Write-Host "Klaar. Verwacht patroon: omhoog -> omlaag -> omhoog." -ForegroundColor Green
