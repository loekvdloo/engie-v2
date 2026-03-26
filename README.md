# ENGIE MCA Microservices

ASP.NET Core/.NET 10 microservices demo for processing ENGIE MCA market messages through a 29-step pipeline.

## Overview

The workspace contains six services:

- API Orchestrator on port 5000
- EventHandler on port 5001
- MessageProcessor on port 5002
- MessageValidator on port 5003
- NackHandler on port 5004
- OutputHandler on port 5005

Messages are submitted to the API and then flow through all six blocks. Each block writes its own log file and also writes to a shared combined log.

## Implemented Features

- 29-step microservice pipeline
- Combined block logging in logs/blocks/all-blocks-YYYYMMDD.log
- Block tags in logs: api, eh, mp, mv, nh, oh
- End-to-end correlation IDs via X-Correlation-ID
- Message metrics on /api/metrics and /metrics
- Deterministic all-fault-code testing via ForceErrorCodes
- Natural validation tests for real validator rules
- Contract tests for the API surface without live worker dependencies
- Load test script for basic concurrency and latency checks

## Current Validation Scope

The fault code catalog contains 33 codes in [src/Engie.Mca.Api/Services/FaultCodeCatalog.cs](src/Engie.Mca.Api/Services/FaultCodeCatalog.cs).

Natural validation rules currently cover these codes:

- 676 required field missing
- 686 invalid EAN
- 755 duplicate document ID heuristic
- 758 invalid or out-of-window time range
- 760 future dated message
- 772 negative quantity
- 773 quantity above limit
- 774 zero quantity

Full 33-code coverage is available through the existing ForceErrorCodes test hook.

## Quick Start

### Start all services

```powershell
& .\scripts\start-all-services.ps1
```

### Or start services manually

```powershell
& "C:\Program Files\dotnet\dotnet.exe" run --project ".\src\Engie.Mca.EventHandler\Engie.Mca.EventHandler.csproj"
& "C:\Program Files\dotnet\dotnet.exe" run --project ".\src\Engie.Mca.MessageProcessor\Engie.Mca.MessageProcessor.csproj"
& "C:\Program Files\dotnet\dotnet.exe" run --project ".\src\Engie.Mca.MessageValidator\Engie.Mca.MessageValidator.csproj"
& "C:\Program Files\dotnet\dotnet.exe" run --project ".\src\Engie.Mca.NackHandler\Engie.Mca.NackHandler.csproj"
& "C:\Program Files\dotnet\dotnet.exe" run --project ".\src\Engie.Mca.OutputHandler\Engie.Mca.OutputHandler.csproj"
& "C:\Program Files\dotnet\dotnet.exe" run --project ".\src\Engie.Mca.Api\Engie.Mca.Api.csproj"
```

### Service URLs

- API: http://localhost:5000
- EventHandler health: http://localhost:5001/api/event/health
- MessageProcessor health: http://localhost:5002/api/processor/health
- MessageValidator health: http://localhost:5003/api/validator/health
- NackHandler health: http://localhost:5004/api/nack/health
- OutputHandler health: http://localhost:5005/api/output/health

## API Endpoints

### Process message

```text
POST /api/messages
```

Request body:

```json
{
  "messageId": "optional-id",
  "correlationId": "optional-correlation-id",
  "xmlContent": "<AllocationSeries>...</AllocationSeries>"
}
```

Response body:

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

Optional request header:

```text
X-Correlation-ID: corr-001
```

### Get message status

```text
GET /api/messages/{messageId}
```

Returns processing status, error codes, steps, timestamps and processing duration.

### Get message steps

```text
GET /api/messages/{messageId}/steps
```

### Get messages by status

```text
GET /api/messages/status/{status}
```

### Get all messages

```text
GET /api/messages
```

### Statistics summary

```text
GET /api/messages/stats/summary
```

### Reprocess message

```text
POST /api/messages/{messageId}/reprocess
```

### JSON metrics

```text
GET /api/metrics
```

### Prometheus-style metrics

```text
GET /metrics
```

## Logging

Log files are written to:

- logs/pipeline-YYYYMMDD.log for API-level logs
- logs/blocks/block1-event-handler-YYYYMMDD.log
- logs/blocks/block2-4-message-processor-YYYYMMDD.log
- logs/blocks/block3-message-validator-YYYYMMDD.log
- logs/blocks/block5-nack-handler-YYYYMMDD.log
- logs/blocks/block6-output-handler-YYYYMMDD.log
- logs/blocks/all-blocks-YYYYMMDD.log for the combined cross-service view

Combined log lines now include:

- block code
- correlation ID
- message ID
- message type
- response type
- error code list

## Testing

### Contract tests

```powershell
& "C:\Program Files\dotnet\dotnet.exe" test ".\tests\Engie.Mca.Contracts.Tests\Engie.Mca.Contracts.Tests.csproj"
```

### Natural validation tests

```powershell
& .\scripts\test-natural-faultcodes.ps1
```

### Full fault code coverage

```powershell
& .\scripts\test-all-faultcodes.ps1
```

### Load test

```powershell
& .\scripts\load-test-api.ps1 -TotalRequests 100 -Concurrency 10
```

### Other useful scripts

- scripts/test-api.ps1
- scripts/test-all-steps.ps1
- scripts/test-block-logging.ps1
- scripts/test-with-logging.ps1

## Example Request

```powershell
$headers = @{
    "Content-Type" = "application/json"
    "X-Correlation-ID" = "demo-correlation-001"
}

$body = @{
    messageId = "demo-001"
    xmlContent = "<AllocationSeries><DocumentID>DOC-001</DocumentID><EAN>8712345678901</EAN><Quantity>100</Quantity><StartDateTime>2026-03-20T10:00:00Z</StartDateTime><EndDateTime>2026-03-20T11:00:00Z</EndDateTime></AllocationSeries>"
} | ConvertTo-Json

Invoke-RestMethod -Method Post -Uri "http://localhost:5000/api/messages" -Headers $headers -Body $body
```

## Project Structure

```text
src/
  Engie.Mca.Api/
  Engie.Mca.EventHandler/
  Engie.Mca.MessageProcessor/
  Engie.Mca.MessageValidator/
  Engie.Mca.NackHandler/
  Engie.Mca.OutputHandler/

scripts/
  start-all-services.ps1
  test-all-faultcodes.ps1
  test-natural-faultcodes.ps1
  load-test-api.ps1

tests/
  Engie.Mca.Contracts.Tests/

docs/
  ENGIE-MCA-API-Postman.json
  API-TESTING-GUIDE.md
```

## Verification Status

The current repo state has been validated with:

- successful build of all csproj files
- successful contract tests
- successful natural validation script run
- successful live metrics response from the API

## Next Logical Extensions

- make more catalog codes naturally triggerable
- add durable persistence instead of in-memory storage
- expose richer Prometheus metrics per service
- add CI execution for contract, natural and load smoke tests
