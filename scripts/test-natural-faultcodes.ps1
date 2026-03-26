param(
    [string]$ApiBaseUrl = "http://localhost:5000",
    [switch]$ShowResponses
)

$ErrorActionPreference = "Stop"

function New-RequestBody {
    param(
        [string]$MessageId,
        [string]$Ean = "8712345678901",
        [string]$DocumentId = "DOC-OK",
        [decimal]$Quantity = 100,
        [string]$StartDateTime = "2026-03-20T10:00:00Z",
        [string]$EndDateTime = "2026-03-20T11:00:00Z"
    )

    $xml = @"
<AllocationSeries>
  <DocumentID>$DocumentId</DocumentID>
  <EAN>$Ean</EAN>
  <Quantity>$Quantity</Quantity>
  <StartDateTime>$StartDateTime</StartDateTime>
  <EndDateTime>$EndDateTime</EndDateTime>
</AllocationSeries>
"@

    return @{
        messageId = $MessageId
        correlationId = "natural-$MessageId"
        xmlContent = $xml
    } | ConvertTo-Json -Depth 5
}

$cases = @(
    @{ Name = "valid"; ExpectedStatus = "Delivered"; ExpectedCodes = @(); Body = (New-RequestBody -MessageId "natural-valid") }
    @{ Name = "missing-ean"; ExpectedStatus = "Failed"; ExpectedCodes = @("686"); Body = (New-RequestBody -MessageId "natural-686" -Ean "") }
    @{ Name = "missing-documentid"; ExpectedStatus = "Failed"; ExpectedCodes = @("676"); Body = (New-RequestBody -MessageId "natural-676" -DocumentId "") }
    @{ Name = "future-start"; ExpectedStatus = "Failed"; ExpectedCodes = @("760"); Body = (New-RequestBody -MessageId "natural-760" -StartDateTime "2030-01-01T00:00:00Z" -EndDateTime "2030-01-01T01:00:00Z") }
    @{ Name = "old-start"; ExpectedStatus = "Failed"; ExpectedCodes = @("758"); Body = (New-RequestBody -MessageId "natural-758-old" -StartDateTime "2020-01-01T00:00:00Z" -EndDateTime "2020-01-01T01:00:00Z") }
    @{ Name = "end-before-start"; ExpectedStatus = "Failed"; ExpectedCodes = @("758"); Body = (New-RequestBody -MessageId "natural-758-window" -StartDateTime "2026-03-20T11:00:00Z" -EndDateTime "2026-03-20T10:00:00Z") }
    @{ Name = "negative-quantity"; ExpectedStatus = "Failed"; ExpectedCodes = @("772"); Body = (New-RequestBody -MessageId "natural-772" -Quantity -1) }
    @{ Name = "zero-quantity"; ExpectedStatus = "Failed"; ExpectedCodes = @("774"); Body = (New-RequestBody -MessageId "natural-774" -Quantity 0) }
    @{ Name = "too-large-quantity"; ExpectedStatus = "Failed"; ExpectedCodes = @("773"); Body = (New-RequestBody -MessageId "natural-773" -Quantity 1000000) }
    @{ Name = "duplicate-document"; ExpectedStatus = "Failed"; ExpectedCodes = @("755"); Body = (New-RequestBody -MessageId "natural-755" -DocumentId "DUP-001") }
)

$passed = 0

foreach ($case in $cases) {
    $headers = @{ "Content-Type" = "application/json"; "X-Correlation-ID" = "corr-$($case.Name)" }
    $response = Invoke-RestMethod -Method Post -Uri "$ApiBaseUrl/api/messages" -Headers $headers -Body $case.Body

    $codes = @($response.errorCodes)
    $missingCodes = @($case.ExpectedCodes | Where-Object { $_ -notin $codes })
    $statusMatches = $response.status -eq $case.ExpectedStatus
    $codesMatch = $missingCodes.Count -eq 0 -and $codes.Count -eq $case.ExpectedCodes.Count

    if ($statusMatches -and $codesMatch) {
        $passed++
        Write-Host "[PASS] $($case.Name) -> status=$($response.status) codes=$($codes -join ',')"
    }
    else {
        Write-Host "[FAIL] $($case.Name) -> expectedStatus=$($case.ExpectedStatus) actualStatus=$($response.status) expectedCodes=$($case.ExpectedCodes -join ',') actualCodes=$($codes -join ',')"
    }

    if ($ShowResponses) {
        $response | ConvertTo-Json -Depth 5
    }
}

Write-Host ""
Write-Host "Natural validation tests passed: $passed / $($cases.Count)"

if ($passed -ne $cases.Count) {
    exit 1
}