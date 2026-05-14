// ConnectorHost.cs – orchestrates the Pulswerk connector lifecycle
//
//  Loads configuration, initialises stores, drivers, and the monitoring
//  dashboard, then hands off to DevicePoller for the actual read loops.

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
using System.Collections.Concurrent;

namespace Pulswerk.Host
{
    using Telemetry = Dictionary<string, object>;

    /// <summary>
    /// Hosts the full connector lifecycle: config → stores → drivers →
    /// monitoring dashboard → polling loops → graceful shutdown.
    /// </summary>
    sealed class ConnectorHost
    {
        readonly AppConfig _cfg;
        readonly Dictionary<string, ConnectionConfig> _connections;
        readonly Dictionary<string, IDeviceDriver> _drivers = new();
        readonly ConcurrentDictionary<string, byte> _offlineDevices = new();
        readonly ConcurrentDictionary<string, DateTime> _lastPolledAt = new();
        readonly Dictionary<string, SemaphoreSlim> _connLocks = new();

        ConsoleLogger _logger = null!;
        DashboardDataService? _dataService;
        TelemetryStore _tsStore = null!;
        AlarmStore _alarmStore = null!;
        MonitoringServer? _monitoringServer;
        DevicePoller _poller = null!;

        ConnectorHost(AppConfig cfg)
        {
            _cfg = cfg;
            _connections = cfg.Connections.ToDictionary(c => c.Id);
        }

        // =====================================================================
        //  Entry point
        // =====================================================================

        /// <summary>
        /// Loads config, wires services, runs the polling loops, and blocks
        /// until Ctrl+C.  Returns the exit code (0 = clean shutdown).
        /// </summary>
        public static async Task<int> RunAsync()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            Console.WriteLine($"=== Pulswerk v{version?.Major}.{version?.Minor}.{version?.Build} ===\n");

            var loaded = LoadConfig();
            if (loaded == null) return 1;

            var (cfg, configPath) = loaded.Value;
            var host = new ConnectorHost(cfg);
            await host.StartAsync(configPath);
            return 0;
        }

        // =====================================================================
        //  Startup sequence
        // =====================================================================

        async Task StartAsync(string configPath)
        {
            InitLogging(configPath);
            InitStores();
            InitDrivers();
            PrintSummary();

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            StartMonitoringDashboard(cts.Token);
            StartBacnetClients();

            _poller = new DevicePoller(_drivers, _dataService, _offlineDevices, _lastPolledAt);
            StartPollingLoops(cts.Token);

            // BACnet COV discovery + hierarchy run in background — Modbus polling is already active
            _ = Task.Run(() => StartCovSubscriptions(cts.Token), cts.Token);
            StartHierarchyJobs(cts.Token);

            // Block until Ctrl+C
            try { await Task.Delay(-1, cts.Token); }
            catch (TaskCanceledException) { }

            Shutdown();
        }

        // ── Config loading ───────────────────────────────────────────────────

        static (AppConfig Cfg, string Path)? LoadConfig()
        {
            string? configPath = ResolveConfigPath();
            if (configPath == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine("\n[FATAL] pulswerk.json not found.");
                Console.Error.WriteLine("The application cannot start without settings. Please check your deployment.");
                Console.ResetColor();
                return null;
            }

            try
            {
                string json = File.ReadAllText(configPath);
                var options = new JsonSerializerOptions
                {
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };

                var cfg = JsonSerializer.Deserialize<AppConfig>(json, options)
                          ?? throw new Exception("Deserialization returned null.");

                ConfigValidator.Validate(cfg);
                return (cfg, configPath);
            }
            catch (JsonException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine("\n[FATAL] Syntax error in pulswerk.json:");
                Console.Error.WriteLine($"  Line {ex.LineNumber}, Position {ex.BytePositionInLine}: {ex.Message}");
                Console.Error.WriteLine("\nApplication will now exit.");
                Console.ResetColor();
                return null;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine("\n[FATAL] Invalid configuration:");
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine("\nApplication will now exit.");
                Console.ResetColor();
                return null;
            }
        }

