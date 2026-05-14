using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pulswerk.Core;
using Pulswerk.Drivers;
using Pulswerk.Storage;

namespace Pulswerk.Dashboard.Pages
{
    public class ConnectionsModel : PageModel
    {
        private readonly DashboardDataService _data;

        public ConnectionsModel(DashboardDataService data)
        {
            _data = data;
        }

        public List<ConnectionDetailDto> Connections { get; private set; } = new();

        public void OnGet()
        {
            foreach (var conn in _data.Config.Connections)
            {
                var connDevices = _data.Config.Devices
                    .Where(d => d.ConnectionId == conn.Id)
                    .ToList();

                bool isOffline = connDevices.Count > 0 &&
                                 connDevices.All(d => _data.OfflineDevices.Contains(d.Name));

                var lastPolled = connDevices
                    .Select(d => _data.LastPolledAtMap.TryGetValue(d.Name, out var t) ? t : default)
                    .Where(t => t != default)
                    .DefaultIfEmpty(default)
                    .Max();

                string tbType = conn.Type switch
                {
                    "bacnet-ip" => "BACnet Gateway",
                    "modbus-tcp" => "Modbus Gateway",
                    _ => conn.Type
                };

                var deviceRows = connDevices.Select(d =>
                {
                    bool offline = _data.OfflineDevices.Contains(d.Name);
                    _data.LastPolledAtMap.TryGetValue(d.Name, out var polledAt);

                    // If the device is not explicitly offline but hasn't delivered
                    // data in over 5 minutes (or never), mark it as "stale" (amber).
                    bool stale = !offline &&
                                 (polledAt == default ||
                                  (System.DateTime.UtcNow - polledAt).TotalMinutes > 5);

                    string protocol = d.DeviceType.ToLowerInvariant() switch
                    {
                        "janitza" => "Modbus",
                        "glueck" => "Modbus",
                        "abb" => "Modbus",
                        "sunspec" => "Modbus",
                        "bacnet" => "BACnet",
                        "deziko" => "Deziko (BACnet)",
                        _ => d.DeviceType
                    };

                    string address = d.DeviceId.HasValue ? $"ID {d.DeviceId}" : "–";

                    return new ConnectedDeviceDto
                    {
                        Name = d.Name,
                        DeviceType = d.DeviceType,
                        Protocol = protocol,
                        Address = address,
                        AssetType = d.AssetType,
                        Status = offline ? "offline" : stale ? "stale" : "online",
                        LastSeen = polledAt == default
                                       ? "Never"
                                       : polledAt.ToString("HH:mm:ss")
                    };
                }).ToList();

                // Connection status: if ALL devices offline → offline;
                //   if ANY device stale → stale; otherwise → online
                string connStatus = isOffline && connDevices.Count > 0
                    ? "offline"
                    : deviceRows.Any(d => d.Status == "stale") ? "stale" : "online";

                Connections.Add(new ConnectionDetailDto
                {
                    Id = conn.Id,
                    Name = conn.EffectiveName,
                    Type = tbType,
                    Address = (conn.Type == "bacnet-ip" ? conn.LocalAddress : conn.Address) ?? "",
                    Port = (conn.Type == "bacnet-ip" ? conn.LocalPort : conn.Port) ?? 0,
                    Status = connStatus,
                    LastSeen = lastPolled == default
                                    ? "Never"
                                    : lastPolled.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                    DeviceCount = connDevices.Count,
                    OnlineCount = deviceRows.Count(d => d.Status == "online"),
                    Devices = deviceRows
                });
            }
        }
    }

    public class ConnectionDetailDto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string Address { get; set; } = "";
        public int Port { get; set; }
        public string Status { get; set; } = "";
        public string LastSeen { get; set; } = "";
        public int DeviceCount { get; set; }
        public int OnlineCount { get; set; }
        public List<ConnectedDeviceDto> Devices { get; set; } = new();
    }

    public class ConnectedDeviceDto
    {
        public string Name { get; set; } = "";
        public string DeviceType { get; set; } = "";
        public string Protocol { get; set; } = "";
        public string Address { get; set; } = "";
        public string? AssetType { get; set; }
        public string Status { get; set; } = "";
        public string LastSeen { get; set; } = "";
    }
}
