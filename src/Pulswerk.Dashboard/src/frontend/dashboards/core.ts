import { DashboardStore } from './store';
import { IDashboard } from './types';
import { DashboardService } from './api';

// core.js – Custom dashboard engine (Core Initialization & Polling)
export const COLORS = [
    '#38bdf8', '#f59e0b', '#10b981', '#a78bfa', '#f472b6',  // sky, amber, emerald, violet, pink
    '#fb923c', '#34d399', '#60a5fa', '#e879f9', '#fbbf24',  // orange, teal, blue, fuchsia, yellow
    '#22d3ee', '#ef4444', '#84cc16', '#c084fc', '#f97316',  // cyan, red, lime, purple, orange-deep
    '#2dd4bf', '#818cf8', '#facc15', '#ec4899', '#14b8a6',  // teal-bright, indigo, yellow-warm, pink-hot, teal-dark
];

export const token = () => (document.querySelector('input[name="__RequestVerificationToken"]') as HTMLInputElement)?.value || '';
// Legacy api exported for backward compatibility with other files not yet migrated
export const api = async (handler: string, opts?: any) => { const r = await fetch(`${window.location.pathname}?handler=${handler}`, opts); return r.json(); };

export function initDashboards(initial: IDashboard | null, editMode: boolean): void {
    if (initial) { DashboardStore.dashboard = initial; showDashboard(); if (editMode) enterEditMode(); }
    else loadList();
    // Init TW module
    const twContainer = document.getElementById('dashTwContainer');
    if (twContainer) {
        DashboardStore.dashTw = (window as any).createTimeWindowSelector(twContainer, {
            mode: DashboardStore.dashboard?.timewindow?.mode || 'realtime',
            realtimeMs: DashboardStore.dashboard?.timewindow?.realtimeMs || 3600000,
            onChange: () => refreshAllWidgets()
        });
    }
    // Enter key shortcuts
    document.getElementById('newDashName')?.addEventListener('keydown', e => { if (e.key === 'Enter') confirmCreate(); });
    document.getElementById('widgetTitle')?.addEventListener('keydown', e => { if (e.key === 'Enter') confirmAddWidget(); });
}

// ── LIST MODE ────────────────────────────────────────────────────────────
export async function loadList(): Promise<void> {
    const pageHeader = document.getElementById('pageHeader'); if (pageHeader) pageHeader.style.display = '';
    document.getElementById('dashMode')!.style.display = 'none';
    document.getElementById('listMode')!.style.display = '';
    const list = await DashboardService.fetchDashboardList();
    const g = document.getElementById('dashGrid')!, e = document.getElementById('emptyDashboards')!;
    if (!list || list.length === 0) { g.style.display = 'none'; e.style.display = 'flex'; return; }
    const favs = (window as any).pw_fav.get('pw_fav_dashboards');
    g.innerHTML = list.map((d: any) => {
        const wc = d.widgets?.length || 0;
        const ago = timeAgo(d.updatedAt);
        const isFav = favs.includes(d.id);
        return `<div class="dash-card" onclick="location.href='/plswk/Dashboards/${d.id}/${slugify(d.name)}'">
            <div style="display:flex;align-items:center;gap:0.6rem;margin-bottom:0.75rem">
                <div style="width:36px;height:36px;border-radius:10px;background:rgba(56,189,248,0.1);display:flex;align-items:center;justify-content:center;flex-shrink:0">
                    <i class="fas fa-th-large" style="color:#38bdf8;font-size:1rem"></i>
                </div>
                <div style="flex:1;min-width:0"><div class="dash-card-title">${(window as any).esc(d.name)}</div></div>
                <button class="btn-icon ${isFav ? 'active' : ''}" onclick="event.stopPropagation(); toggleFavoriteDash('${d.id}')" title="Favorite" style="display:${(window as any).pwCanEditFavorites ? 'flex' : 'none'}">
                    <i class="${isFav ? 'fas' : 'far'} fa-star" style="color:${isFav ? '#fbbf24' : 'inherit'}"></i>
                </button>
            </div>
            <div class="dash-card-desc">${(window as any).esc(d.description || 'No description')}</div>
            <div class="dash-card-meta">
                <span><i class="fas fa-puzzle-piece" style="margin-right:0.3rem"></i>${wc} widget${wc !== 1 ? 's' : ''} · ${ago}</span>
                <div class="dash-card-actions" onclick="event.stopPropagation()" style="display:${(window as any).pwCanEditDashboard ? 'flex' : 'none'}">
                    <button class="btn-ghost btn-sm" onclick="location.href='/plswk/Dashboards/${d.id}/${slugify(d.name)}?edit=true'"><i class="fas fa-pen"></i></button>
                    <button class="btn-ghost btn-sm btn-danger" onclick="deleteDash('${d.id}')"><i class="fas fa-trash"></i></button>
                </div>
            </div>
        </div>`;
    }).join('');
}

