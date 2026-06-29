// @ts-nocheck
let _currentHistoryKey: string | null = null;
let _currentEditKey: string | null = null;
let _currentPropsKey: string | null = null;
let _currentScheduleKey: string | null = null;
let _historyChart: any = null;
let _historyRefreshTimer: any = null;
let _historyTw: ITimeWindowSelector | null = null; // reusable TW selector instance

let isEditingSchedule = false;
let scheduleValueType: string = 'real';
let scheduleStates: string[] | null = null;
let currentScheduleData: any[] = [];

Object.defineProperties(window, {
    currentHistoryKey: { get: () => _currentHistoryKey, set: (v) => { _currentHistoryKey = v; }, configurable: true },
    currentEditKey: { get: () => _currentEditKey, set: (v) => { _currentEditKey = v; }, configurable: true },
    currentPropsKey: { get: () => _currentPropsKey, set: (v) => { _currentPropsKey = v; }, configurable: true },
    currentScheduleKey: { get: () => _currentScheduleKey, set: (v) => { _currentScheduleKey = v; }, configurable: true },
    historyChart: { get: () => _historyChart, set: (v) => { _historyChart = v; }, configurable: true },
    historyRefreshTimer: { get: () => _historyRefreshTimer, set: (v) => { _historyRefreshTimer = v; }, configurable: true },
    historyTw: { get: () => _historyTw, set: (v) => { _historyTw = v; }, configurable: true }
});

// Initialize TW module for history modal (once)
function ensureHistoryTw(): void {
    console.log("DEBUG: ensureHistoryTw() called");
    if (historyTw) {
        console.log("DEBUG: historyTw already exists");
        return;
    }
    const container = document.getElementById('historyTwContainer');
    if (!container) {
        console.warn("DEBUG: historyTwContainer element not found!");
        return;
    }
    console.log("DEBUG: historyTwContainer found, creating time window selector. createTimeWindowSelector is:", typeof createTimeWindowSelector);
    try {
        historyTw = createTimeWindowSelector(container, {
            mode: 'realtime',
            realtimeMs: 3600000,
            onChange: () => reloadHistory()
        });
        console.log("DEBUG: time window selector created successfully");
    } catch (e) {
        console.error("DEBUG: createTimeWindowSelector threw exception:", e);
        throw e;
    }
}

// --- Tab Switching inside Details Modal ---
function switchTelemetryTab(tabName: 'trend' | 'properties' | 'schedule'): void {
    const trendContent = document.getElementById('tabContent_trend');
    const propsContent = document.getElementById('tabContent_properties');
    const schedContent = document.getElementById('tabContent_schedule');
    
    if (trendContent) trendContent.style.display = 'none';
    if (propsContent) propsContent.style.display = 'none';
    if (schedContent) schedContent.style.display = 'none';
    
    // Reset all tab button styles to inactive
    ['trend', 'properties', 'schedule'].forEach(t => {
        const btn = document.getElementById(`tabBtn_${t}`);
        if (btn) {
            btn.className = "px-4 py-2 text-xs font-semibold rounded-lg text-slate-400 hover:text-slate-200 border border-transparent";
        }
    });
    
    // Show active tab content
    const activeContent = document.getElementById(`tabContent_${tabName}`);
    if (activeContent) activeContent.style.display = 'flex';
    
    // Set active tab button style
    const activeBtn = document.getElementById(`tabBtn_${tabName}`);
    if (activeBtn) {
        activeBtn.className = "px-4 py-2 text-xs font-semibold rounded-lg bg-sky-500/10 text-sky-400 border border-sky-500/20";
    }
}

