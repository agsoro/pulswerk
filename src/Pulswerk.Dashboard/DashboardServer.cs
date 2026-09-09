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
using Pulswerk.Billing;
using Pulswerk.Ems;

namespace Pulswerk.Dashboard
{
    /// <summary>
    /// Embedded Kestrel server that serves a read-only monitoring dashboard using Razor Pages.
    /// </summary>
    public sealed class DashboardServer : IDisposable
    {
        private readonly WebApplication _app;
        private readonly DashboardDataService _data;
        private readonly DashboardStore _store;

        public DashboardServer(DashboardDataService data, DashboardStore dashboardStore, BillingStore billingStore)
        {
            _data = data;
            _store = dashboardStore;

            try
            {
                var builder = WebApplication.CreateBuilder();
                var port = _data.Config.Server?.Port ?? 5000;
                var ports = new HashSet<int> { port };

                if (_data.Config.Connections != null)
                {
                    foreach (var c in _data.Config.Connections)
                    {
                        if (c.Type.Equals("ocpp-ws", StringComparison.OrdinalIgnoreCase) && c.LocalPort.HasValue)
                        {
                            ports.Add(c.LocalPort.Value);
                        }
                    }
                }

                var urls = string.Join(";", ports.Select(p => $"http://*:{p}"));
                builder.WebHost.UseUrls(urls);

                // Limit noisy ASP.NET/Kestrel logs to Errors only
                builder.Logging.AddFilter("Microsoft.AspNetCore", Microsoft.Extensions.Logging.LogLevel.Error);
                builder.Logging.AddFilter("System.Net.Http.HttpClient", Microsoft.Extensions.Logging.LogLevel.Error);
                builder.Logging.AddFilter("Microsoft.Extensions.Http", Microsoft.Extensions.Logging.LogLevel.Error);

                // Add Controllers and Razor Pages with CamelCase JSON
                builder.Services.AddControllers()
                    .AddJsonOptions(options =>
                    {
                        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                    });
                builder.Services.AddRazorPages();
                builder.Services.AddSingleton(_data);
                builder.Services.AddSingleton(dashboardStore);
                builder.Services.AddSingleton(billingStore);

                // Persistent storage for encryption keys (shared volume)
                string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
                if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);

                builder.Services.AddDataProtection()
                    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));

                // Robust discovery of the dashboard's static assets (wwwroot)
                // This handles published (Docker), source (Dev), and multi-level bin directory layouts.
                string? finalWwwRoot = FindDashboardWwwRoot();
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
            // Add global Pulswerk version header to all API responses
            _app.Use(async (ctx, next) =>
            {
                if (ctx.Request.Path.StartsWithSegments("/plswk/api"))
                {
                    ctx.Response.Headers.Append("X-Pulswerk-Version", _data.Version);
                }
                await next();
            });

            _app.UseWebSockets();

