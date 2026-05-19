import { DashboardStore } from './store';
import { IWidget, IWidgetConfig } from './types';
import { renderBackgroundSvg, renderScadaPoint, fitWidgetToSvg } from './scada';
import { addWidgetToGrid, renderWidgetContent } from './widgets';

// editor.ts – Widget configuration and management
// ── ADD/EDIT WIDGET ──────────────────────────────────────────────────────

export async function openAddWidget(): Promise<void> {
    DashboardStore.editingWidgetId = null;
    DashboardStore.activeKeyOrder = []; 
    document.getElementById('widgetModalTitle')!.textContent = 'Add Widget';
    document.getElementById('widgetConfirmText')!.textContent = 'Add Widget';
    const titleEl = document.getElementById('widgetTitle') as HTMLInputElement;
    if (titleEl) titleEl.value = '';
    const stackedEl = document.getElementById('optStacked') as HTMLInputElement;
    if (stackedEl) stackedEl.checked = false;
    const legendEl = document.getElementById('optLegend') as HTMLInputElement;
    if (legendEl) legendEl.checked = true;
    const chartTypeEl = document.getElementById('optChartType') as HTMLSelectElement;
    if (chartTypeEl) chartTypeEl.value = 'line';
    DashboardStore.pendingSvgContent = '';
    const svgPrev = document.getElementById('svgPreviewThumb'); if (svgPrev) { svgPrev.innerHTML = ''; svgPrev.classList.add('hidden'); }
    const svgLbl = document.getElementById('svgDropLabel'); if (svgLbl) svgLbl.classList.remove('hidden');
    selectWidgetType(document.querySelector('.wtype-card[data-type="timeseries"]') as HTMLElement);
    await (window as any).loadKeyPicker();
    (window as any).renderKeyList([], true);
    document.getElementById('addWidgetModal')!.style.display = 'flex';
}

export function editWidget(wid: string): void {
    const w = DashboardStore.dashboard!.widgets!.find(x => x.id === wid); if (!w) return;
    DashboardStore.editingWidgetId = wid;
    const cfg = w.config || {};
    const keys = cfg.keys || []; if (cfg.key && !keys.includes(cfg.key)) keys.push(cfg.key);
    
    const allKeys = (window as any).allKeys || [];
    DashboardStore.activeKeyOrder = [...keys, ...allKeys.map((k: any) => k.key).filter((k: any) => !keys.includes(k))];

    document.getElementById('widgetModalTitle')!.textContent = 'Edit Widget';
    document.getElementById('widgetConfirmText')!.textContent = 'Save Changes';
    const titleEl = document.getElementById('widgetTitle') as HTMLInputElement;
    if (titleEl) titleEl.value = w.title || '';
    
    selectWidgetType(document.querySelector(`.wtype-card[data-type="${w.type}"]`) as HTMLElement);
    
    const chartTypeEl = document.getElementById('optChartType') as HTMLSelectElement;
    if (chartTypeEl) chartTypeEl.value = cfg.chartType || 'line';
    
    const stackedEl = document.getElementById('optStacked') as HTMLInputElement;
    if (stackedEl) stackedEl.checked = !!cfg.stacked;
    
    const legendEl = document.getElementById('optLegend') as HTMLInputElement;
    if (legendEl) legendEl.checked = cfg.showLegend !== false;
    
    // Restore layout option for scada-point
    const layoutRadio = document.querySelector(`input[name="pointLayout"][value="${cfg.layout || 'vertical'}"]`) as HTMLInputElement;
    if (layoutRadio) layoutRadio.checked = true;

    // Restore SVG color
    if (w.type === 'background-svg' && cfg.color) {
        const colorEl = document.getElementById('svgColor') as HTMLInputElement;
        if (colorEl) colorEl.value = cfg.color;
    }

    if (typeof (window as any).loadKeyPicker === 'function') {
        (window as any).loadKeyPicker().then(() => {
            const selected = keys.map((key: string) => allKeys.find((k: any) => k.key === key)).filter(Boolean);
            (window as any).renderKeyList(selected as any[], true);

            // Initialize animation rule editor for background-svg widgets AFTER keys are rendered
            if (w.type === 'background-svg') {
                if (typeof (window as any).initAnimRuleEditor === 'function') {
                    (window as any).initAnimRuleEditor(cfg.animationRules || []);
                }
            }
        });
    }
    document.getElementById('addWidgetModal')!.style.display = 'flex';
}

export function closeAddWidget(): void { 
    document.getElementById('addWidgetModal')!.style.display = 'none'; 
    if (DashboardStore.isKeySelectorOpen) {
        if (typeof (window as any).closeKeySelector === 'function') {
            (window as any).closeKeySelector();
        }
    }
}

