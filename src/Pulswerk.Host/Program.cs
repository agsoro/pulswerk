// Program.cs – entry point; wires everything together
//
//  Standalone connector: reads BACnet/Modbus devices, stores telemetry
//  in InfluxDB, manages alarms in SQLite, serves a dashboard via Kestrel.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pulswerk.Core;
using Pulswerk.Dashboard;
using Pulswerk.Drivers;
using Pulswerk.Drivers.BACnet;
using Pulswerk.Storage;
using System.IO.BACnet;

namespace Pulswerk.Host
{
    using Attributes = Dictionary<string, string>;
    using Telemetry = Dictionary<string, object>;

    class Program
    {
        /// <summary>Shared logger for the monitoring dashboard.</summary>
        static ConsoleLogger? _logger;
        static DashboardDataService? _dataService;
        static Dictionary<string, IDeviceDriver> _drivers = new();
        static Dictionary<string, int> _failCounts = new();
        const int FAIL_THRESHOLD = 10;
        static LogBuffer? _logBuffer => _logger?.Buffer;

        static async Task Main()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            Console.WriteLine($"=== Pulswerk v{version?.Major}.{version?.Minor}.{version?.Build} ===\n");

            // ── Load config ───────────────────────────────────────────────────
            string configPath = Path.Combine(AppContext.BaseDirectory, "pulswerk.json");
            if (!File.Exists(configPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"\n[FATAL] Config file not found: {configPath}");
                Console.Error.WriteLine("The application cannot start without settings. Please check your deployment.");
                Console.ResetColor();
                return;
            }

            AppConfig cfg;
            try
            {
                string json = await File.ReadAllTextAsync(configPath);
                var options = new JsonSerializerOptions 
                { 
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };
                
                cfg = JsonSerializer.Deserialize<AppConfig>(json, options)
                      ?? throw new Exception("Deserialization returned null.");

                // Perform logical validation (connections, devices, IDs, etc.)
                ConfigValidator.Validate(cfg);
            }
            catch (JsonException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine("\n[FATAL] Syntax error in pulswerk.json:");
                Console.Error.WriteLine($"  Line {ex.LineNumber}, Position {ex.BytePositionInLine}: {ex.Message}");
                Console.Error.WriteLine("\nApplication will now exit.");
                Console.ResetColor();
                return;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine("\n[FATAL] Invalid configuration:");
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine("\nApplication will now exit.");
                Console.ResetColor();
                return;
            }


            var connections = cfg.Connections.ToDictionary(c => c.Id);


            // ── Initialize logging ────────────────────────────────────────────
            _logger = new ConsoleLogger(new LogBuffer(cfg.Monitoring?.LogBufferSize ?? 5000));

            // ── Initialize data stores ────────────────────────────────────────
            var influx = cfg.InfluxDb ?? new InfluxConfig();

            // Environment variable overrides (useful for Docker)
            string influxUrl = Environment.GetEnvironmentVariable("INFLUXDB_URL") ?? influx.Url;
            string influxToken = Environment.GetEnvironmentVariable("INFLUXDB_TOKEN") ?? influx.Token;
            string influxOrg = Environment.GetEnvironmentVariable("INFLUXDB_ORG") ?? influx.Org;
            string influxBucket = Environment.GetEnvironmentVariable("INFLUXDB_BUCKET") ?? influx.Bucket;

            var dbCfg = cfg.Database ?? new DatabaseConfig();

            var tsStore = new TelemetryStore(
                influxUrl, influxToken, influxOrg, influxBucket,
                dbCfg.RetentionDays, dbCfg.CompactionAfterDays);

            string alarmDbPath = Path.Combine(AppContext.BaseDirectory, "alarms.db");
            var alarmStore = new AlarmStore(alarmDbPath);

            _logger!.Info($"InfluxDB: {influxUrl} org={influxOrg} bucket={influxBucket}");
            _logger!.Info($"AlarmDB:  {alarmDbPath}");

            // ── Trackers ──────────────────────────────────────────────────────
            var offlineDevices = new HashSet<string>();
            var lastPolledAt = new Dictionary<string, DateTime>();
            foreach (var d in cfg.Devices) lastPolledAt[d.Name] = DateTime.MinValue;

            // ── Create per-device driver instances ────────────────────────────
            foreach (var d in cfg.Devices)
                _drivers[d.Name] = DeviceDriverFactory.Create(d.DeviceType);

