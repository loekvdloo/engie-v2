
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Engie.Mca.EventHandler.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EventController : ControllerBase
{
    private static readonly HashSet<string> KnownMessageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AllocationSeries",
        "AllocationFactorSeries",
        "AggregatedAllocationSeries"
    };

    private readonly ILogger<EventController> _logger;

    public EventController(ILogger<EventController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Handle incoming message event
    /// Steps: 1A-1F (Technical receipt, validation, type identification)
    /// </summary>
    [HttpPost("handle")]
    public async Task<IActionResult> HandleEvent([FromBody] EventHandlerRequest request)
    {
        var receivedAt = DateTime.UtcNow;
        var messageId = request?.MessageId;

        using var messageIdScope = LogContext.PushProperty("MessageId", messageId ?? "(none)");
        using var messageTypeScope = LogContext.PushProperty("MessageType", request?.MessageType ?? "(none)");

        _logger.LogInformation("[{MessageId}] === COLUMN 1: EVENT HANDLER (Steps 1A-1F) ===", messageId);

        try
        {
            // Step 1A: Ontvang event — check request aanwezig en MessageId ingevuld
            if (request == null)
            {
                _logger.LogWarning("[?] ✗ Step 1A: Request body ontbreekt volledig");
                return BadRequest(new { step = "1A", error = "Request body is leeg of null" });
            }
            if (string.IsNullOrWhiteSpace(request.MessageId))
            {
                _logger.LogWarning("[?] ✗ Step 1A: MessageId ontbreekt");
                return BadRequest(new { step = "1A", error = "MessageId is verplicht" });
            }
            _logger.LogInformation("[{MessageId}] ✓ Step 1A: Ontvang event — request aanwezig, MessageId: {MessageId}", messageId, messageId);
            await Task.Delay(5);

            // Step 1B: Technische ontvangstbevestiging — check alle verplichte JSON-velden
            var missingFields = new List<string>();
            if (string.IsNullOrWhiteSpace(request.MessageType)) missingFields.Add("MessageType");
            if (string.IsNullOrWhiteSpace(request.Content))     missingFields.Add("Content");

            if (missingFields.Count > 0)
            {
                _logger.LogWarning("[{MessageId}] ✗ Step 1B: Verplichte velden ontbreken: {Fields}", messageId, string.Join(", ", missingFields));
                return BadRequest(new { step = "1B", error = "Verplichte JSON-velden ontbreken", missingFields });
            }
            _logger.LogInformation("[{MessageId}] ✓ Step 1B: Technische ontvangstbevestiging — alle velden aanwezig", messageId);
            await Task.Delay(5);

            // Step 1C: Technische validatie XML — daadwerkelijk parsen als XML
            XDocument xmlDoc;
            try
            {
                xmlDoc = XDocument.Parse(request.Content);
            }
            catch (Exception xmlEx)
            {
                _logger.LogWarning("[{MessageId}] ✗ Step 1C: XML parse mislukt: {Error}", messageId, xmlEx.Message);
                return BadRequest(new { step = "1C", error = "Content is geen geldige XML", detail = xmlEx.Message });
            }

            if (xmlDoc.Root == null)
            {
                _logger.LogWarning("[{MessageId}] ✗ Step 1C: XML heeft geen root-element", messageId);
                return BadRequest(new { step = "1C", error = "XML heeft geen root-element" });
            }
            _logger.LogInformation("[{MessageId}] ✓ Step 1C: XML geldig — root-element: <{RootElement}>", messageId, xmlDoc.Root.Name.LocalName);
            await Task.Delay(5);

            // Step 1D: Logging van ontvangsttijd
            _logger.LogInformation("[{MessageId}] ✓ Step 1D: Ontvangstijd vastgelegd: {ReceivedAt:O}", messageId, receivedAt);
            await Task.Delay(5);

            // Step 1E: Berichttype identificeren — check tegen bekende types
            if (!KnownMessageTypes.Contains(request.MessageType))
            {
                _logger.LogWarning("[{MessageId}] ✗ Step 1E: Onbekend berichttype: {MessageType}. Geldige types: {KnownTypes}",
                    messageId, request.MessageType, string.Join(", ", KnownMessageTypes));
                return BadRequest(new
                {
                    step = "1E",
                    error = $"Onbekend berichttype: {request.MessageType}",
                    knownTypes = KnownMessageTypes
                });
            }
            _logger.LogInformation("[{MessageId}] ✓ Step 1E: Berichttype geïdentificeerd: {MessageType}", messageId, request.MessageType);
            await Task.Delay(5);

            // Step 1F: Bereid verwerking voor — check root-element komt overeen met berichttype
            var rootName = xmlDoc.Root.Name.LocalName;
            if (!rootName.Contains(request.MessageType, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[{MessageId}] ✗ Step 1F: Root-element <{RootElement}> komt niet overeen met berichttype {MessageType}",
                    messageId, rootName, request.MessageType);
                return BadRequest(new
                {
                    step = "1F",
                    error = $"Root-element <{rootName}> komt niet overeen met opgegeven berichttype {request.MessageType}"
                });
            }
            _logger.LogInformation("[{MessageId}] ✓ Step 1F: Bereid verwerking voor — root <{RootElement}> matcht {MessageType}",
                messageId, rootName, request.MessageType);
            await Task.Delay(5);

            return Ok(new
            {
                messageId,
                stepsCompleted = 6,
                status = "EventHandled",
                receivedAt,
                identifiedMessageType = request.MessageType,
                xmlRootElement = xmlDoc.Root.Name.LocalName,
                nextService = "MessageProcessor"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{MessageId}] Event handling failed", messageId);
            return BadRequest(new { step = "unknown", error = ex.Message });
        }
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { service = "EventHandler", status = "healthy" });
    }

    [HttpGet("/dashboard")]
    [Produces("text/html")]
    public ContentResult Dashboard()
    {
        var html = """
<!DOCTYPE html>
<html lang="nl">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>ENGIE MCA — Live Dashboard</title>
<script>
/* Inline minimal Chart.js polyfill — replaced by real CDN when available */
window._chartFallback = true;
</script>
<script src="https://cdn.jsdelivr.net/npm/chart.js@4/dist/chart.umd.min.js" onerror="window._chartFallback=true" onload="window._chartFallback=false"></script>
<style>
  :root {
    --bg: #0f1117; --card: #1a1d27; --border: #2a2d3e;
    --accent: #00c896; --warn: #f59e0b; --danger: #ef4444;
    --text: #e2e8f0; --muted: #6b7280;
  }
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body { background: var(--bg); color: var(--text); font-family: 'Segoe UI', sans-serif; padding: 24px; }
  header { display: flex; align-items: center; gap: 12px; margin-bottom: 28px; }
  header h1 { font-size: 1.4rem; font-weight: 700; }
  .badge { background: var(--accent); color: #000; font-size: .7rem; font-weight: 700;
           padding: 2px 8px; border-radius: 999px; }
  .pulse { width: 10px; height: 10px; border-radius: 50%; background: var(--accent);
           animation: pulse 1.5s infinite; }
  @keyframes pulse { 0%,100%{opacity:1;transform:scale(1)} 50%{opacity:.4;transform:scale(1.4)} }
  .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 16px; margin-bottom: 24px; }
  .kpi { background: var(--card); border: 1px solid var(--border); border-radius: 12px;
         padding: 20px 18px; }
  .kpi .label { font-size: .72rem; color: var(--muted); text-transform: uppercase; letter-spacing: .05em; }
  .kpi .value { font-size: 2rem; font-weight: 800; margin-top: 6px; }
  .kpi.green .value { color: var(--accent); }
  .kpi.amber .value { color: var(--warn); }
  .kpi.red   .value  { color: var(--danger); }
  .kpi.blue  .value  { color: #60a5fa; }
  .charts { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; margin-bottom: 24px; }
  @media(max-width:720px) { .charts { grid-template-columns: 1fr; } }
  .chart-card { background: var(--card); border: 1px solid var(--border); border-radius: 12px; padding: 20px; }
  .chart-card h2 { font-size: .85rem; color: var(--muted); margin-bottom: 14px; text-transform: uppercase; letter-spacing:.05em; }
  canvas { max-height: 220px; }
  .errors-card { background: var(--card); border: 1px solid var(--border); border-radius: 12px; padding: 20px; }
  .errors-card h2 { font-size: .85rem; color: var(--muted); margin-bottom: 14px; text-transform: uppercase; letter-spacing:.05em; }
  table { width: 100%; border-collapse: collapse; font-size: .83rem; }
  th { text-align: left; color: var(--muted); font-weight: 600; padding: 6px 10px; border-bottom: 1px solid var(--border); }
  td { padding: 8px 10px; border-bottom: 1px solid var(--border); }
  tr:last-child td { border: none; }
  .bar-bg { background: var(--border); border-radius: 4px; height: 6px; }
  .bar-fill { background: var(--danger); border-radius: 4px; height: 6px; transition: width .4s; }
  footer { margin-top: 20px; font-size: .72rem; color: var(--muted); text-align: right; }
  #last-update { color: var(--muted); font-size: .72rem; }
</style>
</head>
<body>
<header>
  <div class="pulse" id="statusDot"></div>
  <h1>ENGIE MCA &mdash; Live Dashboard</h1>
  <span class="badge">LIVE</span>
  <span id="last-update" style="margin-left:auto"></span>
</header>

<div class="grid">
  <div class="kpi blue">  <div class="label">Totaal berichten</div><div class="value" id="kpi-total">—</div></div>
  <div class="kpi green"> <div class="label">Afgeleverd (ACK)</div><div class="value" id="kpi-ack">—</div></div>
  <div class="kpi red">   <div class="label">Gefaald (NACK)</div><div class="value" id="kpi-nack">—</div></div>
  <div class="kpi green"> <div class="label">Successrate</div><div class="value" id="kpi-rate">—</div></div>
  <div class="kpi amber"> <div class="label">Gem. duur (ms)</div><div class="value" id="kpi-avg">—</div></div>
  <div class="kpi amber"> <div class="label">P95 duur (ms)</div><div class="value" id="kpi-p95">—</div></div>
</div>

<div class="charts">
  <div class="chart-card">
    <h2>ACK vs NACK</h2>
    <canvas id="chartDonut"></canvas>
  </div>
  <div class="chart-card">
    <h2>Successrate over tijd (%)</h2>
    <canvas id="chartLine"></canvas>
  </div>
</div>

<div class="errors-card">
  <h2>Foutcodes</h2>
  <table id="errTable">
    <thead><tr><th>Code</th><th>Omschrijving</th><th>Aantal</th><th>% van totaal</th></tr></thead>
    <tbody id="errBody"><tr><td colspan="4" style="color:var(--muted);text-align:center">Geen fouten geregistreerd</td></tr></tbody>
  </table>
</div>

<footer>Ververst elke 3 seconden &bull; <a href="/api/metrics" style="color:var(--accent)">JSON endpoint</a> &bull; EventHandler lokaal op poort 8081</footer>

<script>
const rateHistory = { labels: [], data: [] };
let donut = null, lineChart = null;

function initCharts() {
  if (typeof Chart === 'undefined') {
    document.querySelectorAll('.chart-card').forEach(el => {
      el.innerHTML = '<p style="color:var(--muted);font-size:.8rem;padding:20px">Grafieken niet beschikbaar (geen CDN-verbinding)</p>';
    });
    return;
  }
  const donutCtx = document.getElementById('chartDonut').getContext('2d');
  donut = new Chart(donutCtx, {
    type: 'doughnut',
    data: { labels: ['ACK', 'NACK'], datasets: [{ data: [0,0],
      backgroundColor: ['#00c896','#ef4444'], borderWidth: 0, hoverOffset: 6 }] },
    options: { plugins: { legend: { labels: { color: '#e2e8f0' } } }, cutout: '70%' }
  });
  const lineCtx = document.getElementById('chartLine').getContext('2d');
  lineChart = new Chart(lineCtx, {
    type: 'line',
    data: { labels: [], datasets: [{ label: 'Successrate %', data: [],
      borderColor: '#00c896', backgroundColor: 'rgba(0,200,150,.12)',
      tension: .35, fill: true, pointRadius: 3, pointBackgroundColor: '#00c896' }] },
    options: { scales: {
      x: { ticks: { color:'#6b7280', maxTicksLimit:8 }, grid: { color:'#2a2d3e' } },
      y: { min:0, max:100, ticks: { color:'#6b7280' }, grid: { color:'#2a2d3e' } }
    }, plugins: { legend: { labels: { color:'#e2e8f0' } } } }
  });
}

function fmt(n) { return n == null ? '—' : Number(n).toLocaleString('nl-NL'); }
function fmtPct(n) { return n == null ? '—' : Number(n).toFixed(1) + '%'; }

async function refresh() {
  try {
    const m = await fetch('/api/metrics').then(r => r.json());

    document.getElementById('kpi-total').textContent = fmt(m.totalMessages);
    document.getElementById('kpi-ack').textContent   = fmt(m.ackMessages);
    document.getElementById('kpi-nack').textContent  = fmt(m.nackMessages);
    document.getElementById('kpi-rate').textContent  = fmtPct(m.successRate);
    document.getElementById('kpi-avg').textContent   = fmt(Math.round(m.averageProcessingDurationMs));
    document.getElementById('kpi-p95').textContent   = fmt(Math.round(m.p95ProcessingDurationMs));

    if (donut) {
      donut.data.datasets[0].data = [m.ackMessages || 0, m.nackMessages || 0];
      donut.update();
    }

    const now = new Date().toLocaleTimeString('nl-NL');
    rateHistory.labels.push(now);
    rateHistory.data.push(Number(m.successRate || 0).toFixed(1));
    if (rateHistory.labels.length > 30) { rateHistory.labels.shift(); rateHistory.data.shift(); }
    if (lineChart) {
      lineChart.data.labels = rateHistory.labels;
      lineChart.data.datasets[0].data = rateHistory.data;
      lineChart.update();
    }

    const errors = m.errorsByCode || [];
    const totalErrors = errors.reduce((a,e) => a + (e.count || 0), 0) || 1;
    const tbody = document.getElementById('errBody');
    if (errors.length === 0) {
      tbody.innerHTML = '<tr><td colspan="4" style="color:var(--muted);text-align:center">Geen fouten geregistreerd</td></tr>';
    } else {
      tbody.innerHTML = errors.map(e => {
        const pct = ((e.count || 0) / totalErrors * 100);
        const barW = Math.max(2, Math.round(pct));
        return `<tr>
          <td><strong>${e.code || e.errorCode}</strong></td>
          <td style="color:var(--muted)">${e.description || ''}</td>
          <td>${fmt(e.count)}</td>
          <td style="white-space:nowrap"><div class="bar-bg" style="display:inline-block;width:80px;vertical-align:middle"><div class="bar-fill" style="width:${barW}%"></div></div> <span style="font-size:.75rem;color:var(--muted)">${pct.toFixed(1)}%</span></td>
        </tr>`;
      }).join('');
    }

    document.getElementById('statusDot').style.background = '#00c896';
    document.getElementById('last-update').textContent = 'Bijgewerkt: ' + now;
  } catch (err) {
    document.getElementById('statusDot').style.background = '#ef4444';
    document.getElementById('last-update').textContent = 'Verbinding verbroken: ' + err.message;
  }
}

window.addEventListener('load', () => { initCharts(); refresh(); setInterval(refresh, 3000); });
</script>
</body>
</html>
""";
        return new ContentResult { Content = html, ContentType = "text/html", StatusCode = 200 };
    }
}

public class EventHandlerRequest
{
    public string MessageId { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
