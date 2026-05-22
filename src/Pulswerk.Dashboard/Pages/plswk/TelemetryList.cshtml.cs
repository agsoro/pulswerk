using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pulswerk.Core;
using Pulswerk.Dashboard;

namespace Pulswerk.Dashboard.Pages
{
    public class TelemetryListModel : PageModel
    {
        private readonly DashboardDataService _dataService;

        public TelemetryListModel(DashboardDataService dataService)
        {
            _dataService = dataService;
        }

        public void OnGet()
        {
        }
    }
}