            // ── Print summary ─────────────────────────────────────────────────
            _logger!.Info($"Monitoring Config: Enabled={cfg.Monitoring?.Enabled}, Port={cfg.Monitoring?.Port}");

            _logger!.Info("Connections (" + cfg.Connections.Count + "):");
            foreach (var c in cfg.Connections)
                Console.WriteLine($"  [{c.Id,-22}] {c.Type,-12} {c.Address}:{c.Port}");

            _logger.Info($"Devices ({cfg.Devices.Count}):");
            foreach (var d in cfg.Devices)
            {
                var conn = connections[d.ConnectionId];
                var addr = d.Address ?? conn.Address;
                _logger.Info($"  [{d.DeviceType,-15}] {d.Name,-38} → {addr}:{conn.Port}");
            }

            // ── Ctrl+C ────────────────────────────────────────────────────────
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            // ── Start monitoring dashboard ────────────────────────────────────
            MonitoringServer? monitoringServer = null;
            if (cfg.Monitoring?.Enabled == true)
            {
                _logger!.Info($"Starting monitoring dashboard on port {cfg.Monitoring.Port}...");
                var dataService = new DashboardDataService(
                    _logBuffer!, cfg, tsStore, alarmStore, offlineDevices, lastPolledAt, _drivers);
                _dataService = dataService;

                // Persistent storage for custom dashboards
                string dataDir = "/app/data";
                var dashboardStore = new DashboardStore(dataDir);

                monitoringServer = new MonitoringServer(dataService, dashboardStore);
                var monServer = monitoringServer;
                _ = Task.Run(async () =>
                {
                    try { await monServer.RunAsync(cts.Token); }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { _logger!.Error($"  [Monitoring] ERROR: {ex.Message}"); }
                }, cts.Token);
            }

            // ── BACnet hierarchy tree extraction ──────────────────────────────

            foreach (var d in cfg.Devices)
            {
                bool IsBacnet(DeviceConfig d) =>
                    d.DeviceType.Equals("bacnet", StringComparison.OrdinalIgnoreCase) ||
                    d.DeviceType.Equals("deziko", StringComparison.OrdinalIgnoreCase);

                if (!IsBacnet(d) || !d.HierarchyEnabled) continue;

                var capturedDevice = d;
                var driver = _drivers[d.Name] as BacnetDriver;
                if (driver == null) continue;

                _ = Task.Run(async () =>
                {
                    _logger!.Info(
                        $"  [Hierarchy] Background job started for '{capturedDevice.Name}'.");
                    try
                    {
                        // Wait until the first ReadFull() sets HierarchyDirty
                        DezikoTree? tree = null;
                        while (!cts.Token.IsCancellationRequested)
                        {
                            tree = driver.GetDiscoveredTree(capturedDevice.Name);
                            if (tree is not null) break;
                            await Task.Delay(5_000, cts.Token);
                        }

                        if (tree is null || cts.Token.IsCancellationRequested) return;

                        _logger!.Info(
                            $"  [Hierarchy] Tree ready for '{capturedDevice.Name}' " +
                            $"(roots={tree.Roots.Count}).");

                        // Signal to driver that hierarchy is ready for alarm routing
                        driver.MarkHierarchyReady(capturedDevice.Name);

                        _logger!.Info(
                            $"  [Hierarchy] Asset map initialized for '{capturedDevice.Name}'.");
                    }
                    catch (OperationCanceledException) { /* shutting down */ }
                    catch (Exception ex)
                    {
                        _logger!.Error($"  [Hierarchy] ERROR '{capturedDevice.Name}': {ex.GetType().Name}: {ex.Message}");
                    }
                }, cts.Token);
            }

            int globalIntervalMs = (cfg.Polling?.IntervalSeconds ?? 30) * 1000;
            
