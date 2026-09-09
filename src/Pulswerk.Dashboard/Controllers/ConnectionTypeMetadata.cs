// ConnectionTypeMetadata.cs – describes every supported connection type and the
// fields the configuration UI should render for each one.
//
//  The UI consumes this list from GET /plswk/api/connection-types and renders
//  only the fields that belong to the selected protocol. This keeps the editor
//  in sync with the backend without hard-coding field lists in TypeScript.

using System.Collections.Generic;

namespace Pulswerk.Dashboard.Controllers
{
    /// <summary>
    /// Describes a single editable field on a connection.
    /// </summary>
    public record ConnectionField(
        string Key,            // JSON property name on ConnectionConfig
        string Label,          // human-readable label
        string Type,           // "text" | "number" | "password" | "select" | "checkbox"
        bool Required,
        string? Placeholder = null,
        string? Help = null,
        string? Default = null,
        List<string>? Options = null);

    /// <summary>
    /// Describes a supported connection type.
    /// </summary>
    public record ConnectionTypeMeta(
        string Type,           // e.g. "modbus-tcp"
        string Label,          // e.g. "Modbus TCP"
        string Icon,           // FontAwesome icon class
        int DefaultPort,       // suggested port when adding a new connection
        List<ConnectionField> Fields);

    /// <summary>
    /// Static catalogue of supported connection types and their fields.
    /// </summary>
    public static class ConnectionTypeMetadata
    {
        public static readonly IReadOnlyList<ConnectionTypeMeta> All = new[]
        {
            new ConnectionTypeMeta(
                Type: "modbus-tcp",
                Label: "Modbus TCP",
                Icon: "fa-bolt",
                DefaultPort: 502,
                Fields: new()
                {
                    new("id", "Connection ID", "text", true, "e.g. modbus-gateway-1"),
                    new("name", "Display Name", "text", false, "Optional friendly name"),
                    new("address", "Gateway IP / Host", "text", true, "192.168.1.50"),
                    new("port", "Port", "number", true, Default: "502"),
                }
            ),
            new ConnectionTypeMeta(
                Type: "bacnet-ip",
                Label: "BACnet/IP",
                Icon: "fa-network-wired",
                DefaultPort: 47808,
                Fields: new()
                {
                    new("id", "Connection ID", "text", true, "e.g. bacnet-net-1"),
                    new("name", "Display Name", "text", false, "Optional friendly name"),
                    new("localAddress", "Local Bind Address", "text", true, "0.0.0.0",
                        "Local NIC to bind the BACnet UDP socket to. Use 0.0.0.0 for any."),
                    new("localPort", "Local Bind Port", "number", true, Default: "47808",
                        Help: "BACnet uses UDP. The local port must match the network's BACnet port (47808 by default)."),
                    new("localDeviceId", "Local Device ID", "number", false, Default: "1234",
                        Help: "BACnet device instance advertised by this connector on the bus."),
                }
            ),
            new ConnectionTypeMeta(
                Type: "knx-ip",
                Label: "KNXnet/IP",
                Icon: "fa-house-signal",
                DefaultPort: 3671,
                Fields: new()
                {
                    new("id", "Connection ID", "text", true, "e.g. knx-gateway-1"),
                    new("name", "Display Name", "text", false, "Optional friendly name"),
                    new("address", "Gateway IP / Host", "text", true, "192.168.1.50"),
                    new("port", "Port", "number", false, Default: "3671"),
                    new("knxSecureEnabled", "Enable KNX IP Secure", "checkbox", false,
                        Help: "Enable when the gateway requires KNX IP Secure (tunnelling)."),
                    new("knxCommissioningPassword", "Commissioning Password (FDSK)", "password", false,
                        Placeholder: "ABCDE-ABCDE-ABCDE-ABCDE",
                        Help: "Printed on the device label. Enter as a single line keeping the hyphens between groups."),
                    new("knxMaxConcurrentConnects", "Max Concurrent Connects", "number", false, Default: "2",
                        Help: "Process-wide cap on simultaneous tunnel handshakes. Lower this on small gateways."),
                    new("knxStaleSeconds", "Stale Value Seconds", "number", false, Default: "180",
                        Help: "Cached group values older than this are not emitted as telemetry. 0 disables."),
                }
            ),
            new ConnectionTypeMeta(
                Type: "ocpp-ws",
                Label: "OCPP Central System",
                Icon: "fa-charging-station",
                DefaultPort: 9000,
                Fields: new()
                {
                    new("id", "Connection ID", "text", true, "e.g. ocpp-cs-1"),
                    new("name", "Display Name", "text", false, "Optional friendly name"),
                    new("localAddress", "Listen Address", "text", true, "0.0.0.0",
                        "Local NIC the WebSocket server binds to."),
                    new("localPort", "Listen Port", "number", true, Default: "9000",
                        Help: "TCP port the OCPP central system listens on for charge point connections."),
                }
            ),
            new ConnectionTypeMeta(
                Type: "smgw-http",
                Label: "Smart Meter Gateway (HTTP/S)",
                Icon: "fa-tachometer-alt",
                DefaultPort: 443,
                Fields: new()
                {
                    new("id", "Connection ID", "text", true, "e.g. smgw"),
                    new("name", "Display Name", "text", false, "Smart Meter Gateway"),
                    new("address", "Gateway IP / Host", "text", true, "192.168.1.100"),
                    new("port", "Port", "number", false, Default: "443"),
                    new("username", "Username", "text", true, "e.g. 123456789012_ECPR0001000000"),
                    new("password", "Password", "password", true),
                    new("ignoreSslErrors", "Ignore SSL Certificate Errors", "checkbox", false, Default: "true",
                        Help: "Allow self-signed / private CA certificates on the SMGW local HAN interface."),
                }
            ),
        };
    }