        // ── Logging ──────────────────────────────────────────────────────────

        void InitLogging(string configPath)
        {
            _logger = new ConsoleLogger(new LogBuffer(_cfg.Monitoring?.LogBufferSize ?? 5000));
            Log.Init(_logger);
            Log.Info($"Config: {configPath}");
        }

        // ── Data stores ──────────────────────────────────────────────────────

        void InitStores()
        {
            var influx = _cfg.InfluxDb ?? new InfluxConfig();
            string influxUrl = Environment.GetEnvironmentVariable("INFLUXDB_URL") ?? influx.Url;
            string influxToken = Environment.GetEnvironmentVariable("INFLUXDB_TOKEN") ?? influx.Token;
            string influxOrg = Environment.GetEnvironmentVariable("INFLUXDB_ORG") ?? influx.Org;
            string influxBucket = Environment.GetEnvironmentVariable("INFLUXDB_BUCKET") ?? influx.Bucket;

            var dbCfg = _cfg.Database ?? new DatabaseConfig();

            _tsStore = new TelemetryStore(
                influxUrl, influxToken, influxOrg, influxBucket,
                dbCfg.RetentionDays, dbCfg.CompactionAfterDays);

            string alarmDbPath = Path.Combine(AppContext.BaseDirectory, "alarms.db");
            _alarmStore = new AlarmStore(alarmDbPath);

            Log.Info($"InfluxDB: {influxUrl} org={influxOrg} bucket={influxBucket}");
            Log.Info($"AlarmDB:  {alarmDbPath}");
        }

        // ── Drivers ──────────────────────────────────────────────────────────

        void InitDrivers()
        {
            foreach (var d in _cfg.Devices)
            {
                _drivers[d.Name] = DeviceDriverFactory.Create(d.DeviceType);
                _lastPolledAt[d.Name] = DateTime.MinValue;
            }
        }

        // ── Summary ──────────────────────────────────────────────────────────

        void PrintSummary()
        {
            Log.Info($"Monitoring Config: Enabled={_cfg.Monitoring?.Enabled}, Port={_cfg.Monitoring?.Port}");

            Log.Info("Connections (" + _cfg.Connections.Count + "):");
            foreach (var c in _cfg.Connections)
                Log.Info($"  [{c.Id,-22}] {c.Type,-12} {c.Address}:{c.Port}");

            Log.Info($"Devices ({_cfg.Devices.Count}):");
            foreach (var d in _cfg.Devices)
            {
                var conn = _connections[d.ConnectionId];
                var addr = d.Address ?? conn.Address;
                Log.Info($"  [{d.DeviceType,-15}] {d.Name,-38} → {addr}:{conn.Port}");
            }
        }

        // ── Monitoring dashboard ─────────────────────────────────────────────

        void StartMonitoringDashboard(CancellationToken ct)
        {
            if (_cfg.Monitoring?.Enabled != true) return;

            Log.Info($"Starting monitoring dashboard on port {_cfg.Monitoring.Port}...");

            var dataService = new DashboardDataService(
                _logger.Buffer, _cfg, _tsStore, _alarmStore,
                _offlineDevices, _lastPolledAt, _drivers);
            _dataService = dataService;

            string dataDir = "/app/data";
            var dashboardStore = new DashboardStore(dataDir);

            _monitoringServer = new MonitoringServer(dataService, dashboardStore);
            var server = _monitoringServer;

            _ = Task.Run(async () =>
            {
                try { await server.RunAsync(ct); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Log.Error($"[Monitoring] {ex.Message}"); }
            }, ct);
        }

        // ── BACnet client init ───────────────────────────────────────────────