export function toggleFavoriteDash(id: string): void {
    let favs = (window as any).pw_fav.get('pw_fav_dashboards');
    if (favs.includes(id)) favs = favs.filter((x: string) => x !== id);
    else favs.push(id);
    (window as any).pw_fav.set('pw_fav_dashboards', favs);
    
    // Refresh UI
    if (document.getElementById('listMode')!.style.display !== 'none') loadList();
    else {
        const btn = document.getElementById('btnFavDash');
        if (btn) {
            const isFav = favs.includes(id);
            btn.classList.toggle('active', isFav);
            btn.querySelector('i')!.className = (isFav ? 'fas' : 'far') + ' fa-star';
            btn.querySelector('i')!.style.color = isFav ? '#fbbf24' : '';
        }
    }
}

export function createDashboard(): void { document.getElementById('createModal')!.style.display = 'flex'; document.getElementById('newDashName')!.focus(); }
export async function confirmCreate(): Promise<void> {
    const name = (document.getElementById('newDashName') as HTMLInputElement).value.trim();
    if (!name) return;
    const desc = (document.getElementById('newDashDesc') as HTMLInputElement).value;
    const d = await DashboardService.createDashboard(name, desc);
    location.href = `/plswk/Dashboards/${d.id}/${slugify(d.name)}?edit=true`;
}
export async function deleteDash(id: string): Promise<void> {
    if (!confirm('Delete this dashboard?')) return;
    await DashboardService.deleteDashboard(id);
    loadList();
}

// ── DASHBOARD VIEW ────────────────────────────────────────────────────────
export async function showDashboard(): Promise<void> {
    const pageHeader = document.getElementById('pageHeader'); if (pageHeader) pageHeader.style.display = 'none';
    document.getElementById('listMode')!.style.display = 'none';
    document.getElementById('dashMode')!.style.display = '';
    document.getElementById('dashTitleView')!.textContent = DashboardStore.dashboard!.name;
    (document.getElementById('dashTitle') as HTMLInputElement).value = DashboardStore.dashboard!.name;
    const sep = document.getElementById('dashBreadcrumbSep'); if (sep) sep.style.display = '';
    
    // Init favorite star
    const favs = (window as any).pw_fav.get('pw_fav_dashboards');
    const isFav = favs.includes(DashboardStore.dashboard!.id);
    const btn = document.getElementById('btnFavDash');
    if (btn) {
        btn.classList.toggle('active', isFav);
        btn.querySelector('i')!.className = (isFav ? 'fas' : 'far') + ' fa-star';
        btn.querySelector('i')!.style.color = isFav ? '#fbbf24' : '';
        btn.style.display = (window as any).pwCanEditFavorites ? 'flex' : 'none';
    }
    if (!(window as any).allKeys?.length) { try { (window as any).allKeys = await DashboardService.fetchAvailableDataPoints(); } catch (e) { (window as any).allKeys = []; } }
    initGrid();
    if (DashboardStore.dashboard!.widgets?.length) {
        (window as any).renderAllWidgets();
        setTimeout(() => {
            if ((window as any).updateAllSvgAnimations) {
                (window as any).updateAllSvgAnimations();
            }
        }, 100);
    }
    else document.getElementById('emptyDash')!.style.display = 'flex';
    startPolling();
}

