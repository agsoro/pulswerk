import { useState, useEffect, useRef } from 'preact/hooks';
import { ConfigPage } from './config.page';
import { t } from '../i18n';

interface ConnectionsPageProps {
    initialConnId?: string | null;
}

export function ConnectionsPage({ initialConnId }: ConnectionsPageProps) {
    const [connections, setConnections] = useState<any[]>([]);
    const [canEditConfig, setCanEditConfig] = useState(false);
    const [selectedId, setSelectedId] = useState<string | null>(null);
    const [loading, setLoading] = useState(true);

    const chartRef = useRef<HTMLDivElement>(null);
    const chartInstance = useRef<any>(null);

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
        } catch (e) {
            console.error("Failed to fetch connections:", e);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchConnections();

        const handlePop = () => {
            const params = new URLSearchParams(window.location.search);
            const queryId = params.get('conn');
            if (queryId) setSelectedId(queryId);
        };
        window.addEventListener('popstate', handlePop);
        return () => window.removeEventListener('popstate', handlePop);
    }, [initialConnId]);

    const selectConnection = (id: string) => {
        setSelectedId(id);
        const url = new URL(window.location.href);
        url.searchParams.set('conn', id);
        window.history.pushState({ connId: id }, '', url.toString());
    };

    const selectedConnection = connections.find(c => c.id === selectedId);

    // Render health chart when connection details mount / change
    useEffect(() => {
        if (!selectedConnection || selectedId === 'config') return;

        const loadHealthChart = async () => {
            try {
                const res = await fetch(`/plswk/api/connection-health/${encodeURIComponent(selectedConnection.id)}`);
                if (!res.ok) throw new Error("Health history API error");
                const data = await res.json();

                const onlineSeries = data.map((d: any) => ({
                    x: new Date(d.t).getTime(),
                    y: d.online
                }));

                const offlineSeries = data.map((d: any) => ({
                    x: new Date(d.t).getTime(),
                    y: d.total - d.online
                }));

                if (chartInstance.current) {
                    chartInstance.current.destroy();
                }

                if (chartRef.current && (window as any).ApexCharts) {
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
                            zoom: { enabled: false }
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
                                formatter: (v: number) => Math.round(v).toString()
                            },
                            title: { text: 'devices', style: { color: '#475569', fontSize: '11px' } }
                        },
                        dataLabels: { enabled: false },
                        tooltip: {
                            theme: 'dark',
                            shared: true,
                            intersect: false,
                            x: { format: 'dd MMM HH:mm' },
                            y: { formatter: (val: number) => val + ' device' + (val !== 1 ? 's' : '') }
                        },
                        noData: {
                            text: 'Collecting data\u2026 (first sample in ~5 min)',
                            align: 'center',
                            style: { color: '#475569', fontSize: '13px' }
                        }
                    };
                    chartInstance.current = new (window as any).ApexCharts(chartRef.current, options);
                    chartInstance.current.render();
                }
            } catch (err) {
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
        <div class="flex gap-6 h-[calc(100vh-110px)] w-full page-enter" data-testid="conn-layout">
            {/* Left side: connections list */}
            <div class="w-[300px] shrink-0 flex flex-col gap-3 overflow-y-auto pr-1 conn-list" id="connList" data-testid="conn-list">
                {connections.map(conn => {
                    const isActive = selectedId === conn.id;
                    return (
                        <div 
                            key={conn.id}
                            class={`conn-card glass p-4 rounded-xl border cursor-pointer transition-all duration-150 flex flex-col gap-1.5 select-none hover:border-sky-400 hover:bg-sky-400/5 ${
                                isActive ? 'active border-sky-400 bg-sky-400/5 ring-1 ring-sky-500/20' : 'border-slate-700'
                            }`}
                            onClick={() => selectConnection(conn.id)}
                        >
                            <div class="flex justify-between items-start">
                                <div class="font-bold text-[0.95rem] text-slate-50">{conn.name}</div>
                                <span class="text-[0.65rem] font-bold uppercase tracking-wide px-2 py-0.5 rounded bg-white/5 text-slate-400 whitespace-nowrap">{conn.type}</span>
                            </div>
                            <div class="text-[0.78rem] text-slate-400 font-mono">{conn.address} : {conn.port}</div>
                            <div class="flex justify-between items-center mt-0.5">
                                <span class={`text-xs font-semibold flex items-center ${
                                    conn.status === 'online' ? 'text-emerald-500' : conn.status === 'stale' ? 'text-amber-400' : 'text-red-500'
                                }`}>
                                    <span class={`w-[7px] h-[7px] rounded-full mr-1.5 inline-block ${
                                        conn.status === 'online' ? 'bg-emerald-500' : conn.status === 'stale' ? 'bg-amber-400' : 'bg-red-500'
                                    }`}></span>
                                    {conn.status.toUpperCase()}
                                </span>
                                <div class="flex items-center gap-1.5 text-[0.72rem] font-bold">
                                    <span class="text-emerald-400">{conn.onlineCount}</span>
                                    {conn.deviceCount - conn.onlineCount > 0 && <span class="text-red-400">{conn.deviceCount - conn.onlineCount}</span>}
                                    <span class="text-slate-500 font-normal">/ {conn.deviceCount}</span>
                                </div>
                            </div>
                        </div>
                    );
                })}

                {canEditConfig && (
                    <div class="mt-auto pt-4">
                        <div 
                            class={`conn-card glass p-3.5 rounded-xl border cursor-pointer transition-all duration-150 flex items-center justify-center gap-2 select-none hover:border-amber-400 hover:bg-amber-400/10 text-amber-400 font-bold ${
                                selectedId === 'config' ? 'active border-amber-400 bg-amber-400/10' : 'border-amber-500/20'
                            }`}
                            id="btnEditConfig" 
                            onClick={() => selectConnection('config')}
                        >
                            <i class="fas fa-cog"></i> Edit Configuration
                        </div>
                    </div>
                )}
            </div>

            {/* Right side: detail panel */}
            <div class="flex-1 flex flex-col gap-4 overflow-hidden" data-testid="conn-detail">
                {selectedId === null && (
                    <div class="flex-1 flex flex-col items-center justify-center gap-4 text-slate-400 opacity-45 text-sm" id="detailEmpty" data-testid="conn-detail-empty">
                        <i class="fas fa-network-wired text-4xl"></i>
                        <p>Select a connection to view its devices</p>
                    </div>
                )}

                {selectedId === 'config' && (
                    <div class="flex-1 overflow-y-auto" id="panel-config">
                        <ConfigPage />
                    </div>
                )}

                {selectedId !== null && selectedId !== 'config' && selectedConnection && (
                    <div class="flex-1 flex flex-col bg-slate-800 border border-slate-700 rounded-xl overflow-hidden" id={`panel-${selectedConnection.id}`}>
                        {/* Header info */}
                        <div class="px-6 py-5 border-b border-slate-700 flex justify-between items-center shrink-0 bg-black/15">
                            <div>
                                <div class="text-lg font-bold text-slate-50">{selectedConnection.name}</div>
                                <div class="text-[0.78rem] text-slate-400 font-mono mt-0.5">{selectedConnection.address} : {selectedConnection.port} &nbsp;·&nbsp; {selectedConnection.type} &nbsp;·&nbsp; {selectedConnection.id}</div>
                            </div>
                            <div class="flex gap-6 items-center">
                                <div class="text-right">
                                    <div class="text-xl font-bold text-sky-400 leading-tight">{selectedConnection.deviceCount}</div>
                                    <div class="text-[0.65rem] uppercase tracking-wider text-slate-400">Devices</div>
                                </div>
                                <div class="text-right">
                                    <div class={`text-xl font-bold leading-tight ${
                                        selectedConnection.status === 'online' ? 'text-emerald-500' : selectedConnection.status === 'stale' ? 'text-amber-400' : 'text-red-500'
                                    }`}>
                                        {selectedConnection.status.toUpperCase()}
                                    </div>
                                    <div class="text-[0.65rem] uppercase tracking-wider text-slate-400">Status</div>
                                </div>
                            </div>
                        </div>

                        {/* Health chart */}
                        <div class="px-6 py-5 border-b border-slate-700/50 shrink-0">
                            <div class="flex items-center justify-between mb-2">
                                <div class="flex items-center gap-2 text-sm font-semibold text-slate-300">
                                    <i class="fas fa-heartbeat text-emerald-500 text-xs animate-pulse"></i>
                                    Device Availability
                                </div>
                                <div class="text-[0.65rem] font-bold text-slate-500 uppercase tracking-widest bg-white/5 px-2 py-0.5 rounded">24h · 5 min intervals</div>
                            </div>
                            <div class="h-[110px] health-chart" ref={chartRef}></div>
                        </div>

                        {/* Device list */}
                        <div class="flex-1 overflow-y-auto px-6 py-4 device-table-wrap">
                            {!selectedConnection.devices || selectedConnection.devices.length === 0 ? (
                                <div class="py-8 text-center text-slate-400 opacity-50">
                                    No devices configured on this connection.
                                </div>
                            ) : (
                                <table class="w-full border-separate border-spacing-0 text-sm">
                                    <thead>
                                        <tr class="text-left text-[0.68rem] font-bold uppercase tracking-wider text-slate-400">
                                            <th class="px-3 py-2.5 border-b border-slate-700 whitespace-nowrap">Device</th>
                                            <th class="px-3 py-2.5 border-b border-slate-700 whitespace-nowrap">Protocol</th>
                                            <th class="px-3 py-2.5 border-b border-slate-700 whitespace-nowrap">Address</th>
                                            <th class="px-3 py-2.5 border-b border-slate-700 whitespace-nowrap">Asset Type</th>
                                            <th class="px-3 py-2.5 border-b border-slate-700 whitespace-nowrap">Status</th>
                                            <th class="px-3 py-2.5 border-b border-slate-700 whitespace-nowrap">Last Seen</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {selectedConnection.devices.map((dev: any) => (
                                            <tr key={dev.name} class="transition-colors duration-150 hover:bg-white/[0.03]">
                                                <td class="px-3 py-3 border-b border-white/[0.04] align-middle">
                                                    <div class="font-semibold text-slate-100">{dev.name}</div>
                                                    <div class="text-[0.72rem] text-slate-400 mt-0.5">{dev.deviceType}</div>
                                                </td>
                                                <td class="px-3 py-3 border-b border-white/[0.04] align-middle">
                                                    <span class="dev-type-pill inline-block text-[0.68rem] font-bold uppercase tracking-wide px-2 py-0.5 rounded-full bg-sky-400/10 text-sky-400 border border-sky-400/20">
                                                        {dev.protocol}
                                                    </span>
                                                </td>
                                                <td class="px-3 py-3 border-b border-white/[0.04] align-middle font-mono text-[0.82rem] text-slate-300">
                                                    {dev.address}
                                                </td>
                                                <td class="px-3 py-3 border-b border-white/[0.04] align-middle">
                                                    {dev.assetType ? (
                                                        <span class="inline-block text-[0.68rem] px-2 py-0.5 rounded bg-white/5 text-slate-400 font-mono">{dev.assetType}</span>
                                                    ) : (
                                                        <span class="text-slate-400 opacity-40">–</span>
                                                    )}
                                                </td>
                                                <td class="px-3 py-3 border-b border-white/[0.04] align-middle">
                                                    <div class={`flex items-center gap-1.5 text-[0.78rem] font-semibold ${
                                                        dev.status === 'online' ? 'text-emerald-500' : dev.status === 'stale' ? 'text-amber-400' : 'text-red-500'
                                                    }`}>
                                                        <span class={`w-[7px] h-[7px] rounded-full inline-block ${
                                                            dev.status === 'online' ? 'bg-emerald-500' : dev.status === 'stale' ? 'bg-amber-400' : 'bg-red-500'
                                                        }`}></span>
                                                        {dev.status.toUpperCase()}
                                                    </div>
                                                </td>
                                                <td class="px-3 py-3 border-b border-white/[0.04] align-middle text-[0.78rem] font-mono text-slate-400">
                                                    {dev.lastSeen}
                                                </td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            )}
                        </div>
                    </div>
                )}
            </div>
        </div>
    );
}
