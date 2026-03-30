# Echte Trello Board Aanmaken

Dit project bevat een script dat een echt Trello-bord aanmaakt via de Trello API op basis van:

- [docs/trello-board-api-complete-2026-03.json](docs/trello-board-api-complete-2026-03.json) (nieuw, default)
- [docs/trello-board-api-no-dashboard.json](docs/trello-board-api-no-dashboard.json) (legacy)
- [scripts/create-trello-board.ps1](scripts/create-trello-board.ps1)

## Benodigd

Je hebt nodig:

1. Trello API key
2. Trello API token
3. Optioneel: Trello workspace ID

## Environment variables zetten

```powershell
$env:TRELLO_KEY="jouw-key"
$env:TRELLO_TOKEN="jouw-token"
$env:TRELLO_WORKSPACE_ID="jouw-workspace-id"
```

`TRELLO_WORKSPACE_ID` is optioneel. Zonder die waarde maakt Trello het bord aan onder je standaardaccount.

## Script draaien

```powershell
./scripts/create-trello-board.ps1 -OpenBoard
```

Of expliciet met parameters:

```powershell
./scripts/create-trello-board.ps1 `
  -BoardFile "docs/trello-board-api-complete-2026-03.json" `
  -ApiKey "jouw-key" `
  -ApiToken "jouw-token" `
  -WorkspaceId "jouw-workspace-id" `
  -OpenBoard
```

## Wat het script doet

1. Maakt een nieuw Trello-bord aan
2. Maakt labels aan op basis van `labels`
3. Maakt alle lijsten aan
4. Maakt alle kaarten aan
5. Zet description, acceptance criteria, error codes en bron-ID in de kaartbeschrijving

## Resultaat

Na succesvolle run krijg je de URL van het aangemaakte Trello-bord terug.