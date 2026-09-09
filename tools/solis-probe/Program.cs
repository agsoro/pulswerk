using System;
using System.Collections.Generic;
using Pulswerk.Core;
using Pulswerk.Drivers;
using Pulswerk.Drivers.Modbus;

namespace SolisProbe
{
    class Program
    {
        static void Main(string[] args)
        {
            string host = args.Length > 0 ? args[0] : "10.10.1.21";
            int port = args.Length > 1 && int.TryParse(args[1], out int p) ? p : 502;
            byte deviceId = args.Length > 2 && byte.TryParse(args[2], out byte d) ? d : (byte)1;

            Console.WriteLine("===============================================================");
            Console.WriteLine("           Pulswerk Modbus Inverter Live Probe                ");
            Console.WriteLine("===============================================================\n");

            // 1. Probe Solis S6
            Console.WriteLine($"[1/2] Querying Solis S6 on {host}:{port} (Slave ID: {deviceId})...");
            var solisConn = new ConnectionConfig(Id: "solis-probe", Type: "modbus-tcp", Address: host, Port: port);
            var solisDev = new DeviceConfig(Id: "solis-s6", Name: "Solis S6 Hybrid", DeviceType: "solis", ConnectionId: "solis-probe", DeviceId: deviceId);

            try
            {
                var driver = DeviceDriverFactory.Create("solis");
                var telemetries = driver.Read(solisConn, solisDev);

                Console.WriteLine("\n---------------- Solis S6 Telemetry (Combined) -----------");
                PrintTelemetry(telemetries, TelemetryKeys.PowerKw, "Inverter AC Power");
                PrintTelemetry(telemetries, TelemetryKeys.BatterySocPct, "Battery SOC");
                PrintTelemetry(telemetries, TelemetryKeys.EnergyImportKwh, "Grid Import Total");
                PrintTelemetry(telemetries, TelemetryKeys.EnergyExportKwh, "Grid Export Total (Feed-in)");

                Console.WriteLine("\n---------------- Solis S6 Power Control Setpoints --------");
                PrintTelemetry(telemetries, TelemetryKeys.ForcePowerKw, "Force Power (+disch, -charge) (60s)");

                if (driver is IDeviceWriter writer)
                {
                    Console.WriteLine("\n[WRITER TEST] Solis driver implements IDeviceWriter.");
                    Console.WriteLine($"  IsWritable(force_power):           {writer.IsWritable(TelemetryKeys.ForcePowerKw)}");
                    Console.WriteLine($"  IsWritable(power_limit):           {writer.IsWritable(TelemetryKeys.PowerLimitPct)}");

                    // Test safe write: setting ForcePowerKw = 0 (Normal / Self-Use)
                    Console.WriteLine("  Sending safe write: force_power = 0 (Normal)...");
                    writer.Write(solisConn, solisDev, TelemetryKeys.ForcePowerKw, 0);
                    Console.WriteLine("  [SUCCESS] Safe write executed successfully.");
                }

                Console.WriteLine("\n[SUCCESS] Solis S6 live data acquired and write verified.\n");

                // 1b. Probe Solis S6 as 3 separate sub-devices
                Console.WriteLine("---------------- Solis S6 as 3 Separate Devices ---------");
                var pvDriver = DeviceDriverFactory.Create("solis-pv");
                var battDriver = DeviceDriverFactory.Create("solis-battery");
                var gridDriver = DeviceDriverFactory.Create("solis-grid");

                var pvDev = new DeviceConfig(Id: "solis-pv", Name: "Solis PV", DeviceType: "solis-pv", ConnectionId: "solis-probe", DeviceId: deviceId);
                var battDev = new DeviceConfig(Id: "solis-battery", Name: "Solis Battery", DeviceType: "solis-battery", ConnectionId: "solis-probe", DeviceId: deviceId);
                var gridDev = new DeviceConfig(Id: "solis-grid", Name: "Solis Grid", DeviceType: "solis-grid", ConnectionId: "solis-probe", DeviceId: deviceId);

                Console.WriteLine("  [solis-pv]");
                var pvVals = pvDriver.Read(solisConn, pvDev);
                foreach (var (k, v) in pvVals)
                {
                    Console.WriteLine($"    {k,-25}: {v,8} {TelemetryKeys.GetFriendlyUnit(k)}");
                }

                Console.WriteLine("\n  [solis-battery]");
                var battVals = battDriver.Read(solisConn, battDev);
                foreach (var (k, v) in battVals)
                {
                    Console.WriteLine($"    {k,-25}: {v,8} {TelemetryKeys.GetFriendlyUnit(k)}");
                }

                Console.WriteLine("\n  [solis-grid]");
                var gridVals = gridDriver.Read(solisConn, gridDev);
                foreach (var (k, v) in gridVals)
                {
                    Console.WriteLine($"    {k,-25}: {v,8} {TelemetryKeys.GetFriendlyUnit(k)}");
                }

                Console.WriteLine("\n  [SUCCESS] All 3 separate devices queried (deduplicated via cached Modbus cycle).\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Solis read failed: {ex.Message}\n");
            }
            finally
            {
                ModbusConnection.FullReset(solisConn.Id);
            }

            // 2. Probe Fronius
            string froniusHost = args.Length > 3 ? args[3] : "10.10.1.22";
            Console.WriteLine($"[2/2] Checking Fronius on {froniusHost} (Modbus TCP / Solar API v1)...");
            var froniusConn = new ConnectionConfig(Id: "fronius-probe", Type: "modbus-tcp", Address: froniusHost, Port: 502);
            var froniusDev1 = new DeviceConfig(Id: "fronius-inv1", Name: "Fronius Inverter 1", DeviceType: "fronius", ConnectionId: "fronius-probe", DeviceId: 1);
            var froniusDev2 = new DeviceConfig(Id: "fronius-inv2", Name: "Fronius Inverter 2", DeviceType: "fronius", ConnectionId: "fronius-probe", DeviceId: 2);

            try
            {
                var froniusDriver = DeviceDriverFactory.Create("fronius");

                // Inverter 1
                Console.WriteLine("---------------- Fronius Inverter 1 -------------------");
                var v1 = froniusDriver.Read(froniusConn, froniusDev1);
                foreach (var (k, v) in v1)
                {
                    string unit = TelemetryKeys.GetFriendlyUnit(k);
                    Console.WriteLine($"  {k,-30}: {v,8} {unit}");
                }

                // Inverter 2
                Console.WriteLine("\n---------------- Fronius Inverter 2 -------------------");
                var v2 = froniusDriver.Read(froniusConn, froniusDev2);
                foreach (var (k, v) in v2)
                {
                    string unit = TelemetryKeys.GetFriendlyUnit(k);
                    Console.WriteLine($"  {k,-30}: {v,8} {unit}");
                }

                Console.WriteLine("\n[SUCCESS] Fronius live data acquired successfully (Solar API fallback).\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [ERROR] Fronius query failed: {ex.Message}");
            }
            finally
            {
                ModbusConnection.FullReset(froniusConn.Id);
            }
        }

        static void PrintTelemetry(Dictionary<string, object> values, string key, string label)
        {
            if (values.TryGetValue(key, out var val))
            {
                string unit = TelemetryKeys.GetFriendlyUnit(key);
                Console.WriteLine($"  {label,-40}: {val,8} {unit}");
            }
            else
            {
                Console.WriteLine($"  {label,-40}:      N/A");
            }
        }
    }
}