            _app.Use(async (ctx, next) =>
            {
                var path = ctx.Request.Path.Value;
                if (path != null)
                {
                    ConnectionConfig? ocppConn = null;
                    string matchedPrefix = "";

                    if (_data.Config.Connections != null)
                    {
                        foreach (var c in _data.Config.Connections)
                        {
                            if (!c.Type.Equals("ocpp-ws", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(c.LocalAddress))
                                continue;

                            if (c.LocalPort != null && c.LocalPort != ctx.Connection.LocalPort)
                                continue;

                            var prefixes = new List<string> { c.LocalAddress.TrimEnd('/') };
                            if (c.LocalAddress.StartsWith("/plswk/", StringComparison.OrdinalIgnoreCase))
                            {
                                prefixes.Add(c.LocalAddress.Substring(6).TrimEnd('/')); // e.g. "/ocpp"
                            }
                            else
                            {
                                prefixes.Add("/plswk" + (c.LocalAddress.StartsWith('/') ? c.LocalAddress.TrimEnd('/') : "/" + c.LocalAddress.TrimEnd('/'))); // e.g. "/plswk/ocpp"
                            }

                            foreach (var p in prefixes)
                            {
                                if (path.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase) || path.Equals(p, StringComparison.OrdinalIgnoreCase))
                                {
                                    ocppConn = c;
                                    matchedPrefix = p;
                                    break;
                                }
                            }

                            if (ocppConn != null) break;
                        }
                    }

                    if (ocppConn != null)
                    {
                        var remoteIp = ctx.Connection.RemoteIpAddress != null && ctx.Connection.RemoteIpAddress.IsIPv4MappedToIPv6
                            ? ctx.Connection.RemoteIpAddress.MapToIPv4().ToString()
                            : (ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                        string fullPath = path + (ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value : "");

                        Log.Info($"[OCPP] Incoming request from {remoteIp} on full path '{fullPath}' (isWebSocket: {ctx.WebSockets.IsWebSocketRequest}).");

                        var modules = _data.Config.Modules ?? new Pulswerk.Core.ModulesConfig();
                        if (!modules.Wallbox)
                        {
                            Log.Warning($"[OCPP] Connection attempt from {remoteIp} on full path '{fullPath}' rejected: Wallbox module is disabled.");
                            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                            await ctx.Response.WriteAsync("OCPP Wallbox module is disabled.");
                            return;
                        }

                        // Extract subpath after the matched prefix (e.g. "/ocpp/LP1/AT2DMEY0F39100107" -> "LP1/AT2DMEY0F39100107")
                        string rawSubpath = path.Length > matchedPrefix.Length
                            ? path.Substring(matchedPrefix.Length).Trim('/')
                            : "";

                        if (string.IsNullOrWhiteSpace(rawSubpath))
                        {
                            Log.Warning($"[OCPP] Connection attempt from {remoteIp} on full path '{fullPath}' rejected: ChargePointId is missing from path.");
                            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                            await ctx.Response.WriteAsync("ChargePointId is missing from path.");
                            return;
                        }

                        // Split subpath into segments (e.g. ["LP1", "AT2DMEY0F39100107"])
                        var segments = rawSubpath.Split('/', StringSplitOptions.RemoveEmptyEntries);

                        // Find configured OCPP device on this connection:
                        // 1. Direct match with raw subpath
                        // 2. Match first segment (e.g. "LP1" when wallbox appends its serial number to configured endpoint)
                        // 3. Match last segment (e.g. "AT2DMEY0F39100107" if serial is configured as device ID)
                        // 4. Fallback: if only 1 OCPP device is configured on this connection, match it
                        var ocppDevices = _data.Config.Devices?.Where(d =>
                            d.DeviceType.Equals("ocpp", StringComparison.OrdinalIgnoreCase) &&
                            d.ConnectionId == ocppConn.Id
                        ).ToList() ?? new List<DeviceConfig>();

                        DeviceConfig? device = ocppDevices.FirstOrDefault(d => string.Equals(d.Id, rawSubpath, StringComparison.OrdinalIgnoreCase));

                        if (device == null && segments.Length > 1)
                        {
                            // Match first segment (e.g. "LP1" in "/ocpp/LP1/AT2DMEY0F39100107")
                            device = ocppDevices.FirstOrDefault(d => string.Equals(d.Id, segments[0], StringComparison.OrdinalIgnoreCase));

                            // Match last segment (e.g. "AT2DMEY0F39100107")
                            device ??= ocppDevices.FirstOrDefault(d => string.Equals(d.Id, segments[^1], StringComparison.OrdinalIgnoreCase));

                            // Match any other segment if present
                            if (device == null)
                            {
                                device = ocppDevices.FirstOrDefault(d => segments.Any(s => string.Equals(d.Id, s, StringComparison.OrdinalIgnoreCase)));
                            }
                        }

                        if (device == null && ocppDevices.Count == 1)
                        {
                            device = ocppDevices[0];
                            Log.Info($"[OCPP] Single configured charger '{device.Id}' matched for incoming subpath '{rawSubpath}' from {remoteIp} on full path '{fullPath}'.");
                        }

                        if (device == null)
                        {
                            Log.Warning($"[OCPP] Connection attempt from {remoteIp} on full path '{fullPath}' (subpath: '{rawSubpath}') rejected: No matching charger configured on OCPP connection '{ocppConn.Id}'.");
                            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                            await ctx.Response.WriteAsync($"No charger matching '{rawSubpath}' is configured on OCPP connection '{ocppConn.Id}'.");
                            return;
                        }

                        string chargePointId = device.Id;

                        if (ctx.WebSockets.IsWebSocketRequest)
                        {
                            Log.Info($"[OCPP] Accepting WebSocket connection for charger '{chargePointId}' on full path '{fullPath}' from {remoteIp}.");
                            using var webSocket = await ctx.WebSockets.AcceptWebSocketAsync();
                            await Pulswerk.Drivers.Ocpp.OcppManagerService.Instance.HandleConnectionAsync(chargePointId, webSocket, ctx.RequestAborted);
                            return;
                        }
                        else
                        {
                            Log.Warning($"[OCPP] Connection attempt from {remoteIp} on full path '{fullPath}' for charger '{chargePointId}' rejected: Expected WebSocket request but received HTTP {ctx.Request.Method}.");
                            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                            await ctx.Response.WriteAsync("Only WebSocket connections are accepted on this endpoint.");
                            return;
                        }
                    }
                    else if (ctx.WebSockets.IsWebSocketRequest)
                    {
                        var remoteIp = ctx.Connection.RemoteIpAddress != null && ctx.Connection.RemoteIpAddress.IsIPv4MappedToIPv6
                            ? ctx.Connection.RemoteIpAddress.MapToIPv4().ToString()
                            : (ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                        string fullPath = path + (ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value : "");
                        Log.Warning($"[OCPP] Incoming WebSocket request from {remoteIp} on full path '{fullPath}' did not match any configured OCPP connection prefix.");
                    }
                }
                await next();
            });

            // High-priority redirects for root and legacy paths
            _app.Use(async (ctx, next) =>
            {
                var path = ctx.Request.Path.Value;
                if (path != null)
                {
                    // Redirect legacy /plswk/AssetsList to TelemetryList
                    if (path.Equals("/plswk/AssetsList", StringComparison.OrdinalIgnoreCase))
                    {
                        var dest = "/plswk/TelemetryList" + ctx.Request.QueryString.Value;
                        ctx.Response.Redirect(dest, permanent: true);
                        return;
                    }

                    // Check if path is a legacy root-level page
                    var legacyPages = new[] {
                        "/Dashboards", "/Assets", "/TelemetryList", "/AssetsList",
                        "/Connections", "/Alarms", "/Logs", "/Heartbeat"
                    };

                    string? target = null;
                    if (path.Equals("/", StringComparison.OrdinalIgnoreCase))
                    {
                        target = "/plswk/";
                    }
                    else
                    {
                        foreach (var page in legacyPages)
                        {
                            if (path.Equals(page, StringComparison.OrdinalIgnoreCase))
                            {
                                if (page.Equals("/AssetsList", StringComparison.OrdinalIgnoreCase))
                                {
                                    target = "/plswk/TelemetryList";
                                }
                                else
                                {
                                    target = "/plswk" + page;
                                }
                                break;
                            }
                        }
                    }

                    if (target != null)
                    {
                        var dest = target + ctx.Request.QueryString.Value;
                        ctx.Response.Redirect(dest, permanent: true);
                        return;
                    }
                }
                await next();
            });

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

            _app.MapControllers();
            _app.MapRazorPages();
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

            // The WebApplication owns Kestrel, the DI container (and every disposable
            // singleton/scoped service in it), the data-protection key ring, sockets and
            // thread-pool resources. It must be disposed or all of that leaks. RunAsync is
            // driven by the host's cancellation token, so by the time we get here the server
            // is (being) stopped; DisposeAsync also stops it if still running.
            try { _app.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch (Exception ex) { Log.Debug($"[Server] Error disposing web application: {ex.Message}"); }

            // The health timer keeps a rooted callback over the data service; release it too.
            _data.Dispose();
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

        private static string? FindDashboardWwwRoot()
        {
            // 1. Direct checks relative to AppContext.BaseDirectory
            string[] directCandidates = {
                Path.Combine(AppContext.BaseDirectory, "wwwroot", "_content", "Pulswerk.Dashboard"),
                Path.Combine(AppContext.BaseDirectory, "wwwroot")
            };
            foreach (var c in directCandidates)
            {
                if (Directory.Exists(c) && (File.Exists(Path.Combine(c, "dashboard.html")) || Directory.Exists(Path.Combine(c, "css"))))
                    return Path.GetFullPath(c);
            }

            // 2. Walk up directory tree from AppContext.BaseDirectory
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var directWww = Path.Combine(dir.FullName, "wwwroot");
                if (Directory.Exists(directWww) && (File.Exists(Path.Combine(directWww, "dashboard.html")) || Directory.Exists(Path.Combine(directWww, "css"))))
                    return Path.GetFullPath(directWww);

                var srcDashWww = Path.Combine(dir.FullName, "src", "Pulswerk.Dashboard", "wwwroot");
                if (Directory.Exists(srcDashWww))
                    return Path.GetFullPath(srcDashWww);

                var dashWww = Path.Combine(dir.FullName, "Pulswerk.Dashboard", "wwwroot");
                if (Directory.Exists(dashWww))
                    return Path.GetFullPath(dashWww);

                dir = dir.Parent;
            }

            return null;
        }
    }
}
