import { test, expect } from '@playwright/test';

declare const process: any;

const ADMIN_PROXY_URL = process.env.ADMIN_PROXY_URL || 'http://localhost:5002';

test.describe('KNX Real E2E Tests (Non-Mocked)', () => {

    test('KNX Connection details and live device listing', async ({ page }) => {
        // Go to the Connections page via the auth proxy as admin
        await page.goto(`${ADMIN_PROXY_URL}/plswk/Connections`);
        
        // Wait for connection list to render and check that KNX Test Gateway is visible
        const connCard = page.locator('text=KNX Test Gateway');
        await expect(connCard).toBeVisible({ timeout: 15000 });

        // Read the actual address from the API so the test works in both
        // Docker (knx-sim) and local (127.0.0.1) environments.
        const apiRes = await page.request.get(`${ADMIN_PROXY_URL}/plswk/api/connections`);
        const apiData = await apiRes.json();
        const knxConn = apiData.connections.find((c: any) => c.id === 'knx-test');
        const expectedAddr = knxConn?.address || 'knx-sim';
        const expectedPort = knxConn?.port || 3671;
        
        // Click the connection card
        await connCard.click();

        // Check connection detail header (scoped to detail panel)
        const detailPanel = page.locator('[data-testid="conn-detail"]');
        await expect(detailPanel.locator(`text=${expectedAddr} : ${expectedPort}`)).toBeVisible();

        // Check device status and Address in detail table
        const deviceName = page.locator('text=Living Room KNX');
        await expect(deviceName).toBeVisible();
        await expect(page.locator(`text=${expectedAddr}`).first()).toBeVisible();
        
        // Status should be online
        const statusText = page.locator('text=online');
        await expect(statusText.first()).toBeVisible();
    });

    test('KNX Assets Tree live values and write control', async ({ page }) => {
        // Go to Assets page
        await page.goto(`${ADMIN_PROXY_URL}/plswk/Assets`);

        // Check Building A and expand it
        const folderA = page.locator('.tree-row').filter({ hasText: /^Building A$/ }).first();
        await expect(folderA).toBeVisible({ timeout: 15000 });
        await folderA.locator('.tree-toggle').click();
        await page.waitForTimeout(300);

        // Check Living Room and expand it
        const folderRoom = page.locator('.tree-row').filter({ hasText: /^Living Room$/ }).first();
        await expect(folderRoom).toBeVisible();
        await folderRoom.locator('.tree-toggle').click();
        await page.waitForTimeout(300);

        // Select the KNX controller node — expand it first to reveal nested folders
        const deviceNode = page.locator('.tree-row').filter({ hasText: 'Living Room KNX' }).first();
        await expect(deviceNode).toBeVisible();
        // Expand the device node to show its nested ETS hierarchy children
        const deviceToggle = deviceNode.locator('.tree-toggle');
        if (await deviceToggle.isVisible()) {
            await deviceToggle.click();
            await page.waitForTimeout(300);
        }
        await deviceNode.click();
        await page.waitForTimeout(500);

        // The KNX device nests its telemetry points under the ETS hierarchy
        // (Building A → Living Room). Expand those subfolders to reveal the points.
        // Use last() to target the deepest nested occurrence.
        const nestedBuildingA = page.locator('.tree-row').filter({ hasText: /^Building A$/ }).last();
        await nestedBuildingA.locator('.tree-toggle').click();
        await page.waitForTimeout(300);
        const nestedLivingRoom = page.locator('.tree-row').filter({ hasText: /^Living Room$/ }).last();
        await nestedLivingRoom.locator('.tree-toggle').click();
        await page.waitForTimeout(300);
        await nestedLivingRoom.click();
        await page.waitForTimeout(500);

        // Verify the telemetry points are listed
        await expect(page.getByText('Temperature Sensor', { exact: true })).toBeVisible();
        await expect(page.getByText('Setpoint Temperature', { exact: true })).toBeVisible();
        await expect(page.getByText('Ceiling Light', { exact: true })).toBeVisible();
        await expect(page.getByText('Power Sensor', { exact: true })).toBeVisible();
        await expect(page.getByText('Simulated Switch', { exact: true })).toBeVisible();
        await expect(page.getByText('Simulated Dimmer', { exact: true })).toBeVisible();

        // Locate "Simulated Switch" (which is writable)
        const simSwitchRow = page.locator('.glass').filter({ hasText: 'Simulated Switch' });
        await expect(simSwitchRow).toBeVisible();

        // Click the row to open the details modal
        await simSwitchRow.click();
        await page.waitForSelector('#telemetryDetailsModal', { state: 'visible' });

        // Click Edit Value button to start edit mode
        await page.locator('#telInlineEditStartBtn').click();
        await page.waitForTimeout(100);

        // Verify the inlineBoolGroup is visible
        const boolGroup = page.locator('#inlineBoolGroup');
        await expect(boolGroup).toBeVisible();

        // Verify state is attached (the actual input checkbox is visually hidden by standard toggle switch styling)
        const toggleCheckbox = page.locator('#boolInput');
        await expect(toggleCheckbox).toBeAttached();
        
        const isChecked = await toggleCheckbox.isChecked();
        
        // Click toggle slider to flip the value
        await page.locator('.bool-toggle-slider').click();
        await page.waitForTimeout(100);

        // Verify state flipped locally
        expect(await toggleCheckbox.isChecked()).toBe(!isChecked);

        // Click Save Changes
        await page.locator('#saveBtn').click();
        await page.waitForTimeout(1600); // wait for success state timeout (1500ms) to resolve
        
        // Close modal manually
        await page.locator('#telemetryDetailsModal .close-modal').first().click();
        await page.waitForSelector('#telemetryDetailsModal', { state: 'hidden' });
        
        // Wait for connection to update value and reflect it in UI
        await page.waitForTimeout(500);
    });
});
