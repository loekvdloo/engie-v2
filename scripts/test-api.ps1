$body = @{
    messageId = "test-valid-001"
    xmlContent = '<?xml version="1.0" encoding="UTF-8"?><AllocationSeries><Header><MessageId>test-001</MessageId><Timestamp>2026-03-26T10:00:00Z</Timestamp></Header><EAN>8714568009996</EAN><Quantity>100</Quantity></AllocationSeries>'
} | ConvertTo-Json

$response = Invoke-WebRequest -Uri "http://localhost:5000/api/messages" `
  -Method POST `
  -ContentType "application/json" `
  -Body $body

$response.Content | ConvertFrom-Json | ConvertTo-Json -Depth 3
