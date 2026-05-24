import { jsx as _jsx, jsxs as _jsxs } from "preact/jsx-runtime";
import { useState, useEffect, useRef } from 'preact/hooks';
import { DashboardService } from '../dashboards/api';
import { t } from '../i18n';
const ROW_HEIGHT = 70; // px per row
const BUFFER_ROWS = 10;
export function TelemetryListPage() {
    const [allPoints, setAllPoints] = useState([]);
    const [searchTerm, setSearchTerm] = useState('');
    const [sortField, setSortField] = useState('name');
    const [sortDir, setSortDir] = useState(1);
    const [scrollTop, setScrollTop] = useState(0);
    const [viewportHeight, setViewportHeight] = useState(500);
    const [liveValues, setLiveValues] = useState({});
    const [loading, setLoading] = useState(true);
    const viewportRef = useRef(null);
    // Fetch initial points list
    useEffect(() => {
        const loadPoints = async () => {
            try {
                const response = await fetch('/plswk/api/telemetries?includeLiveValues=true');
                if (!response.ok)
                    throw new Error("Failed to fetch telemetries");
                const data = await response.json();
                setAllPoints(data || []);
                // Put initial values in state
                const initialVals = {};
                data.forEach((p) => {
                    if (p.value !== undefined) {
                        initialVals[p.key] = p.value;
                    }
                });
                setLiveValues(initialVals);
            }
            catch (err) {
                console.error("Failed to load telemetry list:", err);
            }
            finally {
                setLoading(false);
            }
        };
        loadPoints();
    }, []);
    // Set up resize observer on viewport to dynamic measure height
    useEffect(() => {
        if (!viewportRef.current)
            return;
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
        if (searchTerms.length === 0)
            return true;
        const name = (p.name || '').toLowerCase();
        const key = (p.key || '').toLowerCase();
        const device = (p.device || '').toLowerCase();
        const connection = (p.connection || '').toLowerCase();
        const type = (p.type || '').toLowerCase();
        return searchTerms.every(term => name.includes(term) ||
            key.includes(term) ||
            device.includes(term) ||
            connection.includes(term) ||
            type.includes(term));
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
        if (valA < valB)
            return -1 * sortDir;
        if (valA > valB)
            return 1 * sortDir;
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
        if (visibleKeys.length === 0)
            return;
        // Listen to updates
        const unsubscribe = DashboardService.listenToLiveUpdates(visibleKeys, (newData) => {
            setLiveValues(prev => ({ ...prev, ...newData }));
        });
        return () => {
            unsubscribe();
        };
    }, [JSON.stringify(visibleKeys)]);
    const handleSort = (field) => {
        if (sortField === field) {
            setSortDir(prev => prev * -1);
        }
        else {
            setSortField(field);
            setSortDir(1);
        }
    };
    const getSortIcon = (field) => {
        if (sortField !== field)
            return 'fa-sort opacity-20';
        return sortDir === 1 ? 'fa-sort-up text-cyan-400' : 'fa-sort-down text-cyan-400';
    };
    if (loading) {
        return (_jsx("div", { class: "h-full flex items-center justify-center p-16", children: _jsxs("div", { class: "flex flex-col items-center gap-4 text-cyan-400", children: [_jsx("i", { class: "fas fa-spinner fa-spin text-3xl" }), _jsx("p", { class: "text-sm font-medium animate-pulse", children: t('loading') })] }) }));
    }
    const totalHeight = totalCount * ROW_HEIGHT;
    return (_jsxs("div", { class: "h-[calc(100vh-110px)] flex flex-col gap-6 w-full page-enter", children: [_jsxs("div", { class: "flex flex-col md:flex-row items-stretch md:items-center justify-between gap-4 bg-slate-800/40 backdrop-blur-md border border-white/5 rounded-2xl p-5 shadow-2xl shrink-0", children: [_jsxs("div", { class: "flex items-center gap-4", children: [_jsx("div", { class: "w-12 h-12 rounded-xl bg-cyan-500/10 flex items-center justify-center text-cyan-400", children: _jsx("i", { class: "fas fa-list-ul text-xl" }) }), _jsxs("div", { children: [_jsx("h1", { class: "text-xl font-extrabold text-white tracking-tight", children: t('nav_telemetries') }), _jsx("p", { class: "text-[0.7rem] text-slate-400 uppercase tracking-widest font-semibold opacity-60", children: "Real-time point overview" })] })] }), _jsxs("div", { class: "flex items-center gap-4 flex-1 max-w-2xl", children: [_jsxs("div", { class: "relative flex-1", children: [_jsx("i", { class: "fas fa-search absolute left-4 top-1/2 -translate-y-1/2 text-slate-500 text-sm" }), _jsx("input", { type: "text", placeholder: "Search by name, key, device...", value: searchTerm, onInput: (e) => setSearchTerm(e.currentTarget.value), class: "w-full bg-slate-900/40 border border-white/5 rounded-xl py-3 pl-12 pr-4 text-sm text-slate-200 placeholder:text-slate-600 focus:outline-none focus:border-cyan-500/50 focus:bg-slate-900/60 transition-all shadow-inner" })] }), _jsxs("div", { class: "px-4 py-2 bg-slate-900/60 border border-white/5 rounded-xl text-[0.7rem] font-mono text-cyan-400/80 shadow-lg shrink-0 select-none", children: [filteredPoints.length, " / ", allPoints.length, " ", t('points')] })] })] }), _jsxs("div", { class: "flex-1 bg-slate-800/20 backdrop-blur-sm border border-white/5 rounded-2xl overflow-hidden flex flex-col shadow-2xl", children: [_jsx("div", { class: "bg-slate-900/80 backdrop-blur-xl border-b border-white/5 z-20 shrink-0", children: _jsx("table", { class: "w-full text-left border-collapse table-fixed", children: _jsx("thead", { children: _jsxs("tr", { class: "text-[0.65rem] uppercase tracking-[0.2em] text-slate-500 font-black", children: [_jsx("th", { class: "px-6 py-4 cursor-pointer hover:text-sky-400 transition-colors group", onClick: () => handleSort('connection'), style: { width: '10%' }, children: _jsxs("div", { class: "flex items-center gap-1.5 truncate", children: ["Connection ", _jsx("i", { class: `fas ${getSortIcon('connection')} text-[0.6rem] group-hover:opacity-100` })] }) }), _jsx("th", { class: "px-6 py-4 cursor-pointer hover:text-sky-400 transition-colors group", onClick: () => handleSort('device'), style: { width: '10%' }, children: _jsxs("div", { class: "flex items-center gap-1.5 truncate", children: ["Device ", _jsx("i", { class: `fas ${getSortIcon('device')} text-[0.6rem] group-hover:opacity-100` })] }) }), _jsx("th", { class: "px-6 py-4 cursor-pointer hover:text-sky-400 transition-colors group", onClick: () => handleSort('name'), style: { width: '22%' }, children: _jsxs("div", { class: "flex items-center gap-1.5 truncate", children: ["Identity & Path ", _jsx("i", { class: `fas ${getSortIcon('name')} text-[0.6rem] group-hover:opacity-100` })] }) }), _jsx("th", { class: "px-6 py-4 cursor-pointer hover:text-sky-400 transition-colors group", onClick: () => handleSort('key'), style: { width: '20%' }, children: _jsxs("div", { class: "flex items-center gap-1.5 truncate", children: ["Key / Tag ", _jsx("i", { class: `fas ${getSortIcon('key')} text-[0.6rem] group-hover:opacity-100` })] }) }), _jsx("th", { class: "px-6 py-4 cursor-pointer hover:text-sky-400 transition-colors group", onClick: () => handleSort('type'), style: { width: '10%' }, children: _jsxs("div", { class: "flex items-center gap-1.5 truncate", children: ["Type ", _jsx("i", { class: `fas ${getSortIcon('type')} text-[0.6rem] group-hover:opacity-100` })] }) }), _jsx("th", { class: "px-6 py-4 cursor-pointer hover:text-sky-400 transition-colors group text-right", onClick: () => handleSort('value'), style: { width: '10%' }, children: _jsxs("div", { class: "flex items-center justify-end gap-1.5 truncate", children: ["Current Value ", _jsx("i", { class: `fas ${getSortIcon('value')} text-[0.6rem] group-hover:opacity-100` })] }) }), _jsx("th", { class: "px-6 py-4 cursor-pointer hover:text-sky-400 transition-colors group text-right", onClick: () => handleSort('lastSeen'), style: { width: '10%' }, children: _jsxs("div", { class: "flex items-center justify-end gap-1.5 truncate", children: ["Last Seen ", _jsx("i", { class: `fas ${getSortIcon('lastSeen')} text-[0.6rem] group-hover:opacity-100` })] }) }), _jsx("th", { class: "px-6 py-4 text-center", style: { width: '8%' }, children: "Actions" })] }) }) }) }), _jsx("div", { ref: viewportRef, onScroll: (e) => setScrollTop(e.currentTarget.scrollTop), class: "flex-1 overflow-y-auto relative min-h-0", children: totalCount === 0 ? (_jsxs("div", { class: "flex flex-col items-center justify-center gap-2 opacity-20 py-32", children: [_jsx("i", { class: "fas fa-search text-4xl mb-2" }), _jsx("p", { class: "text-slate-400 italic", children: "No data points matching your search criteria" })] })) : (_jsx("div", { style: { height: `${totalHeight}px`, position: 'relative' }, children: _jsx("table", { class: "w-full text-left border-collapse table-fixed absolute inset-x-0 top-0", children: _jsx("tbody", { children: visibleSlice.map((point, index) => {
                                        const absIndex = startIdx + index;
                                        const topPos = absIndex * ROW_HEIGHT;
                                        const curVal = liveValues[point.key] !== undefined ? liveValues[point.key] : point.value;
                                        const displayVal = window.PulswerkValue?.formatDisplay(curVal, point.type) || curVal;
                                        const isSchedule = point.type === 'OBJECT_SCHEDULE';
                                        const isFav = window.pw_fav?.get('deziko_favorites')?.includes(point.key);
                                        return (_jsxs("tr", { style: {
                                                position: 'absolute',
                                                top: `${topPos}px`,
                                                height: `${ROW_HEIGHT}px`,
                                                left: 0,
                                                right: 0,
                                                borderBottom: '1px solid rgba(255, 255, 255, 0.03)'
                                            }, class: "flex items-center w-full transition-colors duration-150 hover:bg-white/[0.03]", children: [_jsx("td", { class: "px-6 py-3 align-middle truncate font-semibold text-slate-100", style: { width: '10%' }, children: point.connection || '–' }), _jsx("td", { class: "px-6 py-3 align-middle truncate text-slate-300", style: { width: '10%' }, children: point.device || '–' }), _jsxs("td", { class: "px-6 py-3 align-middle truncate", style: { width: '22%' }, children: [_jsx("div", { class: "font-bold text-slate-50 truncate", children: point.name }), _jsx("div", { class: "text-[0.62rem] opacity-40 font-mono truncate", children: point.parentPath?.map((p) => p.name).join(' / ') })] }), _jsx("td", { class: "px-6 py-3 align-middle truncate font-mono text-xs text-sky-400/80", style: { width: '20%' }, children: point.key }), _jsx("td", { class: "px-6 py-3 align-middle truncate", style: { width: '10%' }, children: _jsx("span", { class: "text-[0.68rem] font-bold uppercase px-2 py-0.5 rounded bg-white/5 text-slate-400 truncate block text-center", children: point.type?.replace('OBJECT_', '') }) }), _jsxs("td", { class: "px-6 py-3 align-middle text-right", style: { width: '10%' }, children: [isSchedule ? (_jsxs("span", { class: "text-sky-400/50 text-[0.65rem] font-black uppercase tracking-widest", children: [_jsx("i", { class: "fas fa-clock mr-1 opacity-70" }), "Schedule"] })) : (_jsx("span", { class: "text-[0.95rem] font-bold text-sky-400 tabular-nums", children: displayVal })), _jsx("span", { class: "text-[0.7rem] text-slate-500 ml-1", children: point.units })] }), _jsx("td", { class: "px-6 py-3 align-middle text-right font-mono text-[0.78rem] text-slate-400", style: { width: '10%' }, children: point.lastSeen || 'Never' }), _jsxs("td", { class: "px-6 py-3 align-middle text-center flex justify-center gap-1.5", style: { width: '8%' }, children: [_jsx("button", { class: `btn-icon star-btn ${window.pwCanEditFavorites ? '' : 'hidden'} ${isFav ? 'active text-amber-400' : ''}`, onClick: () => {
                                                                if (typeof window.toggleFavorite === 'function') {
                                                                    window.toggleFavorite(point.key);
                                                                    setLiveValues(prev => ({ ...prev })); // force render
                                                                }
                                                            }, children: _jsx("i", { class: `${isFav ? 'fas' : 'far'} fa-star` }) }), _jsx("button", { class: "btn-icon", title: "Trend", onClick: () => window.openHistory(point.key), children: _jsx("i", { class: "fas fa-chart-area" }) }), isSchedule && (_jsx("button", { class: `btn-icon ${window.pwCanWriteValue ? '' : 'hidden'}`, title: "Schedule View", onClick: () => window.openScheduleView(point.key), children: _jsx("i", { class: "fas fa-calendar-check" }) })), point.isWritable && !isSchedule && (_jsx("button", { class: `btn-icon ${window.pwCanWriteValue ? '' : 'hidden'}`, title: "Edit Value", onClick: () => window.openEdit(point.key), children: _jsx("i", { class: "fas fa-pen" }) })), _jsx("button", { class: "btn-icon", title: "Properties", onClick: () => window.openProperties(point.key), children: _jsx("i", { class: "fas fa-cog" }) })] })] }, point.key));
                                    }) }) }) })) })] })] }));
}
