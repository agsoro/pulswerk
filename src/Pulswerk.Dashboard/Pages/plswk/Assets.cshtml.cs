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

        public async Task<JsonResult> OnGetHistory(string key, string days)
        {
            if (!double.TryParse(days, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double d))
                d = 7;
            var data = await _dataService.GetTelemetryHistoryAsync(key, d);
            return new JsonResult(data);
        }

        public JsonResult OnGetProperties(string key)
        {
            var data = _dataService.GetPointProperties(key);
            return new JsonResult(data);
        }

        public async Task<IActionResult> OnPostWrite([FromBody] WriteRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Key))
                return BadRequest("Invalid request");

            bool success = await _dataService.WriteValueAsync(request.Key, request.Value);
            return new JsonResult(new { success });
        }

        public class WriteRequest
        {
            public string Key { get; set; } = "";
            public double Value { get; set; }
        }
    }
}
