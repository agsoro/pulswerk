using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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
        private double _forcePowerValiditySeconds = 0.0;
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
                lock (dict)
                {
                    var res = new Dictionary<string, object>(dict);
                    if (!res.ContainsKey("charging_phases"))
                        res["charging_phases"] = (double)MaxPhases;
                    return res;
                }
            }
            return new Dictionary<string, object>
            {
                ["status"] = "Unavailable",
                ["power"] = 0.0,
                ["energy_import"] = 0.0,
                ["current"] = 0.0,
                ["voltage"] = 0.0,
                ["active_user"] = "None",
                ["charging_phases"] = (double)MaxPhases
            };
        }

        // Handles WebSocket connection loop
        public async Task HandleConnectionAsync(string chargePointId, WebSocket socket, CancellationToken ct)
        {
            Log.Info($"[OCPP] Charger '{chargePointId}' connecting...");
            
            // Disconnect old socket if exists
            if (_activeSockets.TryRemove(chargePointId, out var oldSocket))
            {
                Log.Info($"[OCPP] Charger '{chargePointId}' replacing existing active WebSocket connection.");
                try { await oldSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Replaced", CancellationToken.None); } catch { }
                // The replaced socket is no longer referenced anywhere — dispose it instead of
                // leaving it for the finalizer (it holds an unmanaged socket handle + buffers).
                try { oldSocket.Dispose(); } catch { }
            }

            _activeSockets[chargePointId] = socket;
            
            // Set initial telemetry as connected
            UpdateTelemetryValue(chargePointId, "status", "Connected");
            Log.Info($"[OCPP] Charger '{chargePointId}' connected successfully.");

            _ = Task.Run(async () =>
            {
                await Task.Delay(500);
                await ConfigureChargerTelemetryAsync(chargePointId);
                if (IsForcePowerActive(DateTime.UtcNow))
                {
                    await DistributeForcePowerAsync();
                }
            });

            var buffer = new byte[8192];
            try
            {
                while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        if (result.MessageType == WebSocketMessageType.Close)
                            break;

                        ms.Write(buffer, 0, result.Count);

                        // Prevent unbounded memory allocation (max 2MB per message)
                        if (ms.Length > 2 * 1024 * 1024)
                        {
                            Log.Warning($"[OCPP] [{chargePointId}] Message exceeded 2MB limit. Closing connection.");
                            await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Message too big", CancellationToken.None);
                            return;
                        }
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
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
                    await HandleOcppCallAsync(chargePointId, messageId, action, payload);
                }
                // MessageType 3 = CALLRESULT
                else if (messageType == 3)
                {
                    string payloadStr = root.GetArrayLength() > 2 ? root[2].ToString() : "";
                    Log.Info($"[OCPP] [{chargePointId}] Received CallResult for message {messageId}: {payloadStr}");
                }
                // MessageType 4 = CALLERROR
                else if (messageType == 4)
                {
                    string errorCode = root.GetArrayLength() > 2 ? root[2].GetString() ?? "" : "";
                    string errorDesc = root.GetArrayLength() > 3 ? root[3].GetString() ?? "" : "";
                    Log.Warning($"[OCPP] [{chargePointId}] Received CallError for message {messageId}: {errorCode} - {errorDesc}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[OCPP] [{chargePointId}] Error parsing message: {ex.Message}");
            }
        }

        public async Task HandleOcppCallAsync(string chargePointId, string messageId, string action, JsonElement payload)
        {
            Log.Info($"[OCPP] [{chargePointId}] Action: {action}");

            object? responsePayload = null;

            switch (action)
            {
                case "BootNotification":
                    string vendor = payload.TryGetProperty("chargePointVendor", out var v) ? v.GetString() ?? "" : "";
                    string model = payload.TryGetProperty("chargePointModel", out var m) ? m.GetString() ?? "" : "";
                    string fw = payload.TryGetProperty("firmwareVersion", out var f) ? f.GetString() ?? "" : "";
                    Log.Info($"[OCPP] [{chargePointId}] BootNotification: Vendor='{vendor}', Model='{model}', Firmware='{fw}'");
                    UpdateTelemetryValue(chargePointId, "status", "Available");
                    responsePayload = new
                    {
                        status = "Accepted",
                        currentTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        interval = 60
                    };
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(500);
                        await ConfigureChargerTelemetryAsync(chargePointId);
                        if (IsForcePowerActive(DateTime.UtcNow))
                        {
                            await DistributeForcePowerAsync();
                        }
                    });
                    break;

                case "Heartbeat":
                    responsePayload = new
                    {
                        currentTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                    };
                    break;

                case "StatusNotification":
                    string status = "Available";
                    if (payload.TryGetProperty("status", out var stProp) || payload.TryGetProperty("Status", out stProp))
                    {
                        status = stProp.GetString() ?? "Available";
                    }
                    UpdateTelemetryValue(chargePointId, "status", status);
                    if (status.Equals("Available", StringComparison.OrdinalIgnoreCase) ||
                        status.Equals("Faulted", StringComparison.OrdinalIgnoreCase) ||
                        status.Equals("Unavailable", StringComparison.OrdinalIgnoreCase) ||
                        status.Equals("SuspendedEV", StringComparison.OrdinalIgnoreCase) ||
                        status.Equals("SuspendedEVSE", StringComparison.OrdinalIgnoreCase))
                    {
                        UpdateTelemetryValue(chargePointId, "power", 0.0);
                        UpdateTelemetryValue(chargePointId, "current", 0.0);
                    }
                    responsePayload = new { };
                    break;

                case "Authorize":
                    string idTag = payload.GetProperty("idTag").GetString() ?? "";
                    bool isAuthorized = _billingStore == null || _billingStore.IsRfidValid(idTag);
                    Log.Info($"[OCPP] [{chargePointId}] Authorize tag '{idTag}': {(isAuthorized ? "Accepted" : "Blocked")}");
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
                    int connectorId = 1;
                    if (payload.TryGetProperty("connectorId", out var cProp) || payload.TryGetProperty("ConnectorId", out cProp))
                    {
                        if (cProp.ValueKind == JsonValueKind.Number) connectorId = cProp.GetInt32();
                        else if (int.TryParse(cProp.GetString(), out int cid)) connectorId = cid;
                    }

                    string startIdTag = "Guest";
                    if (payload.TryGetProperty("idTag", out var idProp) || payload.TryGetProperty("IdTag", out idProp))
                    {
                        startIdTag = idProp.GetString() ?? "Guest";
                    }

                    double startMeter = 0.0;
                    if (payload.TryGetProperty("meterStart", out var msProp) || payload.TryGetProperty("MeterStart", out msProp))
                    {
                        if (msProp.ValueKind == JsonValueKind.Number) startMeter = msProp.GetDouble();
                        else if (double.TryParse(msProp.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double sm))
                            startMeter = sm;
                    }

                    int transId = Interlocked.Increment(ref _transactionIdCounter);
                    _activeTransactions[transId] = new ActiveTransactionInfo(chargePointId, connectorId, startIdTag, startMeter, DateTime.UtcNow);

                    Log.Info($"[OCPP] [{chargePointId}] StartTransaction {transId} on connector {connectorId} by '{startIdTag}' (meterStart: {startMeter} Wh)");
                    UpdateTelemetryValue(chargePointId, "status", "Charging");
                    UpdateTelemetryValue(chargePointId, "active_user", startIdTag);
                    UpdateTelemetryValue(chargePointId, "energy_import", Math.Round(startMeter / 1000.0, 3));
                    PublishServerTelemetry();

                    if (IsForcePowerActive(DateTime.UtcNow))
                    {
                        _ = Task.Run(() => DistributeForcePowerAsync());
                    }

                    _ = Task.Run(async () =>
                    {
                        await ConfigureChargerTelemetryAsync(chargePointId);
                        await Task.Delay(1000);
                        await TriggerMessageAsync(chargePointId, "MeterValues", connectorId);
                    });

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
                    double stopMeter = 0.0;
                    if (payload.TryGetProperty("meterStop", out var stpProp) || payload.TryGetProperty("MeterStop", out stpProp))
                    {
                        if (stpProp.ValueKind == JsonValueKind.Number) stopMeter = stpProp.GetDouble();
                        else if (double.TryParse(stpProp.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double sm))
                            stopMeter = sm;
                    }

                    int stopTransId = 0;
                    if (payload.TryGetProperty("transactionId", out var tidProp) || payload.TryGetProperty("TransactionId", out tidProp))
                    {
                        if (tidProp.ValueKind == JsonValueKind.Number) stopTransId = tidProp.GetInt32();
                        else if (int.TryParse(tidProp.GetString(), out int tid)) stopTransId = tid;
                    }

                    if (payload.TryGetProperty("transactionData", out var txData) || payload.TryGetProperty("TransactionData", out txData))
                    {
                        ProcessMeterValues(chargePointId, txData);
                    }

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
                    UpdateTelemetryValue(chargePointId, "power", 0.0);
                    UpdateTelemetryValue(chargePointId, "current", 0.0);
                    if (stopMeter > 0)
                    {
                        UpdateTelemetryValue(chargePointId, "energy_import", Math.Round(stopMeter / 1000.0, 3));
                    }
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

        public void ProcessMeterValues(string chargePointId, JsonElement payload)
        {
            try
            {
                // 1. Locate the array of meter values
                JsonElement meterValuesArray = default;
                if (payload.ValueKind == JsonValueKind.Array)
                {
                    meterValuesArray = payload;
                }
                else if (payload.ValueKind == JsonValueKind.Object)
                {
                    if (!payload.TryGetProperty("meterValue", out meterValuesArray) &&
                        !payload.TryGetProperty("MeterValue", out meterValuesArray) &&
                        !payload.TryGetProperty("transactionData", out meterValuesArray) &&
                        !payload.TryGetProperty("TransactionData", out meterValuesArray))
                    {
                        foreach (var prop in payload.EnumerateObject())
                        {
                            if (string.Equals(prop.Name, "meterValue", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(prop.Name, "transactionData", StringComparison.OrdinalIgnoreCase))
                            {
                                meterValuesArray = prop.Value;
                                break;
                            }
                        }
                    }
                }

                if (meterValuesArray.ValueKind != JsonValueKind.Array)
                {
                    Log.Debug($"[OCPP] [{chargePointId}] No meterValue array found in payload.");
                    return;
                }

                double? explicitTotalPowerKw = null;
                var phasePowersKw = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                double? explicitTotalEnergyKwh = null;
                var phaseCurrents = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                double? maxCurrent = null;
                var phaseVoltages = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                double? maxVoltage = null;
                var activePhases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var valueItem in meterValuesArray.EnumerateArray())
                {
                    JsonElement sampledValueArray = default;
                    if (valueItem.ValueKind == JsonValueKind.Object)
                    {
                        if (!valueItem.TryGetProperty("sampledValue", out sampledValueArray) &&
                            !valueItem.TryGetProperty("SampledValue", out sampledValueArray))
                        {
                            foreach (var prop in valueItem.EnumerateObject())
                            {
                                if (string.Equals(prop.Name, "sampledValue", StringComparison.OrdinalIgnoreCase))
                                {
                                    sampledValueArray = prop.Value;
                                    break;
                                }
                            }
                        }
                    }

                    if (sampledValueArray.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var sample in sampledValueArray.EnumerateArray())
                    {
                        if (sample.ValueKind != JsonValueKind.Object)
                            continue;

                        // Value extraction (handles string and number)
                        string valStr = "0";
                        if (sample.TryGetProperty("value", out var vProp) || sample.TryGetProperty("Value", out vProp))
                        {
                            valStr = vProp.ValueKind switch
                            {
                                JsonValueKind.String => vProp.GetString() ?? "0",
                                JsonValueKind.Number => vProp.GetRawText(),
                                _ => vProp.ToString()
                            };
                        }

                        // If comma-separated (e.g. "0.000,0.000"), take first token
                        if (valStr.Contains(','))
                        {
                            if (valStr.Contains('.') || valStr.IndexOf(',') != valStr.LastIndexOf(','))
                            {
                                valStr = valStr.Split(',')[0].Trim();
                            }
                            else
                            {
                                // German decimal comma: "230,30" -> "230.30"
                                valStr = valStr.Replace(',', '.').Trim();
                            }
                        }

                        if (!double.TryParse(valStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
                        {
                            continue;
                        }

                        // Measurand extraction (case-insensitive)
                        string measurand = "Energy.Active.Import.Register";
                        if (sample.TryGetProperty("measurand", out var mProp) || sample.TryGetProperty("Measurand", out mProp))
                        {
                            measurand = mProp.GetString() ?? measurand;
                        }

                        // Unit extraction
                        string unit = "";
                        if (sample.TryGetProperty("unit", out var uProp) || sample.TryGetProperty("Unit", out uProp))
                        {
                            unit = uProp.GetString() ?? "";
                        }

                        // Phase extraction
                        string? phase = null;
                        if (sample.TryGetProperty("phase", out var pProp) || sample.TryGetProperty("Phase", out pProp))
                        {
                            phase = pProp.GetString()?.Trim();
                        }

                        if (measurand.Equals("Power.Active.Import", StringComparison.OrdinalIgnoreCase) ||
                            measurand.Equals("Power.Active.Import.Register", StringComparison.OrdinalIgnoreCase))
                        {
                            double powerKw = unit.Equals("kW", StringComparison.OrdinalIgnoreCase) ? val : val / 1000.0;
                            if (string.IsNullOrEmpty(phase))
                            {
                                explicitTotalPowerKw = powerKw;
                            }
                            else
                            {
                                phasePowersKw[phase] = powerKw;
                            }
                        }
                        else if (measurand.Equals("Energy.Active.Import.Register", StringComparison.OrdinalIgnoreCase) ||
                                 measurand.Equals("Energy.Active.Import", StringComparison.OrdinalIgnoreCase))
                        {
                            double energyKwh = unit.Equals("kWh", StringComparison.OrdinalIgnoreCase) ? val : val / 1000.0;
                            if (string.IsNullOrEmpty(phase) || !explicitTotalEnergyKwh.HasValue)
                            {
                                explicitTotalEnergyKwh = energyKwh;
                            }
                        }
                        else if (measurand.Equals("Current.Import", StringComparison.OrdinalIgnoreCase) ||
                                 measurand.Equals("Current.Offered", StringComparison.OrdinalIgnoreCase))
                        {
                            // Current in Amps
                            double curAmps = unit.Equals("mA", StringComparison.OrdinalIgnoreCase) ? val / 1000.0 : val;
                            maxCurrent = maxCurrent.HasValue ? Math.Max(maxCurrent.Value, curAmps) : curAmps;

                            if (!string.IsNullOrEmpty(phase))
                            {
                                phaseCurrents[phase] = curAmps;
                                if (curAmps > 0.2 && (phase.StartsWith("L", StringComparison.OrdinalIgnoreCase) ||
                                                      phase.Contains("1") || phase.Contains("2") || phase.Contains("3")))
                                {
                                    activePhases.Add(phase);
                                }
                            }
                        }
                        else if (measurand.Equals("Voltage", StringComparison.OrdinalIgnoreCase))
                        {
                            maxVoltage = maxVoltage.HasValue ? Math.Max(maxVoltage.Value, val) : val;
                            if (!string.IsNullOrEmpty(phase))
                            {
                                phaseVoltages[phase] = val;
                            }
                        }
                    }
                }

                // Calculate total power
                double? resolvedPowerKw = null;
                if (explicitTotalPowerKw.HasValue)
                {
                    resolvedPowerKw = explicitTotalPowerKw.Value;
                }
                else if (phasePowersKw.Count > 0)
                {
                    resolvedPowerKw = phasePowersKw.Values.Sum();
                }
                else if (maxCurrent.HasValue && maxCurrent.Value > 0)
                {
                    // Calculate power from Current and Voltage
                    double defaultV = (maxVoltage.HasValue && maxVoltage.Value > 50.0) ? maxVoltage.Value : 230.0;
                    if (phaseCurrents.Count > 0)
                    {
                        double totalW = 0.0;
                        foreach (var (ph, cur) in phaseCurrents)
                        {
                            double v = phaseVoltages.GetValueOrDefault(ph, defaultV);
                            totalW += cur * v;
                        }
                        resolvedPowerKw = totalW / 1000.0;
                    }
                    else
                    {
                        int phCount = activePhases.Count > 0 ? activePhases.Count : 1;
                        resolvedPowerKw = (maxCurrent.Value * defaultV * phCount) / 1000.0;
                    }
                }

                // Commit telemetry updates
                if (resolvedPowerKw.HasValue)
                {
                    UpdateTelemetryValue(chargePointId, "power", Math.Round(resolvedPowerKw.Value, 3));
                }

                if (explicitTotalEnergyKwh.HasValue)
                {
                    UpdateTelemetryValue(chargePointId, "energy_import", Math.Round(explicitTotalEnergyKwh.Value, 3));
                }

                if (maxCurrent.HasValue)
                {
                    UpdateTelemetryValue(chargePointId, "current", Math.Round(maxCurrent.Value, 2));
                }

                if (maxVoltage.HasValue)
                {
                    UpdateTelemetryValue(chargePointId, "voltage", Math.Round(maxVoltage.Value, 1));
                }

                if (activePhases.Count > 0)
                {
                    UpdateTelemetryValue(chargePointId, "charging_phases", (double)Math.Min(activePhases.Count, MaxPhases));
                }

                // If actively drawing power, ensure status is "Charging"
                if ((resolvedPowerKw.HasValue && resolvedPowerKw.Value > 0.1) || (maxCurrent.HasValue && maxCurrent.Value > 0.5))
                {
                    var curTelemetry = GetTelemetry(chargePointId);
                    string curStatus = curTelemetry.GetValueOrDefault("status", "")?.ToString() ?? "";
                    if (!curStatus.Equals("Charging", StringComparison.OrdinalIgnoreCase))
                    {
                        UpdateTelemetryValue(chargePointId, "status", "Charging");
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

            // Find active transaction ID if any
            int? activeTxId = null;
            foreach (var kvp in _activeTransactions)
            {
                if (string.Equals(kvp.Value.ChargePointId, chargePointId, StringComparison.OrdinalIgnoreCase)
                    && (connectorId == 0 || kvp.Value.ConnectorId == connectorId))
                {
                    activeTxId = kvp.Key;
                    if (connectorId == 0) connectorId = kvp.Value.ConnectorId;
                    break;
                }
            }

            // If unrestricted (16A and no force_power limit active), also clear existing profiles
            if (maxCurrentAmps >= DefaultMaxCurrentAmps && _forcePowerKw <= 0.0)
            {
                _ = ClearChargingProfileAsync(chargePointId, 0);
            }

            int targetConnector = connectorId > 0 ? connectorId : 1;

            // 1. Send ChargePointMaxProfile on connectorId: 0 (takes effect immediately across the hardware)
            bool okMax = await SendChargingProfileMessageAsync(
                chargePointId,
                connectorId: 0,
                profileId: 1,
                stackLevel: 1,
                purpose: "ChargePointMaxProfile",
                kind: "Absolute",
                limitAmps: maxCurrentAmps,
                phases: phases);

            // 2. Send TxDefaultProfile on connectorId (serves as default for upcoming transactions)
            bool okDefault = await SendChargingProfileMessageAsync(
                chargePointId,
                connectorId: targetConnector,
                profileId: 2,
                stackLevel: 1,
                purpose: "TxDefaultProfile",
                kind: "Absolute",
                limitAmps: maxCurrentAmps,
                phases: phases);

            // 3. If a transaction is actively ongoing, send TxProfile (required by OCPP 1.6 for live sessions)
            bool okTx = true;
            if (activeTxId.HasValue)
            {
                okTx = await SendChargingProfileMessageAsync(
                    chargePointId,
                    connectorId: targetConnector,
                    profileId: 3,
                    stackLevel: 2,
                    purpose: "TxProfile",
                    kind: "Relative",
                    limitAmps: maxCurrentAmps,
                    phases: phases,
                    transactionId: activeTxId.Value);
            }

            return okMax || okDefault || okTx;
        }

        public async Task<bool> ClearChargingProfileAsync(string chargePointId, int connectorId = 0)
        {
            string messageId = Guid.NewGuid().ToString("N")[..8];
            var payload = new { connectorId = connectorId };
            string ocppMsg = $"[2,\"{messageId}\",\"ClearChargingProfile\",{JsonSerializer.Serialize(payload)}]";
            Log.Debug($"[OCPP] [{chargePointId}] Sending ClearChargingProfile on connector {connectorId}");
            return await SendMessageAsync(chargePointId, ocppMsg);
        }

        private async Task<bool> SendChargingProfileMessageAsync(
            string chargePointId,
            int connectorId,
            int profileId,
            int stackLevel,
            string purpose,
            string kind,
            double limitAmps,
            int phases,
            int? transactionId = null)
        {
            string messageId = Guid.NewGuid().ToString("N")[..8];
            object profile;
            if (transactionId.HasValue)
            {
                profile = new
                {
                    connectorId = connectorId,
                    csChargingProfiles = new
                    {
                        chargingProfileId = profileId,
                        stackLevel = stackLevel,
                        chargingProfilePurpose = purpose,
                        chargingProfileKind = kind,
                        transactionId = transactionId.Value,
                        chargingSchedule = new
                        {
                            chargingRateUnit = "A",
                            chargingSchedulePeriod = new[]
                            {
                                new { startPeriod = 0, limit = Math.Round(limitAmps, 1), numberPhases = phases }
                            }
                        }
                    }
                };
            }
            else
            {
                profile = new
                {
                    connectorId = connectorId,
                    csChargingProfiles = new
                    {
                        chargingProfileId = profileId,
                        stackLevel = stackLevel,
                        chargingProfilePurpose = purpose,
                        chargingProfileKind = kind,
                        chargingSchedule = new
                        {
                            chargingRateUnit = "A",
                            chargingSchedulePeriod = new[]
                            {
                                new { startPeriod = 0, limit = Math.Round(limitAmps, 1), numberPhases = phases }
                            }
                        }
                    }
                };
            }

            string ocppMsg = $"[2,\"{messageId}\",\"SetChargingProfile\",{JsonSerializer.Serialize(profile)}]";
            Log.Debug($"[OCPP] [{chargePointId}] Sending SetChargingProfile ({purpose}): {limitAmps}A, {phases}ph on connector {connectorId}" + (transactionId.HasValue ? $", txId={transactionId.Value}" : ""));
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

        public async Task<bool> ChangeConfigurationAsync(string chargePointId, string key, string value)
        {
            string messageId = Guid.NewGuid().ToString("N")[..8];
            var payload = new { key = key, value = value };
            string ocppMsg = $"[2,\"{messageId}\",\"ChangeConfiguration\",{JsonSerializer.Serialize(payload)}]";
            Log.Debug($"[OCPP] [{chargePointId}] Sending ChangeConfiguration: {key}={value}");
            return await SendMessageAsync(chargePointId, ocppMsg);
        }

        public async Task<bool> TriggerMessageAsync(string chargePointId, string requestedMessage, int connectorId = 1)
        {
            string messageId = Guid.NewGuid().ToString("N")[..8];
            var payload = new { requestedMessage = requestedMessage, connectorId = connectorId };
            string ocppMsg = $"[2,\"{messageId}\",\"TriggerMessage\",{JsonSerializer.Serialize(payload)}]";
            Log.Debug($"[OCPP] [{chargePointId}] Sending TriggerMessage: {requestedMessage} on connector {connectorId}");
            return await SendMessageAsync(chargePointId, ocppMsg);
        }

        public async Task ConfigureChargerTelemetryAsync(string chargePointId)
        {
            try
            {
                Log.Info($"[OCPP] [{chargePointId}] Configuring meter value sample interval and sampled measurands...");
                // 1. Set sample interval to 10s (standard for responsive monitoring)
                await ChangeConfigurationAsync(chargePointId, "MeterValueSampleInterval", "10");
                // 2. Request essential measurands for real-time telemetry
                await ChangeConfigurationAsync(chargePointId, "MeterValuesSampledData", "Energy.Active.Import.Register,Power.Active.Import,Current.Import,Voltage");
                // 3. Ensure stop transaction message also carries these measurands
                await ChangeConfigurationAsync(chargePointId, "StopTxnSampledData", "Energy.Active.Import.Register,Power.Active.Import,Current.Import,Voltage");
                // 4. Clock-aligned measurands
                await ChangeConfigurationAsync(chargePointId, "MeterValuesAlignedData", "Energy.Active.Import.Register,Power.Active.Import,Current.Import,Voltage");
            }
            catch (Exception ex)
            {
                Log.Warning($"[OCPP] [{chargePointId}] Error configuring telemetry: {ex.Message}");
            }
        }

        // ── Server Telemetry & Force Power Setpoint ──────────────────────────

        public const double DefaultManualValiditySeconds = 10 * 3600; // 10 hours (36,000 seconds)

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
                if (_forcePowerValiditySeconds <= 0) return true;
                return (now - _forcePowerSetAtUtc).TotalSeconds <= _forcePowerValiditySeconds;
            }
        }

        public double GetEffectiveForcePowerKw(DateTime now)
        {
            lock (_forcePowerLock)
            {
                if (_forcePowerValiditySeconds > 0 && _forcePowerKw > 0 && (now - _forcePowerSetAtUtc).TotalSeconds > _forcePowerValiditySeconds)
                {
                    _forcePowerKw = 0.0;
                    _forcePowerSetAtUtc = DateTime.MinValue;
                    Log.Info("[OCPP] Server force_power setpoint expired. Reverted to 0.0 (Normal mode).");
                    _ = Task.Run(() => DistributeForcePowerAsync());
                }
                return _forcePowerKw;
            }
        }

        public async Task SetForcePowerAsync(double value, double validitySeconds = 0.0)
        {
            var now = DateTime.UtcNow;
            lock (_forcePowerLock)
            {
                _forcePowerKw = Math.Max(0.0, value);
                _forcePowerSetAtUtc = _forcePowerKw > 0 ? now : DateTime.MinValue;
                _forcePowerValiditySeconds = validitySeconds;
            }
            Log.Info($"[OCPP] Server force_power set to {_forcePowerKw} kW" + (_forcePowerValiditySeconds > 0 ? $" (valid {_forcePowerValiditySeconds}s)" : " (permanent)"));
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
                if (_forcePowerValiditySeconds > 0 && _forcePowerKw > 0 && (now - _forcePowerSetAtUtc).TotalSeconds > _forcePowerValiditySeconds)
                {
                    _forcePowerKw = 0.0;
                    _forcePowerSetAtUtc = DateTime.MinValue;
                    expired = true;
                }
            }

            if (expired)
            {
                Log.Info("[OCPP] Server force_power setpoint expired. Reverted to 0.0 (Normal mode).");
                PublishServerTelemetry();
                _ = Task.Run(() => DistributeForcePowerAsync());
            }

            // Periodic trigger for active charging sessions: request live MeterValues
            foreach (var cpId in _activeSockets.Keys)
            {
                var telem = GetTelemetry(cpId);
                string currentStatus = telem.GetValueOrDefault("status", "")?.ToString() ?? "";
                bool hasActiveSession = _activeTransactions.Values.Any(t => string.Equals(t.ChargePointId, cpId, StringComparison.OrdinalIgnoreCase));
                if (hasActiveSession || currentStatus.Equals("Charging", StringComparison.OrdinalIgnoreCase))
                {
                    _ = TriggerMessageAsync(cpId, "MeterValues");
                }
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
                double rawAmps = share / 0.69;
                if (Math.Abs(rawAmps - DefaultMaxCurrentAmps) < 0.2) rawAmps = DefaultMaxCurrentAmps;
                double amps = Math.Clamp(Math.Round(rawAmps, 1), MinCurrentAmps, DefaultMaxCurrentAmps);
                foreach (var s in activeSessions)
                {
                    result.Add(new SessionAllocation(s.ChargePointId, s.ConnectorId, amps, 3, share));
                }
            }
            else if (share >= minPower1p)
            {
                // 1-phase allocation: P = 1 * 230 * I / 1000 = 0.23 * I  ==>  I = P / 0.23
                double rawAmps = share / 0.23;
                if (Math.Abs(rawAmps - DefaultMaxCurrentAmps) < 0.2) rawAmps = DefaultMaxCurrentAmps;
                double amps = Math.Clamp(Math.Round(rawAmps, 1), MinCurrentAmps, DefaultMaxCurrentAmps);
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

            // Check for any connected charge points reporting "Charging" status even if not in _activeTransactions
            var activeCpIds = new HashSet<string>(sessions.Select(s => s.ChargePointId), StringComparer.OrdinalIgnoreCase);
            foreach (var cpId in _activeSockets.Keys)
            {
                if (!activeCpIds.Contains(cpId))
                {
                    var telem = GetTelemetry(cpId);
                    string status = telem.GetValueOrDefault("status", "")?.ToString() ?? "";
                    if (status.Equals("Charging", StringComparison.OrdinalIgnoreCase))
                    {
                        sessions.Add(new ActiveTransactionInfo(cpId, 1, "Active", 0.0, DateTime.UtcNow));
                        activeCpIds.Add(cpId);
                    }
                }
            }

            // If no active charging sessions are found, but wallboxes are connected,
            // allocate across all connected wallboxes so hardware/defaults enforce the limit ahead of time.
            if (sessions.Count == 0 && _activeSockets.Count > 0)
            {
                foreach (var cpId in _activeSockets.Keys)
                {
                    sessions.Add(new ActiveTransactionInfo(cpId, 1, "Standby", 0.0, DateTime.UtcNow));
                }
            }

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
                _forcePowerValiditySeconds = 0.0;
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