// --- Unified Telemetry Details Modal ---
async function openTelemetryDetails(key: string): Promise<void> {
    console.log("DEBUG: openTelemetryDetails called with key:", key);
    
    // Stop any running refresh timer
    stopHistoryRefresh();

    // Destroy previous chart instance to prevent listener leaks and flickering when switching keys
    if (historyChart) {
        try {
            historyChart.destroy();
        } catch (e) {
            console.error("Failed to destroy previous chart:", e);
        }
        historyChart = null;
    }
    const chartContainer = document.getElementById('historyChart');
    if (chartContainer) {
        chartContainer.innerHTML = '';
    }
    
    // Open Modal immediately & show loading spinner
    const detailsModal = document.getElementById('telemetryDetailsModal');
    if (detailsModal) {
        detailsModal.style.display = 'flex';
        console.log("DEBUG: modal display set to flex immediately");
    } else {
        console.error("DEBUG: telemetryDetailsModal element not found in DOM!");
    }
    
    const loadingOverlay = document.getElementById('telModalLoadingOverlay');
    if (loadingOverlay) {
        loadingOverlay.classList.remove('hidden');
    }

    try {
        currentHistoryKey = key;
        currentEditKey = key;
        currentPropsKey = key;
        
        console.log("DEBUG: calling ensureHistoryTw()");
        ensureHistoryTw();
        
        // Load ALL keys metadata to populate the explorer sidebar fully
        console.log("DEBUG: calling ensureKeysMeta()");
        await ensureKeysMeta();
        
        console.log("DEBUG: resolving key meta");
        const meta = resolveKeyMeta(key);
        const path = meta.parentPath || [];
        const enums = meta.enumValues || null;
        const type = meta.type || '';
        const isScheduleObj = type === 'OBJECT_SCHEDULE';
        
        console.log("DEBUG: meta resolved:", meta);
        
        // Reset active tab to Trend & Control on open
        switchTelemetryTab('trend');
        
        // Toggle switching schedule tab button visibility
        const scheduleTabBtn = document.getElementById('tabBtn_schedule');
        if (scheduleTabBtn) {
            scheduleTabBtn.style.display = isScheduleObj ? 'block' : 'none';
        }
        
        // Render Sidebar explorer list
        console.log("DEBUG: rendering sidebar list");
        renderTelemetrySidebarList(key);
        
        // Set Header
        const fn = (window as any).friendlyName;
        const telTitle = document.getElementById('telTitle');
        if (telTitle) telTitle.textContent = (fn ? fn(key) : null) || meta.name || key;
        const telMeta = document.getElementById('telMeta');
        if (telMeta) telMeta.textContent = meta.fullName || key;
        const telUnitLabel = document.getElementById('telUnitLabel');
        if (telUnitLabel) telUnitLabel.textContent = meta.units || '';
        renderModalBreadcrumb('telPath', path);
        
        // Setup Favorite Star
        const favBtn = document.getElementById('telFavBtn') as HTMLElement;
        if (favBtn) {
            if ((window as any).pwCanEditFavorites) {
                favBtn.style.display = 'inline-flex';
                const isFav = (window as any).pw_fav?.get('deziko_favorites')?.includes(key);
                const starIcon = favBtn.querySelector('i');
                if (starIcon) {
                    if (isFav) {
                        starIcon.className = 'fas fa-star text-amber-400';
                        favBtn.classList.add('active');
                    } else {
                        starIcon.className = 'far fa-star text-slate-400';
                        favBtn.classList.remove('active');
                    }
                }
                favBtn.onclick = () => {
                    if (typeof (window as any).toggleFavorite === 'function') {
                        (window as any).toggleFavorite(key);
                        // Update icon after toggle
                        const nowFav = (window as any).pw_fav?.get('deziko_favorites')?.includes(key);
                        const dynamicStarIcon = favBtn.querySelector('i');
                        if (dynamicStarIcon) {
                            if (nowFav) {
                                dynamicStarIcon.className = 'fas fa-star text-amber-400';
                                favBtn.classList.add('active');
                            } else {
                                dynamicStarIcon.className = 'far fa-star text-slate-400';
                                favBtn.classList.remove('active');
                            }
                        }
                        
                        // Re-render sidebar to update the star if necessary, but keep selection
                        renderTelemetrySidebarList(key);
                    }
                };
            } else {
                favBtn.style.display = 'none';
            }
        }
        
        // Hide all input groups by default
        const stepperGroup = document.getElementById('inlineStepperGroup');
        if (stepperGroup) stepperGroup.style.display = 'none';
        const enumGroup = document.getElementById('inlineEnumGroup');
        if (enumGroup) enumGroup.style.display = 'none';
        const boolGroup = document.getElementById('inlineBoolGroup');
        if (boolGroup) boolGroup.style.display = 'none';
        
        // Hide edit mode by default, show view mode
        const viewMode = document.getElementById('telValueViewMode');
        if (viewMode) viewMode.classList.remove('hidden');
        const editMode = document.getElementById('telValueEditMode');
        if (editMode) editMode.classList.add('hidden');
        
        const editStartBtn = document.getElementById('telInlineEditStartBtn');
        if (editStartBtn) {
            editStartBtn.style.display = (meta.isWritable && !isScheduleObj) ? 'inline-flex' : 'none';
        }
        
        const status = document.getElementById('editStatus');
        if (status) {
            status.textContent = '';
            status.className = 'status-msg';
        }
        
        // Fetch live value
        const telLiveValue = document.getElementById('telLiveValue');
        if (telLiveValue) telLiveValue.textContent = '---';
        
        let currentVal = '---';
        try {
            console.log("DEBUG: fetching latest values");
            const data = await fetchLatestValues(key);
            const raw = data?.[key];
            if (raw != null) {
                currentVal = typeof PulswerkValue !== 'undefined' ? PulswerkValue.formatDisplay(raw, type) : String(raw);
                if (telLiveValue) telLiveValue.textContent = currentVal;
            }
        } catch(e) { console.error("DEBUG: fetchLatestValues failed", e); }
        
        // Setup Write Controls if Writable
        if (meta.isWritable && !isScheduleObj) {
            if (enums && Object.keys(enums).length > 0) {
                const sel = document.getElementById('enumSelect') as HTMLSelectElement;
                if (sel) {
                    sel.innerHTML = '';
                    for (const [v, label] of Object.entries(enums)) {
                        const opt = document.createElement('option');
                        opt.value = v;
                        opt.textContent = label;
                        sel.appendChild(opt);
                    }
                    const matchOpt = Array.from(sel.options).find(o => o.textContent === currentVal);
                    if (matchOpt) sel.value = matchOpt.value;
                    else sel.value = currentVal;
                }
                if (enumGroup) enumGroup.style.display = 'block';
            } else if (type && PulswerkValue.isBinary(type)) {
                const input = document.getElementById('boolInput') as HTMLInputElement;
                if (input) {
                    input.checked = PulswerkValue.parseDisplay(currentVal, type) !== 0;
                    updateBoolLabel();
                }
                if (boolGroup) boolGroup.style.display = 'flex';
            } else {
                const editInput = document.getElementById('editValue') as HTMLInputElement;
                if (editInput) {
                    editInput.value = String(parseFloat(currentVal) || 0);
                    editInput.step = (window as any).isTemperatureUnit(meta.units) ? '0.5' : '1';
                }
                const editUnitLabel = document.getElementById('telEditUnitLabel');
                if (editUnitLabel) {
                    editUnitLabel.textContent = meta.units || '';
                }
                if (stepperGroup) stepperGroup.style.display = 'flex';
            }
        }
        
        // Load BACnet Properties
        console.log("DEBUG: loading properties");
        const props = await loadPropsForDetails(key);
        
        // Load Schedule if Schedule object
        if (isScheduleObj) {
            console.log("DEBUG: loading schedule");
            await loadScheduleForDetails(key, props);
        }
        
        // Load History Chart
        console.log("DEBUG: reloading history");
        await reloadHistory();
        console.log("DEBUG: starting history refresh");
        startHistoryRefresh();
        
        // Hide loading overlay
        if (loadingOverlay) {
            loadingOverlay.classList.add('hidden');
        }
    } catch (err: any) {
        console.error("DEBUG: openTelemetryDetails caught error:", err);
        if (loadingOverlay) {
            loadingOverlay.classList.add('hidden');
        }
        if (detailsModal) {
            detailsModal.style.display = 'none';
        }
        try {
            fetch("/plswk/api/client-error", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    msg: "DEBUG: openTelemetryDetails caught: " + String(err),
                    source: "modals.ts",
                    line: 0,
                    col: 0,
                    stack: String(err?.stack || "")
                })
            }).catch(() => {});
        } catch (e) {}
    }
}Location: openTelemetryDetails

