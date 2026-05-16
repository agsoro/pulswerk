// core.js – Custom dashboard engine (Core Initialization & Polling)
const COLORS = [
    '#38bdf8', '#f59e0b', '#10b981', '#a78bfa', '#f472b6',  // sky, amber, emerald, violet, pink
    '#fb923c', '#34d399', '#60a5fa', '#e879f9', '#fbbf24',  // orange, teal, blue, fuchsia, yellow
    '#22d3ee', '#ef4444', '#84cc16', '#c084fc', '#f97316',  // cyan, red, lime, purple, orange-deep
    '#2dd4bf', '#818cf8', '#facc15', '#ec4899', '#14b8a6',  // teal-bright, indigo, yellow-warm, pink-hot, teal-dark
];
let grid = null, dashboard = null, isEditing = false, charts = {}, pollTimer = null, pendingSvgContent = '';
let selectedType = 'timeseries', editingWidgetId = null;
let activeKeyOrder = [], isKeySelectorOpen = false;
let dashTw = null; // reusable TW module instance
const pendingRenders = new Set();  // guard against overlapping async renders
const token = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
const api = async (handler, opts) => { const r = await fetch(`${window.location.pathname}?handler=${handler}`, opts); return r.json(); };

function initDashboards(initial, editMode) {
    if (initial) { dashboard = initial; showDashboard(); if (editMode) enterEditMode(); }
    else loadList();
    // Init TW module
    const twContainer = document.getElementById('dashTwContainer');
    if (twContainer) {
        dashTw = createTimeWindowSelector(twContainer, {
            mode: dashboard?.timewindow?.mode || 'realtime',
            realtimeMs: dashboard?.timewindow?.realtimeMs || 3600000,
            onChange: () => refreshAllWidgets()
        });
    }
    // Enter key shortcuts
    document.getElementById('newDashName')?.addEventListener('keydown', e => { if (e.key === 'Enter') confirmCreate(); });
    document.getElementById('widgetTitle')?.addEventListener('keydown', e => { if (e.key === 'Enter') confirmAddWidget(); });
}

// ── LIST MODE ────────────────────────────────────────────────────────────
async function loadList() {
    const pageHeader = document.getElementById('pageHeader'); if (pageHeader) pageHeader.style.display = '';
    document.getElementById('dashMode').style.display = 'none';
    document.getElementById('listMode').style.display = '';
    const list = await api('List');
    const g = document.getElementById('dashGrid'), e = document.getElementById('emptyDashboards');
    if (!list || list.length === 0) { g.style.display = 'none'; e.style.display = 'flex'; return; }
    const favs = pw_fav.get('pw_fav_dashboards');
    g.innerHTML = list.map(d => {
        const wc = d.widgets?.length || 0;
        const ago = timeAgo(d.updatedAt);
        const isFav = favs.includes(d.id);
        return `<div class="dash-card" onclick="location.href='/plswk/Dashboards/${d.id}/${slugify(d.name)}'">
            <div style="display:flex;align-items:center;gap:0.6rem;margin-bottom:0.75rem">
                <div style="width:36px;height:36px;border-radius:10px;background:rgba(56,189,248,0.1);display:flex;align-items:center;justify-content:center;flex-shrink:0">
                    <i class="fas fa-th-large" style="color:#38bdf8;font-size:1rem"></i>
                </div>
                <div style="flex:1;min-width:0"><div class="dash-card-title">${esc(d.name)}</div></div>
                <button class="btn-icon ${isFav ? 'active' : ''}" onclick="event.stopPropagation(); toggleFavoriteDash('${d.id}')" title="Favorite" style="display:${window.pwCanEditFavorites ? 'flex' : 'none'}">
                    <i class="${isFav ? 'fas' : 'far'} fa-star" style="color:${isFav ? '#fbbf24' : 'inherit'}"></i>
                </button>
            </div>
            <div class="dash-card-desc">${esc(d.description || 'No description')}</div>
            <div class="dash-card-meta">
                <span><i class="fas fa-puzzle-piece" style="margin-right:0.3rem"></i>${wc} widget${wc !== 1 ? 's' : ''} · ${ago}</span>
                <div class="dash-card-actions" onclick="event.stopPropagation()" style="display:${window.pwCanEditDashboard ? 'flex' : 'none'}">
                    <button class="btn-ghost btn-sm" onclick="location.href='/plswk/Dashboards/${d.id}/${slugify(d.name)}?edit=true'"><i class="fas fa-pen"></i></button>
                    <button class="btn-ghost btn-sm btn-danger" onclick="deleteDash('${d.id}')"><i class="fas fa-trash"></i></button>
                </div>
            </div>
        </div>`;
    }).join('');
}

