# ENGIE Allocation Processor API - Setup & Testing Guide

## Quick Start

### 1. Build & Run the API

```powershell
cd c:\Users\loek\engie\engie-v2\src\Engie.Mca.EventHandler
dotnet build
dotnet run
```

The API will start on: **http://localhost:5001**

### 2. Access Swagger Documentation

Once running, visit: **http://localhost:5001**

Interactive API documentation with live testing available at the root URL.

## Postman Collection

### Import Collection

1. Open **Postman**
2. Click: `File` → `Import`
3. Select: `docs/ENGIE-MCA-API-Postman.json`
4. Collection will appear in the left sidebar

### Configure Base URL

The collection automatically sets `base_url = http://localhost:5001`

To change it:
1. Select **ENGIE Allocation Processor API** collection
2. Click **Variables** tab
3. Edit `base_url` value

## Test Scenarios

### Scenario 1: Valid Message (Expected: Delivered + ACK)
**Request:** Send valid AllocationSeries message
- All required fields present
- Valid EAN code: `8714568009996`
- Current date/time
- **Expected Result:** Status=Delivered, ResponseType=Ack, ErrorCount=0

**Postman:** Run request `1. Process Valid AllocationSeries`

### Scenario 2: Invalid EAN (Expected: Failed + NACK + Error 686)
**Request:** Send message with invalid EAN
- EAN format doesn't match BRP register
- **Expected Result:** Status=Failed, ResponseType=Nack, ErrorCodes=[686]
- **Error Code:** 686 = "Ongeldige EAN-code"

**Postman:** Run request `2. Process with Invalid EAN`

### Scenario 3: Future Date (Expected: Failed + NACK + Error 760)
**Request:** Send message with future timestamp
- Timestamp in far future (2099)
- **Expected Result:** Status=Failed, ResponseType=Nack, ErrorCodes=[760]
- **Error Code:** 760 = "Bericht in toekomst"

**Postman:** Run request `3. Process with Future Date`

### Scenario 4: Invalid XML (Expected: Failed + NACK + Error 651)
**Request:** Send malformed XML
- Unclosed tags or invalid syntax
- **Expected Result:** Status=Failed, ResponseType=Nack, ErrorCodes=[651]
- **Error Code:** 651 = "XML kan niet geparst worden"

**Postman:** Run request `6. Process Invalid XML`

## API Endpoints

### Process Message
```
POST /api/messages
Content-Type: application/json

Body:
Gebruik het volledige ENGIE envelope-voorbeeld uit `test-envelope.json`.

Response:
Gebruik als referentie `test-response.json`.
```

Voor lokaal testen:

```powershell
Get-Content .\test-envelope.json -Raw | Invoke-RestMethod `
  -Uri "http://localhost:5001/api/messages" `
  -Method Post `
  -ContentType "application/json"
```

### Get Message Status
```
GET /api/messages/{messageId}

Response:
{
  "messageId": "uuid",
  "status": "Delivered|Failed",
  "responseType": "Ack|Nack",
  "errorCount": 0,
  "errorCodes": [],
  "steps": [
    { "StepId": "1A", "Description": "..." },
    { "StepId": "1B", "Description": "..." },
    ...
    { "StepId": "6B", "Description": "..." }
  ],
  "receivedAt": "2026-03-26T10:00:00Z",
  "processedAt": "2026-03-26T10:00:01Z"
}
```

### Get Messages by Status
```
GET /api/messages/status/{status}

Status values:
- Received
- Processing
- Delivered
- Failed
- Retrying

Response: Array of messages with that status
```

### Get All Messages
```
GET /api/messages

Response: Array of all processed messages with summaries
```

### Reprocess Message
```
POST /api/messages/{messageId}/reprocess

