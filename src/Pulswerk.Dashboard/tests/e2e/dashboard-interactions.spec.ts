import { test, expect } from '@playwright/test';

declare const process: any;

// Auth proxy URL for admin-authenticated requests.
// In Docker: set ADMIN_PROXY_URL=http://auth-proxy:81 (port 81 = admins group).
// Locally:   defaults to http://localhost:5002.
const ADMIN_PROXY_URL = process.env.ADMIN_PROXY_URL || 'http://localhost:5002';

test.describe('Dashboard Interactions and Component States', () => {

  // 1. Timewindow Selector Mode Switches (Realtime vs History)
  test('Timewindow Dropdown Modes', async ({ page }) => {
    // Navigate to dashboards list
    await page.goto('/plswk/Dashboards');
    
    // Check if we need to click a dashboard card first to load the dashboard shell
    const cards = page.locator('.dash-card');
    if (await cards.count() > 0) {
      await cards.first().click();
      await page.waitForTimeout(500);
    }

    const selector = page.locator('[data-testid="tw-selector"]');
    if (await selector.count() === 0 || !(await selector.isVisible())) {
      console.warn('Skipping timewindow selector check: selector not visible (no dashboard loaded)');
      return;
    }

    // Open dropdown
    await selector.click();
    await page.waitForSelector('#twDropdown', { state: 'visible' });

    // A. Realtime Mode asserts
    const realtimeTab = page.locator('button[data-tw-mode="realtime"]').first();
    await expect(realtimeTab).toHaveClass(/active/);

    const presetsPanel = page.locator('#twRealtimePanel');
    await expect(presetsPanel).toBeVisible();

    const presets = page.locator('#twPresets button');
    expect(await presets.count()).toBeGreaterThanOrEqual(6);

    const historyPanel = page.locator('#twHistoryPanel');
    await expect(historyPanel).toBeHidden();

    // B. Switch to History Mode
    const historyTab = page.locator('button[data-tw-mode="history"]').first();
    await historyTab.click();
    await page.waitForTimeout(300);

    // Assert panels swapped visibility
    await expect(historyPanel).toBeVisible();
    await expect(presetsPanel).toBeHidden();
    await expect(historyTab).toHaveClass(/active/);

    // Verify date/time inputs are visible
    const fromInput = page.locator('#twHistFrom');
    const toInput = page.locator('#twHistTo');
    await expect(fromInput).toBeVisible();
    await expect(toInput).toBeVisible();

    // Assert inputs have default values populated (not empty)
    const fromVal = await fromInput.inputValue();
    const toVal = await toInput.inputValue();
    expect(fromVal.trim()).not.toBe('');
    expect(toVal.trim()).not.toBe('');
  });

  // 2. Edit Modal Dynamic Form Modes (Numeric, Enum, Boolean)
  test('Edit Modal Input Variants', async ({ page }) => {
    await page.goto(`${ADMIN_PROXY_URL}/plswk/Assets`);
    await page.waitForSelector('[data-testid="page-title"]', { state: 'visible' });

    // A. Test Numeric Stepper Mode
    await page.evaluate(() => {
      const win = window as any;
      win.allKeys = win.allKeys || [];
      const existing = win.allKeys.find((k: any) => k.key === 'test:key');
      if (!existing) {
        win.allKeys.push({
          key: 'test:key',
          name: 'Test Key',
          fullName: 'test:key',
          units: '°C',
          type: 'ANALOG_VALUE',
          isWritable: true,
          parentPath: []
        });
      }
      if (typeof win.openEdit === 'function') {
        win.openEdit('test:key');
      }
    });
    await page.waitForTimeout(500);

    const modal = page.locator('#telemetryDetailsModal');
    await expect(modal).toBeVisible();

    // Click Edit Value button to start edit mode
    await page.locator('#telInlineEditStartBtn').click();
    await page.waitForTimeout(100);

    // Verify stepper is shown, others are hidden
    await expect(page.locator('#inlineStepperGroup')).toBeVisible();
    await expect(page.locator('#inlineEnumGroup')).toBeHidden();
    await expect(page.locator('#inlineBoolGroup')).toBeHidden();

    const plusBtn = page.locator('#inlineStepperGroup button[onclick="step(1)"]');
    const minusBtn = page.locator('#inlineStepperGroup button[onclick="step(-1)"]');
    await expect(plusBtn).toBeVisible();
    await expect(minusBtn).toBeVisible();

    // Close modal
    await page.locator('#telemetryDetailsModal .close-modal').first().click();
    await page.waitForSelector('#telemetryDetailsModal', { state: 'hidden' });

    // B. Test Multi-State Enum Dropdown Mode
    // We register the enum metadata directly onto allKeys to simulate an Enum key
    await page.evaluate(() => {
      const win = window as any;
      win.allKeys = win.allKeys || [];
      const existing = win.allKeys.find((k: any) => k.key === 'test:enum_key');
      if (!existing) {
        win.allKeys.push({
          key: 'test:enum_key',
          name: 'Fan Speed',
          fullName: 'test:enum_key',
          units: '',
          type: 'MULTI_STATE_VALUE',
          isWritable: true,
          enumValues: { '0': 'Off', '1': 'Low', '2': 'High' },
          parentPath: []
        });
      }
      if (typeof win.openEdit === 'function') {
        win.openEdit('test:enum_key');
      }
    });
    await page.waitForTimeout(500);
    await expect(modal).toBeVisible();

    // Click Edit Value button to start edit mode
    await page.locator('#telInlineEditStartBtn').click();
    await page.waitForTimeout(100);

    // Verify dropdown select is shown, stepper and toggle are hidden
    await expect(page.locator('#inlineEnumGroup')).toBeVisible();
    await expect(page.locator('#inlineStepperGroup')).toBeHidden();
    await expect(page.locator('#inlineBoolGroup')).toBeHidden();

    const options = page.locator('#enumSelect option');
    expect(await options.count()).toBe(3);

    // Close modal
    await page.locator('#telemetryDetailsModal .close-modal').first().click();
    await page.waitForSelector('#telemetryDetailsModal', { state: 'hidden' });

    // C. Test Binary Output Boolean Toggle Mode
    // We register the binary metadata directly onto allKeys to simulate a Boolean key
    await page.evaluate(() => {
      const win = window as any;
      win.allKeys = win.allKeys || [];
      const existing = win.allKeys.find((k: any) => k.key === 'test:bool_key');
      if (!existing) {
        win.allKeys.push({
          key: 'test:bool_key',
          name: 'Solenoid Valve',
          fullName: 'test:bool_key',
          units: '',
          type: 'BINARY_OUTPUT',
          isWritable: true,
          enumValues: null,
          parentPath: []
        });
      }
      if (typeof win.openEdit === 'function') {
        win.openEdit('test:bool_key');
      }
    });
    await page.waitForTimeout(500);
    await expect(modal).toBeVisible();

    // Click Edit Value button to start edit mode
    await page.locator('#telInlineEditStartBtn').click();
    await page.waitForTimeout(100);

    // Verify toggle wrap is visible, stepper and dropdown are hidden
    await expect(page.locator('#inlineBoolGroup')).toBeVisible();
    await expect(page.locator('#inlineStepperGroup')).toBeHidden();
    await expect(page.locator('#inlineEnumGroup')).toBeHidden();

    const toggleLabel = page.locator('#boolLabel');
    expect(await toggleLabel.textContent()).not.toBe('');
  });

  // 3. Asset Tree Collapsing and Expansion
  test('Asset Tree Expansion', async ({ page }) => {
    await page.goto('/plswk/Assets');
    await page.waitForSelector('[data-testid="page-title"]', { state: 'visible' });
    await page.waitForTimeout(500); // Wait for tree load

    const treeNodes = page.locator('#assetTree .tree-row');
    if (await treeNodes.count() === 0) {
      console.warn('Skipping asset tree expansion check: no nodes returned');
      return;
    }

    // Capture count before expansion
    const beforeCount = await treeNodes.count();

    // Click the first expandable chevron
    const chevrons = page.locator('#assetTree .tree-chevron, #assetTree .tree-toggle');
    if (await chevrons.count() > 0) {
      await chevrons.first().click();
      await page.waitForTimeout(300);

      // Node count should change or display new child nodes
      const afterCount = await treeNodes.count();
      console.log(`Asset tree count before: ${beforeCount}, after chevron click: ${afterCount}`);
    }
  });

  // 4. Alarm List Filter Chips
  test('Alarm Filter Chip Selections', async ({ page }) => {
    await page.goto('/plswk/Alarms');
    await page.waitForSelector('[data-testid="page-title"]', { state: 'visible' });

    const filterBar = page.locator('[data-testid="alarm-filters"]');
    if (await filterBar.count() === 0 || !(await filterBar.isVisible())) {
      console.warn('Skipping alarm filter check: filter bar not visible');
      return;
    }

    const chips = filterBar.locator('a, button, .filter-chip');
    const chipCount = await chips.count();
    expect(chipCount).toBeGreaterThanOrEqual(4);

    // Verify that exactly 1 filter is active by default
    let activeCount = 0;
    for (let i = 0; i < chipCount; i++) {
      const className = await chips.nth(i).getAttribute('class') || '';
      if (className.includes('active') || className.includes('selected')) {
        activeCount++;
      }
    }
    expect(activeCount).toBe(1);

    // Click on the second chip and check active state migration
    const secondChip = chips.nth(1);
    await secondChip.click();
    await page.waitForTimeout(300);

    const updatedClass = await secondChip.getAttribute('class') || '';
    expect(updatedClass.includes('active') || updatedClass.includes('selected')).toBe(true);
  });

  // 5. Dashboard List and Create Flow
  test('Dashboard List and Create Flow', async ({ page }) => {
    // Navigate to dashboards list via admin auth proxy to get write permissions
    await page.goto(`${ADMIN_PROXY_URL}/plswk/Dashboards`);
    await page.waitForSelector('[data-testid="dash-list-mode"]', { state: 'visible' });

    // Verify that either the cards or empty state is visible
    const cards = page.locator('.dash-card');
    const emptyState = page.locator('#emptyDashboards');
    
    // Wait for either the grid to have cards, or the empty state to be visible
    await Promise.race([
      page.waitForSelector('.dash-card', { timeout: 10000 }).catch(() => {}),
      page.waitForSelector('#emptyDashboards', { state: 'visible', timeout: 10000 }).catch(() => {})
    ]);

    const isGridEmpty = await emptyState.isVisible();
    if (isGridEmpty) {
      // Click the "Create your first dashboard" button inside the empty state
      const createFirstBtn = emptyState.locator('button');
      await expect(createFirstBtn).toBeVisible();
      await createFirstBtn.click();
    } else {
      // Verify that the dashboards list has populated cards (cards exist)
      await expect(cards.first()).toBeVisible();
      
      // Click the "New Dashboard" button on the top right
      const createBtn = page.locator('[data-testid="dash-create-btn"]');
      await expect(createBtn).toBeVisible();
      await createBtn.click();
    }

    // Verify that the "New Dashboard" modal is shown and input is focused
    const modal = page.locator('[data-testid="create-dash-modal"]');
    await expect(modal).toBeVisible();
    
    const newDashName = page.locator('#newDashName');
    await expect(newDashName).toBeFocused();

    // Fill out the form
    const uniqueName = `E2E Test ${Date.now()}`;
    await newDashName.fill(uniqueName);

    const newDashDesc = page.locator('#newDashDesc');
    await newDashDesc.fill('Created via automated test');

    // Click create
    const submitBtn = modal.locator('button:has-text("Create")');
    await submitBtn.click();

    // Verify redirection to the new dashboard in edit mode
    await page.waitForURL(/\/plswk\/Dashboards\/[^/]+/);
    await page.waitForSelector('[data-testid="dash-edit-mode"]', { state: 'visible' });
    
    // Verify the page title matches
    const titleView = page.locator('#dashTitleView');
    await expect(titleView).toHaveText(uniqueName);
    
    // Verify we are in edit mode
    const addWidgetBtn = page.locator('[data-testid="dash-add-widget-btn"]');
    await expect(addWidgetBtn).toBeVisible();

    // Verify description is populated in edit mode
    const descInput = page.locator('#dashDesc');
    await expect(descInput).toBeVisible();
    await expect(descInput).toHaveValue('Created via automated test');

    // Update and save the description
    await descInput.fill('Updated description via E2E test');
    await page.locator('#btnSave').click();
    await page.waitForURL(/\/plswk\/Dashboards\/[^/]+/);
    await page.waitForSelector('[data-testid="dash-edit-mode"]', { state: 'visible' });

    // Verify description updated in view mode
    const descView = page.locator('#dashDescView');
    await expect(descView).toBeVisible();
    await expect(descView).toHaveText('Updated description via E2E test');

    // Edit again and verify cancel reverts change
    await page.locator('#btnEdit').click();
    await expect(descInput).toBeVisible();
    await expect(descInput).toHaveValue('Updated description via E2E test');

    await descInput.fill('Canceled description change');
    await page.locator('#btnCancel').click();
    
    await page.waitForURL(/\/plswk\/Dashboards\/[^/]+/);
    await page.waitForSelector('[data-testid="dash-edit-mode"]', { state: 'visible' });
    await expect(descView).toHaveText('Updated description via E2E test');
  });

  // 6. Verify telemetry fetching optimization (no full telemetries fetch on load/view)
  test('Telemetry Metadata Lazy-Loading Optimization', async ({ page }) => {
    // Collect all requests to the telemetries endpoint
    const telemetriesRequests: { url: string; method: string; postData: string | null }[] = [];
    await page.on('request', request => {
      const url = request.url();
      if (url.includes('/api/telemetries')) {
        telemetriesRequests.push({
          url,
          method: request.method(),
          postData: request.postData()
        });
      }
    });

    // Navigate to dashboards list via admin proxy (port 5002 has auth headers)
    await page.goto(`${ADMIN_PROXY_URL}/plswk/Dashboards`);
    await page.waitForSelector('[data-testid="dash-list-mode"]', { state: 'visible' });

    // Assert that no request to the telemetries endpoint was made
    // (specifically, no full fetch like `/plswk/api/telemetries` or `/plswk/api/telemetries?includeLiveValues=true`)
    const fullFetchRequests = telemetriesRequests.filter(req => {
      if (req.method === 'GET') {
        const parsed = new URL(req.url);
        return !parsed.searchParams.get('keys');
      }
      if (req.method === 'POST') {
        if (!req.postData) return true;
        try {
          const body = JSON.parse(req.postData);
          return !body.keys || body.keys.length === 0;
        } catch {
          return true;
        }
      }
      return false;
    });
    expect(fullFetchRequests.length).toBe(0);

    // If there's a dashboard card, click it to load the dashboard shell
    const cards = page.locator('.dash-card');
    if (await cards.count() > 0) {
      await cards.first().click();
      await page.waitForURL(/\/plswk\/Dashboards\/[^/]+/);
      await page.waitForSelector('[data-testid="dash-edit-mode"]', { state: 'visible' });

      // After loading a dashboard, it should only query metadata for the keys actually used,
      // not a full fetch.
      const fullFetchAfterLoad = telemetriesRequests.filter(req => {
        if (req.method === 'GET') {
          const parsed = new URL(req.url);
          return !parsed.searchParams.get('keys');
        }
        if (req.method === 'POST') {
          if (!req.postData) return true;
          try {
            const body = JSON.parse(req.postData);
            return !body.keys || body.keys.length === 0;
          } catch {
            return true;
          }
        }
        return false;
      });
      expect(fullFetchAfterLoad.length).toBe(0);
    }
  });

  // 7. E2E test for drag, resize, and position persistence of dashboard widgets
  test('Dashboard Widget Drag, Resize, and Position Persistence', async ({ page }) => {
    // Navigate to dashboards list
    await page.goto(`${ADMIN_PROXY_URL}/plswk/Dashboards`);
    await page.waitForSelector('[data-testid="dash-list-mode"]', { state: 'visible' });

    // Open create dashboard modal
    const createBtn = page.locator('[data-testid="dash-create-btn"]');
    if (await createBtn.count() > 0) {
      await createBtn.click();
    } else {
      await page.locator('#emptyDashboards button').click();
    }

    const modal = page.locator('[data-testid="create-dash-modal"]');
    await expect(modal).toBeVisible();

    const uniqueName = `Drag Test ${Date.now()}`;
    await page.locator('#newDashName').fill(uniqueName);
    await page.locator('#newDashDesc').fill('E2E Drag and Resize Test');
    await modal.locator('button:has-text("Create")').click();

    // Verify redirection to the new dashboard in edit mode
    await page.waitForURL(/\/plswk\/Dashboards\/[^/]+/);
    await page.waitForSelector('[data-testid="dash-edit-mode"]', { state: 'visible' });

    // Click "Add your first widget" or "Add Widget" on the top right
    const addFirstWidgetBtn = page.locator('button:has-text("Add your first widget")');
    if (await addFirstWidgetBtn.count() > 0) {
      await addFirstWidgetBtn.click();
    } else {
      await page.locator('[data-testid="dash-add-widget-btn"]').click();
    }

    // Wait for the Add Widget modal
    const addWidgetModal = page.locator('#addWidgetModal');
    await expect(addWidgetModal).toBeVisible();

    // Fill title
    await page.locator('#widgetTitle').fill('Test Timeseries Widget');

    // Open the key selector first
    await page.locator('#btnKeyPickerOpen').click();

    // Select the first available telemetry key (e.g. check the first checkbox in key list)
    // Wait for key picker to load
    await page.waitForSelector('#keyList input[name="wkey"]');
    const firstCheckbox = page.locator('#keyList input[name="wkey"]').first();
    await firstCheckbox.check();

    // Click "OK" to close the key picker
    await page.locator('#keyPickerWrapper button:has-text("OK")').click();

    // Click "Add Widget" confirm button
    await page.locator('#btnAddWidgetConfirm').click();
    await expect(addWidgetModal).toBeHidden();

    // Wait for widget to be added to grid
    const widgetItem = page.locator('.grid-stack-item').first();
    await expect(widgetItem).toBeVisible();

    // Verify default coordinates (usually x=0, y=0, w=6, h=4)
    await expect(widgetItem).toHaveAttribute('gs-x', '0');
    await expect(widgetItem).toHaveAttribute('gs-y', '0');
    await expect(widgetItem).toHaveAttribute('gs-w', '6');
    await expect(widgetItem).toHaveAttribute('gs-h', '4');

    // Wait for resize handle to be attached
    const resizeHandle = widgetItem.locator('.ui-resizable-se');
    await expect(resizeHandle).toBeVisible();

    await page.screenshot({ path: 'test-results/debug-edit-mode.png' });

    // Perform Resize: drag resize handle down-right
    const handleBox = await resizeHandle.boundingBox();
    console.log('Handle box:', handleBox);
    expect(handleBox).not.toBeNull();
    const resizeGridContainer = page.locator('#dashGrid2');
    const resizeGridBox = await resizeGridContainer.boundingBox();
    expect(resizeGridBox).not.toBeNull();
    if (handleBox && resizeGridBox) {
      // Hover over the handle to ensure the mouse is exactly on it
      await resizeHandle.hover();
      await page.waitForTimeout(100);
      await page.mouse.down();
      await page.waitForTimeout(100);
      // Move 10px first to trigger the drag start threshold
      await page.mouse.move(handleBox.x + handleBox.width / 2 + 10, handleBox.y + handleBox.height / 2 + 10, { steps: 5 });
      await page.waitForTimeout(100);
      // Move to absolute target coordinates for w=12, h=6
      const targetX = resizeGridBox.x + resizeGridBox.width - 20;
      const targetY = resizeGridBox.y + 480; // mathematically safe midpoint for 6 rows
      await page.mouse.move(targetX, targetY, { steps: 15 });
      await page.waitForTimeout(100);
      await page.mouse.up();
    }

    // Wait for layout to update
    await page.waitForTimeout(500);

    // Verify resize succeeded
    const resizedW = await widgetItem.getAttribute('gs-w');
    const resizedH = await widgetItem.getAttribute('gs-h');
    console.log(`After resize: w=${resizedW}, h=${resizedH}`);
    expect(resizedW).toBe('12');
    expect(resizedH).toBe('6');

    // Perform Drag: drag the widget by its header to cell
    const header = widgetItem.locator('.widget-header');
    const headerBox = await header.boundingBox();
    expect(headerBox).not.toBeNull();
    if (headerBox) {
      await page.mouse.move(headerBox.x + headerBox.width / 2, headerBox.y + headerBox.height / 2);
      await page.mouse.down();
      // Drag down-right
      await page.mouse.move(headerBox.x + headerBox.width / 2 + 300, headerBox.y + headerBox.height / 2 + 300, { steps: 10 });
      await page.mouse.up();
    }

    // Wait for layout to settle
    await page.waitForTimeout(500);

    // Read updated coordinates from DOM attributes
    const updatedX = await widgetItem.getAttribute('gs-x');
    const updatedY = await widgetItem.getAttribute('gs-y');
    const updatedW = await widgetItem.getAttribute('gs-w');
    const updatedH = await widgetItem.getAttribute('gs-h');

    // Assert coordinates are not default or snapped back to 0,0
    console.log(`Updated coordinates: x=${updatedX}, y=${updatedY}, w=${updatedW}, h=${updatedH}`);
    expect(updatedY).not.toBe('0');
    expect(updatedW).toBe('12');
    expect(updatedH).toBe('6');

    // Save dashboard
    await page.locator('#btnSave').click();
    await page.waitForURL(new RegExp(`/plswk/Dashboards/[^/]+`));
    // Wait for view mode to load
    await page.waitForSelector('[data-testid="dash-edit-mode"]', { state: 'visible' });

    // Assert view mode reloads the widget at the same coordinates
    const savedWidget = page.locator('.grid-stack-item').first();
    await page.screenshot({ path: 'test-results/debug-edit-mode-saved.png' });
    await expect(savedWidget).toHaveAttribute('gs-x', updatedX!);
    await expect(savedWidget).toHaveAttribute('gs-y', updatedY!);
    await expect(savedWidget).toHaveAttribute('gs-w', updatedW!);
    await expect(savedWidget).toHaveAttribute('gs-h', updatedH!);

    // Verify visual layout: ensure the widget's bounding box is placed down-grid
    const savedBox = await savedWidget.boundingBox();
    expect(savedBox).not.toBeNull();
    const gridContainer = page.locator('#dashGrid2');
    const gridBox = await gridContainer.boundingBox();
    expect(gridBox).not.toBeNull();
    if (savedBox && gridBox) {
      const relativeTop = savedBox.y - gridBox.y;
      console.log(`Widget relative top in view mode: ${relativeTop}px`);
      // Since it is saved at y=4, cellHeight=80, margin=8, 
      // relativeTop should be around 4 * 88 = 352px. Verify it's positioned down.
      expect(relativeTop).toBeGreaterThan(200);
    }
  });

  // 8. E2E test for Telemetry Key Picker Dialog Expansion layout and overlay
  test('Telemetry Key Picker Dialog Expansion layout and overlay', async ({ page }) => {
    // Navigate to dashboards list via admin auth proxy to get write permissions
    await page.goto(`${ADMIN_PROXY_URL}/plswk/Dashboards`);
    await page.waitForSelector('[data-testid="dash-list-mode"]', { state: 'visible' });

    // Open create dashboard modal
    const createBtn = page.locator('[data-testid="dash-create-btn"]');
    if (await createBtn.count() > 0) {
      await createBtn.click();
    } else {
      await page.locator('#emptyDashboards button').click();
    }

    const modal = page.locator('[data-testid="create-dash-modal"]');
    await expect(modal).toBeVisible();

    const uniqueName = `Picker Test ${Date.now()}`;
    await page.locator('#newDashName').fill(uniqueName);
    await modal.locator('button:has-text("Create")').click();

    // Verify redirection to the new dashboard in edit mode
    await page.waitForURL(/\/plswk\/Dashboards\/[^/]+/);
    await page.waitForSelector('[data-testid="dash-edit-mode"]', { state: 'visible' });

    // Click "Add your first widget" or "Add Widget" on the top right
    const addFirstWidgetBtn = page.locator('button:has-text("Add your first widget")');
    if (await addFirstWidgetBtn.count() > 0) {
      await addFirstWidgetBtn.click();
    } else {
      await page.locator('[data-testid="dash-add-widget-btn"]').click();
    }

    // Wait for the Add Widget modal
    const addWidgetModal = page.locator('#addWidgetModal');
    await expect(addWidgetModal).toBeVisible();

    // Confirm that the key picker is NOT expanded by default
    const wrapper = page.locator('#keyPickerWrapper');
    await expect(wrapper).not.toHaveClass(/key-picker-expanded/);
    const keyPicker = page.locator('#keyPicker');
    
    // Check initial height constraint (should be around max-height: 250px)
    const initialBox = await keyPicker.boundingBox();
    expect(initialBox).not.toBeNull();
    if (initialBox) {
      expect(initialBox.height).toBeLessThanOrEqual(260); // 250px plus border/padding
    }

    // Open the key selector
    await page.locator('#btnKeyPickerOpen').click();

    // Assert that the wrapper has the expanded class
    await expect(wrapper).toHaveClass(/key-picker-expanded/);

    // Verify that search wrapper is visible inside the picker
    const searchWrapper = page.locator('#keySearchWrapper');
    await expect(searchWrapper).toBeVisible();

    // Assert that the expanded key picker dialog is "big" (positioned absolute/fixed)
    // Its height should now be much larger than 250px (e.g. up to 600px/650px depending on viewport/content)
    const expandedBox = await keyPicker.boundingBox();
    expect(expandedBox).not.toBeNull();
    if (expandedBox) {
      expect(expandedBox.height).toBeGreaterThanOrEqual(250);
      expect(expandedBox.width).toBeGreaterThan(500); // 90vw should be quite wide, default width is 1280
    }

    // Close the key selector
    await page.locator('#keyPickerWrapper button:has-text("OK")').click();
    await expect(wrapper).not.toHaveClass(/key-picker-expanded/);
    await expect(searchWrapper).toBeHidden();
  });
});
