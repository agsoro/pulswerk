import { useState, useEffect, useRef } from 'preact/hooks';
import { t } from '../i18n';

export function HeartbeatPage() {
    const [stats, setStats] = useState<any>(null);
    const [loading, setLoading] = useState(true);

    const chartRef = useRef<HTMLDivElement>(null);
    const chartInstance = useRef<any>(null);
    const pushBufferRef = useRef<number[]>(Array(60).fill(0));
    const pullBufferRef = useRef<number[]>(Array(60).fill(0));
    const lastPushRef = useRef<number>(0);
    const lastPullRef = useRef<number>(0);

    const formatUptime = (seconds: number) => {
        const d = Math.floor(seconds / 86400);
        const h = Math.floor((seconds % 86400) / 3600);
        const m = Math.floor((seconds % 3600) / 60);
        const s = seconds % 60;
        return d > 0 
            ? `${d}d ${h}h ${m}m` 
            : (h > 0 ? `${h}h ${m}m ${s}s` : `${m}m ${s}s`);
    };

    const formatSize = (bytes: number) => {
        if (bytes > 1024 * 1024 * 1024) {
            return (bytes / (1024.0 * 1024.0 * 1024.0)).toFixed(2) + ' GB';
        }
        return (bytes / (1024.0 * 1024.0)).toFixed(2) + ' MB';
    };

    useEffect(() => {
        const fetchInitial = async () => {
            try {
                const res = await fetch('/plswk/api/heartbeat/stats');
                if (res.ok) {
                    const data = await res.json();
                    setStats(data);
                    lastPushRef.current = data.totalPushUpdates;
                    lastPullRef.current = data.totalPullUpdates;
                }
            } catch (e) {
                console.error(e);
            } finally {
                setLoading(false);
            }
        };
        fetchInitial();
    }, []);

    // Setup ApexChart when stats load
    useEffect(() => {
        if (loading || !stats) return;

        if (chartRef.current && (window as any).ApexCharts) {
            const options = {
                series: [
                    { name: 'Push (COV)', data: pushBufferRef.current },
                    { name: 'Pull (Poll)', data: pullBufferRef.current }
                ],
                chart: {
                    type: 'area',
                    height: '100%',
                    stacked: true,
                    animations: { enabled: false },
                    toolbar: { show: false },
                    sparkline: { enabled: false }
                },
                colors: ['#8b5cf6', '#10b981'],
                fill: {
                    type: 'gradient',
                    gradient: {
                        shadeIntensity: 1,
                        opacityFrom: 0.45,
                        opacityTo: 0.05,
                        stops: [20, 100]
                    }
                },
                stroke: { curve: 'smooth', width: 2 },
                legend: {
                    show: true,
                    position: 'top',
                    horizontalAlign: 'right',
                    labels: { colors: '#94a3b8' },
                    markers: { width: 8, height: 8, radius: 4 },
                    fontSize: '12px',
                    fontFamily: 'inherit'
                },
                grid: {
                    borderColor: 'rgba(255,255,255,0.06)',
                    xaxis: { lines: { show: false } },
                    yaxis: { lines: { show: true } },
                    padding: { bottom: 10, left: 10, right: 10, top: 0 }
                },
                xaxis: {
                    labels: { 
                        show: true,
                        style: { colors: '#64748b', fontSize: '10px' },
                        formatter: (val: number) => val > 0 ? `-${60 - val}s` : ''
                    },
                    axisBorder: { show: true, color: 'rgba(255,255,255,0.08)' },
                    axisTicks: { show: false }
                },
                yaxis: {
                    min: 0,
                    forceNiceScale: true,
                    labels: {
                        style: { colors: '#64748b', fontSize: '10px' }
                    },
                    title: { text: 'updates/s', style: { color: '#475569', fontSize: '11px' } }
                },
                dataLabels: { enabled: false },
                tooltip: {
                    theme: 'dark',
                    shared: true,
                    intersect: false,
                    y: { formatter: (val: number) => val.toLocaleString() + ' upd/s' }
                }
            };
            chartInstance.current = new (window as any).ApexCharts(chartRef.current, options);
            chartInstance.current.render();
        }

        const interval = setInterval(async () => {
            try {
                const res = await fetch('/plswk/api/heartbeat/stats');
                if (!res.ok) return;
                const data = await res.json();
                setStats(data);

                const pushDiff = Math.max(0, data.totalPushUpdates - lastPushRef.current);
                const pullDiff = Math.max(0, data.totalPullUpdates - lastPullRef.current);
                lastPushRef.current = data.totalPushUpdates;
                lastPullRef.current = data.totalPullUpdates;

                pushBufferRef.current.shift();
                pushBufferRef.current.push(pushDiff);
                pullBufferRef.current.shift();
                pullBufferRef.current.push(pullDiff);

                if (chartInstance.current) {
                    chartInstance.current.updateSeries([
                        { name: 'Push (COV)', data: [...pushBufferRef.current] },
                        { name: 'Pull (Poll)', data: [...pullBufferRef.current] }
                    ]);
                }
            } catch (e) {
                console.error("Failed to poll heartbeat stats:", e);
            }
        }, 1000);

        return () => {
            clearInterval(interval);
            if (chartInstance.current) {
                chartInstance.current.destroy();
                chartInstance.current = null;
            }
        };
    }, [loading]);

    if (loading || !stats) {
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
        <div class="flex flex-col gap-6 w-full page-enter">
            {/* Header */}
            <div class="flex items-center justify-between animate-fade-in shrink-0">
                <div>
                    <h1 class="text-3xl font-extrabold tracking-tight text-white" data-i18n="system_heartbeat">{t('system_heartbeat')}</h1>
                    <p class="text-slate-400 mt-1" data-i18n="system_heartbeat_desc">{t('system_heartbeat_desc')}</p>
                </div>
                <div class="flex items-center gap-3 bg-white/5 px-4 py-2 rounded-xl border border-white/10 shadow-lg" id="statusBadge">
                    {stats.isScanning ? (
                        <>
                            <div class="w-3 h-3 rounded-full bg-amber-500 animate-pulse"></div>
                            <span class="text-sm font-semibold text-amber-400 uppercase tracking-wider">{t('scanning')}</span>
                        </>
                    ) : (
                        <>
                            <div class="w-3 h-3 rounded-full bg-emerald-500 animate-pulse"></div>
                            <span class="text-sm font-semibold text-emerald-400 uppercase tracking-wider">{t('system_operational')}</span>
                        </>
                    )}
                </div>
            </div>

            {/* Top Cards Grid */}
            <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6 animate-slide-up">
                {/* Uptime */}
                <div class="bg-slate-800/50 border border-white/10 p-6 rounded-2xl shadow-xl flex flex-col justify-between min-h-[140px]">
                    <div class="flex items-center justify-between">
                        <div class="w-10 h-10 bg-indigo-500/20 text-indigo-400 rounded-xl flex items-center justify-center">
                            <i class="fas fa-clock text-xl"></i>
                        </div>
                        <span class="text-[0.65rem] font-bold text-slate-500 uppercase tracking-widest">{t('uptime')}</span>
                    </div>
                    <div class="text-3xl font-bold text-white tracking-tight mt-4">{formatUptime(stats.uptimeSeconds)}</div>
                </div>

                {/* Updates/min */}
                <div class="bg-slate-800/50 border border-white/10 p-6 rounded-2xl shadow-xl flex flex-col justify-between min-h-[140px]">
                    <div class="flex items-center justify-between">
                        <div class="w-10 h-10 bg-emerald-500/20 text-emerald-400 rounded-xl flex items-center justify-center">
                            <i class="fas fa-bolt text-xl"></i>
                        </div>
                        <span class="text-[0.65rem] font-bold text-slate-500 uppercase tracking-widest">{t('updates_min')}</span>
                    </div>
                    <div class="text-3xl font-bold text-white tracking-tight mt-4">{stats.updatesPerMinute.toFixed(1)}</div>
                </div>

                {/* Data Points */}
                <div class="bg-slate-800/50 border border-white/10 p-6 rounded-2xl shadow-xl flex flex-col justify-between min-h-[140px]">
                    <div class="flex items-center justify-between">
                        <div class="w-10 h-10 bg-sky-500/20 text-sky-400 rounded-xl flex items-center justify-center">
                            <i class="fas fa-database text-xl"></i>
                        </div>
                        <span class="text-[0.65rem] font-bold text-slate-500 uppercase tracking-widest">{t('points')}</span>
                    </div>
                    <div class="text-3xl font-bold text-white tracking-tight mt-4">{stats.totalTelemetries.toLocaleString()}</div>
                </div>

                {/* DB Size */}
                <div class="bg-slate-800/50 border border-white/10 p-6 rounded-2xl shadow-xl flex flex-col justify-between min-h-[140px]">
                    <div class="flex items-center justify-between">
                        <div class="w-10 h-10 bg-amber-500/20 text-amber-400 rounded-xl flex items-center justify-center">
                            <i class="fas fa-hdd text-xl"></i>
                        </div>
                        <span class="text-[0.65rem] font-bold text-slate-500 uppercase tracking-widest">{t('storage')}</span>
                    </div>
                    <div class="text-3xl font-bold text-white tracking-tight mt-4">{formatSize(stats.databaseSizeBytes)}</div>
                </div>
            </div>

            {/* Bottom Section */}
            <div class="grid grid-cols-1 lg:grid-cols-3 gap-8 animate-slide-up" style={{ animationDelay: '0.1s' }}>
                {/* System Info */}
                <div class="lg:col-span-1 bg-slate-800/50 border border-white/10 rounded-2xl shadow-xl overflow-hidden flex flex-col gap-4 p-6 justify-between">
                    <div>
                        <h3 class="text-lg font-bold text-white flex items-center gap-2 border-b border-white/5 pb-4 mb-4">
                            <i class="fas fa-server text-slate-500"></i> {t('system_info')}
                        </h3>
                        <div class="space-y-4">
                            <div class="flex justify-between text-sm">
                                <span class="text-slate-400">{t('version')}</span>
                                <span class="text-white font-medium">{stats.version}</span>
                            </div>
                            <div class="flex justify-between text-sm">
                                <span class="text-slate-400">{t('environment')}</span>
                                <span class="text-white font-medium">{t('production')}</span>
                            </div>
                            <div class="flex justify-between text-sm">
                                <span class="text-slate-400">{t('devices')}</span>
                                <span class="text-white font-medium flex items-center gap-1.5">
                                    <span class="text-emerald-400">{stats.onlineDevices}</span>
                                    {stats.staleDevices > 0 && <span class="text-amber-400">/ {stats.staleDevices} stale</span>}
                                    {stats.offlineDevices > 0 && <span class="text-red-400">/ {stats.offlineDevices} off</span>}
                                    <span class="text-slate-500">/ {stats.totalDevices}</span>
                                </span>
                            </div>
                            <div class="flex justify-between text-sm">
                                <span class="text-slate-400">{t('data_point_keys')}</span>
                                <span class="text-white font-medium">{stats.totalTelemetryKeys.toLocaleString()}</span>
                            </div>
                            <div class="flex justify-between text-sm border-t border-white/5 pt-4">
                                <span class="text-slate-400">Memory (RSS / GC)</span>
                                <span class="text-white font-medium">{stats.workingSetMb} MB / {stats.gcHeapMb} MB</span>
                            </div>
                            <div class="flex justify-between text-sm">
                                <span class="text-slate-400">TCP Connections (pooled)</span>
                                <span class="text-white font-medium">{stats.tcpConnections}</span>
                            </div>
                            {stats.oldestDeviceSeenUtc && (
                                <div class="flex justify-between text-sm">
                                    <span class="text-slate-400">Oldest Device Seen</span>
                                    <span class="text-white font-medium text-[0.78rem] font-mono">{new Date(stats.oldestDeviceSeenUtc).toLocaleString()}</span>
                                </div>
                            )}
                        </div>
                    </div>
                    <div class="border-t border-white/5 pt-4">
                        <div class="flex justify-between text-sm">
                            <span class="text-slate-400">{t('total_updates_processed')}</span>
                            <span class="text-sky-400 font-bold text-base">{stats.totalUpdates.toLocaleString()}</span>
                        </div>
                    </div>
                </div>

                {/* Throughput Chart */}
                <div class="lg:col-span-2 bg-slate-800/50 border border-white/10 rounded-2xl shadow-xl p-6 flex flex-col">
                    <h3 class="text-lg font-bold text-white flex items-center gap-2 border-b border-white/5 pb-4 mb-4">
                        <i class="fas fa-chart-line text-slate-500"></i> {t('update_throughput')}
                        <span class="ml-auto text-[0.65rem] font-bold text-slate-500 uppercase tracking-widest bg-white/5 px-2 py-0.5 rounded">{t('live_window')}</span>
                    </h3>
                    <div class="flex-1 h-[250px]" ref={chartRef}></div>
                </div>
            </div>
        </div>
    );
}
