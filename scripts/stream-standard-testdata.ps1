param(
    [string]$ApiBase,
    [int]$MinPerMinute = 50,
    [int]$MaxPerMinute = 200,
    [int]$DuplicateRatePercent = 10,
    [int]$DurationMinutes = 120,
    [switch]$IncludeFullFaultCatalog,
    [string]$EnvelopeTemplateFile = (Join-Path $PSScriptRoot "..\test-envelope.json"),
    [string]$SampleXmlDirectory = (Join-Path $PSScriptRoot "..\samples\onedrive_1_4-7-2026"),
    [switch]$IncludeTemplateValidationResults,
    [bool]$EnsureAllNackCodes = $true
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

if (-not (Test-Path $EnvelopeTemplateFile)) {
    throw "EnvelopeTemplateFile niet gevonden: $EnvelopeTemplateFile"
}

$apiUrl = "$ApiBase/api/messages"
$script:EnvelopeTemplate = Get-Content -Path $EnvelopeTemplateFile -Raw | ConvertFrom-Json
$script:SampleSeeds = [System.Collections.Generic.List[pscustomobject]]::new()

function Get-FirstRegexValue {
    param(
        [string]$InputText,
        [string]$Pattern
    )

    $m = [System.Text.RegularExpressions.Regex]::Match(
        $InputText,
        $Pattern,
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase -bor [System.Text.RegularExpressions.RegexOptions]::Singleline
    )

    if ($m.Success) {
        return $m.Groups[1].Value.Trim()
    }

    return $null
}

if (Test-Path $SampleXmlDirectory) {
    $sampleFiles = Get-ChildItem -Path $SampleXmlDirectory -Filter "*.xml" -File -ErrorAction SilentlyContinue
    foreach ($sampleFile in $sampleFiles) {
        try {
            $rawXml = Get-Content -Path $sampleFile.FullName -Raw

            # Pak representatieve waarden uit de echte voorbeeldberichten.
            $ean = Get-FirstRegexValue -InputText $rawXml -Pattern "<mRID>\s*(\d{18})\s*</mRID>"
            $qtyText = Get-FirstRegexValue -InputText $rawXml -Pattern "<quantity>\s*([0-9]+(?:\.[0-9]+)?)\s*</quantity>"

            if ([string]::IsNullOrWhiteSpace($ean)) {
                continue
            }

            $qty = 100.0
            if (-not [string]::IsNullOrWhiteSpace($qtyText)) {
                [double]$parsedQty = 0.0
                if ([double]::TryParse($qtyText, [System.Globalization.NumberStyles]::Any, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$parsedQty)) {
                    $qty = [Math]::Max($parsedQty, 0.001)
                }
            }

            $script:SampleSeeds.Add([PSCustomObject]@{
                Ean = $ean
                Quantity = $qty
                SourceFile = $sampleFile.Name
            })
        }
        catch {
            Write-Host "Sample XML overslaan ($($sampleFile.Name)): $($_.Exception.Message)" -ForegroundColor DarkYellow
        }
    }
}

function Send-Message {
    param(
        [string]$MessageId,
        [string]$Xml
    )

    $nowUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ")

    # Clone de voorbeeld-envelope zodat elk bericht het echte ENGIE formaat behoudt.
    $payloadEnvelope = $script:EnvelopeTemplate | ConvertTo-Json -Depth 10 | ConvertFrom-Json
    $payloadEnvelope.id = [guid]::NewGuid().ToString()
    $payloadEnvelope.msgid = $MessageId
    $payloadEnvelope.msgcorrelationid = $MessageId
    $payloadEnvelope.createtime = $nowUtc
    $payloadEnvelope.msgcreationtime = $nowUtc
    $payloadEnvelope.msgpayloadid = [guid]::NewGuid().ToString()
    $payloadEnvelope.msgpayload = $Xml
    $payloadEnvelope.entemtimestamp = $nowUtc

    if (-not $IncludeTemplateValidationResults) {
        # Voorkom dat statische template-codes (zoals 100) alle berichten onnodig naar NACK sturen.
        $payloadEnvelope.entemvalidationresult = @()
    }

    $payload = $payloadEnvelope | ConvertTo-Json -Compress -Depth 10

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

    return "<AllocationSeries xmlns='urn:ediel:org:allocation:v4'><DocumentID>$DocId</DocumentID><EAN>$Ean</EAN><StartDateTime>$Start</StartDateTime><EndDateTime>$End</EndDateTime><Period><Point><Quantity>$Qty</Quantity></Point></Period></AllocationSeries>"
}

function New-InvalidEanXml {
    param([string]$DocId, [string]$Start, [string]$End)
    return "<AllocationSeries xmlns='urn:ediel:org:allocation:v4'><DocumentID>$DocId</DocumentID><EAN>INVALID</EAN><StartDateTime>$Start</StartDateTime><EndDateTime>$End</EndDateTime><Period><Point><Quantity>100</Quantity></Point></Period></AllocationSeries>"
}

function New-FutureDateXml {
    param([string]$DocId, [string]$FutureStart, [string]$FutureEnd)
    return "<AllocationSeries xmlns='urn:ediel:org:allocation:v4'><DocumentID>$DocId</DocumentID><EAN>871685900012345678</EAN><StartDateTime>$FutureStart</StartDateTime><EndDateTime>$FutureEnd</EndDateTime><Period><Point><Quantity>100</Quantity></Point></Period></AllocationSeries>"
}

function New-MissingDocXml {
    param([string]$Start, [string]$End)
    return "<AllocationSeries xmlns='urn:ediel:org:allocation:v4'><EAN>871685900012345678</EAN><StartDateTime>$Start</StartDateTime><EndDateTime>$End</EndDateTime><Period><Point><Quantity>100</Quantity></Point></Period></AllocationSeries>"
}

function New-ForcedCodeXml {
    param(
        [string]$DocId,
        [string]$Code,
        [string]$Start,
        [string]$End
    )

    return "<AllocationSeries xmlns='urn:ediel:org:allocation:v4'><DocumentID>$DocId</DocumentID><EAN>871685900012345678</EAN><StartDateTime>$Start</StartDateTime><EndDateTime>$End</EndDateTime><Period><Point><Quantity>100</Quantity></Point></Period><ForceErrorCodes>$Code</ForceErrorCodes></AllocationSeries>"
}

$runUntil = (Get-Date).ToUniversalTime().AddMinutes($DurationMinutes)
$minuteLoop = 0
$total = 0
$ack = 0
$nack = 0
$errors = 0
$knownMessageIds = [System.Collections.Generic.List[string]]::new()

$allFaultCodes = @(
    "650","651","652","653",
    "654","655","656",
    "676","677","678","679","680",
    "683","686","687","688","689",
    "700","701","702",
    "754","755","756",
    "758","759","760",
    "772","773","774","775",
    "780","781","782",
    "999"
)

Write-Host "Start stream standaard testdata"
Write-Host "ApiBase: $ApiBase"
Write-Host "Load: random $MinPerMinute-$MaxPerMinute berichten per minuut"
Write-Host "Duplicate-rate: $DuplicateRatePercent%"
Write-Host "Duur: $DurationMinutes min"
Write-Host "Sample XML map: $SampleXmlDirectory"
Write-Host "Loaded sample seeds: $($script:SampleSeeds.Count)"

if ($EnsureAllNackCodes) {
    Write-Host ""
    Write-Host "NACK/ACK catalogus seeding (interleaved, alle foutcodes minimaal 1x)..." -ForegroundColor Cyan
    $seedStart = (Get-Date).AddDays(-5).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    $seedEnd = (Get-Date).AddDays(-1).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    $seedStamp = Get-Date -Format "yyyyMMddHHmmss"
    $ackCursor = 0

    foreach ($code in $allFaultCodes) {
        $seedId = "seed-$code-$seedStamp"
        $seedXml = New-ForcedCodeXml "DOC-$seedId" $code $seedStart $seedEnd
        $seedRes = Send-Message -MessageId $seedId -Xml $seedXml
        $total++

        if ($seedRes.responseType -eq "Ack") { $ack++ }
        elseif ($seedRes.responseType -eq "Nack") { $nack++ }
        else { $errors++ }

        $seedCodes = if ($seedRes.errorCodes) { @($seedRes.errorCodes) -join "," } else { "" }
        $ok = ($seedRes.responseType -eq "Nack") -and (@($seedRes.errorCodes) -contains $code)
        Write-Host "[seed/$code] $seedId -> $($seedRes.responseType) [$seedCodes] $(if ($ok) { 'OK' } else { 'CHECK' })"

        # Interleave: na elke geforceerde NACK direct een geldige ACK sturen.
        $ackSeedId = "seed-ack-$code-$seedStamp"
        if ($script:SampleSeeds.Count -gt 0) {
            $ackSeed = $script:SampleSeeds[$ackCursor % $script:SampleSeeds.Count]
            $ackEan = $ackSeed.Ean
            $ackQty = $ackSeed.Quantity
        }
        else {
            $ackEan = "871685900012345678"
            $ackQty = (Get-Random -Minimum 1 -Maximum 1000)
        }
        $ackCursor++
        $ackSeedXml = New-ValidXml "DOC-$ackSeedId" $ackEan $seedStart $seedEnd $ackQty
        $ackSeedRes = Send-Message -MessageId $ackSeedId -Xml $ackSeedXml
        $total++

        if ($ackSeedRes.responseType -eq "Ack") { $ack++ }
        elseif ($ackSeedRes.responseType -eq "Nack") { $nack++ }
        else { $errors++ }

        $ackSeedCodes = if ($ackSeedRes.errorCodes) { @($ackSeedRes.errorCodes) -join "," } else { "" }
        Write-Host "[seed/ack] $ackSeedId -> $($ackSeedRes.responseType) [$ackSeedCodes]"
    }
}

while ((Get-Date).ToUniversalTime() -lt $runUntil) {
    $minuteLoop++
    $minuteStartUtc = (Get-Date).ToUniversalTime()
    $targetCount = Get-Random -Minimum $MinPerMinute -Maximum ($MaxPerMinute + 1)
    $stamp = Get-Date -Format "yyyyMMddHHmmss"
    $profile = switch ($minuteLoop % 3) {
        1 { "ack-heavy" }
        2 { "balanced" }
        default { "nack-heavy" }
    }

    Write-Host ""
    Write-Host "Minute ${minuteLoop}: target=$targetCount msgs, profile=$profile" -ForegroundColor Cyan

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
        # Hogere ACK-bias voor continue tests, met nog steeds voldoende NACK-variatie.
        $ackThreshold = if ($profile -eq "ack-heavy") { 88 } elseif ($profile -eq "nack-heavy") { 58 } else { 74 }

        if ($roll -le $ackThreshold) {
            if ($script:SampleSeeds.Count -gt 0) {
                $seed = $script:SampleSeeds[(Get-Random -Minimum 0 -Maximum $script:SampleSeeds.Count)]
                $xml = New-ValidXml "DOC-$messageId" $seed.Ean $start $end $seed.Quantity
            }
            else {
                $xml = New-ValidXml "DOC-$messageId" "871685900012345678" $start $end (Get-Random -Minimum 1 -Maximum 1000)
            }
        } elseif ($roll -le ($ackThreshold + 15)) {
            $xml = New-InvalidEanXml "DOC-$messageId" $start $end
        } elseif ($roll -le ($ackThreshold + 30)) {
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