function closeTelemetryDetails(): void {
    stopHistoryRefresh();
    const detailsModal = document.getElementById('telemetryDetailsModal');
    if (detailsModal) detailsModal.style.display = 'none';
    currentHistoryKey = null;
    currentEditKey = null;
    currentPropsKey = null;
    currentScheduleKey = null;
    
    // Cleanup Chart
    if (historyChart) {
        historyChart.destroy();
        historyChart = null;
    }
    const container = document.getElementById('historyChart');
    if (container) container.innerHTML = '';
    
    // Clear elements
    const telTitle = document.getElementById('telTitle');
    if (telTitle) telTitle.textContent = 'Telemetry Details';
    const telUnitLabel = document.getElementById('telUnitLabel');
    if (telUnitLabel) telUnitLabel.textContent = '';
    const telMeta = document.getElementById('telMeta');
    if (telMeta) telMeta.textContent = '';
    const telLiveValue = document.getElementById('telLiveValue');
    if (telLiveValue) telLiveValue.textContent = '---';
    
    const pathEl = document.getElementById('telPath');
    if (pathEl) pathEl.innerHTML = '';
    
    // Reset properties table
    const propsBody = document.getElementById('propsBody');
    if (propsBody) propsBody.innerHTML = '';
    const propsTable = document.getElementById('propsTable');
    if (propsTable) propsTable.classList.add('hidden');
    const propsEmpty = document.getElementById('propsEmpty');
    if (propsEmpty) propsEmpty.classList.add('hidden');
    
    // Reset schedule view
    isEditingSchedule = false;
    scheduleValueType = 'real';
    scheduleStates = null;
    const scheduleGrid = document.getElementById('scheduleGrid');
    if (scheduleGrid) scheduleGrid.innerHTML = '';
    
    const status = document.getElementById('scheduleStatus');
    if (status) { status.textContent = ''; status.className = 'status-msg'; }

    // Clear search query in sidebar
    const searchInput = document.getElementById('telSidebarSearch') as HTMLInputElement;
    if (searchInput) searchInput.value = '';
    const listContainer = document.getElementById('telSidebarList');
    if (listContainer) listContainer.innerHTML = '';
    
    // Reset active tab button
    switchTelemetryTab('trend');
}

// --- Telemetry Sidebar List Rendering ---
function renderTelemetrySidebarList(activeKey: string): void {
    const listContainer = document.getElementById('telSidebarList');
    if (!listContainer) return;
    
    // Sort allKeys alphabetically by friendly name
    const sortedKeys = [...allKeys].sort((a, b) => {
        const nameA = a.name || a.key;
        const nameB = b.name || b.key;
        return nameA.localeCompare(nameB);
    });
    
    listContainer.innerHTML = '';
    
    sortedKeys.forEach(k => {
        const isSelected = k.key === activeKey;
        const isFav = (window as any).pw_fav?.get('deziko_favorites')?.includes(k.key);
        
        const item = document.createElement('div');
        item.className = `flex items-center gap-2 p-2 cursor-pointer rounded-lg transition-all text-left group hover:bg-white/[0.04] ${isSelected ? 'bg-sky-500/10 border-l-2 border-sky-400 pl-1.5' : ''}`;
        item.dataset.sidebarKey = k.key;
        
        const pathStr = k.parentPath?.map(p => p.name).join(' › ') || '';
        
        item.innerHTML = `
            <div class="shrink-0 text-[0.7rem] ${isFav ? 'text-amber-400' : 'text-slate-600 group-hover:text-slate-400'}">
                <i class="${isFav ? 'fas' : 'far'} fa-star"></i>
            </div>
            <div class="flex-1 min-w-0">
                <div class="font-semibold text-[0.75rem] truncate transition-colors ${isSelected ? 'text-sky-400 font-bold' : 'text-slate-200 group-hover:text-sky-400'}">${k.name || k.key}</div>
                ${pathStr ? `<div class="text-[0.62rem] text-slate-500 truncate mt-0.5">${pathStr}</div>` : ''}
                <div class="text-[0.6rem] text-slate-600 font-mono truncate mt-0.5">${k.key}</div>
            </div>
        `;
        
        item.onclick = () => {
            if (k.key !== activeKey) {
                openTelemetryDetails(k.key);
            }
        };
        
        listContainer.appendChild(item);
    });
    
    // Apply existing query filter
    filterTelemetrySidebar();
}

function filterTelemetrySidebar(): void {
    const searchInput = document.getElementById('telSidebarSearch') as HTMLInputElement;
    if (!searchInput) return;
    const query = searchInput.value.toLowerCase().trim();
    
    const listContainer = document.getElementById('telSidebarList');
    if (!listContainer) return;
    
    const items = listContainer.children;
    for (let i = 0; i < items.length; i++) {
        const item = items[i] as HTMLElement;
        const key = (item.querySelector('.font-mono')?.textContent || '').toLowerCase();
        const name = (item.querySelector('.font-semibold')?.textContent || '').toLowerCase();
        const path = (item.querySelector('.text-\\[0\\.62rem\\]')?.textContent || '').toLowerCase();
        
        if (!query || key.includes(query) || name.includes(query) || path.includes(query)) {
            item.style.display = 'flex';
        } else {
            item.style.display = 'none';
        }
    }
}

