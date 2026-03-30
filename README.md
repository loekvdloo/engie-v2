# ENGIE MCA Microservices

Uitgebreide .NET 10 / ASP.NET Core microservices demo voor ENGIE MCA berichtverwerking.

Het systeem verwerkt marktberichten via een orchestrated pipeline van 29 stappen, verdeeld over 6 services, inclusief:

- end-to-end correlatie (X-Correlation-ID)
- verrijkte gestructureerde logging
- centrale metrics endpoints
- contract tests
- natural fault-code tests
- volledige fault-code dekking via geforceerde foutcodes
- load test script

## Inhoud

- Architectuur
- Services en poorten
- Functionele flow (29 stappen)
- Starten en stoppen
- API endpoints
- Postman collectie
- Trello board definitie
- Logging en observability
- Fault codes en validatiebereik
- Teststrategie en run-instructies
- Troubleshooting
- Uitgebreide mapstructuur

## Architectuur

Het landschap bestaat uit 1 API orchestrator en 5 worker services.

1. API Orchestrator ontvangt het bericht
2. EventHandler doet intake stappen (kolom 1)
3. MessageProcessor doet pre-processing (kolom 2)
4. MessageValidator voert validatieregels uit (kolom 3)
5. MessageProcessor maakt ACK of NACK (kolom 2+4)
6. NackHandler verstuurt NACK-flow (kolom 5)
7. OutputHandler zet output door (kolom 6)

De API houdt context, status en processing steps bij en exposeert status-, step- en metrics endpoints.

Zie ook: MICROSERVICES_ARCHITECTURE.md

## Services en poorten

- API Orchestrator: 5000
- EventHandler: 5001
- MessageProcessor: 5002
- MessageValidator: 5003
- NackHandler: 5004
- OutputHandler: 5005

## Functionele flow (29 stappen)

Het systeem logt en bewaart een complete chain van 29 processtappen per bericht:

- Kolom 1: Event Handler (1A-1F)
- Kolom 2: Message Processor fase 2 (2A-2E)
- Kolom 3: Message Validator (3A-3G)
- Kolom 2+4: Message Processor fase 4 (4A-4E)
- Kolom 5: Nack Handler (5A-5D)
- Kolom 6: Output Handler (6A-6B)

## Vereisten

- Windows + PowerShell
- .NET SDK 10
- Toegang tot dotnet executable:

```powershell
"C:\Program Files\dotnet\dotnet.exe"
```

Als dotnet niet op PATH staat, gebruik altijd het volledige pad.

## Starten en stoppen

### Alles starten via script

```powershell
.\scripts\start-all-services.ps1
```

### Alles handmatig starten

