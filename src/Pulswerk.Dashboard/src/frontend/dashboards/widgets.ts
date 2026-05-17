import { DashboardStore } from './store';
import { IWidget, IWidgetConfig } from './types';
import { DashboardService } from './api';
import { transformToChartSeries, calculateYAxisConstraints } from './transformers';
import { COLORS } from './core';
import { h, render } from 'preact';
import { LatestValuesWidget } from './components/LatestValuesWidget';
import { SingleValueWidget } from './components/SingleValueWidget';

// widgets.ts – Widget rendering and data fetching
// ── WIDGET RENDERING ─────────────────────────────────────────────────────
export function renderAllWidgets(): void {
    if (DashboardStore.grid) DashboardStore.grid.removeAll(); 
    DashboardStore.charts = {};
    const bgLayer = document.getElementById('scadaBg'); if (bgLayer) bgLayer.innerHTML = '';
    const ptLayer = document.getElementById('scadaPoints'); if (ptLayer) ptLayer.innerHTML = '';
    
    if (DashboardStore.dashboard && DashboardStore.dashboard.widgets) {
        DashboardStore.dashboard.widgets.forEach((w: IWidget) => {
            if (w.type === 'background-svg') (window as any).renderBackgroundSvg(w);
            else if (w.type === 'scada-point') (window as any).renderScadaPoint(w);
            else addWidgetToGrid(w);
        });
    }
}

const WTYPE_ICONS: Record<string, string> = { 'timeseries': 'fa-chart-line', 'latest-values': 'fa-table', 'single-value': 'fa-digital-tachograph', 'scada-point': 'fa-map-pin', 'background-svg': 'fa-drafting-compass' };

export function addWidgetToGrid(w: IWidget): void {
    const emptyDash = document.getElementById('emptyDash');
    if (emptyDash) emptyDash.style.display = 'none';
    
    const el = document.createElement('div');
    el.className = 'grid-stack-item';
    const icon = WTYPE_ICONS[w.type] || 'fa-puzzle-piece';
    
    el.innerHTML = `<div class="grid-stack-item-content">
        <div class="widget-header">
            <i class="fas ${icon} widget-type-icon"></i>
            <span class="widget-title">${(window as any).esc((window as any).t(w.title || ''))}</span>
            <div class="widget-actions" style="display:${DashboardStore.isEditing ? 'flex' : 'none'}">
                <button title="Configure" onclick="editWidget('${w.id}')"><i class="fas fa-cog"></i></button>
                <button title="Duplicate" onclick="duplicateWidget('${w.id}')"><i class="fas fa-copy"></i></button>
                <button title="Remove" onclick="removeWidget('${w.id}')"><i class="fas fa-trash"></i></button>
            </div>
        </div>
        <div class="widget-body" id="wb_${w.id}"></div>
    </div>`;
    
    DashboardStore.grid.addWidget(el, { id: w.id, x: w.x, y: w.y, w: w.w, h: w.h, minW: 3, minH: 2 });
    renderWidgetContent(w);
}

export function renderWidgetContent(w: IWidget): void {
    const body = document.getElementById('wb_' + w.id);
    if (!body) return;
    const cfg = w.config || {};
    if (w.type === 'timeseries') renderTimeseries(w, body, cfg);
    else if (w.type === 'latest-values') renderLatestValues(w, body, cfg);
    else if (w.type === 'single-value') renderSingleValue(w, body, cfg);
}

