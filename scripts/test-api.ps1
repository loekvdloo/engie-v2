param(
    [string]$ApiUrl = "http://localhost:5001/api/messages",
    [string]$EnvelopeFile = (Join-Path $PSScriptRoot "..\test-envelope.json"),
    [string]$ExpectedResponseFile = (Join-Path $PSScriptRoot "..\test-response.json")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path $EnvelopeFile)) {
    throw "Voorbeeld envelope niet gevonden: $EnvelopeFile"
}

$envelope = Get-Content -Path $EnvelopeFile -Raw | ConvertFrom-Json
$nowUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ")
$unique = Get-Date -Format "yyyyMMddHHmmss"

# Gebruik het echte ENGIE voorbeeld als basis, maar maak IDs uniek voor herhaalbaar testen.
$envelope.id = [guid]::NewGuid().ToString()
$envelope.msgid = "test-valid-$unique"
$envelope.msgcorrelationid = $envelope.msgid
$envelope.msgcreationtime = $nowUtc
$envelope.createtime = $nowUtc
$envelope.entemtimestamp = $nowUtc
$envelope.msgpayloadid = [guid]::NewGuid().ToString()

$body = $envelope | ConvertTo-Json -Depth 10

$response = Invoke-WebRequest -Uri $ApiUrl `
  -Method POST `
  -ContentType "application/json" `
  -Body $body

$actual = $response.Content | ConvertFrom-Json

Write-Host "=== API RESPONSE ===" -ForegroundColor Green
$actual | ConvertTo-Json -Depth 6

if (Test-Path $ExpectedResponseFile) {
    $expected = Get-Content -Path $ExpectedResponseFile -Raw | ConvertFrom-Json
    Write-Host "`n=== VERWACHT VOORBEELD (test-response.json) ===" -ForegroundColor Cyan
    $expected | Select-Object status, responseType, errorCount, errorCodes | ConvertTo-Json -Depth 6
}
