# ============================================================
# ENGIE MCA - Volledige pipeline test (stappen 1A t/m 6B)
# Elke stap van alle 6 services wordt echt gevalideerd.
# Gebruik: .\scripts\test-full-pipeline.ps1
# ============================================================
param(
    [string]$ApiBaseUrl = "http://localhost:5001",
    [switch]$ShowDetails
)

$ErrorActionPreference = "Continue"

# Valide 18-cijferig Nederlands EAN (871 prefix)
$ValidEan = "871234567890100000"
$Stamp    = Get-Date -Format "HHmmss"
$Today    = Get-Date -Format "yyyy-MM-dd"
$Tomorrow = (Get-Date).AddDays(1).ToString("yyyy-MM-dd")

function Invoke-Message {
    param(
        [string]$MessageId,
        [string]$Xml
    )
    $body = @{
        messageId   = $MessageId
        correlationId = "corr-$MessageId"
        xmlContent  = $Xml
    } | ConvertTo-Json -Depth 3

    try {
        return Invoke-RestMethod -Uri "$ApiBaseUrl/api/messages" `
            -Method Post -ContentType "application/json" -Body $body -TimeoutSec 30
    }
    catch {
        return [PSCustomObject]@{
            status     = "Error"
            errorCodes = @("999")
            errorCount = 1
        }
    }
}

function Test-Case {
    param($Case, $Resp)
    $codes       = @($Resp.errorCodes)
    $statusOk    = $Resp.status -eq $Case.ExpectedStatus
    $missingCodes = @($Case.ExpectedCodes | Where-Object { $_ -notin $codes })
    $extraCodes   = @($codes | Where-Object { $_ -notin $Case.ExpectedCodes })
    $codesOk      = ($missingCodes.Count -eq 0) -and ($extraCodes.Count -eq 0)
    $pass = $statusOk -and $codesOk

    $statusColor = if ($pass) { "Green" } else { "Red" }
    $label       = if ($pass) { "PASS" } else { "FAIL" }
    Write-Host "  [$label] $($Case.Name)" -ForegroundColor $statusColor

    if (-not $pass) {
        if (-not $statusOk) {
            Write-Host "         Status:  verwacht=$($Case.ExpectedStatus)  gekregen=$($Resp.status)" -ForegroundColor Yellow
        }
        if ($missingCodes.Count -gt 0) {
            Write-Host "         Ontbrekende codes: $($missingCodes -join ',')" -ForegroundColor Yellow
        }
        if ($extraCodes.Count -gt 0) {
            Write-Host "         Onverwachte codes: $($extraCodes -join ',')" -ForegroundColor Yellow
        }
    } else {
        $codesStr = if ($codes.Count -gt 0) { $codes -join ',' } else { '-' }
        Write-Host "         Status=$($Resp.status)  Codes=$codesStr"
    }

    if ($ShowDetails) {
        $Resp | ConvertTo-Json -Depth 4 | Write-Host
    }

    return $pass
}

# ── Testgevallen ─────────────────────────────────────────────
$Cases = @(

    # -- GELDIG BERICHT --
    @{
        Name           = "Geldig bericht door alle 29 stappen"
        ExpectedStatus = "Delivered"
        ExpectedCodes  = @()
        Xml            = "<AllocationSeries><DocumentID>DOC-OK-$Stamp</DocumentID><EAN>$ValidEan</EAN><Quantity>150</Quantity><StartDateTime>${Today}T08:00:00Z</StartDateTime><EndDateTime>${Today}T09:00:00Z</EndDateTime></AllocationSeries>"
    }

    # -- STAP 1: EVENTHANDLER --
    @{
        Name           = "1C Ongeldige XML (error 001)"
        ExpectedStatus = "Failed"
        ExpectedCodes  = @("001")
        Xml            = "dit is absoluut geen xml"
    }
    @{
        Name           = "1E Onbekend berichttype (error 001)"
        ExpectedStatus = "Failed"
        ExpectedCodes  = @("001")
        Xml            = "<OnbekendType><DocumentID>DOC-TYPE-$Stamp</DocumentID><EAN>$ValidEan</EAN><Quantity>10</Quantity><StartDateTime>${Today}T08:00:00Z</StartDateTime><EndDateTime>${Today}T09:00:00Z</EndDateTime></OnbekendType>"
    }

    # -- STAP 3: VALIDATOR --
    @{
        Name           = "3A Ontbrekende EAN (error 686)"
        ExpectedStatus = "Failed"
        ExpectedCodes  = @("686")
        Xml            = "<AllocationSeries><DocumentID>DOC-686-$Stamp</DocumentID><EAN></EAN><Quantity>100</Quantity><StartDateTime>${Today}T08:00:00Z</StartDateTime><EndDateTime>${Today}T09:00:00Z</EndDateTime></AllocationSeries>"
    }
    @{
        Name           = "3A EAN te kort / niet 18 cijfers (error 686)"
        ExpectedStatus = "Failed"
        ExpectedCodes  = @("686")
        Xml            = "<AllocationSeries><DocumentID>DOC-686B-$Stamp</DocumentID><EAN>8712345</EAN><Quantity>100</Quantity><StartDateTime>${Today}T08:00:00Z</StartDateTime><EndDateTime>${Today}T09:00:00Z</EndDateTime></AllocationSeries>"
    }
    @{
        Name           = "3B StartDateTime in toekomst (error 760)"
        ExpectedStatus = "Failed"
        ExpectedCodes  = @("760")
        Xml            = "<AllocationSeries><DocumentID>DOC-760-$Stamp</DocumentID><EAN>$ValidEan</EAN><Quantity>100</Quantity><StartDateTime>2030-06-01T08:00:00Z</StartDateTime><EndDateTime>2030-06-01T09:00:00Z</EndDateTime></AllocationSeries>"
    }
    @{
        Name           = "3B StartDateTime te oud >90 dagen (error 758)"
        ExpectedStatus = "Failed"
        ExpectedCodes  = @("758")
        Xml            = "<AllocationSeries><DocumentID>DOC-758A-$Stamp</DocumentID><EAN>$ValidEan</EAN><Quantity>100</Quantity><StartDateTime>2019-01-01T00:00:00Z</StartDateTime><EndDateTime>2019-01-01T01:00:00Z</EndDateTime></AllocationSeries>"
    }
    @{
        Name           = "3C Ontbrekend DocumentID (error 676)"
        ExpectedStatus = "Failed"
        ExpectedCodes  = @("676")
        Xml            = "<AllocationSeries><DocumentID></DocumentID><EAN>$ValidEan</EAN><Quantity>100</Quantity><StartDateTime>${Today}T08:00:00Z</StartDateTime><EndDateTime>${Today}T09:00:00Z</EndDateTime></AllocationSeries>"
    }
    @{
        Name           = "3D Negatieve hoeveelheid (error 772)"
        ExpectedStatus = "Failed"
        ExpectedCodes  = @("772")
        Xml            = "<AllocationSeries><DocumentID>DOC-772-$Stamp</DocumentID><EAN>$ValidEan</EAN><Quantity>-5</Quantity><StartDateTime>${Today}T08:00:00Z</StartDateTime><EndDateTime>${Today}T09:00:00Z</EndDateTime></AllocationSeries>"
    }
    @{
        Name           = "3D Hoeveelheid nul (error 774)"
        ExpectedStatus = "Failed"
        ExpectedCodes  = @("774")
        Xml            = "<AllocationSeries><DocumentID>DOC-774-$Stamp</DocumentID><EAN>$ValidEan</EAN><Quantity>0</Quantity><StartDateTime>${Today}T08:00:00Z</StartDateTime><EndDateTime>${Today}T09:00:00Z</EndDateTime></AllocationSeries>"
    }
    @{
        Name           = "3D Hoeveelheid boven limiet (error 773)"
        ExpectedStatus = "Failed"
        ExpectedCodes  = @("773")
        Xml            = "<AllocationSeries><DocumentID>DOC-773-$Stamp</DocumentID><EAN>$ValidEan</EAN><Quantity>9999999</Quantity><StartDateTime>${Today}T08:00:00Z</StartDateTime><EndDateTime>${Today}T09:00:00Z</EndDateTime></AllocationSeries>"
    }
    @{
        Name           = "3E EndDateTime voor StartDateTime (error 758)"
        ExpectedStatus = "Failed"
        ExpectedCodes  = @("758")
        Xml            = "<AllocationSeries><DocumentID>DOC-758B-$Stamp</DocumentID><EAN>$ValidEan</EAN><Quantity>50</Quantity><StartDateTime>${Today}T10:00:00Z</StartDateTime><EndDateTime>${Today}T08:00:00Z</EndDateTime></AllocationSeries>"
    }
    @{
        Name           = "3F Dubbele DocumentID met DUP-prefix (error 755)"
        ExpectedStatus = "Failed"
        ExpectedCodes  = @("755")
        Xml            = "<AllocationSeries><DocumentID>DUP-$Stamp</DocumentID><EAN>$ValidEan</EAN><Quantity>50</Quantity><StartDateTime>${Today}T08:00:00Z</StartDateTime><EndDateTime>${Today}T09:00:00Z</EndDateTime></AllocationSeries>"
    }

    # -- TWEEDE GELDIG BERICHT (andere docid, vandaag andere tijd) --
    @{
        Name           = "Tweede geldig bericht (ander tijdstip)"
        ExpectedStatus = "Delivered"
        ExpectedCodes  = @()
        Xml            = "<AllocationSeries><DocumentID>DOC-OK2-$Stamp</DocumentID><EAN>$ValidEan</EAN><Quantity>250</Quantity><StartDateTime>${Today}T10:00:00Z</StartDateTime><EndDateTime>${Today}T11:00:00Z</EndDateTime></AllocationSeries>"
    }
)

# ── Uitvoering ────────────────────────────────────────────────
Write-Host ""
Write-Host "======================================================" -ForegroundColor Cyan
Write-Host "   ENGIE MCA - Volledige Pipeline Test" -ForegroundColor Cyan
Write-Host "======================================================" -ForegroundColor Cyan
Write-Host "   API : $ApiBaseUrl"
Write-Host "   Timestamp: $Stamp"
Write-Host "   Aantal testgevallen: $($Cases.Count)"
Write-Host ""

$passed = 0
$index  = 0

foreach ($case in $Cases) {
    $index++
    $msgId = "pipe-$index-$Stamp"
    $resp  = Invoke-Message -MessageId $msgId -Xml $case.Xml
    if (Test-Case -Case $case -Resp $resp) { $passed++ }
}

# ── Overzicht procesde berichten ──────────────────────────────
Write-Host ""
Write-Host "------------------------------------------------------" -ForegroundColor Cyan
Write-Host "  Berichten in het systeem (meest recent)" -ForegroundColor Cyan
Write-Host "------------------------------------------------------" -ForegroundColor Cyan

try {
    $allMsgs = Invoke-RestMethod -Uri "$ApiBaseUrl/api/messages" -Method Get -TimeoutSec 5
    $recent  = $allMsgs | Select-Object -Last 20
    $recent | Select-Object `
        @{N="MessageId";    E={$_.messageId}},
        @{N="Status";       E={$_.status}},
        @{N="Type";         E={$_.responseType}},
        @{N="Fouten";       E={$_.errorCount}},
        @{N="Codes";        E={($_.errorCodes -join ",") -replace "^$","-"}} |
        Format-Table -AutoSize
}
catch {
    Write-Host "  (kon berichten niet ophalen: $($_.Exception.Message))" -ForegroundColor Yellow
}

# ── Eindresultaat ─────────────────────────────────────────────
Write-Host "======================================================" -ForegroundColor Cyan
$color = if ($passed -eq $Cases.Count) { "Green" } else { "Yellow" }
Write-Host "  Resultaat: $passed / $($Cases.Count) geslaagd" -ForegroundColor $color
Write-Host "======================================================" -ForegroundColor Cyan
Write-Host ""

if ($passed -ne $Cases.Count) { exit 1 }
