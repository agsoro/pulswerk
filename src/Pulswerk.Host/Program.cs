// Program.cs – entry point
//
//  Standalone connector: reads BACnet/Modbus devices, stores telemetry
//  in InfluxDB, manages alarms in SQLite, serves a dashboard via Kestrel.

using System.Threading.Tasks;

namespace Pulswerk.Host
{
    class Program
    {
        static Task<int> Main() => ConnectorHost.RunAsync();
    }
}
