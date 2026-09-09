using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pulswerk.Core;
using Pulswerk.Storage;
using Pulswerk.Billing;

namespace Pulswerk.Drivers.Ocpp
{
    public sealed class OcppManagerService
    {
        private static readonly Lazy<OcppManagerService> _instance = new(() => new OcppManagerService());
        public static OcppManagerService Instance => _instance.Value;

        // Tracks active WebSocket connections by ChargePointId
        private readonly ConcurrentDictionary<string, WebSocket> _activeSockets = new();

        // Tracks in-memory live telemetry values by ChargePointId
        private readonly ConcurrentDictionary<string, Dictionary<string, object>> _liveTelemetry = new();

        // Tracks active transaction IDs to details (ChargePointId, ConnectorId, IdTag, StartMeterValue)
        private readonly ConcurrentDictionary<int, ActiveTransactionInfo> _activeTransactions = new();

        private int _transactionIdCounter = 1000;
        private BillingStore? _billingStore;

        // Force power setpoint state (in kW, like Solis battery)
        private double _forcePowerKw = 0.0;
        private DateTime _forcePowerSetAtUtc = DateTime.MinValue;
        private double _forcePowerValiditySeconds = 60.0;
        private readonly object _forcePowerLock = new();
        private Timer? _watchdogTimer;

        // Fixed maximum per-phase charge current (A) and the default number of phases.
        // The 100% power_limit reference is the TOTAL power capacity, i.e.
        // DefaultMaxCurrentAmps * MaxPhases (= 16A x 3 phases).
        public const double DefaultMaxCurrentAmps = 16.0;
        public const int MaxPhases = 3;

        // Minimum charge current per phase (IEC 61851 / OCPP). A car will not charge
        // below this, so when a low power_limit would require less than this per phase
        // we drop phases (e.g. to 1 phase @ 6A) instead of going below it.
        public const double MinCurrentAmps = 6.0;

        // Power_limit cut-off threshold (% of total capacity). At or above this we
        // charge (at least minimally, 1 phase @ 6A); below it charging is shut off.
        public const double MinChargePercent = 10.0;

        public event Action<string, string, object>? OnTelemetryUpdated;
        public event Action<string, object>? OnServerTelemetryUpdated;

        private OcppManagerService() { }

        public void Initialize(BillingStore billingStore)
        {
            _billingStore = billingStore;
            _watchdogTimer ??= new Timer(CheckWatchdog, null, 10000, 10000);
            Log.Info("[OCPP] OcppManagerService initialized.");
        }

        public bool IsConnected(string chargePointId) => _activeSockets.ContainsKey(chargePointId);

        public Dictionary<string, object> GetTelemetry(string chargePointId)
        {
            if (_liveTelemetry.TryGetValue(chargePointId, out var dict))
            {
                lock (dict) return new Dictionary<string, object>(dict);
            }
            return new Dictionary<string, object>
            {
                ["status"] = "Unavailable",
                ["power"] = 0.0,
                ["energy_import"] = 0.0,
                ["current"] = 0.0,
                ["voltage"] = 0.0,
                ["active_user"] = "None"
            };
        }

