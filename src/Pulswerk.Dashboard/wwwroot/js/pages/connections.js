import { jsx as _jsx, jsxs as _jsxs } from "preact/jsx-runtime";
import { useState, useEffect, useRef } from 'preact/hooks';
import { ConfigPage } from './config.page';
import { t } from '../i18n';
export function ConnectionsPage({ initialConnId }) {
    const [connections, setConnections] = useState([]);
    const [canEditConfig, setCanEditConfig] = useState(false);
    const [selectedId, setSelectedId] = useState(null);
    const [loading, setLoading] = useState(true);
    const chartRef = useRef(null);
    const chartInstance = useRef(null);
    const fetchConnections = async () => {
        try {
            const response = await fetch('/plswk/api/connections');
            if (response.ok) {
                const data = await response.json();
                setConnections(data.connections || []);
                setCanEditConfig(data.canEditConfig || false);
                // If there's an initial connection to load
                const params = new URLSearchParams(window.location.search);
                const queryId = params.get('conn') || initialConnId;
                if (queryId) {
                    setSelectedId(queryId);
                }
            }
        }
        catch (e) {
            console.error("Failed to fetch connections:", e);
        }
        finally {
            setLoading(false);
        }
    };
    useEffect(() => {
        fetchConnections();
        const handlePop = () => {
            const params = new URLSearchParams(window.location.search);
            const queryId = params.get('conn');
            if (queryId)
                setSelectedId(queryId);
        };
        window.addEventListener('popstate', handlePop);
        return () => window.removeEventListener('popstate', handlePop);
    }, [initialConnId]);
    const selectConnection = (id) => {
        setSelectedId(id);
        const url = new URL(window.location.href);
        url.searchParams.set('conn', id);
        window.history.pushState({ connId: id }, '', url.toString());
    };
    const selectedConnection = connections.find(c => c.id === selectedId);
    // Render health chart when connection details mount / change
    useEffect(() => {
        if (!selectedConnection || selectedId === 'config')
            return;
        const loadHealthChart = async () => {
            try {
                const res = await fetch(`/plswk/api/connection-health/${encodeURIComponent(selectedConnection.id)}`);
                if (!res.ok)
                    throw new Error("Health history API error");
                const data = await res.json();
                const onlineSeries = data.map((d) => ({
                    x: new Date(d.t).getTime(),
                    y: d.online
                }));
                const offlineSeries = data.map((d) => ({
                    x: new Date(d.t).getTime(),
                    y: d.total - d.online
                }));
                if (chartInstance.current) {
                    chartInstance.current.destroy();
                }
                if (chartRef.current && window.ApexCharts) {
                    const total = selectedConnection.deviceCount || 1;
                    const options = {
                        series: [
                            { name: 'Online', data: onlineSeries },
                            { name: 'Offline', data: offlineSeries }
                        ],
                        chart: {
                            type: 'area',
                            height: '100%',
                            stacked: true,
                            animations: { enabled: true, easing: 'easeinout', speed: 400 },
                            toolbar: { show: false },
                            zoom: { enabled: false },
                            accessibility: { enabled: false }
                        },
                        colors: ['#10b981', '#ef4444'],
                        fill: {
                            type: 'gradient',
                            gradient: {
                                shadeIntensity: 1,
                                opacityFrom: 0.5,
                                opacityTo: 0.08,
                                stops: [10, 100]
                            }
                        },
                        stroke: { curve: 'stepline', width: 2 },
                        legend: {
                            show: true,
                            position: 'top',
                            horizontalAlign: 'right',
                            labels: { colors: '#94a3b8' },
                            markers: { width: 8, height: 8, radius: 4 },
                            fontSize: '11px',
                            fontFamily: 'inherit'
                        },
                        grid: {
                            borderColor: 'rgba(255,255,255,0.06)',
                            xaxis: { lines: { show: false } },
                            yaxis: { lines: { show: true } },
                            padding: { bottom: 0, left: 6, right: 6, top: 0 }
                        },
                        xaxis: {
                            type: 'datetime',
                            labels: {
                                style: { colors: '#64748b', fontSize: '10px' },
                                datetimeUTC: false,
                                datetimeFormatter: { hour: 'HH:mm', day: 'dd MMM' }
                            },
                            axisBorder: { show: true, color: 'rgba(255,255,255,0.08)' },
                            axisTicks: { show: false }
                        },
                        yaxis: {
                            min: 0,
                            max: total,
                            forceNiceScale: false,
                            tickAmount: Math.min(total, 5),
                            labels: {
                                style: { colors: '#64748b', fontSize: '10px' },
                                formatter: (v) => Math.round(v).toString()
                            },
                            title: { text: 'devices', style: { color: '#475569', fontSize: '11px' } }
                        },
                        dataLabels: { enabled: false },
                        tooltip: {
                            theme: 'dark',
                            shared: true,
                            intersect: false,
                            x: { format: 'dd MMM HH:mm' },
                            y: { formatter: (val) => val + ' device' + (val !== 1 ? 's' : '') }
                        },
                        noData: {
                            text: 'Collecting data\u2026 (first sample in ~5 min)',
                            align: 'center',
                            style: { color: '#475569', fontSize: '13px' }
                        }
                    };
                    chartInstance.current = new window.ApexCharts(chartRef.current, options);
                    chartInstance.current.render();
                }
            }
            catch (err) {
                console.error("Failed to load connection health history:", err);
            }
        };
        // Delay slightly for render
        const timer = setTimeout(loadHealthChart, 100);
        return () => {
            clearTimeout(timer);
            if (chartInstance.current) {
                chartInstance.current.destroy();
                chartInstance.current = null;
            }
        };
    }, [selectedId, selectedConnection]);
    if (loading) {
        return (_jsx("div", { class: "h-full flex items-center justify-center p-16", children: _jsxs("div", { class: "flex flex-col items-center gap-4 text-cyan-400", children: [_jsx("i", { class: "fas fa-spinner fa-spin text-3xl" }), _jsx("p", { class: "text-sm font-medium animate-pulse", children: t('loading') })] }) }));
    }
    return (_jsxs("div", { class: "flex gap-6 h-[calc(100vh-110px)] w-full page-enter", "data-testid": "conn-layout", children: [_jsxs("div", { class: "w-[300px] shrink-0 flex flex-col gap-3 overflow-y-auto pr-1 conn-list", id: "connList", "data-testid": "conn-list", children: [connections.map(conn => {
                        const isActive = selectedId === conn.id;
                        return (_jsxs("div", { class: `conn-card glass p-4 rounded-xl border cursor-pointer transition-all duration-150 flex flex-col gap-1.5 select-none hover:border-sky-400 hover:bg-sky-400/5 ${isActive ? 'active border-sky-400 bg-sky-400/5 ring-1 ring-sky-500/20' : 'border-slate-700'}`, onClick: () => selectConnection(conn.id), children: [_jsxs("div", { class: "flex justify-between items-start", children: [_jsx("div", { class: "font-bold text-[0.95rem] text-slate-50", children: conn.name }), _jsx("span", { class: "text-[0.65rem] font-bold uppercase tracking-wide px-2 py-0.5 rounded bg-white/5 text-slate-400 whitespace-nowrap", children: conn.type })] }), _jsxs("div", { class: "text-[0.78rem] text-slate-400 font-mono", children: [conn.address, " : ", conn.port] }), _jsxs("div", { class: "flex justify-between items-center mt-0.5", children: [_jsxs("span", { class: `text-xs font-semibold flex items-center ${conn.status === 'online' ? 'text-emerald-500' : conn.status === 'stale' ? 'text-amber-400' : 'text-red-500'}`, children: [_jsx("span", { class: `w-[7px] h-[7px] rounded-full mr-1.5 inline-block ${conn.status === 'online' ? 'bg-emerald-500' : conn.status === 'stale' ? 'bg-amber-400' : 'bg-red-500'}` }), conn.status.toUpperCase()] }), _jsxs("div", { class: "flex items-center gap-1.5 text-[0.72rem] font-bold", children: [_jsx("span", { class: "text-emerald-400", children: conn.onlineCount }), conn.deviceCount - conn.onlineCount > 0 && _jsx("span", { class: "text-red-400", children: conn.deviceCount - conn.onlineCount }), _jsxs("span", { class: "text-slate-500 font-normal", children: ["/ ", conn.deviceCount] })] })] })] }, conn.id));
                    }), canEditConfig && (_jsx("div", { class: "mt-auto pt-4", children: _jsxs("div", { class: `conn-card glass p-3.5 rounded-xl border cursor-pointer transition-all duration-150 flex items-center justify-center gap-2 select-none hover:border-amber-400 hover:bg-amber-400/10 text-amber-400 font-bold ${selectedId === 'config' ? 'active border-amber-400 bg-amber-400/10' : 'border-amber-500/20'}`, id: "btnEditConfig", onClick: () => selectConnection('config'), children: [_jsx("i", { class: "fas fa-cog" }), " Edit Configuration"] }) }))] }), _jsxs("div", { class: "flex-1 flex flex-col gap-4 overflow-hidden", "data-testid": "conn-detail", children: [selectedId === null && (_jsxs("div", { class: "flex-1 flex flex-col items-center justify-center gap-4 text-slate-400 opacity-45 text-sm", id: "detailEmpty", "data-testid": "conn-detail-empty", children: [_jsx("i", { class: "fas fa-network-wired text-4xl" }), _jsx("p", { children: "Select a connection to view its devices" })] })), selectedId === 'config' && (_jsx("div", { class: "flex-1 overflow-y-auto", id: "panel-config", children: _jsx(ConfigPage, {}) })), selectedId !== null && selectedId !== 'config' && selectedConnection && (_jsxs("div", { class: "flex-1 flex flex-col bg-slate-800 border border-slate-700 rounded-xl overflow-hidden", id: `panel-${selectedConnection.id}`, children: [_jsxs("div", { class: "px-6 py-5 border-b border-slate-700 flex justify-between items-center shrink-0 bg-black/15", children: [_jsxs("div", { children: [_jsx("div", { class: "text-lg font-bold text-slate-50", children: selectedConnection.name }), _jsxs("div", { class: "text-[0.78rem] text-slate-400 font-mono mt-0.5", children: [selectedConnection.address, " : ", selectedConnection.port, " \u00A0\u00B7\u00A0 ", selectedConnection.type, " \u00A0\u00B7\u00A0 ", selectedConnection.id] })] }), _jsxs("div", { class: "flex gap-6 items-center", children: [_jsxs("div", { class: "text-right", children: [_jsx("div", { class: "text-xl font-bold text-sky-400 leading-tight", children: selectedConnection.deviceCount }), _jsx("div", { class: "text-[0.65rem] uppercase tracking-wider text-slate-400", children: "Devices" })] }), _jsxs("div", { class: "text-right", children: [_jsx("div", { class: `text-xl font-bold leading-tight ${selectedConnection.status === 'online' ? 'text-emerald-500' : selectedConnection.status === 'stale' ? 'text-amber-400' : 'text-red-500'}`, children: selectedConnection.status.toUpperCase() }), _jsx("div", { class: "text-[0.65rem] uppercase tracking-wider text-slate-400", children: "Status" })] })] })] }), _jsxs("div", { class: "px-6 py-5 border-b border-slate-700/50 shrink-0", children: [_jsxs("div", { class: "flex items-center justify-between mb-2", children: [_jsxs("div", { class: "flex items-center gap-2 text-sm font-semibold text-slate-300", children: [_jsx("i", { class: "fas fa-heartbeat text-emerald-500 text-xs animate-pulse" }), "Device Availability"] }), _jsx("div", { class: "text-[0.65rem] font-bold text-slate-500 uppercase tracking-widest bg-white/5 px-2 py-0.5 rounded", children: "24h \u00B7 5 min intervals" })] }), _jsx("div", { class: "h-[110px] health-chart", ref: chartRef })] }), _jsx("div", { class: "flex-1 overflow-y-auto px-6 py-4 device-table-wrap", children: !selectedConnection.devices || selectedConnection.devices.length === 0 ? (_jsx("div", { class: "py-8 text-center text-slate-400 opacity-50", children: "No devices configured on this connection." })) : (_jsxs("table", { class: "w-full border-separate border-spacing-0 text-sm", children: [_jsx("thead", { children: _jsxs("tr", { class: "text-left text-[0.68rem] font-bold uppercase tracking-wider text-slate-400", children: [_jsx("th", { class: "px-3 py-2.5 border-b border-slate-700 whitespace-nowrap", children: "Device" }), _jsx("th", { class: "px-3 py-2.5 border-b border-slate-700 whitespace-nowrap", children: "Protocol" }), _jsx("th", { class: "px-3 py-2.5 border-b border-slate-700 whitespace-nowrap", children: "Address" }), _jsx("th", { class: "px-3 py-2.5 border-b border-slate-700 whitespace-nowrap", children: "Asset Type" }), _jsx("th", { class: "px-3 py-2.5 border-b border-slate-700 whitespace-nowrap", children: "Status" }), _jsx("th", { class: "px-3 py-2.5 border-b border-slate-700 whitespace-nowrap", children: "Last Seen" })] }) }), _jsx("tbody", { children: selectedConnection.devices.map((dev) => (_jsxs("tr", { class: "transition-colors duration-150 hover:bg-white/[0.03]", children: [_jsxs("td", { class: "px-3 py-3 border-b border-white/[0.04] align-middle", children: [_jsx("div", { class: "font-semibold text-slate-100", children: dev.name }), _jsx("div", { class: "text-[0.72rem] text-slate-400 mt-0.5", children: dev.deviceType })] }), _jsx("td", { class: "px-3 py-3 border-b border-white/[0.04] align-middle", children: _jsx("span", { class: "dev-type-pill inline-block text-[0.68rem] font-bold uppercase tracking-wide px-2 py-0.5 rounded-full bg-sky-400/10 text-sky-400 border border-sky-400/20", children: dev.protocol }) }), _jsx("td", { class: "px-3 py-3 border-b border-white/[0.04] align-middle font-mono text-[0.82rem] text-slate-300", children: dev.address }), _jsx("td", { class: "px-3 py-3 border-b border-white/[0.04] align-middle", children: dev.assetType ? (_jsx("span", { class: "inline-block text-[0.68rem] px-2 py-0.5 rounded bg-white/5 text-slate-400 font-mono", children: dev.assetType })) : (_jsx("span", { class: "text-slate-400 opacity-40", children: "\u2013" })) }), _jsx("td", { class: "px-3 py-3 border-b border-white/[0.04] align-middle", children: _jsxs("div", { class: `flex items-center gap-1.5 text-[0.78rem] font-semibold ${dev.status === 'online' ? 'text-emerald-500' : dev.status === 'stale' ? 'text-amber-400' : 'text-red-500'}`, children: [_jsx("span", { class: `w-[7px] h-[7px] rounded-full inline-block ${dev.status === 'online' ? 'bg-emerald-500' : dev.status === 'stale' ? 'bg-amber-400' : 'bg-red-500'}` }), dev.status.toUpperCase()] }) }), _jsx("td", { class: "px-3 py-3 border-b border-white/[0.04] align-middle text-[0.78rem] font-mono text-slate-400", children: dev.lastSeen })] }, dev.name))) })] })) })] }))] })] }));
}
