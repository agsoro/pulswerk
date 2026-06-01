import { useState, useEffect, useRef } from 'preact/hooks';
import { t } from '../i18n';

interface TrajectoryStatus {
    enabled: boolean;
    monthlyTargetKwh: number;
    mainMeterKey: string;
    targetKwh: number;
    actualKwh: number;
    deviationPct: number;
    isCurtailmentActive: boolean;
    controlState: string;
    logs: LogEntry[];
}

interface LogEntry {
    timestamp: string;
    message: string;
    state: string;
}

interface CurtailmentTarget {
    telemetryKey: string;
    normalValue: number;
    warningValue: number;
    criticalValue: number;
}

interface TrajectoryTarget15Min {
    timestamp: number;
    targetKwh: number;
}

export function TrajectoryPage() {
    const [status, setStatus] = useState<TrajectoryStatus | null>(null);
    const [curtailmentTargets, setCurtailmentTargets] = useState<CurtailmentTarget[]>([]);
    const [loading, setLoading] = useState(true);
    const [updatingConfig, setUpdatingConfig] = useState(false);

    // Form states
    const [enabled, setEnabled] = useState(false);
    const [monthlyTargetKwh, setMonthlyTargetKwh] = useState(3000);
    const [mainMeterKey, setMainMeterKey] = useState('');

    const [newTarget, setNewTarget] = useState<CurtailmentTarget>({
        telemetryKey: '',
        normalValue: 16,
        warningValue: 10,
        criticalValue: 6
    });

    const chartRef = useRef<HTMLDivElement>(null);
    const chartInstance = useRef<any>(null);

    const fetchStatus = async () => {
        try {
            const res = await fetch('/plswk/api/trajectory/status');
            if (res.ok) {
                const data: TrajectoryStatus = await res.ok ? await res.json() : null;
                if (data) {
                    setStatus(data);
                    setEnabled(data.enabled);
                    setMonthlyTargetKwh(data.monthlyTargetKwh);
                    setMainMeterKey(data.mainMeterKey);
                }
            }
        } catch (e) {
            console.error("Failed to fetch trajectory status:", e);
        }
    };

    const fetchCurtailmentTargets = async () => {
        try {
            const res = await fetch('/plswk/api/trajectory/targets');
            if (res.ok) {
                setCurtailmentTargets(await res.json());
            }
        } catch (e) {
            console.error("Failed to fetch curtailment targets:", e);
        }
    };

    const loadData = async () => {
        await Promise.all([fetchStatus(), fetchCurtailmentTargets()]);
        setLoading(false);
    };

    useEffect(() => {
        loadData();
        const interval = setInterval(fetchStatus, 5000);
        return () => clearInterval(interval);
    }, []);

    // Fetch and render chart series
    useEffect(() => {
        if (loading || !status) return;

        const renderChart = async () => {
            const now = new Date();
            const startOfMonth = new Date(now.getFullYear(), now.getMonth(), 1);
            const endOfMonth = new Date(now.getFullYear(), now.getMonth() + 1, 1);
            const startTs = startOfMonth.getTime();
            const endTs = endOfMonth.getTime();

            // 1. Fetch 15min targets
            let targetSeriesData: { x: number, y: number }[] = [];
            try {
                const res = await fetch('/plswk/api/trajectory/targets/15min');
                if (res.ok) {
                    const list: TrajectoryTarget15Min[] = await res.json();
                    targetSeriesData = list.map(t => ({ x: t.timestamp, y: t.targetKwh }));
                }
            } catch (e) {
                console.error("Failed to load 15min targets:", e);
            }

            // Fallback to linear if empty
            if (targetSeriesData.length === 0) {
                targetSeriesData = [
                    { x: startTs, y: 0 },
                    { x: endTs, y: status.monthlyTargetKwh }
                ];
            }

            // 2. Fetch history of actual consumption
            let actualSeriesData: { x: number, y: number }[] = [];
            if (status.mainMeterKey) {
                try {
                    const res = await fetch(`/plswk/api/history?key=${encodeURIComponent(status.mainMeterKey)}&startTs=${startTs}&endTs=${Date.now()}`);
                    if (res.ok) {
                        const historyPoints: any[] = await res.json();
                        if (historyPoints && historyPoints.length > 0) {
                            // Find the base value (earliest point)
                            // points are returned descending from API
                            const sortedPoints = [...historyPoints].sort((a, b) => a.ts - b.ts);
                            const earliestValue = sortedPoints[0]?.value || 0;
                            actualSeriesData = sortedPoints.map(p => ({
                                x: p.ts,
                                y: Math.max(0, (p.value || 0) - earliestValue)
                            }));
                        }
                    }
                } catch (e) {
                    console.error("Failed to load actual trajectory history:", e);
                }
            }

            if (chartInstance.current) {
                chartInstance.current.destroy();
            }

            if (chartRef.current && (window as any).ApexCharts) {
                const options = {
                    series: [
                        { name: t('ems_target_trajectory'), data: targetSeriesData },
                        { name: t('ems_actual_consumption'), data: actualSeriesData }
                    ],
                    chart: {
                        type: 'line',
                        height: '100%',
                        toolbar: { show: true },
                        zoom: { enabled: true },
                        background: 'transparent',
                        foreColor: '#94a3b8'
                    },
                    colors: ['#06b6d4', '#e11d48'], // cyan vs rose
                    stroke: {
                        width: [2, 3],
                        dashArray: [5, 0], // dashed for planned, solid for actual
                        curve: 'smooth'
                    },
                    grid: {
                        borderColor: 'rgba(255,255,255,0.06)'
                    },
                    xaxis: {
                        type: 'datetime',
                        labels: {
                            style: { colors: '#64748b' }
                        }
                    },
                    yaxis: {
                        title: { text: `${t('nav_billing')} (kWh)` },
                        labels: {
                            formatter: (v: number) => v.toFixed(1)
                        }
                    },
                    tooltip: {
                        theme: 'dark',
                        x: { format: 'dd MMM HH:mm' }
                    },
                    annotations: {
                        yaxis: status.monthlyTargetKwh ? [
                            {
                                y: status.monthlyTargetKwh,
                                borderColor: '#ef4444',
                                strokeDashArray: 4,
                                label: {
                                    borderColor: '#ef4444',
                                    style: {
                                        color: '#fff',
                                        background: '#ef4444',
                                        fontSize: '9px',
                                        fontWeight: 700
                                    },
                                    text: `${t('crud_limit')}: ${status.monthlyTargetKwh} kWh`
                                }
                            }
                        ] : []
                    }
                };
                chartInstance.current = new (window as any).ApexCharts(chartRef.current, options);
                chartInstance.current.render();
            }
        };

        renderChart();
    }, [loading, status?.monthlyTargetKwh, status?.mainMeterKey]);

    const handleUpdateConfig = async (e: Event) => {
        e.preventDefault();
        setUpdatingConfig(true);
        try {
            const res = await fetch('/plswk/api/trajectory/config', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ enabled, monthlyTargetKwh, mainMeterKey })
            });
            if (res.ok) {
                alert("Configuration updated!");
                fetchStatus();
            }
        } catch (e) {
            console.error(e);
        } finally {
            setUpdatingConfig(false);
        }
    };

    const handleAddCurtailmentTarget = async (e: Event) => {
        e.preventDefault();
        if (!newTarget.telemetryKey) return;
        try {
            const res = await fetch('/plswk/api/trajectory/targets', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(newTarget)
            });
            if (res.ok) {
                setNewTarget({ telemetryKey: '', normalValue: 16, warningValue: 10, criticalValue: 6 });
                fetchCurtailmentTargets();
            }
        } catch (e) {
            console.error(e);
        }
    };

    const handleDeleteCurtailmentTarget = async (key: string) => {
        if (!confirm(`Delete curtailment target for '${key}'?`)) return;
        try {
            const res = await fetch(`/plswk/api/trajectory/targets/${encodeURIComponent(key)}`, {
                method: 'DELETE'
            });
            if (res.ok) fetchCurtailmentTargets();
        } catch (e) {
            console.error(e);
        }
    };

    // Client-side CSV parser
    const handleCsvUpload = (e: Event) => {
        const file = (e.target as HTMLInputElement).files?.[0];
        if (!file) return;

        const reader = new FileReader();
        reader.onload = async (evt) => {
            const text = evt.target?.result as string;
            if (!text) return;

            const lines = text.split('\n');
            const targets: TrajectoryTarget15Min[] = [];

            for (const line of lines) {
                const parts = line.trim().split(',');
                if (parts.length < 2) continue;

                // skip header
                if (parts[0].toLowerCase().includes('time') || parts[0].toLowerCase().includes('timestamp')) continue;

                const ts = parseInt(parts[0]);
                const kwh = parseFloat(parts[1]);

                if (!isNaN(ts) && !isNaN(kwh)) {
                    targets.push({ timestamp: ts, targetKwh: kwh });
                }
            }

            if (targets.length === 0) {
                alert("No valid trajectory points found. Check CSV format (Timestamp,TargetKwh).");
                return;
            }

            try {
                const res = await fetch('/plswk/api/trajectory/targets/15min', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(targets)
                });
                if (res.ok) {
                    alert(`Successfully imported ${targets.length} setpoints!`);
                    fetchStatus();
                } else {
                    alert("Failed to submit trajectory targets to server.");
                }
            } catch (err) {
                console.error(err);
                alert("Upload failed.");
            }
        };
        reader.readAsText(file);
    };

    if (loading || !status) {
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
            {/* Status cards */}
            <div class="grid grid-cols-1 md:grid-cols-4 gap-4">
                <div class="glass p-5 rounded-xl border border-slate-700/60 flex items-center justify-between">
                    <div>
                        <div class={`text-2xl font-black ${status.isCurtailmentActive ? 'text-rose-500' : 'text-emerald-500'}`}>
                            {status.controlState === 'Normal' ? t('ems_normal') :
                             status.controlState === 'Warning' ? t('ems_warning') :
                             status.controlState === 'Critical' ? t('ems_critical') : status.controlState}
                        </div>
                        <div class="text-[0.7rem] uppercase tracking-wider text-slate-400 font-bold mt-1">{t('ems_control_status')}</div>
                    </div>
                    <div class={`w-10 h-10 rounded-lg flex items-center justify-center text-lg ${
                        status.isCurtailmentActive ? 'bg-rose-500/10 text-rose-500 animate-pulse' : 'bg-emerald-500/10 text-emerald-500'
                    }`}>
                        <i class={`fas ${status.isCurtailmentActive ? 'fa-exclamation-triangle' : 'fa-shield-alt'}`}></i>
                    </div>
                </div>

                <div class="glass p-5 rounded-xl border border-slate-700/60 flex items-center justify-between">
                    <div>
                        <div class={`text-2xl font-black ${status.deviationPct > 0 ? 'text-rose-400' : 'text-emerald-400'}`}>
                            {status.deviationPct > 0 ? `+${status.deviationPct}` : status.deviationPct}%
                        </div>
                        <div class="text-[0.7rem] uppercase tracking-wider text-slate-400 font-bold mt-1">{t('ems_deviation_plan')}</div>
                    </div>
                    <div class={`w-10 h-10 rounded-lg flex items-center justify-center text-lg ${
                        status.deviationPct > 0 ? 'bg-rose-400/10 text-rose-400' : 'bg-emerald-400/10 text-emerald-400'
                    }`}>
                        <i class={`fas ${status.deviationPct > 0 ? 'fa-arrow-trend-up' : 'fa-arrow-trend-down'}`}></i>
                    </div>
                </div>

                <div class="glass p-5 rounded-xl border border-slate-700/60 flex items-center justify-between">
                    <div>
                        <div class="text-2xl font-black text-slate-100">{status.actualKwh.toFixed(1)} kWh</div>
                        <div class="text-[0.7rem] uppercase tracking-wider text-slate-400 font-bold mt-1">{t('ems_actual_consumption')}</div>
                    </div>
                    <div class="w-10 h-10 rounded-lg bg-sky-400/10 text-sky-400 flex items-center justify-center text-lg">
                        <i class="fas fa-plug"></i>
                    </div>
                </div>

                <div class="glass p-5 rounded-xl border border-slate-700/60 flex items-center justify-between">
                    <div>
                        <div class="text-2xl font-black text-slate-100">{status.targetKwh.toFixed(1)} kWh</div>
                        <div class="text-[0.7rem] uppercase tracking-wider text-slate-400 font-bold mt-1">{t('ems_target_trajectory')}</div>
                    </div>
                    <div class="w-10 h-10 rounded-lg bg-cyan-400/10 text-cyan-400 flex items-center justify-center text-lg">
                        <i class="fas fa-chart-line"></i>
                    </div>
                </div>
            </div>

            {/* Graph area */}
            <div class="bg-slate-800 border border-slate-700 rounded-xl p-6 shadow-lg">
                <div class="flex justify-between items-center mb-4">
                    <h3 class="font-bold text-slate-100 text-base">{t('ems_curve_title')}</h3>
                    <div class="flex items-center gap-3">
                        <label class="py-1 px-3 rounded bg-slate-700 hover:bg-slate-600 text-slate-200 text-xs font-semibold cursor-pointer border border-slate-600">
                            <i class="fas fa-file-csv mr-1.5"></i>{t('ems_upload_profile')}
                            <input type="file" accept=".csv" onChange={handleCsvUpload} class="hidden" />
                        </label>
                        <span class="text-[0.65rem] font-bold text-slate-500 uppercase tracking-widest bg-white/5 px-2 py-0.5 rounded">{t('ems_rt_comparison')}</span>
                    </div>
                </div>
                <div class="h-[280px] w-full" ref={chartRef}></div>
            </div>

            <div class="grid grid-cols-1 lg:grid-cols-3 gap-6">
                {/* Curtailment targets panel */}
                <div class="lg:col-span-2 flex flex-col gap-6">
                    {/* Add & List Curtailment Targets */}
                    <div class="bg-slate-800 border border-slate-700 rounded-xl overflow-hidden shadow-lg">
                        <div class="px-6 py-4 border-b border-slate-700 bg-black/15">
                            <h3 class="font-bold text-slate-50 text-base">{t('ems_curtailment_register')}</h3>
                        </div>
                        <div class="p-6 border-b border-slate-700/60 bg-black/5">
                            <form onSubmit={handleAddCurtailmentTarget} class="grid grid-cols-1 md:grid-cols-4 gap-4 items-end">
                                <div class="flex flex-col gap-1.5">
                                    <label class="text-[0.65rem] font-bold text-slate-400 uppercase tracking-wide">{t('ems_writable_key')}</label>
                                    <input
                                        type="text"
                                        required
                                        value={newTarget.telemetryKey}
                                        onInput={(e) => setNewTarget({ ...newTarget, telemetryKey: (e.target as HTMLInputElement).value })}
                                        placeholder="e.g. wallbox-01_max_charge_current"
                                        class="form-input font-mono"
                                    />
                                </div>
                                <div class="flex flex-col gap-1.5">
                                    <label class="text-[0.65rem] font-bold text-slate-400 uppercase tracking-wide">{t('ems_normal_setpoint')}</label>
                                    <input
                                        type="number"
                                        required
                                        value={newTarget.normalValue}
                                        onInput={(e) => setNewTarget({ ...newTarget, normalValue: parseFloat((e.target as HTMLInputElement).value) })}
                                        class="form-input"
                                    />
                                </div>
                                <div class="flex flex-col gap-1.5">
                                    <label class="text-[0.65rem] font-bold text-slate-400 uppercase tracking-wide">{t('ems_warning_setpoint')}</label>
                                    <input
                                        type="number"
                                        required
                                        value={newTarget.warningValue}
                                        onInput={(e) => setNewTarget({ ...newTarget, warningValue: parseFloat((e.target as HTMLInputElement).value) })}
                                        class="form-input"
                                    />
                                </div>
                                <div class="flex flex-col gap-1.5">
                                    <label class="text-[0.65rem] font-bold text-slate-400 uppercase tracking-wide">{t('ems_critical_setpoint')}</label>
                                    <div class="flex gap-2">
                                        <input
                                            type="number"
                                            required
                                            value={newTarget.criticalValue}
                                            onInput={(e) => setNewTarget({ ...newTarget, criticalValue: parseFloat((e.target as HTMLInputElement).value) })}
                                            class="form-input w-full"
                                        />
                                        <button
                                            type="submit"
                                            class="py-1.5 px-4 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors shrink-0"
                                        >
                                            {t('btn_add')}
                                        </button>
                                    </div>
                                </div>
                            </form>
                        </div>
                        <div class="max-h-[300px] overflow-y-auto">
                            {curtailmentTargets.length === 0 ? (
                                <div class="p-8 text-center text-slate-500">No curtailment targets configured. Curtailment won't trigger any device changes.</div>
                            ) : (
                                <table class="lv-table text-sm">
                                    <thead>
                                        <tr>
                                            <th>{t('ems_writable_key')}</th>
                                            <th class="text-right">{t('ems_normal')}</th>
                                            <th class="text-right">{t('ems_warning')}</th>
                                            <th class="text-right">{t('ems_critical')}</th>
                                            <th class="text-right">{t('bill_actions')}</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {curtailmentTargets.map((ct) => (
                                            <tr key={ct.telemetryKey}>
                                                <td class="font-mono text-slate-300 text-xs">
                                                    {ct.telemetryKey}
                                                </td>
                                                <td class="text-right font-mono font-semibold text-emerald-400">
                                                    {ct.normalValue}
                                                </td>
                                                <td class="text-right font-mono font-semibold text-amber-400">
                                                    {ct.warningValue}
                                                </td>
                                                <td class="text-right font-mono font-semibold text-rose-500">
                                                    {ct.criticalValue}
                                                </td>
                                                <td class="text-right">
                                                    <button
                                                        onClick={() => handleDeleteCurtailmentTarget(ct.telemetryKey)}
                                                        class="p-1 px-2 rounded bg-red-500/10 text-red-400 hover:bg-red-500/20 text-xs transition-colors"
                                                    >
                                                        <i class="fas fa-trash-alt"></i>
                                                    </button>
                                                </td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            )}
                        </div>
                    </div>
                </div>

                {/* Settings & Logs */}
                <div class="flex flex-col gap-6">
                    {/* General Settings */}
                    <div class="glass p-6 rounded-xl border border-slate-700/60 shadow-lg">
                        <h3 class="font-bold text-slate-50 text-base mb-4 border-b border-slate-700 pb-2">{t('ems_control_settings')}</h3>
                        <form onSubmit={handleUpdateConfig} class="flex flex-col gap-4">
                            <div class="flex items-center justify-between">
                                <span class="text-xs font-semibold text-slate-300">{t('ems_enable_control')}</span>
                                <label class="relative inline-flex items-center cursor-pointer">
                                    <input 
                                        type="checkbox" 
                                        checked={enabled} 
                                        onChange={(e) => setEnabled((e.target as HTMLInputElement).checked)}
                                        class="sr-only peer" 
                                    />
                                    <div class="w-11 h-6 bg-slate-700 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-gray-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-sky-500"></div>
                                </label>
                            </div>

                             <div class="flex flex-col gap-1.5">
                                <label class="text-[0.65rem] font-bold text-slate-400 uppercase tracking-wide">{t('ems_monthly_limit')}</label>
                                <input
                                    type="number"
                                    required
                                    value={monthlyTargetKwh}
                                    onInput={(e) => setMonthlyTargetKwh(parseInt((e.target as HTMLInputElement).value))}
                                    class="form-input font-mono"
                                />
                            </div>

                            <div class="flex flex-col gap-1.5">
                                <label class="text-[0.65rem] font-bold text-slate-400 uppercase tracking-wide">{t('ems_grid_meter_key')}</label>
                                <input
                                    type="text"
                                    required
                                    value={mainMeterKey}
                                    onInput={(e) => setMainMeterKey((e.target as HTMLInputElement).value)}
                                    placeholder="analytics-summary_daily-kwh"
                                    class="form-input font-mono"
                                />
                            </div>

                            <button
                                type="submit"
                                disabled={updatingConfig}
                                class="py-2 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 disabled:opacity-40 transition-colors mt-2"
                            >
                                <i class="fas fa-check mr-1.5"></i>{updatingConfig ? 'Applying...' : t('ems_save_config')}
                            </button>
                        </form>
                    </div>

                    {/* Logs Panel */}
                    <div class="glass p-5 rounded-xl border border-slate-700/60 shadow-lg flex flex-col gap-3 max-h-[350px]">
                        <h4 class="font-bold text-slate-200 text-sm border-b border-slate-700/50 pb-2"><i class="fas fa-history mr-2"></i>{t('ems_intervention_logs')}</h4>
                        <div class="overflow-y-auto flex flex-col gap-2 pr-1 font-mono text-[0.7rem]">
                            {status.logs.length === 0 ? (
                                <p class="text-slate-500 text-center py-4">{t('ems_no_intervention')}</p>
                            ) : (
                                status.logs.map((log, idx) => (
                                    <div key={idx} class="p-2 rounded bg-black/25 border-l-2 flex flex-col gap-0.5 border-slate-500">
                                        <div class="flex justify-between items-center text-[0.65rem] text-slate-500">
                                            <span>{log.timestamp}</span>
                                            <span class={`font-bold ${
                                                log.state.includes('Critical') ? 'text-rose-500' :
                                                log.state.includes('Warning') ? 'text-amber-500' : 'text-emerald-500'
                                            }`}>
                                                {log.state === 'Critical' ? t('ems_critical') :
                                                 log.state === 'Warning' ? t('ems_warning') :
                                                 log.state === 'Normal' ? t('ems_normal') : log.state}
                                            </span>
                                        </div>
                                        <p class="text-slate-300 break-words mt-0.5">{log.message}</p>
                                    </div>
                                ))
                            )}
                        </div>
                    </div>
                </div>
            </div>
        </div>
    );
}
