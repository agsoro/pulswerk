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

namespace Pulswerk.Dashboard.Controllers
{
    [ApiController]
    [Route("plswk/api")]
    public class ApiController : ControllerBase
    {
        private readonly DashboardDataService _data;
        private readonly DashboardStore _store;

        public ApiController(DashboardDataService data, DashboardStore store)
        {
            _data = data;
            _store = store;
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
                                  Timestamp = l.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
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
                while (!HttpContext.RequestAborted.IsCancellationRequested)
                {
                    var values = await channel.Reader.ReadAsync(HttpContext.RequestAborted);
                    var json = JsonSerializer.Serialize(values, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    await Response.WriteAsync($"data: {json}\n\n", HttpContext.RequestAborted);
                    await Response.Body.FlushAsync(HttpContext.RequestAborted);
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
                var err = JsonSerializer.Deserialize<JsonElement>(body);

                var msg = err.TryGetProperty("msg", out var m) ? m.GetString() : "unknown";
                var src = err.TryGetProperty("source", out var s) ? s.GetString() : "";
                var line = err.TryGetProperty("line", out var l) ? l.GetInt32().ToString() : "?";
                var col = err.TryGetProperty("col", out var c) ? c.GetInt32().ToString() : "?";
                var stack = err.TryGetProperty("stack", out var st) ? st.GetString() : "";
                var page = err.TryGetProperty("page", out var p) ? p.GetString() : "";

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

        [HttpGet("widget-data")]
        public async Task<IActionResult> GetWidgetData([FromQuery] string keys, [FromQuery] long startTs, [FromQuery] long endTs)
        {
            var keyList = keys?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
                          ?? new List<string>();

            if (keyList.Count == 0)
                return Ok(new Dictionary<string, object?>());

            var data = await _data.GetTelemetryHistoryForWidgetAsync(keyList, startTs, endTs);
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