Gebruik per service een aparte terminal:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" run --project ".\src\Engie.Mca.EventHandler\Engie.Mca.EventHandler.csproj"
& "C:\Program Files\dotnet\dotnet.exe" run --project ".\src\Engie.Mca.MessageProcessor\Engie.Mca.MessageProcessor.csproj"
& "C:\Program Files\dotnet\dotnet.exe" run --project ".\src\Engie.Mca.MessageValidator\Engie.Mca.MessageValidator.csproj"
& "C:\Program Files\dotnet\dotnet.exe" run --project ".\src\Engie.Mca.NackHandler\Engie.Mca.NackHandler.csproj"
& "C:\Program Files\dotnet\dotnet.exe" run --project ".\src\Engie.Mca.OutputHandler\Engie.Mca.OutputHandler.csproj"
& "C:\Program Files\dotnet\dotnet.exe" run --project ".\src\Engie.Mca.Api\Engie.Mca.Api.csproj"
```

### Handig: oude processen eerst stoppen

Als je lock-fouten krijgt op apphost.exe of service .exe bestanden:

```powershell
Get-Process Engie.Mca.Api,Engie.Mca.EventHandler,Engie.Mca.MessageProcessor,Engie.Mca.MessageValidator,Engie.Mca.NackHandler,Engie.Mca.OutputHandler -ErrorAction SilentlyContinue | Stop-Process -Force
```

## API endpoints

Base URL:

```text
http://localhost:5000
```

### Berichten

- POST /api/messages
- GET /api/messages/{messageId}
- GET /api/messages/{messageId}/steps
- GET /api/messages/status/{status}
- GET /api/messages
- GET /api/messages/stats/summary
- POST /api/messages/{messageId}/reprocess

### Metrics

- GET /api/metrics
- GET /metrics

### Request voorbeeld

```json
{
  "messageId": "optional-id",
  "correlationId": "optional-correlation-id",
  "xmlContent": "<AllocationSeries>...</AllocationSeries>"
}
```

### Header voor correlatie

```text
X-Correlation-ID: corr-001
```

### Response voorbeeld

```json
{
  "messageId": "msg-001",
  "correlationId": "corr-001",
  "status": "Delivered",
  "responseType": "Ack",
  "errorCount": 0,
  "errorCodes": []
}
```

## Postman collectie

Gebruik de bijgewerkte collectie:

- docs/ENGIE-MCA-API-Postman.json

Aanbevolen collection variables:

- base_url = http://localhost:5000

De collectie bevat onder andere:

- geldige end-to-end verwerking (ACK)
- stap 1 fouten (001 via Event Handler)
- stap 3 foutcodes (676, 686, 758, 760, 772, 773, 774, 755)
- read endpoints (/api/messages, /api/messages/{id}, /steps)
- metrics endpoints

## Trello board definitie

Bijgewerkte boarddefinities:

- docs/trello-board-api-no-dashboard.json (bestaand)
- docs/trello-board-api-complete-2026-03.json (nieuw, compleet en up-to-date)

Nieuw bord maken via API:

```powershell
./scripts/create-trello-board.ps1 -BoardFile "docs/trello-board-api-complete-2026-03.json" -OpenBoard
```

## Logging en observability

### Logbestanden

- logs/pipeline-YYYYMMDD.log
- logs/event-handler/event-handler-YYYYMMDD.log
- logs/message-processor/processor-YYYYMMDD.log
- logs/message-validator/validator-YYYYMMDD.log
- logs/nack-handler/nack-YYYYMMDD.log
- logs/output-handler/output-YYYYMMDD.log
- logs/blocks/block1-event-handler-YYYYMMDD.log
- logs/blocks/block2-4-message-processor-YYYYMMDD.log
- logs/blocks/block3-message-validator-YYYYMMDD.log
- logs/blocks/block5-nack-handler-YYYYMMDD.log
- logs/blocks/block6-output-handler-YYYYMMDD.log
- logs/blocks/all-blocks-YYYYMMDD.log

### Gestructureerde velden

De gecombineerde block logs bevatten minimaal:

- BlockCode
- CorrelationId
- MessageId
- MessageType
- ResponseType
- ErrorCodes

### Metrics inhoud

Centrale metrics (JSON en Prometheus-style) bevatten onder andere:

- totalMessages
- deliveredMessages
- failedMessages
- ackMessages
- nackMessages
- successRate
- averageProcessingDurationMs
- p95ProcessingDurationMs
- errorsByCode
- messagesByStatus
- messagesByType

## Fault codes en validatie

De catalog bevat 33 fault codes in:

- src/Engie.Mca.Api/Services/FaultCodeCatalog.cs

### Natural validatieregel dekking

Realistische rules dekken op dit moment expliciet:

- 676 required field missing
- 686 invalid EAN
- 755 duplicate document ID heuristic
- 758 invalid/out-of-window time range
- 760 future dated message
- 772 negative quantity
- 773 quantity above limit
- 774 zero quantity

### Volledige catalogusdekking

Volledige deterministische dekking van alle 33 codes loopt via ForceErrorCodes in de testflow.

## Teststrategie

Deze repo gebruikt meerdere testlagen, elk met eigen doel:

1. Contract tests: snel API contract valideren zonder live worker dependency
2. API smoke scripts: basis endpointgedrag en stappen ophalen
3. Natural fault-code tests: realistische validatiepaden
4. All fault-code tests: volledige codecatalog dekking
5. Logging tests: controle op block logging en combined log
6. Load test: eenvoudige performance en latency smoke

## Testen draaien

### 1) Contract tests

```powershell
& "C:\Program Files\dotnet\dotnet.exe" test ".\tests\Engie.Mca.Contracts.Tests\Engie.Mca.Contracts.Tests.csproj"
```

### 2) Live stack starten

```powershell
.\scripts\start-all-services.ps1
```

### 3) Live scripts

```powershell
.\scripts\test-api.ps1
.\scripts\test-all-steps.ps1
.\scripts\test-with-logging.ps1
.\scripts\test-block-logging.ps1
.\scripts\test-natural-faultcodes.ps1
.\scripts\test-all-faultcodes.ps1
.\scripts\test-full-pipeline.ps1
.\scripts\load-test-api.ps1 -TotalRequests 100 -Concurrency 10
.\scripts\view-messages.ps1
```

### Nieuwe scripts

- scripts/test-full-pipeline.ps1
  Doet een complete 14-case run tegen de actuele implementatie en valideert verwachte status + foutcodes.

- scripts/view-messages.ps1
  Toont overzicht van verwerkte berichten of detail incl. stappen per messageId.

## Aanbevolen daily workflow

Voor kleine wijzigingen:

1. contract test
2. test-api
3. test-natural-faultcodes

Voor grotere wijzigingen of release-check:

1. contract test
2. volledige live scripts
3. load test
4. handmatige check van logs/blocks/all-blocks-YYYYMMDD.log
5. handmatige check van /api/metrics

## Troubleshooting

### dotnet wordt niet gevonden

Gebruik expliciet:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" --info
```

