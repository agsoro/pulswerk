using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Pulswerk.Visual.Tests
{
    [TestFixture]
    public class HeartbeatVisualTests : BrowserTestBase
    {
        [Test]
        public async Task HeartbeatPage_RendersCorrectly()
        {
            await Page.GotoAsync(Url("/plswk/Heartbeat"));
            await WaitForDashboard();
            await DisableAnimations();

            // Wait for initial stats to load (they are updated by JS)
            await Page.WaitForFunctionAsync("() => document.getElementById('uptime').innerText !== ''");

            await AssertScreenshot("heartbeat-page", fullPage: true);
        }

        [Test]
        public async Task HeartbeatPage_SidebarLink_Works()
        {
            await Page.GotoAsync(Url("/plswk/"));
            await WaitForDashboard();

            // Hover over sidebar to expand it
            await Page.Locator(Tid("sidebar")).HoverAsync();
            await Page.WaitForTimeoutAsync(200);

            // Click the Heartbeat link
            await Page.Locator(Tid("nav-heartbeat")).ClickAsync();

            // Verify we are on the Heartbeat page
            Assert.That(Page.Url, Does.EndWith("/Heartbeat"));
            var h1 = await Page.Locator(Tid("page-title")).InnerTextAsync();
            Assert.That(h1, Is.EqualTo("System Heartbeat"));
        }
    }
}
