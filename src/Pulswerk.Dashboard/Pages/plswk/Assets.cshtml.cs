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
    }
}
