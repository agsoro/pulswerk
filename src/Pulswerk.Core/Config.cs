// Config.cs – JSON model, mirrors pulswerk.json
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Pulswerk.Core
{
    public record AppConfig(
        [property: JsonPropertyName("influxdb")] InfluxConfig? InfluxDb,
        [property: JsonPropertyName("database")] DatabaseConfig? Database,
        [property: JsonPropertyName("polling")] PollingConfig? Polling,
        [property: JsonPropertyName("connections")] List<ConnectionConfig> Connections,
        [property: JsonPropertyName("devices")] List<DeviceConfig> Devices,
        [property: JsonPropertyName("monitoring")] MonitoringConfig? Monitoring
    );

    // ── InfluxDB ───────────────────────────────────────────────────────────────

    public record InfluxConfig(
        [property: JsonPropertyName("url")] string Url = "http://localhost:8086",
        [property: JsonPropertyName("token")] string Token = "connector-token",
        [property: JsonPropertyName("org")] string Org = "pulswerk",
        [property: JsonPropertyName("bucket")] string Bucket = "telemetry"
    );

    // ── Database / retention ──────────────────────────────────────────────────

    public record DatabaseConfig(
        /// <summary>Days of full-resolution data to keep in InfluxDB (default 730 = 2 years).</summary>
        [property: JsonPropertyName("retentionDays")] int RetentionDays = 730,
        /// <summary>After this many days, data is downsampled to 15-min intervals (default 700).</summary>
        [property: JsonPropertyName("compactionAfterDays")] int CompactionAfterDays = 700,
        /// <summary>Days to keep cleared alarms in SQLite before purging (default 30).</summary>
        [property: JsonPropertyName("alarmRetentionDays")] int AlarmRetentionDays = 30
    );

    public record PollingConfig(
        [property: JsonPropertyName("intervalSeconds")] int IntervalSeconds
    );

    // ── Connections ────────────────────────────────────────────────────────────

    public record ConnectionConfig(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("type")] string Type,   // "modbus-tcp" | "bacnet-ip"

        /// <summary>Remote address (for Modbus Gateway). For BACnet, prefer address on device level.</summary>
        [property: JsonPropertyName("address")] string? Address = null,
        
        /// <summary>Remote port (for Modbus Gateway).</summary>
        [property: JsonPropertyName("port")] int? Port = null,
        
        /// <summary>Local bind address (for BACnet).</summary>
        [property: JsonPropertyName("localAddress")] string? LocalAddress = null,

        /// <summary>Local bind port (for BACnet).</summary>
        [property: JsonPropertyName("localPort")] int? LocalPort = null,

        /// <summary>Optional local BACnet Device ID for this connection (default 1234).</summary>
        [property: JsonPropertyName("localDeviceId")] uint? LocalDeviceId = null,

        /// <summary>Human-readable display name for this connection in the dashboard.</summary>
        [property: JsonPropertyName("name")] string? Name = null
    )
    {
        /// <summary>Effective device name — explicit or derived from Id.</summary>
        public string EffectiveName => Name
            ?? System.Globalization.CultureInfo.InvariantCulture.TextInfo
                     .ToTitleCase(Id.Replace('-', ' ').Replace('_', ' '));
    }

    // ── Devices ───────────────────────────────────────────────────────────────
    //
    //  deviceType:
    //    "janitza"  → JanitzaReader  (needs slaveId)
    //    "glueck"   → GlueckReader   (needs slaveId)
    //    "bacnet"   → BacnetReader   (needs bacnetDeviceId; all bacnet options are at device level)
    //    "deziko"   → BacnetReader   (Deziko BACnet; hierarchy on by default)

    public record DeviceConfig(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("deviceType")] string DeviceType,
        [property: JsonPropertyName("connectionId")] string ConnectionId,
        
        /// <summary>Target address. For Modbus, this is the gateway; for BACnet, this is the device IP (optional fallback for Who-Is).</summary>
        [property: JsonPropertyName("address")] string? Address = null,

        /// <summary>Device identifier (Slave ID for Modbus, Device Instance for BACnet).</summary>
        [property: JsonPropertyName("deviceId")] uint? DeviceId = null,

        // Modbus manual hierarchy path
        [property: JsonPropertyName("path")] List<string>? Path = null,

        // BACnet / general config (all optional; code defaults apply when omitted)
        [property: JsonPropertyName("whoIsTimeoutMs")] int WhoIsTimeoutMs = 2000,
        [property: JsonPropertyName("filter")] BacnetFilterConfig? Filter = null,
        [property: JsonPropertyName("properties")] BacnetPropsConfig? Properties = null,
        [property: JsonPropertyName("discovery")] BacnetDiscoveryConfig? Discovery = null,
        [property: JsonPropertyName("cov")] BacnetCovConfig? Cov = null,

        /// <summary>
        /// Asset type label used for hierarchy provisioning.
        /// Setting this enables hierarchy for the device (no separate "enabled" flag needed).
        /// For Deziko devices this also triggers Structured View tree extraction.
        /// Example: "HVAC Node", "BACnet Node", "Meter".
        /// </summary>
        [property: JsonPropertyName("assetType")] string? AssetType = null,

        // Write-back (BACnet only – direct via dashboard or API)
        [property: JsonPropertyName("writeback")] bool Writeback = true,

        /// <summary>
        /// Per-device poll interval in seconds. Overrides the global polling.intervalSeconds.
        /// Controls how often non-COV / fallback-polled values are read from the device.
        /// Defaults: 3600s (1h) for BACnet to avoid overloading controllers, global interval for others.
        /// </summary>
        [property: JsonPropertyName("pollIntervalSeconds")] int? PollIntervalSeconds = null
    )
    {
        public string AccessToken =>
            "device_" + Id.ToLowerInvariant()
                          .Replace(' ', '_')
                          .Replace('-', '_')
                          .Replace('/', '_')
                          .Replace('\\', '_');

        /// <summary>True when hierarchy provisioning is active (AssetType set).</summary>
        public bool HierarchyEnabled => AssetType != null;
    }

    // ── BACnet COV subscription config ────────────────────────────────────────

    /// <summary>
    /// Configures BACnet Change-of-Value subscriptions for a device.
    /// When <see cref="Enabled"/> is true the connector maintains a long-lived
    /// BACnet client and receives push notifications instead of polling telemetry.
    /// </summary>
    public record BacnetCovConfig(
        /// <summary>Activate COV for this device. False = unchanged polling behaviour.</summary>
        [property: JsonPropertyName("enabled")] bool Enabled = true,

        /// <summary>Subscription lifetime sent to the device in seconds (default 300).</summary>
        [property: JsonPropertyName("lifetimeSeconds")] uint LifetimeSeconds = 60,

        /// <summary>Minimum value change to trigger a notification (0 = any change).</summary>
        [property: JsonPropertyName("covIncrement")] float CovIncrement = 0f,

        /// <summary>True = device sends ConfirmedCOVNotification (requires ACK); false = unconfirmed.</summary>
        [property: JsonPropertyName("confirmedNotifications")] bool ConfirmedNotifications = false,

        /// <summary>
        /// How many attribute property reads to perform per minute across all objects.
        /// Attributes (PROP_UNITS, PROP_DESCRIPTION, …) are drip-polled continuously
        /// in a round-robin at this rate. Default: 5 reads/min.
        /// </summary>
        [property: JsonPropertyName("attributePollRatePerMinute")] int AttributePollRatePerMinute = 5
    );

    // ── Deziko hierarchy config (now unified as HierarchyConfig above) ─────────
    // Kept as alias for backward compatibility, but HierarchyConfig is preferred.

    /// <summary>
    /// Controls which BACnet objects are included after discovery.
    /// All fields are optional – omitting a field falls back to the built-in defaults.
    /// </summary>
    public record BacnetFilterConfig(
        /// <summary>
        /// Allowlist of BACnet object types to include. Each entry may be:
        ///   • Full enum name  – "OBJECT_ANALOG_INPUT"
        ///   • Short alias     – "AI", "AO", "AV", "BI", "BO", "BV", "MI", "MO", "MV"
        ///   • Numeric type ID – "0" … "1023"  (covers proprietary/vendor types ≥ 128)
        /// Null or empty → all object types are included.
        /// Default: AI, AO, AV, BI, BO, BV, MI, MV, OBJECT_MULTI_STATE_OUTPUT, 128.
        /// </summary>
        [property: JsonPropertyName("objectTypes")] List<string>? ObjectTypes = null,

        /// <summary>Inclusive instance number range filter. Default: 0–9999.</summary>
        [property: JsonPropertyName("instanceRange")] InstanceRange? InstanceRange = null,

        /// <summary>Regex applied to PROP_OBJECT_NAME (include). Default: "." (match all).</summary>
        [property: JsonPropertyName("namePattern")] string? NamePattern = null,

        /// <summary>
        /// Regex applied to PROP_OBJECT_NAME. Objects whose name matches are excluded.
        /// Applied after namePattern. Null = nothing excluded.
        /// </summary>
        [property: JsonPropertyName("excludeNamePattern")] string? ExcludeNamePattern = null,

        /// <summary>
        /// Regex applied to PROP_DESCRIPTION (include). Default: "" (no filter).
        /// </summary>
        [property: JsonPropertyName("descriptionPattern")] string? DescriptionPattern = null,

        /// <summary>Maximum number of objects to keep. 0 or null = unlimited.</summary>
        [property: JsonPropertyName("maxObjects")] int? MaxObjects = null
    )
    {
        // ── Built-in defaults (match Deziko common object types) ────────
        public static readonly List<string> DefaultObjectTypes = new()
            { "AI", "AO", "AV", "BI", "BO", "BV", "MI", "MO", "MV", "SV" };
        public static readonly InstanceRange DefaultInstanceRange = new(0, 9999);
        public const string DefaultNamePattern = ".";

        /// <summary>Default filter instance used when no "filter" block is present in config.</summary>
        public static readonly BacnetFilterConfig Default = new();

        public List<string> EffectiveObjectTypes => ObjectTypes?.Count > 0 ? ObjectTypes : DefaultObjectTypes;
        public InstanceRange EffectiveInstanceRange => InstanceRange ?? DefaultInstanceRange;
        public string EffectiveNamePattern => NamePattern ?? DefaultNamePattern;
    }

    public record InstanceRange(
        [property: JsonPropertyName("min")] uint Min,
        [property: JsonPropertyName("max")] uint Max
    );

    /// <summary>
    /// Which BACnet properties to read on every poll cycle and how to categorize them.
    /// Property names use BacnetPropertyIds enum names, e.g. "PROP_PRESENT_VALUE".
    /// Omitting either list falls back to built-in defaults.
    /// </summary>
    public record BacnetPropsConfig(
        /// <summary>Published as timeseries on every poll.</summary>
        [property: JsonPropertyName("telemetry")] List<string>? Telemetry = null,

        /// <summary>Published as attributes once on startup (and after rediscovery).</summary>
        [property: JsonPropertyName("attributes")] List<string>? Attributes = null
    )
    {
        public static readonly List<string> DefaultTelemetry = new() { "PROP_PRESENT_VALUE", "PROP_STATUS_FLAGS", "PROP_RELIABILITY" };
        public static readonly List<string> DefaultAttributes = new()
            { "PROP_OBJECT_NAME", "PROP_DESCRIPTION", "PROP_UNITS" };

        /// <summary>Default properties instance used when no "properties" block is present in config.</summary>
        public static readonly BacnetPropsConfig Default = new();

        public List<string> EffectiveTelemetry => Telemetry?.Count > 0 ? Telemetry : DefaultTelemetry;
        public List<string> EffectiveAttributes => Attributes?.Count > 0 ? Attributes : DefaultAttributes;
    }

    /// <summary>Controls how and when the full object list is (re-)fetched from the device.</summary>
    public record BacnetDiscoveryConfig(
        /// <summary>Run object discovery immediately on first poll.</summary>
        [property: JsonPropertyName("onStartup")] bool OnStartup = true,

        /// <summary>Re-discover after this many minutes (0 = never refresh).</summary>
        [property: JsonPropertyName("refreshIntervalMinutes")] int RefreshIntervalMinutes = 60,

        /// <summary>
        /// Delay in milliseconds between individual BACnet ReadProperty requests
        /// during full reads and discovery. Prevents overloading field controllers
        /// with large object counts. Default: 50 ms. Set to 0 to disable.
        /// </summary>
        [property: JsonPropertyName("readDelayMs")] int ReadDelayMs = 50
    )
    {
        public static readonly BacnetDiscoveryConfig Default = new();
    };

    // ── Modbus hierarchy config (superseded by DeviceConfig.AssetType) ─────────
    // Kept only for backward compatibility. New configs use "assetType" at device level.
    [Obsolete("Use DeviceConfig.AssetType instead.")]
    public record ModbusHierarchyConfig(
        [property: JsonPropertyName("enabled")] bool Enabled,
        [property: JsonPropertyName("assetType")] string? AssetType = null
    );

    // ── Monitoring Dashboard ──────────────────────────────────────────────────

    /// <summary>
    /// Configuration for the embedded read-only monitoring dashboard.
    /// When enabled, a Kestrel HTTP server serves a dashboard on the configured port.
    /// </summary>
    public record MonitoringConfig(
        [property: JsonPropertyName("enabled")] bool Enabled,
        [property: JsonPropertyName("port")] int Port = 5000,
        [property: JsonPropertyName("logBufferSize")] int LogBufferSize = 5000,
        [property: JsonPropertyName("language")] string Language = "en"
    );
}