// --- Metadata Properties Helper ---
function escapeHtml(value: any): string {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

async function loadPropsForDetails(key: string): Promise<any[]> {
    const loader = document.getElementById('propsLoading');
    const table = document.getElementById('propsTable');
    const empty = document.getElementById('propsEmpty');
    const body = document.getElementById('propsBody');
    
    if (loader) loader.classList.remove('hidden');
    if (table) table.classList.add('hidden');
    if (empty) empty.classList.add('hidden');
    if (body) body.innerHTML = '';

    // Helper to render the always-present rows: the internal real telemetry key
    // and a deep-link to the Historical Data (TelemetryCrud) view for this key.
    // These are only shown inside the Extended Properties tab.
    const renderInternalRows = () => {
        if (!body) return;
        const crudUrl = `/plswk/TelemetryCrud?key=${encodeURIComponent(key)}`;

        const keyRow = document.createElement('tr');
        keyRow.innerHTML = `<td class="p-2 border-b border-white/5 font-semibold text-slate-400">Internal Key</td><td class="p-2 border-b border-white/5 font-mono text-slate-100 break-all">${escapeHtml(key)}</td>`;
        body.appendChild(keyRow);

        const linkRow = document.createElement('tr');
        linkRow.innerHTML = `<td class="p-2 border-b border-white/5 font-semibold text-slate-400">Historical Data</td><td class="p-2 border-b border-white/5"><a href="${crudUrl}" class="inline-flex items-center gap-1.5 text-sky-400 hover:text-sky-300 hover:underline font-mono"><i class="fas fa-history text-[0.7rem]"></i><span>Open in Data view</span></a></td>`;
        const linkEl = linkRow.querySelector('a');
        if (linkEl) {
            linkEl.addEventListener('click', () => {
                // Close the details modal so it doesn't linger over the navigated view.
                try { (window as any).closeTelemetryDetails?.(); } catch { /* ignore */ }
            });
        }
        body.appendChild(linkRow);
    };

    try {
        const response = await fetch(`/plswk/api/properties?key=${encodeURIComponent(key)}`);
        const props = await response.json();

        if (body) renderInternalRows();

        if (Array.isArray(props) && props.length > 0) {
            if (body) {
                props.forEach(p => {
                    const tr = document.createElement('tr');
                    tr.innerHTML = `<td class="p-2 border-b border-white/5 font-semibold text-slate-400">${escapeHtml(p.name || '')}</td><td class="p-2 border-b border-white/5 font-mono text-slate-100">${escapeHtml(p.value || '')}</td>`;
                    body.appendChild(tr);
                });
            }
            if (table) table.classList.remove('hidden');
            return props;
        } else {
            // Still show the table because the internal key / link rows are always present.
            if (table) table.classList.remove('hidden');
            return [];
        }
    } catch (e) {
        // Even on failure, show the internal key + link rows.
        if (body) renderInternalRows();
        if (table) table.classList.remove('hidden');
        console.error("Props load failed", e);
        return [];
    } finally {
        if (loader) loader.classList.add('hidden');
    }
}

// --- History reload & refresh ---
async function reloadHistory(): Promise<void> {
    const range = historyTw ? historyTw.getRange() : { startTs: Date.now() - 3600000, endTs: Date.now(), mode: 'realtime' };
    const days = (range.endTs - range.startTs) / 86400000;
    const loader = document.getElementById('chartLoading')!;
    if (loader) loader.classList.remove('hidden');
    
    try {
        let url = `/plswk/api/history?key=${encodeURIComponent(currentHistoryKey)}`;
        if (range.mode === 'history') {
            url += `&startTs=${range.startTs}&endTs=${range.endTs}`;
        } else {
            url += `&days=${days}`;
        }
        
        const response = await fetch(url);
        const data = await response.json();
        
        renderChart(data);
    } catch (e) {
        console.error("History load failed", e);
    } finally {
        if (loader) loader.classList.add('hidden');
    }
}

/**
 * Builds an ordinal → label map from history points that carry a categorical `valueStr`.
 * Returns an empty map for plain numeric series (no string labels present), in which case
 * the chart is rendered as a normal numeric line.
 */
function buildOrdinalLabelMap(data: any[]): Map<number, string> {
    const map = new Map<number, string>();
    for (const d of data) {
        if (d == null) continue;
        const label = d.valueStr;
        const ordinal = d.value;
        if (label == null || ordinal == null) continue;
        const rounded = Math.round(ordinal);
        if (!map.has(rounded)) map.set(rounded, String(label));
    }
    return map;
}

/**
 * Builds the ApexCharts options for a history dataset. Detects categorical (enum/boolean)
 * series — where the backend encodes each distinct string state to a numeric ordinal in
 * `value` while keeping the original label in `valueStr` — and renders them as a discrete
 * step chart with the original state names on the Y axis / tooltip. Plain numeric series
 * fall back to a smooth numeric line. Shared by initial render and live refresh so both
 * paths stay consistent.
 */
function buildChartOptions(data: any[]): any {
    const ordinalToLabel = buildOrdinalLabelMap(data);
    const isCategoricalSeries = ordinalToLabel.size > 0;

    return {
        series: [{
            name: document.getElementById('telTitle')?.textContent || 'Telemetry Details',
            data: data.map(d => ({ x: new Date(d.ts).getTime(), y: d.value }))
        }],
        chart: {
            type: 'area',
            height: '100%',
            foreColor: '#94a3b8',
            toolbar: { show: false },
            zoom: { enabled: false },
            animations: { enabled: false },
            accessibility: { enabled: false }
        },
        colors: ['#38bdf8'],
        fill: {
            type: 'gradient',
            gradient: {
                shadeIntensity: 1,
                opacityFrom: 0.45,
                opacityTo: 0.05,
                stops: [20, 100]
            }
        },
        stroke: { curve: isCategoricalSeries ? 'stepline' : 'straight', width: 2 },
        dataLabels: { enabled: false },
        xaxis: {
            type: 'datetime',
            labels: { datetimeUTC: false },
            axisBorder: { show: false },
            axisTicks: { show: false }
        },
        yaxis: isCategoricalSeries
            ? {
                min: 0,
                max: ordinalToLabel.size - 1,
                tickAmount: Math.max(1, ordinalToLabel.size - 1),
                labels: {
                    formatter: (val: any) => ordinalToLabel.get(Math.round(val)) ?? ''
                }
            }
            : {
                labels: {
                    formatter: (val: any) => formatNumber(val, 2)
                }
            },
        tooltip: {
            theme: 'dark',
            x: { format: 'dd MMM HH:mm:ss' },
            ...(isCategoricalSeries
                ? {
                    y: {
                        formatter: (val: any) => ordinalToLabel.get(Math.round(val)) ?? String(val)
                    }
                }
                : {})
        },
        grid: {
            borderColor: 'rgba(255,255,255,0.05)',
            strokeDashArray: 4
        }
    };
}

function renderChart(data: any[]): void {
    // Destroy previous chart instance if it exists to avoid memory leaks and hover flickering
    if (historyChart) {
        try {
            historyChart.destroy();
        } catch (e) {
            console.error("Failed to destroy previous chart:", e);
        }
        historyChart = null;
    }

    const options = buildChartOptions(data);
    const container = document.getElementById('historyChart')!;
    container.innerHTML = '';
    historyChart = new ApexCharts(container, options);
    historyChart.render();
}

function startHistoryRefresh(): void {
    stopHistoryRefresh();
    historyRefreshTimer = setInterval(refreshHistoryData, 10_000);
}

function stopHistoryRefresh(): void {
    if (historyRefreshTimer) {
        clearInterval(historyRefreshTimer);
        historyRefreshTimer = null;
    }
}

async function refreshHistoryData(): Promise<void> {
    if (!currentHistoryKey) return;
    if (document.getElementById('telemetryDetailsModal')?.style.display !== 'flex') return;
    const range = historyTw ? historyTw.getRange() : { startTs: Date.now() - 3600000, endTs: Date.now(), mode: 'realtime' };
    
    // In history mode, we don't auto-refresh because the data is static
    if (range.mode === 'history') return;

    const days = (range.endTs - range.startTs) / 86400000;

    try {
        // Fetch updated history + current value in parallel
        const [histRes, valRes] = await Promise.all([
            fetch(`/plswk/api/history?key=${encodeURIComponent(currentHistoryKey)}&days=${days}`),
            fetch(`/plswk/api/latest-value/${encodeURIComponent(currentHistoryKey)}`)
        ]);

        const data = await histRes.json();
        const vals = await valRes.json();

        // Update live value display
        const raw = vals?.[currentHistoryKey];
        if (raw != null) {
            const meta = resolveKeyMeta(currentHistoryKey);
            const lvEl = document.getElementById('telLiveValue');
            if (lvEl) lvEl.textContent = typeof PulswerkValue !== 'undefined' ? PulswerkValue.formatDisplay(raw, meta.type) : String(raw);
        }

        // Update chart without a full redraw. Recompute the categorical-aware options so
        // enum/boolean series stay correct on live refresh — e.g. when a brand-new state
        // appears the Y-axis range, tick count and label/tooltip formatters expand to include
        // it, and the step-vs-line curve adapts if the series type changes. updateOptions
        // animates smoothly (no flicker) and we pass the fresh series in the same call.
        if (historyChart) {
            const opts = buildChartOptions(data);
            historyChart.updateOptions(
                {
                    series: opts.series,
                    stroke: opts.stroke,
                    yaxis: opts.yaxis,
                    tooltip: opts.tooltip
                },
                false, // redrawPaths
                false  // animate
            );
        }
    } catch (e) {
        console.error('History refresh failed:', e);
    }
}

// --- Stepper & Toggle input helpers ---
function step(n: number): void {
    const input = document.getElementById('editValue') as HTMLInputElement;
    let stepVal = 1.0;
    if (currentEditKey) {
        const meta = resolveKeyMeta(currentEditKey);
        if ((window as any).isTemperatureUnit(meta.units)) {
            stepVal = 0.5;
        }
    }
    const current = parseFloat(input.value) || 0;
    const nextVal = current + (n * stepVal);
    input.value = String(Math.round(nextVal * 10) / 10);
}

function updateBoolLabel(): void {
    const input = document.getElementById('boolInput') as HTMLInputElement;
    if (!input) return;
    const label = document.getElementById('boolLabel');
    if (label) {
        label.textContent = input.checked ? 'ON' : 'OFF';
        label.className = 'bool-toggle-label ' + (input.checked ? 'text-sky-400' : 'text-slate-500');
    }
}

function startTelemetryEdit(): void {
    const viewMode = document.getElementById('telValueViewMode');
    if (viewMode) viewMode.classList.add('hidden');
    const editMode = document.getElementById('telValueEditMode');
    if (editMode) editMode.classList.remove('hidden');
}

function cancelTelemetryEdit(): void {
    const viewMode = document.getElementById('telValueViewMode');
    if (viewMode) viewMode.classList.remove('hidden');
    const editMode = document.getElementById('telValueEditMode');
    if (editMode) editMode.classList.add('hidden');
    
    // Reset inputs
    const telLiveValue = document.getElementById('telLiveValue');
    const liveValText = telLiveValue ? telLiveValue.textContent || '' : '';
    const key = currentEditKey;
    if (key) {
        const meta = resolveKeyMeta(key);
        const type = meta.type || '';
        const enums = meta.enumValues || null;
        if (enums && Object.keys(enums).length > 0) {
            const sel = document.getElementById('enumSelect') as HTMLSelectElement;
            if (sel) {
                const matchOpt = Array.from(sel.options).find(o => o.textContent === liveValText);
                if (matchOpt) sel.value = matchOpt.value;
                else sel.value = liveValText;
            }
        } else if (type && PulswerkValue.isBinary(type)) {
            const input = document.getElementById('boolInput') as HTMLInputElement;
            if (input) {
                input.checked = PulswerkValue.parseDisplay(liveValText, type) !== 0;
                updateBoolLabel();
            }
        } else {
            const editInput = document.getElementById('editValue') as HTMLInputElement;
            if (editInput) {
                editInput.value = String(parseFloat(liveValText) || 0);
            }
        }
    }
}

// --- Submit Writable Value Changes ---
async function submitEdit(e?: Event): Promise<void> {
    if (e) e.preventDefault();
    const btn = document.getElementById('saveBtn') as HTMLButtonElement;
    const status = document.getElementById('editStatus');
    if (btn) {
        btn.disabled = true;
        btn.innerHTML = '<i class="fas fa-spinner fa-spin"></i> <span>Sending...</span>';
    }
    if (status) {
        status.innerHTML = '<i class="fas fa-sync-alt fa-spin"></i> Sending update to device...';
        status.className = 'status-msg info';
    }
    
    let value = 0;
    const stepperGroup = document.getElementById('inlineStepperGroup');
    const enumGroup = document.getElementById('inlineEnumGroup');
    if (stepperGroup && stepperGroup.style.display !== 'none') {
        const editInput = document.getElementById('editValue') as HTMLInputElement;
        value = parseFloat(editInput ? editInput.value : '0') || 0;
    } else if (enumGroup && enumGroup.style.display !== 'none') {
        const sel = document.getElementById('enumSelect') as HTMLSelectElement;
        value = parseInt(sel ? sel.value : '0', 10) || 0;
        const meta = resolveKeyMeta(currentEditKey!);
        if (meta && meta.type && meta.type.includes('MULTI_STATE')) {
            value += 1;
        }
    } else {
        const input = document.getElementById('boolInput') as HTMLInputElement;
        value = (input && input.checked) ? 1 : 0;
    }
    
    const token = (document.querySelector('input[name="__RequestVerificationToken"]') as HTMLInputElement)?.value || '';
    
    try {
        const response = await fetch('/plswk/api/write', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token
            },
            body: JSON.stringify({ key: currentEditKey, value: value })
        });
        
        if (response.ok) {
            const result = await response.json();
            if (result.success) {
                if (status) {
                    status.innerHTML = '<i class="fas fa-check-circle"></i> Success! Value updated.';
                    status.className = 'status-msg success';
                }
                if (btn) btn.innerHTML = '<i class="fas fa-check"></i> <span>Updated</span>';
                
                // Snappy local UI update
                const meta = resolveKeyMeta(currentEditKey!);
                const formatted = typeof PulswerkValue !== 'undefined' ? PulswerkValue.formatDisplay(value, meta.type) : String(value);
                const telLiveValue = document.getElementById('telLiveValue');
                if (telLiveValue) telLiveValue.textContent = formatted;
                
                // If history chart is visible, reload chart with a slight delay
                if (historyChart) {
                    setTimeout(reloadHistory, 1000);
                }
                
                setTimeout(() => {
                    btn.disabled = false;
                    btn.innerHTML = '<i class="fas fa-save"></i> <span data-i18n="btn_save_changes">Save Changes</span>';
                    status.textContent = '';
                    status.className = 'status-msg';
                    cancelTelemetryEdit();
                }, 1500);
            } else {
                status.innerHTML = '<i class="fas fa-exclamation-triangle"></i> Write failed on device';
                status.className = 'status-msg error';
                btn.disabled = false;
                btn.innerHTML = '<i class="fas fa-save"></i> <span data-i18n="btn_save_changes">Save Changes</span>';
            }
        } else {
            const err = await response.text();
            status.innerHTML = '<i class="fas fa-bug"></i> Error: ' + err;
            status.className = 'status-msg error';
            btn.disabled = false;
            btn.innerHTML = '<i class="fas fa-save"></i> <span data-i18n="btn_save_changes">Save Changes</span>';
        }
    } catch (e) {
        status.innerHTML = '<i class="fas fa-wifi"></i> Failed to connect';
        status.className = 'status-msg error';
        btn.disabled = false;
        btn.innerHTML = '<i class="fas fa-save"></i> <span data-i18n="btn_save_changes">Save Changes</span>';
    }
}

