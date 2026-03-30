param(
    [string]$BoardFile = "docs/trello-board-api-complete-2026-03.json",
    [string]$ApiKey = $env:TRELLO_KEY,
    [string]$ApiToken = $env:TRELLO_TOKEN,
    [string]$WorkspaceId = $env:TRELLO_WORKSPACE_ID,
    [switch]$OpenBoard
)

$ErrorActionPreference = "Stop"

function Assert-Value {
    param(
        [string]$Value,
        [string]$Name
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "$Name ontbreekt. Zet de parameter of environment variable."
    }
}

function Invoke-TrelloApi {
    param(
        [ValidateSet("GET", "POST", "PUT")]
        [string]$Method,
        [string]$Path,
        [hashtable]$Query = @{},
        $Body = $null
    )

    $baseUrl = "https://api.trello.com/1"
    $uriBuilder = [System.UriBuilder]::new("$baseUrl$Path")
    $queryParts = New-Object System.Collections.Generic.List[string]
    $queryParts.Add("key=$([uri]::EscapeDataString($ApiKey))")
    $queryParts.Add("token=$([uri]::EscapeDataString($ApiToken))")

    foreach ($entry in $Query.GetEnumerator()) {
        if ($null -ne $entry.Value -and $entry.Value -ne "") {
            $queryParts.Add("$($entry.Key)=$([uri]::EscapeDataString([string]$entry.Value))")
        }
    }

    $uriBuilder.Query = [string]::Join("&", $queryParts)
    if ($null -eq $Body) {
        return Invoke-RestMethod -Method $Method -Uri $uriBuilder.Uri
    }

    return Invoke-RestMethod -Method $Method -Uri $uriBuilder.Uri -ContentType "application/json" -Body ($Body | ConvertTo-Json -Depth 20)
}

function Get-LabelColor {
    param([string]$Label)

    switch ($Label.ToLowerInvariant()) {
        "blauw" { return "blue" }
        "geel" { return "yellow" }
        "oranje" { return "orange" }
        "groen" { return "green" }
        "fase-1" { return "sky" }
        "fase-2" { return "lime" }
        "fase-3" { return "pink" }
        "fase-4" { return "purple" }
        "kolom-1" { return "blue" }
        "kolom-2-4" { return "orange" }
        "kolom-3" { return "yellow" }
        "kolom-5" { return "red" }
        "kolom-6" { return "green" }
        default { return "black" }
    }
}

function Build-CardDescription {
    param($Card)

    $lines = New-Object System.Collections.Generic.List[string]
    if ($Card.description) {
        $lines.Add($Card.description)
        $lines.Add("")
    }

    if ($Card.errorCodes -and $Card.errorCodes.Count -gt 0) {
        $lines.Add("Foutcodes")
        foreach ($code in $Card.errorCodes) {
            $lines.Add("- $code")
        }
        $lines.Add("")
    }

    if ($Card.acceptanceCriteria -and $Card.acceptanceCriteria.Count -gt 0) {
        $lines.Add("Acceptatiecriteria")
        foreach ($criterion in $Card.acceptanceCriteria) {
            $lines.Add("- $criterion")
        }
        $lines.Add("")
    }

    if ($Card.definitionOfDone -and $Card.definitionOfDone.Count -gt 0) {
        $lines.Add("Definition of Done")
        foreach ($item in $Card.definitionOfDone) {
            $lines.Add("- $item")
        }
        $lines.Add("")
    }

    if ($Card.id) {
        $lines.Add("Bron ID: $($Card.id)")
    }

    return ($lines -join [Environment]::NewLine).Trim()
}

Assert-Value -Value $ApiKey -Name "ApiKey/TRELLO_KEY"
Assert-Value -Value $ApiToken -Name "ApiToken/TRELLO_TOKEN"

if (-not (Test-Path $BoardFile)) {
    throw "BoardFile niet gevonden: $BoardFile"
}

$boardDefinition = Get-Content -Path $BoardFile -Raw | ConvertFrom-Json
$boardData = $boardDefinition.board

$boardQuery = @{
    name = $boardData.name
    desc = $boardData.description
    defaultLists = "false"
}

if (-not [string]::IsNullOrWhiteSpace($WorkspaceId)) {
    $boardQuery.idOrganization = $WorkspaceId
}

$board = Invoke-TrelloApi -Method POST -Path "/boards/" -Query $boardQuery
Write-Host "Board aangemaakt: $($board.name) ($($board.url))"

$labelsByName = @{}
$allLabels = New-Object System.Collections.Generic.HashSet[string]
foreach ($list in $boardData.lists) {
    foreach ($card in $list.cards) {
        if ($card.labels) {
            foreach ($label in $card.labels) {
                [void]$allLabels.Add([string]$label)
            }
        }
    }
}

foreach ($label in $allLabels) {
    $createdLabel = Invoke-TrelloApi -Method POST -Path "/labels" -Query @{
        idBoard = $board.id
        name = $label
        color = (Get-LabelColor -Label $label)
    }
    $labelsByName[$label] = $createdLabel.id
    Write-Host "Label aangemaakt: $label"
}

foreach ($list in $boardData.lists) {
    $createdList = Invoke-TrelloApi -Method POST -Path "/lists" -Query @{
        idBoard = $board.id
        name = $list.name
        pos = "bottom"
    }
    Write-Host "Lijst aangemaakt: $($list.name)"

    foreach ($card in $list.cards) {
        $labelIds = @()
        if ($card.labels) {
            foreach ($label in $card.labels) {
                if ($labelsByName.ContainsKey([string]$label)) {
                    $labelIds += $labelsByName[[string]$label]
                }
            }
        }

        $query = @{
            idList = $createdList.id
            name = $card.title
            desc = (Build-CardDescription -Card $card)
            pos = "bottom"
        }

        if ($labelIds.Count -gt 0) {
            $query.idLabels = ($labelIds -join ",")
        }

        $null = Invoke-TrelloApi -Method POST -Path "/cards" -Query $query
        Write-Host "  Kaart aangemaakt: $($card.title)"
    }
}

Write-Host "Klaar. Bord URL: $($board.url)"

if ($OpenBoard) {
    Start-Process $board.url
}