            // ── Initialize BACnet clients per connection ─────────────────────
            foreach (var conn in cfg.Connections.Where(c => c.Type == "bacnet-ip"))
            {
                int bindPort = conn.LocalPort ?? throw new InvalidOperationException($"Connection '{conn.Id}' is missing localPort.");
                string bindAddr = string.IsNullOrWhiteSpace(conn.LocalAddress) ? "0.0.0.0" : conn.LocalAddress;

                _logger!.Info($"  [BACnet] Initialising connection '{conn.Id}' (addr={bindAddr}, port={bindPort}, deviceId={conn.LocalDeviceId ?? 1234})…");
                try
                {
                    // Check if we already have a client for this port to avoid "Address already in use"
                    var client = BacnetDriver.GetSharedClient(conn); 
                    
                    if (client == null)
                    {
                        var transport = new BacnetIpUdpProtocolTransport(bindPort, false, false, 1472, bindAddr);
                        client = new BacnetClient(transport, (int)(conn.LocalDeviceId ?? 1234));
                        client.Start();
                        _logger!.Info($"  [BACnet] Started new client for '{conn.Id}' on port {bindPort}.");
                    }
                    else
                    {
                        _logger!.Info($"  [BACnet] Reusing existing client for '{conn.Id}' on port {bindPort}.");
                    }

                    BacnetDriver.RegisterClient(conn.Id, bindAddr, bindPort, client);
                }
                catch (Exception ex)
                {
                    _logger!.Error($"  [BACnet] Failed to start client for connection '{conn.Id}' on port {bindPort}: {ex.Message}");
                }
            }

            // ── COV init: set up long-lived clients for COV-enabled BACnet devices ──
            foreach (var d in cfg.Devices)
            {
                if (d.Cov is not { Enabled: true }) continue;

                var capturedDevice = d;
                var capturedConn = connections[d.ConnectionId];

                _logger!.Info($"  [COV] Initialising COV mode for '{d.Name}'…");
                var covDriver = _drivers[d.Name] as BacnetDriver
                    ?? throw new InvalidOperationException($"COV requires a BACnet driver, got '{d.DeviceType}'");
                covDriver.InitCovMode(
                    capturedConn,
                    capturedDevice,
                    alarmStore,
                    tsStore,
                    tel =>
                    {
                        var persisted = _dataService?.UpdateTelemetry(tel);
                        if (persisted != null)
                        {
                            foreach (var p in persisted)
                                tsStore.Insert(p.Key, new DateTimeOffset(p.Value.ts).ToUnixTimeMilliseconds(), p.Value.val);
                        }
                        return Task.CompletedTask;
                    },
                    attr => { _dataService?.UpdateAttributes(attr); return Task.CompletedTask; });

                lastPolledAt[d.Name] = DateTime.UtcNow;
            }

            // ── Start polling loops (one per device) ─────────────────────────
            _logger.Info("Starting device polling loops. Ctrl+C to stop.");

