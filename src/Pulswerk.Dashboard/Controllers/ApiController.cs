using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Pulswerk.Core;
using Pulswerk.Drivers;
using Pulswerk.Storage;
using Pulswerk.Billing;
using Pulswerk.Ems;

namespace Pulswerk.Dashboard.Controllers
{
    [ApiController]
    [Route("plswk/api")]
    public partial class ApiController : ControllerBase, Microsoft.AspNetCore.Mvc.Filters.IActionFilter
    {
        private readonly DashboardDataService _data;
        private readonly DashboardStore _store;
        private readonly BillingStore _billing;

        public ApiController(DashboardDataService data, DashboardStore store, BillingStore billing)
        {
            _data = data;
            _store = store;
            _billing = billing;
        }

        [NonAction]
        public void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
        {
            var path = HttpContext.Request.Path.Value;
            if (path == null) return;

            var modules = _data.Config.Modules ?? new ModulesConfig();

            // 1. Gate OCPP Wallbox endpoints
            if (path.Contains("/api/wallboxes", StringComparison.OrdinalIgnoreCase))
            {
                if (!modules.Wallbox || !DashboardAuth.CanAccessWallbox(HttpContext, _data.Config.Server))
                {
                    context.Result = StatusCode(403, "Wallbox access is disabled or unauthorized.");
                    return;
                }
            }

            // 2. Gate Billing endpoints
            if (path.Contains("/api/billing", StringComparison.OrdinalIgnoreCase))
            {
                if (!modules.Billing || !DashboardAuth.CanAccessBilling(HttpContext, _data.Config.Server))
                {
                    context.Result = StatusCode(403, "Billing access is disabled or unauthorized.");
                    return;
                }
            }

            // 3. Gate EMS / Trajectory endpoints
            if (path.Contains("/api/trajectory", StringComparison.OrdinalIgnoreCase))
            {
                if (!modules.Ems || !DashboardAuth.CanAccessEms(HttpContext, _data.Config.Server))
                {
                    context.Result = StatusCode(403, "EMS access is disabled or unauthorized.");
                    return;
                }
            }

            // 4. Gate Historical Data CRUD endpoints
            if (path.Contains("/api/telemetry/data", StringComparison.OrdinalIgnoreCase))
            {
                if (!modules.HistoricalData || !DashboardAuth.CanAccessHistoricalData(HttpContext, _data.Config.Server))
                {
                    context.Result = StatusCode(403, "Historical data access is disabled or unauthorized.");
                    return;
                }
            }

            // 5. Gate Alarms endpoints
            if (path.Contains("/api/alarm", StringComparison.OrdinalIgnoreCase))
            {
                if (!modules.Alarms || !DashboardAuth.CanAccessAlarms(HttpContext, _data.Config.Server))
                {
                    context.Result = StatusCode(403, "Alarms access is disabled or unauthorized.");
                    return;
                }
            }

            // 6. Gate Logs endpoints
            if (path.Contains("/api/logs", StringComparison.OrdinalIgnoreCase))
            {
                if (!modules.Logs || !DashboardAuth.CanAccessLogs(HttpContext, _data.Config.Server))
                {
                    context.Result = StatusCode(403, "Logs access is disabled or unauthorized.");
                    return;
                }
            }

            // 7. Gate Heartbeat endpoints
            if (path.Contains("/api/heartbeat", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("/api/health-history", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("/api/connection-health", StringComparison.OrdinalIgnoreCase))
            {
                if (!modules.Heartbeat || !DashboardAuth.CanAccessHeartbeat(HttpContext, _data.Config.Server))
                {
                    context.Result = StatusCode(403, "Heartbeat access is disabled or unauthorized.");
                    return;
                }
            }

            // 8. Gate Dashboards endpoints
            if (path.Contains("/api/dashboards", StringComparison.OrdinalIgnoreCase))
            {
                if (!modules.Dashboards || !DashboardAuth.CanAccessDashboards(HttpContext, _data.Config.Server))
                {
                    context.Result = StatusCode(403, "Dashboards access is disabled or unauthorized.");
                    return;
                }
            }

            // 9. Gate Assets endpoints
            if (path.Contains("/api/tree", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("/api/properties", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("/api/write", StringComparison.OrdinalIgnoreCase))
            {
                if (!modules.Assets || !DashboardAuth.CanAccessAssets(HttpContext, _data.Config.Server))
                {
                    context.Result = StatusCode(403, "Assets access is disabled or unauthorized.");
                    return;
                }
            }

            // 10. Gate Telemetry endpoints (excluding historical data CRUD which has its own path)
            if (path.Contains("/api/telemetry-keys", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("/api/telemetries", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("/api/widget-data", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("/api/latest-value", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("/api/history", StringComparison.OrdinalIgnoreCase))
            {
                if (!path.Contains("/api/telemetry/data", StringComparison.OrdinalIgnoreCase))
                {
                    if (!modules.Telemetry || !DashboardAuth.CanAccessTelemetry(HttpContext, _data.Config.Server))
                    {
                        context.Result = StatusCode(403, "Telemetry access is disabled or unauthorized.");
                        return;
                    }
                }
            }

            // 11. Gate Connections endpoints
            if (path.Contains("/api/connections", StringComparison.OrdinalIgnoreCase))
            {
                if (!modules.Connections || !DashboardAuth.CanAccessConnections(HttpContext, _data.Config.Server))
                {
                    context.Result = StatusCode(403, "Connections access is disabled or unauthorized.");
                    return;
                }
            }
        }

        [NonAction]
        public void OnActionExecuted(Microsoft.AspNetCore.Mvc.Filters.ActionExecutedContext context)
        {
        }

        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            var alarmCount = _data.AlarmStore.CountActive();
            var status = new DeviceStatusDto
            {
                TotalDevices = _data.Config.Devices.Count,
                OnlineDevices = _data.Config.Devices.Count - _data.OfflineDevices.Count,
                OfflineDevices = _data.OfflineDevices.Count,
                ActiveAlarms = alarmCount,
                ConnectorVersion = _data.Version,
                Version = _data.Version,
                UptimeSeconds = (long)_data.Uptime.Elapsed.TotalSeconds,
                LogBufferSize = _data.LogBuffer.Count,
                LogBufferCapacity = _data.LogBuffer.Capacity,
                Timestamp = DateTime.UtcNow.ToString("o")
            };

            return Ok(status);
        }

        [HttpGet("devices")]
        public IActionResult GetDevices()
        {
            var devices = _data.Config.Devices.Select(d =>
            {
                bool isOffline = _data.OfflineDevices.ContainsKey(d.Name);
                _data.LastPolledAtMap.TryGetValue(d.Name, out var lastPolled);
                var connCfg = _data.Config.Connections.FirstOrDefault(c => c.Id == d.ConnectionId);

                return new DeviceDto
                {
                    Name = d.Name,
                    Type = d.DeviceType,
                    ConnectionId = d.ConnectionId ?? "",
                    Status = isOffline ? "offline" : "online",
                    StatusColor = isOffline ? "#ef4444" : "#10b981",
                    LastSeen = lastPolled == default ? "Never" : lastPolled.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                    Connection = connCfg?.Address ?? "unknown",
                    Port = connCfg?.Port ?? 0
                };
            }).ToList();

            return Ok(devices);
        }

        [HttpGet("telemetry-keys")]
        public IActionResult GetTelemetryKeys()
        {
            var serverCfg = _data.Config.Server;
            if (!DashboardAuth.CanEditConfig(HttpContext, serverCfg))
                return StatusCode(403);

            var keys = _data.GetAvailableTelemetries();
            return Ok(keys);
        }

        [HttpGet("logs")]
        public IActionResult GetLogs([FromQuery] int count = 200, [FromQuery] string? level = null)
        {
            int c = Math.Min(count, 5000);
            var allLogs = _data.LogBuffer.GetAll();

            if (!string.IsNullOrEmpty(level) && level != "all")
            {
                if (level == "info")
                {
                    allLogs = allLogs.Where(l => l.Severity != LogSeverity.Debug).ToList();
                }
                else if (level == "warning")
                {
                    allLogs = allLogs.Where(l => l.Severity == LogSeverity.Warning || l.Severity == LogSeverity.Error).ToList();
                }
                else if (level == "error")
                {
                    allLogs = allLogs.Where(l => l.Severity == LogSeverity.Error).ToList();
                }
            }

            var logs = allLogs.OrderByDescending(l => l.Timestamp)
                              .Take(c)
                              .Reverse()
                              .Select(l => new LogEntryDto
                              {
                                  Timestamp = l.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fffZ"),
                                  Severity = l.Severity.ToString().ToLowerInvariant(),
                                  Message = l.Message,
                                  Source = l.Source
                              }).ToList();

            return Ok(logs);
        }

        [HttpGet("alarm")]
        public IActionResult GetAlarms()
        {
            var alarms = _data.AlarmStore.GetAllActive();
            var dtos = alarms.Select(a => new AlarmDisplayDto
            {
                AlarmId = a.Id,
                Type = a.Type,
                Severity = a.Severity,
                Status = a.Status,
                Message = a.Message,
                Originator = a.Originator,
                Time = DateTimeOffset.FromUnixTimeMilliseconds(a.CreatedAt).ToString("o"),
                AckComment = a.AckComment,
                BacnetAckKey = a.BacnetAckKey
            }).ToList();

            return Ok(dtos);
        }

        [HttpGet("connection-health/{connId}")]
        public IActionResult GetConnectionHealth(string connId)
        {
            var history = _data.GetConnectionHealth(connId);
            var dto = history.Select(h => new
            {
                t = h.Time.ToString("o"),
                online = h.Online,
                total = h.Total
            }).ToList();

            return Ok(dto);
        }

        [HttpGet("health-history")]
        public IActionResult GetHealthHistory()
        {
            var history = _data.GetHealthHistory();
            return Ok(history);
        }

        [HttpGet("latest-value/{key}")]
        public IActionResult GetLatestValue(string key)
        {
            var values = _data.GetCurrentValues(new List<string> { key });
            return Ok(values);
        }

        private class SseSubscription
        {
            public List<string> Keys { get; set; } = new();
            public DateTime ExpiresAt { get; set; }
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SseSubscription> _sseSubscriptions = new();

        public class SseSubscriptionRequestDto
        {
            [JsonPropertyName("keys")]
            public List<string> Keys { get; set; } = new();
        }

        public class KeysRequestDto
        {
            [JsonPropertyName("keys")]
            public List<string> Keys { get; set; } = new();
        }

        [HttpPost("sse/subscribe")]
        public IActionResult SubscribeSse([FromBody] SseSubscriptionRequestDto request)
        {
            if (request == null || request.Keys == null || request.Keys.Count == 0)
            {
                return BadRequest("Keys are required");
            }

            var now = DateTime.UtcNow;
            foreach (var kvp in _sseSubscriptions)
            {
                if (kvp.Value.ExpiresAt < now)
                {
                    _sseSubscriptions.TryRemove(kvp.Key, out _);
                }
            }

            var subscriptionId = Guid.NewGuid().ToString("N");
            _sseSubscriptions[subscriptionId] = new SseSubscription
            {
                Keys = request.Keys,
                ExpiresAt = now.AddMinutes(15)
            };

            return Ok(new { subscriptionId });
        }

        [HttpGet("sse")]
        public async Task GetSse([FromQuery] string? keys, [FromQuery] string? subscriptionId)
        {
            Response.Headers.Append("Content-Type", "text/event-stream");
            Response.Headers.Append("Cache-Control", "no-cache");
            Response.Headers.Append("Connection", "keep-alive");
            Response.Headers.Append("X-Accel-Buffering", "no");

            // Flush headers immediately so the browser/proxy recognizes the SSE stream setup
            await Response.Body.FlushAsync(HttpContext.RequestAborted);

            var channel = System.Threading.Channels.Channel.CreateUnbounded<Dictionary<string, string>>();

            HashSet<string>? requestedKeys = null;

            if (!string.IsNullOrEmpty(subscriptionId))
            {
                if (_sseSubscriptions.TryGetValue(subscriptionId, out var sub))
                {
                    sub.ExpiresAt = DateTime.UtcNow.AddHours(2);
                    requestedKeys = new HashSet<string>(sub.Keys.Select(k => k.Trim()));
                }
                else
                {
                    Log.Warning($"[Server] SSE subscriptionId '{subscriptionId}' not found or expired.");
                    Response.StatusCode = StatusCodes.Status410Gone;
                    await Response.WriteAsync("Subscription not found or expired.");
                    return;
                }
            }

            if (requestedKeys == null && !string.IsNullOrEmpty(keys))
            {
                requestedKeys = new HashSet<string>(keys.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(k => k.Trim()));
            }

            // Write initial state immediately so client gets first value instantly
            if (requestedKeys != null)
            {
                var initialValues = _data.GetCurrentValues(requestedKeys.ToList());
                if (initialValues.Count > 0)
                {
                    channel.Writer.TryWrite(initialValues);
                }
            }
            else
            {
                var allMeta = _data.GetAvailableTelemetries();
                var allKeys = allMeta.Select(m => m.Key).ToList();
                var initialValues = _data.GetCurrentValues(allKeys);
                if (initialValues.Count > 0)
                {
                    channel.Writer.TryWrite(initialValues);
                }
            }

            void HandleUpdate(Dictionary<string, string> values)
            {
                if (requestedKeys != null)
                {
                    var filtered = values.Where(kv => requestedKeys.Contains(kv.Key))
                                         .ToDictionary(kv => kv.Key, kv => kv.Value);
                    if (filtered.Count > 0)
                    {
                        channel.Writer.TryWrite(filtered);
                    }
                }
                else
                {
                    channel.Writer.TryWrite(values);
                }
            }

            _data.OnTelemetriesUpdated += HandleUpdate;

            try
            {
                // Send a keepalive comment every 15s so that proxies, load balancers,
                // and the browser keep the SSE connection alive even when no telemetry
                // updates flow for a long time. This also lets the client detect a
                // dead connection faster (stale detection on the client side).
                Task<Dictionary<string, string>>? readTask = null;
                while (!HttpContext.RequestAborted.IsCancellationRequested)
                {
                    readTask ??= channel.Reader.ReadAsync(HttpContext.RequestAborted).AsTask();

                    var delayTask = Task.Delay(TimeSpan.FromSeconds(15), HttpContext.RequestAborted);
                    var completed = await Task.WhenAny(readTask, delayTask);

                    if (completed == readTask)
                    {
                        var values = await readTask;
                        readTask = null;
                        var json = JsonSerializer.Serialize(values, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                        await Response.WriteAsync($"data: {json}\n\n", HttpContext.RequestAborted);
                        await Response.Body.FlushAsync(HttpContext.RequestAborted);
                    }
                    else
                    {
                        // SSE comment — ignored by EventSource clients but keeps the
                        // TCP connection alive through proxies and prevents timeouts.
                        await Response.WriteAsync($": keepalive {DateTime.UtcNow:O}\n\n", HttpContext.RequestAborted);
                        await Response.Body.FlushAsync(HttpContext.RequestAborted);
                    }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                _data.OnTelemetriesUpdated -= HandleUpdate;
            }
        }

        [HttpPost("client-error")]
        public async Task<IActionResult> ReportClientError()
        {
            try
            {
                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync();
                
                // Limit log payload size to prevent OOM
                if (body.Length > 65536)
                {
                    body = body.Substring(0, 65536);
                }

                using var doc = JsonDocument.Parse(body);
                var err = doc.RootElement;

                string msg = "unknown";
                if (err.TryGetProperty("msg", out var m))
                {
                    msg = m.ValueKind == JsonValueKind.String ? (m.GetString() ?? "unknown") : m.GetRawText();
                }

                string src = "";
                if (err.TryGetProperty("source", out var s))
                {
                    src = s.ValueKind == JsonValueKind.String ? (s.GetString() ?? "") : s.GetRawText();
                }

                string line = "?";
                if (err.TryGetProperty("line", out var l))
                {
                    if (l.ValueKind == JsonValueKind.Number && l.TryGetInt32(out var ln)) line = ln.ToString();
                    else if (l.ValueKind == JsonValueKind.String) line = l.GetString() ?? "?";
                    else line = l.GetRawText();
                }

                string col = "?";
                if (err.TryGetProperty("col", out var c))
                {
                    if (c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var cl)) col = cl.ToString();
                    else if (c.ValueKind == JsonValueKind.String) col = c.GetString() ?? "?";
                    else col = c.GetRawText();
                }

                string stack = "";
                if (err.TryGetProperty("stack", out var st))
                {
                    stack = st.ValueKind == JsonValueKind.String ? (st.GetString() ?? "") : st.GetRawText();
                }

                string page = "";
                if (err.TryGetProperty("page", out var p))
                {
                    page = p.ValueKind == JsonValueKind.String ? (p.GetString() ?? "") : p.GetRawText();
                }

                Log.Warning($"[UI] JS Error on {page} at {src}:{line}:{col} — {msg}");
                if (!string.IsNullOrEmpty(stack))
                    Log.Warning($"[UI]   Stack: {stack.Replace("\n", " | ")}");
            }
            catch { /* don't fail on malformed reports */ }

            return Ok();
        }

        [HttpGet("user")]
        public IActionResult GetUser()
        {
            var serverCfg = _data.Config.Server;
            var authCfg = serverCfg?.Auth;

            string? user = DashboardAuth.GetUser(HttpContext, authCfg);
            var groups = DashboardAuth.GetGroups(HttpContext, authCfg);

            var headers = Request.Headers;
            string? name = headers["Remote-Name"].FirstOrDefault();
            string? email = headers["Remote-Email"].FirstOrDefault();

            var dto = new
            {
                authenticated = !string.IsNullOrWhiteSpace(user) && user != authCfg?.DefaultUser,
                isDefault = !string.IsNullOrWhiteSpace(user) && user == authCfg?.DefaultUser,
                user = user ?? "public",
                name = name ?? user ?? "Public",
                email = email ?? "",
                groups = groups,
                canWriteValue = DashboardAuth.CanWriteValue(HttpContext, serverCfg),
                canAckAlarm = DashboardAuth.CanAckAlarm(HttpContext, serverCfg),
                canEditDashboard = DashboardAuth.CanEditDashboard(HttpContext, serverCfg),
                canEditFavorites = DashboardAuth.CanEditFavorites(HttpContext, serverCfg),
                version = _data.Version
            };

            return Ok(dto);
        }

        [HttpGet("config")]
        public IActionResult GetConfig()
        {
            var serverCfg = _data.Config.Server;
            if (!DashboardAuth.CanEditConfig(HttpContext, serverCfg))
                return StatusCode(403);

            string baseConfigPath = ResolveConfigPath() ?? "";
            string baseJson = System.IO.File.Exists(baseConfigPath) ? System.IO.File.ReadAllText(baseConfigPath) : "{}";

            string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
            string overridePath = Path.Combine(dataDir, "pulswerk.override.json");
            string overrideJson = System.IO.File.Exists(overridePath) ? System.IO.File.ReadAllText(overridePath) : "{}";

            try
            {
                var options = new JsonSerializerOptions
                {
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };
                var baseObj = JsonSerializer.Deserialize<AppConfig>(baseJson, options);
                var overrideObj = JsonSerializer.Deserialize<AppConfig>(overrideJson, options);

                var result = new
                {
                    @base = baseObj,
                    @override = overrideObj ?? new AppConfig(null, null, null, new(), new(), null),
                    version = _data.Version
                };
                return Ok(result);
            }
            catch (Exception ex)
            {
                return Problem("Failed to parse config: " + ex.Message);
            }
        }

        [HttpPost("config/override")]
        public async Task<IActionResult> SaveConfigOverride()
        {
            var serverCfg = _data.Config.Server;
            if (!DashboardAuth.CanEditConfig(HttpContext, serverCfg))
                return StatusCode(403);

            try
            {
                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync();

                var options = new JsonSerializerOptions
                {
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };
                var newOverride = JsonSerializer.Deserialize<AppConfig>(body, options);
                if (newOverride == null) return BadRequest("Invalid JSON configuration");

                string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
                if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
                string overridePath = Path.Combine(dataDir, "pulswerk.override.json");

                var writeOpts = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                string saveJson = JsonSerializer.Serialize(newOverride, writeOpts);
                System.IO.File.WriteAllText(overridePath, saveJson);

                // Update the in-memory config immediately so sidebar gating and
                // /api/user/identity reflect the new module state without a restart.
                _data.UpdateModules(newOverride.Modules);

                return Ok();
            }
            catch (Exception ex)
            {
                Log.Error($"[Server] Failed to save config override: {ex.Message}");
                return Problem(ex.Message);
            }
        }

        [HttpPost("config/evaluate-formula")]
        public async Task<IActionResult> EvaluateFormula()
        {
            var serverCfg = _data.Config.Server;
            if (!DashboardAuth.CanEditConfig(HttpContext, serverCfg))
                return StatusCode(403);

            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            var req = JsonSerializer.Deserialize<JsonElement>(body);

            string formula = req.TryGetProperty("formula", out var f) ? f.GetString() ?? "" : "";
            string deviceId = req.TryGetProperty("deviceId", out var d) ? d.GetString() ?? "" : "";

            DeviceConfig? dev = _data.Config.Devices.FirstOrDefault(x => x.Id == deviceId);

            try
            {
                var method = _data.GetType().GetMethod("GetLiveValueForFormula", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (method != null)
                {
                    string? val = method.Invoke(_data, new object?[] { formula, null, dev }) as string;
                    return Ok(new { result = val ?? "", success = true });
                }
            }
            catch (Exception ex)
            {
                return Ok(new { error = ex.Message, success = false });
            }
            return Ok(new { error = "Evaluation not available", success = false });
        }

        [HttpGet("dashboards")]
        public IActionResult GetDashboards()
        {
            var dashboards = _store.GetAll();
            var result = dashboards.Select(d => new
            {
                id = d.Id,
                name = d.Name,
                description = d.Description,
                createdAt = d.CreatedAt,
                updatedAt = d.UpdatedAt,
                widgets = d.Widgets.Select(w => new
                {
                    id = w.Id,
                    type = w.Type,
                    title = w.Title
                }).ToList()
            }).ToList();
            return Ok(result);
        }

        [HttpGet("dashboards/{id}")]
        public IActionResult GetDashboard(string id)
        {
            var dash = _store.GetById(id);
            if (dash == null) return NotFound();
            return Ok(dash);
        }

        [HttpPost("dashboards")]
        public IActionResult CreateDashboard([FromBody] CreateDashboardRequestDto req)
        {
            if (!DashboardAuth.CanEditDashboard(HttpContext, _data.Config.Server))
                return StatusCode(403);

            if (req == null || string.IsNullOrWhiteSpace(req.Name))
                return BadRequest("Name is required");

            var dash = _store.Create(req.Name, req.Description ?? "");
            return Ok(dash);
        }

        [HttpPost("dashboards/save")]
        public IActionResult SaveDashboard([FromBody] DashboardDefinition dash)
        {
            if (!DashboardAuth.CanEditDashboard(HttpContext, _data.Config.Server))
                return StatusCode(403);

            if (dash == null || string.IsNullOrEmpty(dash.Id))
                return BadRequest("Invalid dashboard");

            bool ok = _store.Save(dash);
            return Ok(new { success = ok });
        }

        [HttpPost("dashboards/delete")]
        public IActionResult DeleteDashboard([FromBody] DeleteDashboardRequestDto req)
        {
            if (!DashboardAuth.CanEditDashboard(HttpContext, _data.Config.Server))
                return StatusCode(403);

            if (req == null || string.IsNullOrEmpty(req.Id))
                return BadRequest("Invalid ID");

            bool ok = _store.Delete(req.Id);
            return Ok(new { success = ok });
        }

        [HttpGet("telemetries")]
        public IActionResult GetTelemetries([FromQuery] string? keys, [FromQuery] bool includeLiveValues = false)
        {
            var all = _data.GetAvailableTelemetries(includeLiveValues);
            if (!string.IsNullOrEmpty(keys))
            {
                var requestedKeys = new HashSet<string>(keys.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(k => k.Trim()));
                var filtered = all.Where(t => requestedKeys.Contains(t.Key)).ToList();
                return Ok(filtered);
            }
            return Ok(all);
        }

        [HttpPost("telemetries")]
        public IActionResult GetTelemetriesPost([FromBody] KeysRequestDto request, [FromQuery] bool includeLiveValues = false)
        {
            var all = _data.GetAvailableTelemetries(includeLiveValues);
            if (request != null && request.Keys != null && request.Keys.Count > 0)
            {
                var requestedKeys = new HashSet<string>(request.Keys.Select(k => k.Trim()));
                var filtered = all.Where(t => requestedKeys.Contains(t.Key)).ToList();
                return Ok(filtered);
            }
            return Ok(all);
        }

        [HttpGet("widget-data")]
        public async Task<IActionResult> GetWidgetData(
            [FromQuery] string keys, 
            [FromQuery] long startTs, 
            [FromQuery] long endTs,
            [FromQuery] string? barGranularity = null,
            [FromQuery] string? barMode = null)
        {
            var keyList = keys?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
                          ?? new List<string>();

            if (keyList.Count == 0)
                return Ok(new Dictionary<string, object?>());

            var data = await _data.GetTelemetryHistoryForWidgetAsync(keyList, startTs, endTs, barGranularity, barMode);
            return Ok(data);
        }

        [HttpGet("latest-values")]
        public IActionResult GetLatestValues([FromQuery] string keys)
        {
            var keyList = keys?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
                          ?? new List<string>();

            var values = _data.GetCurrentValues(keyList);
            return Ok(values);
        }

        [HttpPost("latest-values")]
        public IActionResult GetLatestValuesPost([FromBody] KeysRequestDto request)
        {
            var keyList = request?.Keys ?? new List<string>();
            var values = _data.GetCurrentValues(keyList);
            return Ok(values);
        }

        [HttpGet("tree")]
        public IActionResult GetTree()
        {
            var tree = _data.GetAssetTrees();
            return Ok(tree);
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetHistory([FromQuery] string key, [FromQuery] string? days, [FromQuery] long? startTs, [FromQuery] long? endTs)
        {
            if (startTs.HasValue && endTs.HasValue)
            {
                var dataRange = await _data.GetTelemetryHistoryAsync(key, startTs.Value, endTs.Value);
                return Ok(dataRange);
            }

            if (!double.TryParse(days, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double d))
                d = 7;
            var data = await _data.GetTelemetryHistoryAsync(key, d);
            return Ok(data);
        }

        [HttpGet("properties")]
        public async Task<IActionResult> GetProperties([FromQuery] string key)
        {
            var data = await _data.GetPropertiesAsync(key);
            return Ok(data);
        }

        [HttpPost("write")]
        public async Task<IActionResult> WriteTelemetry([FromBody] WriteTelemetryRequestDto request)
        {
            if (!DashboardAuth.CanWriteValue(HttpContext, _data.Config.Server))
                return StatusCode(403);

            if (request == null || string.IsNullOrEmpty(request.Key))
                return BadRequest("Invalid request");

            bool success = await _data.WriteValueAsync(request.Key, request.Value);
            return Ok(new { success });
        }

        [HttpPost("write-complex")]
        public async Task<IActionResult> WriteComplexTelemetry([FromBody] WriteComplexTelemetryRequestDto request)
        {
            if (!DashboardAuth.CanWriteValue(HttpContext, _data.Config.Server))
                return StatusCode(403);

            if (request == null || string.IsNullOrEmpty(request.Key))
                return BadRequest("Invalid request");

            bool success = await _data.WriteComplexValueAsync(request.Key, request.Value);
            return Ok(new { success });
        }

        [HttpPost("alarm/reset")]
        public IActionResult ResetAlarm([FromBody] AlarmResetRequestDto req)
        {
            if (!DashboardAuth.CanAckAlarm(HttpContext, _data.Config.Server))
                return StatusCode(403);

            if (string.IsNullOrEmpty(req?.AlarmId))
                return BadRequest("Missing alarm ID");

            try
            {
                bool ok = _data.AlarmStore.Clear(req.AlarmId);
                return Ok(new { success = ok });
            }
            catch (Exception ex)
            {
                return Ok(new { success = false, error = ex.Message });
            }
        }

        [HttpPost("alarm/acknowledge")]
        public IActionResult AcknowledgeAlarm([FromBody] AlarmAckRequestDto req)
        {
            if (!DashboardAuth.CanAckAlarm(HttpContext, _data.Config.Server))
                return StatusCode(403);

            if (string.IsNullOrEmpty(req?.AlarmId))
                return BadRequest("Missing alarm ID");

            try
            {
                bool ok = _data.AlarmStore.Acknowledge(req.AlarmId, req.Comment);

                bool bacnetAcked = false;
                if (ok && !string.IsNullOrEmpty(req.BacnetAckKey))
                {
                    string ackText = !string.IsNullOrWhiteSpace(req.Comment)
                        ? req.Comment
                        : "Acknowledged via Deziko Dashboard";
                    bacnetAcked = Pulswerk.Drivers.BACnet.BacnetDriver.SendAlarmAcknowledgement(req.BacnetAckKey, ackText);
                }

                return Ok(new { success = ok, bacnetAcked });
            }
            catch (Exception ex)
            {
                return Ok(new { success = false, error = ex.Message });
            }
        }

        [HttpGet("heartbeat/stats")]
        public async Task<IActionResult> GetHeartbeatStats()
        {
            var stats = await _data.GetHeartbeatStatsAsync();
            return Ok(stats);
        }

        [HttpGet("user/identity")]
        public IActionResult GetUserIdentity()
        {
            var user = DashboardAuth.GetUser(HttpContext, _data.Config.Server?.Auth);
            var groups = DashboardAuth.GetGroups(HttpContext, _data.Config.Server?.Auth);
            
            var email = HttpContext.Request.Headers["Remote-Email"].FirstOrDefault() ?? "Not authenticated";
            var name = HttpContext.Request.Headers["Remote-Name"].FirstOrDefault() ?? (user ?? "Public");

            return Ok(new
            {
                user = user ?? "Public",
                authenticated = !string.IsNullOrWhiteSpace(user) && user != _data.Config.Server?.Auth?.DefaultUser,
                isDefault = !string.IsNullOrWhiteSpace(user) && user == _data.Config.Server?.Auth?.DefaultUser,
                email = email,
                name = name,
                groups = groups,
                permissions = new
                {
                    canWriteValue = DashboardAuth.CanWriteValue(HttpContext, _data.Config.Server),
                    canAckAlarm = DashboardAuth.CanAckAlarm(HttpContext, _data.Config.Server),
                    canEditDashboard = DashboardAuth.CanEditDashboard(HttpContext, _data.Config.Server),
                    canEditFavorites = DashboardAuth.CanEditFavorites(HttpContext, _data.Config.Server),
                    canEditConfig = DashboardAuth.CanEditConfig(HttpContext, _data.Config.Server),
                    canAccessEms = (_data.Config.Modules?.Ems ?? true) && DashboardAuth.CanAccessEms(HttpContext, _data.Config.Server),
                    canAccessBilling = (_data.Config.Modules?.Billing ?? true) && DashboardAuth.CanAccessBilling(HttpContext, _data.Config.Server),
                    canAccessWallbox = (_data.Config.Modules?.Wallbox ?? true) && DashboardAuth.CanAccessWallbox(HttpContext, _data.Config.Server),
                    canAccessHistoricalData = (_data.Config.Modules?.HistoricalData ?? true) && DashboardAuth.CanAccessHistoricalData(HttpContext, _data.Config.Server),
                    canAccessAlarms = (_data.Config.Modules?.Alarms ?? true) && DashboardAuth.CanAccessAlarms(HttpContext, _data.Config.Server),
                    canAccessLogs = (_data.Config.Modules?.Logs ?? true) && DashboardAuth.CanAccessLogs(HttpContext, _data.Config.Server),
                    canAccessHeartbeat = (_data.Config.Modules?.Heartbeat ?? true) && DashboardAuth.CanAccessHeartbeat(HttpContext, _data.Config.Server),
                    canAccessDashboards = (_data.Config.Modules?.Dashboards ?? true) && DashboardAuth.CanAccessDashboards(HttpContext, _data.Config.Server),
                    canAccessAssets = (_data.Config.Modules?.Assets ?? true) && DashboardAuth.CanAccessAssets(HttpContext, _data.Config.Server),
                    canAccessTelemetry = (_data.Config.Modules?.Telemetry ?? true) && DashboardAuth.CanAccessTelemetry(HttpContext, _data.Config.Server),
                    canAccessConnections = (_data.Config.Modules?.Connections ?? true) && DashboardAuth.CanAccessConnections(HttpContext, _data.Config.Server)
                },
                modules = new
                {
                    ems = _data.Config.Modules?.Ems ?? true,
                    billing = _data.Config.Modules?.Billing ?? true,
                    wallbox = _data.Config.Modules?.Wallbox ?? true,
                    historicalData = _data.Config.Modules?.HistoricalData ?? true,
                    alarms = _data.Config.Modules?.Alarms ?? true,
                    logs = _data.Config.Modules?.Logs ?? true,
                    heartbeat = _data.Config.Modules?.Heartbeat ?? true,
                    dashboards = _data.Config.Modules?.Dashboards ?? true,
                    assets = _data.Config.Modules?.Assets ?? true,
                    telemetry = _data.Config.Modules?.Telemetry ?? true,
                    connections = _data.Config.Modules?.Connections ?? true
                }
            });
        }

        [HttpGet("alarms")]
        public IActionResult GetAlarms([FromQuery] string? severity)
        {
            try
            {
                var allRecords = _data.AlarmStore.GetAllActive();
                var all = allRecords.Select(a => new
                {
                    alarmId = a.Id,
                    type = a.Type,
                    severity = a.Severity,
                    status = a.Status,
                    message = a.Message,
                    originator = a.Originator,
                    time = DateTimeOffset.FromUnixTimeMilliseconds(a.CreatedAt).ToString("o"),
                    ackComment = a.AckComment,
                    bacnetAckKey = a.BacnetAckKey,
                    telemetryKey = a.Details != null && a.Details.Contains("\"telemetryKey\"") 
                        ? JsonSerializer.Deserialize<JsonElement>(a.Details).GetProperty("telemetryKey").GetString() 
                        : null
                }).ToList();

                var unacked = all.Where(a => a.status == "ACTIVE_UNACK").ToList();

                int countCritical = unacked.Count(a => a.severity == "CRITICAL");
                int countMajor = unacked.Count(a => a.severity == "MAJOR");
                int countMinor = unacked.Count(a => a.severity == "MINOR");
                int countWarning = unacked.Count(a => a.severity != "CRITICAL" && a.severity != "MAJOR" && a.severity != "MINOR" && a.severity != "MAINTENANCE");
                int countMaintenance = unacked.Count(a => a.severity == "MAINTENANCE");
                int countAcked = all.Count(a => a.status.StartsWith("ACTIVE_ACK"));

                var filtered = severity switch
                {
                    "ACKED" => all.Where(a => a.status.StartsWith("ACTIVE_ACK")).ToList(),
                    { } s when !string.IsNullOrEmpty(s) => unacked.Where(a => string.Equals(a.severity, s, StringComparison.OrdinalIgnoreCase)).ToList(),
                    _ => all
                };

                return Ok(new
                {
                    alarms = filtered,
                    countCritical,
                    countMajor,
                    countMinor,
                    countWarning,
                    countMaintenance,
                    countAcked,
                    countTotal = countCritical + countMajor + countMinor + countWarning + countMaintenance + countAcked
                });
            }
            catch (Exception ex)
            {
                Log.Error($"[Dashboard] Failed to fetch alarms: {ex.Message}");
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("connections")]
        public IActionResult GetConnections()
        {
            var connectionsList = new List<object>();

            foreach (var conn in _data.Config.Connections)
            {
                var connDevices = _data.Config.Devices
                    .Where(d => d.ConnectionId == conn.Id)
                    .ToList();

                bool isOffline = connDevices.Count > 0 &&
                                 connDevices.All(d => _data.OfflineDevices.ContainsKey(d.Name));

                var lastPolled = connDevices
                    .Select(d => _data.LastPolledAtMap.TryGetValue(d.Name, out var t) ? t : default)
                    .Where(t => t != default)
                    .DefaultIfEmpty(default)
                    .Max();

                string tbType = conn.Type switch
                {
                    "bacnet-ip" => "BACnet Gateway",
                    "modbus-tcp" => "Modbus Gateway",
                    "ocpp" => "OCPP Central System",
                    _ => conn.Type
                };

                var deviceRows = connDevices.Select(d =>
                {
                    bool offline = _data.OfflineDevices.ContainsKey(d.Name);
                    _data.LastPolledAtMap.TryGetValue(d.Name, out var polledAt);

                    bool stale = !offline &&
                                 (polledAt == default ||
                                  (System.DateTime.UtcNow - polledAt).TotalMinutes > 5);

                    string protocol = d.DeviceType.ToLowerInvariant() switch
                    {
                        "janitza" => "Modbus",
                        "glueck" => "Modbus",
                        "abb" => "Modbus",
                        "sunspec" => "Modbus",
                        "bacnet" => "BACnet",
                        "deziko" => "Deziko (BACnet)",
                        "ocpp" => "OCPP",
                        _ => d.DeviceType
                    };

                    string address = d.DeviceId.HasValue ? $"ID {d.DeviceId}" : "–";

                    return new
                    {
                        name = d.Name,
                        deviceType = d.DeviceType,
                        protocol = protocol,
                        address = address,
                        assetType = d.AssetType,
                        status = offline ? "offline" : stale ? "stale" : "online",
                        lastSeen = polledAt == default
                                       ? "Never"
                                       : polledAt.ToString("HH:mm:ss")
                    };
                }).ToList();

                string connStatus = isOffline && connDevices.Count > 0
                    ? "offline"
                    : deviceRows.Any(d => d.status == "stale") ? "stale" : "online";

                connectionsList.Add(new
                {
                    id = conn.Id,
                    name = conn.EffectiveName,
                    type = tbType,
                    address = (conn.Type == "bacnet-ip" ? conn.LocalAddress : conn.Address) ?? "",
                    port = (conn.Type == "bacnet-ip" ? conn.LocalPort : conn.Port) ?? 0,
                    status = connStatus,
                    lastSeen = lastPolled == default
                                    ? "Never"
                                    : lastPolled.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                    deviceCount = connDevices.Count,
                    onlineCount = deviceRows.Count(d => d.status == "online"),
                    devices = deviceRows
                });
            }

            return Ok(new
            {
                connections = connectionsList,
                canEditConfig = DashboardAuth.CanEditConfig(HttpContext, _data.Config.Server)
            });
        }

        // ── OCPP Wallbox Endpoints ──────────────────────────────────────────

        [HttpGet("wallboxes")]
        public IActionResult GetWallboxes()
        {
            var wallboxes = _data.Config.Devices
                .Where(d => d.DeviceType.Equals("ocpp", StringComparison.OrdinalIgnoreCase))
                .Select(d => {
                    var telemetry = Pulswerk.Drivers.Ocpp.OcppManagerService.Instance.GetTelemetry(d.Id);
                    return new {
                        id = d.Id,
                        name = d.Name,
                        connected = Pulswerk.Drivers.Ocpp.OcppManagerService.Instance.IsConnected(d.Id),
                        status = telemetry.GetValueOrDefault("status", "Unavailable"),
                        power = telemetry.GetValueOrDefault("power", 0.0),
                        energyImport = telemetry.GetValueOrDefault("energy_import", 0.0),
                        current = telemetry.GetValueOrDefault("current", 0.0),
                        voltage = telemetry.GetValueOrDefault("voltage", 0.0),
                        activeUser = telemetry.GetValueOrDefault("active_user", "None")
                    };
                }).ToList();

            return Ok(wallboxes);
        }

        public class WallboxCommandDto
        {
            public string ChargePointId { get; set; } = "";
            public string Command { get; set; } = ""; // "start", "stop", "unlock"
            public string? RfidTag { get; set; }
            public int? TransactionId { get; set; }
        }

        [HttpPost("wallboxes/command")]
        public async Task<IActionResult> ExecuteWallboxCommand([FromBody] WallboxCommandDto req)
        {
            var serverCfg = _data.Config.Server;
            if (!DashboardAuth.CanWriteValue(HttpContext, serverCfg))
                return StatusCode(403);

            if (string.IsNullOrEmpty(req.ChargePointId)) return BadRequest("chargePointId is required");

            bool ok = false;
            if (req.Command == "start")
            {
                ok = await Pulswerk.Drivers.Ocpp.OcppManagerService.Instance.RemoteStartTransactionAsync(req.ChargePointId, 1, req.RfidTag ?? "RemoteUser");
            }
            else if (req.Command == "stop" && req.TransactionId.HasValue)
            {
                ok = await Pulswerk.Drivers.Ocpp.OcppManagerService.Instance.RemoteStopTransactionAsync(req.ChargePointId, req.TransactionId.Value);
            }
            else if (req.Command == "unlock")
            {
                string messageId = Guid.NewGuid().ToString("N")[..8];
                string ocppMsg = $"[2,\"{messageId}\",\"UnlockConnector\",{{\"connectorId\":1}}]";
                ok = await Pulswerk.Drivers.Ocpp.OcppManagerService.Instance.SendMessageAsync(req.ChargePointId, ocppMsg);
            }

            return Ok(new { success = ok });
        }

        // ── Billing / Invoice Endpoints ─────────────────────────────────────

        [HttpGet("billing/tariffs")]
        public IActionResult GetBillingTariffs()
        {
            return Ok(new {
                ratePerKwh = _billing.GetTariff("rate_per_kwh", 0.30),
                baseMonthlyFee = _billing.GetTariff("base_monthly_fee", 10.00)
            });
        }

        public class TariffsDto
        {
            public double RatePerKwh { get; set; }
            public double BaseMonthlyFee { get; set; }
        }

        [HttpPost("billing/tariffs")]
        public IActionResult UpdateBillingTariffs([FromBody] TariffsDto req)
        {
            var serverCfg = _data.Config.Server;
            if (!DashboardAuth.CanEditConfig(HttpContext, serverCfg)) return StatusCode(403);

            _billing.SetTariff("rate_per_kwh", req.RatePerKwh);
            _billing.SetTariff("base_monthly_fee", req.BaseMonthlyFee);
            return Ok(new { success = true });
        }

        [HttpGet("billing/rfid")]
        public IActionResult GetRfidMappings()
        {
            var map = _billing.GetRfidMap();
            var list = map.Select(kv => new { idTag = kv.Key, userName = kv.Value }).ToList();
            return Ok(list);
        }

        public class RfidDto
        {
            public string IdTag { get; set; } = "";
            public string UserName { get; set; } = "";
        }

        [HttpPost("billing/rfid")]
        public IActionResult AddRfidMapping([FromBody] RfidDto req)
        {
            var serverCfg = _data.Config.Server;
            if (!DashboardAuth.CanEditConfig(HttpContext, serverCfg)) return StatusCode(403);

            if (string.IsNullOrEmpty(req.IdTag) || string.IsNullOrEmpty(req.UserName))
                return BadRequest("idTag and userName are required");

            _billing.AddRfidMapping(req.IdTag, req.UserName);
            return Ok(new { success = true });
        }

        [HttpDelete("billing/rfid/{idTag}")]
        public IActionResult DeleteRfidMapping(string idTag)
        {
            var serverCfg = _data.Config.Server;
            if (!DashboardAuth.CanEditConfig(HttpContext, serverCfg)) return StatusCode(403);

            _billing.DeleteRfidMapping(idTag);
            return Ok(new { success = true });
        }

        [HttpGet("billing/transactions")]
        public IActionResult GetBillingTransactions()
        {
            var list = _billing.GetTransactions();
            var rfidMap = _billing.GetRfidMap();

            var result = list.Select(t => new {
                id = t.Id,
                chargepointId = t.ChargepointId,
                connectorId = t.ConnectorId,
                idTag = t.IdTag,
                userName = rfidMap.TryGetValue(t.IdTag, out var u) ? u : "Guest",
                kwh = t.Kwh,
                timestamp = DateTimeOffset.FromUnixTimeMilliseconds(t.Timestamp).ToString("yyyy-MM-dd HH:mm:ss")
            }).ToList();

            return Ok(result);
        }

        [HttpGet("billing/invoice")]
        public async Task<IActionResult> GenerateMonthlyInvoice([FromQuery] int year, [FromQuery] int month)
        {
            var transactions = _billing.GetTransactions();
            var rfidMap = _billing.GetRfidMap();
            var tenants = _billing.GetTenants();
            double ratePerKwh = _billing.GetTariff("rate_per_kwh", 0.30);
            double baseMonthlyFee = _billing.GetTariff("base_monthly_fee", 10.00);

            var invoices = new List<object>();

            // 1. Process EV charging sessions
            var filtered = transactions.Where(t => {
                var dt = DateTimeOffset.FromUnixTimeMilliseconds(t.Timestamp);
                return dt.Year == year && dt.Month == month;
            }).ToList();

            var grouped = filtered.GroupBy(t => t.IdTag);
            foreach (var g in grouped)
            {
                string idTag = g.Key;
                string userName = rfidMap.TryGetValue(idTag, out var u) ? u : "Guest";
                double totalKwh = g.Sum(t => t.Kwh);
                double energyCost = Math.Round(totalKwh * ratePerKwh, 2);
                double totalCost = Math.Round(energyCost + baseMonthlyFee, 2);

                invoices.Add(new {
                    type = "EV Charging",
                    idTag = idTag,
                    userName = userName,
                    details = $"RFID: {idTag}",
                    transactionCount = g.Count(),
                    totalKwh = Math.Round(totalKwh, 3),
                    ratePerKwh = ratePerKwh,
                    baseFee = baseMonthlyFee,
                    energyCost = energyCost,
                    totalCost = totalCost,
                    billingPeriod = $"{year:D4}-{month:D2}"
                });
            }

            // 2. Process Metered Tenants
            var startOfMonth = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var endOfMonth = startOfMonth.AddMonths(1);
            long startTs = new DateTimeOffset(startOfMonth).ToUnixTimeMilliseconds();
            long endTs   = new DateTimeOffset(endOfMonth).ToUnixTimeMilliseconds();

            foreach (var tenant in tenants)
            {
                double totalKwh = 0.0;
                int replacementCount = 0;
                try
                {
                    // Build breakpoints: split the billing period at every meter replacement
                    var replacements = _billing.GetMeterReplacementsInRange(tenant.Id, startTs, endTs);
                    replacementCount = replacements.Count;

                    var breakpoints = new List<long> { startTs };
                    foreach (var r in replacements)
                        breakpoints.Add(r.ReplacedAt);
                    breakpoints.Add(endTs);

                    // Sum each contiguous sub-segment independently
                    for (int i = 0; i < breakpoints.Count - 1; i++)
                    {
                        var points = await _data.GetTelemetryHistoryAsync(tenant.MeterKey, breakpoints[i], breakpoints[i + 1]);
                        if (points != null && points.Count > 1)
                        {
                            // points are returned newest-first (descending)
                            double segLatest   = Convert.ToDouble(points.First().Value);
                            double segEarliest = Convert.ToDouble(points.Last().Value);
                            double segKwh = segLatest - segEarliest;
                            totalKwh += Math.Max(0.0, segKwh); // clamp: never subtract for anomalies
                        }
                        else if (points != null && points.Count == 1 && breakpoints.Count == 2)
                        {
                            // Only a single reading in the whole period — fall back to live value
                            var liveVals = _data.GetCurrentValues(new List<string> { tenant.MeterKey });
                            if (liveVals.TryGetValue(tenant.MeterKey, out var liveStr) &&
                                double.TryParse(liveStr, System.Globalization.NumberStyles.Float,
                                                System.Globalization.CultureInfo.InvariantCulture, out double liveVal))
                            {
                                totalKwh = liveVal;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[Billing] Failed to query consumption for tenant '{tenant.Name}' ({tenant.MeterKey}): {ex.Message}");
                }

                double energyCost = Math.Round(totalKwh * ratePerKwh, 2);
                double totalCost  = Math.Round(energyCost + baseMonthlyFee, 2);

                invoices.Add(new {
                    type = "Tenant Meter",
                    idTag = tenant.Id,
                    userName = tenant.Name,
                    details = $"Meter Point: {tenant.MeterKey}",
                    transactionCount = 1,
                    totalKwh = Math.Round(totalKwh, 3),
                    ratePerKwh = ratePerKwh,
                    baseFee = baseMonthlyFee,
                    energyCost = energyCost,
                    totalCost = totalCost,
                    replacementCount = replacementCount,
                    billingPeriod = $"{year:D4}-{month:D2}"
                });
            }

            return Ok(new { invoices, ratePerKwh, baseMonthlyFee });
        }

        [HttpGet("billing/tenants")]
        public IActionResult GetTenants()
        {
            var list = _billing.GetTenants().Select(t => new {
                id = t.Id,
                name = t.Name,
                meterKey = t.MeterKey
            }).ToList();
            return Ok(list);
        }

        public class TenantDto
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string MeterKey { get; set; } = "";
        }

        [HttpPost("billing/tenants")]
        public IActionResult AddOrUpdateTenant([FromBody] TenantDto req)
        {
            var serverCfg = _data.Config.Server;
            if (!DashboardAuth.CanEditConfig(HttpContext, serverCfg)) return StatusCode(403);

            if (string.IsNullOrEmpty(req.Id) || string.IsNullOrEmpty(req.Name) || string.IsNullOrEmpty(req.MeterKey))
                return BadRequest("id, name, and meterKey are required");

            _billing.AddTenant(req.Id, req.Name, req.MeterKey);
            return Ok(new { success = true });
        }

        [HttpDelete("billing/tenants/{id}")]
        public IActionResult DeleteTenant(string id)
        {
            var serverCfg = _data.Config.Server;
            if (!DashboardAuth.CanEditConfig(HttpContext, serverCfg)) return StatusCode(403);

            _billing.DeleteTenant(id);
            return Ok(new { success = true });
        }

        // ── Meter Replacement Events ────────────────────────────────────────

        [HttpGet("billing/meter-replacements")]
        public IActionResult GetMeterReplacements([FromQuery] string tenantId)
        {
            if (string.IsNullOrEmpty(tenantId)) return BadRequest("tenantId is required");
            var list = _billing.GetMeterReplacements(tenantId).Select(r => new {
                id           = r.Id,
                tenantId     = r.TenantId,
                replacedAt   = r.ReplacedAt,
                replacedAtIso = DateTimeOffset.FromUnixTimeMilliseconds(r.ReplacedAt).ToString("o"),
                oldFinalKwh  = r.OldFinalKwh,
                newStartKwh  = r.NewStartKwh,
                note         = r.Note
            }).ToList();
            return Ok(list);
        }

        public class MeterReplacementDto
        {
            public string TenantId     { get; set; } = "";
            public long   ReplacedAt   { get; set; }   // unix ms
            public double? OldFinalKwh { get; set; }
            public double? NewStartKwh { get; set; }
            public string? Note        { get; set; }
        }

        [HttpPost("billing/meter-replacements")]
        public IActionResult AddMeterReplacement([FromBody] MeterReplacementDto req)
        {
            var serverCfg = _data.Config.Server;
            if (!DashboardAuth.CanEditConfig(HttpContext, serverCfg)) return StatusCode(403);

            if (string.IsNullOrEmpty(req.TenantId)) return BadRequest("tenantId is required");
            if (req.ReplacedAt <= 0) return BadRequest("replacedAt (unix ms) is required");

            _billing.AddMeterReplacement(req.TenantId, req.ReplacedAt, req.OldFinalKwh, req.NewStartKwh, req.Note);
            return Ok(new { success = true });
        }

        [HttpDelete("billing/meter-replacements/{id:int}")]
        public IActionResult DeleteMeterReplacement(int id)
        {
            var serverCfg = _data.Config.Server;
            if (!DashboardAuth.CanEditConfig(HttpContext, serverCfg)) return StatusCode(403);

            bool ok = _billing.DeleteMeterReplacement(id);
            return Ok(new { success = ok });
        }

        [HttpPost("trajectory/targets/15min")]
        public IActionResult Set15MinTrajectoryTargets([FromBody] List<TrajectoryTarget15MinDto> req)
        {
            var serverCfg = _data.Config.Server;
            if (!DashboardAuth.CanEditConfig(HttpContext, serverCfg)) return StatusCode(403);

            if (req == null) return BadRequest("Targets list is required");

            var list = req.Select(t => new TrajectoryTarget15Min(t.Timestamp, t.TargetKwh)).ToList();
            _billing.SetTrajectoryTargets15Min(list);
            return Ok(new { success = true });
        }

        [HttpGet("trajectory/targets/15min")]
        public IActionResult Get15MinTrajectoryTargets()
        {
            var list = _billing.GetTrajectoryTargets15Min().Select(t => new {
                timestamp = t.Timestamp,
                targetKwh = t.TargetKwh
            }).ToList();
            return Ok(list);
        }

        public class TrajectoryTarget15MinDto
        {
            public long Timestamp { get; set; }
            public double TargetKwh { get; set; }
        }

        // ── Trajectory Control Endpoints ────────────────────────────────────

        [HttpGet("trajectory/status")]
        public IActionResult GetTrajectoryStatus()
        {
            var svc = TrajectoryService.Instance;
            return Ok(new {
                enabled = _billing.GetTariff("trajectory_control_enabled", 0) == 1,
                monthlyTargetKwh = _billing.GetTariff("trajectory_monthly_target_kwh", 3000.0),
                mainMeterKey = _billing.GetRfidMap().TryGetValue("trajectory_main_meter_key", out var k) ? k : "analytics-summary_daily-kwh",
                targetKwh = svc.TargetKwh,
                actualKwh = svc.ActualKwh,
                deviationPct = svc.DeviationPct,
                isCurtailmentActive = svc.IsCurtailmentActive,
                controlState = svc.ControlState,
                logs = svc.GetLogs().Select(l => new {
                    timestamp = l.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                    message = l.Message,
                    state = l.State
                }).ToList()
            });
        }

        public class TrajectoryConfigDto
        {
            public bool Enabled { get; set; }
            public double MonthlyTargetKwh { get; set; }
            public string MainMeterKey { get; set; } = "";
        }

        [HttpPost("trajectory/config")]
        public IActionResult UpdateTrajectoryConfig([FromBody] TrajectoryConfigDto req)
        {
            var serverCfg = _data.Config.Server;
            if (!DashboardAuth.CanEditConfig(HttpContext, serverCfg)) return StatusCode(403);

            _billing.SetTariff("trajectory_control_enabled", req.Enabled ? 1 : 0);
            _billing.SetTariff("trajectory_monthly_target_kwh", req.MonthlyTargetKwh);
            _billing.AddRfidMapping("trajectory_main_meter_key", req.MainMeterKey);
            return Ok(new { success = true });
        }

        [HttpGet("trajectory/targets")]
        public IActionResult GetTrajectoryTargets()
        {
            var list = _billing.GetCurtailmentTargets().Select(t => new {
                telemetryKey = t.TelemetryKey,
                normalValue = t.NormalValue,
                warningValue = t.WarningValue,
                criticalValue = t.CriticalValue
            }).ToList();
            return Ok(list);
        }

        public class CurtailmentTargetDto
        {
            public string TelemetryKey { get; set; } = "";
            public double NormalValue { get; set; }
            public double WarningValue { get; set; }
            public double CriticalValue { get; set; }
        }

        [HttpPost("trajectory/targets")]
        public IActionResult AddOrUpdateTrajectoryTarget([FromBody] CurtailmentTargetDto req)
        {
            var serverCfg = _data.Config.Server;
            if (!DashboardAuth.CanEditConfig(HttpContext, serverCfg)) return StatusCode(403);

            if (string.IsNullOrEmpty(req.TelemetryKey)) return BadRequest("telemetryKey is required");

            _billing.AddCurtailmentTarget(req.TelemetryKey, req.NormalValue, req.WarningValue, req.CriticalValue);
            return Ok(new { success = true });
        }

        [HttpDelete("trajectory/targets/{*telemetryKey}")]
        public IActionResult DeleteTrajectoryTarget(string telemetryKey)
        {
            var serverCfg = _data.Config.Server;
            if (!DashboardAuth.CanEditConfig(HttpContext, serverCfg)) return StatusCode(403);

            _billing.DeleteCurtailmentTarget(telemetryKey);
            return Ok(new { success = true });
        }

        public class TelemetryInsertDto
        {
            [JsonPropertyName("key")]
            public string Key { get; set; } = null!;
            [JsonPropertyName("ts")]
            public long Ts { get; set; }
            [JsonPropertyName("value")]
            public JsonElement Value { get; set; }
        }

        public class TelemetryBatchInsertDto
        {
            [JsonPropertyName("key")]
            public string Key { get; set; } = null!;
            [JsonPropertyName("points")]
            public List<TelemetryBatchPointDto> Points { get; set; } = null!;
        }

        public class TelemetryBatchPointDto
        {
            [JsonPropertyName("ts")]
            public long Ts { get; set; }
            [JsonPropertyName("value")]
            public JsonElement Value { get; set; }
        }

        [HttpGet("telemetry/data")]
        public async Task<IActionResult> GetTelemetryData(
            [FromQuery] string key,
            [FromQuery] long startTs,
            [FromQuery] long endTs,
            [FromQuery] int limit = 1000)
        {
            if (string.IsNullOrEmpty(key))
                return BadRequest("key is required");

            var data = await _data.DataStore.QueryAsync(key, startTs, endTs, limit, descending: true);
            return Ok(data);
        }

        [HttpPost("telemetry/data")]
        public IActionResult InsertTelemetryData([FromBody] TelemetryInsertDto req)
        {
            var serverCfg = _data.Config.Server;
            if (!DashboardAuth.CanWriteValue(HttpContext, serverCfg))
                return StatusCode(403);

            if (string.IsNullOrEmpty(req.Key))
                return BadRequest("key is required");

            var valObj = ExtractValue(req.Value);
            _data.DataStore.Insert(req.Key, req.Ts, valObj);
            _data.DataStore.Flush();

            return Ok(new { success = true });
        }

        [HttpPost("telemetry/data/batch")]
        public IActionResult InsertTelemetryDataBatch([FromBody] TelemetryBatchInsertDto req)
        {
            var serverCfg = _data.Config.Server;
            if (!DashboardAuth.CanWriteValue(HttpContext, serverCfg))
                return StatusCode(403);

            if (string.IsNullOrEmpty(req.Key))
                return BadRequest("key is required");

            if (req.Points == null || req.Points.Count == 0)
                return BadRequest("points are required");

            foreach (var pt in req.Points)
            {
                var valObj = ExtractValue(pt.Value);
                _data.DataStore.Insert(req.Key, pt.Ts, valObj);
            }
            _data.DataStore.Flush();

            return Ok(new { success = true, count = req.Points.Count });
        }

        [HttpDelete("telemetry/data")]
        public async Task<IActionResult> DeleteTelemetryData(
            [FromQuery] string key,
            [FromQuery] long startTs,
            [FromQuery] long endTs)
        {
            var serverCfg = _data.Config.Server;
            if (!DashboardAuth.CanWriteValue(HttpContext, serverCfg))
                return StatusCode(403);

            if (string.IsNullOrEmpty(key))
                return BadRequest("key is required");

            await _data.DataStore.DeleteAsync(key, startTs, endTs);
            return Ok(new { success = true });
        }

        private static object ExtractValue(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Number:
                    if (element.TryGetDouble(out double d)) return d;
                    return element.GetRawText();
                case JsonValueKind.True:
                    return 1.0;
                case JsonValueKind.False:
                    return 0.0;
                case JsonValueKind.String:
                    var str = element.GetString() ?? "";
                    if (double.TryParse(str, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
                        return parsed;
                    return str;
                default:
                    return element.GetRawText();
            }
        }

        private static string? ResolveConfigPath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var path = Path.Combine(dir.FullName, "pulswerk.json");
                if (System.IO.File.Exists(path)) return path;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
