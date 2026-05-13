// Program.cs – Modbus TCP simulator (Janitza + Glück)
//
// Janitza (slaves 1–4) register map (float32 big-endian = 2 × uint16):
//   19026  power_w     → connector divides by 1000 → power_kw
//   19062  import_wh   → connector divides by 1000 → import_kwh
//   19076  export_wh   → always 0 (no export on test meter)
//
// Glück (slave 5) register map:
//   Input  1901       uint16         utility_limit_pct
//   Input  1902–1903  uint32 swapped feedback_limit_pct
//   Input  1904–1905  int32  swapped generation_power (W)
//
// Behaviour: Janitza power oscillates on a 5-min sine wave (2–8 kW);
//            Glück generation oscillates on a 3-min sine (10–50 kW).

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NModbus;

const ushort REG_POWER_W   = 19026;
const ushort REG_IMPORT_WH = 19062;
const ushort REG_EXPORT_WH = 19076;

const double POWER_MIN_W    = 2_000;
const double POWER_MAX_W    = 8_000;
const double POWER_PERIOD_S = 300;          // 5-minute sine cycle

// Random starting energy so the meter doesn't always read 0
var rng       = new Random();
double importWh = rng.NextDouble() * 499_000_000 + 1_000_000;  // 1–500 MWh in Wh

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// ── Build slave ──────────────────────────────────────────────────────────────
var factory  = new ModbusFactory();
var listener = new TcpListener(IPAddress.Any, 502);
listener.Start();

// Slave 1 – Main Meter Building A  (2–8 kW sine)
var network = factory.CreateSlaveNetwork(listener);
var slave1 = factory.CreateSlave(1);
network.AddSlave(slave1);

// Slave 2 – Sub Meter Floor 2      (0.5–3 kW, offset phase)
var slave2 = factory.CreateSlave(2);
network.AddSlave(slave2);

// Slave 3 – Sub Meter Floor 3      (1–5 kW, inverted phase)
var slave3 = factory.CreateSlave(3);
network.AddSlave(slave3);

// Slave 4 – HVAC Meter             (4–12 kW, fast 2-min cycle)
var slave4 = factory.CreateSlave(4);
network.AddSlave(slave4);

// Slave 5 – Glück Controller        (10–50 kW PV generation, 3-min sine)
var slave5 = factory.CreateSlave(5);
network.AddSlave(slave5);
// Initialize power limit to 100% — only changes when the connector writes
slave5.DataStore.HoldingRegisters.WritePoints(401, new ushort[] { 100 });

var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
Console.WriteLine($"=== Modbus TCP Simulator v{version?.Major}.{version?.Minor}.{version?.Build} (Janitza + Glück) ===");
Console.WriteLine($"Listening on 0.0.0.0:502  slaves=1,2,3,4,5");
Console.WriteLine($"Slave1 import = {importWh / 1000:F1} kWh");
Console.WriteLine();

// ── Background value updater ─────────────────────────────────────────────────
var t0     = DateTime.UtcNow;
var prevAt = DateTime.UtcNow;
double importWh2 = rng.NextDouble() * 200_000_000 + 500_000;
double importWh3 = rng.NextDouble() * 150_000_000 + 300_000;
double importWh4 = rng.NextDouble() * 300_000_000 + 800_000;

