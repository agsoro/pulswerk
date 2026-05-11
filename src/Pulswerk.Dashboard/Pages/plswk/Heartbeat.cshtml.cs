using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Pulswerk.Dashboard.Pages
{
    public class HeartbeatModel : PageModel
    {
        private readonly DashboardDataService _dataService;

        public HeartbeatStatsDto Stats { get; private set; } = null!;

        public HeartbeatModel(DashboardDataService dataService)
        {
            _dataService = dataService;
        }

        public async Task OnGetAsync()
        {
            Stats = await _dataService.GetHeartbeatStatsAsync();
        }

        public async Task<JsonResult> OnGetStatsAsync()
        {
            var stats = await _dataService.GetHeartbeatStatsAsync();
            return new JsonResult(stats);
        }
    }
}
