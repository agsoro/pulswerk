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
    public class DashboardsModel : PageModel
    {
        private readonly DashboardDataService _data;
        private readonly DashboardStore _store;

        public DashboardsModel(DashboardDataService data, DashboardStore store)
        {
            _data = data;
            _store = store;
        }

        public List<DashboardDefinition> AllDashboards { get; set; } = new();
        public DashboardDefinition? CurrentDashboard { get; set; }
        public bool EditMode { get; set; }

        public void OnGet(string? id, string? name, bool edit = false)
        {
            AllDashboards = _store.GetAll();
            if (!string.IsNullOrEmpty(id))
            {
                CurrentDashboard = _store.GetById(id);
                EditMode = edit;
            }
        }

        // ── CRUD API ─────────────────────────────────────────────────────────

        public JsonResult OnGetList()
        {
            return new JsonResult(_store.GetAll());
        }

        public IActionResult OnPostCreate([FromBody] CreateDashboardRequest req)
        {
            if (!DashboardAuth.CanEditDashboard(HttpContext, _data.Config.Server))
                return new JsonResult(new { success = false, error = "Forbidden" }) { StatusCode = 403 };

            if (req == null || string.IsNullOrWhiteSpace(req.Name))
                return BadRequest("Name is required");

            var dash = _store.Create(req.Name, req.Description ?? "");
            return new JsonResult(dash);
        }

        public IActionResult OnPostSave([FromBody] DashboardDefinition dash)
        {
            if (!DashboardAuth.CanEditDashboard(HttpContext, _data.Config.Server))
                return new JsonResult(new { success = false, error = "Forbidden" }) { StatusCode = 403 };

            if (dash == null || string.IsNullOrEmpty(dash.Id))
                return BadRequest("Invalid dashboard");

            bool ok = _store.Save(dash);
            return new JsonResult(new { success = ok });
        }

        public IActionResult OnPostDelete([FromBody] DeleteRequest req)
        {
            if (!DashboardAuth.CanEditDashboard(HttpContext, _data.Config.Server))
                return new JsonResult(new { success = false, error = "Forbidden" }) { StatusCode = 403 };

            if (req == null || string.IsNullOrEmpty(req.Id))
                return BadRequest("Invalid ID");

            bool ok = _store.Delete(req.Id);
            return new JsonResult(new { success = ok });
        }

        // ── Widget data API ──────────────────────────────────────────────────

        /// <summary>Returns all available data point keys for the key picker.</summary>
        public JsonResult OnGetAvailableTelemetries()
        {
            var keys = _data.GetAvailableTelemetries();
            return new JsonResult(keys);
        }

        /// <summary>Fetches historical data point data for widget rendering.</summary>
        public async Task<JsonResult> OnGetWidgetData(string keys, long startTs, long endTs)
        {
            var keyList = keys?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
                          ?? new List<string>();

            if (keyList.Count == 0)
                return new JsonResult(new Dictionary<string, object?>());

            var data = await _data.GetTelemetryHistoryForWidgetAsync(keyList, startTs, endTs);
            return new JsonResult(data);
        }

        /// <summary>Fetches current values for latest-values and single-value widgets.</summary>
        public JsonResult OnGetLatestValues(string keys)
        {
            var keyList = keys?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
                          ?? new List<string>();

            var values = _data.GetCurrentValues(keyList);
            return new JsonResult(values);
        }

        // ── Handlers for _AssetModals (history/edit/properties) ──────────

        public JsonResult OnGetTree()
        {
            return new JsonResult(_data.GetAssetTrees());
        }

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

        // ── Request DTOs ─────────────────────────────────────────────────

        public class CreateDashboardRequest
        {
            public string Name { get; set; } = "";
            public string? Description { get; set; }
        }

        public class DeleteRequest
        {
            public string Id { get; set; } = "";
        }

        public class WriteRequest
        {
            public string Key { get; set; } = "";
            public double Value { get; set; }
        }
    }
}
