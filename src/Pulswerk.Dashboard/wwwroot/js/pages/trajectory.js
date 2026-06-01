import { jsx as _jsx, jsxs as _jsxs } from "preact/jsx-runtime";
import { useState, useEffect, useRef } from 'preact/hooks';
import { t } from '../i18n';
export function TrajectoryPage() {
    const [status, setStatus] = useState(null);
    const [curtailmentTargets, setCurtailmentTargets] = useState([]);
    const [loading, setLoading] = useState(true);
    const [updatingConfig, setUpdatingConfig] = useState(false);
    // Form states
    const [enabled, setEnabled] = useState(false);
    const [monthlyTargetKwh, setMonthlyTargetKwh] = useState(3000);
    const [mainMeterKey, setMainMeterKey] = useState('');
    const [newTarget, setNewTarget] = useState({
        telemetryKey: '',
        normalValue: 16,
        warningValue: 10,
        criticalValue: 6
    });
    const chartRef = useRef(null);
    const chartInstance = useRef(null);
    const fetchStatus = async () => {
        try {
            const res = await fetch('/plswk/api/trajectory/status');
            if (res.ok) {
                const data = await res.ok ? await res.json() : null;
                if (data) {
                    setStatus(data);
                    setEnabled(data.enabled);
                    setMonthlyTargetKwh(data.monthlyTargetKwh);
                    setMainMeterKey(data.mainMeterKey);
                }
            }
        }
        catch (e) {
            console.error("Failed to fetch trajectory status:", e);
        }
    };
    const fetchCurtailmentTargets = async () => {
        try {
            const res = await fetch('/plswk/api/trajectory/targets');
            if (res.ok) {
                setCurtailmentTargets(await res.json());
            }
        }
        catch (e) {
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
        if (loading || !status)
            return;
        const renderChart = async () => {
            const now = new Date();
            const startOfMonth = new Date(now.getFullYear(), now.getMonth(), 1);
            const endOfMonth = new Date(now.getFullYear(), now.getMonth() + 1, 1);
            const startTs = startOfMonth.getTime();
            const endTs = endOfMonth.getTime();
            // 1. Fetch 15min targets
            let targetSeriesData = [];
            try {
                const res = await fetch('/plswk/api/trajectory/targets/15min');
                if (res.ok) {
                    const list = await res.json();
                    targetSeriesData = list.map(t => ({ x: t.timestamp, y: t.targetKwh }));
                }
            }
            catch (e) {
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
            let actualSeriesData = [];
            if (status.mainMeterKey) {
                try {
                    const res = await fetch(`/plswk/api/history?key=${encodeURIComponent(status.mainMeterKey)}&startTs=${startTs}&endTs=${Date.now()}`);
                    if (res.ok) {
                        const historyPoints = await res.json();
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
                }
                catch (e) {
                    console.error("Failed to load actual trajectory history:", e);
                }
            }
            if (chartInstance.current) {
                chartInstance.current.destroy();
            }
            if (chartRef.current && window.ApexCharts) {
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
                            formatter: (v) => v.toFixed(1)
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
                chartInstance.current = new window.ApexCharts(chartRef.current, options);
                chartInstance.current.render();
            }
        };
        renderChart();
    }, [loading, status?.monthlyTargetKwh, status?.mainMeterKey]);
    const handleUpdateConfig = async (e) => {
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
        }
        catch (e) {
            console.error(e);
        }
        finally {
            setUpdatingConfig(false);
        }
    };
    const handleAddCurtailmentTarget = async (e) => {
        e.preventDefault();
        if (!newTarget.telemetryKey)
            return;
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
        }
        catch (e) {
            console.error(e);
        }
    };
    const handleDeleteCurtailmentTarget = async (key) => {
        if (!confirm(`Delete curtailment target for '${key}'?`))
            return;
        try {
            const res = await fetch(`/plswk/api/trajectory/targets/${encodeURIComponent(key)}`, {
                method: 'DELETE'
            });
            if (res.ok)
                fetchCurtailmentTargets();
        }
        catch (e) {
            console.error(e);
        }
    };
    // Client-side CSV parser
    const handleCsvUpload = (e) => {
        const file = e.target.files?.[0];
        if (!file)
            return;
        const reader = new FileReader();
        reader.onload = async (evt) => {
            const text = evt.target?.result;
            if (!text)
                return;
            const lines = text.split('\n');
            const targets = [];
            for (const line of lines) {
                const parts = line.trim().split(',');
                if (parts.length < 2)
                    continue;
                // skip header
                if (parts[0].toLowerCase().includes('time') || parts[0].toLowerCase().includes('timestamp'))
                    continue;
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
                }
                else {
                    alert("Failed to submit trajectory targets to server.");
                }
            }
            catch (err) {
                console.error(err);
                alert("Upload failed.");
            }
        };
        reader.readAsText(file);
    };
    if (loading || !status) {
        return (_jsx("div", { class: "h-full flex items-center justify-center p-16", children: _jsxs("div", { class: "flex flex-col items-center gap-4 text-cyan-400", children: [_jsx("i", { class: "fas fa-spinner fa-spin text-3xl" }), _jsx("p", { class: "text-sm font-medium animate-pulse", children: t('loading') })] }) }));
    }
    return (_jsxs("div", { class: "flex flex-col gap-6 w-full page-enter", children: [_jsxs("div", { class: "grid grid-cols-1 md:grid-cols-4 gap-4", children: [_jsxs("div", { class: "glass p-5 rounded-xl border border-slate-700/60 flex items-center justify-between", children: [_jsxs("div", { children: [_jsx("div", { class: `text-2xl font-black ${status.isCurtailmentActive ? 'text-rose-500' : 'text-emerald-500'}`, children: status.controlState === 'Normal' ? t('ems_normal') :
                                            status.controlState === 'Warning' ? t('ems_warning') :
                                                status.controlState === 'Critical' ? t('ems_critical') : status.controlState }), _jsx("div", { class: "text-[0.7rem] uppercase tracking-wider text-slate-400 font-bold mt-1", children: t('ems_control_status') })] }), _jsx("div", { class: `w-10 h-10 rounded-lg flex items-center justify-center text-lg ${status.isCurtailmentActive ? 'bg-rose-500/10 text-rose-500 animate-pulse' : 'bg-emerald-500/10 text-emerald-500'}`, children: _jsx("i", { class: `fas ${status.isCurtailmentActive ? 'fa-exclamation-triangle' : 'fa-shield-alt'}` }) })] }), _jsxs("div", { class: "glass p-5 rounded-xl border border-slate-700/60 flex items-center justify-between", children: [_jsxs("div", { children: [_jsxs("div", { class: `text-2xl font-black ${status.deviationPct > 0 ? 'text-rose-400' : 'text-emerald-400'}`, children: [status.deviationPct > 0 ? `+${status.deviationPct}` : status.deviationPct, "%"] }), _jsx("div", { class: "text-[0.7rem] uppercase tracking-wider text-slate-400 font-bold mt-1", children: t('ems_deviation_plan') })] }), _jsx("div", { class: `w-10 h-10 rounded-lg flex items-center justify-center text-lg ${status.deviationPct > 0 ? 'bg-rose-400/10 text-rose-400' : 'bg-emerald-400/10 text-emerald-400'}`, children: _jsx("i", { class: `fas ${status.deviationPct > 0 ? 'fa-arrow-trend-up' : 'fa-arrow-trend-down'}` }) })] }), _jsxs("div", { class: "glass p-5 rounded-xl border border-slate-700/60 flex items-center justify-between", children: [_jsxs("div", { children: [_jsxs("div", { class: "text-2xl font-black text-slate-100", children: [status.actualKwh.toFixed(1), " kWh"] }), _jsx("div", { class: "text-[0.7rem] uppercase tracking-wider text-slate-400 font-bold mt-1", children: t('ems_actual_consumption') })] }), _jsx("div", { class: "w-10 h-10 rounded-lg bg-sky-400/10 text-sky-400 flex items-center justify-center text-lg", children: _jsx("i", { class: "fas fa-plug" }) })] }), _jsxs("div", { class: "glass p-5 rounded-xl border border-slate-700/60 flex items-center justify-between", children: [_jsxs("div", { children: [_jsxs("div", { class: "text-2xl font-black text-slate-100", children: [status.targetKwh.toFixed(1), " kWh"] }), _jsx("div", { class: "text-[0.7rem] uppercase tracking-wider text-slate-400 font-bold mt-1", children: t('ems_target_trajectory') })] }), _jsx("div", { class: "w-10 h-10 rounded-lg bg-cyan-400/10 text-cyan-400 flex items-center justify-center text-lg", children: _jsx("i", { class: "fas fa-chart-line" }) })] })] }), _jsxs("div", { class: "bg-slate-800 border border-slate-700 rounded-xl p-6 shadow-lg", children: [_jsxs("div", { class: "flex justify-between items-center mb-4", children: [_jsx("h3", { class: "font-bold text-slate-100 text-base", children: t('ems_curve_title') }), _jsxs("div", { class: "flex items-center gap-3", children: [_jsxs("label", { class: "py-1 px-3 rounded bg-slate-700 hover:bg-slate-600 text-slate-200 text-xs font-semibold cursor-pointer border border-slate-600", children: [_jsx("i", { class: "fas fa-file-csv mr-1.5" }), t('ems_upload_profile'), _jsx("input", { type: "file", accept: ".csv", onChange: handleCsvUpload, class: "hidden" })] }), _jsx("span", { class: "text-[0.65rem] font-bold text-slate-500 uppercase tracking-widest bg-white/5 px-2 py-0.5 rounded", children: t('ems_rt_comparison') })] })] }), _jsx("div", { class: "h-[280px] w-full", ref: chartRef })] }), _jsxs("div", { class: "grid grid-cols-1 lg:grid-cols-3 gap-6", children: [_jsx("div", { class: "lg:col-span-2 flex flex-col gap-6", children: _jsxs("div", { class: "bg-slate-800 border border-slate-700 rounded-xl overflow-hidden shadow-lg", children: [_jsx("div", { class: "px-6 py-4 border-b border-slate-700 bg-black/15", children: _jsx("h3", { class: "font-bold text-slate-50 text-base", children: t('ems_curtailment_register') }) }), _jsx("div", { class: "p-6 border-b border-slate-700/60 bg-black/5", children: _jsxs("form", { onSubmit: handleAddCurtailmentTarget, class: "grid grid-cols-1 md:grid-cols-4 gap-4 items-end", children: [_jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.65rem] font-bold text-slate-400 uppercase tracking-wide", children: t('ems_writable_key') }), _jsx("input", { type: "text", required: true, value: newTarget.telemetryKey, onInput: (e) => setNewTarget({ ...newTarget, telemetryKey: e.target.value }), placeholder: "e.g. wallbox-01_max_charge_current", class: "form-input font-mono" })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.65rem] font-bold text-slate-400 uppercase tracking-wide", children: t('ems_normal_setpoint') }), _jsx("input", { type: "number", required: true, value: newTarget.normalValue, onInput: (e) => setNewTarget({ ...newTarget, normalValue: parseFloat(e.target.value) }), class: "form-input" })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.65rem] font-bold text-slate-400 uppercase tracking-wide", children: t('ems_warning_setpoint') }), _jsx("input", { type: "number", required: true, value: newTarget.warningValue, onInput: (e) => setNewTarget({ ...newTarget, warningValue: parseFloat(e.target.value) }), class: "form-input" })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.65rem] font-bold text-slate-400 uppercase tracking-wide", children: t('ems_critical_setpoint') }), _jsxs("div", { class: "flex gap-2", children: [_jsx("input", { type: "number", required: true, value: newTarget.criticalValue, onInput: (e) => setNewTarget({ ...newTarget, criticalValue: parseFloat(e.target.value) }), class: "form-input w-full" }), _jsx("button", { type: "submit", class: "py-1.5 px-4 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors shrink-0", children: t('btn_add') })] })] })] }) }), _jsx("div", { class: "max-h-[300px] overflow-y-auto", children: curtailmentTargets.length === 0 ? (_jsx("div", { class: "p-8 text-center text-slate-500", children: "No curtailment targets configured. Curtailment won't trigger any device changes." })) : (_jsxs("table", { class: "lv-table text-sm", children: [_jsx("thead", { children: _jsxs("tr", { children: [_jsx("th", { children: t('ems_writable_key') }), _jsx("th", { class: "text-right", children: t('ems_normal') }), _jsx("th", { class: "text-right", children: t('ems_warning') }), _jsx("th", { class: "text-right", children: t('ems_critical') }), _jsx("th", { class: "text-right", children: t('bill_actions') })] }) }), _jsx("tbody", { children: curtailmentTargets.map((ct) => (_jsxs("tr", { children: [_jsx("td", { class: "font-mono text-slate-300 text-xs", children: ct.telemetryKey }), _jsx("td", { class: "text-right font-mono font-semibold text-emerald-400", children: ct.normalValue }), _jsx("td", { class: "text-right font-mono font-semibold text-amber-400", children: ct.warningValue }), _jsx("td", { class: "text-right font-mono font-semibold text-rose-500", children: ct.criticalValue }), _jsx("td", { class: "text-right", children: _jsx("button", { onClick: () => handleDeleteCurtailmentTarget(ct.telemetryKey), class: "p-1 px-2 rounded bg-red-500/10 text-red-400 hover:bg-red-500/20 text-xs transition-colors", children: _jsx("i", { class: "fas fa-trash-alt" }) }) })] }, ct.telemetryKey))) })] })) })] }) }), _jsxs("div", { class: "flex flex-col gap-6", children: [_jsxs("div", { class: "glass p-6 rounded-xl border border-slate-700/60 shadow-lg", children: [_jsx("h3", { class: "font-bold text-slate-50 text-base mb-4 border-b border-slate-700 pb-2", children: t('ems_control_settings') }), _jsxs("form", { onSubmit: handleUpdateConfig, class: "flex flex-col gap-4", children: [_jsxs("div", { class: "flex items-center justify-between", children: [_jsx("span", { class: "text-xs font-semibold text-slate-300", children: t('ems_enable_control') }), _jsxs("label", { class: "relative inline-flex items-center cursor-pointer", children: [_jsx("input", { type: "checkbox", checked: enabled, onChange: (e) => setEnabled(e.target.checked), class: "sr-only peer" }), _jsx("div", { class: "w-11 h-6 bg-slate-700 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-gray-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-sky-500" })] })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.65rem] font-bold text-slate-400 uppercase tracking-wide", children: t('ems_monthly_limit') }), _jsx("input", { type: "number", required: true, value: monthlyTargetKwh, onInput: (e) => setMonthlyTargetKwh(parseInt(e.target.value)), class: "form-input font-mono" })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.65rem] font-bold text-slate-400 uppercase tracking-wide", children: t('ems_grid_meter_key') }), _jsx("input", { type: "text", required: true, value: mainMeterKey, onInput: (e) => setMainMeterKey(e.target.value), placeholder: "analytics-summary_daily-kwh", class: "form-input font-mono" })] }), _jsxs("button", { type: "submit", disabled: updatingConfig, class: "py-2 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 disabled:opacity-40 transition-colors mt-2", children: [_jsx("i", { class: "fas fa-check mr-1.5" }), updatingConfig ? 'Applying...' : t('ems_save_config')] })] })] }), _jsxs("div", { class: "glass p-5 rounded-xl border border-slate-700/60 shadow-lg flex flex-col gap-3 max-h-[350px]", children: [_jsxs("h4", { class: "font-bold text-slate-200 text-sm border-b border-slate-700/50 pb-2", children: [_jsx("i", { class: "fas fa-history mr-2" }), t('ems_intervention_logs')] }), _jsx("div", { class: "overflow-y-auto flex flex-col gap-2 pr-1 font-mono text-[0.7rem]", children: status.logs.length === 0 ? (_jsx("p", { class: "text-slate-500 text-center py-4", children: t('ems_no_intervention') })) : (status.logs.map((log, idx) => (_jsxs("div", { class: "p-2 rounded bg-black/25 border-l-2 flex flex-col gap-0.5 border-slate-500", children: [_jsxs("div", { class: "flex justify-between items-center text-[0.65rem] text-slate-500", children: [_jsx("span", { children: log.timestamp }), _jsx("span", { class: `font-bold ${log.state.includes('Critical') ? 'text-rose-500' :
                                                                log.state.includes('Warning') ? 'text-amber-500' : 'text-emerald-500'}`, children: log.state === 'Critical' ? t('ems_critical') :
                                                                log.state === 'Warning' ? t('ems_warning') :
                                                                    log.state === 'Normal' ? t('ems_normal') : log.state })] }), _jsx("p", { class: "text-slate-300 break-words mt-0.5", children: log.message })] }, idx)))) })] })] })] })] }));
}
