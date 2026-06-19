import { useState, useEffect, useRef } from 'preact/hooks';
import { DashboardService } from '../dashboards/api';
import { t } from '../i18n';

const ROW_HEIGHT = 60; // px per row
const BUFFER_ROWS = 10;

/** Format a lastSeen ISO string to a human-readable relative time.
 *  Returns null if the value is falsy (never seen). */
function formatLastSeen(raw: string | null | undefined): { label: string; cls: string } {
    if (!raw) return { label: 'Never', cls: 'text-slate-600' };
    const date = new Date(raw);
    if (isNaN(date.getTime())) return { label: raw, cls: 'text-slate-500' };
    const seconds = Math.floor((Date.now() - date.getTime()) / 1000);
    if (seconds < 0) return { label: 'just now', cls: 'text-emerald-400' };
    if (seconds < 15)  return { label: 'just now',            cls: 'text-emerald-400' };
    if (seconds < 60)  return { label: `${seconds}s ago`,     cls: 'text-emerald-400/80' };
    if (seconds < 3600) return { label: `${Math.floor(seconds / 60)}m ago`, cls: 'text-sky-400/80' };
    if (seconds < 86400) return { label: `${Math.floor(seconds / 3600)}h ago`, cls: 'text-amber-400/70' };
    return { label: `${Math.floor(seconds / 86400)}d ago`,   cls: 'text-slate-500' };
}

