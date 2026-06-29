/**
 * E2E tests for the network scan & per-type configuration editors.
 *
 * Covers:
 *   - The "Discover Devices" button opens the scan modal
 *   - The scan modal calls /api/scan-network and renders discovered hosts
 *   - Selecting hosts and clicking "Add" creates connections in the override list
 *   - The connection editor renders only the fields relevant to the selected type
 *   - The device editor renders only the fields relevant to the selected type
 *   - The /api/connection-types and /api/device-types metadata endpoints are consumed
 */
import { test, expect } from './fixtures';

// Mock metadata returned by the backend. Mirrors ConnectionTypeMetadata.cs.
const MOCK_CONNECTION_TYPES = [
    {
        type: 'modbus-tcp', label: 'Modbus TCP', icon: 'fa-bolt', defaultPort: 502,
        fields: [
            { key: 'id', label: 'Connection ID', type: 'text', required: true, placeholder: 'e.g. modbus-gateway-1' },
            { key: 'name', label: 'Display Name', type: 'text', required: false, placeholder: 'Optional friendly name' },
            { key: 'address', label: 'Gateway IP / Host', type: 'text', required: true, placeholder: '192.168.1.50' },
            { key: 'port', label: 'Port', type: 'number', required: true, default: '502' },
        ]
    },
    {
        type: 'bacnet-ip', label: 'BACnet/IP', icon: 'fa-network-wired', defaultPort: 47808,
        fields: [
            { key: 'id', label: 'Connection ID', type: 'text', required: true },
            { key: 'name', label: 'Display Name', type: 'text', required: false },
            { key: 'localAddress', label: 'Local Bind Address', type: 'text', required: true, placeholder: '0.0.0.0' },
            { key: 'localPort', label: 'Local Bind Port', type: 'number', required: true, default: '47808' },
            { key: 'localDeviceId', label: 'Local Device ID', type: 'number', required: false, default: '1234' },
        ]
    },
    {
        type: 'knx-ip', label: 'KNXnet/IP', icon: 'fa-house-signal', defaultPort: 3671,
        fields: [
            { key: 'id', label: 'Connection ID', type: 'text', required: true },
            { key: 'name', label: 'Display Name', type: 'text', required: false },
            { key: 'address', label: 'Gateway IP / Host', type: 'text', required: true },
            { key: 'port', label: 'Port', type: 'number', required: false, default: '3671' },
            { key: 'knxSecureEnabled', label: 'Enable KNX IP Secure', type: 'checkbox', required: false },
            { key: 'knxCommissioningPassword', label: 'Commissioning Password (FDSK)', type: 'password', required: false },
        ]
    },
];

const MOCK_DEVICE_TYPES = [
    {
        type: 'virtual', label: 'Virtual / Analytics', icon: 'fa-calculator',
        compatibleConnections: [],
        fields: [
            { key: 'id', label: 'Device ID', type: 'text', required: true },
            { key: 'name', label: 'Display Name', type: 'text', required: true },
            { key: 'path', label: 'Path', type: 'text', required: false, placeholder: 'Building/Floor' },
        ]
    },
    {
        type: 'janitza', label: 'Janitza (Modbus)', icon: 'fa-bolt',
        compatibleConnections: ['modbus-tcp'],
        fields: [
            { key: 'id', label: 'Device ID', type: 'text', required: true },
            { key: 'name', label: 'Display Name', type: 'text', required: true },
            { key: 'connectionId', label: 'Connection', type: 'select', required: true },
            { key: 'deviceId', label: 'Modbus Slave ID', type: 'number', required: true, default: '1' },
            { key: 'path', label: 'Path', type: 'text', required: false },
            { key: 'assetType', label: 'Asset Type', type: 'text', required: false },
            { key: 'pollIntervalSeconds', label: 'Poll Interval (s)', type: 'number', required: false },
        ]
    },
    {
        type: 'bacnet', label: 'BACnet (Generic)', icon: 'fa-network-wired',
        compatibleConnections: ['bacnet-ip'],
        fields: [
            { key: 'id', label: 'Device ID', type: 'text', required: true },
            { key: 'name', label: 'Display Name', type: 'text', required: true },
            { key: 'connectionId', label: 'Connection', type: 'select', required: true },
            { key: 'deviceId', label: 'BACnet Device Instance', type: 'number', required: true, default: '100' },
            { key: 'address', label: 'Device IP (optional)', type: 'text', required: false },
            { key: 'path', label: 'Path', type: 'text', required: false },
            { key: 'assetType', label: 'Asset Type', type: 'text', required: false },
            { key: 'pollIntervalSeconds', label: 'Poll Interval (s)', type: 'number', required: false, default: '3600' },
            { key: 'writeback', label: 'Enable Write-back', type: 'checkbox', required: false },
        ]
    },
];