export function selectWidgetType(el: HTMLElement | null): void {
    if (!el) return;
    document.querySelectorAll('.wtype-card').forEach(c => c.classList.remove('selected'));
    el.classList.add('selected'); 
    DashboardStore.selectedType = el.dataset.type || 'timeseries';
    
    document.getElementById('tsOptions')!.style.display = DashboardStore.selectedType === 'timeseries' ? '' : 'none';
    document.getElementById('svgOptions')!.style.display = DashboardStore.selectedType === 'background-svg' ? '' : 'none';
    document.getElementById('pointOptions')!.style.display = DashboardStore.selectedType === 'scada-point' ? '' : 'none';
    const keySection = document.getElementById('keyPicker')?.closest('.mb-5') as HTMLElement;
    if (keySection) keySection.style.display = '';
    // Show/hide animation rules section
    const animSection = document.getElementById('animRulesSection');
    if (animSection) animSection.style.display = DashboardStore.selectedType === 'background-svg' ? '' : 'none';
    
    // Wire SVG source radio toggle
    if (DashboardStore.selectedType === 'background-svg') {
        document.querySelectorAll('input[name="svgSource"]').forEach(r => {
            (r as HTMLInputElement).onchange = () => {
                const area = document.getElementById('svgImportArea');
                if (area) area.style.display = (r as HTMLInputElement).value === 'import' && (r as HTMLInputElement).checked ? '' : 'none';
            };
        });
        // Reset to draw.io default
        const drawioRadio = document.querySelector('input[name="svgSource"][value="drawio"]') as HTMLInputElement;
        if (drawioRadio) drawioRadio.checked = true;
        const area = document.getElementById('svgImportArea');
        if (area) area.style.display = 'none';
    }
}

export function confirmAddWidget(): void {
    const title = (document.getElementById('widgetTitle') as HTMLInputElement).value.trim() || 'Untitled';

    const checkedKeys = [...document.querySelectorAll('#keyList input[name="wkey"]')].filter(i => (i as HTMLInputElement).type === 'hidden' || (i as HTMLInputElement).checked).map(c => (c as HTMLInputElement).value);

    // Background SVG
    if (DashboardStore.selectedType === 'background-svg') {
        const svgSource = (document.querySelector('input[name="svgSource"]:checked') as HTMLInputElement)?.value || 'drawio';
        const color = (document.getElementById('svgColor') as HTMLInputElement)?.value || '#38bdf8';
        const animRules = typeof (window as any).getAnimRules === 'function' ? (window as any).getAnimRules() : [];

        // Editing existing background-svg widget
        if (DashboardStore.editingWidgetId) {
            const w = DashboardStore.dashboard!.widgets!.find(x => x.id === DashboardStore.editingWidgetId);
            if (w) {
                w.title = title;
                w.config = w.config || {};
                w.config.color = color;
                w.config.animationRules = animRules;
                w.config.keys = checkedKeys;
            }
            closeAddWidget(); return;
        }

        if (svgSource === 'drawio') {
            closeAddWidget();
            if (typeof (window as any).openDrawioEditor === 'function') {
                (window as any).openDrawioEditor('', (newSvg: string) => {
                    const w: IWidget = { id: 'w' + Date.now().toString(36), type: 'background-svg', title: title || 'Background', x: 0, y: 0, w: 12, h: 8, config: { svg: newSvg, color, posX: 5, posY: 5, posW: 90, posH: 90, animationRules: animRules, keys: checkedKeys } };
                    fitWidgetToSvg(w);
                    DashboardStore.dashboard!.widgets = DashboardStore.dashboard!.widgets || []; DashboardStore.dashboard!.widgets.push(w);
                    renderBackgroundSvg(w);
                    if (typeof (window as any).closeDrawioEditor === 'function') (window as any).closeDrawioEditor();
                });
            }
            return;
        }

        if (!DashboardStore.pendingSvgContent) { window.pwToast('Please upload an SVG file.', 'error'); return; }
        const w: IWidget = { id: 'w' + Date.now().toString(36), type: 'background-svg', title: title || 'Background', x: 0, y: 0, w: 12, h: 8, config: { svg: DashboardStore.pendingSvgContent, color, posX: 5, posY: 5, posW: 90, posH: 90, animationRules: animRules, keys: checkedKeys } };
        DashboardStore.dashboard!.widgets = DashboardStore.dashboard!.widgets || []; DashboardStore.dashboard!.widgets.push(w);
        renderBackgroundSvg(w);
        closeAddWidget(); return;
    }

    const checked = checkedKeys;
    if (!checked.length) { window.pwToast('Select at least one telemetry.', 'error'); return; }

    if (DashboardStore.selectedType === 'scada-point') {
        const layout = (document.querySelector('input[name="pointLayout"]:checked') as HTMLInputElement)?.value || 'vertical';
        const cfg: IWidgetConfig = { keys: checked, posX: 50, posY: 50, layout };
        if (DashboardStore.editingWidgetId) {
            const w = DashboardStore.dashboard!.widgets!.find(x => x.id === DashboardStore.editingWidgetId);
            if (w) {
                w.title = title; w.config = w.config || {}; w.config.keys = checked; w.config.layout = layout;
                const old = document.querySelector(`.scada-point[data-wid="${DashboardStore.editingWidgetId}"]`);
                if (old) old.remove();
                renderScadaPoint(w);
            }
        } else {
            const w: IWidget = { id: 'w' + Date.now().toString(36), type: 'scada-point', title, x: 0, y: 0, w: 0, h: 0, config: cfg };
            DashboardStore.dashboard!.widgets = DashboardStore.dashboard!.widgets || []; DashboardStore.dashboard!.widgets.push(w);
            renderScadaPoint(w);
        }
        closeAddWidget(); return;
    }

    const cfg: IWidgetConfig = { keys: checked, chartType: (document.getElementById('optChartType') as HTMLSelectElement).value, stacked: (document.getElementById('optStacked') as HTMLInputElement).checked, showLegend: (document.getElementById('optLegend') as HTMLInputElement).checked };
    if (DashboardStore.selectedType === 'single-value') cfg.key = checked[0];

    if (DashboardStore.editingWidgetId) {
        const w = DashboardStore.dashboard!.widgets!.find(x => x.id === DashboardStore.editingWidgetId);
        if (w) {
            w.title = title; w.type = DashboardStore.selectedType; w.config = cfg;
            const body = document.getElementById('wb_' + w.id); if (body) renderWidgetContent(w);
        }
    } else {
        const w: IWidget = { id: 'w' + Date.now().toString(36), type: DashboardStore.selectedType, title, x: 0, y: 0, w: DashboardStore.selectedType === 'single-value' ? 3 : 6, h: DashboardStore.selectedType === 'single-value' ? 3 : 4, config: cfg };
        DashboardStore.dashboard!.widgets = DashboardStore.dashboard!.widgets || []; DashboardStore.dashboard!.widgets.push(w);
        addWidgetToGrid(w);
    }
    closeAddWidget();
}

