$body = @{
    messageId = "test-valid-001"
    xmlContent = '<AllocationSeries><DocumentID>DOC-TEST-001</DocumentID><EAN>871456800999612345</EAN><Quantity>100</Quantity><StartDateTime>2026-03-28T08:00:00Z</StartDateTime><EndDateTime>2026-03-28T09:00:00Z</EndDateTime></AllocationSeries>'
} | ConvertTo-Json

$response = Invoke-WebRequest -Uri "http://localhost:5001/api/messages" `
  -Method POST `
  -ContentType "application/json" `
  -Body $body

$response.Content | ConvertFrom-Json | ConvertTo-Json -Depth 3