// --- Weekly Switch Schedule View/Editor Helpers ---
async function loadScheduleForDetails(key: string, props: any[]): Promise<void> {
    const grid = document.getElementById('scheduleGrid');
    const loading = document.getElementById('scheduleLoading');
    const view = document.getElementById('scheduleView');
    
    currentScheduleKey = key;
    isEditingSchedule = false;
    scheduleValueType = 'real';
    scheduleStates = null;
    toggleScheduleEdit(false);
    
    if (loading) loading.classList.remove('hidden');
    if (view) view.classList.add('hidden');
    if (grid) grid.innerHTML = '';
    
    try {
        const schedProp = props.find((p: any) => p.name === 'Weekly Schedule');
        
        const typeProp = props.find((p: any) => p.name === '_scheduleValueType');
        if (typeProp) scheduleValueType = typeProp.value;
        
        const statesProp = props.find((p: any) => p.name === '_scheduleStates');
        if (statesProp) {
            try { statesProp.value && (scheduleStates = JSON.parse(statesProp.value)); } catch(e) {}
        }
        
        if (scheduleValueType === 'boolean' && !scheduleStates) {
            scheduleStates = ['Off', 'On'];
        }
        
        if (loading) loading.classList.add('hidden');
        if (view) view.classList.remove('hidden');
        
        currentScheduleData = [0,1,2,3,4,5,6].map(i => ({ dayIndex: i, entries: [] }));
        
        if (schedProp && schedProp.value && schedProp.value !== 'Empty Schedule' && schedProp.value !== 'None') {
            let parsedJson = false;
            try {
                const schedData = JSON.parse(schedProp.value);
                if (schedData && schedData.Days) {
                    const dayMap: Record<string, number> = { "Monday": 0, "Tuesday": 1, "Wednesday": 2, "Thursday": 3, "Friday": 4, "Saturday": 5, "Sunday": 6 };
                    schedData.Days.forEach((d: any) => {
                        const idx = dayMap[d.Day];
                        if (idx !== undefined && d.Entries) {
                            currentScheduleData[idx].entries = d.Entries.map((e: any) => {
                                let val = parseFloat(e.Value);
                                if (scheduleValueType === 'enumerated') val -= 1;
                                return { time: e.Time, value: val };
                            });
                        }
                    });
                    parsedJson = true;
                }
            } catch (e) {}
            
            if (!parsedJson) {
                const dayNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
                const days = schedProp.value.split(' | ');
                days.forEach((dayStr: string) => {
                    const parts = dayStr.split(': ');
                    if (parts.length < 2) return;
                    const dayIdx = dayNames.indexOf(parts[0]);
                    if (dayIdx === -1) return;
                    
                    const timesStr = parts[1];
                    currentScheduleData[dayIdx].entries = timesStr.split(', ').map((t: string) => {
                        const [time, val] = t.split('➔');
                        let v = parseFloat(val);
                        if (scheduleValueType === 'enumerated') v -= 1;
                        return { time, value: v };
                    });
                });
            }
        }
        
        renderSchedule();
        
    } catch (error) {
        console.error("Schedule load error:", error);
        if (grid) grid.innerHTML = '<div class="text-center p-8 text-red-400">Failed to load schedule from device.</div>';
    }
}

