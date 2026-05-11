using System.Text.Json.Serialization;

namespace Pulswerk.Core
{
    public class DeviceStatusDto
    {
        [JsonPropertyName("totalDevices")] public int TotalDevices { get; set; }
        [JsonPropertyName("onlineDevices")] public int OnlineDevices { get; set; }
        [JsonPropertyName("offlineDevices")] public int OfflineDevices { get; set; }
        [JsonPropertyName("activeAlarms")] public int ActiveAlarms { get; set; }
        [JsonPropertyName("connectorVersion")] public string ConnectorVersion { get; set; } = "";
        [JsonPropertyName("uptimeSeconds")] public long UptimeSeconds { get; set; }
        [JsonPropertyName("logBufferSize")] public int LogBufferSize { get; set; }
        [JsonPropertyName("logBufferCapacity")] public int LogBufferCapacity { get; set; }
        [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";
    }

    public class DeviceDto
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("statusColor")] public string StatusColor { get; set; } = "";
        [JsonPropertyName("lastSeen")] public string LastSeen { get; set; } = "";
        [JsonPropertyName("connection")] public string Connection { get; set; } = "";
        [JsonPropertyName("port")] public int Port { get; set; }
        [JsonPropertyName("connectionId")] public string ConnectionId { get; set; } = "";
    }

    public class LogEntryDto
    {
        [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";
        [JsonPropertyName("severity")] public string Severity { get; set; } = "";
        [JsonPropertyName("message")] public string Message { get; set; } = "";
        [JsonPropertyName("source")] public string Source { get; set; } = "";
    }

    public class AlarmDisplayDto
    {
        [JsonPropertyName("id")] public string AlarmId { get; set; } = "";
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("severity")] public string Severity { get; set; } = "";
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("message")] public string Message { get; set; } = "";
        [JsonPropertyName("originator")] public string Originator { get; set; } = "";
        [JsonPropertyName("time")] public string Time { get; set; } = "";
        [JsonPropertyName("ackComment")] public string? AckComment { get; set; }
        [JsonPropertyName("bacnetAckKey")] public string? BacnetAckKey { get; set; }
    }

    public class AssetNodeDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("isView")] public bool IsView { get; set; }
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("children")] public List<AssetNodeDto> Children { get; set; } = new();
        [JsonPropertyName("points")] public List<AssetPointDto> Points { get; set; } = new();
    }

    public class PathSegmentDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
    }

    public class AssetPointDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("fullName")] public string FullName { get; set; } = "";
        [JsonPropertyName("value")] public string Value { get; set; } = "";
        [JsonPropertyName("units")] public string Units { get; set; } = "";
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("key")] public string Key { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("isWritable")] public bool IsWritable { get; set; }
        [JsonPropertyName("parentId")] public string ParentId { get; set; } = "";
        [JsonPropertyName("parentPath")] public List<PathSegmentDto> ParentPath { get; set; } = new();

        /// <summary>
        /// Non-null for multi-state / binary objects. Index 0 = first enum label.
        /// The frontend will render a combobox instead of a numeric stepper.
        /// </summary>
        [JsonPropertyName("enumValues")] public List<string>? EnumValues { get; set; }
    }

    public class PropertyDto
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("value")] public string Value { get; set; } = "";
        [JsonPropertyName("units")] public string Units { get; set; } = "";
    }

    /// <summary>
    /// Represents a telemetry key available for use in custom dashboard widgets.
    /// </summary>
    public class AvailableKeyDto
    {
        [JsonPropertyName("key")] public string Key { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("fullName")] public string FullName { get; set; } = "";
        [JsonPropertyName("units")] public string Units { get; set; } = "";
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("path")] public string Path { get; set; } = "";
        [JsonPropertyName("value")] public string Value { get; set; } = "";
        [JsonPropertyName("parentId")] public string ParentId { get; set; } = "";
        [JsonPropertyName("parentPath")] public List<PathSegmentDto> ParentPath { get; set; } = new();
        [JsonPropertyName("isWritable")] public bool IsWritable { get; set; }
        [JsonPropertyName("enumValues")] public List<string>? EnumValues { get; set; }
    }
}