        // Handles WebSocket connection loop
        public async Task HandleConnectionAsync(string chargePointId, WebSocket socket, CancellationToken ct)
        {
            Log.Info($"[OCPP] Charger '{chargePointId}' connecting...");
            
            // Disconnect old socket if exists
            if (_activeSockets.TryRemove(chargePointId, out var oldSocket))
            {
                try { await oldSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Replaced", CancellationToken.None); } catch { }
                // The replaced socket is no longer referenced anywhere — dispose it instead of
                // leaving it for the finalizer (it holds an unmanaged socket handle + buffers).
                try { oldSocket.Dispose(); } catch { }
            }

            _activeSockets[chargePointId] = socket;
            
            // Set initial telemetry as connected
            UpdateTelemetryValue(chargePointId, "status", "Connected");

            var buffer = new byte[8192];
            try
            {
                while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        await ProcessOcppMessageAsync(chargePointId, message, socket);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[OCPP] Connection error on '{chargePointId}': {ex.Message}");
            }
            finally
            {
                _activeSockets.TryRemove(chargePointId, out _);
                UpdateTelemetryValue(chargePointId, "status", "Offline");

                // Purge any transactions still open for this charge point. A charger that
                // disconnects mid-charge (crash, network loss) may never send StopTransaction,
                // and transaction ids are monotonically increasing, so without this the
                // _activeTransactions dictionary would accumulate orphaned entries forever.
                foreach (var kvp in _activeTransactions)
                {
                    if (string.Equals(kvp.Value.ChargePointId, chargePointId, StringComparison.OrdinalIgnoreCase))
                        _activeTransactions.TryRemove(kvp.Key, out _);
                }

                PublishServerTelemetry();
                if (IsForcePowerActive(DateTime.UtcNow))
                {
                    _ = Task.Run(() => DistributeForcePowerAsync());
                }

                Log.Info($"[OCPP] Charger '{chargePointId}' disconnected.");
            }
        }

        private void UpdateTelemetryValue(string chargePointId, string key, object value)
        {
            var dict = _liveTelemetry.GetOrAdd(chargePointId, _ => new Dictionary<string, object>());
            lock (dict)
            {
                dict[key] = value;
            }

            OnTelemetryUpdated?.Invoke(chargePointId, key, value);
        }

        // Processes OCPP 1.6-J messages
        private async Task ProcessOcppMessageAsync(string chargePointId, string rawMessage, WebSocket socket)
        {
            try
            {
                using var doc = JsonDocument.Parse(rawMessage);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 3)
                {
                    Log.Warning($"[OCPP] [{chargePointId}] Invalid message format: {rawMessage}");
                    return;
                }

                int messageType = root[0].GetInt32();
                string messageId = root[1].GetString() ?? "";

                // MessageType 2 = CALL
                if (messageType == 2)
                {
                    string action = root[2].GetString() ?? "";
                    var payload = root[3];
                    await HandleOcppCallAsync(chargePointId, messageId, action, payload, socket);
                }
                // MessageType 3 = CALLRESULT (we don't need to handle responses in detail for this design, but log it)
                else if (messageType == 3)
                {
                    Log.Debug($"[OCPP] [{chargePointId}] Received CallResult for message {messageId}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[OCPP] [{chargePointId}] Error parsing message: {ex.Message}");
            }
        }

        private async Task HandleOcppCallAsync(string chargePointId, string messageId, string action, JsonElement payload, WebSocket socket)
        {
            Log.Info($"[OCPP] [{chargePointId}] Action: {action}");

            object? responsePayload = null;

            switch (action)
            {
                case "BootNotification":
                    UpdateTelemetryValue(chargePointId, "status", "Available");
                    responsePayload = new
                    {
                        status = "Accepted",
                        currentTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        interval = 60
                    };
                    break;

                case "Heartbeat":
                    responsePayload = new
                    {
                        currentTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                    };
                    break;

                case "StatusNotification":
                    string status = payload.GetProperty("status").GetString() ?? "Available";
                    UpdateTelemetryValue(chargePointId, "status", status);
                    responsePayload = new { };
                    break;

                case "Authorize":
                    string idTag = payload.GetProperty("idTag").GetString() ?? "";
                    bool isAuthorized = _billingStore == null || _billingStore.IsRfidValid(idTag);
                    responsePayload = new
                    {
                        idTagInfo = new
                        {
                            status = isAuthorized ? "Accepted" : "Blocked",
                            expiryDate = DateTime.UtcNow.AddDays(365).ToString("yyyy-MM-ddTHH:mm:ssZ")
                        }
                    };
                    break;

                case "StartTransaction":
                    int connectorId = payload.GetProperty("connectorId").GetInt32();
                    string startIdTag = payload.GetProperty("idTag").GetString() ?? "Guest";
                    double startMeter = payload.GetProperty("meterStart").GetDouble(); // in Wh

                    int transId = Interlocked.Increment(ref _transactionIdCounter);
                    _activeTransactions[transId] = new ActiveTransactionInfo(chargePointId, connectorId, startIdTag, startMeter, DateTime.UtcNow);

                    UpdateTelemetryValue(chargePointId, "status", "Charging");
                    UpdateTelemetryValue(chargePointId, "active_user", startIdTag);
                    PublishServerTelemetry();

                    if (IsForcePowerActive(DateTime.UtcNow))
                    {
                        _ = Task.Run(() => DistributeForcePowerAsync());
                    }

                    responsePayload = new
                    {
                        transactionId = transId,
                        idTagInfo = new
                        {
                            status = "Accepted"
                        }
                    };
                    break;

                case "StopTransaction":
                    double stopMeter = payload.GetProperty("meterStop").GetDouble(); // in Wh
                    int stopTransId = payload.GetProperty("transactionId").GetInt32();

                    string stopIdTag = "Guest";
                    if (_activeTransactions.TryRemove(stopTransId, out var info))
                    {
                        stopIdTag = info.IdTag;
                        double consumedKwh = Math.Round((stopMeter - info.StartMeterValue) / 1000.0, 3);
                        if (consumedKwh < 0) consumedKwh = 0;

                        // Save transaction to DB
                        if (_billingStore != null)
                        {
                            _billingStore.RecordTransaction(stopTransId, info.ChargePointId, info.ConnectorId, info.IdTag, consumedKwh);
                        }
                        Log.Info($"[OCPP] [{chargePointId}] Transaction {stopTransId} completed. Consumed: {consumedKwh} kWh by {info.IdTag}");
                    }

                    UpdateTelemetryValue(chargePointId, "status", "Available");
                    UpdateTelemetryValue(chargePointId, "active_user", "None");
                    PublishServerTelemetry();

                    if (IsForcePowerActive(DateTime.UtcNow))
                    {
                        _ = Task.Run(() => DistributeForcePowerAsync());
                    }

                    responsePayload = new
                    {
                        idTagInfo = new
                        {
                            status = "Accepted"
                        }
                    };
                    break;

                case "MeterValues":
                    ProcessMeterValues(chargePointId, payload);
                    responsePayload = new { };
                    break;

                default:
                    Log.Warning($"[OCPP] [{chargePointId}] Unhandled action: {action}");
                    responsePayload = new { };
                    break;
            }

            if (responsePayload != null)
            {
                // Send response: [3, messageId, payload]
                string responseStr = $"[3,\"{messageId}\",{JsonSerializer.Serialize(responsePayload)}]";
                await SendMessageAsync(chargePointId, responseStr);
            }
        }

        private void ProcessMeterValues(string chargePointId, JsonElement payload)
        {
            try
            {
                var values = payload.GetProperty("meterValue");
                foreach (var value in values.EnumerateArray())
                {
                    var sampledValue = value.GetProperty("sampledValue");
                    foreach (var sample in sampledValue.EnumerateArray())
                    {
                        string valStr = sample.GetProperty("value").GetString() ?? "0";
                        double.TryParse(valStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val);

                        string measurand = "Energy.Active.Import.Register";
                        if (sample.TryGetProperty("measurand", out var mProp))
                            measurand = mProp.GetString() ?? measurand;

                        string unit = "Wh";
                        if (sample.TryGetProperty("unit", out var uProp))
                            unit = uProp.GetString() ?? unit;

                        if (measurand == "Power.Active.Import")
                        {
                            // Convert W to kW
                            double powerKw = unit.Equals("kW", StringComparison.OrdinalIgnoreCase) ? val : val / 1000.0;
                            UpdateTelemetryValue(chargePointId, "power", Math.Round(powerKw, 3));
                        }
                        else if (measurand == "Energy.Active.Import.Register")
                        {
                            // Convert Wh to kWh
                            double energyKwh = unit.Equals("kWh", StringComparison.OrdinalIgnoreCase) ? val : val / 1000.0;
                            UpdateTelemetryValue(chargePointId, "energy_import", Math.Round(energyKwh, 3));
                        }
                        else if (measurand == "Current.Import")
                        {
                            UpdateTelemetryValue(chargePointId, "current", Math.Round(val, 2));
                        }
                        else if (measurand == "Voltage")
                        {
                            UpdateTelemetryValue(chargePointId, "voltage", Math.Round(val, 1));
                        }
                    }
                }

                PublishServerTelemetry();
            }
            catch (Exception ex)
            {
                Log.Error($"[OCPP] [{chargePointId}] Failed to process meter values: {ex.Message}");
            }
        }

        public async Task<bool> SendMessageAsync(string chargePointId, string message)
        {
            if (_activeSockets.TryGetValue(chargePointId, out var socket) && socket.State == WebSocketState.Open)
            {
                var bytes = Encoding.UTF8.GetBytes(message);
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Resolves the maximum per-phase charge current (in Amperes) for the given
        /// charge point. Falls back to the fixed <see cref="DefaultMaxCurrentAmps"/>
        /// (16A) when no other value is available.
        /// </summary>
        public double GetMaxCurrentAmps(string chargePointId) => DefaultMaxCurrentAmps;

        /// <summary>
        /// Maximum total charge capacity used as the 100% reference for power_limit,
        /// expressed in "phase-amperes" (per-phase current x max phases = 16A x 3).
        /// </summary>
        public double GetMaxTotalCapacity(string chargePointId) => GetMaxCurrentAmps(chargePointId) * MaxPhases;

        /// <summary>
        /// Converts a per-phase charge current (A) into a percentage of the TOTAL power
        /// capacity (16A x 3 phases). The active <paramref name="phases"/> determines how
        /// much of the total capacity the per-phase current represents.
        /// </summary>
        public double AmpsToPercent(string chargePointId, double amps, int phases)
        {
            double maxTotal = GetMaxTotalCapacity(chargePointId);
            if (maxTotal <= 0) return 0.0;
            double total = amps * Math.Max(phases, 1);
            return Math.Clamp(Math.Round(total / maxTotal * 100.0, 1), 0.0, 100.0);
        }

        /// <summary>
        /// Converts a power_limit percentage (0-100) of the TOTAL power capacity into a
        /// per-phase charge current (A), given the active number of <paramref name="phases"/>.
        /// The result is capped at the per-phase maximum (16A).
        /// </summary>
        public double PercentToAmps(string chargePointId, double percent, int phases)
        {
            double maxTotal = GetMaxTotalCapacity(chargePointId);
            double totalAmps = Math.Clamp(percent, 0.0, 100.0) / 100.0 * maxTotal;
            double perPhase = totalAmps / Math.Max(phases, 1);
            return Math.Round(Math.Min(perPhase, GetMaxCurrentAmps(chargePointId)), 2);
        }

        /// <summary>
        /// Resolves a power_limit percentage (of total capacity, 16A x 3) into the
        /// per-phase current and phase count to actually apply:
        ///  - Below <see cref="MinChargePercent"/> (10%): charging is shut off (0A).
        ///  - At or above 10%: charge with at least the minimum (1 phase @ 6A), using
        ///    the most phases that keep each phase at or above <see cref="MinCurrentAmps"/>.
        /// </summary>
        public (double Amps, int Phases) ResolveLimit(string chargePointId, double percent)
        {
            double maxPerPhase = GetMaxCurrentAmps(chargePointId);
            double maxTotal = GetMaxTotalCapacity(chargePointId);

            // Below the cut-off threshold we shut off charging entirely.
            if (percent < MinChargePercent)
                return (0.0, 1);

            double totalAmps = Math.Clamp(percent, 0.0, 100.0) / 100.0 * maxTotal;

            // Use the most phases that still keep each phase at or above the minimum,
            // so we curtail by reducing phases before reducing below 6A per phase.
            for (int phases = MaxPhases; phases >= 1; phases--)
            {
                double perPhase = totalAmps / phases;
                if (perPhase >= MinCurrentAmps || phases == 1)
                {
                    // At/above 10% we always charge at least the minimum current.
                    perPhase = Math.Clamp(perPhase, MinCurrentAmps, maxPerPhase);
                    return (Math.Round(perPhase, 2), phases);
                }
            }

            return (MinCurrentAmps, 1);
        }

        // Dynamic charging curtailment (Smart Charging)
        public async Task<bool> SetChargingLimitAsync(string chargePointId, int connectorId, double maxCurrentAmps, int? numPhases = null)
        {
            Log.Info($"[OCPP] [{chargePointId}] Setting charge limit to {maxCurrentAmps}A" + (numPhases.HasValue ? $", Phases: {numPhases.Value}" : ""));
            if (numPhases.HasValue)
            {
                UpdateTelemetryValue(chargePointId, "charging_phases", (double)numPhases.Value);
            }
            else
            {
                var dict = GetTelemetry(chargePointId);
                if (dict.TryGetValue("charging_phases", out var phVal) && phVal is double ph)
                {
                    numPhases = (int)ph;
                }
            }

            int phases = numPhases ?? MaxPhases;

            // power_limit telemetry is exposed as a percentage of the TOTAL power
            // capacity (16A x 3 phases), taking the active phase count into account.
            UpdateTelemetryValue(chargePointId, "power_limit", AmpsToPercent(chargePointId, maxCurrentAmps, phases));

            string messageId = Guid.NewGuid().ToString("N")[..8];
            var profile = new
            {
                connectorId = connectorId,
                csChargingProfiles = new
                {
                    chargingProfileId = 1,
                    stackLevel = 0,
                    chargingProfilePurpose = "TxDefaultProfile",
                    chargingProfileKind = "Relative",
                    chargingSchedule = new
                    {
                        chargingRateUnit = "A",
                        chargingSchedulePeriod = new[]
                        {
                            new { startPeriod = 0, limit = maxCurrentAmps, numberPhases = phases }
                        }
                    }
                }
            };

            // Send call: [2, messageId, "SetChargingProfile", payload]
            string ocppMsg = $"[2,\"{messageId}\",\"SetChargingProfile\",{JsonSerializer.Serialize(profile)}]";
            return await SendMessageAsync(chargePointId, ocppMsg);
        }

        public async Task<bool> RemoteStartTransactionAsync(string chargePointId, int connectorId, string idTag)
        {
            string messageId = Guid.NewGuid().ToString("N")[..8];
            var payload = new
            {
                connectorId = connectorId,
                idTag = idTag
            };
            string ocppMsg = $"[2,\"{messageId}\",\"RemoteStartTransaction\",{JsonSerializer.Serialize(payload)}]";
            return await SendMessageAsync(chargePointId, ocppMsg);
        }

        public async Task<bool> RemoteStopTransactionAsync(string chargePointId, int transactionId)
        {
            string messageId = Guid.NewGuid().ToString("N")[..8];
            var payload = new
            {
                transactionId = transactionId
            };
            string ocppMsg = $"[2,\"{messageId}\",\"RemoteStopTransaction\",{JsonSerializer.Serialize(payload)}]";
            return await SendMessageAsync(chargePointId, ocppMsg);
        }

        // ── Server Telemetry & Force Power Setpoint ──────────────────────────

        public double ForcePowerValiditySeconds
        {
            get => _forcePowerValiditySeconds;
            set => _forcePowerValiditySeconds = value;
        }

        public bool IsForcePowerActive(DateTime now)
        {
            lock (_forcePowerLock)
            {
                if (_forcePowerKw <= 0) return false;
                return (now - _forcePowerSetAtUtc).TotalSeconds <= _forcePowerValiditySeconds;
            }
        }

        public double GetEffectiveForcePowerKw(DateTime now)
        {
            lock (_forcePowerLock)
            {
                if (_forcePowerKw > 0 && (now - _forcePowerSetAtUtc).TotalSeconds > _forcePowerValiditySeconds)
                {
                    _forcePowerKw = 0.0;
                    _forcePowerSetAtUtc = DateTime.MinValue;
                    Log.Info("[OCPP] Server force_power setpoint expired after 60s. Reverted to 0.0 (Normal mode).");
                    _ = Task.Run(() => DistributeForcePowerAsync());
                }
                return _forcePowerKw;
            }
        }

        public async Task SetForcePowerAsync(double value)
        {
            var now = DateTime.UtcNow;
            lock (_forcePowerLock)
            {
                _forcePowerKw = Math.Max(0.0, value);
                _forcePowerSetAtUtc = _forcePowerKw > 0 ? now : DateTime.MinValue;
            }
            Log.Info($"[OCPP] Server force_power set to {_forcePowerKw} kW (valid {_forcePowerValiditySeconds}s)");
            PublishServerTelemetry();
            await DistributeForcePowerAsync();
        }

        public Dictionary<string, object> GetServerTelemetry()
        {
            double totalPower = 0.0;
            double totalEnergy = 0.0;

            foreach (var kvp in _liveTelemetry)
            {
                var dict = kvp.Value;
                lock (dict)
                {
                    if (dict.TryGetValue("power", out var p) && p is double pd)
                        totalPower += pd;
                    if (dict.TryGetValue("energy_import", out var e) && e is double ed)
                        totalEnergy += ed;
                }
            }

            int activeSessions = _activeTransactions.Count;
            double forcePower = GetEffectiveForcePowerKw(DateTime.UtcNow);

            return new Dictionary<string, object>
            {
                [TelemetryKeys.PowerKw] = Math.Round(totalPower, 3),
                [TelemetryKeys.EnergyImportKwh] = Math.Round(totalEnergy, 3),
                [TelemetryKeys.ActiveSessions] = activeSessions,
                [TelemetryKeys.ForcePowerKw] = Math.Round(forcePower, 3)
            };
        }

        public void PublishServerTelemetry()
        {
            var telemetry = GetServerTelemetry();
            foreach (var kvp in telemetry)
            {
                OnServerTelemetryUpdated?.Invoke(kvp.Key, kvp.Value);
            }
        }

        private void CheckWatchdog(object? state)
        {
            var now = DateTime.UtcNow;
            bool expired = false;
            lock (_forcePowerLock)
            {
                if (_forcePowerKw > 0 && (now - _forcePowerSetAtUtc).TotalSeconds > _forcePowerValiditySeconds)
                {
                    _forcePowerKw = 0.0;
                    _forcePowerSetAtUtc = DateTime.MinValue;
                    expired = true;
                }
            }

            if (expired)
            {
                Log.Info("[OCPP] Server force_power setpoint expired after 60s. Reverted to 0.0 (Normal mode).");
                PublishServerTelemetry();
                _ = Task.Run(() => DistributeForcePowerAsync());
            }
        }

        /// <summary>
        /// Pure allocation algorithm: distributes a total target power (in kW) across
        /// all active charging sessions within the physical bounds of EV charging (IEC 61851):
        /// - Setpoint &lt;= 0: Unrestricted (16A, 3-phase = 100%).
        /// - Share &gt;= 4.14 kW: 3-phase allocation (6A to 16A).
        /// - 1.38 kW &lt;= Share &lt; 4.14 kW: 1-phase allocation (6A to 16A).
        /// - Share &lt; 1.38 kW: Total power cannot satisfy all cars at minimum 6A.
        ///   Allocates 1-phase to the oldest k = floor(P / 1.38) sessions, pausing the rest.
        /// </summary>
        public static List<SessionAllocation> ComputeAllocation(double forcePowerKw, IReadOnlyList<ActiveTransactionInfo> activeSessions)
        {
            var result = new List<SessionAllocation>();
            if (activeSessions == null || activeSessions.Count == 0)
                return result;

            int n = activeSessions.Count;

            // Normal / Unrestricted mode
            if (forcePowerKw <= 0.0)
            {
                foreach (var s in activeSessions)
                {
                    result.Add(new SessionAllocation(s.ChargePointId, s.ConnectorId, DefaultMaxCurrentAmps, MaxPhases, 11.04));
                }
                return result;
            }

            // Minimum power required per session:
            // 1-phase @ 6A = 1 * 230V * 6A = 1.38 kW
            // 3-phase @ 6A = 3 * 230V * 6A = 4.14 kW
            const double minPower1p = 1.38;
            const double minPower3p = 4.14;

            double share = forcePowerKw / n;

            if (share >= minPower3p)
            {
                // 3-phase allocation: P = 3 * 230 * I / 1000 = 0.69 * I  ==>  I = P / 0.69
                double amps = Math.Clamp(Math.Round(share / 0.69, 1), MinCurrentAmps, DefaultMaxCurrentAmps);
                foreach (var s in activeSessions)
                {
                    result.Add(new SessionAllocation(s.ChargePointId, s.ConnectorId, amps, 3, share));
                }
            }
            else if (share >= minPower1p)
            {
                // 1-phase allocation: P = 1 * 230 * I / 1000 = 0.23 * I  ==>  I = P / 0.23
                double amps = Math.Clamp(Math.Round(share / 0.23, 1), MinCurrentAmps, DefaultMaxCurrentAmps);
                foreach (var s in activeSessions)
                {
                    result.Add(new SessionAllocation(s.ChargePointId, s.ConnectorId, amps, 1, share));
                }
            }
            else
            {
                // Sub-minimum total power: cannot support all n sessions at 6A
                int k = Math.Min(n, (int)Math.Floor(forcePowerKw / minPower1p));

                if (k <= 0)
                {
                    // Cannot even support 1 car at 6A: pause all
                    foreach (var s in activeSessions)
                    {
                        result.Add(new SessionAllocation(s.ChargePointId, s.ConnectorId, 0.0, 1, 0.0));
                    }
                }
                else
                {
                    // Order sessions by StartedAtUtc (FIFO)
                    var sorted = activeSessions.OrderBy(s => s.StartedAtUtc).ToList();
                    double activeShare = forcePowerKw / k;
                    double amps = Math.Clamp(Math.Round(activeShare / 0.23, 1), MinCurrentAmps, DefaultMaxCurrentAmps);

                    for (int i = 0; i < sorted.Count; i++)
                    {
                        var s = sorted[i];
                        if (i < k)
                        {
                            result.Add(new SessionAllocation(s.ChargePointId, s.ConnectorId, amps, 1, activeShare));
                        }
                        else
                        {
                            result.Add(new SessionAllocation(s.ChargePointId, s.ConnectorId, 0.0, 1, 0.0));
                        }
                    }
                }
            }

            return result;
        }

        public async Task DistributeForcePowerAsync()
        {
            var now = DateTime.UtcNow;
            double forcePower = GetEffectiveForcePowerKw(now);
            var sessions = _activeTransactions.Values.ToList();

            var allocations = ComputeAllocation(forcePower, sessions);

            foreach (var alloc in allocations)
            {
                try
                {
                    await SetChargingLimitAsync(alloc.ChargePointId, alloc.ConnectorId, alloc.Amps, alloc.Phases);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[OCPP] Failed to apply allocation to '{alloc.ChargePointId}': {ex.Message}");
                }
            }
        }

        // ── Test helpers ─────────────────────────────────────────────────────

        public void RegisterTransactionForTest(int transId, ActiveTransactionInfo info)
        {
            _activeTransactions[transId] = info;
        }

        public void ClearTransactionsForTest()
        {
            _activeTransactions.Clear();
            _liveTelemetry.Clear();
            lock (_forcePowerLock)
            {
                _forcePowerKw = 0.0;
                _forcePowerSetAtUtc = DateTime.MinValue;
            }
        }

        public void SetLiveTelemetryForTest(string chargePointId, string key, object value)
        {
            UpdateTelemetryValue(chargePointId, key, value);
        }
    }

    public record ActiveTransactionInfo(
        string ChargePointId,
        int ConnectorId,
        string IdTag,
        double StartMeterValue,
        DateTime StartedAtUtc = default)
    {
        public DateTime StartedAtUtc { get; init; } = StartedAtUtc == default ? DateTime.UtcNow : StartedAtUtc;
    }

    public record SessionAllocation(
        string ChargePointId,
        int ConnectorId,
        double Amps,
        int Phases,
        double TargetPowerKw);
}
