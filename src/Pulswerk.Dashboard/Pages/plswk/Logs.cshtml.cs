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

        public void OnGet()
        {
            Logs = _data.LogBuffer.GetLatest(500).Select(l => new LogEntryDto
            {
                Timestamp = l.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                Severity = l.Severity.ToString().ToLowerInvariant(),
                Message = l.Message,
                Source = l.Source
            }).ToList();
        }
    }
}
