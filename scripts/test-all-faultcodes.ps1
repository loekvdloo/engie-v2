# Test all known EDSN fault codes + a batch of valid ACK messages.
# Targets Docker stack on port 8081 (docker compose up).
# Usage: .\scripts\test-all-faultcodes.ps1

param(
    [string]$ApiBase = "http://localhost:8081"
)

$apiUrl     = "$ApiBase/api/messages"
$metricsUrl = "$ApiBase/api/metrics"

# Dynamische datums
$start7      = (Get-Date).AddDays(-7).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$end1        = (Get-Date).AddDays(-1).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$start30     = (Get-Date).AddDays(-30).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$end20       = (Get-Date).AddDays(-20).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$start60     = (Get-Date).AddDays(-60).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$end50       = (Get-Date).AddDays(-50).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$futureStart = (Get-Date).AddDays(10).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$futureEnd   = (Get-Date).AddDays(20).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$oldStart    = (Get-Date).AddDays(-120).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$oldEnd      = (Get-Date).AddDays(-91).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

$stamp = Get-Date -Format "HHmmss"

#  Helpers 

function Send-Msg([string]$id, [string]$xml) {
    $body = "{`"messageId`":`"$id`",`"xmlContent`":`"$xml`"}"
    try {
        return (Invoke-WebRequest -Uri $apiUrl -Method Post -ContentType 'application/json' -Body $body -UseBasicParsing -TimeoutSec 20 -ErrorAction Stop).Content | ConvertFrom-Json
    } catch {
        return [PSCustomObject]@{ responseType="Error"; errorCodes=@(); errorMessage=$_.Exception.Message }
    }
}

function Valid-Xml([string]$docId, [string]$ean, [string]$s, [string]$e, [decimal]$qty, [string]$type="AllocationSeries", [string]$extra="") {
    return "<$type><DocumentID>$docId</DocumentID><EAN>$ean</EAN><StartDateTime>$s</StartDateTime><EndDateTime>$e</EndDateTime><Period><Point><Quantity>$qty</Quantity></Point></Period>$extra</$type>"
}

function Force-Xml([string]$docId, [string]$code) {
    return Valid-Xml $docId "871685900012345678" $start7 $end1 100 "AllocationSeries" "<ForceErrorCodes>$code</ForceErrorCodes>"
}

$ackResults  = [System.Collections.Generic.List[PSCustomObject]]::new()
$nackResults = [System.Collections.Generic.List[PSCustomObject]]::new()

function Record([string]$id, $r, [string]$expect, [string]$expectCode="") {
    $codes = if ($r.errorCodes) { @($r.errorCodes) } else { @() }
    $passed = if ($expect -eq "Ack") {
        $r.responseType -eq "Ack"
    } else {
        ($r.responseType -eq "Nack") -and ($expectCode -eq "" -or $codes -contains $expectCode)
    }
    $obj = [PSCustomObject]@{
        Id      = $id
        Expect  = $expect
        Got     = $r.responseType
        Codes   = ($codes -join ",")
        Passed  = $passed
        Err     = if ($r.PSObject.Properties.Name -contains "errorMessage") { $r.errorMessage } else { "" }
    }
    if ($expect -eq "Ack") { $ackResults.Add($obj) } else { $nackResults.Add($obj) }
    $color = if ($passed) { if ($expect -eq "Ack") { "Green" } else { "Red" } } else { "Yellow" }
    $codeStr = if ($expectCode) { " (verwacht code $expectCode)" } else { "" }
    Write-Host "  $(if ($passed) { "[OK]" } else { "[!!]" }) $id -> $($r.responseType) [$($codes -join ",")]$codeStr" -ForegroundColor $color
}

# 
# BLOK 1  ACK: 15 geldige berichten
# 
Write-Host ""
Write-Host " ACK-berichten (geldig) " -ForegroundColor Cyan

$ackCases = @(
    (Valid-Xml "AS-A-$stamp"  "871685900012345678" $start7  $end1  500)
    (Valid-Xml "AS-B-$stamp"  "871685900087654321" $start7  $end1  1)
    (Valid-Xml "AS-C-$stamp"  "871685900011223344" $start7  $end1  999998)
    (Valid-Xml "AS-D-$stamp"  "871685900055667788" $start30 $end20 250)
    (Valid-Xml "AS-E-$stamp"  "871685900099887766" $start60 $end50 12345)
    (Valid-Xml "AFS-A-$stamp" "871685900012345678" $start7  $end1  750   "AllocationFactorSeries")
    (Valid-Xml "AFS-B-$stamp" "871685900087654321" $start30 $end20 1200  "AllocationFactorSeries")
    (Valid-Xml "AFS-C-$stamp" "871685900011223344" $start60 $end50 88    "AllocationFactorSeries")
    (Valid-Xml "AFS-D-$stamp" "871685900099887766" $start7  $end1  33333 "AllocationFactorSeries")
    (Valid-Xml "AFS-E-$stamp" "871685900055667788" $start30 $end20 2     "AllocationFactorSeries")
    (Valid-Xml "AAS-A-$stamp" "871685900099887766" $start7  $end1  9999  "AggregatedAllocationSeries")
    (Valid-Xml "AAS-B-$stamp" "871685900055667788" $start30 $end20 4500  "AggregatedAllocationSeries")
    (Valid-Xml "AAS-C-$stamp" "871685900011223344" $start60 $end50 1     "AggregatedAllocationSeries")
    (Valid-Xml "AAS-D-$stamp" "871685900012399999" $start7  $end1  77777 "AggregatedAllocationSeries")
    (Valid-Xml "AAS-E-$stamp" "871685900012388888" $start7  $end1  50000 "AggregatedAllocationSeries")
)
$i = 1
foreach ($xml in $ackCases) {
    Record "ack-$i-$stamp" (Send-Msg "ack-$i-$stamp" $xml) "Ack"
    $i++
}

# 
# BLOK 2  NACK: elke cataloguscode minimaal 1x
# 
Write-Host ""
Write-Host " NACK  validator-native triggers " -ForegroundColor Cyan

# 686: ongeldige EAN (te kort)
Record "n-686-$stamp" (Send-Msg "n-686-$stamp" "<AllocationSeries><DocumentID>N686</DocumentID><EAN>12345</EAN><StartDateTime>$start7</StartDateTime><EndDateTime>$end1</EndDateTime><Period><Point><Quantity>100</Quantity></Point></Period></AllocationSeries>") "Nack" "686"

# 758: datum ouder dan 90 dagen
Record "n-758-$stamp" (Send-Msg "n-758-$stamp" "<AllocationSeries><DocumentID>N758</DocumentID><EAN>871685900012345678</EAN><StartDateTime>$oldStart</StartDateTime><EndDateTime>$oldEnd</EndDateTime><Period><Point><Quantity>100</Quantity></Point></Period></AllocationSeries>") "Nack" "758"

# 760: datum in toekomst
Record "n-760-$stamp" (Send-Msg "n-760-$stamp" "<AllocationSeries><DocumentID>N760</DocumentID><EAN>871685900012345678</EAN><StartDateTime>$futureStart</StartDateTime><EndDateTime>$futureEnd</EndDateTime><Period><Point><Quantity>100</Quantity></Point></Period></AllocationSeries>") "Nack" "760"

# 676: DocumentID ontbreekt
Record "n-676-$stamp" (Send-Msg "n-676-$stamp" "<AllocationSeries><EAN>871685900012345678</EAN><StartDateTime>$start7</StartDateTime><EndDateTime>$end1</EndDateTime><Period><Point><Quantity>100</Quantity></Point></Period></AllocationSeries>") "Nack" "676"

# 774: hoeveelheid nul
Record "n-774-$stamp" (Send-Msg "n-774-$stamp" (Valid-Xml "N774" "871685900012345678" $start7 $end1 0)) "Nack" "774"

# 772: negatieve hoeveelheid
Record "n-772-$stamp" (Send-Msg "n-772-$stamp" "<AllocationSeries><DocumentID>N772</DocumentID><EAN>871685900012345678</EAN><StartDateTime>$start7</StartDateTime><EndDateTime>$end1</EndDateTime><Period><Point><Quantity>-50</Quantity></Point></Period></AllocationSeries>") "Nack" "772"

# 773: hoeveelheid boven limiet
Record "n-773-$stamp" (Send-Msg "n-773-$stamp" (Valid-Xml "N773" "871685900012345678" $start7 $end1 9999999)) "Nack" "773"

# 755: DocumentID bevat "DUP"
Record "n-755-$stamp" (Send-Msg "n-755-$stamp" (Valid-Xml "DOC-DUP-$stamp" "871685900012345678" $start7 $end1 100)) "Nack" "755"

# 754: stuur zelfde EAN+DocumentID tweemaal (sequence violation op 2e)
$seqXml = Valid-Xml "DOC-SEQ-$stamp" "871685900044332211" $start7 $end1 100
Send-Msg "seq-seed-$stamp" $seqXml | Out-Null
Record "n-754-$stamp" (Send-Msg "n-754-$stamp" $seqXml) "Nack" "754"

Write-Host ""
Write-Host " NACK  ForceErrorCodes via stap 3G " -ForegroundColor Cyan

$forceCodes = @(
    "650","651","652","653",   # XML/Technical
    "654","655","656",         # Message Type
    "677","678","679","680",   # Field Validation
    "687","688","689",         # Business Rules
    "700","701","702",         # BRP Register
    "756","759",               # Sequence/Time
    "775",                     # Quantity format
    "780","781","782",         # Configuration
    "999"                      # Generic
)

foreach ($code in $forceCodes) {
    $id = "n-$code-$stamp"
    Record $id (Send-Msg $id (Force-Xml "FC-$code-$stamp" $code)) "Nack" $code
    Start-Sleep -Milliseconds 100
}

# 
# ═════════════════════════════════════════════════════════════════════════════
# BLOK 3 — Frequente foutcodes: realistische verdeling voor dashboard
# Simuleert productiepatroon waarbij bepaalde codes vaker optreden
# ═════════════════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "━━━ Frequente foutcodes (realistisch patroon) ━━━" -ForegroundColor Cyan

# Code → aantal keer sturen (bovenop de 1x uit blok 2)
$frequent = @(
    @{ code="686"; count=14; desc="Ongeldige EAN — meest voorkomend in productie" }
    @{ code="676"; count=9;  desc="Vereist veld ontbreekt" }
    @{ code="758"; count=8;  desc="Bericht buiten geldige periode" }
    @{ code="774"; count=6;  desc="Hoeveelheid nul" }
    @{ code="760"; count=5;  desc="Datum in toekomst" }
    @{ code="999"; count=4;  desc="Onbekende verwerkingsfout" }
    @{ code="700"; count=3;  desc="BRP-register niet beschikbaar" }
    @{ code="772"; count=3;  desc="Negatieve hoeveelheid" }
    @{ code="755"; count=2;  desc="Dubbele bericht-ID" }
    @{ code="650"; count=2;  desc="Ongeldig XML-formaat" }
)

foreach ($entry in $frequent) {
    $code  = $entry.code
    $count = $entry.count
    Write-Host "  $code ($($entry.desc)) — $count extra berichten" -ForegroundColor DarkGray
    for ($n = 1; $n -le $count; $n++) {
        $id  = "freq-$code-$n-$stamp"
        $xml = if ($code -eq "686") {
            # Native trigger: ongeldige EAN
            "<AllocationSeries><DocumentID>FREQ-$code-$n</DocumentID><EAN>INVALID</EAN><StartDateTime>$start7</StartDateTime><EndDateTime>$end1</EndDateTime><Period><Point><Quantity>100</Quantity></Point></Period></AllocationSeries>"
        } elseif ($code -eq "676") {
            # Native trigger: DocumentID ontbreekt
            "<AllocationSeries><EAN>871685900012345678</EAN><StartDateTime>$start7</StartDateTime><EndDateTime>$end1</EndDateTime><Period><Point><Quantity>100</Quantity></Point></Period></AllocationSeries>"
        } elseif ($code -eq "758") {
            # Native trigger: datum ouder dan 90 dagen
            "<AllocationSeries><DocumentID>FREQ-$code-$n</DocumentID><EAN>871685900012345678</EAN><StartDateTime>$oldStart</StartDateTime><EndDateTime>$oldEnd</EndDateTime><Period><Point><Quantity>100</Quantity></Point></Period></AllocationSeries>"
        } elseif ($code -eq "774") {
            # Native trigger: hoeveelheid nul
            Valid-Xml "FREQ-$code-$n" "871685900012345678" $start7 $end1 0
        } elseif ($code -eq "760") {
            # Native trigger: datum in toekomst
            "<AllocationSeries><DocumentID>FREQ-$code-$n</DocumentID><EAN>871685900012345678</EAN><StartDateTime>$futureStart</StartDateTime><EndDateTime>$futureEnd</EndDateTime><Period><Point><Quantity>100</Quantity></Point></Period></AllocationSeries>"
        } elseif ($code -eq "772") {
            # Native trigger: negatieve hoeveelheid
            "<AllocationSeries><DocumentID>FREQ-$code-$n</DocumentID><EAN>871685900012345678</EAN><StartDateTime>$start7</StartDateTime><EndDateTime>$end1</EndDateTime><Period><Point><Quantity>-1</Quantity></Point></Period></AllocationSeries>"
        } elseif ($code -eq "755") {
            # Native trigger: DUP in DocumentID
            Valid-Xml "DOC-DUP-FREQ-$n-$stamp" "871685900012345678" $start7 $end1 100
        } else {
            # ForceErrorCodes voor alle overige codes
            Force-Xml "FREQ-$code-$n-$stamp" $code
        }
        $r = Send-Msg $id $xml
        $codes = if ($r.errorCodes) { @($r.errorCodes) } else { @() }
        $ok = ($r.responseType -eq "Nack") -and ($codes -contains $code)
        if (-not $ok) {
            Write-Host "    [!!] $id -> $($r.responseType) [$($codes -join ',')] — verwacht $code" -ForegroundColor Yellow
        }
        Start-Sleep -Milliseconds 80
    }
}

# ═════════════════════════════════════════════════════════════════════════════
# Samenvatting
# ═════════════════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host " Samenvatting " -ForegroundColor Cyan

$ackPass  = ($ackResults  | Where-Object Passed).Count
$nackPass = ($nackResults | Where-Object Passed).Count
$ackFail  = $ackResults.Count  - $ackPass
$nackFail = $nackResults.Count - $nackPass

Write-Host "ACK-tests:  $ackPass/$($ackResults.Count) geslaagd"  -ForegroundColor $(if ($ackFail  -eq 0) {"Green"} else {"Yellow"})
Write-Host "NACK-tests: $nackPass/$($nackResults.Count) geslaagd" -ForegroundColor $(if ($nackFail -eq 0) {"Green"} else {"Yellow"})
Write-Host "Totaal:     $($ackPass+$nackPass)/$($ackResults.Count+$nackResults.Count) geslaagd" -ForegroundColor $(if ($ackFail+$nackFail -eq 0) {"Green"} else {"Yellow"})

if ($ackFail + $nackFail -gt 0) {
    Write-Host ""
    Write-Host "Mislukte tests:" -ForegroundColor Red
    ($ackResults + $nackResults) | Where-Object { -not $_.Passed } | Format-Table Id,Expect,Got,Codes,Err -AutoSize
}

try {
    $m = (Invoke-WebRequest -Uri $metricsUrl -UseBasicParsing -TimeoutSec 5).Content | ConvertFrom-Json
    Write-Host ""
    Write-Host "Dashboard na test:" -ForegroundColor Cyan
    Write-Host "  Totaal=$($m.totalMessages)  ACK=$($m.ackMessages)  NACK=$($m.nackMessages)  Rate=$([math]::Round($m.successRate,1))%"
    Write-Host "  Gem=$([math]::Round($m.averageProcessingDurationMs))ms  P95=$([math]::Round($m.p95ProcessingDurationMs))ms"
    if ($m.errorsByCode) {
        Write-Host "  Foutcodes: $(($m.errorsByCode | ForEach-Object { "$($_.code):$($_.count)x" }) -join "  ")"
    }
} catch {}

Write-Host ""
Write-Host "Dashboard: http://localhost:8081/dashboard" -ForegroundColor Cyan
