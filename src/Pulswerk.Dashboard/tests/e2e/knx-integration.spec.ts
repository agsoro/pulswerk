import { test, expect } from './fixtures';

test.describe('KNX IP Secure & Tunneling E2E Tests', () => {

    test.beforeEach(async ({ page }) => {
        // Mock connections API for KNX
        await page.route('**/api/connections', async route => {
            await route.fulfill({
                status: 200,
                contentType: 'application/json',
                body: JSON.stringify({
                    connections: [
                        {
                            id: "knx-test",
                            name: "KNX Test Gateway",
                            type: "knx-ip",
                            address: "knx-sim",
                            port: 3671,
                            status: "online",
                            onlineCount: 1,
                            deviceCount: 1,
                            devices: [
                                {
                                    name: "Living Room KNX",
                                    deviceType: "knx",
                                    protocol: "knx-ip",
                                    address: "15.15.250",
                                    assetType: "KNX Controller",
                                    status: "online",
                                    lastSeen: "2026-06-19 11:30:00"
                                }
                            ]
                        }
                    ],
                    canEditConfig: true
                })
            });
        });

        // Mock connection health API
        await page.route('**/api/connection-health/**', async route => {
            await route.fulfill({
                status: 200,
                contentType: 'application/json',
                body: JSON.stringify([
                    { t: "2026-06-19T11:00:00Z", online: 1, total: 1 },
                    { t: "2026-06-19T11:05:00Z", online: 1, total: 1 }
                ])
            });
        });

        // Mock asset tree API for KNX points autodetected from XML
        await page.route('**/api/tree', async route => {
            await route.fulfill({
                status: 200,
                contentType: 'application/json',
                body: JSON.stringify([
                    {
                        id: "building-a",
                        name: "Building A",
                        type: "Folder",
                        isView: true,
                        children: [
                            {
                                id: "living-room",
                                name: "Living Room",
                                type: "Folder",
                                isView: true,
                                children: [
                                    {
                                        id: "knx-living-room",
                                        name: "Living Room KNX",
                                        type: "KNX Device",
                                        isView: true,
                                        children: [],
                                        telemetries: [
                                            {
                                                key: "knx-living-room_temp_living",
                                                name: "Temperature Sensor",
                                                fullName: "Living Room KNX / Temperature Sensor",
                                                type: "Analog",
                                                units: "°C",
                                                isWritable: false,
                                                value: 21.5
                                            },
                                            {
                                                key: "knx-living-room_setpoint_living",
                                                name: "Setpoint Temperature",
                                                fullName: "Living Room KNX / Setpoint Temperature",
                                                type: "Analog",
                                                units: "°C",
                                                isWritable: true,
                                                value: 22.0
                                            },
                                            {
                                                key: "knx-living-room_light_living",
                                                name: "Ceiling Light",
                                                fullName: "Living Room KNX / Ceiling Light",
                                                type: "Binary",
                                                units: "",
                                                isWritable: true,
                                                value: false
                                            },
                                            {
                                                key: "knx-living-room_power_living",
                                                name: "Power Sensor",
                                                fullName: "Living Room KNX / Power Sensor",
                                                type: "Analog",
                                                units: "W",
                                                isWritable: false,
                                                value: 450.0
                                            },
                                            {
                                                key: "knx-living-room_sim_switch",
                                                name: "Simulated Switch",
                                                fullName: "Living Room KNX / Simulated Switch",
                                                type: "Binary",
                                                units: "",
                                                isWritable: true,
                                                value: false
                                            },
                                            {
                                                key: "knx-living-room_sim_dimmer",
                                                name: "Simulated Dimmer",
                                                fullName: "Living Room KNX / Simulated Dimmer",
                                                type: "Analog",
                                                units: "",
                                                isWritable: true,
                                                value: 128.0
                                            }
                                        ]
                                    }
                                ]
                            }
                        ]
                    }
                ])
            });
        });

        // Mock latest values API
        await page.route('**/api/telemetry/latest**', async route => {
            await route.fulfill({
                status: 200,
                contentType: 'application/json',
                body: JSON.stringify({
                    "knx-living-room_temp_living": 21.5,
                    "knx-living-room_setpoint_living": 22.0,
                    "knx-living-room_light_living": false,
                    "knx-living-room_power_living": 450.0,
                    "knx-living-room_sim_switch": false,
                    "knx-living-room_sim_dimmer": 128.0
                })
            });
        });
    });

    test('KNX Connection details and device listing', async ({ page }) => {
        await page.goto('/plswk/Connections');
        
        // Wait for connection list to render
        const connCard = page.locator('text=KNX Test Gateway');
        await expect(connCard).toBeVisible();
        await connCard.click();

        // Check connection detail header (scoped to detail panel to avoid duplicate match)
        const detailPanel = page.locator('[data-testid="conn-detail"]');
        await expect(detailPanel.locator('text=knx-sim : 3671')).toBeVisible();

        // Check devices list table in details panel
        const deviceName = page.locator('text=Living Room KNX');
        await expect(deviceName).toBeVisible();
        await expect(page.locator('text=15.15.250')).toBeVisible();
    });

    test('KNX Asset Tree auto-discovered XML points and write controls', async ({ page }) => {
        await page.goto('/plswk/Assets');

        // Check parent folder Building A and expand it
        const folderA = page.locator('.tree-row').filter({ hasText: /^Building A$/ });
        await expect(folderA).toBeVisible();
        await folderA.locator('.tree-toggle').click();
        await page.waitForTimeout(300);

        // Check nested folder Living Room and expand it
        const folderRoom = page.locator('.tree-row').filter({ hasText: /^Living Room$/ });
        await expect(folderRoom).toBeVisible();
        await folderRoom.locator('.tree-toggle').click();
        await page.waitForTimeout(300);

        // Check nested device node Living Room KNX and select it
        const deviceNode = page.locator('.tree-row').filter({ hasText: 'Living Room KNX' });
        await expect(deviceNode).toBeVisible();
        await deviceNode.click();
        await page.waitForTimeout(300);

        // Assert all autodetected points are rendered
        await expect(page.getByText('Temperature Sensor', { exact: true })).toBeVisible();
        await expect(page.getByText('Setpoint Temperature', { exact: true })).toBeVisible();
        await expect(page.getByText('Ceiling Light', { exact: true })).toBeVisible();
        await expect(page.getByText('Power Sensor', { exact: true })).toBeVisible();
        await expect(page.getByText('Simulated Switch', { exact: true })).toBeVisible();
        await expect(page.getByText('Simulated Dimmer', { exact: true })).toBeVisible();

        // Click the row to open the details modal
        const simSwitchRow = page.locator('.glass').filter({ hasText: 'Simulated Switch' });
        await simSwitchRow.click();
        await page.waitForSelector('#telemetryDetailsModal', { state: 'visible' });

        // Verify the inline edit start button is visible in the modal
        const editBtn = page.locator('#telInlineEditStartBtn');
        await expect(editBtn).toBeVisible();
    });
});
