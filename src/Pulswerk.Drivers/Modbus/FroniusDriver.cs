// FroniusDriver.cs – Fronius inverter driver (SunSpec Modbus TCP + Solar API HTTP Fallback)
//
//  Fronius inverters (Symo, Primo, Galvo, Eco, Gen24, etc.) support both:
//   1. SunSpec Modbus TCP (port 502) when activated in Datamanager settings.
//   2. Fronius Solar API v1 REST (HTTP port 80), which is unauthenticated and enabled
//      by default. This is the exact mechanism used by Victron CCGX / Venus OS (dbus-fronius).
//
//  When Modbus TCP is refused or inaccessible (e.g. settings password unknown),
//  this driver automatically falls back to querying the Solar API v1 endpoints on HTTP port 80.
//

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using Pulswerk.Core;

namespace Pulswerk.Drivers.Modbus
{
    using TelemetryValues = Dictionary<string, object>;

    /// <summary>
    /// Fronius driver supporting SunSpec Modbus TCP with automatic fallback
    /// to Fronius Solar API v1 HTTP REST (port 80).
    /// </summary>
    public class FroniusDriver : SunSpecDriver
    {
        public override string DriverName => "Fronius";

        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(4)
        };

        // Cache hosts that failed Modbus TCP so subsequent polls directly use HTTP Solar API
        private static readonly ConcurrentDictionary<string, bool> _preferHttp = new();

        // Short-lived cache for Solar API HTTP responses so polling multiple inverters only queries the Datamanager once per cycle
        private static readonly ConcurrentDictionary<string, (DateTime CachedAt, string Json)> _solarApiCache = new();

        public static void ResetTransportCache()
        {
            _preferHttp.Clear();
            _solarApiCache.Clear();
        }

        public override IEnumerable<string> GetTelemetryKeys() => new[]
        {
            TelemetryKeys.PowerKw,
            TelemetryKeys.EnergyExportKwh
        };

        public override IReadOnlyDictionary<string, string> GetTelemetryUnits() => new Dictionary<string, string>
        {
            [TelemetryKeys.PowerKw] = Units.Kilowatt,
            [TelemetryKeys.EnergyExportKwh] = Units.KilowattHour
        };

        public override TelemetryValues Read(ConnectionConfig conn, DeviceConfig device)
        {
            string host = conn.Address ?? "";
            int port = conn.Port ?? 502;

            // Direct HTTP requested or host already marked as preferring HTTP Solar API
            bool isHttpDirect = port == 80 || conn.Type.Equals("http", StringComparison.OrdinalIgnoreCase) || conn.Type.Equals("solar-api", StringComparison.OrdinalIgnoreCase);
            if (isHttpDirect || _preferHttp.ContainsKey(host))
            {
                try
                {
                    return ReadSolarApi(conn, device);
                }
                catch (Exception httpEx)
                {
                    Log.Warning($"[Fronius] Solar API read failed for {host}: {httpEx.Message}");
                    throw;
                }
            }

            // Otherwise attempt SunSpec Modbus TCP first
            try
            {
                return base.Read(conn, device);
            }
            catch (Exception modbusEx)
            {
                Log.Info($"[Fronius] Modbus TCP to {host}:{port} failed ({modbusEx.Message}). Falling back to Solar API v1 (HTTP port 80)...");

                try
                {
                    var result = ReadSolarApi(conn, device);
                    _preferHttp[host] = true;
                    Log.Info($"[Fronius] Successfully acquired values via Solar API v1 from {host}. Will prefer HTTP for subsequent reads.");
                    return result;
                }
                catch (Exception httpEx)
                {
                    Log.Error($"[Fronius] Solar API fallback to {host} also failed: {httpEx.Message}");
                    throw new AggregateException($"Both Modbus TCP and Solar API failed for Fronius at {host}", modbusEx, httpEx);
                }
            }
        }

        /// <summary>
        /// Queries the Fronius Solar API v1 GetPowerFlowRealtimeData endpoint.
        /// </summary>
        public virtual TelemetryValues ReadSolarApi(ConnectionConfig conn, DeviceConfig device)
        {
            int httpPort = (!conn.Port.HasValue || conn.Port.Value == 502 || conn.Port.Value <= 0) ? 80 : conn.Port.Value;
            string cacheKey = $"{conn.Address}:{httpPort}";
            int? devId = device.DeviceId != null ? (int?)device.DeviceId.Value : null;

            if (_solarApiCache.TryGetValue(cacheKey, out var entry) && (DateTime.UtcNow - entry.CachedAt).TotalSeconds < 2.5)
            {
                return ParseSolarApiPowerFlowJson(entry.Json, devId);
            }

            string url = $"http://{conn.Address}:{httpPort}/solar_api/v1/GetPowerFlowRealtimeData.fcgi";

            string json;
            using (var resp = _httpClient.GetAsync(url).GetAwaiter().GetResult())
            {
                resp.EnsureSuccessStatusCode();
                json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }

            _solarApiCache[cacheKey] = (DateTime.UtcNow, json);
            return ParseSolarApiPowerFlowJson(json, devId);
        }

        /// <summary>
        /// Parses the JSON payload from GetPowerFlowRealtimeData.fcgi.
        /// Extracts site totals and specific inverter data if deviceId is specified.
        /// </summary>
        public static TelemetryValues ParseSolarApiPowerFlowJson(string json, int? deviceId)
        {
            var result = new TelemetryValues();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("Body", out var body) ||
                !body.TryGetProperty("Data", out var data))
            {
                return result;
            }

            // 1. Inverter-specific reading if deviceId matches an inverter entry
            bool hasInverterData = false;
            string devIdStr = deviceId?.ToString() ?? "";

            if (data.TryGetProperty("Inverters", out var invertersElem) && invertersElem.ValueKind == JsonValueKind.Object)
            {
                if (!string.IsNullOrEmpty(devIdStr) && invertersElem.TryGetProperty(devIdStr, out var invElem))
                {
                    hasInverterData = true;
                    if (invElem.TryGetProperty("P", out var pElem) && pElem.ValueKind == JsonValueKind.Number)
                    {
                        double pKw = Math.Round(pElem.GetDouble() / 1000.0, 3);
                        result[TelemetryKeys.PowerKw] = pKw;
                    }

                    if (invElem.TryGetProperty("E_Total", out var etElem) && etElem.ValueKind == JsonValueKind.Number)
                    {
                        result[TelemetryKeys.EnergyExportKwh] = Math.Round(etElem.GetDouble() / 1000.0, 3);
                    }
                }
            }

            // 2. Extract Site metrics (fallback for power/energy if no specific inverter matched)
            if (data.TryGetProperty("Site", out var siteElem) && siteElem.ValueKind == JsonValueKind.Object)
            {
                if (siteElem.TryGetProperty("P_PV", out var pvElem) && pvElem.ValueKind == JsonValueKind.Number)
                {
                    double pvKw = Math.Round(pvElem.GetDouble() / 1000.0, 3);
                    if (!hasInverterData)
                    {
                        result[TelemetryKeys.PowerKw] = pvKw;
                    }
                }

                if (siteElem.TryGetProperty("E_Total", out var siteTotalElem) && siteTotalElem.ValueKind == JsonValueKind.Number)
                {
                    if (!hasInverterData)
                    {
                        result[TelemetryKeys.EnergyExportKwh] = Math.Round(siteTotalElem.GetDouble() / 1000.0, 3);
                    }
                }
            }

            return result;
        }
    }
}