function formatScheduleValue(val: any): any {
    if (scheduleStates && val >= 0 && val < scheduleStates.length) {
        return scheduleStates[val];
    }
    return val;
}

function renderScheduleValueInput(dayIndex: number, entryIndex: number, value: any): string {
    if (scheduleValueType === 'boolean') {
        const opts = (scheduleStates || ['Off', 'On']).map((s, i) => 
            `<option value="${i}" ${i === value ? 'selected' : ''}>${s}</option>`
        ).join('');
        const col = value ? '#38bdf8' : '#94a3b8';
        return `<select style="background:#1e293b;color:${col};font-weight:700;font-size:0.7rem;border:1px solid #475569;border-radius:4px;padding:2px 4px;outline:none"
                        onchange="updateScheduleEntry(${dayIndex}, ${entryIndex}, 'value', parseInt(this.value))">${opts}</select>`;
    }
    
    if (scheduleValueType === 'enumerated' && scheduleStates) {
        const options = scheduleStates.map((s, i) => 
            `<option value="${i}" ${i === value ? 'selected' : ''}>${s}</option>`
        ).join('');
        return `<select style="background:#1e293b;color:#38bdf8;font-weight:700;font-size:0.7rem;border:1px solid #475569;border-radius:4px;padding:2px 4px;outline:none"
                        onchange="updateScheduleEntry(${dayIndex}, ${entryIndex}, 'value', parseInt(this.value))">${options}</select>`;
    }
    
    return `<input type="number" step="0.1" class="bg-transparent text-sky-400 font-bold text-[0.7rem] border-none focus:ring-0 w-10 p-0 text-center" 
                   value="${value}" onchange="updateScheduleEntry(${dayIndex}, ${entryIndex}, 'value', parseFloat(this.value))">`;
}