function toggleFavoriteDash(id) {
    let favs = pw_fav.get('pw_fav_dashboards');
    if (favs.includes(id)) favs = favs.filter(x => x !== id);
    else favs.push(id);
    pw_fav.set('pw_fav_dashboards', favs);
    
    // Refresh UI
    if (document.getElementById('listMode').style.display !== 'none') loadList();
    else {
        const btn = document.getElementById('btnFavDash');
        if (btn) {
            const isFav = favs.includes(id);
            btn.classList.toggle('active', isFav);
            btn.querySelector('i').className = (isFav ? 'fas' : 'far') + ' fa-star';
            btn.querySelector('i').style.color = isFav ? '#fbbf24' : '';
        }
    }
}

function createDashboard() { document.getElementById('createModal').style.display = 'flex'; document.getElementById('newDashName').focus(); }
async function confirmCreate() {
    const name = document.getElementById('newDashName').value.trim();
    if (!name) return;
    const d = await fetch('/plswk/Dashboards?handler=Create', { method: 'POST', headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() }, body: JSON.stringify({ name, description: document.getElementById('newDashDesc').value }) }).then(r => r.json());
    location.href = `/plswk/Dashboards/${d.id}/${slugify(d.name)}?edit=true`;
}
async function deleteDash(id) {
    if (!confirm('Delete this dashboard?')) return;
    await fetch('/plswk/Dashboards?handler=Delete', { method: 'POST', headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() }, body: JSON.stringify({ id }) });
    loadList();
}

// ── DASHBOARD VIEW ────────────────────────────────────────────────────────
async function showDashboard() {
    const pageHeader = document.getElementById('pageHeader'); if (pageHeader) pageHeader.style.display = 'none';
    document.getElementById('listMode').style.display = 'none';
    document.getElementById('dashMode').style.display = '';
    document.getElementById('dashTitleView').textContent = dashboard.name;
    document.getElementById('dashTitle').value = dashboard.name;
    const sep = document.getElementById('dashBreadcrumbSep'); if (sep) sep.style.display = '';
    
    // Init favorite star
    const favs = pw_fav.get('pw_fav_dashboards');
    const isFav = favs.includes(dashboard.id);
    const btn = document.getElementById('btnFavDash');
    if (btn) {
        btn.classList.toggle('active', isFav);
        btn.querySelector('i').className = (isFav ? 'fas' : 'far') + ' fa-star';
        btn.querySelector('i').style.color = isFav ? '#fbbf24' : '';
        btn.style.display = window.pwCanEditFavorites ? 'flex' : 'none';
    }
    // Pre-fetch key metadata so widgets can show friendly names
    if (!allKeys.length) { try { allKeys = await api('AvailableDataPoints'); } catch (e) { allKeys = []; } }
    initGrid();
    if (dashboard.widgets?.length) renderAllWidgets();
    else document.getElementById('emptyDash').style.display = 'flex';
    startPolling();
}

function initGrid() {
    if (grid) grid.destroy(false);
    grid = GridStack.init({ column: 12, cellHeight: 80, margin: 8, disableResize: true, disableDrag: true, float: true }, '#dashGrid2');
    grid.on('resizestop', function (event, el) {
        const id = el.getAttribute('gs-id');
        if (charts[id]) {
            setTimeout(() => charts[id].windowResize(), 50);
        }
    });
}