// A scan result with two hosts, each responding on a different protocol.
const MOCK_SCAN_RESULT = {
    range: '192.168.1.0/24',
    hostsScanned: 254,
    hostsFound: 2,
    durationMs: 1234,
    hosts: [
        {
            ip: '192.168.1.50',
            protocols: [{ type: 'modbus-tcp', label: 'Modbus TCP', port: 502 }]
        },
        {
            ip: '192.168.1.60',
            protocols: [{ type: 'bacnet-ip', label: 'BACnet/IP', port: 47808 }]
        },
    ]
};

test.describe('Network Scan & Per-Type Config Editors', () => {

    test.beforeEach(async ({ page }) => {
        // Mock the connections list (the Connections page shell fetches this)
        await page.route('**/api/connections', async route => {
            await route.fulfill({
                status: 200, contentType: 'application/json',
                body: JSON.stringify({ connections: [], canEditConfig: true })
            });
        });

        // Mock the config endpoint (base + override)
        await page.route('**/api/config', async route => {
            await route.fulfill({
                status: 200, contentType: 'application/json',
                body: JSON.stringify({
                    base: { connections: [], devices: [] },
                    override: { connections: [], devices: [] },
                    version: 'test'
                })
            });
        });

        // Mock telemetry keys (config page fetches these on load)
        await page.route('**/api/telemetry-keys', async route => {
            await route.fulfill({
                status: 200, contentType: 'application/json',
                body: JSON.stringify([])
            });
        });

        // Mock connection & device type metadata
        await page.route('**/api/connection-types', async route => {
            await route.fulfill({
                status: 200, contentType: 'application/json',
                body: JSON.stringify(MOCK_CONNECTION_TYPES)
            });
        });
        await page.route('**/api/device-types', async route => {
            await route.fulfill({
                status: 200, contentType: 'application/json',
                body: JSON.stringify(MOCK_DEVICE_TYPES)
            });
        });

        // Mock connection health (Connections page chart)
        await page.route('**/api/connection-health/**', async route => {
            await route.fulfill({
                status: 200, contentType: 'application/json',
                body: JSON.stringify([])
            });
        });
    });

    // ── Scan modal ────────────────────────────────────────────────────────

    test('Discover Devices button opens scan modal', async ({ page }) => {
        await page.goto('/plswk/Connections?conn=config');

        // Wait for the config page to render
        await expect(page.getByText('System Configuration')).toBeVisible({ timeout: 15000 });

        // Click the Discover Devices button
        const discoverBtn = page.getByRole('button', { name: /Discover Devices/i });
        await expect(discoverBtn).toBeVisible();
        await discoverBtn.click();

        // Modal should be visible with the range input
        await expect(page.getByText('Discover Devices on Network')).toBeVisible();
        await expect(page.locator('input[placeholder*="192.168.1.0/24"]')).toBeVisible();
    });

    test('Scan modal calls API and renders discovered hosts', async ({ page }) => {
        // Intercept the scan request and return the mock result
        let scanRequested = false;
        await page.route('**/api/scan-network', async route => {
            scanRequested = true;
            await route.fulfill({
                status: 200, contentType: 'application/json',
                body: JSON.stringify(MOCK_SCAN_RESULT)
            });
        });

        await page.goto('/plswk/Connections?conn=config');
        await expect(page.getByText('System Configuration')).toBeVisible({ timeout: 15000 });

        await page.getByRole('button', { name: /Discover Devices/i }).click();
        await expect(page.getByText('Discover Devices on Network')).toBeVisible();

        // Click Scan
        await page.getByRole('button', { name: /^Scan$/ }).click();

        // Wait for results
        await expect(page.getByText('192.168.1.50')).toBeVisible({ timeout: 10000 });
        await expect(page.getByText('192.168.1.60')).toBeVisible();
        await expect(page.getByText('Modbus TCP :502')).toBeVisible();
        await expect(page.getByText('BACnet/IP :47808')).toBeVisible();
        await expect(page.getByText(/2 hosts found/)).toBeVisible();

        expect(scanRequested).toBe(true);
    });

    test('Scan modal shows empty state when no hosts found', async ({ page }) => {
        await page.route('**/api/scan-network', async route => {
            await route.fulfill({
                status: 200, contentType: 'application/json',
                body: JSON.stringify({
                    range: '10.0.0.0/24', hostsScanned: 254, hostsFound: 0,
                    hosts: [], durationMs: 500
                })
            });
        });

        await page.goto('/plswk/Connections?conn=config');
        await expect(page.getByText('System Configuration')).toBeVisible({ timeout: 15000 });

        await page.getByRole('button', { name: /Discover Devices/i }).click();
        await page.getByRole('button', { name: /^Scan$/ }).click();

        await expect(page.getByText(/No devices responded/)).toBeVisible({ timeout: 10000 });
    });

    test('Scan modal shows error on API failure', async ({ page }) => {
        await page.route('**/api/scan-network', async route => {
            await route.fulfill({
                status: 400, contentType: 'text/plain',
                body: 'Invalid CIDR notation'
            });
        });

        await page.goto('/plswk/Connections?conn=config');
        await expect(page.getByText('System Configuration')).toBeVisible({ timeout: 15000 });

        await page.getByRole('button', { name: /Discover Devices/i }).click();
        await page.getByRole('button', { name: /^Scan$/ }).click();

        await expect(page.getByText(/Invalid CIDR|Scan failed/i)).toBeVisible({ timeout: 10000 });
    });

    test('Adding scanned hosts creates connections in the override list', async ({ page }) => {
        await page.route('**/api/scan-network', async route => {
            await route.fulfill({
                status: 200, contentType: 'application/json',
                body: JSON.stringify(MOCK_SCAN_RESULT)
            });
        });

        await page.goto('/plswk/Connections?conn=config');
        await expect(page.getByText('System Configuration')).toBeVisible({ timeout: 15000 });

        // Open scan modal and run scan
        await page.getByRole('button', { name: /Discover Devices/i }).click();
        await page.getByRole('button', { name: /^Scan$/ }).click();
        await expect(page.getByText('192.168.1.50')).toBeVisible({ timeout: 10000 });

        // Both hosts should be pre-selected. Click Add.
        const addBtn = page.getByRole('button', { name: /Add \(2\)/ });
        await expect(addBtn).toBeVisible();
        await addBtn.click();

        // The modal should close and two new connections should appear in the
        // "Configured (Editable)" section.
        await expect(page.getByText('Discover Devices on Network')).not.toBeVisible();
        await expect(page.getByText('scan-modbus-tcp-192-168-1-50')).toBeVisible();
        await expect(page.getByText('scan-bacnet-ip-192-168-1-60')).toBeVisible();
    });

    test('Toggling a host deselects it and updates the Add button count', async ({ page }) => {
        await page.route('**/api/scan-network', async route => {
            await route.fulfill({
                status: 200, contentType: 'application/json',
                body: JSON.stringify(MOCK_SCAN_RESULT)
            });
        });

        await page.goto('/plswk/Connections?conn=config');
        await expect(page.getByText('System Configuration')).toBeVisible({ timeout: 15000 });

        await page.getByRole('button', { name: /Discover Devices/i }).click();
        await page.getByRole('button', { name: /^Scan$/ }).click();
        await expect(page.getByText('192.168.1.50')).toBeVisible({ timeout: 10000 });

        // Initially 2 selected
        await expect(page.getByRole('button', { name: /Add \(2\)/ })).toBeVisible();

        // Deselect the first host by clicking its checkbox label
        const hostRow = page.locator('label', { hasText: '192.168.1.50' });
        await hostRow.locator('input[type="checkbox"]').uncheck();

        // Now only 1 selected
        await expect(page.getByRole('button', { name: /Add \(1\)/ })).toBeVisible();
    });

    // ── Per-type connection editor ─────────────────────────────────────────

    test('Connection editor renders Modbus TCP fields', async ({ page }) => {
        await page.goto('/plswk/Connections?conn=config');
        await expect(page.getByText('System Configuration')).toBeVisible({ timeout: 15000 });

        // Click the "Add" button in the Connections panel (first one)
        await page.getByRole('button', { name: /Add/i }).first().click();

        // The modal should be open
        await expect(page.getByText('New Connection')).toBeVisible();

        // Modbus TCP should be the default type
        await expect(page.locator('select').first()).toHaveValue('modbus-tcp');

        // Modbus-specific fields should be visible
        await expect(page.getByText('Gateway IP / Host')).toBeVisible();
        await expect(page.locator('label').filter({ hasText: 'Port' })).toBeVisible();

        // BACnet-specific fields should NOT be visible
        await expect(page.getByText('Local Bind Address')).not.toBeVisible();
        await expect(page.getByText('Local Device ID')).not.toBeVisible();
    });

    test('Connection editor switches fields when type changes to BACnet/IP', async ({ page }) => {
        await page.goto('/plswk/Connections?conn=config');
        await expect(page.getByText('System Configuration')).toBeVisible({ timeout: 15000 });

        await page.getByRole('button', { name: /Add/i }).first().click();
        await expect(page.getByText('New Connection')).toBeVisible();

        // Switch to BACnet/IP
        await page.locator('select').first().selectOption('bacnet-ip');

        // BACnet-specific fields should now be visible
        await expect(page.getByText('Local Bind Address')).toBeVisible();
        await expect(page.getByText('Local Bind Port')).toBeVisible();
        await expect(page.getByText('Local Device ID')).toBeVisible();

        // Modbus-specific "Gateway IP / Host" should NOT be visible
        await expect(page.getByText('Gateway IP / Host')).not.toBeVisible();
    });

    test('Connection editor renders KNX secure fields for knx-ip type', async ({ page }) => {
        await page.goto('/plswk/Connections?conn=config');
        await expect(page.getByText('System Configuration')).toBeVisible({ timeout: 15000 });

        await page.getByRole('button', { name: /Add/i }).first().click();
        await expect(page.getByText('New Connection')).toBeVisible();

        await page.locator('select').first().selectOption('knx-ip');

        await expect(page.getByText('Enable KNX IP Secure')).toBeVisible();
        await expect(page.getByText('Commissioning Password (FDSK)')).toBeVisible();
    });

    // ── Per-type device editor ─────────────────────────────────────────────

    test('Device editor renders Janitza fields with connection select', async ({ page }) => {
        // Provide a modbus connection so the device editor's connection select has an option
        await page.route('**/api/config', async route => {
            await route.fulfill({
                status: 200, contentType: 'application/json',
                body: JSON.stringify({
                    base: {
                        connections: [{ id: 'modbus-1', type: 'modbus-tcp', address: '10.0.0.1', port: 502 }],
                        devices: []
                    },
                    override: { connections: [], devices: [] },
                    version: 'test'
                })
            });
        });

        await page.goto('/plswk/Connections?conn=config');
        await expect(page.getByText('System Configuration')).toBeVisible({ timeout: 15000 });

        // Click the "Add Device" button in the Devices panel
        await page.getByRole('button', { name: /Add Device/i }).click();

        await expect(page.getByText('New Device')).toBeVisible();

        // Select Janitza device type
        await page.locator('select').first().selectOption('janitza');

        // Janitza-specific fields should be visible
        await expect(page.getByText('Modbus Slave ID')).toBeVisible();
        await expect(page.getByText('Asset Type')).toBeVisible();
        await expect(page.getByText('Poll Interval (s)')).toBeVisible();

        // The connection select should contain the modbus-1 connection
        const connSelect = page.locator('select').nth(1);
        const options = connSelect.locator('option');
        await expect(options.filter({ hasText: 'modbus-1' })).toHaveCount(1);
    });

    test('Device editor renders BACnet fields when type is bacnet', async ({ page }) => {
        await page.route('**/api/config', async route => {
            await route.fulfill({
                status: 200, contentType: 'application/json',
                body: JSON.stringify({
                    base: {
                        connections: [{ id: 'bacnet-1', type: 'bacnet-ip', localAddress: '0.0.0.0', localPort: 47808 }],
                        devices: []
                    },
                    override: { connections: [], devices: [] },
                    version: 'test'
                })
            });
        });

        await page.goto('/plswk/Connections?conn=config');
        await expect(page.getByText('System Configuration')).toBeVisible({ timeout: 15000 });

        await page.getByRole('button', { name: /Add Device/i }).click();
        await expect(page.getByText('New Device')).toBeVisible();

        await page.locator('select').first().selectOption('bacnet');

        await expect(page.getByText('BACnet Device Instance')).toBeVisible();
        await expect(page.getByText('Device IP (optional)')).toBeVisible();
        await expect(page.getByText('Enable Write-back')).toBeVisible();

        // Modbus Slave ID should NOT be visible
        await expect(page.getByText('Modbus Slave ID')).not.toBeVisible();
    });

    test('Device editor filters connection options by compatible type', async ({ page }) => {
        // Provide both a modbus and a bacnet connection
        await page.route('**/api/config', async route => {
            await route.fulfill({
                status: 200, contentType: 'application/json',
                body: JSON.stringify({
                    base: {
                        connections: [
                            { id: 'modbus-1', type: 'modbus-tcp', address: '10.0.0.1', port: 502 },
                            { id: 'bacnet-1', type: 'bacnet-ip', localAddress: '0.0.0.0', localPort: 47808 }
                        ],
                        devices: []
                    },
                    override: { connections: [], devices: [] },
                    version: 'test'
                })
            });
        });

        await page.goto('/plswk/Connections?conn=config');
        await expect(page.getByText('System Configuration')).toBeVisible({ timeout: 15000 });

        await page.getByRole('button', { name: /Add Device/i }).click();
        await expect(page.getByText('New Device')).toBeVisible();

        // Select Janitza (modbus-only) — connection select should only show modbus-1
        await page.locator('select').first().selectOption('janitza');
        const connSelect = page.locator('select').nth(1);
        await expect(connSelect.locator('option')).toContainText(['modbus-1']);
        await expect(connSelect.locator('option')).not.toContainText(['bacnet-1']);

        // Switch to BACnet — connection select should only show bacnet-1
        await page.locator('select').first().selectOption('bacnet');
        const connSelectBacnet = page.locator('select').nth(1);
        await expect(connSelectBacnet.locator('option')).toContainText(['bacnet-1']);
        await expect(connSelectBacnet.locator('option')).not.toContainText(['modbus-1']);
    });

    // ── Metadata endpoints ────────────────────────────────────────────────

    test('connection-types endpoint returns metadata', async ({ page }) => {
        let receivedTypes: any[] = [];
        await page.route('**/api/connection-types', async route => {
            receivedTypes = MOCK_CONNECTION_TYPES;
            await route.fulfill({
                status: 200, contentType: 'application/json',
                body: JSON.stringify(MOCK_CONNECTION_TYPES)
            });
        });

        await page.goto('/plswk/Connections?conn=config');
        await expect(page.getByText('System Configuration')).toBeVisible({ timeout: 15000 });

        // Wait a moment for the fetch to complete
        await page.waitForTimeout(500);
        expect(receivedTypes.length).toBeGreaterThan(0);
    });
});
