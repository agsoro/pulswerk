// MonitoringServer.cs – Embedded Kestrel server + Razor Pages dashboard
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pulswerk.Core;
using Pulswerk.Drivers;
using Pulswerk.Storage;

namespace Pulswerk.Dashboard
{
    /// <summary>
    /// Embedded Kestrel server that serves a read-only monitoring dashboard using Razor Pages.
    /// </summary>
    public sealed class MonitoringServer : IDisposable
    {
        private readonly WebApplication _app;
        private readonly DashboardDataService _data;

        public MonitoringServer(DashboardDataService data, DashboardStore dashboardStore)
        {
            _data = data;

            try
            {
                var builder = WebApplication.CreateBuilder();
                var port = _data.Config.Monitoring?.Port ?? 5000;

                builder.WebHost.UseUrls($"http://*:{port}");

                // Limit noisy ASP.NET/Kestrel logs to Errors only
                builder.Logging.AddFilter("Microsoft.AspNetCore", Microsoft.Extensions.Logging.LogLevel.Error);
                builder.Logging.AddFilter("System.Net.Http.HttpClient", Microsoft.Extensions.Logging.LogLevel.Error);
                builder.Logging.AddFilter("Microsoft.Extensions.Http", Microsoft.Extensions.Logging.LogLevel.Error);

                // Add Razor Pages with CamelCase JSON
                builder.Services.AddRazorPages()
                    .AddJsonOptions(options =>
                    {
                        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                    });
                builder.Services.AddSingleton(_data);
                builder.Services.AddSingleton(dashboardStore);

                // Persistent storage for encryption keys (shared volume)
                string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
                if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);

                builder.Services.AddDataProtection()
                    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));

                _app = builder.Build();

                ConfigureMiddleware();
                ConfigureRoutes();
            }
            catch (Exception ex)
            {
                Log.Error($"[Monitoring] FATAL: Failed to build WebApplication: {ex.Message}");
                if (ex.InnerException != null) Log.Error($"[Monitoring]   Inner: {ex.InnerException.Message}");
                throw;
            }
        }

        private void ConfigureMiddleware()
        {
            if (_app.Environment.IsDevelopment())
            {
                _app.UseDeveloperExceptionPage();
            }

            // Serve the Host's own wwwroot (if any) at /plswk
            _app.UseStaticFiles("/plswk");

            // Razor class library static files are published under
            // wwwroot/_content/Pulswerk.Dashboard/.  Map them to /plswk
            // so that <script src="/plswk/js/dashboards.js"> resolves correctly.
            var rclPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "_content", "Pulswerk.Dashboard");
            if (Directory.Exists(rclPath))
            {
                _app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
                {
                    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(rclPath),
                    RequestPath = "/plswk"
                });
            }

            _app.UseRouting();

            // Redirect root to /plswk
            _app.MapGet("/", (HttpContext ctx) => Results.Redirect("/plswk/", permanent: true));

            _app.MapRazorPages();
        }

        private void ConfigureRoutes()
        {
            // We keep the API routes for live-updates and potential external integrations

            _app.MapGet("/plswk/api/status", (HttpContext ctx) =>
            {
                var alarmCount = _data.AlarmStore.CountActive();
                var status = new DeviceStatusDto
                {
                    TotalDevices = _data.Config.Devices.Count,
                    OnlineDevices = _data.Config.Devices.Count - _data.OfflineDevices.Count,
                    OfflineDevices = _data.OfflineDevices.Count,
                    ActiveAlarms = alarmCount,
                    ConnectorVersion = _data.Version,
                    UptimeSeconds = (long)_data.Uptime.Elapsed.TotalSeconds,
                    LogBufferSize = _data.LogBuffer.Count,
                    LogBufferCapacity = _data.LogBuffer.Capacity,
                    Timestamp = DateTime.UtcNow.ToString("o")
                };

                return Results.Json(status, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            });

            _app.MapGet("/plswk/api/devices", (HttpContext ctx) =>
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
                        ConnectionId = d.ConnectionId,
                        Status = isOffline ? "offline" : "online",
                        StatusColor = isOffline ? "#ef4444" : "#10b981",
                        LastSeen = lastPolled == default ? "Never" : lastPolled.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                        Connection = connCfg?.Address ?? "unknown",
                        Port = connCfg?.Port ?? 0
                    };
                }).ToList();

                return Results.Json(devices, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            });

            _app.MapGet("/plswk/api/logs", (HttpContext ctx) =>
            {
                int count = 200;
                if (int.TryParse(ctx.Request.Query["count"], out int c)) count = Math.Min(c, 5000);

                var logs = _data.LogBuffer.GetLatest(count).Select(l => new LogEntryDto
                {
                    Timestamp = l.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    Severity = l.Severity.ToString().ToLowerInvariant(),
                    Message = l.Message,
                    Source = l.Source
                }).ToList();

                return Results.Json(logs, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            });

            _app.MapGet("/plswk/api/alarm", (HttpContext ctx) =>
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

                return Results.Json(dtos, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            });

            _app.MapGet("/plswk/api/connection-health/{connId}", (HttpContext ctx, string connId) =>
            {
                var history = _data.GetConnectionHealth(connId);
                var dto = history.Select(h => new
                {
                    t = h.Time.ToString("o"),
                    online = h.Online,
                    total = h.Total
                }).ToList();

                return Results.Json(dto, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            });

            _app.MapGet("/plswk/api/health-history", (HttpContext ctx) =>
            {
                var history = _data.GetHealthHistory();
                return Results.Json(history, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            });

            _app.MapGet("/plswk/api/latest-value/{key}", (HttpContext ctx, string key) =>
            {
                var values = _data.GetCurrentValues(new List<string> { key });
                return Results.Json(values, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            });

            // Client-side JS error reporting endpoint
            _app.MapPost("/plswk/api/client-error", async (HttpContext ctx) =>
            {
                try
                {
                    using var reader = new StreamReader(ctx.Request.Body);
                    var body = await reader.ReadToEndAsync();
                    var err = JsonSerializer.Deserialize<JsonElement>(body);

                    var msg   = err.TryGetProperty("msg", out var m) ? m.GetString() : "unknown";
                    var src   = err.TryGetProperty("source", out var s) ? s.GetString() : "";
                    var line  = err.TryGetProperty("line", out var l) ? l.GetInt32().ToString() : "?";
                    var col   = err.TryGetProperty("col", out var c) ? c.GetInt32().ToString() : "?";
                    var stack = err.TryGetProperty("stack", out var st) ? st.GetString() : "";
                    var page  = err.TryGetProperty("page", out var p) ? p.GetString() : "";

                    Log.Warning($"[UI] JS Error on {page} at {src}:{line}:{col} — {msg}");
                    if (!string.IsNullOrEmpty(stack))
                        Log.Warning($"[UI]   Stack: {stack.Replace("\n", " | ")}");
                }
                catch { /* don't fail on malformed reports */ }
                return Results.Ok();
            });
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            Log.Info($"[Monitoring] Dashboard starting...");
            await _app.RunAsync(cancellationToken);
        }

        public void Dispose()
        {
            _data.Uptime?.Stop();
        }
    }
}
