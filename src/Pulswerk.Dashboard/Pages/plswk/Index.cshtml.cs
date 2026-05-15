using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pulswerk.Core;
using Pulswerk.Drivers;
using Pulswerk.Storage;

namespace Pulswerk.Dashboard.Pages
{
    public class IndexModel : PageModel
    {
        private readonly DashboardDataService _data;

        public IndexModel(DashboardDataService data)
        {
            _data = data;
        }

        public DeviceStatusDto Status { get; private set; } = new();

        // Alarm counts per severity
        public int AlarmCritical { get; private set; }
        public int AlarmMajor { get; private set; }
        public int AlarmMinor { get; private set; }
        public int AlarmWarning { get; private set; }
        public int AlarmMaintenance { get; private set; }
        public int AlarmTotal { get; private set; }

        public void OnGet()
        {
            Status = new DeviceStatusDto
            {
                TotalDevices = _data.Config.Devices.Count,
                OfflineDevices = _data.OfflineDevices.Count,
                OnlineDevices = _data.Config.Devices.Count - _data.OfflineDevices.Count,
                UptimeSeconds = (long)_data.Uptime.Elapsed.TotalSeconds,
                ConnectorVersion = _data.Version,
                LogBufferSize = _data.LogBuffer.Count,
                LogBufferCapacity = _data.LogBuffer.Capacity
            };

            var alarms = _data.AlarmStore.GetAllActive();
            foreach (var a in alarms)
            {
                if (a.Status != "ACTIVE_UNACK") continue;
                switch (a.Severity)
                {
                    case "CRITICAL": AlarmCritical++; break;
                    case "MAJOR": AlarmMajor++; break;
                    case "MINOR": AlarmMinor++; break;
                    case "MAINTENANCE": AlarmMaintenance++; break;
                    default: AlarmWarning++; break;
                }
            }
            AlarmTotal = AlarmCritical + AlarmMajor + AlarmMinor + AlarmWarning + AlarmMaintenance;
        }

        // ── Favorites handlers (Tree, History, Properties, Write) ────────────

        public JsonResult OnGetTree()
        {
            var trees = _data.GetAssetTrees();
            return new JsonResult(trees);
        }

        public JsonResult OnGetLatestValues(string keys)
        {
            var keyList = keys?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() ?? new List<string>();
            return new JsonResult(_data.GetCurrentValues(keyList));
        }

        public JsonResult OnGetAvailableKeys() => new JsonResult(_data.GetAvailableKeys());

        public async Task<JsonResult> OnGetHistory(string key, string days)
        {
            if (!double.TryParse(days, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double d))
                d = 7;
            var data = await _data.GetTelemetryHistoryAsync(key, d);
            return new JsonResult(data);
        }

        public async Task<JsonResult> OnGetPropertiesAsync(string key)
        {
            var data = await _data.GetPropertiesAsync(key);
            return new JsonResult(data);
        }

        public async Task<IActionResult> OnPostWrite([FromBody] WriteRequest request)
        {
            if (!DashboardAuth.CanWriteValue(HttpContext, _data.Config.Server))
                return new JsonResult(new { success = false, error = "Forbidden" }) { StatusCode = 403 };

            if (request == null || string.IsNullOrEmpty(request.Key))
                return BadRequest("Invalid request");

            bool success = await _data.WriteValueAsync(request.Key, request.Value);
            return new JsonResult(new { success });
        }

        public class WriteRequest
        {
            public string Key { get; set; } = "";
            public double Value { get; set; }
        }
    }
}
