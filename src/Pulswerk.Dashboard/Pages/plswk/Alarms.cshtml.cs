using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pulswerk.Core;
using Pulswerk.Drivers;
using Pulswerk.Drivers.BACnet;
using Pulswerk.Storage;

namespace Pulswerk.Dashboard.Pages
{
    public class AlarmsModel : PageModel
    {
        private readonly DashboardDataService _data;

        public AlarmsModel(DashboardDataService data)
        {
            _data = data;
        }

        [BindProperty(SupportsGet = true)]
        public string? Severity { get; set; }

        public List<AlarmDisplayDto> Alarms { get; private set; } = new();
        public int CountCritical { get; private set; }
        public int CountMajor { get; private set; }
        public int CountMinor { get; private set; }
        public int CountWarning { get; private set; }
        public int CountAcked { get; private set; }

        public void OnGet()
        {
            try
            {
                var allRecords = _data.AlarmStore.GetAllActive();
                var all = allRecords.Select(a => new AlarmDisplayDto
                {
                    AlarmId = a.Id,
                    Type = a.Type,
                    Severity = a.Severity,
                    Status = a.Status,
                    Message = a.Message,
                    Originator = a.Originator,
                    Time = DateTimeOffset.FromUnixTimeMilliseconds(a.CreatedAt).ToString("o"),
                    AckComment = a.AckComment,
                    BacnetAckKey = a.BacnetAckKey
                }).ToList();

                var unacked = all.Where(a => a.Status == "ACTIVE_UNACK").ToList();

                CountCritical = unacked.Count(a => a.Severity == "CRITICAL");
                CountMajor = unacked.Count(a => a.Severity == "MAJOR");
                CountMinor = unacked.Count(a => a.Severity == "MINOR");
                CountWarning = unacked.Count(a => a.Severity != "CRITICAL" && a.Severity != "MAJOR" && a.Severity != "MINOR");
                CountAcked = all.Count(a => a.Status.StartsWith("ACTIVE_ACK"));

                Alarms = Severity switch
                {
                    "ACKED" => all.Where(a => a.Status.StartsWith("ACTIVE_ACK")).ToList(),
                    { } s when !string.IsNullOrEmpty(s) => unacked.Where(a => string.Equals(a.Severity, s, StringComparison.OrdinalIgnoreCase)).ToList(),
                    _ => all
                };
            }
            catch (Exception ex)
            {
                Log.Error($"[Dashboard] Failed to fetch alarms: {ex.Message}");
            }
        }

        public IActionResult OnPostAck([FromBody] AckRequest req)
        {
            if (!DashboardAuth.CanAckAlarm(HttpContext, _data.Config.Server))
                return new JsonResult(new { success = false, error = "Forbidden" }) { StatusCode = 403 };

            if (string.IsNullOrEmpty(req?.AlarmId))
                return new JsonResult(new { success = false, error = "Missing alarm ID" });

            try
            {
                // 1. Acknowledge in local alarm store
                bool ok = _data.AlarmStore.Acknowledge(req.AlarmId, req.Comment);

                // 2. Send BACnet AcknowledgeAlarm to the originating field device (Deziko pattern)
                bool bacnetAcked = false;
                if (ok && !string.IsNullOrEmpty(req.BacnetAckKey))
                {
                    string ackText = !string.IsNullOrWhiteSpace(req.Comment)
                        ? req.Comment
                        : "Acknowledged via Deziko Dashboard";
                    bacnetAcked = BacnetDriver.SendAlarmAcknowledgement(req.BacnetAckKey, ackText);
                }

                return new JsonResult(new { success = ok, bacnetAcked });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, error = ex.Message });
            }
        }

        public class AckRequest
        {
            public string AlarmId { get; set; } = "";
            public string Comment { get; set; } = "";
            public string? BacnetAckKey { get; set; }
        }
    }
}
