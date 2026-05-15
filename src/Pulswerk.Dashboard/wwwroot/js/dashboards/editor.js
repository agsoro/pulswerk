// editor.js – Widget configuration and management
// ── ADD/EDIT WIDGET ──────────────────────────────────────────────────────

async function openAddWidget() {
    editingWidgetId = null;
    activeKeyOrder = []; 
    document.getElementById('widgetModalTitle').textContent = 'Add Widget';
    document.getElementById('widgetConfirmText').textContent = 'Add Widget';
    document.getElementById('widgetTitle').value = '';
    document.getElementById('optStacked').checked = false;
    document.getElementById('optLegend').checked = true;
    document.getElementById('optChartType').value = 'line';
    pendingSvgContent = '';
    const svgPrev = document.getElementById('svgPreviewThumb'); if (svgPrev) { svgPrev.innerHTML = ''; svgPrev.classList.add('hidden'); }
    const svgLbl = document.getElementById('svgDropLabel'); if (svgLbl) svgLbl.classList.remove('hidden');
    selectWidgetType(document.querySelector('.wtype-card[data-type="timeseries"]'));
    await loadKeyPicker();
    renderKeyList([], true);
    document.getElementById('addWidgetModal').style.display = 'flex';
}

function editWidget(wid) {
    const w = dashboard.widgets.find(x => x.id === wid); if (!w) return;
    editingWidgetId = wid;
    const cfg = w.config || {};
    const keys = cfg.keys || []; if (cfg.key && !keys.includes(cfg.key)) keys.push(cfg.key);
    activeKeyOrder = [...keys, ...allKeys.map(k => k.key).filter(k => !keys.includes(k))];

    document.getElementById('widgetModalTitle').textContent = 'Edit Widget';
    document.getElementById('widgetConfirmText').textContent = 'Save Changes';
    document.getElementById('widgetTitle').value = w.title;
    selectWidgetType(document.querySelector(`.wtype-card[data-type="${w.type}"]`));
    document.getElementById('optChartType').value = cfg.chartType || 'line';
    document.getElementById('optStacked').checked = !!cfg.stacked;
    document.getElementById('optLegend').checked = cfg.showLegend !== false;
    
    // Restore layout option for scada-point
    const layoutRadio = document.querySelector(`input[name="pointLayout"][value="${cfg.layout || 'vertical'}"]`);
    if (layoutRadio) layoutRadio.checked = true;

    // Restore SVG color
    if (w.type === 'background-svg' && cfg.color) document.getElementById('svgColor').value = cfg.color;

    loadKeyPicker().then(() => {
        // Render the small list with ONLY the selected keys in saved order
        const selected = keys.map(key => allKeys.find(k => k.key === key)).filter(Boolean);
        renderKeyList(selected, true);
    });
    document.getElementById('addWidgetModal').style.display = 'flex';
}

function closeAddWidget() { 
    document.getElementById('addWidgetModal').style.display = 'none'; 
    if (isKeySelectorOpen) {
        closeKeySelector();
    }
}

function selectWidgetType(el) {
    if (!el) return;
    document.querySelectorAll('.wtype-card').forEach(c => c.classList.remove('selected'));
    el.classList.add('selected'); selectedType = el.dataset.type;
    document.getElementById('tsOptions').style.display = selectedType === 'timeseries' ? '' : 'none';
    document.getElementById('svgOptions').style.display = selectedType === 'background-svg' ? '' : 'none';
    document.getElementById('pointOptions').style.display = selectedType === 'scada-point' ? '' : 'none';
    const keySection = document.getElementById('keyPicker')?.closest('.mb-5');
    if (keySection) keySection.style.display = selectedType === 'background-svg' ? 'none' : '';
    
    // Wire SVG source radio toggle
    if (selectedType === 'background-svg') {
        document.querySelectorAll('input[name="svgSource"]').forEach(r => {
            r.onchange = () => {
                const area = document.getElementById('svgImportArea');
                if (area) area.style.display = r.value === 'import' && r.checked ? '' : 'none';
            };
        });
        // Reset to draw.io default
        const drawioRadio = document.querySelector('input[name="svgSource"][value="drawio"]');
        if (drawioRadio) drawioRadio.checked = true;
        const area = document.getElementById('svgImportArea');
        if (area) area.style.display = 'none';
    }
}

