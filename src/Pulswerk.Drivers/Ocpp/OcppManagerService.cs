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

        private OcppManagerService() { }

        public void Initialize(BillingStore billingStore)
        {
            _billingStore = billingStore;
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
                    _activeTransactions[transId] = new ActiveTransactionInfo(chargePointId, connectorId, startIdTag, startMeter);

                    UpdateTelemetryValue(chargePointId, "status", "Charging");
                    UpdateTelemetryValue(chargePointId, "active_user", startIdTag);

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
    }

    public record ActiveTransactionInfo(string ChargePointId, int ConnectorId, string IdTag, double StartMeterValue);
}
