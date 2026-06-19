import { useState, useEffect } from 'preact/hooks';
import { DashboardService } from '../dashboards/api';
import { t } from '../i18n';

interface DashboardsPageProps {
    dashboardId?: string;
    slug?: string;
}

export function DashboardsPage({ dashboardId, slug }: DashboardsPageProps) {
    const [dashboards, setDashboards] = useState<any[]>([]);
    const [loading, setLoading] = useState(true);
    
    // Silence unused variable compiler warnings
    if (slug) {}
    if (dashboards.length) {}
    
    const editMode = new URLSearchParams(window.location.search).get('edit') === 'true';

    useEffect(() => {
        // Load dashboards list if not in detail mode or to populate list
        const loadList = async () => {
            try {
                const list = await DashboardService.fetchDashboardList();
                setDashboards(list || []);
            } catch (e) {
                console.error("Failed to load dashboards list:", e);
            } finally {
                setLoading(false);
            }
        };
        loadList();
    }, [dashboardId]);

    useEffect(() => {
        if (loading) return;

        // If in detail mode, trigger core.ts to initialize
        if (dashboardId) {
            const bootstrapDashboard = async () => {
                try {
                    // Fetch dashboard detail
                    const response = await fetch(`/plswk/api/dashboards/${dashboardId}`);
                    if (!response.ok) throw new Error("Dashboard not found");
                    const dashDetail = await response.json();

                    // Call window.initDashboards
                    if (typeof (window as any).initDashboards === 'function') {
                        (window as any).initDashboards(dashDetail, editMode);
                    }
                } catch (err) {
                    console.error("Error bootstrapping dashboard detail:", err);
                }
            };
            bootstrapDashboard();
        } else {
            // List mode
            if (typeof (window as any).loadList === 'function') {
                (window as any).loadList();
            }
        }

        // Cleanup on unmount or navigation
        return () => {
            const store = (window as any).DashboardStore;
            if (store) {
                if (store.pollTimer) {
                    if (typeof store.pollTimer === 'function') {
                        store.pollTimer(); // unsubscribe
                    } else {
                        clearInterval(store.pollTimer);
                    }
                    store.pollTimer = null;
                }
                if (store.grid) {
                    try {
                        store.grid.destroy(false);
                    } catch (e) {}
                    store.grid = null;
                }
            }
        };
    }, [dashboardId, editMode, loading]);

    if (loading) {
        return (
            <div class="h-full flex items-center justify-center p-16">
                <div class="flex flex-col items-center gap-4 text-cyan-400">
                    <i class="fas fa-spinner fa-spin text-3xl"></i>
                    <p class="text-sm font-medium animate-pulse">{t('loading')}</p>
                </div>
            </div>
        );
    }

    return (
        <div class="w-full page-enter">
            {/* ═══════════════════ LIST MODE ═══════════════════ */}
            <div id="listMode" style={{ display: dashboardId ? 'none' : '' }} data-testid="dash-list-mode">
                <div class="flex justify-between items-center mb-6">
                    <p class="text-slate-400 text-sm">Create and manage custom data dashboards.</p>
                    <button class="btn-primary auth-edit-dash-only" onClick={() => (window as any).createDashboard()} data-testid="dash-create-btn">
                        <i class="fas fa-plus"></i> New Dashboard
                    </button>
                </div>
                <div id="dashGrid" class="grid grid-cols-[repeat(auto-fill,minmax(320px,1fr))] gap-5" data-testid="dash-grid"></div>
                <div id="emptyDashboards" class="hidden flex-col items-center justify-center gap-4 text-center text-slate-400 p-16 glass rounded-2xl">
                    <i class="fas fa-th-large text-5xl opacity-30 text-sky-400"></i>
                    <p class="text-sm">No dashboards yet.</p>
                    <button class="btn-primary btn-sm auth-edit-dash-only" onClick={() => (window as any).createDashboard()}>
                        <i class="fas fa-plus"></i> Create your first dashboard
                    </button>
                </div>
            </div>

            {/* ═══════════════════ VIEW / EDIT MODE ═══════════════════ */}
            <div id="dashMode" class="relative min-h-[calc(100vh-120px)]" style={{ display: dashboardId ? '' : 'none' }} data-testid="dash-edit-mode">
                <div class="flex items-center gap-4 mb-4 px-4 py-3 rounded-xl bg-slate-800/50 border border-white/[0.06]" data-testid="dash-toolbar">
                    <a href="/plswk/Dashboards" class="text-white no-underline text-xl font-bold tracking-tight transition-colors duration-200 hover:text-sky-400" data-i18n="nav_dashboards">Dashboards</a>
                    <i class="fas fa-chevron-right text-[0.55rem] text-slate-600 animate-fade-in" id="dashBreadcrumbSep" style={{ display: 'none' }}></i>
                    <div class="flex flex-col flex-1 min-w-0 gap-0.5">
                        <div class="flex items-center">
                            <input type="text" class="bg-transparent border-0 text-white text-lg font-bold outline-none w-full" id="dashTitle" style={{ display: 'none' }} placeholder="Dashboard name" />
                            <span class="text-lg font-bold text-ellipsis overflow-hidden whitespace-nowrap" id="dashTitleView"></span>
                        </div>
                        <div class="flex items-center">
                            <input type="text" class="bg-transparent border-0 text-slate-400 text-xs outline-none w-full" id="dashDesc" style={{ display: 'none' }} placeholder="Dashboard description (optional)" />
                            <span class="text-xs text-slate-400 text-ellipsis overflow-hidden whitespace-nowrap" id="dashDescView"></span>
                        </div>
                    </div>
                    <button class="btn-icon auth-edit-fav-only" id="btnFavDash" onClick={() => (window as any).toggleFavoriteDash((window as any).dashboard?.id)} title="Favorite">
                        <i class="far fa-star"></i>
                    </button>

                    {/* Timewindow selector (reusable module) */}
                    <div id="dashTwContainer"></div>

                    <div class="flex gap-2" id="editButtons">
                        <button class="btn-ghost auth-edit-dash-only" id="btnAddWidget" onClick={() => (window as any).openAddWidget()} style={{ display: 'none' }} data-testid="dash-add-widget-btn"><i class="fas fa-plus"></i> Add Widget</button>
                        <button class="btn-ghost auth-edit-dash-only" id="btnEdit" onClick={() => (window as any).enterEditMode()} data-testid="dash-edit-btn"><i class="fas fa-pen"></i> Edit</button>
                        <button class="btn-primary btn-sm auth-edit-dash-only" id="btnSave" onClick={() => (window as any).saveDashboard()} style={{ display: 'none' }} data-testid="dash-save-btn"><i class="fas fa-save"></i> Save</button>
                        <button class="btn-ghost auth-edit-dash-only" id="btnCancel" onClick={() => (window as any).cancelEdit()} style={{ display: 'none' }} data-testid="dash-cancel-btn"><i class="fas fa-times"></i></button>
                    </div>
                </div>

                <div id="dashCanvas" class="dash-canvas">
                    <div id="scadaBg" class="scada-bg-layer animate-fade-in"></div>
                    <div id="scadaPoints" class="scada-points-layer animate-fade-in"></div>
                    <div class="grid-stack" id="dashGrid2"></div>
                </div>

                <div id="emptyDash" class="absolute inset-0 flex items-center justify-center pointer-events-none" style={{ display: 'none' }}>
                    <div class="flex flex-col items-center justify-center gap-4 text-center text-slate-400 p-16 glass rounded-2xl max-w-[500px] w-full pointer-events-auto shadow-2xl">
                        <i class="fas fa-puzzle-piece text-5xl opacity-30 text-sky-400 animate-pulse"></i>
                        <p class="text-sm">This dashboard has no widgets yet.</p>
                        <button class="btn-primary btn-sm auth-edit-dash-only" onClick={() => { (window as any).enterEditMode(); (window as any).openAddWidget(); }}><i class="fas fa-plus"></i> Add your first widget</button>
                    </div>
                </div>
            </div>

            {/* ═══════════════════ CREATE DASHBOARD MODAL ═══════════════════ */}
            <div class="fixed inset-0 bg-slate-900/90 backdrop-blur-xl hidden items-center justify-center z-[1000] p-8" id="createModal" data-testid="create-dash-modal">
                <div class="bg-slate-800 border border-white/10 rounded-2xl w-full max-w-[420px] flex flex-col p-8 relative shadow-2xl">
                    <div class="flex justify-between items-center mb-6">
                        <h2 class="text-xl font-extrabold">New Dashboard</h2>
                        <button class="close-modal bg-white/5 border border-white/10 text-white w-8 h-8 rounded-full flex items-center justify-center cursor-pointer transition-all duration-300 p-0 leading-none hover:bg-red-500/20 hover:text-red-500 hover:border-red-500" onClick={() => { const el = document.getElementById('createModal'); if (el) el.style.display = 'none'; }}>&times;</button>
                    </div>
                    <div class="mb-5">
                        <label class="block text-[0.72rem] font-semibold text-slate-400 uppercase tracking-wide mb-1.5">Name</label>
                        <input class="form-input" id="newDashName" placeholder="e.g. HVAC Overview" autofocus />
                    </div>
                    <div class="mb-5">
                        <label class="block text-[0.72rem] font-semibold text-slate-400 uppercase tracking-wide mb-1.5">Description (optional)</label>
                        <input class="form-input" id="newDashDesc" placeholder="Main building monitoring" />
                    </div>
                    <button class="btn-primary w-full shadow-lg" onClick={() => (window as any).confirmCreate()}><i class="fas fa-plus"></i> Create</button>
                </div>
            </div>

            {/* ═══════════════════ ADD WIDGET MODAL ═══════════════════ */}
            <div class="fixed inset-0 bg-slate-900/90 backdrop-blur-xl hidden items-center justify-center z-[1000] p-8" id="addWidgetModal" data-testid="add-widget-modal">
                <div class="bg-slate-800 border border-white/10 rounded-2xl w-full max-w-[1400px] max-h-[85vh] flex flex-col p-8 relative shadow-2xl">
                    <div class="flex justify-between items-center mb-6 shrink-0">
                        <h2 id="widgetModalTitle" class="text-xl font-extrabold">Add Widget</h2>
                        <button class="close-modal bg-white/5 border border-white/10 text-white w-8 h-8 rounded-full flex items-center justify-center cursor-pointer transition-all duration-300 p-0 leading-none hover:bg-red-500/20 hover:text-red-500 hover:border-red-500" onClick={() => (window as any).closeAddWidget()}>&times;</button>
                    </div>

                    <div class="overflow-y-auto flex-1 pr-2">
                        {/* Step 1: Type */}
                        <div class="mb-5">
                            <label class="block text-[0.72rem] font-semibold text-slate-400 uppercase tracking-wide mb-1.5">Widget Type</label>
                            <div class="wtype-grid grid grid-cols-5 gap-3">
                                <div class="wtype-card selected" data-type="timeseries" onClick={(e) => (window as any).selectWidgetType(e.currentTarget)}>
                                    <i class="fas fa-chart-line text-2xl text-sky-400 block mb-2"></i><span class="text-[0.78rem] font-semibold">Time Series</span>
                                </div>
                                <div class="wtype-card" data-type="latest-values" onClick={(e) => (window as any).selectWidgetType(e.currentTarget)}>
                                    <i class="fas fa-table text-2xl text-sky-400 block mb-2"></i><span class="text-[0.78rem] font-semibold">Latest Values</span>
                                </div>
                                <div class="wtype-card" data-type="single-value" onClick={(e) => (window as any).selectWidgetType(e.currentTarget)}>
                                    <i class="fas fa-digital-tachograph text-2xl text-sky-400 block mb-2"></i><span class="text-[0.78rem] font-semibold">Single Value</span>
                                </div>
                                <div class="wtype-card" data-type="scada-point" onClick={(e) => (window as any).selectWidgetType(e.currentTarget)}>
                                    <i class="fas fa-map-pin text-2xl text-sky-400 block mb-2"></i><span class="text-[0.78rem] font-semibold">Data Point</span>
                                </div>
                                <div class="wtype-card" data-type="background-svg" onClick={(e) => (window as any).selectWidgetType(e.currentTarget)}>
                                    <i class="fas fa-drafting-compass text-2xl text-sky-400 block mb-2"></i><span class="text-[0.78rem] font-semibold">Background SVG</span>
                                </div>
                            </div>
                        </div>

                        {/* Step 2: Title */}
                        <div class="mb-5">
                            <label class="block text-[0.72rem] font-semibold text-slate-400 uppercase tracking-wide mb-1.5">Title</label>
                            <input class="form-input" id="widgetTitle" placeholder="e.g. Zone Temperatures" value="" />
                        </div>

                        {/* Step 3: Keys */}
                        <div class="mb-5 relative" id="keyPickerWrapper">
                            <div class="flex justify-between items-center mb-1.5 key-picker-header">
                                <label class="block text-[0.72rem] font-semibold text-slate-400 uppercase tracking-wide">Telemetries</label>
                                <div class="flex items-center gap-1">
                                    <button class="btn-ghost btn-sm h-7 !px-2 !py-0 text-[0.65rem] font-bold" id="btnKeyPickerOpen" title="Select Keys" onClick={() => (window as any).openKeySelector()}>
                                        <i class="fas fa-plus mr-1"></i> SELECT KEYS
                                    </button>
                                </div>
                            </div>
                            <div class="max-h-[250px] overflow-y-auto border border-slate-700 rounded-lg transition-all duration-300" id="keyPicker">
                                <div class="sticky top-0 z-[1] p-2 bg-slate-800 border-b border-white/5 hidden" id="keySearchWrapper">
                                    <div class="flex items-center gap-2">
                                        <input type="text" placeholder="Search keys... (space = AND)" id="keySearch" onInput={() => (window as any).filterKeys()}
                                            class="flex-1 bg-white/5 border border-slate-700 text-white px-2.5 py-2 rounded text-xs outline-none focus:border-sky-400" />
                                        <div class="flex items-center gap-1">
                                            <button class="btn-primary btn-sm !px-3 !py-1 text-[0.65rem] font-bold uppercase tracking-wider shadow-md" onClick={(e) => (window as any).closeKeySelector(e)}>OK</button>
                                            <button class="btn-ghost btn-sm !px-3 !py-1 text-[0.65rem] font-bold uppercase tracking-wider" onClick={(e) => (window as any).cancelKeySelector(e)}>Cancel</button>
                                        </div>
                                    </div>
                                </div>
                                <div id="keyList"></div>
                            </div>
                        </div>

                        {/* Step 4: Options (timeseries-specific) */}
                        <div id="tsOptions">
                            <div class="flex gap-4 flex-wrap">
                                <div class="flex-1 min-w-[120px] mb-3">
                                    <label class="block text-[0.72rem] font-semibold text-slate-400 uppercase tracking-wide mb-1.5">Chart Type</label>
                                    <select class="form-select" id="optChartType">
                                        <option value="line">Line</option>
                                        <option value="bar">Bar</option>
                                    </select>
                                </div>
                                <div class="flex-1 min-w-[120px] mb-3 flex items-end">
                                    <label class="flex items-center gap-2 cursor-pointer text-[0.82rem] text-slate-400"><input type="checkbox" id="optStacked" class="accent-sky-400" /> Stacked</label>
                                </div>
                                <div class="flex-1 min-w-[120px] mb-3 flex items-end">
                                    <label class="flex items-center gap-2 cursor-pointer text-[0.82rem] text-slate-400"><input type="checkbox" id="optLegend" checked class="accent-sky-400" /> Show Legend</label>
                                </div>
                            </div>
                        </div>

                        {/* SVG upload (background-svg specific) */}
                        <div id="svgOptions" style={{ display: 'none' }}>
                            <div class="mb-4">
                                <label class="block text-[0.72rem] font-semibold text-slate-400 uppercase tracking-wide mb-1.5">SVG Source</label>
                                <div class="flex gap-3 mb-3">
                                    <label class="flex items-center gap-2 cursor-pointer text-[0.82rem] text-slate-400">
                                        <input type="radio" name="svgSource" value="drawio" checked class="accent-sky-400" />
                                        <i class="fas fa-drafting-compass text-sky-400"></i> draw.io
                                    </label>
                                    <label class="flex items-center gap-2 cursor-pointer text-[0.82rem] text-slate-400">
                                        <input type="radio" name="svgSource" value="import" class="accent-sky-400" />
                                        <i class="fas fa-cloud-upload-alt"></i> Import SVG
                                    </label>
                                </div>
                                <div id="svgImportArea" style={{ display: 'none' }}>
                                    <div class="flex flex-col items-center gap-2 py-4 rounded-xl border-2 border-dashed border-slate-600 cursor-pointer hover:border-sky-400 transition-colors mb-3" onClick={() => { const input = document.getElementById('svgFileInput'); if (input) input.click(); }}>
                                        <i class="fas fa-cloud-upload-alt text-2xl text-slate-500"></i>
                                        <span class="text-[0.78rem] font-semibold text-slate-400">Upload SVG file</span>
                                    </div>
                                </div>
                                <input type="file" id="svgFileInput" accept=".svg" class="hidden" onChange={(e) => (window as any).handleSvgFileSelect(e.currentTarget)} />
                                <div id="svgPreviewThumb" class="hidden rounded-lg border border-slate-700 p-3" style={{ maxHeight: '180px', overflow: 'hidden' }}></div>
                            </div>
                        </div>

                        {/* Animation rules (background-svg specific) */}
                        <div id="animRulesSection" style={{ display: 'none' }}>
                            <div class="mb-4">
                                <div class="flex justify-between items-center mb-1.5">
                                    <label class="block text-[0.72rem] font-semibold text-slate-400 uppercase tracking-wide">
                                        <i class="fas fa-magic" style={{ marginRight: '0.3rem', opacity: 0.5 }}></i> Animation Rules
                                    </label>
                                    <div class="flex gap-1">
                                        <button class="btn-ghost btn-sm h-7 !px-2 !py-0 text-[0.65rem] font-bold" onClick={(e) => { e.preventDefault(); (window as any).openAnimPreview(); }} title="Preview animations interactively">
                                            <i class="fas fa-play mr-1"></i> PREVIEW
                                        </button>
                                    </div>
                                </div>
                                <div id="animRuleList"></div>
                                <button class="anim-rule-add shadow" onClick={(e) => { e.preventDefault(); (window as any).addAnimRule(); }}>
                                    <i class="fas fa-plus"></i> Add Animation Rule
                                </button>
                            </div>
                        </div>

                        {/* Layout option (scada-point specific) */}
                        <div id="pointOptions" style={{ display: 'none' }}>
                            <div class="mb-4">
                                <label class="block text-[0.72rem] font-semibold text-slate-400 uppercase tracking-wide mb-1.5">Layout</label>
                                <div class="flex gap-3">
                                    <label class="flex items-center gap-2 cursor-pointer text-[0.82rem] text-slate-400"><input type="radio" name="pointLayout" value="vertical" checked class="accent-sky-400" /> <i class="fas fa-arrows-alt-v"></i> Vertical</label>
                                    <label class="flex items-center gap-2 cursor-pointer text-[0.82rem] text-slate-400"><input type="radio" name="pointLayout" value="horizontal" class="accent-sky-400" /> <i class="fas fa-arrows-alt-h"></i> Horizontal</label>
                                </div>
                            </div>
                        </div>
                    </div>

                    <button class="btn-primary w-full mt-4 shrink-0 shadow-lg" id="btnAddWidgetConfirm" onClick={() => (window as any).confirmAddWidget()}>
                        <i class="fas fa-plus"></i> <span id="widgetConfirmText">Add Widget</span>
                    </button>
                </div>
            </div>

            {/* ═══════════════════ SCADA INFO POPUP ═══════════════════ */}
            <div id="scadaPopup" class="scada-popup" style={{ display: 'none' }} onClick={(e) => e.stopPropagation()}>
                <div class="scada-popup-card shadow-2xl" id="scadaPopupContent"></div>
            </div>

            {/* ═══════════════════ ANIMATION PREVIEW POPUP ═══════════════════ */}
            <div id="scadaAnimPreview" class="scada-preview-popup" onClick={(e) => { if (e.target === e.currentTarget) (window as any).closeAnimPreview(); }}>
                <div class="scada-preview-panel shadow-2xl border border-white/10">
                    <div class="scada-preview-header">
                        <h3><i class="fas fa-magic" style={{ marginRight: '0.5rem', color: '#38bdf8', opacity: 0.7 }}></i> Animation Preview</h3>
                        <button class="close-modal bg-white/5" onClick={() => (window as any).closeAnimPreview()}>&times;</button>
                    </div>
                    <div class="scada-preview-body">
                        <div class="scada-preview-svg">
                            <div style={{ color: '#64748b', fontSize: '0.85rem' }}><i class="fas fa-drafting-compass" style={{ marginRight: '0.4rem' }}></i> SVG preview will load here</div>
                        </div>
                        <div class="scada-preview-controls">
                            <div style={{ flex: '0 0 auto' }}>
                                <label>SVG Element</label>
                                <select id="previewElementSelect" onChange={() => (window as any).onPreviewElementChange()}>
                                    <option value="">— Select Element —</option>
                                </select>
                            </div>
                            <div style={{ flex: '0 0 auto', alignSelf: 'flex-end' }}>
                                <button class="btn-ghost btn-sm" onClick={() => (window as any).clearPreviewClasses()}>
                                    <i class="fas fa-undo"></i> Clear All
                                </button>
                            </div>
                        </div>
                        <div style={{ gridColumn: '1/-1' }}>
                            <label style={{ fontSize: '0.72rem', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.05em', color: '#94a3b8', display: 'block', marginBottom: '0.5rem' }}>
                                Toggle Animation Classes
                            </label>
                            <div id="previewAnimChips" class="scada-anim-chips">
                                <span style={{ color: '#64748b', fontSize: '0.78rem' }}>Select an element above to see available classes</span>
                            </div>
                        </div>
                    </div>
                </div>
            </div>

            {/* ═══════════════════ SVG ELEMENT ID PICKER / RENAMER ═══════════════════ */}
            <div id="scadaIdPanel" class="scada-id-panel shadow-2xl" onClick={(e) => e.stopPropagation()}>
                <div class="scada-id-panel-header">
                    <h4>Elements (0)</h4>
                    <button class="close-modal bg-white/5" onClick={() => (window as any).closeIdPicker()}>&times;</button>
                </div>
                <div class="scada-id-panel-list"></div>
            </div>

            <div id="scadaRenamePopup" class="scada-rename-popup shadow-2xl" onClick={(e) => e.stopPropagation()}>
                <label class="block text-xs font-bold text-slate-400 mb-2">Rename SVG Element</label>
                <div class="scada-rename-tag mb-2">&lt;<span>g</span>&gt; id="something"</div>
                <input type="text" class="scada-rename-input w-full bg-slate-900 border border-slate-700 text-white rounded px-2.5 py-1.5 text-xs focus:border-sky-400 outline-none" placeholder="Enter new ID..." onKeyDown={(e: any) => { if (e.key === 'Enter') (window as any).confirmRenameElement(); if (e.key === 'Escape') (window as any).cancelRenameElement(); }} />
                <div class="scada-rename-actions mt-3 flex justify-end gap-2">
                    <button class="scada-rename-cancel px-3 py-1 text-xs hover:bg-white/5 rounded text-slate-300" onClick={() => (window as any).cancelRenameElement()}>Cancel</button>
                    <button class="scada-rename-ok px-3 py-1 text-xs bg-sky-600 hover:bg-sky-500 rounded text-white font-bold" onClick={() => (window as any).confirmRenameElement()}>Rename</button>
                </div>
            </div>
        </div>
    );
}