        void StartBacnetClients()
        {
            foreach (var conn in _cfg.Connections.Where(c => c.Type == "bacnet-ip"))
            {
                int bindPort = conn.LocalPort
                    ?? throw new InvalidOperationException($"Connection '{conn.Id}' is missing localPort.");
                string bindAddr = string.IsNullOrWhiteSpace(conn.LocalAddress)
                    ? "0.0.0.0" : conn.LocalAddress;

                Log.Info($"[BACnet] Initialising connection '{conn.Id}' (addr={bindAddr}, port={bindPort}, deviceId={conn.LocalDeviceId ?? 1234})…");
                try
                {
                    var client = BacnetDriver.GetSharedClient(conn);

                    if (client == null)
                    {
                        var transport = new BacnetIpUdpProtocolTransport(
                            bindPort, false, false, 1472, bindAddr);
                        client = new BacnetClient(transport, (int)(conn.LocalDeviceId ?? 1234));
                        client.Start();
                        Log.Info($"[BACnet] Started new client for '{conn.Id}' on port {bindPort}.");
                    }
                    else
                    {
                        Log.Info($"[BACnet] Reusing existing client for '{conn.Id}' on port {bindPort}.");
                    }

                    BacnetDriver.RegisterClient(conn.Id, bindAddr, bindPort, client);
                }
                catch (Exception ex)
                {
                    Log.Error($"[BACnet] Failed to start client for connection '{conn.Id}' on port {bindPort}: {ex.Message}");
                }
            }
        }

        // ── COV subscriptions ────────────────────────────────────────────────

        void StartCovSubscriptions(CancellationToken ct)
        {
            // Group COV devices by connection — parallelize across connections,
            // but keep devices on the same connection sequential to avoid
            // UDP contention on the shared BACnet socket.
            var covDevices = _cfg.Devices
                .Where(d => d.EffectiveCov is { Enabled: true })
                .GroupBy(d => d.ConnectionId)
                .ToList();

            if (covDevices.Count == 0) return;

            var tasks = covDevices.Select(connGroup => Task.Run(() =>
            {
                foreach (var d in connGroup)
                {
                    try
                    {
                        var covDriver = _drivers[d.Name] as BacnetDriver;
                        if (covDriver == null)
                        {
                            Log.Warning($"[COV] Skipping '{d.Name}': driver is {d.DeviceType}, not BACnet.");
                            continue;
                        }

                        var capturedDevice = d;
                        var capturedConn = _connections[d.ConnectionId];

                        Log.Info($"[COV] Initialising COV mode for '{d.Name}'…");

                        covDriver.InitCovMode(
                            capturedConn,
                            capturedDevice,
                            _alarmStore,
                            _tsStore,
                            tel =>
                            {
                                var persisted = _dataService?.UpdateTelemetry(tel, isPush: true);
                                if (persisted != null)
                                {
                                    foreach (var p in persisted)
                                        _tsStore.Insert(p.Key,
                                            new DateTimeOffset(p.Value.ts).ToUnixTimeMilliseconds(),
                                            p.Value.val);
                                }
                                return Task.CompletedTask;
                            },
                            attr => { _dataService?.UpdateAttributes(attr); return Task.CompletedTask; });

                        _lastPolledAt[d.Name] = DateTime.UtcNow;
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[COV] Failed to init COV for '{d.Name}': {ex.Message}");
                    }
                }
            }, ct)).ToArray();

            Task.WaitAll(tasks);
            Log.Info($"[COV] All COV subscriptions initialised ({covDevices.Sum(g => g.Count())} devices across {covDevices.Count} connections).");
        }

        // ── BACnet hierarchy background jobs ─────────────────────────────────

