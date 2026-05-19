using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pulswerk.Core;
using Pulswerk.Drivers;
using Pulswerk.Storage;

namespace Pulswerk.Dashboard.Pages
{
    public class AssetsModel : PageModel
    {
        private readonly DashboardDataService _dataService;

        public AssetsModel(DashboardDataService dataService)
        {
            _dataService = dataService;
        }

        public List<AssetNodeDto> AssetTrees { get; set; } = new();

        public void OnGet()
        {
            AssetTrees = _dataService.GetAssetTrees();
        }

        public JsonResult OnGetTree()
        {
            return new JsonResult(_dataService.GetAssetTrees());
        }

        public JsonResult OnGetLatestValues(string keys)
        {
            var keyList = keys?.Split(',', System.StringSplitOptions.RemoveEmptyEntries).ToList() ?? new List<string>();
            return new JsonResult(_dataService.GetCurrentValues(keyList));
        }

        public JsonResult OnGetAvailableTelemetries() => new JsonResult(_dataService.GetAvailableTelemetries());

        public async Task<JsonResult> OnGetHistory(string key, string? days, long? startTs, long? endTs)
        {
            if (startTs.HasValue && endTs.HasValue)
            {
                var d2 = await _dataService.GetTelemetryHistoryAsync(key, startTs.Value, endTs.Value);
                return new JsonResult(d2);
            }

            if (!double.TryParse(days, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double d))
                d = 7;
            var data = await _dataService.GetTelemetryHistoryAsync(key, d);
            return new JsonResult(data);
        }

        public async Task<JsonResult> OnGetPropertiesAsync(string key)
        {
            var data = await _dataService.GetPropertiesAsync(key);
            return new JsonResult(data);
        }

        public async Task<IActionResult> OnPostWrite([FromBody] WriteRequest request)
        {
            if (!DashboardAuth.CanWriteValue(HttpContext, _dataService.Config.Server))
                return new JsonResult(new { success = false, error = "Forbidden" }) { StatusCode = 403 };

            if (request == null || string.IsNullOrEmpty(request.Key))
                return BadRequest("Invalid request");

            bool success = await _dataService.WriteValueAsync(request.Key, request.Value);
            return new JsonResult(new { success });
        }

        public async Task<IActionResult> OnPostWriteComplex([FromBody] WriteComplexRequest request)
        {
            if (!DashboardAuth.CanWriteValue(HttpContext, _dataService.Config.Server))
                return new JsonResult(new { success = false, error = "Forbidden" }) { StatusCode = 403 };

            if (request == null || string.IsNullOrEmpty(request.Key))
                return BadRequest("Invalid request");

            bool success = await _dataService.WriteComplexValueAsync(request.Key, request.Value);
            return new JsonResult(new { success });
        }

        public class WriteRequest
        {
            public string Key { get; set; } = "";
            public double Value { get; set; }
        }

        public class WriteComplexRequest
        {
            public string Key { get; set; } = "";
            public object Value { get; set; } = null!;
        }
    }
}