function enterEditMode() {
    isEditing = true;
    if (grid) { grid.enableMove(true); grid.enableResize(true); }
    document.getElementById('dashTitle').style.display = ''; document.getElementById('dashTitleView').style.display = 'none';
    document.getElementById('btnEdit').style.display = 'none';
    document.getElementById('btnSave').style.display = ''; document.getElementById('btnCancel').style.display = ''; document.getElementById('btnAddWidget').style.display = '';
    document.querySelectorAll('.widget-actions').forEach(e => e.style.display = 'flex');
    document.getElementById('emptyDash').style.display = dashboard.widgets?.length ? 'none' : 'flex';
    // Raise bg layer above grid so SVG widgets can be dragged
    const bgLayer = document.getElementById('scadaBg');
    if (bgLayer) bgLayer.classList.add('edit-mode');
    document.querySelectorAll('.scada-point').forEach(el => {
        el.classList.add('edit-mode');
        const wid = el.dataset.wid;
        const w = dashboard.widgets.find(x => x.id === wid);
        if (w) initScadaPointDrag(el, w);
    });
    document.querySelectorAll('.scada-svg-widget').forEach(el => {
        el.classList.add('edit-mode');
        const wid = el.dataset.wid;
        const w = dashboard.widgets.find(x => x.id === wid);
        if (w) initSvgWidgetDrag(el, w);
    });
}

function cancelEdit() { location.href = `/plswk/Dashboards/${dashboard.id}/${slugify(dashboard.name)}`; }

async function saveDashboard() {
    dashboard.name = document.getElementById('dashTitle').value.trim() || dashboard.name;
    const r = dashTw ? dashTw.getRange() : {};
    dashboard.timewindow = { mode: r.mode || 'realtime', realtimeMs: r.realtimeMs || 3600000 };
    // Sync widget positions from grid
    const items = grid.getGridItems();
    items.forEach(el => {
        const wid = el.getAttribute('gs-id');
        const w = dashboard.widgets.find(x => x.id === wid);
        if (w) { w.x = parseInt(el.getAttribute('gs-x')) || 0; w.y = parseInt(el.getAttribute('gs-y')) || 0; w.w = parseInt(el.getAttribute('gs-w')) || 6; w.h = parseInt(el.getAttribute('gs-h')) || 4; }
    });
    // Note: scada-point positions are already synced to config by the drag handler's onUp
    await fetch('/plswk/Dashboards?handler=Save', { method: 'POST', headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() }, body: JSON.stringify(dashboard) });
    location.href = `/plswk/Dashboards/${dashboard.id}/${slugify(dashboard.name)}`;
}

// ── POLLING ──────────────────────────────────────────────────────────────
function startPolling() { if (pollTimer) clearInterval(pollTimer); pollTimer = setInterval(refreshAllWidgets, 10000); }
function refreshAllWidgets() {
    if (!dashboard?.widgets) return;
    dashboard.widgets.forEach(w => {
        if (w.type === 'timeseries' && (!dashTw || dashTw.mode === 'realtime')) renderTimeseries(w, document.getElementById('wb_' + w.id), w.config || {});
        else if (w.type === 'latest-values') updateLatestValues(w, w.config || {});
        else if (w.type === 'single-value') updateSingleValue(w, w.config || {});
    });
    updateAllScadaPoints();
}

// ── HELPERS ──────────────────────────────────────────────────────────────
// esc() and friendlyName() are provided by base.js
function keyName(key) { const parts = key.split('_'); return parts.length > 2 ? parts.slice(1, -1).join(' ') : key; }
function hexToRgba(hex, a) { const r = parseInt(hex.slice(1, 3), 16), g = parseInt(hex.slice(3, 5), 16), b = parseInt(hex.slice(5, 7), 16); return `rgba(${r},${g},${b},${a})`; }
function timeAgo(isoStr) {
    if (!isoStr) return '';
    const diff = Date.now() - new Date(isoStr).getTime();
    if (diff < 60000) return 'just now';
    if (diff < 3600000) return Math.floor(diff / 60000) + ' min ago';
    if (diff < 86400000) return Math.floor(diff / 3600000) + ' hr ago';
    return Math.floor(diff / 86400000) + ' days ago';
}

function slugify(text) {
    return text.toString().toLowerCase()
        .replace(/\s+/g, '-')           // Replace spaces with -
        .replace(/[^\w\-]+/g, '')       // Remove all non-word chars
        .replace(/\-\-+/g, '-')         // Replace multiple - with single -
        .replace(/^-+/, '')             // Trim - from start of text
        .replace(/-+$/, '');            // Trim - from end of text
}

function updateHistoryLiveValue(key, value) {
    if (typeof currentHistoryKey !== 'undefined' && currentHistoryKey === key) {
        const hlv = document.getElementById('chartLiveValue');
        if (hlv && document.getElementById('historyModal')?.style.display === 'flex') {
            hlv.textContent = value;
        }
    }
}
