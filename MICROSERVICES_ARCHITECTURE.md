# ENGIE MCA - Microservices Architecture

## Overview

The ENGIE Market Code Allocation (MCA) system has been refactored from a monolithic pipeline into a microservices architecture with 6 independent services orchestrated by an API Gateway.

## Architecture Diagram

```
test data
   ↓
[🎪 API Orchestrator] ← Main entry point (http://localhost:5000)
   ↓
   ├→ [🎯 Event Handler (5001)]           Steps 1A-1F: Event capturing & receipt
   ├→ [🔄 Message Processor (5002)]       Steps 2A-2E: Classification & queuing
   ├→ [✓ Message Validator (5003)]        Steps 3A-3G: Validation & rules
   ├→ [🔄 Message Processor (5002)]       Steps 4A-4E: ACK/NACK generation
   ├→ [✗ N-ACK Handler (5004)]            Steps 5A-5D: Response sending
   └→ [📤 Output Handler (5005)]          Steps 6A-6B: Delivery & registration
```

## Services

### 1. API Orchestrator (Port 5000)
**Location**: `src/Engie.Mca.Api`
**Role**: Main gateway that orchestrates all microservices
**Endpoints**:
- `POST /api/messages` - Process a message (calls all services in sequence)
- `GET /api/messages/{id}` - Get message status
- `GET /api/messages/{id}/steps` - Get all processing steps with column mapping
- `GET /api/messages/stats/summary` - Get aggregated statistics
- `GET /api/health` - Service health check

**Responsibility**:
- Receives incoming messages
- Orchestrates calls to all 5 microservices in proper sequence
- Stores results in message store
- Returns final status and response type

### 2. Event Handler Service (Port 5001)
**Location**: `src/Engie.Mca.EventHandler`
**Role**: Column 1 - Initial message capture and validation
**Endpoints**:
- `POST /api/event/handle` - Process event (Steps 1A-1F)
- `GET /api/event/health` - Health check

**Steps Executed**:
- 1A: Receive event
- 1B: Technical receipt confirmation
- 1C: Technical XML validation
- 1D: Log receipt time
- 1E: Identify message type
- 1F: Prepare for processing

### 3. Message Processor Service (Port 5002)
**Location**: `src/Engie.Mca.MessageProcessor`
**Role**: Columns 2 & 4 - Message classification and response generation
**Endpoints**:
- `POST /api/processor/phase2` - Phase 2 processing (Steps 2A-2E)
- `POST /api/processor/phase4` - Phase 4 processing (Steps 4A-4E)
- `GET /api/processor/health` - Health check

**Phase 2 Steps**:
- 2A: Classify message type
- 2B: Determine priority
- 2C: Place in queue
- 2D: Check for parked exceptions
- 2E: Check resending requirements

**Phase 4 Steps**:
- 4A: Generate ACK/NACK message
- 4B: Document any errors
- 4C: Add error codes
- 4D: Register validation result
- 4E: Configure response sending

### 4. Message Validator Service (Port 5003)
**Location**: `src/Engie.Mca.MessageValidator`
**Role**: Column 3 - Message validation and business rules
**Endpoints**:
- `POST /api/validator/validate` - Validate message (Steps 3A-3G)
- `GET /api/validator/health` - Health check

**Steps Executed**:
- 3A: Check EAN code validity
- 3B: Check date/time
- 3C: Check required fields
- 3D: Apply configurable validation rules
- 3E: Check time window
- 3F: Check sequence ordering
- 3G: Apply reusable validation rules

**Error Codes**:
- 686: Invalid EAN code
- Other validation errors as needed

### 5. N-ACK Handler Service (Port 5004)
**Location**: `src/Engie.Mca.NackHandler`
**Role**: Column 5 - ACK/NACK response sending
**Endpoints**:
- `POST /api/nack/send` - Send ACK or NACK response (Steps 5A-5D)
- `GET /api/nack/health` - Health check

**Steps Executed**:
- 5A: Send ACK or NACK message
- 5B: Apply configured response settings
- 5C: Log send time
- 5D: Independent asynchronous delivery

### 6. Output Handler Service (Port 5005)
**Location**: `src/Engie.Mca.OutputHandler`
**Role**: Column 6 - Final output and delivery registration
**Endpoints**:
- `POST /api/output/finalize` - Finalize and register output (Steps 6A-6B)
- `GET /api/output/health` - Health check

**Steps Executed**:
- 6A: Forward to raw-layer destination
- 6B: Register delivery status in system

## Communication Pattern

Services communicate via **HTTP REST API calls** through the orchestrator:

1. Orchestrator receives message on port 5000
2. Orchestrator calls Event Handler (5001)
3. Orchestrator calls Message Processor Phase 2 (5002)
4. Orchestrator calls Message Validator (5003)
5. Orchestrator calls Message Processor Phase 4 (5002)
6. Orchestrator calls N-ACK Handler (5004)
7. Orchestrator calls Output Handler (5005)
8. Orchestrator returns final result to client