var updater = Task.Run(async () =>
{
    while (!cts.Token.IsCancellationRequested)
    {
        var now = DateTime.UtcNow;
        double dt      = (now - prevAt).TotalSeconds;
        double elapsed = (now - t0).TotalSeconds;
        prevAt = now;

        // Slave 1: 2–8 kW, 5-min sine
        double phase1  = elapsed % POWER_PERIOD_S / POWER_PERIOD_S;
        double powerW1 = POWER_MIN_W + (POWER_MAX_W - POWER_MIN_W)
                         * (0.5 + 0.5 * Math.Sin(2 * Math.PI * phase1));
        importWh  += powerW1 * dt / 3600.0;
        WriteFloat32(slave1, REG_POWER_W,   (float)powerW1);
        WriteFloat32(slave1, REG_IMPORT_WH, (float)importWh);
        WriteFloat32(slave1, REG_EXPORT_WH, 0f);

        // Slave 2: 0.5–3 kW, offset by 90°
        double phase2  = (elapsed % POWER_PERIOD_S / POWER_PERIOD_S) + 0.25;
        double powerW2 = 500 + 2500 * (0.5 + 0.5 * Math.Sin(2 * Math.PI * phase2));
        importWh2 += powerW2 * dt / 3600.0;
        WriteFloat32(slave2, REG_POWER_W,   (float)powerW2);
        WriteFloat32(slave2, REG_IMPORT_WH, (float)importWh2);
        WriteFloat32(slave2, REG_EXPORT_WH, 0f);

        // Slave 3: 1–5 kW, inverted phase
        double phase3  = (elapsed % POWER_PERIOD_S / POWER_PERIOD_S) + 0.5;
        double powerW3 = 1000 + 4000 * (0.5 + 0.5 * Math.Sin(2 * Math.PI * phase3));
        importWh3 += powerW3 * dt / 3600.0;
        WriteFloat32(slave3, REG_POWER_W,   (float)powerW3);
        WriteFloat32(slave3, REG_IMPORT_WH, (float)importWh3);
        WriteFloat32(slave3, REG_EXPORT_WH, 0f);

        // Slave 4: 4–12 kW, fast 2-min cycle
        double phase4  = elapsed % 120.0 / 120.0;
        double powerW4 = 4000 + 8000 * (0.5 + 0.5 * Math.Sin(2 * Math.PI * phase4));
        importWh4 += powerW4 * dt / 3600.0;
        WriteFloat32(slave4, REG_POWER_W,   (float)powerW4);
        WriteFloat32(slave4, REG_IMPORT_WH, (float)importWh4);
        WriteFloat32(slave4, REG_EXPORT_WH, (float)(powerW4 > 9000 ? (powerW4 - 9000) : 0));  // exports when peak

        // Slave 5: Glück – 10–50 kW PV generation, 3-min sine
        double phase5 = elapsed % 180.0 / 180.0;
        int genPowerW = (int)(10_000 + 40_000 * (0.5 + 0.5 * Math.Sin(2 * Math.PI * phase5)));
        ushort utilLimit = 100;  // utility always allows 100%
        // feedback mirrors the holding register limit (written by connector)
        var fbRegs = slave5.DataStore.HoldingRegisters.ReadPoints(401, 1);
        uint feedbackLimit = fbRegs[0];
        WriteInputUInt16(slave5, 1901, utilLimit);
        WriteInputUInt32Swapped(slave5, 1902, feedbackLimit);
        WriteInputInt32Swapped(slave5, 1904, genPowerW);

        Console.WriteLine($"[{now:HH:mm:ss}] " +
            $"S1={powerW1/1000:F2}kW  S2={powerW2/1000:F2}kW  " +
            $"S3={powerW3/1000:F2}kW  S4={powerW4/1000:F2}kW  " +
            $"G={genPowerW/1000:F1}kW lim={feedbackLimit}%");

        await Task.Delay(5_000, cts.Token).ConfigureAwait(false);
    }
}, cts.Token);

// ── Run server ───────────────────────────────────────────────────────────────
var serverTask = network.ListenAsync(cts.Token);
await Task.WhenAny(serverTask, updater);

Console.WriteLine("Shutting down.");
listener.Stop();

// ── Helpers ──────────────────────────────────────────────────────────────────

/// <summary>Encodes a float32 as two big-endian uint16 holding registers
/// (mirrors ModbusHelper.WriteFloat32 in the connector).</summary>
static void WriteFloat32(IModbusSlave slave, ushort address, float value)
{
    var b = BitConverter.GetBytes(value);
    if (BitConverter.IsLittleEndian) Array.Reverse(b);
    ushort hi = (ushort)(b[0] << 8 | b[1]);
    ushort lo = (ushort)(b[2] << 8 | b[3]);
    slave.DataStore.HoldingRegisters.WritePoints(address,     new ushort[] { hi });
    slave.DataStore.HoldingRegisters.WritePoints((ushort)(address + 1), new ushort[] { lo });
}

/// <summary>Write a uint16 to an input register.</summary>
static void WriteInputUInt16(IModbusSlave slave, ushort address, ushort value)
{
    slave.DataStore.InputRegisters.WritePoints(address, new ushort[] { value });
}

/// <summary>Write a uint32 as two input registers (word-swapped = lo,hi).</summary>
static void WriteInputUInt32Swapped(IModbusSlave slave, ushort address, uint value)
{
    ushort hi = (ushort)(value >> 16);
    ushort lo = (ushort)(value & 0xFFFF);
    // Swapped: low word first, high word second
    slave.DataStore.InputRegisters.WritePoints(address, new ushort[] { lo, hi });
}

/// <summary>Write an int32 as two input registers (word-swapped = lo,hi).</summary>
static void WriteInputInt32Swapped(IModbusSlave slave, ushort address, int value)
{
    WriteInputUInt32Swapped(slave, address, (uint)value);
}
