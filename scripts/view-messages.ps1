# ============================================================
# ENGIE MCA - Bekijk verwerkte berichten
# Toont alle berichten met status, foutcodes en stappen.
# Gebruik: .\scripts\view-messages.ps1 [-MessageId <id>] [-Steps] [-Last <n>]
# ============================================================
param(
    [string]$ApiBaseUrl = "http://localhost:5000",
    [string]$MessageId  = "",
    [switch]$Steps,
    [int]$Last          = 20
)

$ErrorActionPreference = "Continue"

function Show-Header {
    param([string]$Title)
    Write-Host ""
    Write-Host "======================================================" -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host "======================================================" -ForegroundColor Cyan
}

function Show-MessageDetail {
    param($Msg)
    $statusColor = if ($Msg.status -eq "Delivered") { "Green" } else { "Red" }
    Write-Host ""
    Write-Host "  MessageId   : $($Msg.messageId)"
    Write-Host "  CorrelationId: $($Msg.correlationId)"
    Write-Host "  Status      : $($Msg.status)" -ForegroundColor $statusColor
    Write-Host "  Response    : $($Msg.responseType)"
    Write-Host "  Foutcodes   : $(($Msg.errorCodes -join ',') -replace '^$','-')"
    Write-Host "  Fouten      : $($Msg.errorCount)"
}

# ── Enkel bericht met stappen ─────────────────────────────────
if ($MessageId -ne "") {
    Show-Header "Detail: $MessageId"

    try {
        $msg = Invoke-RestMethod -Uri "$ApiBaseUrl/api/messages/$MessageId" -Method Get -TimeoutSec 10
        Show-MessageDetail -Msg $msg
    }
    catch {
        Write-Host "  Bericht niet gevonden: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }

    if ($Steps) {
        Write-Host ""
        Write-Host "  --- Verwerkingsstappen ---" -ForegroundColor Cyan
        try {
            $detail = Invoke-RestMethod -Uri "$ApiBaseUrl/api/messages/$MessageId/steps" -Method Get -TimeoutSec 10
            $detail.steps | ForEach-Object {
                $icon = if ($_.hasError) { "✗" } else { "✓" }
                $color = if ($_.hasError) { "Red" } else { "Gray" }
                Write-Host "    $icon [$($_.step)] [$($_.column)] $($_.description)" -ForegroundColor $color
            }
            if ($detail.errors.Count -gt 0) {
                Write-Host ""
                Write-Host "  --- Fouten ---" -ForegroundColor Yellow
                $detail.errors | ForEach-Object {
                    Write-Host "    Code $($_.code) (stap $($_.step)): $($_.message)" -ForegroundColor Yellow
                }
            }
        }
        catch {
            Write-Host "  (stappen niet beschikbaar)" -ForegroundColor Yellow
        }
    }

    exit 0
}

# ── Alle berichten ────────────────────────────────────────────
Show-Header "Verwerkte berichten (laatste $Last)"

try {
    $all = Invoke-RestMethod -Uri "$ApiBaseUrl/api/messages" -Method Get -TimeoutSec 10
}
catch {
    Write-Host "  Kan API niet bereiken: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

if ($all.Count -eq 0) {
    Write-Host "  Geen berichten gevonden. Stuur eerst een bericht via test-api.ps1." -ForegroundColor Yellow
    exit 0
}

$recent = $all | Select-Object -Last $Last

# Tabel
$recent | Select-Object `
    @{N="MessageId";   E={ $_.messageId.Substring(0, [Math]::Min(30, $_.messageId.Length)) }},
    @{N="Status";      E={ $_.status }},
    @{N="Response";    E={ $_.responseType }},
    @{N="Fouten";      E={ $_.errorCount }},
    @{N="Codes";       E={ ($_.errorCodes -join ",") -replace "^$","-" }} |
    Format-Table -AutoSize

# Statistieken
Write-Host "------------------------------------------------------" -ForegroundColor Cyan
$delivered = @($all | Where-Object { $_.status -eq "Delivered" }).Count
$failed    = @($all | Where-Object { $_.status -eq "Failed" }).Count
$total     = $all.Count
$pct       = if ($total -gt 0) { [Math]::Round(100 * $delivered / $total, 1) } else { 0 }

Write-Host "  Totaal  : $total berichten"
Write-Host "  Geslaagd: $delivered  ($pct%)" -ForegroundColor Green
Write-Host "  Mislukt : $failed" -ForegroundColor $(if ($failed -gt 0) { "Red" } else { "Green" })

# Top foutcodes
$allCodes = $all | ForEach-Object { $_.errorCodes } | Where-Object { $_ } | Group-Object | Sort-Object Count -Descending | Select-Object -First 5
if ($allCodes) {
    Write-Host ""
    Write-Host "  Top foutcodes:" -ForegroundColor Cyan
    $allCodes | ForEach-Object {
        Write-Host "    Code $($_.Name): $($_.Count)x"
    }
}

Write-Host ""
Write-Host "  Tip: gebruik -MessageId <id> -Steps voor details van 1 bericht." -ForegroundColor Gray
Write-Host ""