### Lock fout op apphost.exe of service .exe

Oorzaak: service draait nog.
Oplossing: stop bestaande processen en start opnieuw.

### /api/health geeft 404

Niet elke service gebruikt exact hetzelfde health pad. Gebruik de service-specifieke endpoints of test direct met een script.

### Te veel gewijzigde bestanden in Git

De meeste zijn gegenereerde bestanden (bin/obj en test output). Voeg een .gitignore toe als die nog ontbreekt.

## Uitgebreide mapstructuur

Onderstaande structuur toont de functionele layout. Build artifacts (bin/obj) zijn functioneel minder relevant en horen meestal niet in Git.

```text
engie-v2/
|-- .git/
|-- MICROSERVICES_ARCHITECTURE.md
|-- README.md
|
|-- docs/
|   |-- API-TESTING-GUIDE.md
|   |-- ENGIE-MCA-API-Postman.json
|   |-- trello-board-api-no-dashboard.json
|   |-- trello-board-api-complete-2026-03.json
|   `-- trello-board-import-guide.md
|
|-- scripts/
|   |-- create-trello-board.ps1
|   |-- start-all-services.ps1
|   |-- test-api.ps1
|   |-- test-all-steps.ps1
|   |-- test-with-logging.ps1
|   |-- test-block-logging.ps1
|   |-- test-natural-faultcodes.ps1
|   |-- test-all-faultcodes.ps1
|   |-- test-full-pipeline.ps1
|   |-- view-messages.ps1
|   `-- load-test-api.ps1
|
|-- src/
|   |-- Engie.Mca.Api/
|   |   |-- appsettings.json
|   |   |-- Engie.Mca.Api.csproj
|   |   |-- Program.cs
|   |   |-- Controllers/
|   |   |-- Models/
|   |   |-- Services/
|   |   |-- bin/                  (generated)
|   |   `-- obj/                  (generated)
|   |
|   |-- Engie.Mca.EventHandler/
|   |   |-- Engie.Mca.EventHandler.csproj
|   |   |-- Program.cs
|   |   |-- Controllers/
|   |   |-- bin/                  (generated)
|   |   `-- obj/                  (generated)
|   |
|   |-- Engie.Mca.MessageProcessor/
|   |   |-- Engie.Mca.MessageProcessor.csproj
|   |   |-- Program.cs
|   |   |-- Controllers/
|   |   |-- bin/                  (generated)
|   |   `-- obj/                  (generated)
|   |
|   |-- Engie.Mca.MessageValidator/
|   |   |-- Engie.Mca.MessageValidator.csproj
|   |   |-- Program.cs
|   |   |-- Controllers/
|   |   |-- bin/                  (generated)
|   |   `-- obj/                  (generated)
|   |
|   |-- Engie.Mca.NackHandler/
|   |   |-- Engie.Mca.NackHandler.csproj
|   |   |-- Program.cs
|   |   |-- Controllers/
|   |   |-- bin/                  (generated)
|   |   `-- obj/                  (generated)
|   |
|   |-- Engie.Mca.OutputHandler/
|   |   |-- Engie.Mca.OutputHandler.csproj
|   |   |-- Program.cs
|   |   |-- Controllers/
|   |   |-- bin/                  (generated)
|   |   `-- obj/                  (generated)
|   |
|   `-- logs/
|
|-- tests/
|   `-- Engie.Mca.Contracts.Tests/
|       |-- Engie.Mca.Contracts.Tests.csproj
|       |-- ContractWebApplicationFactory.cs
|       |-- MessagesContractTests.cs
|       |-- bin/                  (generated)
|       `-- obj/                  (generated)
|
`-- logs/
    |-- pipeline-YYYYMMDD.log
    |-- event-handler/
    |-- message-processor/
    |-- message-validator/
    |-- nack-handler/
    |-- output-handler/
    `-- blocks/
        |-- all-blocks-YYYYMMDD.log
        |-- block1-event-handler-YYYYMMDD.log
        |-- block2-4-message-processor-YYYYMMDD.log
        |-- block3-message-validator-YYYYMMDD.log
        |-- block5-nack-handler-YYYYMMDD.log
        `-- block6-output-handler-YYYYMMDD.log
```

## Status van de huidige implementatie

De stack is gevalideerd op:

- succesvolle build van alle csproj projecten
- contract tests geslaagd
- natural fault-code tests geslaagd
- all fault-code tests geslaagd
- load test geslaagd
- metrics endpoint live bevestigd

## Mogelijke vervolgstappen

- meer natural fault-code paden implementeren
- persistente opslag toevoegen in plaats van uitsluitend in-memory store
- CI pipeline toevoegen voor contract + live smoke + load smoke
- standaard .gitignore opnemen voor bin/obj/log output
