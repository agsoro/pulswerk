import { useState, useEffect, useRef } from 'preact/hooks';
import { DashboardService } from '../dashboards/api';
import { t } from '../i18n';

const ROW_HEIGHT = 70; // px per row
const BUFFER_ROWS = 10;

export function TelemetryListPage() {
    const [allPoints, setAllPoints] = useState<any[]>([]);
    const [searchTerm, setSearchTerm] = useState('');
    const [sortField, setSortField] = useState('name');
    const [sortDir, setSortDir] = useState(1);
    const [scrollTop, setScrollTop] = useState(0);
    const [viewportHeight, setViewportHeight] = useState(500);
    const [liveValues, setLiveValues] = useState<Record<string, any>>({});
    const [loading, setLoading] = useState(true);

    const viewportRef = useRef<HTMLDivElement>(null);

    // Fetch initial points list
    useEffect(() => {
        const loadPoints = async () => {
            try {
                const response = await fetch('/plswk/api/telemetries?includeLiveValues=true');
                if (!response.ok) throw new Error("Failed to fetch telemetries");
                const data = await response.json();
                setAllPoints(data || []);

                // Put initial values in state
                const initialVals: Record<string, any> = {};
                data.forEach((p: any) => {
                    if (p.value !== undefined) {
                        initialVals[p.key] = p.value;
                    }
                });
                setLiveValues(initialVals);
            } catch (err) {
                console.error("Failed to load telemetry list:", err);
            } finally {
                setLoading(false);
            }
        };
        loadPoints();
    }, []);

    // Set up resize observer on viewport to dynamic measure height
    useEffect(() => {
        if (!viewportRef.current) return;
        const resizeObs = new ResizeObserver((entries) => {
            if (entries[0]) {
                setViewportHeight(entries[0].contentRect.height || 500);
            }
        });
        resizeObs.observe(viewportRef.current);
        return () => resizeObs.disconnect();
    }, []);

    // Perform search filtering
    const searchTerms = searchTerm.toLowerCase().split(/\s+/).filter(Boolean);
    const filteredPoints = allPoints.filter(p => {
        if (searchTerms.length === 0) return true;
        const name = (p.name || '').toLowerCase();
        const key = (p.key || '').toLowerCase();
        const device = (p.device || '').toLowerCase();
        const connection = (p.connection || '').toLowerCase();
        const type = (p.type || '').toLowerCase();
        return searchTerms.every(term => 
            name.includes(term) || 
            key.includes(term) || 
            device.includes(term) || 
            connection.includes(term) || 
            type.includes(term)
        );
    });

    // Apply sorting
    const sortedPoints = [...filteredPoints].sort((a, b) => {
        let valA = (a[sortField] || '').toString().toLowerCase();
        let valB = (b[sortField] || '').toString().toLowerCase();

        if (sortField === 'value') {
            const liveA = liveValues[a.key] !== undefined ? liveValues[a.key] : a.value;
            const liveB = liveValues[b.key] !== undefined ? liveValues[b.key] : b.value;
            valA = parseFloat(liveA) || 0;
            valB = parseFloat(liveB) || 0;
        }

        if (valA < valB) return -1 * sortDir;
        if (valA > valB) return 1 * sortDir;
        return 0;
    });

    // Subscribe to SSE updates for visible keys
    const totalCount = sortedPoints.length;
    let startIdx = Math.floor(scrollTop / ROW_HEIGHT) - BUFFER_ROWS;
    let endIdx = Math.ceil((scrollTop + viewportHeight) / ROW_HEIGHT) + BUFFER_ROWS;
    startIdx = Math.max(0, startIdx);
    endIdx = Math.min(totalCount - 1, endIdx);

    const visibleSlice = sortedPoints.slice(startIdx, endIdx + 1);
    const visibleKeys = visibleSlice.map(p => p.key).filter(Boolean);

    useEffect(() => {
        if (visibleKeys.length === 0) return;

        // Listen to updates
        const unsubscribe = DashboardService.listenToLiveUpdates(visibleKeys, (newData) => {
            setLiveValues(prev => ({ ...prev, ...newData }));
        });

        return () => {
            unsubscribe();
        };
    }, [JSON.stringify(visibleKeys)]);

    const handleSort = (field: string) => {
        if (sortField === field) {
            setSortDir(prev => prev * -1);
        } else {
            setSortField(field);
            setSortDir(1);
        }
    };

    const getSortIcon = (field: string) => {
        if (sortField !== field) return 'fa-sort opacity-20';
        return sortDir === 1 ? 'fa-sort-up text-cyan-400' : 'fa-sort-down text-cyan-400';
    };

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

    const totalHeight = totalCount * ROW_HEIGHT;

    return (
        <div class="h-[calc(100vh-110px)] flex flex-col gap-6 w-full page-enter">
            {/* Header / Stats */}
            <div class="flex flex-col md:flex-row items-stretch md:items-center justify-between gap-4 bg-slate-800/40 backdrop-blur-md border border-white/5 rounded-2xl p-5 shadow-2xl shrink-0">
                <div class="flex items-center gap-4">
                    <div class="w-12 h-12 rounded-xl bg-cyan-500/10 flex items-center justify-center text-cyan-400">
                        <i class="fas fa-list-ul text-xl"></i>
                    </div>
                    <div>
                        <h1 class="text-xl font-extrabold text-white tracking-tight">{t('nav_telemetries')}</h1>
                        <p class="text-[0.7rem] text-slate-400 uppercase tracking-widest font-semibold opacity-60">Real-time point overview</p>
                    </div>
                </div>

                <div class="flex items-center gap-4 flex-1 max-w-2xl">
                    <div class="relative flex-1">
                        <i class="fas fa-search absolute left-4 top-1/2 -translate-y-1/2 text-slate-500 text-sm"></i>
                        <input 
                            type="text" 
                            placeholder="Search by name, key, device..." 
                            value={searchTerm}
                            onInput={(e) => setSearchTerm(e.currentTarget.value)}
                            class="w-full bg-slate-900/40 border border-white/5 rounded-xl py-3 pl-12 pr-4 text-sm text-slate-200 placeholder:text-slate-600 focus:outline-none focus:border-cyan-500/50 focus:bg-slate-900/60 transition-all shadow-inner"
                        />
                    </div>
                    <div class="px-4 py-2 bg-slate-900/60 border border-white/5 rounded-xl text-[0.7rem] font-mono text-cyan-400/80 shadow-lg shrink-0 select-none">
                        {filteredPoints.length} / {allPoints.length} {t('points')}
                    </div>
                </div>
            </div>

            {/* Table Container */}
            <div class="flex-1 bg-slate-800/20 backdrop-blur-sm border border-white/5 rounded-2xl overflow-hidden flex flex-col shadow-2xl">
                {/* Fixed Header */}
                <div class="bg-slate-900/80 backdrop-blur-xl border-b border-white/5 z-20 shrink-0">
                    <table class="w-full text-left border-collapse table-fixed">
                        <thead>
                            <tr class="text-[0.65rem] uppercase tracking-[0.2em] text-slate-500 font-black">
                                <th class="px-6 py-4 cursor-pointer hover:text-sky-400 transition-colors group" onClick={() => handleSort('connection')} style={{ width: '10%' }}>
                                    <div class="flex items-center gap-1.5 truncate">Connection <i class={`fas ${getSortIcon('connection')} text-[0.6rem] group-hover:opacity-100`}></i></div>
                                </th>
                                <th class="px-6 py-4 cursor-pointer hover:text-sky-400 transition-colors group" onClick={() => handleSort('device')} style={{ width: '10%' }}>
                                    <div class="flex items-center gap-1.5 truncate">Device <i class={`fas ${getSortIcon('device')} text-[0.6rem] group-hover:opacity-100`}></i></div>
                                </th>
                                <th class="px-6 py-4 cursor-pointer hover:text-sky-400 transition-colors group" onClick={() => handleSort('name')} style={{ width: '22%' }}>
                                    <div class="flex items-center gap-1.5 truncate">Identity &amp; Path <i class={`fas ${getSortIcon('name')} text-[0.6rem] group-hover:opacity-100`}></i></div>
                                </th>
                                <th class="px-6 py-4 cursor-pointer hover:text-sky-400 transition-colors group" onClick={() => handleSort('key')} style={{ width: '20%' }}>
                                    <div class="flex items-center gap-1.5 truncate">Key / Tag <i class={`fas ${getSortIcon('key')} text-[0.6rem] group-hover:opacity-100`}></i></div>
                                </th>
                                <th class="px-6 py-4 cursor-pointer hover:text-sky-400 transition-colors group" onClick={() => handleSort('type')} style={{ width: '10%' }}>
                                    <div class="flex items-center gap-1.5 truncate">Type <i class={`fas ${getSortIcon('type')} text-[0.6rem] group-hover:opacity-100`}></i></div>
                                </th>
                                <th class="px-6 py-4 cursor-pointer hover:text-sky-400 transition-colors group text-right" onClick={() => handleSort('value')} style={{ width: '10%' }}>
                                    <div class="flex items-center justify-end gap-1.5 truncate">Current Value <i class={`fas ${getSortIcon('value')} text-[0.6rem] group-hover:opacity-100`}></i></div>
                                </th>
                                <th class="px-6 py-4 cursor-pointer hover:text-sky-400 transition-colors group text-right" onClick={() => handleSort('lastSeen')} style={{ width: '10%' }}>
                                    <div class="flex items-center justify-end gap-1.5 truncate">Last Seen <i class={`fas ${getSortIcon('lastSeen')} text-[0.6rem] group-hover:opacity-100`}></i></div>
                                </th>
                                <th class="px-6 py-4 text-center" style={{ width: '8%' }}>Actions</th>
                            </tr>
                        </thead>
                    </table>
                </div>

                {/* Virtual Scroll Viewport */}
                <div 
                    ref={viewportRef}
                    onScroll={(e) => setScrollTop(e.currentTarget.scrollTop)}
                    class="flex-1 overflow-y-auto relative min-h-0"
                >
                    {totalCount === 0 ? (
                        <div class="flex flex-col items-center justify-center gap-2 opacity-20 py-32">
                            <i class="fas fa-search text-4xl mb-2"></i>
                            <p class="text-slate-400 italic">No data points matching your search criteria</p>
                        </div>
                    ) : (
                        <div style={{ height: `${totalHeight}px`, position: 'relative' }}>
                            <table class="w-full text-left border-collapse table-fixed absolute inset-x-0 top-0">
                                <tbody>
                                    {visibleSlice.map((point, index) => {
                                        const absIndex = startIdx + index;
                                        const topPos = absIndex * ROW_HEIGHT;
                                        const curVal = liveValues[point.key] !== undefined ? liveValues[point.key] : point.value;
                                        const displayVal = (window as any).PulswerkValue?.formatDisplay(curVal, point.type) || curVal;
                                        const isSchedule = point.type === 'OBJECT_SCHEDULE';
                                        const isFav = (window as any).pw_fav?.get('deziko_favorites')?.includes(point.key);

                                        return (
                                            <tr 
                                                key={point.key}
                                                style={{ 
                                                    position: 'absolute', 
                                                    top: `${topPos}px`, 
                                                    height: `${ROW_HEIGHT}px`,
                                                    left: 0, 
                                                    right: 0,
                                                    borderBottom: '1px solid rgba(255, 255, 255, 0.03)'
                                                }}
                                                class="flex items-center w-full transition-colors duration-150 hover:bg-white/[0.03]"
                                            >
                                                <td class="px-6 py-3 align-middle truncate font-semibold text-slate-100" style={{ width: '10%' }}>
                                                    {point.connection || '–'}
                                                </td>
                                                <td class="px-6 py-3 align-middle truncate text-slate-300" style={{ width: '10%' }}>
                                                    {point.device || '–'}
                                                </td>
                                                <td class="px-6 py-3 align-middle truncate" style={{ width: '22%' }}>
                                                    <div class="font-bold text-slate-50 truncate">{point.name}</div>
                                                    <div class="text-[0.62rem] opacity-40 font-mono truncate">{point.parentPath?.map((p: any) => p.name).join(' / ')}</div>
                                                </td>
                                                <td class="px-6 py-3 align-middle truncate font-mono text-xs text-sky-400/80" style={{ width: '20%' }}>
                                                    {point.key}
                                                </td>
                                                <td class="px-6 py-3 align-middle truncate" style={{ width: '10%' }}>
                                                    <span class="text-[0.68rem] font-bold uppercase px-2 py-0.5 rounded bg-white/5 text-slate-400 truncate block text-center">
                                                        {point.type?.replace('OBJECT_', '')}
                                                    </span>
                                                </td>
                                                <td class="px-6 py-3 align-middle text-right" style={{ width: '10%' }}>
                                                    {isSchedule ? (
                                                        <span class="text-sky-400/50 text-[0.65rem] font-black uppercase tracking-widest"><i class="fas fa-clock mr-1 opacity-70"></i>Schedule</span>
                                                    ) : (
                                                        <span class="text-[0.95rem] font-bold text-sky-400 tabular-nums">
                                                            {displayVal}
                                                        </span>
                                                    )}
                                                    <span class="text-[0.7rem] text-slate-500 ml-1">{point.units}</span>
                                                </td>
                                                <td class="px-6 py-3 align-middle text-right font-mono text-[0.78rem] text-slate-400" style={{ width: '10%' }}>
                                                    {point.lastSeen || 'Never'}
                                                </td>
                                                <td class="px-6 py-3 align-middle text-center flex justify-center gap-1.5" style={{ width: '8%' }}>
                                                    <button 
                                                        class={`btn-icon star-btn ${(window as any).pwCanEditFavorites ? '' : 'hidden'} ${isFav ? 'active text-amber-400' : ''}`}
                                                        onClick={() => {
                                                            if (typeof (window as any).toggleFavorite === 'function') {
                                                                (window as any).toggleFavorite(point.key);
                                                                setLiveValues(prev => ({ ...prev })); // force render
                                                            }
                                                        }}
                                                    >
                                                        <i class={`${isFav ? 'fas' : 'far'} fa-star`}></i>
                                                    </button>
                                                    <button class="btn-icon" title="Trend" onClick={() => (window as any).openHistory(point.key)}>
                                                        <i class="fas fa-chart-area"></i>
                                                    </button>
                                                    {isSchedule && (
                                                        <button class={`btn-icon ${(window as any).pwCanWriteValue ? '' : 'hidden'}`} title="Schedule View" onClick={() => (window as any).openScheduleView(point.key)}>
                                                            <i class="fas fa-calendar-check"></i>
                                                        </button>
                                                    )}
                                                    {point.isWritable && !isSchedule && (
                                                        <button class={`btn-icon ${(window as any).pwCanWriteValue ? '' : 'hidden'}`} title="Edit Value" onClick={() => (window as any).openEdit(point.key)}>
                                                            <i class="fas fa-pen"></i>
                                                        </button>
                                                    )}
                                                    <button class="btn-icon" title="Properties" onClick={() => (window as any).openProperties(point.key)}>
                                                        <i class="fas fa-cog"></i>
                                                    </button>
                                                </td>
                                            </tr>
                                        );
                                    })}
                                </tbody>
                            </table>
                        </div>
                    )}
                </div>
            </div>
        </div>
    );
}