export async function removeWidget(wid: string): Promise<void> {
    if (!await window.pwConfirm((window as any).t ? (window as any).t('confirm_remove_widget') : 'Remove widget?', 'Remove Widget')) return;
    const removed = DashboardStore.dashboard!.widgets!.find(w => w.id === wid);
    DashboardStore.dashboard!.widgets = DashboardStore.dashboard!.widgets!.filter(w => w.id !== wid);
    if (removed?.type === 'background-svg') {
        const el = document.querySelector(`.scada-svg-widget[data-wid="${wid}"]`); if (el) el.remove();
    } else if (removed?.type === 'scada-point') {
        const sp = document.querySelector(`.scada-point[data-wid="${wid}"]`); if (sp) sp.remove();
    } else {
        const el = DashboardStore.grid.getGridItems().find((e: HTMLElement) => e.getAttribute('gs-id') === wid);
        if (el) DashboardStore.grid.removeWidget(el);
        if (DashboardStore.charts[wid]) { DashboardStore.charts[wid].destroy(); delete DashboardStore.charts[wid]; }
    }
    if (!DashboardStore.dashboard!.widgets!.length) document.getElementById('emptyDash')!.style.display = 'flex';
}

export function duplicateWidget(wid: string): void {
    const src = DashboardStore.dashboard!.widgets!.find(w => w.id === wid); if (!src) return;
    const copy: IWidget = JSON.parse(JSON.stringify(src));
    copy.id = 'w' + Date.now().toString(36);
    copy.title = 'Copy of ' + (copy.title || 'Untitled');
    if (copy.type === 'scada-point' && copy.config) { copy.config.posX = (copy.config.posX || 50) + 3; copy.config.posY = (copy.config.posY || 50) + 3; }
    else if (copy.type === 'background-svg' && copy.config) { copy.config.posX = (copy.config.posX || 5) + 3; copy.config.posY = (copy.config.posY || 5) + 3; }
    else { copy.x = (copy.x || 0) + 1; copy.y = (copy.y || 0) + 1; }
    DashboardStore.dashboard!.widgets!.push(copy);
    if (copy.type === 'background-svg') renderBackgroundSvg(copy);
    else if (copy.type === 'scada-point') renderScadaPoint(copy);
    else addWidgetToGrid(copy);
}

Object.assign(window, {
    openAddWidget, editWidget, closeAddWidget, selectWidgetType, confirmAddWidget, removeWidget, duplicateWidget
});