function confirmAddWidget() {
    const title = document.getElementById('widgetTitle').value.trim() || 'Untitled';

    // Background SVG – no keys needed
    if (selectedType === 'background-svg') {
        const svgSource = document.querySelector('input[name="svgSource"]:checked')?.value || 'drawio';
        const color = document.getElementById('svgColor')?.value || '#38bdf8';

        if (svgSource === 'drawio') {
            closeAddWidget();
            openDrawioEditor('', newSvg => {
                const w = { id: 'w' + Date.now().toString(36), type: 'background-svg', title: title || 'Background', x: 0, y: 0, w: 12, h: 8, config: { svg: newSvg, color, posX: 5, posY: 5, posW: 90, posH: 90 } };
                fitWidgetToSvg(w);
                dashboard.widgets = dashboard.widgets || []; dashboard.widgets.push(w);
                renderBackgroundSvg(w);
                closeDrawioEditor();
            });
            return;
        }

        if (!pendingSvgContent) { alert('Please upload an SVG file.'); return; }
        const w = { id: 'w' + Date.now().toString(36), type: 'background-svg', title: title || 'Background', x: 0, y: 0, w: 12, h: 8, config: { svg: pendingSvgContent, color, posX: 5, posY: 5, posW: 90, posH: 90 } };
        dashboard.widgets = dashboard.widgets || []; dashboard.widgets.push(w);
        renderBackgroundSvg(w);
        closeAddWidget(); return;
    }

    const checked = [...document.querySelectorAll('#keyList input[name="wkey"]')].filter(i => i.type === 'hidden' || i.checked).map(c => c.value);
    if (!checked.length) { alert('Select at least one data key.'); return; }

    if (selectedType === 'scada-point') {
        const layout = document.querySelector('input[name="pointLayout"]:checked')?.value || 'vertical';
        const cfg = { keys: checked, posX: 50, posY: 50, layout };
        if (editingWidgetId) {
            const w = dashboard.widgets.find(x => x.id === editingWidgetId);
            if (w) {
                w.title = title; w.config.keys = checked; w.config.layout = layout;
                const old = document.querySelector(`.scada-point[data-wid="${editingWidgetId}"]`);
                if (old) old.remove();
                renderScadaPoint(w);
            }
        } else {
            const w = { id: 'w' + Date.now().toString(36), type: 'scada-point', title, x: 0, y: 0, w: 0, h: 0, config: cfg };
            dashboard.widgets = dashboard.widgets || []; dashboard.widgets.push(w);
            renderScadaPoint(w);
        }
        closeAddWidget(); return;
    }

    const cfg = { keys: checked, chartType: document.getElementById('optChartType').value, stacked: document.getElementById('optStacked').checked, showLegend: document.getElementById('optLegend').checked };
    if (selectedType === 'single-value') cfg.key = checked[0];

    if (editingWidgetId) {
        const w = dashboard.widgets.find(x => x.id === editingWidgetId);
        if (w) {
            w.title = title; w.type = selectedType; w.config = cfg;
            const body = document.getElementById('wb_' + w.id); if (body) renderWidgetContent(w);
        }
    } else {
        const w = { id: 'w' + Date.now().toString(36), type: selectedType, title, x: 0, y: 0, w: selectedType === 'single-value' ? 3 : 6, h: selectedType === 'single-value' ? 3 : 4, config: cfg };
        dashboard.widgets = dashboard.widgets || []; dashboard.widgets.push(w);
        addWidgetToGrid(w);
    }
    closeAddWidget();
}

function removeWidget(wid) {
    if (!confirm(t('confirm_remove_widget'))) return;
    const removed = dashboard.widgets.find(w => w.id === wid);
    dashboard.widgets = dashboard.widgets.filter(w => w.id !== wid);
    if (removed?.type === 'background-svg') {
        const el = document.querySelector(`.scada-svg-widget[data-wid="${wid}"]`); if (el) el.remove();
    } else if (removed?.type === 'scada-point') {
        const sp = document.querySelector(`.scada-point[data-wid="${wid}"]`); if (sp) sp.remove();
    } else {
        const el = grid.getGridItems().find(e => e.getAttribute('gs-id') === wid);
        if (el) grid.removeWidget(el);
        if (charts[wid]) { charts[wid].destroy(); delete charts[wid]; }
    }
    if (!dashboard.widgets.length) document.getElementById('emptyDash').style.display = 'flex';
}

function duplicateWidget(wid) {
    const src = dashboard.widgets.find(w => w.id === wid); if (!src) return;
    const copy = JSON.parse(JSON.stringify(src));
    copy.id = 'w' + Date.now().toString(36);
    copy.title = 'Copy of ' + (copy.title || 'Untitled');
    if (copy.type === 'scada-point' && copy.config) { copy.config.posX = (copy.config.posX || 50) + 3; copy.config.posY = (copy.config.posY || 50) + 3; }
    else if (copy.type === 'background-svg' && copy.config) { copy.config.posX = (copy.config.posX || 5) + 3; copy.config.posY = (copy.config.posY || 5) + 3; }
    else { copy.x = (copy.x || 0) + 1; copy.y = (copy.y || 0) + 1; }
    dashboard.widgets.push(copy);
    if (copy.type === 'background-svg') renderBackgroundSvg(copy);
    else if (copy.type === 'scada-point') renderScadaPoint(copy);
    else addWidgetToGrid(copy);
}