function renderSchedule(): void {
    const grid = document.getElementById('scheduleGrid')!;
    grid.innerHTML = '';
    
    currentScheduleData.forEach(day => {
        const dayNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
        const dayRow = document.createElement('div');
        dayRow.className = 'sched-day-row';
        
        let entriesHtml = day.entries.map((e: any, idx: number) => {
            if (isEditingSchedule) {
                return `
                    <div class="sched-entry-edit">
                        <input type="time" class="bg-transparent text-slate-300 text-[0.7rem] border-none focus:ring-0 w-[4.5rem] p-0" 
                               value="${e.time}" onchange="updateScheduleEntry(${day.dayIndex}, ${idx}, 'time', this.value)">
                        ${renderScheduleValueInput(day.dayIndex, idx, e.value)}
                        <button class="text-red-400 hover:text-red-300 px-1 text-sm leading-none" onclick="removeScheduleEntry(${day.dayIndex}, ${idx})">&times;</button>
                    </div>
                `;
            } else {
                return `
                    <div class="sched-entry-view">
                        <span class="text-slate-400"><i class="far fa-clock mr-1 opacity-70"></i>${e.time}</span>
                        <span class="font-bold text-sky-400 border-l border-white/10 pl-2">${formatScheduleValue(e.value)}</span>
                    </div>
                `;
            }
        }).join('');
        
        if (isEditingSchedule) {
            entriesHtml += `
                <button class="sched-add-btn" onclick="addScheduleEntry(${day.dayIndex})">
                    <i class="fas fa-plus mr-1"></i> Add
                </button>
            `;
        }

        dayRow.innerHTML = `
            <div class="w-14 font-bold text-slate-300 pt-1.5 border-r border-slate-700 pr-2 shrink-0">${dayNames[day.dayIndex]}</div>
            <div class="flex-1 flex flex-wrap gap-2 items-center">
                ${entriesHtml || (isEditingSchedule ? '' : '<span class="text-slate-600 text-xs italic">No switching points</span>')}
            </div>
        `;
        grid.appendChild(dayRow);
    });
}

function toggleScheduleEdit(edit: boolean): void {
    isEditingSchedule = edit;
    const btnEdit = document.getElementById('btnEditSchedule');
    const btnActions = document.getElementById('editScheduleActions');
    const status = document.getElementById('scheduleStatus');
    const saveBtn = document.getElementById('scheduleSaveBtn') as HTMLButtonElement;
    if (btnEdit) btnEdit.classList.toggle('hidden', edit);
    if (btnActions) btnActions.classList.toggle('hidden', !edit);
    if (status) { status.textContent = ''; status.className = 'status-msg'; }
    if (saveBtn) { saveBtn.disabled = false; saveBtn.innerHTML = '<i class="fas fa-save mr-1"></i> <span>Save</span>'; }
    renderSchedule();
}

