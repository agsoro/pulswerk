import { DashboardStore } from './store';
import { renderBackgroundSvg, renderScadaPoint, fitWidgetToSvg } from './scada';
import { addWidgetToGrid, renderWidgetContent } from './widgets';
// editor.ts – Widget configuration and management
// ── ADD/EDIT WIDGET ──────────────────────────────────────────────────────
export async function openAddWidget() {
    DashboardStore.editingWidgetId = null;
    DashboardStore.activeKeyOrder = [];
    document.getElementById('widgetModalTitle').textContent = 'Add Widget';
    document.getElementById('widgetConfirmText').textContent = 'Add Widget';
    const titleEl = document.getElementById('widgetTitle');
    if (titleEl)
        titleEl.value = '';
    const stackedEl = document.getElementById('optStacked');
    if (stackedEl)
        stackedEl.checked = false;
    const legendEl = document.getElementById('optLegend');
    if (legendEl)
        legendEl.checked = true;
    const chartTypeEl = document.getElementById('optChartType');
    if (chartTypeEl)
        chartTypeEl.value = 'line';
    DashboardStore.pendingSvgContent = '';
    const svgPrev = document.getElementById('svgPreviewThumb');
    if (svgPrev) {
        svgPrev.innerHTML = '';
        svgPrev.classList.add('hidden');
    }
    const svgLbl = document.getElementById('svgDropLabel');
    if (svgLbl)
        svgLbl.classList.remove('hidden');
    selectWidgetType(document.querySelector('.wtype-card[data-type="timeseries"]'));
    await window.loadKeyPicker();
    window.renderKeyList([], true);
    document.getElementById('addWidgetModal').style.display = 'flex';
}
export function editWidget(wid) {
    const w = DashboardStore.dashboard.widgets.find(x => x.id === wid);
    if (!w)
        return;
    DashboardStore.editingWidgetId = wid;
    const cfg = w.config || {};
    const keys = cfg.keys || [];
    if (cfg.key && !keys.includes(cfg.key))
        keys.push(cfg.key);
    const allKeys = window.allKeys || [];
    DashboardStore.activeKeyOrder = [...keys, ...allKeys.map((k) => k.key).filter((k) => !keys.includes(k))];
    document.getElementById('widgetModalTitle').textContent = 'Edit Widget';
    document.getElementById('widgetConfirmText').textContent = 'Save Changes';
    const titleEl = document.getElementById('widgetTitle');
    if (titleEl)
        titleEl.value = w.title || '';
    selectWidgetType(document.querySelector(`.wtype-card[data-type="${w.type}"]`));
    const chartTypeEl = document.getElementById('optChartType');
    if (chartTypeEl)
        chartTypeEl.value = cfg.chartType || 'line';
    const stackedEl = document.getElementById('optStacked');
    if (stackedEl)
        stackedEl.checked = !!cfg.stacked;
    const legendEl = document.getElementById('optLegend');
    if (legendEl)
        legendEl.checked = cfg.showLegend !== false;
    // Restore layout option for scada-point
    const layoutRadio = document.querySelector(`input[name="pointLayout"][value="${cfg.layout || 'vertical'}"]`);
    if (layoutRadio)
        layoutRadio.checked = true;
    // Restore SVG color
    if (w.type === 'background-svg' && cfg.color) {
        const colorEl = document.getElementById('svgColor');
        if (colorEl)
            colorEl.value = cfg.color;
    }
    // Initialize animation rule editor for background-svg widgets
    if (w.type === 'background-svg') {
        if (typeof window.initAnimRuleEditor === 'function') {
            window.initAnimRuleEditor(cfg.animationRules || []);
        }
    }
    if (typeof window.loadKeyPicker === 'function') {
        window.loadKeyPicker().then(() => {
            const selected = keys.map(key => allKeys.find((k) => k.key === key)).filter(Boolean);
            window.renderKeyList(selected, true);
        });
    }
    document.getElementById('addWidgetModal').style.display = 'flex';
}
export function closeAddWidget() {
    document.getElementById('addWidgetModal').style.display = 'none';
    if (DashboardStore.isKeySelectorOpen) {
        if (typeof window.closeKeySelector === 'function') {
            window.closeKeySelector();
        }
    }
}
export function selectWidgetType(el) {
    if (!el)
        return;
    document.querySelectorAll('.wtype-card').forEach(c => c.classList.remove('selected'));
    el.classList.add('selected');
    DashboardStore.selectedType = el.dataset.type || 'timeseries';
    document.getElementById('tsOptions').style.display = DashboardStore.selectedType === 'timeseries' ? '' : 'none';
    document.getElementById('svgOptions').style.display = DashboardStore.selectedType === 'background-svg' ? '' : 'none';
    document.getElementById('pointOptions').style.display = DashboardStore.selectedType === 'scada-point' ? '' : 'none';
    const keySection = document.getElementById('keyPicker')?.closest('.mb-5');
    if (keySection)
        keySection.style.display = '';
    // Show/hide animation rules section
    const animSection = document.getElementById('animRulesSection');
    if (animSection)
        animSection.style.display = DashboardStore.selectedType === 'background-svg' ? '' : 'none';
    // Wire SVG source radio toggle
    if (DashboardStore.selectedType === 'background-svg') {
        document.querySelectorAll('input[name="svgSource"]').forEach(r => {
            r.onchange = () => {
                const area = document.getElementById('svgImportArea');
                if (area)
                    area.style.display = r.value === 'import' && r.checked ? '' : 'none';
            };
        });
        // Reset to draw.io default
        const drawioRadio = document.querySelector('input[name="svgSource"][value="drawio"]');
        if (drawioRadio)
            drawioRadio.checked = true;
        const area = document.getElementById('svgImportArea');
        if (area)
            area.style.display = 'none';
    }
}
export function confirmAddWidget() {
    const title = document.getElementById('widgetTitle').value.trim() || 'Untitled';
    const checkedKeys = [...document.querySelectorAll('#keyList input[name="wkey"]')].filter(i => i.type === 'hidden' || i.checked).map(c => c.value);
    // Background SVG
    if (DashboardStore.selectedType === 'background-svg') {
        const svgSource = document.querySelector('input[name="svgSource"]:checked')?.value || 'drawio';
        const color = document.getElementById('svgColor')?.value || '#38bdf8';
        const animRules = typeof window.getAnimRules === 'function' ? window.getAnimRules() : [];
        // Editing existing background-svg widget
        if (DashboardStore.editingWidgetId) {
            const w = DashboardStore.dashboard.widgets.find(x => x.id === DashboardStore.editingWidgetId);
            if (w) {
                w.title = title;
                w.config = w.config || {};
                w.config.color = color;
                w.config.animationRules = animRules;
                w.config.keys = checkedKeys;
            }
            closeAddWidget();
            return;
        }
        if (svgSource === 'drawio') {
            closeAddWidget();
            if (typeof window.openDrawioEditor === 'function') {
                window.openDrawioEditor('', (newSvg) => {
                    const w = { id: 'w' + Date.now().toString(36), type: 'background-svg', title: title || 'Background', x: 0, y: 0, w: 12, h: 8, config: { svg: newSvg, color, posX: 5, posY: 5, posW: 90, posH: 90, animationRules: animRules, keys: checkedKeys } };
                    fitWidgetToSvg(w);
                    DashboardStore.dashboard.widgets = DashboardStore.dashboard.widgets || [];
                    DashboardStore.dashboard.widgets.push(w);
                    renderBackgroundSvg(w);
                    if (typeof window.closeDrawioEditor === 'function')
                        window.closeDrawioEditor();
                });
            }
            return;
        }
        if (!DashboardStore.pendingSvgContent) {
            alert('Please upload an SVG file.');
            return;
        }
        const w = { id: 'w' + Date.now().toString(36), type: 'background-svg', title: title || 'Background', x: 0, y: 0, w: 12, h: 8, config: { svg: DashboardStore.pendingSvgContent, color, posX: 5, posY: 5, posW: 90, posH: 90, animationRules: animRules, keys: checkedKeys } };
        DashboardStore.dashboard.widgets = DashboardStore.dashboard.widgets || [];
        DashboardStore.dashboard.widgets.push(w);
        renderBackgroundSvg(w);
        closeAddWidget();
        return;
    }
    const checked = checkedKeys;
    if (!checked.length) {
        alert('Select at least one data key.');
        return;
    }
    if (DashboardStore.selectedType === 'scada-point') {
        const layout = document.querySelector('input[name="pointLayout"]:checked')?.value || 'vertical';
        const cfg = { keys: checked, posX: 50, posY: 50, layout };
        if (DashboardStore.editingWidgetId) {
            const w = DashboardStore.dashboard.widgets.find(x => x.id === DashboardStore.editingWidgetId);
            if (w) {
                w.title = title;
                w.config = w.config || {};
                w.config.keys = checked;
                w.config.layout = layout;
                const old = document.querySelector(`.scada-point[data-wid="${DashboardStore.editingWidgetId}"]`);
                if (old)
                    old.remove();
                renderScadaPoint(w);
            }
        }
        else {
            const w = { id: 'w' + Date.now().toString(36), type: 'scada-point', title, x: 0, y: 0, w: 0, h: 0, config: cfg };
            DashboardStore.dashboard.widgets = DashboardStore.dashboard.widgets || [];
            DashboardStore.dashboard.widgets.push(w);
            renderScadaPoint(w);
        }
        closeAddWidget();
        return;
    }
    const cfg = { keys: checked, chartType: document.getElementById('optChartType').value, stacked: document.getElementById('optStacked').checked, showLegend: document.getElementById('optLegend').checked };
    if (DashboardStore.selectedType === 'single-value')
        cfg.key = checked[0];
    if (DashboardStore.editingWidgetId) {
        const w = DashboardStore.dashboard.widgets.find(x => x.id === DashboardStore.editingWidgetId);
        if (w) {
            w.title = title;
            w.type = DashboardStore.selectedType;
            w.config = cfg;
            const body = document.getElementById('wb_' + w.id);
            if (body)
                renderWidgetContent(w);
        }
    }
    else {
        const w = { id: 'w' + Date.now().toString(36), type: DashboardStore.selectedType, title, x: 0, y: 0, w: DashboardStore.selectedType === 'single-value' ? 3 : 6, h: DashboardStore.selectedType === 'single-value' ? 3 : 4, config: cfg };
        DashboardStore.dashboard.widgets = DashboardStore.dashboard.widgets || [];
        DashboardStore.dashboard.widgets.push(w);
        addWidgetToGrid(w);
    }
    closeAddWidget();
}
export function removeWidget(wid) {
    if (!confirm(window.t ? window.t('confirm_remove_widget') : 'Remove widget?'))
        return;
    const removed = DashboardStore.dashboard.widgets.find(w => w.id === wid);
    DashboardStore.dashboard.widgets = DashboardStore.dashboard.widgets.filter(w => w.id !== wid);
    if (removed?.type === 'background-svg') {
        const el = document.querySelector(`.scada-svg-widget[data-wid="${wid}"]`);
        if (el)
            el.remove();
    }
    else if (removed?.type === 'scada-point') {
        const sp = document.querySelector(`.scada-point[data-wid="${wid}"]`);
        if (sp)
            sp.remove();
    }
    else {
        const el = DashboardStore.grid.getGridItems().find((e) => e.getAttribute('gs-id') === wid);
        if (el)
            DashboardStore.grid.removeWidget(el);
        if (DashboardStore.charts[wid]) {
            DashboardStore.charts[wid].destroy();
            delete DashboardStore.charts[wid];
        }
    }
    if (!DashboardStore.dashboard.widgets.length)
        document.getElementById('emptyDash').style.display = 'flex';
}
export function duplicateWidget(wid) {
    const src = DashboardStore.dashboard.widgets.find(w => w.id === wid);
    if (!src)
        return;
    const copy = JSON.parse(JSON.stringify(src));
    copy.id = 'w' + Date.now().toString(36);
    copy.title = 'Copy of ' + (copy.title || 'Untitled');
    if (copy.type === 'scada-point' && copy.config) {
        copy.config.posX = (copy.config.posX || 50) + 3;
        copy.config.posY = (copy.config.posY || 50) + 3;
    }
    else if (copy.type === 'background-svg' && copy.config) {
        copy.config.posX = (copy.config.posX || 5) + 3;
        copy.config.posY = (copy.config.posY || 5) + 3;
    }
    else {
        copy.x = (copy.x || 0) + 1;
        copy.y = (copy.y || 0) + 1;
    }
    DashboardStore.dashboard.widgets.push(copy);
    if (copy.type === 'background-svg')
        renderBackgroundSvg(copy);
    else if (copy.type === 'scada-point')
        renderScadaPoint(copy);
    else
        addWidgetToGrid(copy);
}
Object.assign(window, {
    openAddWidget, editWidget, closeAddWidget, selectWidgetType, confirmAddWidget, removeWidget, duplicateWidget
});
