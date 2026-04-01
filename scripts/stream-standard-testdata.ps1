param(
    [string]$ApiBase,
    [int]$MinPerMinute = 50,
    [int]$MaxPerMinute = 200,
    [int]$DuplicateRatePercent = 10,
    [int]$DurationMinutes = 120,
    [switch]$IncludeFullFaultCatalog
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ApiBase)) {
    throw "ApiBase is verplicht, bijvoorbeeld: https://engie-mca-event-handler-loek-engie.apps.experience.ilionx-ocp.com"
}

if ($MinPerMinute -lt 1 -or $MaxPerMinute -lt $MinPerMinute) {
    throw "MinPerMinute en MaxPerMinute ongeldig. Verwacht: Min >= 1 en Max >= Min."
}

if ($DuplicateRatePercent -lt 0 -or $DuplicateRatePercent -gt 100) {
    throw "DuplicateRatePercent moet tussen 0 en 100 liggen."
}

$apiUrl = "$ApiBase/api/messages"

function Send-Message {
    param(
        [string]$MessageId,
        [string]$Xml
    )

    $payload = @{
        messageId = $MessageId
        xmlContent = $Xml
    } | ConvertTo-Json -Compress

    try {
        $resp = Invoke-WebRequest -Uri $apiUrl -Method Post -ContentType "application/json" -Body $payload -UseBasicParsing -TimeoutSec 20
        return ($resp.Content | ConvertFrom-Json)
    }
    catch {
        return [PSCustomObject]@{
            responseType = "Error"
            errorCodes = @()
            errorMessage = $_.Exception.Message
        }
    }
}

function New-ValidXml {
    param(
        [string]$DocId,
        [string]$Ean,
        [string]$Start,
        [string]$End,
        [decimal]$Qty
    )

    return "<AllocationSeries><DocumentID>$DocId</DocumentID><EAN>$Ean</EAN><StartDateTime>$Start</StartDateTime><EndDateTime>$End</EndDateTime><Period><Point><Quantity>$Qty</Quantity></Point></Period></AllocationSeries>"
}

function New-InvalidEanXml {
    param([string]$DocId, [string]$Start, [string]$End)
    return "<AllocationSeries><DocumentID>$DocId</DocumentID><EAN>INVALID</EAN><StartDateTime>$Start</StartDateTime><EndDateTime>$End</EndDateTime><Period><Point><Quantity>100</Quantity></Point></Period></AllocationSeries>"
}

function New-FutureDateXml {
    param([string]$DocId, [string]$FutureStart, [string]$FutureEnd)
    return "<AllocationSeries><DocumentID>$DocId</DocumentID><EAN>871685900012345678</EAN><StartDateTime>$FutureStart</StartDateTime><EndDateTime>$FutureEnd</EndDateTime><Period><Point><Quantity>100</Quantity></Point></Period></AllocationSeries>"
}

function New-MissingDocXml {
    param([string]$Start, [string]$End)
    return "<AllocationSeries><EAN>871685900012345678</EAN><StartDateTime>$Start</StartDateTime><EndDateTime>$End</EndDateTime><Period><Point><Quantity>100</Quantity></Point></Period></AllocationSeries>"
}

$runUntil = (Get-Date).ToUniversalTime().AddMinutes($DurationMinutes)
$minuteLoop = 0
$total = 0
$ack = 0
$nack = 0
$errors = 0
$knownMessageIds = [System.Collections.Generic.List[string]]::new()

Write-Host "Start stream standaard testdata"
Write-Host "ApiBase: $ApiBase"
Write-Host "Load: random $MinPerMinute-$MaxPerMinute berichten per minuut"
Write-Host "Duplicate-rate: $DuplicateRatePercent%"
Write-Host "Duur: $DurationMinutes min"

while ((Get-Date).ToUniversalTime() -lt $runUntil) {
    $minuteLoop++
    $minuteStartUtc = (Get-Date).ToUniversalTime()
    $targetCount = Get-Random -Minimum $MinPerMinute -Maximum ($MaxPerMinute + 1)
    $stamp = Get-Date -Format "yyyyMMddHHmmss"

    Write-Host ""
    Write-Host "Minute ${minuteLoop}: target=$targetCount msgs" -ForegroundColor Cyan

    $start = (Get-Date).AddDays(-5).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    $end = (Get-Date).AddDays(-1).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    $futureStart = (Get-Date).AddDays(5).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    $futureEnd = (Get-Date).AddDays(6).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

    for ($i = 1; $i -le $targetCount; $i++) {
        $useDuplicate = ($knownMessageIds.Count -gt 0) -and ((Get-Random -Minimum 1 -Maximum 101) -le $DuplicateRatePercent)
        if ($useDuplicate) {
            $messageId = $knownMessageIds[(Get-Random -Minimum 0 -Maximum $knownMessageIds.Count)]
        } else {
            $messageId = "stream-$stamp-$minuteLoop-$i-$(Get-Random -Minimum 1000 -Maximum 9999)"
            $knownMessageIds.Add($messageId)
        }

        $roll = Get-Random -Minimum 1 -Maximum 101
        if ($roll -le 55) {
            $xml = New-ValidXml "DOC-$messageId" "871685900012345678" $start $end (Get-Random -Minimum 1 -Maximum 1000)
        } elseif ($roll -le 75) {
            $xml = New-InvalidEanXml "DOC-$messageId" $start $end
        } elseif ($roll -le 90) {
            $xml = New-MissingDocXml $start $end
        } else {
            $xml = New-FutureDateXml "DOC-$messageId" $futureStart $futureEnd
        }

        $res = Send-Message -MessageId $messageId -Xml $xml
        $total++

        if ($res.responseType -eq "Ack") { $ack++ }
        elseif ($res.responseType -eq "Nack") { $nack++ }
        else { $errors++ }

        $codes = if ($res.errorCodes) { @($res.errorCodes) -join "," } else { "" }
        Write-Host "[$minuteLoop/$i] $messageId -> $($res.responseType) [$codes]"
    }

    if ($IncludeFullFaultCatalog) {
        & "$PSScriptRoot\test-all-faultcodes.ps1" -ApiBase $ApiBase
    }

    $elapsed = ((Get-Date).ToUniversalTime() - $minuteStartUtc).TotalSeconds
    $sleepFor = [Math]::Max([int](60 - $elapsed), 0)

    Write-Host "Minute $minuteLoop klaar | totaal=$total ack=$ack nack=$nack errors=$errors"
    if ($sleepFor -gt 0) {
        Write-Host "Wacht $sleepFor sec tot volgende minuut..."
        Start-Sleep -Seconds $sleepFor
    }
}

Write-Host "Klaar | totaal=$total ack=$ack nack=$nack errors=$errors"