            foreach (var device in cfg.Devices)
            {
                var capturedDevice = device;
                var capturedConn = connections[device.ConnectionId];
                int defaultIntervalSeconds = (device.DeviceType == "bacnet" || device.DeviceType == "deziko") 
                                               ? 120 
                                               : (cfg.Polling?.IntervalSeconds ?? 60);

                int intervalMs = (capturedDevice.PollIntervalSeconds ?? defaultIntervalSeconds) * 1000;

                _ = Task.Run(async () =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            await PollAndPublishAsync(
                                capturedDevice, capturedConn,
                                tsStore, alarmStore,
                                lastPolledAt,
                                intervalMs,
                                offlineDevices);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error($"  [Loop] FATAL error for {capturedDevice.Name}: {ex.Message}");
                        }

                        try { await Task.Delay(1_000, cts.Token); }
                        catch (TaskCanceledException) { break; }
                    }
                }, cts.Token);
            }

            // Keep main thread alive
            try { await Task.Delay(-1, cts.Token); }
            catch (TaskCanceledException) { }

            _logger.Info("Shutting down…");

            // ── Graceful shutdown ─────────────────────────────────────────
            foreach (var d in cfg.Devices)
                if (d.Cov is { Enabled: true })
                    (_drivers[d.Name] as BacnetDriver)?.DisposeCovClient(d.Name);

            // Purge old cleared alarms
            alarmStore.PurgeCleared(dbCfg.AlarmRetentionDays);

            // Dispose stores
            tsStore.Dispose();
            alarmStore.Dispose();
            monitoringServer?.Dispose();
        }

        // =====================================================================
        //  Poll one device
        // =====================================================================
        static Task PollAndPublishAsync(
            DeviceConfig device, ConnectionConfig conn,
            TelemetryStore tsStore, AlarmStore alarmStore,
            Dictionary<string, DateTime> lastPolledAt,
            int deviceIntervalMs,
            HashSet<string> offlineDevices)
        {
            try
            {
                var reader = _drivers[device.Name];

                Telemetry telemetry;
                Attributes attributes = new();

                // ── BACnet COV mode ───────────────────────────────────────────
                if (reader is BacnetDriver bacnetDrv && device.Cov is { Enabled: true })
                {
                    var result = bacnetDrv.ServiceCovDevice(conn, device);
                    attributes = result.Attributes;
                    telemetry = result.Telemetry;

                    if (telemetry.Count > 0)
                    {
                        tsStore.InsertBatch(telemetry);
                        var persisted = _dataService?.UpdateTelemetry(telemetry);
                        if (persisted != null)
                        {
                            foreach (var p in persisted)
                                tsStore.Insert(p.Key, new DateTimeOffset(p.Value.ts).ToUnixTimeMilliseconds(), p.Value.val);
                        }
                    }

                    if (attributes.Count > 0)
                        _dataService?.UpdateAttributes(attributes);

                    if (attributes.Count > 0 || telemetry.Count > 0)
                        _logger!.Info(
                            $"[{DateTime.Now:HH:mm:ss}] [{reader.DriverName,-10}] {device.Name,-38} " +
                            $"[COV] attrs={attributes.Count} fallback-tel={telemetry.Count}");
                    return Task.CompletedTask;
                }

                // ── Polling mode (non-COV BACnet / Modbus) ────────────────────
                var now = DateTime.UtcNow;
                if (now - lastPolledAt[device.Name] < TimeSpan.FromMilliseconds(deviceIntervalMs))
                    return Task.CompletedTask;

                lastPolledAt[device.Name] = now;

                if (reader is BacnetDriver br)
                {
                    var result = br.ReadFull(conn, device, alarmStore, tsStore);
                    telemetry = result.Telemetry;
                    attributes = result.Attributes;
                }
                else
                {
                    telemetry = reader.Read(conn, device);
                }

                if (telemetry.Count > 0)
                {
                    // Store in InfluxDB
                    tsStore.InsertBatch(telemetry);

                    // Update live values and get finalized deltas
                    var persisted = _dataService?.UpdateTelemetry(telemetry);
                    if (persisted != null)
                    {
                        foreach (var p in persisted)
                            tsStore.Insert(p.Key, new DateTimeOffset(p.Value.ts).ToUnixTimeMilliseconds(), p.Value.val);
                    }

                    // For non-BACnet readers, also scope keys by technology and device name
                    if (reader is not BacnetDriver)
                    {
                        var scoped = telemetry.ToDictionary(
                            kv => $"{device.Id}_{kv.Key}",
                            kv => kv.Value);
                        var persistedScoped = _dataService?.UpdateTelemetry(scoped);
                        if (persistedScoped != null)
                        {
                            foreach (var p in persistedScoped)
                                tsStore.Insert(p.Key, new DateTimeOffset(p.Value.ts).ToUnixTimeMilliseconds(), p.Value.val);
                        }
                        tsStore.InsertBatch(scoped);
                    }
                }

                if (attributes.Count > 0)
                    _dataService?.UpdateAttributes(attributes);

                // ── Clear Communication Loss Alarm on recovery ────────────────
                _failCounts[device.Name] = 0;
                if (offlineDevices.Remove(device.Name))
                {
                    alarmStore.ClearByOriginAndType(device.Name, "Communication Loss");
                    _logger!.Info(
                        $"[{DateTime.Now:HH:mm:ss}] [{reader.DriverName,-10}] {device.Name,-38} ✓ back online");
                }
            }
            catch (Exception ex)
            {
                // ── Track consecutive failures, only log/alarm after threshold ──
                _failCounts.TryGetValue(device.Name, out int count);
                _failCounts[device.Name] = ++count;

                if (count == FAIL_THRESHOLD)
                {
                    _logger!.Warning(
                        $"[{DateTime.Now:HH:mm:ss}] [{device.DeviceType,-10}] {device.Name,-38} offline ({count} consecutive failures)");

                    if (offlineDevices.Add(device.Name))
                    {
                        alarmStore.CreateOrUpdate(
                            device.Name, "DEVICE",
                            "Communication Loss", "CRITICAL",
                            $"Device {device.Name} is not responding after {count} attempts.");
                    }
                }
            }

            return Task.CompletedTask;
        }
    }
}
