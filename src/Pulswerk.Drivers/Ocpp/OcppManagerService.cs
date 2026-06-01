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

        // Dynamic charging curtailment (Smart Charging)
        public async Task<bool> SetChargingLimitAsync(string chargePointId, int connectorId, double maxCurrentAmps, int? numPhases = null)
        {
            Log.Info($"[OCPP] [{chargePointId}] Setting charge limit to {maxCurrentAmps}A" + (numPhases.HasValue ? $", Phases: {numPhases.Value}" : ""));
            UpdateTelemetryValue(chargePointId, "power_limit", maxCurrentAmps);
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

            int phases = numPhases ?? 3;

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
