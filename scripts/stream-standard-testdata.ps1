param(
    [string]$ApiBase,
    [int]$IntervalSeconds = 20,
    [int]$DurationMinutes = 120,
    [switch]$IncludeFullFaultCatalog
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ApiBase)) {
    throw "ApiBase is verplicht, bijvoorbeeld: https://engie-mca-event-handler-loek-engie.apps.experience.ilionx-ocp.com"
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
$loop = 0
$total = 0
$ack = 0
$nack = 0
$errors = 0

Write-Host "Start stream standaard testdata"
Write-Host "ApiBase: $ApiBase"
Write-Host "Interval: $IntervalSeconds sec"
Write-Host "Duur: $DurationMinutes min"

while ((Get-Date).ToUniversalTime() -lt $runUntil) {
    $loop++
    $stamp = Get-Date -Format "yyyyMMddHHmmss"
    $start = (Get-Date).AddDays(-5).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    $end = (Get-Date).AddDays(-1).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    $futureStart = (Get-Date).AddDays(5).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    $futureEnd = (Get-Date).AddDays(6).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

    $cases = @(
        @{ Id = "stream-ack-1-$stamp"; Xml = (New-ValidXml "S-ACK1-$stamp" "871685900012345678" $start $end 250) },
        @{ Id = "stream-ack-2-$stamp"; Xml = (New-ValidXml "S-ACK2-$stamp" "871685900087654321" $start $end 900) },
        @{ Id = "stream-n686-$stamp"; Xml = (New-InvalidEanXml "S-N686-$stamp" $start $end) },
        @{ Id = "stream-n676-$stamp"; Xml = (New-MissingDocXml $start $end) },
        @{ Id = "stream-n760-$stamp"; Xml = (New-FutureDateXml "S-N760-$stamp" $futureStart $futureEnd) }
    )

    foreach ($c in $cases) {
        $res = Send-Message -MessageId $c.Id -Xml $c.Xml
        $total++

        if ($res.responseType -eq "Ack") { $ack++ }
        elseif ($res.responseType -eq "Nack") { $nack++ }
        else { $errors++ }

        $codes = if ($res.errorCodes) { @($res.errorCodes) -join "," } else { "" }
        Write-Host "[$loop] $($c.Id) -> $($res.responseType) [$codes]"
    }

    if ($IncludeFullFaultCatalog) {
        & "$PSScriptRoot\test-all-faultcodes.ps1" -ApiBase $ApiBase
    }

    Write-Host "Loop $loop klaar | totaal=$total ack=$ack nack=$nack errors=$errors"
    Start-Sleep -Seconds $IntervalSeconds
}

Write-Host "Klaar | totaal=$total ack=$ack nack=$nack errors=$errors"
