using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pulswerk.Core;
using Pulswerk.Dashboard;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Pulswerk.Dashboard.Pages
{
    public class DataPointsListModel : PageModel
    {
        private readonly DashboardDataService _dataService;

        public DataPointsListModel(DashboardDataService dataService)
        {
            _dataService = dataService;
        }

        public void OnGet()
        {
        }

        public IActionResult OnGetTree()
        {
            // We use the same tree-based data but flatten it in the JS for the table
            return new JsonResult(_dataService.GetAssetTrees());
        }

        public IActionResult OnGetPoints()
        {
            return new JsonResult(_dataService.GetAvailableDataPoints());
        }

        public JsonResult OnGetLatestValues(string keys)
        {
            var keyList = keys?.Split(',', System.StringSplitOptions.RemoveEmptyEntries).ToList() ?? new List<string>();
            return new JsonResult(_dataService.GetCurrentValues(keyList));
        }

        public JsonResult OnGetAvailableDataPoints() => new JsonResult(_dataService.GetAvailableDataPoints());

        public async Task<IActionResult> OnGetHistoryAsync(string key, string? days, long? startTs, long? endTs)
        {
            if (startTs.HasValue && endTs.HasValue)
            {
                var historyRange = await _dataService.GetDataPointHistoryAsync(key, startTs.Value, endTs.Value);
                return new JsonResult(historyRange);
            }

            if (!double.TryParse(days, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double d))
                d = 0.166; // fallback to ~4 hours if nothing provided

            var history = await _dataService.GetDataPointHistoryAsync(key, d);
            return new JsonResult(history);
        }

        public async Task<IActionResult> OnGetPropertiesAsync(string key)
        {
            var props = await _dataService.GetPropertiesAsync(key);
            return new JsonResult(props);
        }

        public async Task<IActionResult> OnPostWriteAsync([FromBody] WriteRequest request)
        {
            if (!DashboardAuth.CanWriteValue(HttpContext, _dataService.Config.Server))
                return new JsonResult(new { success = false, error = "Forbidden" }) { StatusCode = 403 };

            if (string.IsNullOrEmpty(request.Key)) return new JsonResult(new { success = false, message = "Key missing" });
            var success = await _dataService.WriteValueAsync(request.Key, request.Value);
            return new JsonResult(new { success });
        }

        public async Task<IActionResult> OnPostWriteComplexAsync([FromBody] WriteComplexRequest request)
        {
            if (!DashboardAuth.CanWriteValue(HttpContext, _dataService.Config.Server))
                return new JsonResult(new { success = false, error = "Forbidden" }) { StatusCode = 403 };

            if (string.IsNullOrEmpty(request.Key)) return new JsonResult(new { success = false, message = "Key missing" });
            var success = await _dataService.WriteComplexValueAsync(request.Key, request.Value);
            return new JsonResult(new { success });
        }

        public class WriteRequest { public string Key { get; set; } = ""; public double Value { get; set; } }
        public class WriteComplexRequest { public string Key { get; set; } = ""; public object Value { get; set; } = null!; }
    }
}
