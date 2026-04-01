#!/usr/bin/env pwsh
# deploy-to-openshift.ps1
# Volledig lokaal deployment script voor OpenShift project loek-engie
# Gebruik: .\scripts\deploy-to-openshift.ps1

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$OC = "$env:USERPROFILE\bin\oc.exe"
$NAMESPACE = "loek-engie"
$GIT_URL = "https://github.com/loekvdloo/engie-v2.git"
$SERVICES = @("event-handler", "message-processor", "message-validator", "nack-handler", "output-handler")

function Invoke-Oc {
    param([string[]]$Args)
    $result = & $OC @Args 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: oc $($Args -join ' ')" -ForegroundColor Red
        Write-Host $result -ForegroundColor Red
        exit 1
    }
    return $result
}

Write-Host ""
Write-Host "=== Engie MCA - OpenShift Deploy ===" -ForegroundColor Cyan
Write-Host "Namespace : $NAMESPACE"
Write-Host "Git URL   : $GIT_URL"
Write-Host ""

# Controleer of ingelogd
$whoami = & $OC whoami 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "Niet ingelogd op OpenShift." -ForegroundColor Red
    Write-Host ""
    Write-Host "Doe het volgende:" -ForegroundColor Yellow
    Write-Host "  1. Ga naar je OpenShift console" -ForegroundColor Yellow
    Write-Host "  2. Klik rechtsbovenaan op je naam -> 'Copy login command'" -ForegroundColor Yellow
    Write-Host "  3. Klik 'Display Token'" -ForegroundColor Yellow
    Write-Host "  4. Kopieer het 'oc login ...' commando" -ForegroundColor Yellow
    Write-Host "  5. Plak en voer het uit in dit venster" -ForegroundColor Yellow
    Write-Host "  6. Daarna: .\scripts\deploy-to-openshift.ps1" -ForegroundColor Yellow
    exit 1
}
Write-Host "Ingelogd als: $whoami" -ForegroundColor Green

# Switch naar juiste project
Write-Host "`n[1/5] Project instellen..." -ForegroundColor Cyan
Invoke-Oc "project", $NAMESPACE | Out-Null
Write-Host "  Project: $NAMESPACE" -ForegroundColor Green

# Patch buildconfigs.yaml met echte git URL
Write-Host "`n[2/5] Manifests toepassen..." -ForegroundColor Cyan
$buildconfigsRaw = Get-Content "$PSScriptRoot\..\openshift\buildconfigs.yaml" -Raw
$buildconfigsPatched = $buildconfigsRaw -replace "REPLACE_WITH_YOUR_GIT_URL", $GIT_URL
$tempFile = [System.IO.Path]::GetTempFileName() + ".yaml"
$buildconfigsPatched | Set-Content $tempFile -Encoding UTF8

Invoke-Oc "apply", "-f", $tempFile | ForEach-Object { Write-Host "  $_" }
Remove-Item $tempFile -Force

Invoke-Oc "apply", "-f", "$PSScriptRoot\..\openshift\configmap.yaml" | ForEach-Object { Write-Host "  $_" }
Invoke-Oc "apply", "-f", "$PSScriptRoot\..\openshift\deployments.yaml" | ForEach-Object { Write-Host "  $_" }
Write-Host "  Manifests toegepast" -ForegroundColor Green

# Start builds
Write-Host "`n[3/5] Image builds starten (dit duurt ~5-10 min per service)..." -ForegroundColor Cyan
foreach ($svc in $SERVICES) {
    $buildName = "engie-mca-$svc"
    Write-Host "  Build starten: $buildName" -ForegroundColor Yellow
    & $OC start-build $buildName --follow 2>&1 | ForEach-Object {
        if ($_ -match "error|Error" -or $LASTEXITCODE -ne 0) {
            Write-Host "  $_" -ForegroundColor Red
        } else {
            Write-Host "  $_"
        }
    }
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build mislukt voor $buildName" -ForegroundColor Red
        exit 1
    }
    Write-Host "  Build klaar: $buildName" -ForegroundColor Green
}

# Rollout
Write-Host "`n[4/5] Deployments herstarten..." -ForegroundColor Cyan
foreach ($svc in $SERVICES) {
    Invoke-Oc "rollout", "restart", "deployment/engie-mca-$svc" | Out-Null
    Write-Host "  Herstart: engie-mca-$svc"
}

Write-Host "`n[5/5] Wachten op rollout (max 5 min per service)..." -ForegroundColor Cyan
foreach ($svc in $SERVICES) {
    Write-Host "  Wacht: engie-mca-$svc..." -NoNewline
    Invoke-Oc "rollout", "status", "deployment/engie-mca-$svc", "--timeout=300s" | Out-Null
    Write-Host " KLAAR" -ForegroundColor Green
}

# Smoke test
Write-Host "`nSmoke test..." -ForegroundColor Cyan
$host_url = & $OC get route engie-mca-event-handler -o jsonpath='{.spec.host}' 2>&1
if ($LASTEXITCODE -eq 0 -and $host_url) {
    $url = "http://$host_url/api/event/health"
    Write-Host "  URL: $url"
    try {
        $resp = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 10
        Write-Host "  Health check: $($resp.StatusCode) OK" -ForegroundColor Green
        Write-Host ""
        Write-Host "=== DEPLOYMENT GESLAAGD ===" -ForegroundColor Green
        Write-Host "Dashboard : http://$host_url/dashboard" -ForegroundColor Cyan
        Write-Host "API       : http://$host_url/api/event/health" -ForegroundColor Cyan
    } catch {
        Write-Host "  Health check mislukt (service mogelijk nog aan het starten)" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "=== DEPLOYMENT KLAAR ===" -ForegroundColor Green
        Write-Host "Dashboard : http://$host_url/dashboard" -ForegroundColor Cyan
    }
} else {
    Write-Host "  Kon route niet ophalen" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "=== DEPLOYMENT KLAAR ===" -ForegroundColor Green
}
