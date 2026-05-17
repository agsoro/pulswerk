// widgets.js – Widget rendering and data fetching
// ── WIDGET RENDERING ─────────────────────────────────────────────────────
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
    const isStacked = !!cfg.stacked;
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

    // For stacked charts, trim all series to the common time range so
    // missing data at edges isn't treated as 0 by ApexCharts.
    if (isStacked && series.length > 1) {
        const seriesWithData = series.filter(s => s.data.length > 0);
        if (seriesWithData.length > 1) {
            const commonEnd = Math.min(...seriesWithData.map(s => s.data[s.data.length - 1].x));
            const commonStart = Math.max(...seriesWithData.map(s => s.data[0].x));
            series.forEach(s => {
                s.data = s.data.filter(p => p.x >= commonStart && p.x <= commonEnd);
            });
        }
    }

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
                        labels: { datetimeUTC: false, style: { colors: '#64748b', fontSize: '10px' } },
                        axisBorder: { show: false }, axisTicks: { show: false }
                    },
                    series: series
                }, true, false);
                return;
            }
            // Chart container was destroyed – clean up and recreate
            existingChart.destroy();
        } catch (e) { /* destroyed chart, ignore */ }
        delete charts[w.id];
    }

    // Calculate the actual available height in pixels for reliable rendering
    const bodyRect = body.getBoundingClientRect();
    const chartHeight = Math.max(bodyRect.height - 10, 150);  // fallback min 150px

    body.innerHTML = '<div style="flex:1;min-height:0;position:relative"><div id="chart_' + w.id + '" style="width:100%;height:' + chartHeight + 'px"></div></div>';

    // ── Chart config adapts to series count ─────────────────────────
    // Always use 'area' for non-bar charts (ApexCharts 'line' type has
    // rendering issues with many series and with stacked mode)
    const isBar = cfg.chartType === 'bar';
    const options = {
        series: series,
        chart: {
            type: isBar ? 'bar' : 'area',
            height: chartHeight,
            fontFamily: 'Inter, sans-serif',
            animations: { enabled: !many, easing: 'easeinout', speed: 400 },
            toolbar: { show: false },
            sparkline: { enabled: false },
            stacked: isStacked,
        },
        colors: colors,
        stroke: {
            curve: 'straight',
            width: many ? 1.5 : 2,
        },
        fill: {
            type: 'gradient',
            gradient: {
                shadeIntensity: 1, opacityFrom: many ? 0.08 : 0.45, opacityTo: 0.05,
                stops: [0, 90, 100]
            }
        },
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
            labels: { datetimeUTC: false, style: { colors: '#64748b', fontSize: '10px' } },
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
        const numVal = parseFloat(val);
        const isNum = !isNaN(numVal);
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
        <div class="sv-card-path">${pathHtml || '<span style="opacity:0.4">—</span>'}</div>
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

function getTimeRange() {
    if (!dashTw) { const now = Date.now(); return { startTs: now - 3600000, endTs: now }; }
    const r = dashTw.getRange(); return { startTs: r.startTs, endTs: r.endTs };
}
