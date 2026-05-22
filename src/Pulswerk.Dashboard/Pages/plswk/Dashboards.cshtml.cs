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
    }
}
