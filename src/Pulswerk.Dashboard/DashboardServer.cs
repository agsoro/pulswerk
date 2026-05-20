// DashboardServer.cs – Embedded Kestrel server + Razor Pages dashboard
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    public sealed class DashboardServer : IDisposable
    {
        private readonly WebApplication _app;
        private readonly DashboardDataService _data;

        public DashboardServer(DashboardDataService data, DashboardStore dashboardStore)
        {
            _data = data;

            try
            {
                var builder = WebApplication.CreateBuilder();
                var port = _data.Config.Server?.Port ?? 5000;

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

                // Robust discovery of the dashboard's static assets (wwwroot)
                // This handles both published (Docker) and source (Dev) environments.
                string[] possibleWwwRoots = {
                    Path.Combine(AppContext.BaseDirectory, "wwwroot", "_content", "Pulswerk.Dashboard"),
                    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Pulswerk.Dashboard", "wwwroot")),
                    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Pulswerk.Dashboard", "wwwroot")),
                    Path.Combine(AppContext.BaseDirectory, "wwwroot")
                };

                string? finalWwwRoot = possibleWwwRoots.FirstOrDefault(Directory.Exists);
                if (finalWwwRoot != null)
                {
                    builder.Environment.WebRootPath = finalWwwRoot;
                    Log.Info($"[Server] Using static assets from: {finalWwwRoot}");
                }
                else
                {
                    Log.Warning("[Server] Could not locate dashboard wwwroot folder. Static assets (icons/logo) may be broken.");
                }

                _app = builder.Build();

                ConfigureMiddleware();
                ConfigureRoutes();
            }
            catch (Exception ex)
            {
                Log.Error($"[Server] FATAL: Failed to build WebApplication: {ex.Message}");
                if (ex.InnerException != null) Log.Error($"[Server]   Inner: {ex.InnerException.Message}");
                throw;
            }
        }

        private void ConfigureMiddleware()
        {
            if (_app.Environment.IsDevelopment())
            {
                _app.UseDeveloperExceptionPage();
            }

            var contentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
            contentTypeProvider.Mappings[".woff2"] = "font/woff2";
            contentTypeProvider.Mappings[".woff"] = "font/woff";
            contentTypeProvider.Mappings[".ttf"] = "font/ttf";
            contentTypeProvider.Mappings[".otf"] = "font/otf";
            contentTypeProvider.Mappings[".eot"] = "application/vnd.ms-fontobject";

            // Serve static files from the discovered WebRootPath at /plswk
            if (!string.IsNullOrEmpty(_app.Environment.WebRootPath))
            {
                _app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(_app.Environment.WebRootPath),
                    RequestPath = "/plswk",
                    ContentTypeProvider = contentTypeProvider
                });
            }
            else
            {
                // Fallback to default behavior if no path was discovered
                _app.UseStaticFiles(new StaticFileOptions
                {
                    RequestPath = "/plswk",
                    ContentTypeProvider = contentTypeProvider
                });
            }
            // ── Anti-spoofing: strip Authelia headers from untrusted sources ──
            // Remote-User/Name/Email/Groups must only be trusted from the
            // reverse proxy (nginx + Authelia).  Direct access to Kestrel
            // could forge these headers otherwise.
            // Anti-spoofing: Strip identity headers if not from a trusted proxy.
            // Malicious users on the local network (bypassing the reverse proxy)
            // could forge these headers otherwise.
            var authCfg = _data.Config.Server?.Auth;
            _app.Use(async (ctx, next) =>
            {
                bool trusted = false;
                if (authCfg is { Enabled: true, TrustedProxies: { Count: > 0 } })
                {
                    var remoteIp = ctx.Connection.RemoteIpAddress;
                    if (remoteIp != null)
                    {
                        // Normalize IPv6-mapped IPv4 (::ffff:10.0.0.1 → 10.0.0.1)
                        if (remoteIp.IsIPv4MappedToIPv6)
                            remoteIp = remoteIp.MapToIPv4();

                        foreach (var entry in authCfg.TrustedProxies!)
                        {
                            if (entry.Contains('/'))
                            {
                                // CIDR range (e.g. "172.16.0.0/12")
                                if (IsInCidr(remoteIp, entry)) { trusted = true; break; }
                            }
                            else if (System.Net.IPAddress.TryParse(entry, out var ip))
                            {
                                var compareIp = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
                                if (remoteIp.Equals(compareIp)) { trusted = true; break; }
                            }
                        }
                    }
                }

                if (!trusted)
                {
                    ctx.Request.Headers.Remove("Remote-User");
                    ctx.Request.Headers.Remove("Remote-Name");
                    ctx.Request.Headers.Remove("Remote-Email");
                    ctx.Request.Headers.Remove("Remote-Groups");
                }

                await next();
            });

            // Forward headers from reverse proxy (e.g. nginx)
            _app.UseForwardedHeaders(new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
            {
                ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.All
            });

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
                        ConnectionId = d.ConnectionId ?? "",
                        Status = isOffline ? "offline" : "online",
                        StatusColor = isOffline ? "#ef4444" : "#10b981",
                        LastSeen = lastPolled == default ? "Never" : lastPolled.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                        Connection = connCfg?.Address ?? "unknown",
                        Port = connCfg?.Port ?? 0
                    };
                }).ToList();

                return Results.Json(devices, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            });

            // ── Telemetry Keys Endpoint ──────────────────────────────────────
            _app.MapGet("/plswk/api/telemetry-keys", (HttpContext ctx) =>
            {
                var serverCfg = _data.Config.Server;
                if (!DashboardAuth.CanEditConfig(ctx, serverCfg))
                    return Results.StatusCode(403);

                var keys = _data.GetAvailableTelemetries();
                return Results.Json(keys, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
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
                return Results.Ok();
            });

            // ── Authelia user identity endpoint ──────────────────────────────
            _app.MapGet("/plswk/api/user", (HttpContext ctx) =>
            {
                var serverCfg = _data.Config.Server;
                var authCfg = serverCfg?.Auth;

                string? user = DashboardAuth.GetUser(ctx, authCfg);
                var groups = DashboardAuth.GetGroups(ctx, authCfg);

                var headers = ctx.Request.Headers;
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
                    canWriteValue = DashboardAuth.CanWriteValue(ctx, serverCfg),
                    canAckAlarm = DashboardAuth.CanAckAlarm(ctx, serverCfg),
                    canEditDashboard = DashboardAuth.CanEditDashboard(ctx, serverCfg),
                    canEditFavorites = DashboardAuth.CanEditFavorites(ctx, serverCfg)
                };

                return Results.Json(dto, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            });

            // ── Configuration Editor Endpoints ──────────────────────────────
            _app.MapGet("/plswk/api/config", (HttpContext ctx) =>
            {
                var serverCfg = _data.Config.Server;
                if (!DashboardAuth.CanEditConfig(ctx, serverCfg))
                    return Results.StatusCode(403);

                string baseConfigPath = ResolveConfigPath() ?? "";
                string baseJson = File.Exists(baseConfigPath) ? File.ReadAllText(baseConfigPath) : "{}";

                string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
                string overridePath = Path.Combine(dataDir, "pulswerk.override.json");
                string overrideJson = File.Exists(overridePath) ? File.ReadAllText(overridePath) : "{}";

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
                        @override = overrideObj ?? new AppConfig(null, null, null, new(), new(), null)
                    };
                    return Results.Json(result, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                }
                catch (Exception ex)
                {
                    return Results.Problem("Failed to parse config: " + ex.Message);
                }
            });

            _app.MapPost("/plswk/api/config/override", async (HttpContext ctx) =>
            {
                var serverCfg = _data.Config.Server;
                if (!DashboardAuth.CanEditConfig(ctx, serverCfg))
                    return Results.StatusCode(403);

                try
                {
                    using var reader = new StreamReader(ctx.Request.Body);
                    var body = await reader.ReadToEndAsync();

                    // Validate JSON
                    var options = new JsonSerializerOptions
                    {
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    };
                    var newOverride = JsonSerializer.Deserialize<AppConfig>(body, options);
                    if (newOverride == null) return Results.BadRequest("Invalid JSON configuration");

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
                    File.WriteAllText(overridePath, saveJson);

                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    Log.Error($"[Server] Failed to save config override: {ex.Message}");
                    return Results.Problem(ex.Message);
                }
            });

            _app.MapPost("/plswk/api/config/evaluate-formula", async (HttpContext ctx) =>
            {
                var serverCfg = _data.Config.Server;
                if (!DashboardAuth.CanEditConfig(ctx, serverCfg))
                    return Results.StatusCode(403);

                using var reader = new StreamReader(ctx.Request.Body);
                var body = await reader.ReadToEndAsync();
                var req = JsonSerializer.Deserialize<JsonElement>(body);

                string formula = req.TryGetProperty("formula", out var f) ? f.GetString() ?? "" : "";
                string deviceId = req.TryGetProperty("deviceId", out var d) ? d.GetString() ?? "" : "";

                DeviceConfig? dev = _data.Config.Devices.FirstOrDefault(x => x.Id == deviceId);

                try
                {
                    // Quick live value resolution using DashboardDataService methods via reflection or public method
                    var method = _data.GetType().GetMethod("GetLiveValueForFormula", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (method != null)
                    {
                        string? val = method.Invoke(_data, new object?[] { formula, null, dev }) as string;
                        return Results.Json(new { result = val ?? "", success = true });
                    }
                }
                catch (Exception ex)
                {
                    return Results.Json(new { error = ex.Message, success = false });
                }
                return Results.Json(new { error = "Evaluation not available", success = false });
            });
        }

        static string? ResolveConfigPath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var path = Path.Combine(dir.FullName, "pulswerk.json");
                if (File.Exists(path)) return path;
                dir = dir.Parent;
            }
            return null;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            Log.Info($"[Server] Dashboard starting...");
            await _app.RunAsync(cancellationToken);
        }

        public void Dispose()
        {
            _data.Uptime?.Stop();
        }

        // ── CIDR range matching helper ──────────────────────────────────────
        private static bool IsInCidr(System.Net.IPAddress address, string cidr)
        {
            try
            {
                var parts = cidr.Split('/');
                if (parts.Length != 2) return false;
                if (!System.Net.IPAddress.TryParse(parts[0], out var network)) return false;
                if (!int.TryParse(parts[1], out var prefixLen)) return false;

                var addrBytes = address.GetAddressBytes();
                var netBytes = network.GetAddressBytes();
                if (addrBytes.Length != netBytes.Length) return false;

                int fullBytes = prefixLen / 8;
                int remainBits = prefixLen % 8;

                for (int i = 0; i < fullBytes && i < addrBytes.Length; i++)
                    if (addrBytes[i] != netBytes[i]) return false;

                if (remainBits > 0 && fullBytes < addrBytes.Length)
                {
                    int mask = 0xFF << (8 - remainBits);
                    if ((addrBytes[fullBytes] & mask) != (netBytes[fullBytes] & mask)) return false;
                }

                return true;
            }
            catch { return false; }
        }
    }
}