export async function renderTimeseries(w: IWidget, body: HTMLElement, cfg: IWidgetConfig): Promise<void> {
    const keys = cfg.keys || [];
    if (!keys.length) { body.innerHTML = '<div class="empty-state" style="padding:1rem"><p style="font-size:0.8rem">No keys configured</p></div>'; return; }

    // Guard against overlapping async renders for the same widget
    if (DashboardStore.pendingRenders.has(w.id)) return;
    DashboardStore.pendingRenders.add(w.id);

    const { startTs, endTs } = getTimeRange();
    let data: any;
    try { 
        data = await DashboardService.fetchWidgetData(keys, startTs, endTs); 
    } catch (e) { 
        DashboardStore.pendingRenders.delete(w.id); 
        return; 
    }
    DashboardStore.pendingRenders.delete(w.id);

    const many = keys.length > 5;  // threshold for "dense" chart mode
    const isStacked = !!cfg.stacked;
    
    const allKeysMeta = (window as any).allKeys || [];
    const { series, usedColors } = transformToChartSeries(
        data, 
        keys, 
        allKeysMeta, 
        COLORS, 
        isStacked,
        (window as any).friendlyName
    );

    const yAxisOpts = calculateYAxisConstraints(series);

    // Update existing chart if it still has a valid DOM element
    const existingChart = DashboardStore.charts[w.id];
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
                    yaxis: yAxisOpts,
                    series: series
                }, true, false);
                return;
            }
            // Chart container was destroyed – clean up and recreate
            existingChart.destroy();
        } catch (e) { /* destroyed chart, ignore */ }
        delete DashboardStore.charts[w.id];
    }

    body.innerHTML = '<div id="chart_' + w.id + '" style="width:100%;flex:1;min-height:0"></div>';

    // ── Chart config adapts to series count ─────────────────────────
    const isBar = cfg.chartType === 'bar';
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
        },
        colors: usedColors,
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
            y: { formatter: (v: any) => (window as any).formatNumber(v, 2) },
        }
    };

    const chartEl = document.getElementById('chart_' + w.id);
    if (!chartEl) return;  // safety: body may have been replaced by another widget
    const chart = new (window as any).ApexCharts(chartEl, options);
    DashboardStore.charts[w.id] = chart;  // register BEFORE render to prevent concurrent creation
    chart.render().then(() => {
        setTimeout(() => chart.windowResize?.(), 200);
    }).catch((e: any) => {
        console.error('Chart render failed for', w.id, e);
        delete DashboardStore.charts[w.id];
    });
}

export async function renderLatestValues(_w: IWidget, body: HTMLElement, cfg: IWidgetConfig): Promise<void> {
    const keys = cfg.keys || [];
    if (!keys.length) { body.innerHTML = '<div class="empty-state" style="padding:1rem"><p style="font-size:0.8rem">No keys configured</p></div>'; return; }
    const allKeysMeta = (window as any).allKeys || [];
    render(h(LatestValuesWidget, { keys, allKeysMeta }), body);
}

export async function updateLatestValues(_w: IWidget, _cfg: IWidgetConfig): Promise<void> {
    // No-op: Preact component handles its own polling and updates
}

export async function renderSingleValue(w: IWidget, body: HTMLElement, cfg: IWidgetConfig): Promise<void> {
    const key = cfg.key || (cfg.keys?.[0]) || '';
    if (!key) { body.innerHTML = `<div class="empty-state" style="padding:1rem"><p style="font-size:0.8rem">${(window as any).t('no_key')}</p></div>`; return; }
    const allKeysMeta = (window as any).allKeys || [];
    render(h(SingleValueWidget, { widgetId: w.id, keyName: key, allKeysMeta }), body);
}

export async function updateSingleValue(_w: IWidget, _cfg: IWidgetConfig): Promise<void> {
    // No-op: Preact component handles its own polling and updates
}

export function miniSparkSvg(val: number, color: string): string {
    // Tiny inline spark indicator
    const h = 16, w = 32;
    return `<svg width="${w}" height="${h}" style="vertical-align:middle"><rect x="0" y="${h / 4}" width="${w}" height="${h / 2}" rx="2" fill="${(window as any).hexToRgba(color, 0.15)}"/><rect x="0" y="${h / 4}" width="${Math.min(w, Math.max(4, w * Math.abs(val % 100) / 100))}" height="${h / 2}" rx="2" fill="${color}" opacity="0.6"/></svg>`;
}

export function getTimeRange(): { startTs: number; endTs: number } {
    if (!DashboardStore.dashTw) { const now = Date.now(); return { startTs: now - 3600000, endTs: now }; }
    const r = DashboardStore.dashTw.getRange(); return { startTs: r.startTs, endTs: r.endTs };
}

// Keep exporting globally for older scripts and Razor pages until they are converted to ES Modules
Object.assign(window, {
    renderAllWidgets, addWidgetToGrid, renderWidgetContent, renderTimeseries,
    renderLatestValues, updateLatestValues, renderSingleValue, updateSingleValue,
    miniSparkSvg, getTimeRange
});