export function initGrid(): void {
    if (DashboardStore.grid) DashboardStore.grid.destroy(false);
    DashboardStore.grid = (window as any).GridStack.init({ column: 12, cellHeight: 80, margin: 8, disableResize: true, disableDrag: true, float: true }, '#dashGrid2');
    DashboardStore.grid.on('resizestop', function (_event: Event, el: HTMLElement) {
        const id = el.getAttribute('gs-id');
        if (id && DashboardStore.charts[id]) {
            setTimeout(() => DashboardStore.charts[id].windowResize(), 50);
        }
    });
}

export function enterEditMode(): void {
    DashboardStore.isEditing = true;
    if (DashboardStore.grid) { DashboardStore.grid.enableMove(true); DashboardStore.grid.enableResize(true); }
    document.getElementById('dashTitle')!.style.display = ''; document.getElementById('dashTitleView')!.style.display = 'none';
    document.getElementById('btnEdit')!.style.display = 'none';
    document.getElementById('btnSave')!.style.display = ''; document.getElementById('btnCancel')!.style.display = ''; document.getElementById('btnAddWidget')!.style.display = '';
    document.querySelectorAll('.widget-actions').forEach(e => (e as HTMLElement).style.display = 'flex');
    document.getElementById('emptyDash')!.style.display = DashboardStore.dashboard!.widgets?.length ? 'none' : 'flex';
    // Raise bg layer above grid so SVG widgets can be dragged
    const bgLayer = document.getElementById('scadaBg');
    if (bgLayer) bgLayer.classList.add('edit-mode');
    document.querySelectorAll('.scada-point').forEach(el => {
        el.classList.add('edit-mode');
        const wid = (el as HTMLElement).dataset.wid;
        const w = DashboardStore.dashboard!.widgets!.find((x: any) => x.id === wid);
        if (w) (window as any).initScadaPointDrag(el as HTMLElement, w);
    });
    document.querySelectorAll('.scada-svg-widget').forEach(el => {
        el.classList.add('edit-mode');
        const wid = (el as HTMLElement).dataset.wid;
        const w = DashboardStore.dashboard!.widgets!.find((x: any) => x.id === wid);
        if (w) (window as any).initSvgWidgetDrag(el as HTMLElement, w);
    });
}

export function cancelEdit(): void { location.href = `/plswk/Dashboards/${DashboardStore.dashboard!.id}/${slugify(DashboardStore.dashboard!.name)}`; }

export async function saveDashboard(): Promise<void> {
    DashboardStore.dashboard!.name = (document.getElementById('dashTitle') as HTMLInputElement).value.trim() || DashboardStore.dashboard!.name;
    const r = DashboardStore.dashTw ? DashboardStore.dashTw.getRange() : {};
    DashboardStore.dashboard!.timewindow = { mode: r.mode || 'realtime', realtimeMs: r.realtimeMs || 3600000 };
    // Sync widget positions from grid
    const items = DashboardStore.grid.getGridItems();
    items.forEach((el: HTMLElement) => {
        const wid = el.getAttribute('gs-id');
        const w = DashboardStore.dashboard!.widgets!.find((x: any) => x.id === wid);
        if (w) { w.x = parseInt(el.getAttribute('gs-x') || '0') || 0; w.y = parseInt(el.getAttribute('gs-y') || '0') || 0; w.w = parseInt(el.getAttribute('gs-w') || '6') || 6; w.h = parseInt(el.getAttribute('gs-h') || '4') || 4; }
    });
    // Note: scada-point positions are already synced to config by the drag handler's onUp
    await DashboardService.saveDashboard(DashboardStore.dashboard);
    location.href = `/plswk/Dashboards/${DashboardStore.dashboard!.id}/${slugify(DashboardStore.dashboard!.name)}`;
}

// ── POLLING ──────────────────────────────────────────────────────────────
export function startPolling(): void { if (DashboardStore.pollTimer) clearInterval(DashboardStore.pollTimer); DashboardStore.pollTimer = setInterval(refreshAllWidgets, 10000); }
export function refreshAllWidgets(): void {
    if (!DashboardStore.dashboard?.widgets) return;
    DashboardStore.dashboard.widgets.forEach((w: any) => {
        if (w.type === 'timeseries' && (!DashboardStore.dashTw || DashboardStore.dashTw.mode === 'realtime')) (window as any).renderTimeseries(w, document.getElementById('wb_' + w.id)!, w.config || {});
        else if (w.type === 'latest-values') (window as any).updateLatestValues(w, w.config || {});
        else if (w.type === 'single-value') (window as any).updateSingleValue(w, w.config || {});
    });
    (window as any).updateAllScadaPoints();
    (window as any).updateAllSvgAnimations();
}

