# Start all ENGIE Microservices Script
# This script starts all services with explicit local ports and local inter-service URLs.

$basePath = "c:\Users\loek\engie\engie-v2\src"
$services = @(
    @{ Name = "🎯 Event Handler";    Path = "$basePath\Engie.Mca.EventHandler";    Port = 5001 },
    @{ Name = "🔄 Message Processor"; Path = "$basePath\Engie.Mca.MessageProcessor"; Port = 5002 },
    @{ Name = "✓ Message Validator";  Path = "$basePath\Engie.Mca.MessageValidator"; Port = 5003 },
    @{ Name = "✗ N-ACK Handler";      Path = "$basePath\Engie.Mca.NackHandler";      Port = 5004 },
    @{ Name = "📤 Output Handler";    Path = "$basePath\Engie.Mca.OutputHandler";    Port = 5005 }
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "🚀 Starting ENGIE Microservices" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$pids = @()

# Start each service
foreach ($service in $services) {
    Write-Host "Starting: $($service.Name) on port $($service.Port)..." -ForegroundColor Yellow
    
    # Check if project exists
    if (Test-Path "$($service.Path)\$([System.IO.Path]::GetFileName($service.Path)).csproj") {
        $serviceLogPath = Join-Path "c:\Users\loek\engie\engie-v2\logs" ($service.Path.Split('\\')[-1])
        $envBlock = @(
            "$env:ASPNETCORE_URLS=http://localhost:$($service.Port)",
            "$env:LOGS_DIRECTORY=c:\Users\loek\engie\engie-v2\logs\blocks",
            "$env:MESSAGE_PROCESSOR_BASE_URL=http://localhost:5002",
            "$env:MESSAGE_VALIDATOR_BASE_URL=http://localhost:5003",
            "$env:NACK_HANDLER_BASE_URL=http://localhost:5004",
            "$env:OUTPUT_HANDLER_BASE_URL=http://localhost:5005"
        ) -join "; "

        # Start service in background
        $command = "$envBlock; cd '$($service.Path)'; & 'C:\Program Files\dotnet\dotnet.exe' run"
        $process = Start-Process PowerShell -ArgumentList "-NoExit", "-Command", $command -PassThru
        $pids += $process.Id
        Write-Host "   ✓ Started (PID: $($process.Id))" -ForegroundColor Green
        Start-Sleep -Milliseconds 500
    } else {
        Write-Host "   ✗ Project not found at $($service.Path)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "✓ All services started!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Service Status:" -ForegroundColor Cyan
$services | ForEach-Object {
    switch ($_.Port) {
        5001 { Write-Host "  $($_.Name): http://localhost:$($_.Port)/api/event/health" -ForegroundColor Gray }
        5002 { Write-Host "  $($_.Name): http://localhost:$($_.Port)/api/processor/health" -ForegroundColor Gray }
        5003 { Write-Host "  $($_.Name): http://localhost:$($_.Port)/api/validator/health" -ForegroundColor Gray }
        5004 { Write-Host "  $($_.Name): http://localhost:$($_.Port)/api/nack/health" -ForegroundColor Gray }
        5005 { Write-Host "  $($_.Name): http://localhost:$($_.Port)/api/output/health" -ForegroundColor Gray }
    }
}
Write-Host ""
Write-Host "📝 Logs Location:" -ForegroundColor Cyan
Write-Host "  - Event Handler:    c:\Users\loek\engie\engie-v2\logs\event-handler\" -ForegroundColor Gray
Write-Host "  - Message Processor: c:\Users\loek\engie\engie-v2\logs\message-processor\" -ForegroundColor Gray
Write-Host "  - Message Validator: c:\Users\loek\engie\engie-v2\logs\message-validator\" -ForegroundColor Gray
Write-Host "  - N-ACK Handler:    c:\Users\loek\engie\engie-v2\logs\nack-handler\" -ForegroundColor Gray
Write-Host "  - Output Handler:   c:\Users\loek\engie\engie-v2\logs\output-handler\" -ForegroundColor Gray
Write-Host ""
Write-Host "💡 Stuur een bericht:" -ForegroundColor Cyan
Write-Host '  Invoke-WebRequest -Uri "http://localhost:5001/api/messages" -Method Post' -ForegroundColor Gray
Write-Host ""
Write-Host "Type 'exit' in any service window to stop it." -ForegroundColor Yellow
Write-Host ""

# Keep main window open
Write-Host "Press Ctrl+C or close this window to stop monitoring." -ForegroundColor Yellow
$pids | ForEach-Object {
    Wait-Process -Id $_ -ErrorAction SilentlyContinue
}