/** Color-class for the TYPE badge */
function typeColor(type: string): string {
    if (!type) return 'bg-white/5 text-slate-400';
    const t = type.replace('OBJECT_', '').toUpperCase();
    if (t === 'ANALOG_INPUT' || t === 'ANALOG_VALUE')  return 'bg-sky-500/10 text-sky-400';
    if (t === 'BINARY_INPUT' || t === 'BINARY_VALUE')  return 'bg-violet-500/10 text-violet-400';
    if (t === 'DISCRETE' || t === 'MULTI_STATE_INPUT') return 'bg-amber-500/10 text-amber-400';
    if (t === 'SCHEDULE')                               return 'bg-emerald-500/10 text-emerald-400';
    return 'bg-white/5 text-slate-400';
}

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

    // Virtual scroll window
    const totalCount = sortedPoints.length;
    let startIdx = Math.floor(scrollTop / ROW_HEIGHT) - BUFFER_ROWS;
    let endIdx = Math.ceil((scrollTop + viewportHeight) / ROW_HEIGHT) + BUFFER_ROWS;
    startIdx = Math.max(0, startIdx);
    endIdx = Math.min(totalCount - 1, endIdx);

    const visibleSlice = sortedPoints.slice(startIdx, endIdx + 1);
    const visibleKeys = visibleSlice.map(p => p.key).filter(Boolean);

    useEffect(() => {
        if (visibleKeys.length === 0) return;
        const unsubscribe = DashboardService.listenToLiveUpdates(visibleKeys, (newData) => {
            setLiveValues(prev => ({ ...prev, ...newData }));
        });
        return () => { unsubscribe(); };
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
                        <h1 class="text-xl font-extrabold text-white tracking-tight" data-testid="page-title">{t('nav_telemetries')}</h1>
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
                                <th class="px-4 py-3 cursor-pointer hover:text-sky-400 transition-colors group" onClick={() => handleSort('connection')} style={{ width: '9%' }}>
                                    <div class="flex items-center gap-1.5">Connection <i class={`fas ${getSortIcon('connection')} text-[0.6rem] group-hover:opacity-100`}></i></div>
                                </th>
                                <th class="px-4 py-3 cursor-pointer hover:text-sky-400 transition-colors group" onClick={() => handleSort('device')} style={{ width: '11%' }}>
                                    <div class="flex items-center gap-1.5">Device <i class={`fas ${getSortIcon('device')} text-[0.6rem] group-hover:opacity-100`}></i></div>
                                </th>
                                <th class="px-4 py-3 cursor-pointer hover:text-sky-400 transition-colors group" onClick={() => handleSort('name')} style={{ width: '24%' }}>
                                    <div class="flex items-center gap-1.5">Identity &amp; Path <i class={`fas ${getSortIcon('name')} text-[0.6rem] group-hover:opacity-100`}></i></div>
                                </th>
                                <th class="px-4 py-3 cursor-pointer hover:text-sky-400 transition-colors group" onClick={() => handleSort('key')} style={{ width: '22%' }}>
                                    <div class="flex items-center gap-1.5">Key / Tag <i class={`fas ${getSortIcon('key')} text-[0.6rem] group-hover:opacity-100`}></i></div>
                                </th>
                                <th class="px-4 py-3 cursor-pointer hover:text-sky-400 transition-colors group" onClick={() => handleSort('type')} style={{ width: '9%' }}>
                                    <div class="flex items-center gap-1.5">Type <i class={`fas ${getSortIcon('type')} text-[0.6rem] group-hover:opacity-100`}></i></div>
                                </th>
                                <th class="px-4 py-3 cursor-pointer hover:text-sky-400 transition-colors group text-right" onClick={() => handleSort('value')} style={{ width: '10%' }}>
                                    <div class="flex items-center justify-end gap-1.5">Value <i class={`fas ${getSortIcon('value')} text-[0.6rem] group-hover:opacity-100`}></i></div>
                                </th>
                                <th class="px-4 py-3 cursor-pointer hover:text-sky-400 transition-colors group text-right" onClick={() => handleSort('lastSeen')} style={{ width: '8%' }}>
                                    <div class="flex items-center justify-end gap-1.5">Last Seen <i class={`fas ${getSortIcon('lastSeen')} text-[0.6rem] group-hover:opacity-100`}></i></div>
                                </th>
                                <th class="px-4 py-3 text-center" style={{ width: '7%' }}>Actions</th>
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

                                        const { label: lastSeenLabel, cls: lastSeenCls } = formatLastSeen(point.lastSeen);
                                        const typeBadgeCls = typeColor(point.type);
                                        const typeShort = (point.type || '').replace('OBJECT_', '');
                                        const parentPath = point.parentPath?.map((p: any) => p.name).join(' / ') || '';

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
                                                class="flex items-center w-full transition-colors duration-150 hover:bg-white/[0.04] group/row"
                                            >
                                                {/* CONNECTION */}
                                                <td class="px-4 py-2 align-middle" style={{ width: '9%' }}>
                                                    <span
                                                        class="text-[0.78rem] font-semibold text-slate-300 truncate block"
                                                        title={point.connection}
                                                    >
                                                        {point.connection || '–'}
                                                    </span>
                                                </td>

                                                {/* DEVICE */}
                                                <td class="px-4 py-2 align-middle" style={{ width: '11%' }}>
                                                    <span
                                                        class="text-[0.78rem] text-slate-400 truncate block"
                                                        title={point.device}
                                                    >
                                                        {point.device || '–'}
                                                    </span>
                                                </td>

                                                {/* IDENTITY & PATH — name bold, path faint mono, both with tooltip */}
                                                <td class="px-4 py-2 align-middle cursor-pointer" style={{ width: '24%' }} onClick={() => (window as any).openTelemetryDetails(point.key)}>
                                                    <div
                                                        class="font-semibold text-slate-50 text-[0.85rem] truncate leading-tight hover:text-sky-400 transition-colors"
                                                        title={point.name}
                                                    >
                                                        {point.name}
                                                    </div>
                                                    {parentPath && (
                                                        <div
                                                            class="text-[0.62rem] text-slate-600 font-mono truncate mt-0.5 leading-tight"
                                                            title={parentPath}
                                                        >
                                                            {parentPath}
                                                        </div>
                                                    )}
                                                </td>

                                                {/* KEY / TAG — monospace, full key in title tooltip */}
                                                <td class="px-4 py-2 align-middle cursor-pointer" style={{ width: '22%' }} onClick={() => (window as any).openTelemetryDetails(point.key)}>
                                                    <span
                                                        class="font-mono text-[0.72rem] text-sky-400/75 truncate block leading-snug hover:text-sky-300 transition-colors"
                                                        title={point.key}
                                                    >
                                                        {point.key}
                                                    </span>
                                                </td>

                                                {/* TYPE badge — colored by type family, tooltip on overflow */}
                                                <td class="px-4 py-2 align-middle cursor-pointer" style={{ width: '9%' }} onClick={() => (window as any).openTelemetryDetails(point.key)}>
                                                    <span
                                                        class={`inline-flex items-center text-[0.65rem] font-bold uppercase px-1.5 py-0.5 rounded-md leading-none max-w-full ${typeBadgeCls}`}
                                                        title={typeShort}
                                                    >
                                                        <span class="truncate">{typeShort}</span>
                                                    </span>
                                                </td>

                                                {/* CURRENT VALUE */}
                                                <td class="px-4 py-2 align-middle text-right cursor-pointer" style={{ width: '10%' }} onClick={() => (window as any).openTelemetryDetails(point.key)}>
                                                    {isSchedule ? (
                                                        <span class="text-sky-400/50 text-[0.65rem] font-black uppercase tracking-widest">
                                                            <i class="fas fa-clock mr-1 opacity-70"></i>Schedule
                                                        </span>
                                                    ) : (
                                                        <span class="text-[0.9rem] font-bold text-sky-300 tabular-nums">
                                                            {displayVal}
                                                            {point.units && (
                                                                <span class="text-[0.68rem] text-slate-500 ml-1 font-normal">{point.units}</span>
                                                            )}
                                                        </span>
                                                    )}
                                                </td>

                                                {/* LAST SEEN — relative time, color-coded freshness */}
                                                <td class="px-4 py-2 align-middle text-right" style={{ width: '8%' }}>
                                                    <span
                                                        class={`font-mono text-[0.72rem] ${lastSeenCls}`}
                                                        title={point.lastSeen || 'Never polled'}
                                                    >
                                                        {lastSeenLabel}
                                                    </span>
                                                </td>

                                                {/* ACTIONS */}
                                                <td class="px-4 py-2 align-middle text-center flex justify-center gap-1.5" style={{ width: '7%' }}>
                                                    <button
                                                        class={`btn-icon star-btn ${(window as any).pwCanEditFavorites ? '' : 'hidden'} ${isFav ? 'active text-amber-400' : ''}`}
                                                        title={isFav ? 'Remove from favourites' : 'Add to favourites'}
                                                        onClick={(e) => {
                                                            e.stopPropagation();
                                                            if (typeof (window as any).toggleFavorite === 'function') {
                                                                (window as any).toggleFavorite(point.key);
                                                                setLiveValues(prev => ({ ...prev }));
                                                            }
                                                        }}
                                                    >
                                                        <i class={`${isFav ? 'fas' : 'far'} fa-star`}></i>
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
