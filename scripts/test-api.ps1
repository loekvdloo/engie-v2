$body = @{
    id                       = [guid]::NewGuid().ToString()
    type                     = "mma.msg.new"
    createtime               = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ")
    source                   = "ENTEM"
    msgsender                = "8716867000016"
    msgsenderrole            = "ZV"
    msgreceiver              = "8716800000085"
    msgreceiverrole          = "LV"
    msgtype                  = "AllocationSeries"
    msgsubtype               = "E35"
    msgid                    = "test-valid-001"
    msgcorrelationid         = "test-valid-001"
    msgcreationtime          = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ")
    msgversion               = "4.0"
    msgpayloadid             = [guid]::NewGuid().ToString()
    msgcontenttype           = "application/xml"
    msgpayload               = "<AllocationSeries xmlns='urn:ediel:org:allocation:v4'><DocumentID>DOC-TEST-001</DocumentID><EAN>871456800999612345</EAN><Quantity>100</Quantity><StartDateTime>2026-03-28T08:00:00Z</StartDateTime><EndDateTime>2026-03-28T09:00:00Z</EndDateTime></AllocationSeries>"
    entemsendacknowledgement = $true
    entemsendtooutput        = $true
    entemvalidationresult    = @()
    entemtimestamp           = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ")
} | ConvertTo-Json

$response = Invoke-WebRequest -Uri "http://localhost:5001/api/messages" `
  -Method POST `
  -ContentType "application/json" `
  -Body $body

$response.Content | ConvertFrom-Json | ConvertTo-Json -Depth 3
