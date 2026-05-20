using Microsoft.Playwright;
using NUnit.Framework;

namespace Pulswerk.Visual.Tests;

[TestFixture]
public class ScheduleEditorTests : BrowserTestBase
{
    [Test]
    public async Task CanOpenScheduleEditorAndModifySwitchingTimes()
    {
        await Page.GotoAsync(Url("/plswk/Assets"));
        await WaitForDashboard();

        // Expand 'Building A' and select a child
        await Page.ClickAsync(".tree-toggle"); // Expand first node
        await Page.WaitForSelectorAsync(".tree-row:has-text('Floor')", new() { State = WaitForSelectorState.Visible });
        await Page.ClickAsync(".tree-row:has-text('Floor')");

        // Wait for point list to load
        await Page.WaitForSelectorAsync(Tid("schedule-btn"), new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        var scheduleBtn = Page.Locator(Tid("schedule-btn")).First;
        await Expect(scheduleBtn).ToBeVisibleAsync();
        await scheduleBtn.ClickAsync();

        // 2. Assert modal is open and shows loading then data
        await Page.WaitForSelectorAsync("#scheduleModal", new() { State = WaitForSelectorState.Visible });
        await Page.WaitForSelectorAsync(".sched-day-row", new() { State = WaitForSelectorState.Visible });

        // 3. Verify initial state (Read-only)
        await Expect(Page.Locator("#btnEditSchedule")).ToBeVisibleAsync();
        await Expect(Page.Locator("#editScheduleActions")).ToBeHiddenAsync();
        Assert.That(await Page.Locator(".sched-entry-view").CountAsync(), Is.GreaterThan(0));

        // 4. Enter Edit Mode
        await Page.ClickAsync("#btnEditSchedule");
        await Expect(Page.Locator("#editScheduleActions")).ToBeVisibleAsync();
        Assert.That(await Page.Locator(".sched-entry-edit").CountAsync(), Is.GreaterThan(0));

        // 5. Add a new switching point to Monday (index 0)
        var monRow = Page.Locator(".sched-day-row").First;
        var addBtn = monRow.Locator(".sched-add-btn");
        await addBtn.ClickAsync();

        // Verify a new entry was added
        var entries = monRow.Locator(".sched-entry-edit");
        var initialCount = await entries.CountAsync();

        // 6. Change the value of the last entry
        var lastValueInput = entries.Last.Locator("input[type='number']");
        await lastValueInput.FillAsync("24.5");

        // 7. Save changes
        var saveBtn = Page.Locator("button:has-text('Save')");
        await saveBtn.ClickAsync();

        // 8. Wait for save to complete (modal returns to view mode)
        await Page.WaitForSelectorAsync("#editScheduleActions", new() { State = WaitForSelectorState.Hidden });

        // 9. Verify the new value is visible in the view mode
        await Expect(Page.Locator(".sched-entry-view:has-text('24.5')")).ToBeVisibleAsync();

        // Final screenshot for regression
        await AssertElementScreenshot("#scheduleModal .modal-content", "schedule-editor-after-save");
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
