using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OcppSim
{
    class Program
    {
        private static string _chargePointId = "sim-01";
        private static string _serverUrl = "ws://localhost:5000/plswk/ocpp/sim-01";
        private static ClientWebSocket _ws = new();
        private static int _msgIdCounter = 100;
        private static int? _transactionId = null;
        private static double _meterValueWh = 10000.0;
        private static bool _chargingActive = false;
        private static double _currentLimit = 16.0;
        private static int _chargingPhases = 3;
        private static string _activeRfid = "None";
        private static readonly CancellationTokenSource _cts = new();

        static async Task Main(string[] args)
        {
            if (args.Length > 0) _chargePointId = args[0];
            if (args.Length > 1) _serverUrl = args[1];

            Console.WriteLine($"[OcppSim] ChargePointId: '{_chargePointId}'");
            Console.WriteLine($"[OcppSim] Server URL: '{_serverUrl}'");

            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    _ws = new ClientWebSocket();
                    Console.WriteLine("[OcppSim] Connecting to server...");
                    await _ws.ConnectAsync(new Uri(_serverUrl), _cts.Token);
                    Console.WriteLine("[OcppSim] Connected!");

                    // Start receive loop
                    var receiveTask = ReceiveLoopAsync(_ws, _cts.Token);

                    // Send BootNotification
                    await SendCallAsync("BootNotification", new
                    {
                        chargePointVendor = "PulswerkSim-CS",
                        chargePointModel = "CS-ModelX-2026",
                        firmwareVersion = "2.0.0"
                    });

                    // Send StatusNotification
                    await SendCallAsync("StatusNotification", new
                    {
                        connectorId = 1,
                        errorCode = "NoError",
                        status = "Available"
                    });

                    // Start simulation loop (Meter values / telemetry updates)
                    var simTask = SimulationLoopAsync(_cts.Token);

                    await Task.WhenAll(receiveTask, simTask);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[OcppSim] Error: {ex.Message}");
                    if (!_cts.IsCancellationRequested)
                    {
                        Console.WriteLine("[OcppSim] Reconnecting in 5 seconds...");
                        await Task.Delay(5000);
                    }
                }
            }
        }

        private static async Task SendMessageAsync(string message)
        {
            if (_ws.State == WebSocketState.Open)
            {
                var bytes = Encoding.UTF8.GetBytes(message);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        private static async Task<string> SendCallAsync(string action, object payload)
        {
            string msgId = Interlocked.Increment(ref _msgIdCounter).ToString();
            var msg = new object[] { 2, msgId, action, payload };
            string serialized = JsonSerializer.Serialize(msg);
            Console.WriteLine($"[OcppSim] [Sent CALL] {action}: {serialized}");
            await SendMessageAsync(serialized);
            return msgId;
        }

        private static async Task SendCallResultAsync(string msgId, object payload)
        {
            var msg = new object[] { 3, msgId, payload };
            string serialized = JsonSerializer.Serialize(msg);
            Console.WriteLine($"[OcppSim] [Sent RESULT] ID {msgId}: {serialized}");
            await SendMessageAsync(serialized);
        }

        private static async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
        {
            var buffer = new byte[8192];
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine("[OcppSim] Server initiated close.");
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Console.WriteLine($"[OcppSim] [Received] {message}");
                    try
                    {
                        using var doc = JsonDocument.Parse(message);
                        var root = doc.RootElement;
                        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() >= 3)
                        {
                            int msgType = root[0].GetInt32();
                            string msgId = root[1].GetString() ?? "";

                            if (msgType == 2) // CALL
                            {
                                string action = root[2].GetString() ?? "";
                                var payload = root[3];
                                await HandleServerCallAsync(msgId, action, payload);
                            }
                            else if (msgType == 3) // CALLRESULT
                            {
                                var payload = root[2];
                                if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("transactionId", out var transProp))
                                {
                                    _transactionId = transProp.GetInt32();
                                    Console.WriteLine($"[OcppSim] Transaction ID set to {_transactionId}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[OcppSim] Failed to parse message: {ex.Message}");
                    }
                }
            }
        }

        private static async Task HandleServerCallAsync(string msgId, string action, JsonElement payload)
        {
            Console.WriteLine($"[OcppSim] Processing server call '{action}'...");
            if (action == "RemoteStartTransaction")
            {
                string rfid = payload.GetProperty("idTag").GetString() ?? "Guest";
                await SendCallResultAsync(msgId, new { status = "Accepted" });
                _activeRfid = rfid;
                _chargingActive = true;

                // Send StartTransaction
                await SendCallAsync("StartTransaction", new
                {
                    connectorId = 1,
                    idTag = rfid,
                    meterStart = (int)_meterValueWh,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                });

                // Status Notification
                await SendCallAsync("StatusNotification", new
                {
                    connectorId = 1,
                    errorCode = "NoError",
                    status = "Charging"
                });
            }
            else if (action == "RemoteStopTransaction")
            {
                await SendCallResultAsync(msgId, new { status = "Accepted" });
                _chargingActive = false;

                // Send StopTransaction
                await SendCallAsync("StopTransaction", new
                {
                    transactionId = _transactionId ?? 1000,
                    meterStop = (int)_meterValueWh,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    reason = "Remote"
                });

                // Status Notification
                await SendCallAsync("StatusNotification", new
                {
                    connectorId = 1,
                    errorCode = "NoError",
                    status = "Available"
                });

                _transactionId = null;
                _activeRfid = "None";
            }
            else if (action == "SetChargingProfile")
            {
                await SendCallResultAsync(msgId, new { status = "Accepted" });
                try
                {
                    var csChargingProfiles = payload.GetProperty("csChargingProfiles");
                    var chargingSchedule = csChargingProfiles.GetProperty("chargingSchedule");
                    var period = chargingSchedule.GetProperty("chargingSchedulePeriod")[0];

                    if (period.TryGetProperty("limit", out var limProp))
                    {
                        _currentLimit = limProp.GetDouble();
                        Console.WriteLine($"[OcppSim] Max current limit adjusted to: {_currentLimit} A");
                    }

                    if (period.TryGetProperty("numberPhases", out var phasesProp))
                    {
                        _chargingPhases = phasesProp.GetInt32();
                        Console.WriteLine($"[OcppSim] Phase switching triggered. Target phases: {_chargingPhases}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[OcppSim] Failed to parse SetChargingProfile: {ex.Message}");
                }
            }
            else
            {
                await SendCallResultAsync(msgId, new { });
            }
        }

        private static async Task SimulationLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                if (_chargingActive)
                {
                    // Power = I * V * Phases. 
                    // Voltage = 230V
                    double powerW = _currentLimit * 230.0 * _chargingPhases;
                    // Wh increment for 5 seconds = Power * 5 / 3600
                    double incrementWh = (powerW * 5.0) / 3600.0;
                    _meterValueWh += incrementWh;

                    // Send MeterValues
                    await SendCallAsync("MeterValues", new
                    {
                        connectorId = 1,
                        transactionId = _transactionId ?? 0,
                        meterValue = new[]
                        {
                            new
                            {
                                timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                                sampledValue = new[]
                                {
                                    new { value = Math.Round(_meterValueWh).ToString(), measurand = "Energy.Active.Import.Register", unit = "Wh" },
                                    new { value = Math.Round(powerW).ToString(), measurand = "Power.Active.Import", unit = "W" },
                                    new { value = _currentLimit.ToString("F1"), measurand = "Current.Import", unit = "A" },
                                    new { value = "230", measurand = "Voltage", unit = "V" }
                                }
                            }
                        }
                    });
                }

                await Task.Delay(5000, ct);
            }
        }
    }
}