Response: Fresh processing result with new status
```

## Error Codes Reference

| Code | Message | Step | Handling |
|------|---------|------|----------|
| 650  | Ongeldig XML-formaat | 1C | XML validation |
| 651  | XML kan niet geparst worden | 1C | XML parsing |
| 676  | Vereist veld ontbreekt | 3C | Field validation |
| 686  | Ongeldige EAN-code | 3A | BRP register |
| 754  | Ongeldige bericht-sequence | 3F | Sequence check |
| 758  | Bericht buiten geldige periode | 3E | Time window |
| 760  | Bericht in toekomst | 3E | Time window |
| 772  | Negatieve hoeveelheid niet toegestaan | 3D | Quantity validation |
| 780  | Verwerking niet geconfigureerd | 4E | Config check |
| 782  | Configuratie niet geladen | 4E | Config load |
| 999  | Onbekende fout bij verwerking | * | Catch-all |

## Processing Pipeline (29 Steps)

### Column 1: Event Handler (6 steps)
- **1A:** Receive market message
- **1B:** Technical receipt confirmation
- **1C:** Technical XML validation
- **1D:** Log receipt timestamp
- **1E:** Identify message type
- **1F:** Prepare for processing

### Column 2+4: Message Processor (10 steps)
- **2A:** Classify message type
- **2B:** Determine priority
- **2C:** Queue message
- **2D:** Handle exceptions
- **2E:** Retry handling
- **4A:** Generate ACK/NACK
- **4B:** Generate NACK response
- **4C:** Add error codes
- **4D:** Record validation result
- **4E:** Configure NACK sending

### Column 3: Message Validator (7 steps)
- **3A:** BRP register check
- **3B:** Business rule validation
- **3C:** Required field check
- **3D:** Configurable rules
- **3E:** Time window validation
- **3F:** Sequence check
- **3G:** Reusable validation rules

### Column 5: N-ACK Handler (4 steps)
- **5A:** Send ACK/NACK
- **5B:** Configured response
- **5C:** Log send time
- **5D:** Independent sending

### Column 6: Output Handler (2 steps)
- **6A:** Forward to raw-layer
- **6B:** Record final status

## Running Full Test Suite

In Postman:
1. Select collection: **ENGIE Allocation Processor API**
2. Click the **Run** button (play icon)
3. Select all requests
4. Click **Run**

Expected Results:
- ✅ Test 1: Valid message → Delivered + Ack
- ✅ Test 2: Invalid EAN → Failed + Nack (686)
- ✅ Test 3: Future date → Failed + Nack (760)
- ✅ Test 4: Factor series → Delivered
- ✅ Test 5: Aggregated series → Delivered
- ✅ Test 6: Invalid XML → Failed + Nack (651)
- ✅ Test 7-11: Status checks and reprocessing

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    ASP.NET Core Web API                     │
│                    (Swagger UI at /swagger)                 │
├─────────────────────────────────────────────────────────────┤
│ POST /api/messages                                          │
│   └─> PipelineEngine.ProcessAsync()                         │
│       ├─> Column 1: Event Handler (1A-1F)                   │
│       ├─> Column 2+4: Message Processor (2A-2E + 4A-4E)     │
│       ├─> Column 3: Message Validator (3A-3G)              │
│       ├─> Column 5: N-ACK Handler (5A-5D)                  │
│       └─> Column 6: Output Handler (6A-6B)                 │
├─────────────────────────────────────────────────────────────┤
│ GET /api/messages/{id}        → Retrieve stored message     │
│ GET /api/messages/status/{s}  → Filter by status            │
│ GET /api/messages             → List all messages           │
│ POST /api/messages/{id}/reprocess → Retry processing        │
└─────────────────────────────────────────────────────────────┘
```

## In-Memory Storage

Messages are stored in-memory during the session. For production:
- Replace `MessageStore` with database (SQL Server, PostgreSQL)
- Add persistence layer
- Implement audit logging
- Add backup/recovery

## Troubleshooting

### API won't start
```powershell
# Check if port 5000 is in use
netstat -ano | findstr :5000

# Kill process if needed
taskkill /PID <PID> /F

# Try alternate port in appsettings.json
```

### Swagger UI not loading
- Ensure API is running: `dotnet run`
- Navigate to: `http://localhost:5000`
- Check firewall settings

### Postman collection tests fail
- Verify `base_url` is set correctly
- Ensure API is running
- Check error response in Postman console (CMD+Alt+C)
- Review error codes in the response

## Next Steps

1. ✅ Run the API
2. ✅ Import Postman collection
3. ✅ Execute test scenarios
4. ✅ Verify 29-step pipeline
5. ✅ Review error codes and handling
6. ➡️ Integrate with real databases
7. ➡️ Add authentication/authorization
8. ➡️ Deploy to production