## Logging

Each service maintains its own dedicated logs in:
```
c:\Users\loek\engie\engie-v2\logs\
├── event-handler\          (Event Handler logs)
├── message-processor\      (Message Processor logs)
├── message-validator\      (Message Validator logs)
├── nack-handler\           (N-ACK Handler logs)
└── output-handler\         (Output Handler logs)
```

All services use Serilog with:
- Console output for real-time monitoring
- Daily rolling file logs for persistence
- Structured logging with timestamps and message IDs

## Starting Services

### Option 1: Unified Startup Script
```powershell
.\start-all-services.ps1
```

This will start all 6 services in separate PowerShell windows on their respective ports.

### Option 2: Manual Startup
Start each service individually:

```powershell
# Terminal 1: Event Handler
cd src\Engie.Mca.EventHandler
dotnet run

# Terminal 2: Message Processor
cd src\Engie.Mca.MessageProcessor
dotnet run

# Terminal 3: Message Validator
cd src\Engie.Mca.MessageValidator
dotnet run

# Terminal 4: N-ACK Handler
cd src\Engie.Mca.NackHandler
dotnet run

# Terminal 5: Output Handler
cd src\Engie.Mca.OutputHandler
dotnet run

# Terminal 6: API Orchestrator
cd src\Engie.Mca.Api
dotnet run
```

### Option 3: Docker Containers (Future)
Each service can be containerized for deployment:
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY . .
ENTRYPOINT ["dotnet", "run"]
```

## Message Flow

### Valid Message Example
```
POST /api/messages
{
  "messageId": "msg-123",
  "messageType": "AllocationSeries",
  "xmlContent": "<Message><EAN>123456789012</EAN>...</Message>"
}

Response:
{
  "messageId": "msg-123",
  "status": "Delivered",
  "response": "Ack",
  "errorCount": 0,
  "errors": []
}
```

### Invalid Message Example
```
POST /api/messages
{
  "messageId": "msg-456",
  "messageType": "AllocationSeries",
  "xmlContent": "<Message><EAN></EAN>...</Message>"  // Empty EAN
}

Response:
{
  "messageId": "msg-456",
  "status": "Failed",
  "response": "Nack",
  "errorCount": 1,
  "errors": ["686"]  // Invalid EAN code
}
```

## Monitoring

### Health Checks
Each service exposes a health endpoint:

```powershell
# Check Event Handler
curl http://localhost:5001/api/event/health

# Check Message Processor
curl http://localhost:5002/api/processor/health

# Check Validator
curl http://localhost:5003/api/validator/health

# Check N-ACK Handler
curl http://localhost:5004/api/nack/health

# Check Output Handler
curl http://localhost:5005/api/output/health

# Check Orchestrator
curl http://localhost:5000/api/health
```

### View Logs
```powershell
# Event Handler logs
Get-Content "c:\Users\loek\engie\engie-v2\logs\event-handler\event-handler-.log" -Tail 50

# Message Processor logs
Get-Content "c:\Users\loek\engie\engie-v2\logs\message-processor\processor-.log" -Tail 50

# And so on...
```

## Testing with Postman

An updated Postman collection with all microservice endpoints is available in:
`docs/ENGIE-MCA-API-Postman.json`

Import and test:
- Valid message processing end-to-end
- Invalid message error handling
- Step tracking per message
- Statistics aggregation
- Individual service health checks

## Future Enhancements

1. **Service Discovery**: Add Consul or Kubernetes for dynamic service registration
2. **API Gateway**: Implement Kong or Azure API Management
3. **Message Queue**: Add RabbitMQ or Azure Service Bus for async communication
4. **Database**: Replace memory store with SQL Server/PostgreSQL
5. **Containerization**: Docker + Docker Compose or Kubernetes
6. **Monitoring**: Application Insights or Prometheus/Grafana
7. **Circuit Breaker**: Implement Polly for resilience
8. **API Versioning**: Semantic versioning for backward compatibility

## Performance Considerations

- **Sequential Execution**: Services called in defined order (no parallelization yet)
- **Average Processing Time**: ~70-80ms for valid messages
- **Error Handling**: Graceful degradation with proper error codes
- **Scalability**: Each service can be scaled independently
- **Isolation**: Service failure doesn't cascade to others

## Database Strategy

Currently using **shared in-memory store** for message persistence. 

For production, recommend:
- Separate database per service (micro database pattern)
- Or shared database with separate schemas per service
- Message store in SQL Server/PostgreSQL with audit trail

## Security (Future)

- API Gateway authentication (OAuth2/JWT)
- Service-to-service authentication
- API rate limiting
- Input validation and sanitization
- Encrypted inter-service communication (TLS)