// ── HELPERS ──────────────────────────────────────────────────────────────
// esc() and friendlyName() are provided by base.js
export function keyName(key: string): string { const parts = key.split('_'); return parts.length > 2 ? parts.slice(1, -1).join(' ') : key; }
export function hexToRgba(hex: string, a: number): string { const r = parseInt(hex.slice(1, 3), 16), g = parseInt(hex.slice(3, 5), 16), b = parseInt(hex.slice(5, 7), 16); return `rgba(${r},${g},${b},${a})`; }
export function timeAgo(isoStr: string): string {
    if (!isoStr) return '';
    const diff = Date.now() - new Date(isoStr).getTime();
    if (diff < 60000) return 'just now';
    if (diff < 3600000) return Math.floor(diff / 60000) + ' min ago';
    if (diff < 86400000) return Math.floor(diff / 3600000) + ' hr ago';
    return Math.floor(diff / 86400000) + ' days ago';
}

export function slugify(text: string): string {
    return text.toString().toLowerCase()
        .replace(/\s+/g, '-')           // Replace spaces with -
        .replace(/[^\w\-]+/g, '')       // Remove all non-word chars
        .replace(/\-\-+/g, '-')         // Replace multiple - with single -
        .replace(/^-+/, '')             // Trim - from start of text
        .replace(/-+$/, '');            // Trim - from end of text
}

export function updateHistoryLiveValue(key: string, value: any): void {
    if (typeof (window as any).currentHistoryKey !== 'undefined' && (window as any).currentHistoryKey === key) {
        const hlv = document.getElementById('chartLiveValue');
        if (hlv && document.getElementById('historyModal')?.style.display === 'flex') {
            hlv.textContent = value;
        }
    }
}

// Keep exporting globally for older scripts and Razor pages until they are converted to ES Modules
Object.assign(window, {
    initDashboards, loadList, toggleFavoriteDash, createDashboard, confirmCreate, deleteDash,
    showDashboard, initGrid, enterEditMode, cancelEdit, saveDashboard, startPolling, refreshAllWidgets,
    keyName, hexToRgba, timeAgo, slugify, updateHistoryLiveValue, api, token, COLORS, DashboardStore
});

// Deprecated Global Accessors (to be removed once all files use DashboardStore directly)
Object.defineProperties(window, {
    grid: { get: () => DashboardStore.grid, set: (v) => { DashboardStore.grid = v; }, configurable: true },
    dashboard: { get: () => DashboardStore.dashboard, set: (v) => { DashboardStore.dashboard = v; }, configurable: true },
    isEditing: { get: () => DashboardStore.isEditing, set: (v) => { DashboardStore.isEditing = v; }, configurable: true },
    charts: { get: () => DashboardStore.charts, set: (v) => { DashboardStore.charts = v; }, configurable: true },
    pollTimer: { get: () => DashboardStore.pollTimer, set: (v) => { DashboardStore.pollTimer = v; }, configurable: true },
    pendingSvgContent: { get: () => DashboardStore.pendingSvgContent, set: (v) => { DashboardStore.pendingSvgContent = v; }, configurable: true },
    selectedType: { get: () => DashboardStore.selectedType, set: (v) => { DashboardStore.selectedType = v; }, configurable: true },
    editingWidgetId: { get: () => DashboardStore.editingWidgetId, set: (v) => { DashboardStore.editingWidgetId = v; }, configurable: true },
    activeKeyOrder: { get: () => DashboardStore.activeKeyOrder, set: (v) => { DashboardStore.activeKeyOrder = v; }, configurable: true },
    isKeySelectorOpen: { get: () => DashboardStore.isKeySelectorOpen, set: (v) => { DashboardStore.isKeySelectorOpen = v; }, configurable: true },
    dashTw: { get: () => DashboardStore.dashTw, set: (v) => { DashboardStore.dashTw = v; }, configurable: true },
    pendingRenders: { get: () => DashboardStore.pendingRenders, set: (_v) => { /* noop */ }, configurable: true }
});