// Custom schedule entry hooks
function updateScheduleEntry(dayIdx: number, entryIdx: number, field: string, value: any): void {
    if (field === 'value') value = (scheduleValueType === 'real') ? parseFloat(value) : parseInt(value);
    currentScheduleData[dayIdx].entries[entryIdx][field] = value;
    if (field === 'value') renderSchedule();
}

function addScheduleEntry(dayIdx: number): void {
    const lastEntry = currentScheduleData[dayIdx].entries[currentScheduleData[dayIdx].entries.length - 1];
    const defaultValue = (scheduleValueType === 'boolean') ? 1 : (lastEntry ? lastEntry.value : 0);
    const newTime = lastEntry ? lastEntry.time : "08:00";
    currentScheduleData[dayIdx].entries.push({ time: newTime, value: defaultValue });
    renderSchedule();
}

function removeScheduleEntry(dayIdx: number, entryIdx: number): void {
    currentScheduleData[dayIdx].entries.splice(entryIdx, 1);
    renderSchedule();
}

async function saveSchedule(): Promise<void> {
    const btn = document.getElementById('scheduleSaveBtn') as HTMLButtonElement;
    const status = document.getElementById('scheduleStatus');
    if (!btn || !status) return;

    btn.disabled = true;
    btn.innerHTML = '<i class="fas fa-spinner fa-spin mr-1"></i> <span>Sending...</span>';
    status.innerHTML = '<i class="fas fa-sync-alt fa-spin"></i> Sending schedule to device...';
    status.className = 'status-msg info';

    try {
        currentScheduleData.forEach(day => {
            day.entries.sort((a: any, b: any) => a.time.localeCompare(b.time));
        });

        const payloadData = currentScheduleData.map(day => ({
            dayIndex: day.dayIndex,
            entries: day.entries.map((e: any) => {
                let v = e.value;
                if (scheduleValueType === 'enumerated') v += 1;
                return { time: e.time, value: v };
            })
        }));

        const token = (document.querySelector('input[name="__RequestVerificationToken"]') as HTMLInputElement)?.value || '';
        const response = await fetch('/plswk/api/write-complex', {
            method: 'POST',
            headers: { 
                'Content-Type': 'application/json',
                'RequestVerificationToken': token
            },
            body: JSON.stringify({
                key: currentScheduleKey,
                value: JSON.stringify(payloadData)
            })
        });

        if (response.ok) {
            const result = await response.json();
            if (result.success) {
                status.innerHTML = '<i class="fas fa-check-circle"></i> Success! Schedule updated.';
                status.className = 'status-msg success';
                btn.innerHTML = '<i class="fas fa-check mr-1"></i> <span>Updated</span>';
                setTimeout(() => {
                    toggleScheduleEdit(false);
                    status.textContent = '';
                    status.className = 'status-msg';
                }, 1200);
            } else {
                status.innerHTML = '<i class="fas fa-exclamation-triangle"></i> Write failed on device';
                status.className = 'status-msg error';
                btn.disabled = false;
                btn.innerHTML = '<i class="fas fa-save mr-1"></i> <span>Save</span>';
            }
        } else {
            const err = await response.text();
            status.innerHTML = '<i class="fas fa-bug"></i> Error: ' + err;
            status.className = 'status-msg error';
            btn.disabled = false;
            btn.innerHTML = '<i class="fas fa-save mr-1"></i> <span>Save</span>';
        }
    } catch (error) {
        console.error("Save error:", error);
        status.innerHTML = '<i class="fas fa-wifi"></i> Failed to connect';
        status.className = 'status-msg error';
        btn.disabled = false;
        btn.innerHTML = '<i class="fas fa-save mr-1"></i> <span>Save</span>';
    }
}

// --- Utils ---
function renderModalBreadcrumb(id: string, path: any[]): void {
    const container = document.getElementById(id);
    if (!container) return;
    container.innerHTML = '';
    
    path.forEach((p, index) => {
        const span = document.createElement('span');
        span.textContent = p.name;
        container.appendChild(span);
        
        if (index < path.length - 1) {
            const sep = document.createElement('span');
            sep.className = 'sep';
            sep.textContent = ' / ';
            container.appendChild(sep);
        }
    });
}

// --- Compatibility Shims ---
(window as any).openHistory = (key: string) => openTelemetryDetails(key);
(window as any).openEdit = (key: string) => openTelemetryDetails(key);
(window as any).openProperties = (key: string) => openTelemetryDetails(key);
(window as any).openScheduleView = (key: string) => openTelemetryDetails(key);
(window as any).openTelemetryDetails = openTelemetryDetails;
(window as any).closeTelemetryDetails = closeTelemetryDetails;
(window as any).closeHistory = closeTelemetryDetails;
(window as any).closeEdit = closeTelemetryDetails;
(window as any).closeProperties = closeTelemetryDetails;
(window as any).closeSchedule = closeTelemetryDetails;

// Expose internal hooks
(window as any).reloadHistory = reloadHistory;
(window as any).startHistoryRefresh = startHistoryRefresh;
(window as any).stopHistoryRefresh = stopHistoryRefresh;
(window as any).refreshHistoryData = refreshHistoryData;
(window as any).step = step;
(window as any).updateBoolLabel = updateBoolLabel;
(window as any).submitEdit = submitEdit;
(window as any).startTelemetryEdit = startTelemetryEdit;
(window as any).cancelTelemetryEdit = cancelTelemetryEdit;
(window as any).toggleScheduleEdit = toggleScheduleEdit;
(window as any).updateScheduleEntry = updateScheduleEntry;
(window as any).addScheduleEntry = addScheduleEntry;
(window as any).removeScheduleEntry = removeScheduleEntry;
(window as any).saveSchedule = saveSchedule;
(window as any).filterTelemetrySidebar = filterTelemetrySidebar;
(window as any).switchTelemetryTab = switchTelemetryTab;
