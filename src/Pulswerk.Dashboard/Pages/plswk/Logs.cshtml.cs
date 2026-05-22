using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pulswerk.Core;
using Pulswerk.Drivers;
using Pulswerk.Storage;

namespace Pulswerk.Dashboard.Pages
{
    public class LogsModel : PageModel
    {
        private readonly DashboardDataService _data;

        public LogsModel(DashboardDataService data)
        {
            _data = data;
        }

        public List<LogEntryDto> Logs { get; private set; } = new();
        public string CurrentLevel { get; private set; } = "all";

        public void OnGet([FromQuery] string? level)
        {
            CurrentLevel = level ?? "all";
            var allLogs = _data.LogBuffer.GetAll();

            if (CurrentLevel != "all")
            {
                if (CurrentLevel == "info")
                {
                    allLogs = allLogs.Where(l => l.Severity != LogSeverity.Debug).ToList();
                }
                else if (CurrentLevel == "warning")
                {
                    allLogs = allLogs.Where(l => l.Severity == LogSeverity.Warning || l.Severity == LogSeverity.Error).ToList();
                }
                else if (CurrentLevel == "error")
                {
                    allLogs = allLogs.Where(l => l.Severity == LogSeverity.Error).ToList();
                }
            }

            Logs = allLogs.OrderByDescending(l => l.Timestamp)
                          .Take(500)
                          .Reverse()
                          .Select(l => new LogEntryDto
                          {
                              Timestamp = l.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                              Severity = l.Severity.ToString().ToLowerInvariant(),
                              Message = l.Message,
                              Source = l.Source
                          }).ToList();
        }
    }
}