        void StartHierarchyJobs(CancellationToken ct)
        {
            foreach (var d in _cfg.Devices)
            {
                if (!IsBacnet(d) || !d.HierarchyEnabled) continue;

                var driver = _drivers[d.Name] as BacnetDriver;
                if (driver == null) continue;

                var capturedDevice = d;
                _ = Task.Run(async () =>
                {
                    Log.Info($"[Hierarchy] Background job started for '{capturedDevice.Name}'.");
                    try
                    {
                        DezikoTree? tree = null;
                        while (!ct.IsCancellationRequested)
                        {
                            tree = driver.GetDiscoveredTree(capturedDevice.Name);
                            if (tree is not null) break;
                            await Task.Delay(5_000, ct);
                        }

                        if (tree is null || ct.IsCancellationRequested) return;

                        Log.Info(
                            $"[Hierarchy] Tree ready for '{capturedDevice.Name}' " +
                            $"(roots={tree.Roots.Count}).");
                        driver.MarkHierarchyReady(capturedDevice.Name);
                        Log.Info(
                            $"[Hierarchy] Asset map initialized for '{capturedDevice.Name}'.");
                    }
                    catch (OperationCanceledException) { /* shutting down */ }
                    catch (Exception ex)
                    {
                        Log.Error($"[Hierarchy] '{capturedDevice.Name}': {ex.GetType().Name}: {ex.Message}");
                    }
                }, ct);
            }
        }

        // ── Polling loops ────────────────────────────────────────────────────

        void StartPollingLoops(CancellationToken ct)
        {
            Log.Info("Starting device polling loops. Ctrl+C to stop.");

            // Create one semaphore per connection to serialize requests
            // and prevent overloading shared gateways/controllers.
            foreach (var conn in _cfg.Connections)
                _connLocks[conn.Id] = new SemaphoreSlim(1, 1);

            int staggerIndex = 0;
            foreach (var device in _cfg.Devices)
            {
                var capturedDevice = device;
                var capturedConn = _connections[device.ConnectionId];
                var connLock = _connLocks[device.ConnectionId];

                // BACnet devices default to 1h full-read interval to avoid overloading
                // field controllers. COV-mode devices handle real-time updates via
                // subscriptions; this interval only governs periodic rediscovery/full reads.
                int defaultIntervalSeconds = IsBacnet(device) ? 3600
                    : (_cfg.Polling?.IntervalSeconds ?? 60);

                int intervalMs = (capturedDevice.PollIntervalSeconds ?? defaultIntervalSeconds) * 1000;

                // Stagger each device loop start by 250ms to spread initial load
                int staggerMs = staggerIndex++ * 250;

                _ = Task.Run(async () =>
                {
                    // Initial stagger delay
                    if (staggerMs > 0)
                    {
                        try { await Task.Delay(staggerMs, ct); }
                        catch (TaskCanceledException) { return; }
                    }

                    while (!ct.IsCancellationRequested)
                    {
                        try
                        {
                            await connLock.WaitAsync(ct);
                            try
                            {
                                _poller.PollAndPublish(
                                    capturedDevice, capturedConn,
                                    _tsStore, _alarmStore, intervalMs);
                            }
                            finally
                            {
                                connLock.Release();
                            }
                        }
                        catch (OperationCanceledException) { break; }
                        catch (Exception ex)
                        {
                            Log.Error($"[Loop] FATAL error for {capturedDevice.Name}: {ex.Message}");
                        }

                        try { await Task.Delay(1_000, ct); }
                        catch (TaskCanceledException) { break; }
                    }
                }, ct);
            }
        }

        // ── Shutdown ─────────────────────────────────────────────────────────

        void Shutdown()
        {
            Log.Info("Shutting down…");

            foreach (var d in _cfg.Devices)
                if (d.EffectiveCov is { Enabled: true })
                    (_drivers[d.Name] as BacnetDriver)?.DisposeCovClient(d.Name);

            var dbCfg = _cfg.Database ?? new DatabaseConfig();
            _alarmStore.PurgeCleared(dbCfg.AlarmRetentionDays);

            _tsStore.Dispose();
            _alarmStore.Dispose();
            _monitoringServer?.Dispose();
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        static bool IsBacnet(DeviceConfig d) =>
            d.DeviceType.Equals("bacnet", StringComparison.OrdinalIgnoreCase) ||
            d.DeviceType.Equals("deziko", StringComparison.OrdinalIgnoreCase);

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
    }
}
