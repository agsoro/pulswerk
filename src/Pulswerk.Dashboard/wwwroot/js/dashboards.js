// dashboards.js â€“ Custom dashboard engine
const COLORS = [
    '#38bdf8', '#f59e0b', '#10b981', '#a78bfa', '#f472b6',  // sky, amber, emerald, violet, pink
    '#fb923c', '#34d399', '#60a5fa', '#e879f9', '#fbbf24',  // orange, teal, blue, fuchsia, yellow
    '#22d3ee', '#ef4444', '#84cc16', '#c084fc', '#f97316',  // cyan, red, lime, purple, orange-deep
    '#2dd4bf', '#818cf8', '#facc15', '#ec4899', '#14b8a6',  // teal-bright, indigo, yellow-warm, pink-hot, teal-dark
];
let grid = null, dashboard = null, isEditing = false, charts = {}, pollTimer = null, pendingSvgContent = '';
let dashTw = null; // reusable TW module instance
const pendingRenders = new Set();  // guard against overlapping async renders
const token = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
const api = async (handler, opts) => { const r = await fetch(`/plswk/Dashboards?handler=${handler}`, opts); return r.json(); };

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

// â”€â”€ LIST MODE â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
async function loadList() {
    const pageHeader = document.getElementById('pageHeader'); if (pageHeader) pageHeader.style.display = '';
    document.getElementById('dashMode').style.display = 'none';
    document.getElementById('listMode').style.display = '';
    const list = await api('List');
    const g = document.getElementById('dashGrid'), e = document.getElementById('emptyDashboards');
    if (!list || list.length === 0) { g.style.display = 'none'; e.style.display = 'flex'; return; }
    g.style.display = 'grid'; e.style.display = 'none';
    g.innerHTML = list.map(d => {
        const wc = d.widgets?.length || 0;
        const ago = timeAgo(d.updatedAt);
        return `<div class="dash-card" onclick="location.href='/plswk/Dashboards/${d.id}/${slugify(d.name)}'">
            <div style="display:flex;align-items:center;gap:0.6rem;margin-bottom:0.75rem">
                <div style="width:36px;height:36px;border-radius:10px;background:rgba(56,189,248,0.1);display:flex;align-items:center;justify-content:center;flex-shrink:0">
                    <i class="fas fa-th-large" style="color:#38bdf8;font-size:1rem"></i>
                </div>
                <div style="flex:1;min-width:0"><div class="dash-card-title">${esc(d.name)}</div></div>
            </div>
            <div class="dash-card-desc">${esc(d.description || 'No description')}</div>
            <div class="dash-card-meta">
                <span><i class="fas fa-puzzle-piece" style="margin-right:0.3rem"></i>${wc} widget${wc !== 1 ? 's' : ''} Â· ${ago}</span>
                <div class="dash-card-actions" onclick="event.stopPropagation()">
                    <button class="btn-ghost btn-sm" onclick="location.href='/plswk/Dashboards/${d.id}/${slugify(d.name)}?edit=true'"><i class="fas fa-pen"></i></button>
                    <button class="btn-ghost btn-sm btn-danger" onclick="deleteDash('${d.id}')"><i class="fas fa-trash"></i></button>
                </div>
            </div>
        </div>`;
    }).join('');
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

// â”€â”€ DASHBOARD VIEW â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
async function showDashboard() {
    const pageHeader = document.getElementById('pageHeader'); if (pageHeader) pageHeader.style.display = 'none';
    document.getElementById('listMode').style.display = 'none';
    document.getElementById('dashMode').style.display = '';
    document.getElementById('dashTitleView').textContent = dashboard.name;
    document.getElementById('dashTitle').value = dashboard.name;
    const sep = document.getElementById('dashBreadcrumbSep'); if (sep) sep.style.display = '';
    // Pre-fetch key metadata so widgets can show friendly names
    if (!allKeys.length) { try { allKeys = await api('AvailableKeys'); } catch (e) { allKeys = []; } }
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

// â”€â”€ WIDGET RENDERING â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
function renderAllWidgets() {
    grid.removeAll(); charts = {};
    const bgLayer = document.getElementById('scadaBg'); if (bgLayer) bgLayer.innerHTML = '';
    const ptLayer = document.getElementById('scadaPoints'); if (ptLayer) ptLayer.innerHTML = '';
    dashboard.widgets.forEach(w => {
        if (w.type === 'background-svg') renderBackgroundSvg(w);
        else if (w.type === 'scada-point') renderScadaPoint(w);
        else addWidgetToGrid(w);
    });
}

const WTYPE_ICONS = { 'timeseries': 'fa-chart-line', 'latest-values': 'fa-table', 'single-value': 'fa-digital-tachograph', 'scada-point': 'fa-map-pin', 'background-svg': 'fa-drafting-compass' };
function addWidgetToGrid(w) {
    document.getElementById('emptyDash').style.display = 'none';
    const el = document.createElement('div');
    el.className = 'grid-stack-item';
    const icon = WTYPE_ICONS[w.type] || 'fa-puzzle-piece';
    el.innerHTML = `<div class="grid-stack-item-content">
        <div class="widget-header">
            <i class="fas ${icon} widget-type-icon"></i>
            <span class="widget-title">${esc(t(w.title))}</span>
            <div class="widget-actions" style="display:${isEditing ? 'flex' : 'none'}">
                <button title="Configure" onclick="editWidget('${w.id}')"><i class="fas fa-cog"></i></button>
                <button title="Duplicate" onclick="duplicateWidget('${w.id}')"><i class="fas fa-copy"></i></button>
                <button title="Remove" onclick="removeWidget('${w.id}')"><i class="fas fa-trash"></i></button>
            </div>
        </div>
        <div class="widget-body" id="wb_${w.id}"></div>
    </div>`;
    grid.addWidget(el, { id: w.id, x: w.x, y: w.y, w: w.w, h: w.h, minW: 3, minH: 2 });
    renderWidgetContent(w);
}

function renderWidgetContent(w) {
    const body = document.getElementById('wb_' + w.id);
    if (!body) return;
    const cfg = w.config || {};
    if (w.type === 'timeseries') renderTimeseries(w, body, cfg);
    else if (w.type === 'latest-values') renderLatestValues(w, body, cfg);
    else if (w.type === 'single-value') renderSingleValue(w, body, cfg);
}

async function renderTimeseries(w, body, cfg) {
    const keys = cfg.keys || [];
    if (!keys.length) { body.innerHTML = '<div class="empty-state" style="padding:1rem"><p style="font-size:0.8rem">No keys configured</p></div>'; return; }

    // Guard against overlapping async renders for the same widget
    if (pendingRenders.has(w.id)) return;
    pendingRenders.add(w.id);

    const { startTs, endTs } = getTimeRange();
    let data;
    try { data = await api(`WidgetData&keys=${keys.join(',')}&startTs=${startTs}&endTs=${endTs}`); } catch (e) { pendingRenders.delete(w.id); return; }
    pendingRenders.delete(w.id);

    const many = keys.length > 5;  // threshold for "dense" chart mode
    const series = [], colors = [];
    keys.forEach((key, i) => {
        const color = COLORS[i % COLORS.length];
        const raw = data?.[key] || [];
        const points = raw.map(p => ({
            x: typeof p.ts === 'number' ? p.ts : new Date(p.ts).getTime(),
            y: p.value != null ? parseFloat(parseFloat(p.value).toFixed(2)) : NaN
        })).filter(p => !isNaN(p.y));

        const meta = allKeys.find(k => k.key === key);
        series.push({ name: meta?.fullName || friendlyName(key), data: points });
        colors.push(color);
    });

    // Update existing chart if it still has a valid DOM element
    const existingChart = charts[w.id];
    if (existingChart) {
        try {
            // Verify the chart's container is still in the DOM
            const chartEl = document.getElementById('chart_' + w.id);
            if (chartEl && chartEl.querySelector('.apexcharts-canvas')) {
                existingChart.updateOptions({
                    xaxis: {
                        type: 'datetime', min: startTs, max: endTs,
                        labels: { style: { colors: '#64748b', fontSize: '10px' } },
                        axisBorder: { show: false }, axisTicks: { show: false }
                    },
                    series: series
                }, false, true);
                return;
            }
            // Chart container was destroyed â€” clean up and recreate
            existingChart.destroy();
        } catch (e) { /* destroyed chart, ignore */ }
        delete charts[w.id];
    }

    // Calculate the actual available height in pixels for reliable rendering
    const bodyRect = body.getBoundingClientRect();
    const chartHeight = Math.max(bodyRect.height - 10, 150);  // fallback min 150px

    body.innerHTML = '<div style="flex:1;min-height:0;position:relative"><div id="chart_' + w.id + '" style="width:100%;height:' + chartHeight + 'px"></div></div>';

    // â”€â”€ Chart config adapts to series count â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    const useArea = !many && cfg.chartType !== 'bar';
    const options = {
        series: series,
        chart: {
            type: useArea ? 'area' : (cfg.chartType === 'bar' ? 'bar' : 'line'),
            height: chartHeight,
            fontFamily: 'Inter, sans-serif',
            animations: { enabled: !many, easing: 'easeinout', speed: 400 },
            toolbar: { show: false },
            sparkline: { enabled: false },
            stacked: !!cfg.stacked,
        },
        colors: colors,
        stroke: {
            curve: 'straight',
            width: many ? 1.5 : 2,
        },
        fill: useArea ? {
            type: 'gradient',
            gradient: {
                shadeIntensity: 1, opacityFrom: 0.45, opacityTo: 0.05,
                stops: [0, 90, 100]
            }
        } : { opacity: 0 },
        dataLabels: { enabled: false },
        grid: {
            show: true, borderColor: 'rgba(255,255,255,0.05)',
            xaxis: { lines: { show: false } },
            yaxis: { lines: { show: true } },
            padding: { top: 0, right: 0, bottom: 0, left: 10 }
        },
        xaxis: {
            type: 'datetime',
            min: startTs, max: endTs,
            labels: { style: { colors: '#64748b', fontSize: '10px' } },
            axisBorder: { show: false },
            axisTicks: { show: false },
        },
        yaxis: {
            labels: { style: { colors: '#94a3b8', fontSize: '10px' } }
        },
        legend: {
            show: cfg.showLegend !== false, position: 'top', horizontalAlign: 'right',
            fontSize: many ? '10px' : '11px',
            labels: { colors: '#94a3b8' },
            markers: { width: many ? 6 : 8, height: many ? 6 : 8, radius: 12 },
            itemMargin: { horizontal: many ? 4 : 8, vertical: 1 },
        },
        tooltip: {
            theme: 'dark', x: { format: 'dd MMM HH:mm:ss' },
            y: { formatter: (v) => formatNumber(v, 2) },
            shared: !many,          // individual tooltips when dense
            intersect: many,
        }
    };

    const chartEl = document.getElementById('chart_' + w.id);
    if (!chartEl) return;  // safety: body may have been replaced by another widget
    const chart = new ApexCharts(chartEl, options);
    charts[w.id] = chart;  // register BEFORE render to prevent concurrent creation
    chart.render().then(() => {
        setTimeout(() => chart.windowResize?.(), 200);
    }).catch(e => {
        console.error('Chart render failed for', w.id, e);
        delete charts[w.id];
    });
}

async function renderLatestValues(w, body, cfg) {
    const keys = cfg.keys || [];
    if (!keys.length) { body.innerHTML = '<div class="empty-state" style="padding:1rem"><p style="font-size:0.8rem">No keys configured</p></div>'; return; }
    body.innerHTML = '<div style="overflow:auto;height:100%"><table class="lv-table"><thead><tr><th>Name</th><th>Value</th><th style="width:50px">Trend</th></tr></thead><tbody id="lvt_' + w.id + '"></tbody></table></div>';
    await updateLatestValues(w, cfg);
}

async function updateLatestValues(w, cfg) {
    const keys = cfg.keys || [];
    if (!keys.length) return;
    let data;
    try { data = await api(`LatestValues&keys=${keys.join(',')}`); } catch (e) { return; }
    const tbody = document.getElementById('lvt_' + w.id);
    if (!tbody) return;
    const keyMeta = allKeys.reduce((m, k) => { m[k.key] = k; return m; }, {});
    tbody.innerHTML = keys.map((key, i) => {
        const val = data?.[key] || '---';
        const meta = keyMeta[key] || {};
        const color = COLORS[i % COLORS.length];
        const displayVal = PulswerkValue.formatDisplay(val, meta.type);
        updateHistoryLiveValue(key, displayVal);
        return `<tr>
            <td><span style="display:inline-block;width:8px;height:8px;border-radius:50%;background:${color};margin-right:0.5rem"></span>${esc(friendlyName(key))}</td>
            <td><span class="lv-value" data-key="${key}">${esc(displayVal)}</span><span class="lv-units">${esc(meta.units || '')}</span></td>
            <td>${isNum ? miniSparkSvg(numVal, color) : ''}</td>
        </tr>`;
    }).join('');
}

async function renderSingleValue(w, body, cfg) {
    const key = cfg.key || (cfg.keys?.[0]) || '';
    if (!key) { body.innerHTML = `<div class="empty-state" style="padding:1rem"><p style="font-size:0.8rem">${t('no_key')}</p></div>`; return; }
    const meta = allKeys.find(k => k.key === key) || {};
    const icon = typeof getPointIcon === 'function' ? getPointIcon(meta.type || '') : '<i class="fas fa-microchip"></i>';
    const safeName = (meta.name || friendlyName(key)).replace(/'/g, "\\'");
    const safeFullName = (meta.fullName || '').replace(/'/g, "\\'");
    const safeUnits = (meta.units || '').replace(/'/g, "\\'");

    // Build clickable breadcrumb path from parentPath array (same as Favorites)
    const pp = meta.parentPath || [];
    const safePathEnc = encodeURIComponent(JSON.stringify(pp));
    const pathHtml = pp.map((p, i) => {
        const link = `<a href="/plswk/Assets?node=${p.id}" style="color:inherit;text-decoration:none">${esc(p.name)}</a>`;
        const sep = i < pp.length - 1 ? '<i class="fas fa-chevron-right" style="margin:0 0.4rem;font-size:0.55rem;opacity:0.4"></i>' : '';
        return link + sep;
    }).join('');

    const safeEnumValues = encodeURIComponent(JSON.stringify(meta.enumValues || null));
    const keyJs = key.replace(/'/g, "\\'");

    body.innerHTML = `<div class="sv-card">
        <div class="sv-card-path">${pathHtml || '<span style="opacity:0.4">â€”</span>'}</div>
        <div class="sv-card-body">
            <div class="sv-card-icon">${icon}</div>
            <div class="sv-card-info">
                <a href="/plswk/Assets?node=${meta.parentId || ''}" class="sv-card-name" style="text-decoration:none;color:#fff;display:block">${esc(meta.name || friendlyName(key))}</a>
                <div class="sv-card-fullname">${esc(meta.fullName || key)}</div>
            </div>
            <div class="sv-card-valbox">
                <span class="sv-card-val" id="svv_${w.id}" data-key="${key}">---</span>
                <span class="sv-card-units">${esc(meta.units || '')}</span>
            </div>
        </div>
        <div class="sv-card-actions">
            <button class="btn-icon" title="Trend" onclick="openHistory('${keyJs}')"><i class="fas fa-chart-area"></i></button>
            ${meta.isWritable ? `<button class="btn-icon" title="Edit Value" onclick="openEdit('${keyJs}')"><i class="fas fa-pen"></i></button>` : ''}
            <button class="btn-icon" title="Properties" onclick="openProperties('${keyJs}')"><i class="fas fa-cog"></i></button>
        </div>
    </div>`;
    await updateSingleValue(w, cfg);
}

async function updateSingleValue(w, cfg) {
    const key = cfg.key || (cfg.keys?.[0]) || '';
    if (!key) return;
    let data; try { data = await api(`LatestValues&keys=${key}`); } catch (e) { return; }
    const el = document.getElementById('svv_' + w.id);
    if (el) {
        const val = data?.[key] || '---';
        const meta = allKeys.find(k => k.key === key) || {};
        const display = PulswerkValue.formatDisplay(val, meta.type);
        if (el.textContent !== display) el.textContent = display;
        updateHistoryLiveValue(key, display);
    }
}

function miniSparkSvg(val, color) {
    // Tiny inline spark indicator
    const h = 16, w = 32;
    return `<svg width="${w}" height="${h}" style="vertical-align:middle"><rect x="0" y="${h / 4}" width="${w}" height="${h / 2}" rx="2" fill="${hexToRgba(color, 0.15)}"/><rect x="0" y="${h / 4}" width="${Math.min(w, Math.max(4, w * Math.abs(val % 100) / 100))}" height="${h / 2}" rx="2" fill="${color}" opacity="0.6"/></svg>`;
}

// â”€â”€ TIMEWINDOW â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
function getTimeRange() {
    if (!dashTw) { const now = Date.now(); return { startTs: now - 3600000, endTs: now }; }
    const r = dashTw.getRange(); return { startTs: r.startTs, endTs: r.endTs };
}

// â”€â”€ ADD/EDIT WIDGET â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
let selectedType = 'timeseries', editingWidgetId = null;
async function openAddWidget() {
    editingWidgetId = null;
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
    document.getElementById('addWidgetModal').style.display = 'flex';
}
function editWidget(wid) {
    const w = dashboard.widgets.find(x => x.id === wid); if (!w) return;
    editingWidgetId = wid;
    document.getElementById('widgetModalTitle').textContent = 'Edit Widget';
    document.getElementById('widgetConfirmText').textContent = 'Save Changes';
    document.getElementById('widgetTitle').value = w.title;
    selectWidgetType(document.querySelector(`.wtype-card[data-type="${w.type}"]`));
    const cfg = w.config || {};
    document.getElementById('optChartType').value = cfg.chartType || 'line';
    document.getElementById('optStacked').checked = !!cfg.stacked;
    document.getElementById('optLegend').checked = cfg.showLegend !== false;
    // Restore layout option for scada-point
    const layoutRadio = document.querySelector(`input[name="pointLayout"][value="${cfg.layout || 'vertical'}"]`);
    if (layoutRadio) layoutRadio.checked = true;
    // Restore anchor SVG (after selectWidgetType populates the dropdown)

    // Restore SVG color
    if (w.type === 'background-svg' && cfg.color) document.getElementById('svgColor').value = cfg.color;
    loadKeyPicker().then(() => {
        const keys = cfg.keys || []; if (cfg.key) keys.push(cfg.key);
        document.querySelectorAll('#keyList .key-item input').forEach(cb => { cb.checked = keys.includes(cb.value); cb.closest('.key-item').classList.toggle('selected', cb.checked); });
    });
    document.getElementById('addWidgetModal').style.display = 'flex';
}
function closeAddWidget() { document.getElementById('addWidgetModal').style.display = 'none'; }
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
async function loadKeyPicker() {
    if (!allKeys.length) { try { allKeys = await api('AvailableKeys'); } catch (e) { allKeys = []; } }
    renderKeyList(allKeys);
}
function renderKeyList(keys) {
    const list = document.getElementById('keyList');
    const multi = selectedType !== 'single-value';
    list.innerHTML = keys.map(k => `<label class="key-item" data-search="${(k.name + ' ' + k.path + ' ' + k.key).toLowerCase()}">
        <input type="${multi ? 'checkbox' : 'radio'}" name="wkey" value="${k.key}" onchange="handleKeyToggle(this)">
        <div style="min-width:0;flex:1"><div class="key-name">${esc(t(k.name))}</div><div class="key-path">${esc(k.path)}</div></div>
        <span class="key-val">${esc(k.value)} ${esc(k.units)}</span>
    </label>`).join('');
}
function handleKeyToggle(input) {
    if (input.type === 'radio') {
        document.querySelectorAll('#keyList .key-item').forEach(el => el.classList.remove('selected'));
        input.closest('.key-item').classList.add('selected');
    } else {
        input.closest('.key-item').classList.toggle('selected', input.checked);
    }
}
function filterKeys() {
    const raw = document.getElementById('keySearch').value.toLowerCase();
    const terms = raw.split(/\s+/).filter(t => t.length > 0);
    const selectedOnly = document.getElementById('keyShowSelected')?.checked || false;
    document.querySelectorAll('#keyList .key-item').forEach(el => {
        const search = el.dataset.search;
        const matchesSearch = terms.length === 0 || terms.every(t => search.includes(t));
        const matchesSelected = !selectedOnly || el.querySelector('input')?.checked;
        el.style.display = (matchesSearch && matchesSelected) ? '' : 'none';
    });
}
function confirmAddWidget() {
    const title = document.getElementById('widgetTitle').value.trim() || 'Untitled';

    // Background SVG â€” no keys needed
    if (selectedType === 'background-svg') {
        const svgSource = document.querySelector('input[name="svgSource"]:checked')?.value || 'drawio';
        const color = document.getElementById('svgColor')?.value || '#38bdf8';

        if (svgSource === 'drawio') {
            // Close modal first, then open draw.io â€” on save, create the widget
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

        // Import mode â€” require uploaded file
        if (!pendingSvgContent) { alert('Please upload an SVG file.'); return; }
        const w = { id: 'w' + Date.now().toString(36), type: 'background-svg', title: title || 'Background', x: 0, y: 0, w: 12, h: 8, config: { svg: pendingSvgContent, color, posX: 5, posY: 5, posW: 90, posH: 90 } };
        dashboard.widgets = dashboard.widgets || []; dashboard.widgets.push(w);
        renderBackgroundSvg(w);
        closeAddWidget(); return;
    }

    const checked = [...document.querySelectorAll('#keyList input:checked')].map(c => c.value);
    if (!checked.length) { alert('Select at least one data key.'); return; }

    // SCADA data point â€” freely positioned, multi-key
    if (selectedType === 'scada-point') {
        const layout = document.querySelector('input[name="pointLayout"]:checked')?.value || 'vertical';
        const cfg = { keys: checked, posX: 50, posY: 50, layout };
        if (editingWidgetId) {
            const w = dashboard.widgets.find(x => x.id === editingWidgetId);
            if (w) {
                w.title = title; w.config.keys = checked; w.config.layout = layout;
                // Re-render so changes are visible immediately
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

// â”€â”€ POLLING â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

// â”€â”€ HELPERS â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

// â”€â”€ SCADA: BACKGROUND SVG â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
function svgColorFilter(hex) {
    // Convert hex color to a CSS filter that tints a black SVG to the target color
    const r = parseInt(hex.slice(1, 3), 16) / 255, g = parseInt(hex.slice(3, 5), 16) / 255, b = parseInt(hex.slice(5, 7), 16) / 255;
    // Use brightness+sepia+hue-rotate+saturate to approximate target color
    const max = Math.max(r, g, b), min = Math.min(r, g, b), l = (max + min) / 2;
    let h = 0, s = 0;
    if (max !== min) {
        const d = max - min; s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        if (max === r) h = ((g - b) / d + (g < b ? 6 : 0)) / 6;
        else if (max === g) h = ((b - r) / d + 2) / 6;
        else h = ((r - g) / d + 4) / 6;
    }
    const brightness = Math.max(0.3, l * 1.8);
    return `brightness(0) saturate(100%) invert(${Math.round(l * 100)}%) sepia(80%) saturate(${Math.round(s * 500)}%) hue-rotate(${Math.round(h * 360)}deg) brightness(${brightness.toFixed(2)})`;
}

function renderBackgroundSvg(w) {
    const bg = document.getElementById('scadaBg'); if (!bg) return;
    const cfg = w.config || {};
    if (!cfg.svg) return;

    const canvas = document.getElementById('dashCanvas');
    const cw = canvas.getBoundingClientRect().width;

    const el = document.createElement('div');
    el.className = 'scada-svg-widget' + (isEditing ? ' edit-mode' : '');
    el.dataset.wid = w.id;
    // All positions relative to canvas WIDTH to avoid height feedback loop
    el.style.left = (cfg.posX ?? 5) + '%';
    el.style.top = ((cfg.posY ?? 5) / 100 * cw) + 'px';
    el.style.width = (cfg.posW ?? 90) + '%';
    // No explicit height — SVG viewBox determines it
    // Store base width for zoom calculation (set once at first render)
    if (!cfg.baseW) { cfg.baseW = cfg.posW || 90; cfg.zoom = 100; }

    el.innerHTML = cfg.svg;
    const svg = el.querySelector('svg');
    if (svg) {
        svg.removeAttribute('width'); svg.removeAttribute('height');
        svg.style.width = '100%';
        svg.style.display = 'block'; // removes inline baseline gap
        svg.style.pointerEvents = 'none';
    }
    // Unified toolbar (visible only when selected in edit mode)
    const toolbar = document.createElement('div');
    toolbar.className = 'svg-toolbar';
    toolbar.innerHTML = `<button class="svg-zoom-btn" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();setSvgZoom('${w.id}',(parseInt(this.parentNode.querySelector('.svg-zoom-input').value)||100)-10)" title="Zoom out"><i class="fas fa-minus"></i></button>
        <input type="number" class="svg-zoom-input" value="${cfg.zoom || 100}" min="10" max="500" step="10"
            onmousedown="event.stopPropagation()" onclick="event.stopPropagation()"
            oninput="setSvgZoom('${w.id}',this.value)">
        <span style="color:#64748b;font-size:0.65rem">%</span>
        <button class="svg-zoom-btn" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();setSvgZoom('${w.id}',(parseInt(this.parentNode.querySelector('.svg-zoom-input').value)||100)+10)" title="Zoom in"><i class="fas fa-plus"></i></button>
        <button class="svg-zoom-btn" style="font-size:0.6rem;font-weight:700;letter-spacing:-0.5px" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();setSvgZoom('${w.id}',100)" title="Reset to 1:1">1:1</button>
        <span style="width:1px;height:14px;background:rgba(255,255,255,0.1);margin:0 2px"></span>
        <button class="svg-zoom-btn" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();editBackgroundSvg('${w.id}')" title="Edit in draw.io"><i class="fas fa-drafting-compass"></i></button>
        <button class="svg-zoom-btn" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();duplicateWidget('${w.id}')" title="Duplicate"><i class="fas fa-copy"></i></button>
        <button class="svg-zoom-btn sp-del" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();removeWidget('${w.id}')" title="Delete"><i class="fas fa-trash"></i></button>`;
    el.appendChild(toolbar);

    // Resize handle
    const rh = document.createElement('div');
    rh.className = 'svg-resize';
    el.appendChild(rh);

    // Click to select/deselect
    el.addEventListener('click', e => {
        if (!isEditing || e.target.closest('.svg-toolbar')) return;
        e.stopPropagation();
        const wasSelected = el.classList.contains('selected');
        // Deselect all others
        document.querySelectorAll('.scada-svg-widget.selected').forEach(s => s.classList.remove('selected'));
        if (!wasSelected) el.classList.add('selected');
    });

    bg.appendChild(el);
    if (isEditing) initSvgWidgetDrag(el, w);
    expandCanvasToFit();
}

// Expand canvas height so all SVG widgets are fully visible
function expandCanvasToFit() {
    const canvas = document.getElementById('dashCanvas'); if (!canvas) return;
    let maxBottom = 500; // minimum canvas height in px
    // Check actual rendered positions (height is auto from SVG viewBox)
    document.querySelectorAll('.scada-svg-widget').forEach(el => {
        const rect = el.getBoundingClientRect();
        const canvasRect = canvas.getBoundingClientRect();
        const bottom = rect.top - canvasRect.top + rect.height;
        maxBottom = Math.max(maxBottom, bottom + 40);
    });
    canvas.style.minHeight = Math.ceil(maxBottom) + 'px';
}

// Deselect SVG widgets when clicking outside
document.addEventListener('click', e => {
    if (!e.target.closest('.scada-svg-widget')) {
        document.querySelectorAll('.scada-svg-widget.selected').forEach(s => s.classList.remove('selected'));
    }
});

// Zoom = scale posW relative to baseW (height auto from SVG viewBox)
function setSvgZoom(wid, val) {
    const w = dashboard.widgets.find(x => x.id === wid); if (!w) return;
    const cfg = w.config;
    const newZoom = Math.max(10, Math.min(500, parseInt(val) || 100));
    const baseW = cfg.baseW || 90;
    cfg.posW = Math.round(baseW * newZoom / 100 * 10) / 10;
    cfg.zoom = newZoom;
    const el = document.querySelector(`.scada-svg-widget[data-wid="${wid}"]`);
    if (el) {
        el.style.width = cfg.posW + '%';
        const inp = el.querySelector('.svg-zoom-input');
        if (inp) inp.value = newZoom;
    }
    expandCanvasToFit();
}


function initSvgWidgetDrag(el, w) {
    const canvas = document.getElementById('dashCanvas');
    let startX, startY, startLeft, startTop, dragging = false;

    el.addEventListener('mousedown', e => {
        if (!isEditing || e.target.closest('.svg-toolbar') || e.target.closest('.svg-resize')) return;
        e.preventDefault(); dragging = true; el.classList.add('dragging');
        const cw = canvas.getBoundingClientRect().width;
        startX = e.clientX; startY = e.clientY;
        startLeft = parseFloat(el.style.left) || 5; startTop = (parseFloat(el.style.top) || 0) / cw * 100;
        const onMove = ev => {
            const r = canvas.getBoundingClientRect();
            const newLeft = startLeft + (ev.clientX - startX) / r.width * 100;
            const newTop = startTop + (ev.clientY - startY) / r.width * 100;
            el.style.left = Math.max(0, newLeft) + '%';
            el.style.top = (Math.max(0, newTop) / 100 * r.width) + 'px';
            repositionAnchoredPoints();
        };
        const onUp = () => {
            dragging = false; el.classList.remove('dragging');
            const r = canvas.getBoundingClientRect();
            w.config.posX = parseFloat(el.style.left);
            w.config.posY = parseFloat(el.style.top) / r.width * 100;
            document.removeEventListener('mousemove', onMove); document.removeEventListener('mouseup', onUp);
            expandCanvasToFit();
        };
        document.addEventListener('mousemove', onMove); document.addEventListener('mouseup', onUp);
    });

    // Resize handle (width only, height auto from SVG viewBox)
    const rh = el.querySelector('.svg-resize');
    if (rh) rh.addEventListener('mousedown', e => {
        e.preventDefault(); e.stopPropagation();
        const startW = parseFloat(el.style.width) || 90;
        const sx = e.clientX;
        const onMove = ev => {
            const r = canvas.getBoundingClientRect();
            const newW = Math.max(5, startW + (ev.clientX - sx) / r.width * 100);
            el.style.width = newW + '%';
            repositionAnchoredPoints();
        };
        const onUp = () => {
            w.config.posW = parseFloat(el.style.width);
            // Update zoom to reflect new size
            const baseW = w.config.baseW || 90;
            w.config.zoom = Math.round(w.config.posW / baseW * 100);
            const zoomInput = el.querySelector('.svg-zoom-input');
            if (zoomInput) zoomInput.value = w.config.zoom;
            expandCanvasToFit();
            document.removeEventListener('mousemove', onMove); document.removeEventListener('mouseup', onUp);
        };
        document.addEventListener('mousemove', onMove); document.addEventListener('mouseup', onUp);
    });
}

// â”€â”€ SCADA: DATA POINT RENDERING â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
function renderScadaPoint(w) {
    const layer = document.getElementById('scadaPoints'); if (!layer) return;
    const cfg = w.config || {};
    const keys = cfg.keys || [];
    if (!keys.length) return;
    const layout = cfg.layout || 'vertical';

    const el = document.createElement('div');
    el.className = 'scada-point layout-' + layout + (isEditing ? ' edit-mode' : '');
    el.dataset.wid = w.id;
    if (cfg.anchorSvg) el.dataset.anchorSvg = cfg.anchorSvg;

    // Edge anchor dots (visible when anchored to SVG)
    const edge = cfg.anchorEdge || '';
    const edgeDots = ['top', 'right', 'bottom', 'left'].map(d =>
        `<div class="sp-edge-dot sp-edge-${d}${d === edge ? ' active' : ''}" data-edge="${d}" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();setPointEdge('${w.id}','${d}',this)"></div>`
    ).join('');

    // Optional title header + action buttons (edit mode)
    const hasTitle = w.title && w.title !== 'Untitled';
    let html = edgeDots;
    if (hasTitle) {
        html += `<div style="font-size:0.72rem;font-weight:600;color:#cbd5e1;letter-spacing:0.02em;padding:0.2rem 0.35rem;margin-bottom:0.2rem;border-bottom:1px solid rgba(255,255,255,0.08);display:flex;align-items:center;gap:0.3rem;background:rgba(255,255,255,0.03);border-radius:4px 4px 0 0">
            <span style="flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${esc(w.title)}</span>
            <i class="fas fa-cog sp-action" title="Edit" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();editWidget('${w.id}')"></i>
            <i class="fas fa-copy sp-action" title="Duplicate" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();duplicateWidget('${w.id}')"></i>
            <i class="fas fa-trash sp-delete" title="Delete" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();removeWidget('${w.id}')"></i>
        </div>`;
    } else {
        html += `<div class="sp-action-bar" style="position:absolute;top:-8px;right:-6px;display:none;gap:2px;z-index:5">
            <i class="fas fa-cog sp-action-btn" title="Edit" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();editWidget('${w.id}')"></i>
            <i class="fas fa-copy sp-action-btn" title="Duplicate" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();duplicateWidget('${w.id}')"></i>
            <i class="fas fa-trash sp-action-btn sp-del" title="Delete" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();removeWidget('${w.id}')"></i>
        </div>`;
    }

    // Rows: each key = label | value+unit | info icon
    html += '<div class="sp-rows">';
    keys.forEach((key, i) => {
        const meta = allKeys.find(k => k.key === key) || {};
        const name = meta.name || friendlyName(key);
        const units = meta.units || '';
        const borderStyle = layout === 'vertical' && i < keys.length - 1 ? 'border-bottom:1px solid rgba(255,255,255,0.04);' : '';
        html += `<div class="sp-row" style="display:flex;align-items:center;gap:0.4rem;padding:0.15rem 0;${borderStyle}">
            <span style="flex:1;font-size:0.68rem;color:#94a3b8;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;max-width:120px">${esc(name)}</span>
            <span class="sp-val" data-key="${key}">---</span>
            ${units ? `<span class="sp-unit">${esc(units)}</span>` : ''}
            <i class="fas fa-info-circle sp-info" onclick="event.stopPropagation();showScadaPopup('${key}',this)"></i>
        </div>`;
    });
    html += '</div>';

    el.innerHTML = html;
    layer.appendChild(el);

    // Position AFTER element is in the DOM so getBoundingClientRect returns
    // real dimensions for the edge-dot offset calculation.
    // Use rAF to ensure browser has completed layout.
    requestAnimationFrame(() => positionScadaPoint(el, cfg));

    if (isEditing) initScadaPointDrag(el, w);

    // Fetch initial values
    updateScadaPointValues(w);
}

// Get the SVG content area within its container (skipping border)
function getSvgContentBox(containerEl) {
    const r = containerEl.getBoundingClientRect();
    const canvas = document.getElementById('dashCanvas');
    const cr = canvas ? canvas.getBoundingClientRect() : { left: 0, top: 0 };

    // Width of borders (clientLeft/Top are reliable integers)
    const bl = containerEl.clientLeft || 0, bt = containerEl.clientTop || 0;
    const br = containerEl.offsetWidth - containerEl.clientWidth - bl;
    const bb = containerEl.offsetHeight - containerEl.clientHeight - bt;

    return {
        left: r.left - cr.left + bl,
        top: r.top - cr.top + bt,
        width: r.width - bl - br,
        height: r.height - bt - bb
    };
}

// Pixel offset from widget center to the selected edge dot
function centerDotOffset(edge, w, h) {
    if (edge === 'top') return { x: 0, y: -h / 2 };
    if (edge === 'right') return { x: w / 2, y: 0 };
    if (edge === 'bottom') return { x: 0, y: h / 2 };
    if (edge === 'left') return { x: -w / 2, y: 0 };
    return { x: 0, y: 0 }; // no edge: posX/posY is the top-left
}

// Position a scada-point element relative to its anchor SVG
function positionScadaPoint(el, cfg) {
    const anchorId = cfg.anchorSvg;
    if (anchorId) {
        const svgEl = document.querySelector(`.scada-svg-widget[data-wid="${anchorId}"]`);
        if (svgEl) {
            const box = getSvgContentBox(svgEl);

            // posX/posY stores the edge-dot position as % of SVG box.
            // Subtract the edge offset to get the widget's top-left.
            const dotPx = box.left + (cfg.posX || 0) / 100 * box.width;
            const dotPy = box.top + (cfg.posY || 0) / 100 * box.height;
            const r = el.getBoundingClientRect();
            const off = centerDotOffset(cfg.anchorEdge || '', r.width, r.height);
            el.style.left = (dotPx - off.x) + 'px';
            el.style.top = (dotPy - off.y) + 'px';
            return;
        }
    }
    // Fallback: canvas-relative %
    el.style.left = (cfg.posX || 50) + '%';
    el.style.top = (cfg.posY || 50) + '%';
}

// Reposition all SVG-anchored points (call on resize/SVG move)
function repositionAnchoredPoints() {
    if (!dashboard?.widgets) return;
    document.querySelectorAll('.scada-point[data-anchor-svg]').forEach(el => {
        const w = dashboard.widgets.find(x => x.id === el.dataset.wid);
        if (w && w.config?.anchorSvg) positionScadaPoint(el, w.config);
    });
}

// Find the SVG widget that a scada-point overlaps with
function findOverlappingSvg(pointEl) {
    const pr = pointEl.getBoundingClientRect();
    const cx = pr.left + pr.width / 2, cy = pr.top + pr.height / 2;
    let best = null;
    document.querySelectorAll('.scada-svg-widget').forEach(svgEl => {
        const sr = svgEl.getBoundingClientRect();
        if (cx >= sr.left && cx <= sr.right && cy >= sr.top && cy <= sr.bottom) best = svgEl;
    });
    return best;
}

// Set/toggle edge anchor on a data point — auto-detects SVG underneath
function setPointEdge(wid, edge, dotEl) {
    const w = dashboard?.widgets?.find(x => x.id === wid);
    if (!w) return;
    const cfg = w.config || {};
    const parent = dotEl.closest('.scada-point');
    if (!parent) return;

    // Toggle: clicking the active edge unanchors
    if (cfg.anchorEdge === edge && cfg.anchorSvg) {
        cfg.anchorEdge = ''; cfg.anchorSvg = '';
        delete cfg.anchorW; delete cfg.anchorH;
        delete parent.dataset.anchorSvg;
    } else {
        // Find which SVG is underneath
        const svgEl = findOverlappingSvg(parent);
        if (!svgEl) { return; }
        const svgWid = svgEl.dataset.wid;
        cfg.anchorSvg = svgWid;
        cfg.anchorEdge = edge;
        parent.dataset.anchorSvg = svgWid;
        const box = getSvgContentBox(svgEl);
        // Store the edge-dot position (not widget top-left) as % of SVG box
        const off = centerDotOffset(edge, parent.offsetWidth, parent.offsetHeight);
        cfg.posX = (parent.offsetLeft + off.x - box.left) / box.width * 100;
        cfg.posY = (parent.offsetTop + off.y - box.top) / box.height * 100;
        cfg.anchorW = box.width;
        cfg.anchorH = box.height;
    }
    w.config = cfg;
    parent.querySelectorAll('.sp-edge-dot').forEach(d => d.classList.toggle('active', d.dataset.edge === cfg.anchorEdge));
}

// Update edge dot visibility after drag (show when overlapping SVG in edit mode)
function updateEdgeDotVisibility(pointEl) {
    if (!isEditing) return;
    const overSvg = !!findOverlappingSvg(pointEl);
    const anchored = !!pointEl.dataset.anchorSvg;
    pointEl.querySelectorAll('.sp-edge-dot').forEach(d => {
        d.style.display = (overSvg || anchored) ? 'block' : 'none';
    });
}

// Listen for window resize to reposition anchored points
window.addEventListener('resize', () => { repositionAnchoredPoints(); });


async function updateScadaPointValues(w) {
    const cfg = w.config || {};
    const keys = cfg.keys || [];
    if (!keys.length) return;
    let data;
    try { data = await api(`LatestValues&keys=${keys.join(',')}`); } catch (e) { return; }
    renderScadaValues(w.id, keys, data);
    // Values may change widget dimensions — reposition so edge offset is correct
    if (cfg.anchorSvg) {
        const el = document.querySelector(`.scada-point[data-wid="${w.id}"]`);
        if (el) requestAnimationFrame(() => positionScadaPoint(el, cfg));
    }
}

function renderScadaValues(wid, keys, data) {
    keys.forEach(key => {
        const el = document.querySelector(`.scada-point[data-wid="${wid}"] .sp-val[data-key="${key}"]`);
        if (!el) return;
        const val = data?.[key] || '---';
        const meta = allKeys.find(k => k.key === key) || {};
        const type = meta.type || '';
        const display = PulswerkValue.formatDisplay(val, type);

        if (PulswerkValue.isBinary(type)) {
            const isOn = display === '1' || ['on', 'ein', 'active', 'ja', 'yes'].includes(display.toLowerCase());
            el.innerHTML = `<span class="sp-dot" style="background:${isOn ? '#34d399' : '#64748b'}"></span><span class="sp-bool ${isOn ? 'on' : 'off'}">${esc(display)}</span>`;
        } else {
            el.textContent = display;
        }
        updateHistoryLiveValue(key, display);
    });
}

async function updateAllScadaPoints() {
    if (!dashboard?.widgets) return;
    const scadaWidgets = dashboard.widgets.filter(w => w.type === 'scada-point');
    const allScadaKeys = [...new Set(scadaWidgets.flatMap(w => (w.config?.keys || [])))];
    if (!allScadaKeys.length) return;
    let data;
    try { data = await api(`LatestValues&keys=${allScadaKeys.join(',')}`); } catch (e) { return; }
    scadaWidgets.forEach(w => {
        renderScadaValues(w.id, w.config?.keys || [], data);
    });
}

// â”€â”€ SCADA: INFO POPUP â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
async function showScadaPopup(key, triggerEl) {
    hideScadaPopup();
    const popup = document.getElementById('scadaPopup');
    const content = document.getElementById('scadaPopupContent');
    if (!popup || !content) return;

    // Ensure metadata is loaded
    if (!allKeys.length) { try { allKeys = await api('AvailableKeys'); } catch (e) { allKeys = []; } }
    const meta = allKeys.find(k => k.key === key) || {};
    const icon = typeof getPointIcon === 'function' ? getPointIcon(meta.type || '') : '<i class="fas fa-microchip"></i>';
    const keyJs = key.replace(/'/g, "\\'");

    const pp = meta.parentPath || [];
    const pathHtml = pp.map((p, i) => {
        const link = `<a href="/plswk/Assets?node=${p.id}" style="color:inherit;text-decoration:none">${esc(p.name)}</a>`;
        return link + (i < pp.length - 1 ? '<i class="fas fa-chevron-right" style="margin:0 0.4rem;font-size:0.55rem;opacity:0.4"></i>' : '');
    }).join('');

    // Fetch live value from API
    let currentVal = '---';
    try {
        const data = await api(`LatestValues&keys=${key}`);
        const raw = data?.[key];
        if (raw != null) currentVal = PulswerkValue.formatDisplay(raw, meta.type);
    } catch (e) { }

    content.innerHTML = `<div class="sv-card" style="height:auto">
        <div class="sv-card-path">${pathHtml || '<span style="opacity:0.4">\u2014</span>'}</div>
        <div class="sv-card-body">
            <div class="sv-card-icon">${icon}</div>
            <div class="sv-card-info">
                <div class="sv-card-name">${esc(meta.name || friendlyName(key))}</div>
                <div class="sv-card-fullname">${esc(meta.fullName || key)}</div>
            </div>
            <div class="sv-card-valbox">
                <span class="sv-card-val">${esc(currentVal)}</span>
                <span class="sv-card-units">${esc(meta.units || '')}</span>
            </div>
        </div>
        <div class="sv-card-actions">
            <button class="btn-icon" title="Trend" onclick="hideScadaPopup();openHistory('${keyJs}')"><i class="fas fa-chart-area"></i></button>
            ${meta.isWritable ? `<button class="btn-icon" title="Edit" onclick="hideScadaPopup();openEdit('${keyJs}')"><i class="fas fa-pen"></i></button>` : ''}
            <button class="btn-icon" title="Properties" onclick="hideScadaPopup();openProperties('${keyJs}')"><i class="fas fa-cog"></i></button>
        </div>
    </div>`;

    // Position near trigger element
    const rect = triggerEl.getBoundingClientRect();
    popup.style.left = Math.min(rect.right + 8, window.innerWidth - 360) + 'px';
    popup.style.top = Math.max(rect.top - 20, 8) + 'px';
    popup.style.display = 'block';
    popup.classList.add('show');

    setTimeout(() => {
        const closer = e => {
            if (!popup.contains(e.target)) { hideScadaPopup(); document.removeEventListener('click', closer); }
        };
        document.addEventListener('click', closer);
    }, 10);
}

function hideScadaPopup() {
    const popup = document.getElementById('scadaPopup');
    if (popup) { popup.style.display = 'none'; popup.classList.remove('show'); }
}

// â”€â”€ SCADA: DRAG POSITIONING â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
function initScadaPointDrag(el, w) {
    let startX, startY, startPx, startPy, dragging = false;
    const canvas = document.getElementById('dashCanvas');

    el.addEventListener('mousedown', e => {
        if (!isEditing || e.target.closest('.sp-info') || e.target.closest('.sp-delete') || e.target.closest('.sp-edge-dot')) return;
        e.preventDefault();
        dragging = true;
        el.classList.add('dragging');
        startX = e.clientX; startY = e.clientY;
        // Store start pixel position
        startPx = el.offsetLeft; startPy = el.offsetTop;

        const onMove = ev => {
            if (!dragging) return;
            const dx = ev.clientX - startX;
            const dy = ev.clientY - startY;
            el.style.left = (startPx + dx) + 'px';
            el.style.top = (startPy + dy) + 'px';
        };
        const onUp = () => {
            dragging = false;
            el.classList.remove('dragging');
            if (w) {
                const cfg = w.config || {};
                const anchorId = cfg.anchorSvg;
                if (anchorId) {
                    // Save as % relative to anchor SVG content area
                    const svgEl = document.querySelector(`.scada-svg-widget[data-wid="${anchorId}"]`);
                    if (svgEl) {
                        const box = getSvgContentBox(svgEl);
                        // Store the edge-dot position (not widget top-left)
                        const edge = cfg.anchorEdge || '';
                        const off = centerDotOffset(edge, el.offsetWidth, el.offsetHeight);
                        cfg.posX = (el.offsetLeft + off.x - box.left) / box.width * 100;
                        cfg.posY = (el.offsetTop + off.y - box.top) / box.height * 100;
                        cfg.anchorW = box.width;
                        cfg.anchorH = box.height;
                    }
                } else {
                    // Save as % of canvas
                    cfg.posX = el.offsetLeft / canvas.clientWidth * 100;
                    cfg.posY = el.offsetTop / canvas.clientHeight * 100;
                }
                w.config = cfg;
            }
            updateEdgeDotVisibility(el);
            document.removeEventListener('mousemove', onMove);
            document.removeEventListener('mouseup', onUp);
        };
        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup', onUp);
    });
}

// â”€â”€ SVG FILE HANDLING â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
function handleSvgFileSelect(input) {
    if (!input.files?.length) return;
    const reader = new FileReader();
    reader.onload = e => {
        pendingSvgContent = e.target.result;
        const preview = document.getElementById('svgPreviewThumb');
        const label = document.getElementById('svgDropLabel');
        if (preview) {
            preview.innerHTML = pendingSvgContent;
            const svg = preview.querySelector('svg');
            if (svg) {
                svg.style.width = '100%'; svg.style.height = 'auto'; svg.style.maxHeight = '160px';
            }
            preview.classList.remove('hidden');
        }
        if (label) label.classList.add('hidden');
    };
    reader.readAsText(input.files[0]);
}

// â”€â”€ DRAW.IO EMBED INTEGRATION â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
// Opens draw.io in a new browser tab. Communication via postMessage:
//   init â†’ load XML â†’ user edits â†’ save â†’ export as SVG â†’ update local
let drawioCallback = null, drawioWindow = null, _drawioPending = '', _drawioIsXml = false, _drawioExitAfterSave = false;

function openDrawioEditor(svgContent, onSave) {
    drawioCallback = onSave;
    _drawioPending = '';
    _drawioIsXml = false;

    if (svgContent) {
        // If content starts with <mxfile or <mxGraphModel, it's already native XML
        const trimmed = svgContent.trim();
        if (trimmed.startsWith('<mxfile') || trimmed.startsWith('<mxGraphModel')) {
            _drawioPending = svgContent;
            _drawioIsXml = true;
        }
        // If it's an SVG from draw.io export, extract the embedded diagram from content attribute
        else if (trimmed.startsWith('<svg') && svgContent.includes('content=')) {
            try {
                const parser = new DOMParser();
                const doc = parser.parseFromString(svgContent, 'image/svg+xml');
                const svgEl = doc.querySelector('svg');
                const diagramXml = svgEl?.getAttribute('content');
                if (diagramXml) {
                    _drawioPending = diagramXml;
                    _drawioIsXml = true;
                    console.log('[draw.io] Extracted diagram XML from SVG content attribute');
                } else {
                    _drawioPending = svgContent;
                    console.log('[draw.io] SVG has no content attribute, loading as raw SVG');
                }
            } catch (e) {
                console.warn('[draw.io] Failed to parse SVG:', e);
                _drawioPending = svgContent;
            }
        } else {
            _drawioPending = svgContent;
        }
    }

    console.log('[draw.io] Opening editor, isXml:', _drawioIsXml, 'contentLength:', _drawioPending.length);
    drawioWindow = window.open(
        'https://embed.diagrams.net/?embed=1&ui=dark&spin=1&proto=json&saveAndExit=1&noExitBtn=0&modified=unsavedChanges',
        '_blank'
    );
}

function closeDrawioEditor() {
    if (drawioWindow && !drawioWindow.closed) drawioWindow.close();
    drawioWindow = null;
    drawioCallback = null;
}

// Compute baseW/baseH from the SVG's intrinsic pixel size relative to the canvas.
// posW/posH are then set to baseW*zoom/100 (and baseH*zoom/100).
function fitWidgetToSvg(w) {
    const cfg = w.config; if (!cfg?.svg) return;
    try {
        const parser = new DOMParser();
        const doc = parser.parseFromString(cfg.svg, 'image/svg+xml');
        const svgEl = doc.querySelector('svg');
        if (!svgEl) return;
        let svgW, svgH;
        const vb = svgEl.getAttribute('viewBox');
        if (vb) {
            const parts = vb.split(/[\s,]+/).map(Number);
            if (parts.length >= 4) { svgW = parts[2]; svgH = parts[3]; }
        }
        if (!svgW) svgW = parseFloat(svgEl.getAttribute('width')) || 0;
        if (!svgH) svgH = parseFloat(svgEl.getAttribute('height')) || 0;
        if (svgW > 0 && svgH > 0) {
            const canvas = document.getElementById('dashCanvas');
            const cr = canvas?.getBoundingClientRect();
            const containerW = cr?.width || 1200, containerH = cr?.height || 800;
            // Base = SVG's natural pixel size as % of canvas
            cfg.baseW = Math.round(svgW / containerW * 1000) / 10;
            cfg.baseH = Math.round(svgH / containerH * 1000) / 10;
            // Apply current zoom
            const zoom = cfg.zoom || 100;
            cfg.posW = Math.round(cfg.baseW * zoom / 100 * 10) / 10;
            cfg.posH = Math.round(cfg.baseH * zoom / 100 * 10) / 10;
            console.log(`[SVG fit] ${svgW}x${svgH}px → baseW=${cfg.baseW}%, baseH=${cfg.baseH}%, zoom=${zoom}%, posW=${cfg.posW}%, posH=${cfg.posH}%`);
        }
    } catch (e) { console.warn('[SVG fit] parse error:', e); }
}

// Edit specific background SVG widget in draw.io
function editBackgroundSvg(wid) {
    const bgWidget = dashboard.widgets.find(w => w.id === wid && w.type === 'background-svg');
    if (!bgWidget) return;
    const currentSvg = bgWidget.config?.svg || '';
    openDrawioEditor(currentSvg, newSvg => {
        bgWidget.config = bgWidget.config || {};
        bgWidget.config.svg = newSvg;
        fitWidgetToSvg(bgWidget);
        // Re-render this specific widget
        const old = document.querySelector(`.scada-svg-widget[data-wid="${wid}"]`);
        if (old) old.remove();
        renderBackgroundSvg(bgWidget);
    });
}

// Duplicate any widget (deep copy with new ID)
function duplicateWidget(wid) {
    const src = dashboard.widgets.find(w => w.id === wid); if (!src) return;
    const copy = JSON.parse(JSON.stringify(src));
    copy.id = 'w' + Date.now().toString(36);
    copy.title = 'Copy of ' + (copy.title || 'Untitled');
    // Offset position slightly
    if (copy.type === 'scada-point' && copy.config) { copy.config.posX = (copy.config.posX || 50) + 3; copy.config.posY = (copy.config.posY || 50) + 3; }
    else if (copy.type === 'background-svg' && copy.config) { copy.config.posX = (copy.config.posX || 5) + 3; copy.config.posY = (copy.config.posY || 5) + 3; }
    else { copy.x = (copy.x || 0) + 1; copy.y = (copy.y || 0) + 1; }
    dashboard.widgets.push(copy);
    if (copy.type === 'background-svg') renderBackgroundSvg(copy);
    else if (copy.type === 'scada-point') renderScadaPoint(copy);
    else addWidgetToGrid(copy);
}

// Create new SVG in draw.io (from add widget modal â€” legacy, kept for compatibility)
function createSvgInDrawio() {
    openDrawioEditor('', newSvg => {
        pendingSvgContent = newSvg;
        // Show preview in the add widget modal
        const preview = document.getElementById('svgPreviewThumb');
        if (preview) {
            preview.innerHTML = newSvg;
            const svg = preview.querySelector('svg');
            if (svg) {
                svg.style.width = '100%'; svg.style.height = 'auto'; svg.style.maxHeight = '160px';
                const color = document.getElementById('svgColor')?.value || '#38bdf8';
                svg.style.filter = svgColorFilter(color);
                svg.style.opacity = '0.6';
            }
            preview.classList.remove('hidden');
        }
    });
}

// draw.io postMessage handler (works for both iframe and window.open)
window.addEventListener('message', function (evt) {
    if (!evt.data || typeof evt.data !== 'string') return;
    let msg;
    try { msg = JSON.parse(evt.data); } catch { return; }

    // Determine the target window to reply to
    const target = drawioWindow && !drawioWindow.closed ? drawioWindow : null;
    if (!target) return;

    if (msg.event === 'init') {
        // Editor is ready â€” load content
        const content = _drawioPending || '';
        console.log('[draw.io] init received, loading content:', _drawioIsXml ? 'diagram XML' : 'blank/raw', 'length:', content.length, 'preview:', content.substring(0, 100));
        if (!content) {
            target.postMessage(JSON.stringify({
                action: 'load',
                xml: '<mxGraphModel><root><mxCell id="0"/><mxCell id="1" parent="0"/></root></mxGraphModel>',
                autosave: 0
            }), '*');
        } else if (_drawioIsXml) {
            target.postMessage(JSON.stringify({
                action: 'load',
                xml: content,
                autosave: 0
            }), '*');
        } else {
            // Raw SVG â€” start blank canvas
            target.postMessage(JSON.stringify({
                action: 'load',
                xml: '<mxGraphModel><root><mxCell id="0"/><mxCell id="1" parent="0"/></root></mxGraphModel>',
                autosave: 0
            }), '*');
        }
    }
    else if (msg.event === 'save') {
        // User clicked Save (or Save & Exit) â€” request SVG with embedded diagram XML
        _drawioExitAfterSave = !!msg.exit;
        target.postMessage(JSON.stringify({
            action: 'export',
            format: 'xmlsvg',
            border: 0
        }), '*');
    }
    else if (msg.event === 'export') {
        // Received exported SVG â€” decode data URI if needed
        let svgContent = msg.data || '';
        if (svgContent.startsWith('data:image/svg+xml;base64,')) {
            try { svgContent = atob(svgContent.split(',')[1]); } catch (_) { }
        } else if (svgContent.startsWith('data:image/svg+xml,')) {
            try { svgContent = decodeURIComponent(svgContent.split(',')[1]); } catch (_) { }
        }
        if (drawioCallback && svgContent) {
            drawioCallback(svgContent);
        }
        if (_drawioExitAfterSave) {
            closeDrawioEditor();
        } else {
            // Clear dirty state so editor doesn't prompt on close
            target.postMessage(JSON.stringify({
                action: 'status',
                message: 'Saved',
                modified: false
            }), '*');
        }
    }
    else if (msg.event === 'exit') {
        closeDrawioEditor();
    }
});
