using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Pulswerk.Visual.Tests
{
    [TestFixture]
    public class InventoryVisualTests : BrowserTestBase
    {
        [Test]
        public async Task InventoryPage_RendersCorrectly()
        {
            await Page.GotoAsync($"{EffectiveDashboardUrl}/plswk/AssetsList");

            // Wait for data to load
            var tableBody = Page.Locator("#tableBody");
            await Microsoft.Playwright.Assertions.Expect(tableBody).Not.ToContainTextAsync("Loading asset inventory...");

            // Assert core components
            await AssertScreenshot("inventory_page_initial");

            // Test Search
            await Page.FillAsync("#assetSearch", "power");
            await Task.Delay(500); // Wait for filter
            await AssertScreenshot("inventory_page_search_power");

            // Clear search
            await Page.FillAsync("#assetSearch", "");

            // Test Sort (by name)
            await Page.ClickAsync("th[data-sort='name']");
            await Task.Delay(200);
            await AssertScreenshot("inventory_page_sort_name_asc");
        }
    }
}