    // ── Device type metadata ────────────────────────────────────────────────

    /// <summary>
    /// Describes a single editable field on a device.
    /// </summary>
    public record DeviceField(
        string Key,            // JSON property name on DeviceConfig
        string Label,
        string Type,           // "text" | "number" | "password" | "select" | "checkbox"
        bool Required,
        string? Placeholder = null,
        string? Help = null,
        string? Default = null,
        List<string>? Options = null);

    /// <summary>
    /// Describes a supported device type, including which connection types it
    /// can run on and which fields the UI should render.
    /// </summary>
    public record DeviceTypeMeta(
        string Type,           // e.g. "janitza"
        string Label,          // e.g. "Janitza"
        string Icon,
        List<string> CompatibleConnections, // connection types this device can use
        List<DeviceField> Fields);

    /// <summary>
    /// Static catalogue of supported device types and their fields.
    /// </summary>
    public static class DeviceTypeMetadata
    {
        public static readonly IReadOnlyList<DeviceTypeMeta> All = new[]
        {
            new DeviceTypeMeta(
                Type: "virtual",
                Label: "Virtual / Analytics",
                Icon: "fa-calculator",
                CompatibleConnections: new(),
                Fields: new()
                {
                    new("id", "Device ID", "text", true, "e.g. virtual-1"),
                    new("name", "Display Name", "text", true),
                    new("path", "Path", "text", false, "Building/Floor"),
                }
            ),
            new DeviceTypeMeta(
                Type: "janitza",
                Label: "Janitza (Modbus)",
                Icon: "fa-bolt",
                CompatibleConnections: new() { "modbus-tcp" },
                Fields: new()
                {
                    new("id", "Device ID", "text", true, "e.g. janitza-1"),
                    new("name", "Display Name", "text", true),
                    new("connectionId", "Connection", "select", true),
                    new("deviceId", "Modbus Slave ID", "number", true, "1"),
                    new("address", "Address Override", "text", false,
                        Help: "Optional. Overrides the connection's gateway address for this device."),
                    new("path", "Path", "text", false, "Building/Floor"),
                    new("assetType", "Asset Type", "text", false,
                        Help: "Enables hierarchy provisioning. e.g. 'Meter', 'HVAC Node'."),
                    new("pollIntervalSeconds", "Poll Interval (s)", "number", false),
                }
            ),
            new DeviceTypeMeta(
                Type: "glueck",
                Label: "Glück (Modbus)",
                Icon: "fa-bolt",
                CompatibleConnections: new() { "modbus-tcp" },
                Fields: new()
                {
                    new("id", "Device ID", "text", true),
                    new("name", "Display Name", "text", true),
                    new("connectionId", "Connection", "select", true),
                    new("deviceId", "Modbus Slave ID", "number", true, "1"),
                    new("path", "Path", "text", false, "Building/Floor"),
                    new("assetType", "Asset Type", "text", false),
                    new("pollIntervalSeconds", "Poll Interval (s)", "number", false),
                }
            ),
            new DeviceTypeMeta(
                Type: "abb",
                Label: "ABB (Modbus)",
                Icon: "fa-bolt",
                CompatibleConnections: new() { "modbus-tcp" },
                Fields: new()
                {
                    new("id", "Device ID", "text", true),
                    new("name", "Display Name", "text", true),
                    new("connectionId", "Connection", "select", true),
                    new("deviceId", "Modbus Slave ID", "number", true, "1"),
                    new("path", "Path", "text", false, "Building/Floor"),
                    new("assetType", "Asset Type", "text", false),
                    new("pollIntervalSeconds", "Poll Interval (s)", "number", false),
                }
            ),
            new DeviceTypeMeta(
                Type: "sunspec",
                Label: "SunSpec / Solar (Modbus)",
                Icon: "fa-solar-panel",
                CompatibleConnections: new() { "modbus-tcp" },
                Fields: new()
                {
                    new("id", "Device ID", "text", true),
                    new("name", "Display Name", "text", true),
                    new("connectionId", "Connection", "select", true),
                    new("deviceId", "Modbus Slave ID", "number", true, "1"),
                    new("path", "Path", "text", false, "Building/Floor"),
                    new("assetType", "Asset Type", "text", false),
                    new("pollIntervalSeconds", "Poll Interval (s)", "number", false),
                }
            ),
            new DeviceTypeMeta(
                Type: "sdm630",
                Label: "SDM630 Meter (Modbus)",
                Icon: "fa-gauge-high",
                CompatibleConnections: new() { "modbus-tcp" },
                Fields: new()
                {
                    new("id", "Device ID", "text", true),
                    new("name", "Display Name", "text", true),
                    new("connectionId", "Connection", "select", true),
                    new("deviceId", "Modbus Slave ID", "number", true, "1"),
                    new("path", "Path", "text", false, "Building/Floor"),
                    new("assetType", "Asset Type", "text", false),
                    new("pollIntervalSeconds", "Poll Interval (s)", "number", false),
                }
            ),
            new DeviceTypeMeta(
                Type: "smadm",
                Label: "SMA Data Manager (Modbus)",
                Icon: "fa-server",
                CompatibleConnections: new() { "modbus-tcp" },
                Fields: new()
                {
                    new("id", "Device ID", "text", true),
                    new("name", "Display Name", "text", true),
                    new("connectionId", "Connection", "select", true),
                    new("deviceId", "Modbus Slave ID", "number", true, "1"),
                    new("path", "Path", "text", false, "Building/Floor"),
                    new("assetType", "Asset Type", "text", false),
                    new("pollIntervalSeconds", "Poll Interval (s)", "number", false),
                }
            ),
            new DeviceTypeMeta(
                Type: "bacnet",
                Label: "BACnet (Generic)",
                Icon: "fa-network-wired",
                CompatibleConnections: new() { "bacnet-ip" },
                Fields: new()
                {
                    new("id", "Device ID", "text", true),
                    new("name", "Display Name", "text", true),
                    new("connectionId", "Connection", "select", true),
                    new("deviceId", "BACnet Device Instance", "number", true, "100"),
                    new("address", "Device IP (optional)", "text", false,
                        Help: "Optional unicast target. When omitted the device is found via Who-Is."),
                    new("path", "Path", "text", false, "Building/Floor"),
                    new("assetType", "Asset Type", "text", false,
                        Help: "Enables hierarchy provisioning. e.g. 'HVAC Node', 'BACnet Node'."),
                    new("pollIntervalSeconds", "Poll Interval (s)", "number", false, Default: "3600"),
                    new("writeback", "Enable Write-back", "checkbox", false),
                }
            ),
            new DeviceTypeMeta(
                Type: "deziko",
                Label: "Deziko (BACnet)",
                Icon: "fa-network-wired",
                CompatibleConnections: new() { "bacnet-ip" },
                Fields: new()
                {
                    new("id", "Device ID", "text", true),
                    new("name", "Display Name", "text", true),
                    new("connectionId", "Connection", "select", true),
                    new("deviceId", "BACnet Device Instance", "number", true, "100"),
                    new("address", "Device IP (optional)", "text", false),
                    new("path", "Path", "text", false, "Building/Floor"),
                    new("assetType", "Asset Type", "text", false,
                        Help: "Enables hierarchy provisioning and Structured View extraction."),
                    new("pollIntervalSeconds", "Poll Interval (s)", "number", false, Default: "3600"),
                    new("writeback", "Enable Write-back", "checkbox", false),
                }
            ),
            new DeviceTypeMeta(
                Type: "knx",
                Label: "KNX Device",
                Icon: "fa-house-signal",
                CompatibleConnections: new() { "knx-ip" },
                Fields: new()
                {
                    new("id", "Device ID", "text", true),
                    new("name", "Display Name", "text", true),
                    new("connectionId", "Connection", "select", true),
                    new("knxGroupAddressXml", "ETS Group Address XML", "text", false,
                        Help: "Optional path to an ETS XML export file for metadata."),
                    new("path", "Path", "text", false, "Building/Floor"),
                }
            ),
            new DeviceTypeMeta(
                Type: "ocpp",
                Label: "OCPP Wallbox",
                Icon: "fa-charging-station",
                CompatibleConnections: new() { "ocpp-ws" },
                Fields: new()
                {
                    new("id", "Charge Point ID", "text", true, "e.g. CP-001"),
                    new("name", "Display Name", "text", true),
                    new("connectionId", "Connection", "select", true),
                    new("path", "Path", "text", false, "Building/Floor"),
                }
            ),
            new DeviceTypeMeta(
                Type: "ocpp-master",
                Label: "OCPP Master",
                Icon: "fa-charging-station",
                CompatibleConnections: new() { "ocpp-ws" },
                Fields: new()
                {
                    new("id", "Device ID", "text", true, "e.g. ocpp-master"),
                    new("name", "Display Name", "text", true, "Master"),
                    new("connectionId", "Connection", "select", true),
                    new("path", "Path", "text", false, "Wallbox"),
                }
            ),
            new DeviceTypeMeta(
                Type: "smgw",
                Label: "Smart Meter Gateway",
                Icon: "fa-tachometer-alt",
                CompatibleConnections: new() { "smgw-http" },
                Fields: new()
                {
                    new("id", "Device ID", "text", true, "e.g. efr-grid-meter"),
                    new("name", "Display Name", "text", true, "EFR Abrechnungszähler (TAF-7)"),
                    new("connectionId", "Connection", "select", true),
                    new("meterId", "Meter ID", "text", false, "e.g. 1 EFR 24 75081296",
                        Help: "Optional meter number to match against if the gateway connects multiple meters."),
                    new("pollIntervalSeconds", "Poll Interval (s)", "number", false, Default: "120",
                        Help: "Recommended: 120s to conserve gateway resources."),
                    new("path", "Path", "text", false, "Abrechnung/Einspeisung"),
                }
            ),
        };
    }
}
