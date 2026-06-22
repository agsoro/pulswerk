import { DashboardStore } from './store';
import { DashboardService } from './api';
import { transformToChartSeries, calculateYAxisConstraints } from './transformers';
import { COLORS } from './core';
import { h, render } from 'preact';
import { LatestValuesWidget } from './components/LatestValuesWidget';
import { SingleValueWidget } from './components/SingleValueWidget';
// widgets.ts – Widget rendering and data fetching
// ── WIDGET RENDERING ─────────────────────────────────────────────────────
export function renderAllWidgets() {
    if (DashboardStore.grid) {
        DashboardStore.grid.batchUpdate();
        DashboardStore.grid.removeAll();
    }
    DashboardStore.charts = {};
    const bgLayer = document.getElementById('scadaBg');
    if (bgLayer)
        bgLayer.innerHTML = '';
    const ptLayer = document.getElementById('scadaPoints');
    if (ptLayer)
        ptLayer.innerHTML = '';
    if (DashboardStore.dashboard && DashboardStore.dashboard.widgets) {
        DashboardStore.dashboard.widgets.forEach((w) => {
            if (w.type === 'background-svg')
                window.renderBackgroundSvg(w);
            else if (w.type === 'scada-point')
                window.renderScadaPoint(w);
            else
                addWidgetToGrid(w);
        });
    }
    if (DashboardStore.grid) {
        DashboardStore.grid.commit();
    }
}
const WTYPE_ICONS = { 'timeseries': 'fa-chart-line', 'latest-values': 'fa-table', 'single-value': 'fa-digital-tachograph', 'scada-point': 'fa-map-pin', 'background-svg': 'fa-drafting-compass' };
export function addWidgetToGrid(w) {
    const emptyDash = document.getElementById('emptyDash');
    if (emptyDash)
        emptyDash.style.display = 'none';
    const el = document.createElement('div');
    el.className = 'grid-stack-item';
    const icon = WTYPE_ICONS[w.type] || 'fa-puzzle-piece';
    el.innerHTML = `<div class="grid-stack-item-content">
        <div class="widget-header">
            <i class="fas ${icon} widget-type-icon"></i>
            <span class="widget-title">${window.esc(window.t(w.title || ''))}</span>
            <div class="widget-actions" style="display:${DashboardStore.isEditing ? 'flex' : 'none'}">
                <button title="Configure" onclick="editWidget('${w.id}')"><i class="fas fa-cog"></i></button>
                <button title="Duplicate" onclick="duplicateWidget('${w.id}')"><i class="fas fa-copy"></i></button>
                <button title="Remove" onclick="removeWidget('${w.id}')"><i class="fas fa-trash"></i></button>
            </div>
        </div>
        <div class="widget-body" id="wb_${w.id}"></div>
    </div>`;
    el.setAttribute('gs-id', w.id);
    el.setAttribute('gs-x', String(w.x !== undefined ? w.x : 0));
    el.setAttribute('gs-y', String(w.y !== undefined ? w.y : 0));
    el.setAttribute('gs-w', String(w.w || 6));
    el.setAttribute('gs-h', String(w.h || 4));
    el.setAttribute('gs-min-w', '3');
    el.setAttribute('gs-min-h', '2');
    DashboardStore.grid.addWidget(el, { id: w.id, x: w.x, y: w.y, w: w.w, h: w.h, minW: 3, minH: 2, autoPosition: false });
    renderWidgetContent(w);
}
export function renderWidgetContent(w) {
    const body = document.getElementById('wb_' + w.id);
    if (!body)
        return;
    const cfg = w.config || {};
    if (w.type === 'timeseries')
        renderTimeseries(w, body, cfg);
    else if (w.type === 'latest-values')
        renderLatestValues(w, body, cfg);
    else if (w.type === 'single-value')
        renderSingleValue(w, body, cfg);
}
export function getEnabledGranularities(durationMs) {
    const HOUR_MS = 3600000;
    const DAY_MS = 86400000;
    const MONTH_MS = 30 * DAY_MS;
    const YEAR_MS = 365 * DAY_MS;
    const hourBars = durationMs / HOUR_MS;
    const dayBars = durationMs / DAY_MS;
    const monthBars = durationMs / MONTH_MS;
    const yearBars = durationMs / YEAR_MS;
    const enabled = {
        hour: hourBars >= 2 && hourBars <= 400,
        day: dayBars >= 2 && dayBars <= 400,
        month: monthBars >= 2 && monthBars <= 400,
        year: yearBars >= 2 && yearBars <= 400
    };
    // Guarantee at least one enabled granularity
    if (!enabled.hour && !enabled.day && !enabled.month && !enabled.year) {
        if (durationMs < 2 * HOUR_MS) {
            enabled.hour = true;
        }
        else if (durationMs > 400 * YEAR_MS) {
            enabled.year = true;
        }
        else {
            if (durationMs < 2 * DAY_MS) {
                enabled.hour = true;
            }
            else if (durationMs < 2 * MONTH_MS) {
                enabled.day = true;
            }
            else if (durationMs < 2 * YEAR_MS) {
                enabled.month = true;
            }
            else {
                enabled.year = true;
            }
        }
    }
    return enabled;
}
export function alignTimestampToGranularity(ts, granularity) {
    const d = new Date(ts);
    if (granularity === 'hour') {
        d.setUTCMinutes(0, 0, 0);
    }
    else if (granularity === 'day') {
        d.setUTCHours(0, 0, 0, 0);
    }
    else if (granularity === 'month') {
        d.setUTCDate(1);
        d.setUTCHours(0, 0, 0, 0);
    }
    else if (granularity === 'year') {
        d.setUTCMonth(0, 1);
        d.setUTCHours(0, 0, 0, 0);
    }
    return d.getTime();
}
export function changeBarGranularity(widgetId, granularity) {
    const widgets = DashboardStore.dashboard?.widgets || [];
    const w = widgets.find((widget) => widget.id === widgetId);
    if (w) {
        w.config = w.config || {};
        w.config.barGranularity = granularity;
        renderWidgetContent(w);
    }
}
export function changeBarMode(widgetId, mode) {
    const widgets = DashboardStore.dashboard?.widgets || [];
    const w = widgets.find((widget) => widget.id === widgetId);
    if (w) {
        w.config = w.config || {};
        w.config.barMode = mode;
        renderWidgetContent(w);
    }
}
export async function renderTimeseries(w, body, cfg) {
    if (!body)
        return;
    const keys = cfg.keys || [];
    if (!keys.length) {
        body.innerHTML = '<div class="empty-state" style="padding:1rem"><p style="font-size:0.8rem">No keys configured</p></div>';
        return;
    }
    // Guard against overlapping async renders for the same widget
    if (DashboardStore.pendingRenders.has(w.id))
        return;
    DashboardStore.pendingRenders.add(w.id);
    const { startTs, endTs } = getTimeRange();
    const isBar = cfg.chartType === 'bar';
    let activeGranularity = cfg.barGranularity;
    let activeMode = cfg.barMode;
    let alignedStartTs = startTs;
    if (isBar) {
        const durationMs = endTs - startTs;
        const enabled = getEnabledGranularities(durationMs);
        if (!activeGranularity || !enabled[activeGranularity]) {
            if (enabled.day)
                activeGranularity = 'day';
            else if (enabled.hour)
                activeGranularity = 'hour';
            else if (enabled.month)
                activeGranularity = 'month';
            else if (enabled.year)
                activeGranularity = 'year';
            cfg.barGranularity = activeGranularity;
        }
        if (!activeMode) {
            activeMode = 'max';
            cfg.barMode = activeMode;
        }
        alignedStartTs = alignTimestampToGranularity(startTs, activeGranularity);
    }
    let data;
    try {
        data = await DashboardService.fetchWidgetData(keys, alignedStartTs, endTs, activeGranularity, activeMode);
    }
    catch (e) {
        DashboardStore.pendingRenders.delete(w.id);
        return;
    }
    DashboardStore.pendingRenders.delete(w.id);
    const many = keys.length > 5; // threshold for "dense" chart mode
    const isStacked = !!cfg.stacked;
    const allKeysMeta = window.allKeys || [];
    const { series, usedColors } = transformToChartSeries(data, keys, allKeysMeta, COLORS, isStacked, window.friendlyName, isBar, activeGranularity);
    const yAxisOpts = calculateYAxisConstraints(series);
    const isRealtime = !DashboardStore.dashTw || DashboardStore.dashTw.mode === 'realtime';
    const realtimeMs = DashboardStore.dashTw ? DashboardStore.dashTw.realtimeMs : 3600000;
    const xaxisConfig = {
        type: 'datetime',
        labels: { datetimeUTC: false, style: { colors: '#64748b', fontSize: '10px' } },
        axisBorder: { show: false },
        axisTicks: { show: false }
    };
    if (isRealtime) {
        xaxisConfig.range = realtimeMs;
        xaxisConfig.min = undefined;
        xaxisConfig.max = undefined;
    }
    else {
        xaxisConfig.min = alignedStartTs;
        xaxisConfig.max = endTs;
        xaxisConfig.range = undefined;
    }
    // Update existing chart if it still has a valid DOM element
    const existingChart = DashboardStore.charts[w.id];
    if (existingChart) {
        try {
            // Verify the chart's container is still in the DOM
            const chartEl = document.getElementById('chart_' + w.id);
            if (!isBar && chartEl && chartEl.querySelector('.apexcharts-canvas')) {
                existingChart.updateOptions({
                    xaxis: xaxisConfig,
                    yaxis: yAxisOpts,
                    series: series
                }, true, true);
                return;
            }
            // Chart container was destroyed or it's a bar chart – clean up and recreate
            existingChart.destroy();
        }
        catch (e) { /* destroyed chart, ignore */ }
        delete DashboardStore.charts[w.id];
    }
    if (isBar) {
        const durationMs = endTs - startTs;
        const enabled = getEnabledGranularities(durationMs);
        const btnClass = (g) => {
            const isActive = activeGranularity === g;
            const isGEnabled = enabled[g];
            let cls = "px-2.5 py-0.5 text-[10px] font-semibold rounded-md transition-all ";
            if (!isGEnabled) {
                cls += "text-slate-600 opacity-25 cursor-not-allowed pointer-events-none border border-transparent";
            }
            else if (isActive) {
                cls += "bg-sky-500/10 text-sky-400 border border-sky-500/20 shadow-sm shadow-sky-500/5";
            }
            else {
                cls += "text-slate-400 hover:text-slate-200 cursor-pointer border border-transparent";
            }
            return cls;
        };
        const modeBtnClass = (m) => {
            const isActive = activeMode === m;
            let cls = "px-2.5 py-0.5 text-[10px] font-semibold rounded-md transition-all ";
            if (isActive) {
                cls += "bg-sky-500/10 text-sky-400 border border-sky-500/20 shadow-sm shadow-sky-500/5";
            }
            else {
                cls += "text-slate-400 hover:text-slate-200 cursor-pointer border border-transparent";
            }
            return cls;
        };
        body.innerHTML = `
            <div class="bar-chart-controls flex items-center justify-between mb-2 px-1 text-xs text-slate-400 gap-4 flex-wrap">
                <!-- Granularity Group -->
                <div class="flex items-center gap-2">
                    <span class="text-[0.65rem] text-slate-500 font-bold uppercase tracking-wider">Granularity:</span>
                    <div class="inline-flex rounded-lg p-0.5 bg-slate-950/80 border border-slate-800/80 backdrop-blur-md">
                        <button class="${btnClass('hour')}" onclick="changeBarGranularity('${w.id}', 'hour')">Hour</button>
                        <button class="${btnClass('day')}" onclick="changeBarGranularity('${w.id}', 'day')">Day</button>
                        <button class="${btnClass('month')}" onclick="changeBarGranularity('${w.id}', 'month')">Month</button>
                        <button class="${btnClass('year')}" onclick="changeBarGranularity('${w.id}', 'year')">Year</button>
                    </div>
                </div>
                <!-- Mode Group -->
                <div class="flex items-center gap-2">
                    <span class="text-[0.65rem] text-slate-500 font-bold uppercase tracking-wider">Mode:</span>
                    <div class="inline-flex rounded-lg p-0.5 bg-slate-950/80 border border-slate-800/80 backdrop-blur-md">
                        <button class="${modeBtnClass('max')}" title="Max in Period" onclick="changeBarMode('${w.id}', 'max')">Max</button>
                        <button class="${modeBtnClass('diff')}" title="Difference in Period (for meter readings)" onclick="changeBarMode('${w.id}', 'diff')">Diff</button>
                    </div>
                </div>
            </div>
            <div style="position:relative; flex:1; min-height:0; width:100%">
                <div id="chart_${w.id}" style="position:absolute; inset:0"></div>
            </div>
        `;
    }
    else {
        body.innerHTML = `
            <div style="position:relative; flex:1; min-height:0; width:100%">
                <div id="chart_${w.id}" style="position:absolute; inset:0"></div>
            </div>
        `;
    }
    // ── Chart config adapts to series count ─────────────────────────
    const options = {
        series: series,
        chart: {
            type: isBar ? 'bar' : 'area',
            height: '100%',
            fontFamily: 'Inter, sans-serif',
            animations: { enabled: false },
            toolbar: { show: false },
            sparkline: { enabled: false },
            stacked: isStacked,
            accessibility: { enabled: false },
        },
        colors: usedColors,
        stroke: {
            curve: 'straight',
            width: isBar ? 0 : (many ? 1.5 : 2),
        },
        fill: isBar ? {
            type: 'solid',
            opacity: 0.85
        } : {
            type: 'gradient',
            gradient: {
                shadeIntensity: 1,
                opacityFrom: many ? 0.08 : 0.45,
                opacityTo: 0.05,
                stops: [0, 90, 100]
            }
        },
        dataLabels: { enabled: false },
        grid: {
            show: true, borderColor: 'rgba(255,255,255,0.05)',
            xaxis: { lines: { show: false } },
            yaxis: { lines: { show: true } },
            padding: { top: 0, right: 0, bottom: 12, left: 10 }
        },
        xaxis: xaxisConfig,
        yaxis: yAxisOpts,
        annotations: {
            yaxis: [{
                    y: 0,
                    borderColor: 'rgba(148,163,184,0.35)',
                    strokeDashArray: 0,
                    label: { show: false }
                }]
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
            y: { formatter: (v) => window.formatNumber(v, 2) },
        }
    };
    if (isBar) {
        options.plotOptions = {
            bar: {
                columnWidth: '75%',
                borderRadius: 4
            }
        };
    }
    requestAnimationFrame(() => {
        const chartEl = document.getElementById('chart_' + w.id);
        if (!chartEl)
            return; // safety: body may have been replaced by another widget
        const chart = new window.ApexCharts(chartEl, options);
        DashboardStore.charts[w.id] = chart; // register BEFORE render to prevent concurrent creation
        chart.render().then(() => {
            setTimeout(() => {
                chart.windowResize?.();
                window.dispatchEvent(new Event('resize'));
            }, 100);
            setTimeout(() => {
                chart.windowResize?.();
                window.dispatchEvent(new Event('resize'));
            }, 500);
        }).catch((e) => {
            console.error('Chart render failed for', w.id, e);
            delete DashboardStore.charts[w.id];
        });
    });
}
export function appendTimeseriesData(w, newData) {
    const chart = DashboardStore.charts[w.id];
    if (!chart)
        return;
    const cfg = w.config || {};
    if (cfg.chartType === 'bar')
        return;
    const keys = cfg.keys || [];
    if (!keys.length)
        return;
    const isRealtime = !DashboardStore.dashTw || DashboardStore.dashTw.mode === 'realtime';
    const realtimeMs = DashboardStore.dashTw ? DashboardStore.dashTw.realtimeMs : 3600000;
    let hasUpdate = false;
    const now = Date.now();
    const cutoff = now - realtimeMs;
    const currentSeries = chart.w?.config?.series || [];
    const updatedSeries = keys.map((key, i) => {
        const existing = currentSeries[i] || { name: key, data: [] };
        let points = [];
        if (Array.isArray(existing.data)) {
            points = existing.data.map((p) => {
                if (p && typeof p === 'object') {
                    if (p.x !== undefined && p.y !== undefined) {
                        return { x: Number(p.x), y: Number(p.y) };
                    }
                    if (Array.isArray(p) && p.length >= 2) {
                        return { x: Number(p[0]), y: Number(p[1]) };
                    }
                }
                return null;
            }).filter((p) => p !== null && !isNaN(p.x) && !isNaN(p.y));
        }
        if (newData[key] !== undefined) {
            const val = parseFloat(newData[key]);
            if (!isNaN(val)) {
                hasUpdate = true;
                points.push({ x: now, y: parseFloat(val.toFixed(2)) });
            }
        }
        if (isRealtime) {
            points = points.filter((p) => p.x >= cutoff);
        }
        return {
            name: existing.name || key,
            data: points
        };
    });
    if (hasUpdate) {
        chart.updateSeries(updatedSeries, true);
    }
}
export async function renderLatestValues(_w, body, cfg) {
    if (!body)
        return;
    const keys = cfg.keys || [];
    if (!keys.length) {
        body.innerHTML = '<div class="empty-state" style="padding:1rem"><p style="font-size:0.8rem">No keys configured</p></div>';
        return;
    }
    const allKeysMeta = window.allKeys || [];
    render(h(LatestValuesWidget, { keys, allKeysMeta }), body);
}
export async function updateLatestValues(_w, _cfg) {
    // No-op: Preact component handles its own polling and updates
}
export async function renderSingleValue(w, body, cfg) {
    if (!body)
        return;
    const key = cfg.key || (cfg.keys?.[0]) || '';
    if (!key) {
        body.innerHTML = `<div class="empty-state" style="padding:1rem"><p style="font-size:0.8rem">${window.t('no_key')}</p></div>`;
        return;
    }
    const allKeysMeta = window.allKeys || [];
    render(h(SingleValueWidget, { widgetId: w.id, keyName: key, allKeysMeta }), body);
}
export async function updateSingleValue(_w, _cfg) {
    // No-op: Preact component handles its own polling and updates
}
export function miniSparkSvg(val, color) {
    // Tiny inline spark indicator
    const h = 16, w = 32;
    return `<svg width="${w}" height="${h}" style="vertical-align:middle"><rect x="0" y="${h / 4}" width="${w}" height="${h / 2}" rx="2" fill="${window.hexToRgba(color, 0.15)}"/><rect x="0" y="${h / 4}" width="${Math.min(w, Math.max(4, w * Math.abs(val % 100) / 100))}" height="${h / 2}" rx="2" fill="${color}" opacity="0.6"/></svg>`;
}
export function getTimeRange() {
    if (!DashboardStore.dashTw) {
        const now = Date.now();
        return { startTs: now - 3600000, endTs: now };
    }
    const r = DashboardStore.dashTw.getRange();
    return { startTs: r.startTs, endTs: r.endTs };
}
// Keep exporting globally for older scripts and Razor pages until they are converted to ES Modules
Object.assign(window, {
    renderAllWidgets, addWidgetToGrid, renderWidgetContent, renderTimeseries, appendTimeseriesData,
    renderLatestValues, updateLatestValues, renderSingleValue, updateSingleValue,
    miniSparkSvg, getTimeRange,
    getEnabledGranularities, alignTimestampToGranularity, changeBarGranularity, changeBarMode
});
