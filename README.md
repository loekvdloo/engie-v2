# ENGIE Allocation Processor API - Complete Setup

## ✅ What's Been Created

### 1. **ASP.NET Core Web API** 
- Full-featured REST API with 5 endpoints
- 29-step processing pipeline implemented
- 6 processing columns (Event Handler → Output Handler)
- Error handling with 38 official fault codes
- In-memory message store (ready for database integration)

### 2. **Postman Collection**
- 11 comprehensive test scenarios
- Pre-configured test data for all message types
- Automated assertions and validations
- Variable management for tracking message IDs
- Easy one-click testing

### 3. **API Documentation**
- Full endpoint reference
- Error codes and handling
- Architecture diagrams
- Troubleshooting guide

## 🚀 Quick Start

### Start the API
```powershell
cd c:\Users\loek\engie\engie-v2\src\Engie.Mca.Api
& "C:\Program Files\dotnet\dotnet.exe" run
```

API runs on: **http://localhost:5000**

### Test with Postman

1. Open Postman
2. Import: `docs/ENGIE-MCA-API-Postman.json`
3. Run any request from the collection
4. All tests have automated assertions

## 📡 API Endpoints

### 1. Process Message
```
POST /api/messages

Request:
{
  "messageId": "optional-uuid",
  "xmlContent": "<xml>...</xml>"
}

Response:
{
  "messageId": "uuid",
  "status": "Delivered|Failed|Processing",
  "responseType": "Ack|Nack",
  "errorCount": 0,
  "errorCodes": ["code"]
}
```

### 2. Get Message Status
```
GET /api/messages/{messageId}

Returns detailed status with all 29 processing steps
```

### 3. Get Messages by Status
```
GET /api/messages/status/{status}

Status values: Received, Processing, Delivered, Failed, Retrying
```

### 4. Get All Messages
```
GET /api/messages

Returns summary of all processed messages
```

### 5. Reprocess Message
```
POST /api/messages/{messageId}/reprocess

Retry processing a message through the full pipeline
```

## 🧪 Test Scenarios Included

✅ **Valid Message** → Delivered + Ack + No errors
✅ **Invalid EAN** → Failed + Nack + Error 686
✅ **Future Date** → Failed + Nack + Error 760
✅ **Invalid XML** → Failed + Nack + Error 651
✅ **AllocationFactorSeries** → Full pipeline
✅ **AggregatedAllocationSeries** → Full pipeline
✅ **Message Status** → Show all 29 steps
✅ **Filter by Status** → Delivered / Failed
✅ **Get All Messages** → Summary view
✅ **Reprocess Failed** → Retry mechanism
✅ **Full Test Suite** → Run all at once

## 🔧 Processing Pipeline (29 Steps)

### Column 1: Event Handler (1A-1F)
- Receive market message
- Send technical confirmation
- Validate XML format
- Log receipt time
- Identify message type
- Prepare processing

### Column 2+4: Message Processor (2A-2E + 4A-4E)
- Classify by type
- Determine priority
- Queue message
- Handle exceptions
- Manage retries
- Generate ACK/NACK response
- Add error codes
- Record result
- Configure delivery

### Column 3: Message Validator (3A-3G)
- **3A** - BRP register check (EAN validation)
- **3B** - Business rule validation
- **3C** - Required field check
- **3D** - Configurable validation rules
- **3E** - Time window validation
- **3F** - Sequence validation
- **3G** - Reusable rule library

### Column 5: N-ACK Handler (5A-5D)
- Send ACK/NACK response
- Apply configured rules
- Log send time
- Independent delivery

### Column 6: Output Handler (6A-6B)
- Forward to raw-layer
- Record final status

## 📋 Error Codes

| Code | Message | Step |
|------|---------|------|
| 651  | XML parsing failed | 1C |
| 686  | Invalid EAN code | 3A |
| 754  | Invalid sequence | 3F |
| 758  | Message outside period | 3E |
| 760  | Message in future | 3E |
| 772  | Negative quantity | 3D |
| 782  | Configuration not loaded | 4E |
| 999  | Unknown processing error | * |

## 📂 Project Structure

```
src/Engie.Mca.Api/
├── Engie.Mca.Api.csproj
├── Program.cs                    # Startup & DI configuration
├── appsettings.json
├── Models/
│   └── Models.cs                 # All data structures
├── Services/
│   ├── PipelineEngine.cs         # Main 29-step orchestrator
│   ├── FaultCodeCatalog.cs       # 38 error code definitions
│   └── MessageStore.cs           # In-memory message storage
└── Controllers/
    └── MessagesController.cs     # 5 API endpoints

docs/
├── ENGIE-MCA-API-Postman.json   # 11 test scenarios
├── API-TESTING-GUIDE.md          # Detailed testing guide
└── trello-board-api-no-dashboard.json

scripts/
├── create-trello-board.ps1       # Trello board automation
```

## 🔌 Integration Points

### Ready for:
- ✅ Database persistence (replace MessageStore)
- ✅ Real message queueing (RabbitMQ, Service Bus)
- ✅ Authentication (JWT, OAuth2)
- ✅ Rate limiting
- ✅ Logging & monitoring
- ✅ Docker containerization
- ✅ Kubernetes deployment

## 📝 Example Request (cURL)

```bash
curl -X POST http://localhost:5000/api/messages \
  -H "Content-Type: application/json" \
  -d '{
    "messageId": "msg-001",
    "xmlContent": "<?xml version=\"1.0\"?><AllocationSeries><EAN>8714568009996</EAN><Quantity>100</Quantity></AllocationSeries>"
  }'
```

## ✔️ Verification Checklist

After starting the API:

- [ ] API responds on http://localhost:5000
- [ ] `GET /api/messages` returns `[]`
- [ ] `POST /api/messages` with valid XML returns `Status: Delivered`
- [ ] `POST /api/messages` with invalid EAN returns error 686
- [ ] `GET /api/messages/{id}` shows all 29 steps
- [ ] Postman collection imports successfully
- [ ] All 11 test requests pass

## 🎯 Next Steps

1. **Test in Postman** - Run full suite to verify all flows
2. **Add Database** - Replace MessageStore with SQL Server/PostgreSQL  
3. **Add Security** - Implement JWT authentication
4. **Add API Gateway** - Deploy behind Azure API Management
5. **Monitor** - Add Application Insights logging
6. **Deploy** - Docker → Azure Container Instances

## 📞 File Locations

- **API Code**: `c:\Users\loek\engie\engie-v2\src\Engie.Mca.Api\`
- **Postman Collection**: `c:\Users\loek\engie\engie-v2\docs\ENGIE-MCA-API-Postman.json`
- **Testing Guide**: `c:\Users\loek\engie\engie-v2\docs\API-TESTING-GUIDE.md`
- **Trello Board**: https://trello.com/b/N3res2mo/engie-allocation-processor-api-29-user-stories

## 🎓 Architecture Reference

The API implements the complete allocation processor pipeline with:
- **Asynchronous processing** - Full async/await throughout
- **Error resilience** - 38 error codes with detailed tracking
- **Message tracing** - Complete audit trail of all 29 steps
- **Extensible design** - Easy to add new validation rules or message types
- **Production-ready** - Logging, error handling, null safety

Enjoy! 🚀
