import { jsx as _jsx, jsxs as _jsxs, Fragment as _Fragment } from "preact/jsx-runtime";
import { useState, useEffect, useRef } from 'preact/hooks';
import { t } from '../i18n';
import { setMobileHeaderAction } from '../mobileHeaderAction';
/** Formats a kWh value with adaptive precision so 1y totals stay readable. */
function formatKwh(value) {
    const v = value ?? 0;
    if (!isFinite(v))
        return '0';
    const abs = Math.abs(v);
    if (abs >= 1000)
        return Math.round(v).toLocaleString();
    if (abs >= 100)
        return v.toFixed(0);
    return v.toFixed(1);
}
/**
 * Renders the rolling day / month / year energy as a three-cell grid.
 * The instantaneous power is already shown as the card's headline value.
 */
function RollingEnergyLine({ label, h24, d30, y1, accentClass, onOpen, telemetryKey, }) {
    const cells = [
        { key: t('ems_24h_short'), value: formatKwh(h24) },
        { key: t('ems_30d_short'), value: formatKwh(d30) },
        { key: t('ems_1y_short'), value: formatKwh(y1) },
    ];
    return (_jsxs("div", { class: onOpen && telemetryKey ? 'cursor-pointer hover:opacity-85 transition-opacity' : '', onClick: onOpen && telemetryKey ? () => onOpen(telemetryKey) : undefined, title: telemetryKey ? `View telemetry details: ${telemetryKey}` : undefined, children: [label && (_jsx("div", { class: `text-[0.6rem] uppercase tracking-[0.16em] font-bold mb-1 ${accentClass}`, children: label })), _jsx("div", { class: "grid grid-cols-3 gap-1.5", children: cells.map(cell => (_jsxs("div", { class: "rounded-lg bg-slate-950/50 border border-slate-800/70 px-2 py-1.5 min-w-0", children: [_jsx("div", { class: "text-[0.6rem] uppercase tracking-wider text-slate-500 font-bold", children: cell.key }), _jsxs("div", { class: "font-mono leading-tight truncate", children: [_jsx("span", { class: "text-base font-bold text-slate-100", children: cell.value }), _jsx("span", { class: "text-[0.6rem] text-slate-500 ml-1", children: "kWh" })] })] }, cell.key))) })] }));
}
export function EmsPage() {
    const [snapshot, setSnapshot] = useState(null);
    const [loading, setLoading] = useState(true);
    // Modal state
    const [showConfigModal, setShowConfigModal] = useState(false);
    const [showConsumerModal, setShowConsumerModal] = useState(false);
    const [editingConsumer, setEditingConsumer] = useState(null);
    const [savingConsumer, setSavingConsumer] = useState(false);
    const [savingConfig, setSavingConfig] = useState(false);
    // Config form
    const [gridMaxKw, setGridMaxKw] = useState(8.0);
    const [gridKey, setGridKey] = useState('');
    const [pvKey, setPvKey] = useState('');
    const [hasBattery, setHasBattery] = useState(false);
    const [battPowerKey, setBattPowerKey] = useState('');
    const [battSocKey, setBattSocKey] = useState('');
    const [battReserveKw, setBattReserveKw] = useState(1.0);
    const [battMaxKw, setBattMaxKw] = useState(5.0);
    const [invertGridSign, setInvertGridSign] = useState(false);
    const [invertPvSign, setInvertPvSign] = useState(false);
    const [invertBattSign, setInvertBattSign] = useState(false);
    const fetchSnapshot = async () => {
        try {
            const res = await fetch('/plswk/api/ems/status');
            if (res.ok) {
                const data = await res.json();
                if (data) {
                    setSnapshot(data);
                }
            }
        }
        catch (e) {
            console.error("Failed to fetch EMS status:", e);
        }
        finally {
            setLoading(false);
        }
    };
    useEffect(() => {
        fetchSnapshot();
        const interval = setInterval(fetchSnapshot, 4000);
        return () => clearInterval(interval);
    }, []);
    const handleOpenSystemConfig = () => {
        const s = snapshot?.sources;
        setGridMaxKw(s?.gridMaxImportKw ?? 8.0);
        setGridKey(s?.gridMeterKey ?? '');
        setPvKey(s?.pvMeterKey ?? '');
        setHasBattery(s?.hasBattery ?? !!s?.batteryPowerKey);
        setBattPowerKey(s?.batteryPowerKey ?? '');
        setBattSocKey(s?.batterySocKey ?? '');
        setBattReserveKw(s?.batteryMinReserveKw ?? 1.0);
        setBattMaxKw(s?.batteryMaxPowerKw ?? 5.0);
        setInvertGridSign(s?.invertGridPowerSign ?? false);
        setInvertPvSign(s?.invertPvPowerSign ?? false);
        setInvertBattSign(s?.invertBatteryPowerSign ?? false);
        setShowConfigModal(true);
    };
    const openConfigRef = useRef(handleOpenSystemConfig);
    openConfigRef.current = handleOpenSystemConfig;
    useEffect(() => {
        setMobileHeaderAction({
            icon: 'fa-sliders-h',
            title: t('ems_configure_system'),
            onClick: () => openConfigRef.current(),
        });
        return () => setMobileHeaderAction(null);
    }, []);
    const handleSaveSystemConfig = async (e) => {
        e.preventDefault();
        setSavingConfig(true);
        try {
            const res = await fetch('/plswk/api/ems/config', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    enabled: snapshot?.enabled ?? true,
                    batteryReserveKw: battReserveKw,
                    batteryMaxPowerKw: battMaxKw,
                    batteryPowerKey: hasBattery ? battPowerKey : '',
                    batterySocKey: hasBattery ? battSocKey : '',
                    hasBattery: hasBattery,
                    sources: {
                        gridMeterKey: gridKey,
                        gridMaxImportKw: gridMaxKw,
                        pvMeterKey: pvKey,
                        hasBattery: hasBattery,
                        batteryPowerKey: hasBattery ? battPowerKey : '',
                        batterySocKey: hasBattery ? battSocKey : '',
                        batteryMaxPowerKw: battMaxKw,
                        batteryMaxChargeKw: battMaxKw,
                        batteryMaxDischargeKw: battMaxKw,
                        batteryMinReserveKw: battReserveKw,
                        batteryMinSocPct: snapshot?.sources?.batteryMinSocPct ?? 15.0,
                        batteryFullSocPct: snapshot?.sources?.batteryFullSocPct ?? 98.0,
                        invertGridPowerSign: invertGridSign,
                        invertPvPowerSign: invertPvSign,
                        invertBatteryPowerSign: invertBattSign
                    }
                })
            });
            if (res.ok) {
                setShowConfigModal(false);
                await fetchSnapshot();
                window.pwToast(t('ems_config_saved'));
            }
            else {
                const errText = await res.text();
                window.pwToast(`Failed to save system configuration: ${errText || res.statusText}`, 'error');
            }
        }
        catch (err) {
            console.error("Failed to save config:", err);
            window.pwToast(`Error saving configuration: ${err?.message || err}`, 'error');
        }
        finally {
            setSavingConfig(false);
        }
    };
    const handleOpenAddConsumer = () => {
        setEditingConsumer({
            id: '',
            name: '',
            basePowerKw: 0.0,
            hasOptionalTier: true,
            maxOptionalKw: 8.0,
            minOptionalKw: 1.38,
            standbyOptionalKw: 0.0,
            actualPowerKey: '',
            forcePowerKey: '',
            priority: (snapshot?.consumers?.length ?? 0) + 1,
            maxPowerKw: 22.0
        });
        setShowConsumerModal(true);
    };
    const handleOpenEditConsumer = (c) => {
        setEditingConsumer({
            ...c,
            id: c.id,
            name: c.name,
            basePowerKw: c.basePowerKw ?? 0.0,
            hasOptionalTier: c.hasOptionalTier ?? true,
            maxOptionalKw: c.maxOptionalKw ?? 8.0,
            minOptionalKw: c.minOptionalKw ?? 0.0,
            standbyOptionalKw: c.standbyOptionalKw ?? 0.0,
            actualPowerKey: c.actualPowerKey ?? '',
            forcePowerKey: c.forcePowerKey ?? '',
            priority: c.priority ?? 1,
            maxPowerKw: c.maxPowerKw ?? 22.0
        });
        setShowConsumerModal(true);
    };
    const handleDeleteConsumer = async (id, name) => {
        if (!(await window.pwConfirm(`Delete consumer '${name}'?`)))
            return;
        try {
            const res = await fetch(`/plswk/api/ems/consumers/${encodeURIComponent(id)}`, {
                method: 'DELETE'
            });
            if (res.ok) {
                await fetchSnapshot();
                window.pwToast(t('ems_consumer_deleted'));
            }
            else {
                window.pwToast('Failed to delete consumer', 'error');
            }
        }
        catch (e) {
            console.error("Failed to delete consumer:", e);
            window.pwToast('Failed to delete consumer', 'error');
        }
    };
    const handleSaveConsumer = async (e) => {
        e.preventDefault();
        if (!editingConsumer)
            return;
        setSavingConsumer(true);
        try {
            const res = await fetch('/plswk/api/ems/consumers', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(editingConsumer)
            });
            if (res.ok) {
                setShowConsumerModal(false);
                setEditingConsumer(null);
                await fetchSnapshot();
                window.pwToast(t('ems_consumer_saved'));
            }
            else {
                const errText = await res.text();
                window.pwToast(`Failed to save consumer: ${errText || res.statusText}`, 'error');
            }
        }
        catch (err) {
            console.error("Failed to save consumer:", err);
            window.pwToast(`Error saving consumer: ${err?.message || err}`, 'error');
        }
        finally {
            setSavingConsumer(false);
        }
    };
    const handleOpenTelemetryDetails = (key) => {
        if (!key)
            return;
        if (typeof window.openTelemetryDetails === 'function') {
            window.openTelemetryDetails(key);
        }
        else {
            console.warn("openTelemetryDetails is not available on window");
        }
    };
    const getConsumerIconConfig = (name = '', id = '') => {
        const lower = (name + ' ' + id).toLowerCase();
        if (lower.includes('wallbox') || lower.includes('wb-') || lower.includes('charger') || lower.includes('charge')) {
            return { icon: 'fa-charging-station', text: 'text-cyan-400', bg: 'bg-cyan-500/10', border: 'border-cyan-500/30' };
        }
        if (lower.includes('heat') || lower.includes('hvac') || lower.includes('pump') || lower.includes('waerme')) {
            return { icon: 'fa-fire-alt', text: 'text-rose-400', bg: 'bg-rose-500/10', border: 'border-rose-500/30' };
        }
        return { icon: 'fa-building', text: 'text-purple-400', bg: 'bg-purple-500/10', border: 'border-purple-500/30' };
    };
    if (loading && !snapshot) {
        return (_jsx("div", { class: "flex items-center justify-center min-h-[60vh]", children: _jsxs("div", { class: "flex items-center gap-3 text-cyan-400", children: [_jsx("i", { class: "fas fa-spinner fa-spin text-2xl" }), _jsx("span", { class: "font-medium text-slate-300", children: "Loading Energy Management..." })] }) }));
    }
    const controllableConsumers = snapshot?.consumers?.filter(c => c.hasOptionalTier) ?? [];
    const uncontrollableConsumers = snapshot?.consumers?.filter(c => !c.hasOptionalTier) ?? [];
    // Autarky color: continuous gradient red (0%) → amber (~50%) → dark green (100%).
    // HSL hue interpolation 0° (red) → 145° (dark green), lightness 45% for readability.
    const autarkyPct = Math.min(100, Math.max(0, snapshot?.autarkyPct ?? 0));
    const autarkyHue = (autarkyPct / 100) * 145;
    const autarkyColor = `hsl(${autarkyHue.toFixed(0)}, 75%, 45%)`;
    // Saldo = self-generated energy that stayed on site (PV minus grid feed-in)
    // minus what the site actually consumed. Positive = surplus, negative = deficit.
    // The headline shows the rolling 24 h balance in kWh; D/M/Y break it down.
    const saldo24h = Math.max(0, (snapshot?.pvGeneration24hKwh ?? 0) - (snapshot?.gridExport24hKwh ?? 0)) - (snapshot?.totalConsumption24hKwh ?? 0);
    const saldo30d = Math.max(0, (snapshot?.pvGeneration30dKwh ?? 0) - (snapshot?.gridExport30dKwh ?? 0)) - (snapshot?.totalConsumption30dKwh ?? 0);
    const saldo1y = Math.max(0, (snapshot?.pvGeneration1yKwh ?? 0) - (snapshot?.gridExport1yKwh ?? 0)) - (snapshot?.totalConsumption1yKwh ?? 0);
    const saldoPositive = saldo24h >= 0;
    const saldoColor = saldoPositive ? 'text-emerald-400' : 'text-rose-400';
    const autarkyWindows = [
        { key: t('ems_24h_short'), pct: snapshot?.autarky24hPct ?? 0 },
        { key: t('ems_30d_short'), pct: snapshot?.autarky30dPct ?? 0 },
        { key: t('ems_1y_short'), pct: snapshot?.autarky1yPct ?? 0 },
    ];
    const saldoWindows = [
        { key: t('ems_24h_short'), value: saldo24h },
        { key: t('ems_30d_short'), value: saldo30d },
        { key: t('ems_1y_short'), value: saldo1y },
    ];
    return (_jsxs("div", { class: "max-w-7xl mx-auto px-2 py-3 sm:px-4 sm:py-8 space-y-3 sm:space-y-8", children: [_jsxs("div", { class: "flex flex-col gap-3 bg-slate-900/60 backdrop-blur-md border border-slate-800/80 p-3 sm:p-5 rounded-2xl sm:rounded-3xl shadow-xl", "data-testid": "ems-header", children: [_jsxs("div", { class: "hidden sm:flex items-center gap-3 min-w-0", children: [_jsx("div", { class: "flex w-11 h-11 rounded-2xl bg-cyan-500/10 border border-cyan-500/30 items-center justify-center text-cyan-400 shadow-sm shrink-0", children: _jsx("i", { class: "fas fa-bolt text-lg" }) }), _jsx("h2", { class: "text-2xl font-black text-slate-100 tracking-tight", children: t('ems_page_title') }), _jsx("div", { class: "flex items-center gap-2 ml-auto shrink-0", children: _jsxs("button", { onClick: handleOpenSystemConfig, class: "inline-flex items-center gap-1.5 rounded-full border border-slate-700 bg-slate-800/80 px-3 py-1 text-[0.6rem] font-bold uppercase tracking-[0.16em] text-slate-300 hover:text-cyan-300 hover:border-cyan-500/50 transition-colors", title: t('ems_configure_system'), children: [_jsx("i", { class: "fas fa-sliders-h" }), _jsx("span", { children: t('ems_configure_system') })] }) })] }), _jsxs("div", { class: "grid grid-cols-1 sm:grid-cols-3 gap-3", children: [_jsxs("div", { class: "rounded-2xl border border-slate-800/90 bg-slate-950/40 p-3", children: [_jsxs("div", { class: "flex items-baseline justify-between gap-2", children: [_jsx("div", { class: "text-[0.6rem] uppercase tracking-[0.18em] text-slate-400 font-bold", children: t('ems_autarky') }), _jsxs("div", { class: "text-2xl font-black leading-none", style: { color: autarkyColor }, children: [(snapshot?.autarkyPct ?? 0).toFixed(0), "%"] })] }), _jsx("div", { class: "w-full bg-slate-800/80 rounded-full h-1.5 mt-2 overflow-hidden", children: _jsx("div", { class: "h-full rounded-full transition-all duration-500", style: { width: `${autarkyPct}%`, backgroundColor: autarkyColor } }) }), _jsx("div", { class: "grid grid-cols-3 gap-1.5 mt-2", children: autarkyWindows.map(w => {
                                            const pct = Math.min(100, Math.max(0, w.pct));
                                            const color = `hsl(${((pct / 100) * 145).toFixed(0)}, 75%, 45%)`;
                                            return (_jsxs("div", { class: "rounded-lg bg-slate-950/50 border border-slate-800/70 px-2 py-1.5 min-w-0", children: [_jsx("div", { class: "text-[0.6rem] uppercase tracking-wider text-slate-500 font-bold", children: w.key }), _jsxs("div", { class: "text-lg font-black font-mono leading-tight truncate", style: { color }, children: [w.pct.toFixed(0), "%"] })] }, w.key));
                                        }) })] }), _jsxs("div", { class: "rounded-2xl border border-slate-800/90 bg-slate-950/40 p-3", children: [_jsxs("div", { class: "flex items-baseline justify-between gap-2", children: [_jsx("div", { class: "text-[0.6rem] uppercase tracking-[0.18em] text-slate-400 font-bold", children: t('ems_saldo') }), _jsxs("div", { class: `text-2xl font-black leading-none font-mono ${saldoColor}`, children: [saldoPositive ? '+' : '−', formatKwh(Math.abs(saldo24h)), _jsx("span", { class: "text-xs font-bold text-slate-400 ml-1", children: "kWh" })] })] }), _jsx("div", { class: "text-[0.6rem] text-slate-500 mt-1 leading-snug", title: t('ems_saldo_hint'), children: t('ems_saldo_hint') }), _jsx("div", { class: "grid grid-cols-3 gap-1.5 mt-2", children: saldoWindows.map(w => (_jsxs("div", { class: "rounded-lg bg-slate-950/50 border border-slate-800/70 px-2 py-1.5 min-w-0", children: [_jsx("div", { class: "text-[0.6rem] uppercase tracking-wider text-slate-500 font-bold", children: w.key }), _jsxs("div", { class: `text-lg font-black font-mono leading-tight truncate ${w.value >= 0 ? 'text-emerald-400' : 'text-rose-400'}`, children: [w.value >= 0 ? '+' : '−', formatKwh(Math.abs(w.value))] })] }, w.key))) })] }), _jsxs("div", { class: "rounded-2xl border border-slate-800/90 bg-slate-950/40 p-3", children: [_jsxs("div", { class: "flex items-baseline justify-between gap-2", children: [_jsx("div", { class: "text-[0.6rem] uppercase tracking-[0.18em] text-slate-400 font-bold", children: t('ems_battery_soc') }), _jsxs("div", { class: "text-2xl font-black leading-none text-emerald-400", children: [(snapshot?.batterySocPct ?? 0).toFixed(0), "%"] })] }), _jsx("div", { class: "w-full bg-slate-800/80 rounded-full h-1.5 mt-2 overflow-hidden", children: _jsx("div", { class: `h-full rounded-full transition-all duration-500 ${(snapshot?.batterySocPct ?? 0) > 20 ? 'bg-emerald-400' : 'bg-amber-400'}`, style: { width: `${Math.min(100, Math.max(0, snapshot?.batterySocPct ?? 0))}%` } }) }), _jsxs("div", { class: "text-[0.6rem] text-slate-500 mt-2 truncate", children: [snapshot?.isBatteryCharging
                                                ? t('ems_battery_charging')
                                                : (snapshot?.batteryPowerKw ?? 0) > 0.3 ? t('ems_battery_discharging') : t('ems_battery_idle'), ' · ', Math.abs(snapshot?.batteryPowerKw ?? 0).toFixed(1), " kW"] })] })] })] }), _jsxs("div", { class: "grid grid-cols-1 lg:grid-cols-2 gap-6 items-start", children: [_jsxs("div", { class: "space-y-4", children: [_jsx("div", { class: "flex items-center justify-between px-1", children: _jsxs("h2", { class: "text-xs uppercase tracking-widest font-black text-slate-400 flex items-center gap-2", children: [_jsx("i", { class: "fas fa-layer-group text-emerald-400" }), t('ems_sources_title')] }) }), _jsxs("div", { class: "bg-slate-900/70 border border-slate-800/90 rounded-2xl p-5 shadow-lg relative overflow-hidden group hover:border-sky-500/30 transition-all", children: [_jsxs("div", { class: "flex items-center justify-between mb-3", children: [_jsxs("div", { class: "flex items-center gap-2.5", children: [_jsx("div", { class: "w-8 h-8 rounded-xl bg-sky-500/10 border border-sky-500/30 text-sky-400 flex items-center justify-center text-xs", children: _jsx("i", { class: "fas fa-network-wired" }) }), _jsxs("div", { children: [_jsx("h3", { class: "text-sm font-bold text-slate-200", children: t('ems_grid_connection') }), _jsxs("div", { onClick: () => handleOpenTelemetryDetails(snapshot?.sources?.gridMeterKey || 'ems_grid_power'), class: "text-[0.65rem] text-slate-400 font-mono hover:text-sky-400 cursor-pointer transition-colors flex items-center gap-1 group/key", title: `View telemetry details for ${snapshot?.sources?.gridMeterKey || 'ems_grid_power'}`, children: [_jsx("span", { children: snapshot?.sources?.gridMeterKey || 'ems_grid_power' }), _jsx("i", { class: "fas fa-chart-line text-[0.55rem] opacity-0 group-hover/key:opacity-100 transition-opacity text-sky-400" })] })] })] }), _jsx("div", { class: "text-right", children: _jsxs("span", { onClick: () => handleOpenTelemetryDetails('ems_grid_max_import'), class: "px-2 py-0.5 rounded-md bg-sky-500/10 text-sky-400 text-[0.65rem] font-bold border border-sky-500/20 hover:bg-sky-500/20 hover:border-sky-500/40 cursor-pointer transition-all", title: "View / edit telemetry details: ems_grid_max_import", children: ["Max ", snapshot?.gridMaxImportKw?.toFixed(1) ?? '8.0', " kW"] }) })] }), _jsxs("div", { class: "flex items-baseline justify-between mt-2", children: [_jsxs("div", { onClick: () => handleOpenTelemetryDetails(snapshot?.sources?.gridMeterKey || 'ems_grid_power'), class: "cursor-pointer group/val inline-flex items-baseline hover:opacity-85 transition-all", title: `View telemetry details for ${snapshot?.sources?.gridMeterKey || 'ems_grid_power'}`, children: [_jsx("span", { class: "text-2xl font-black text-slate-100 group-hover/val:text-sky-400 transition-colors", children: Math.abs(snapshot?.gridImportKw ?? 0).toFixed(1) }), _jsx("span", { class: "text-xs font-bold text-slate-400 ml-1", children: "kW" }), _jsx("i", { class: "fas fa-chart-area text-[0.65rem] text-slate-600 group-hover/val:text-sky-400 ml-1.5 opacity-0 group-hover/val:opacity-100 transition-all" })] }), _jsxs("span", { onClick: () => handleOpenTelemetryDetails((snapshot?.gridImportKw ?? 0) >= 0 ? 'ems_grid_import' : 'ems_grid_export'), class: `text-xs font-bold ${(snapshot?.gridImportKw ?? 0) >= 0 ? 'text-amber-400 hover:text-amber-300' : 'text-emerald-400 hover:text-emerald-300'} flex items-center gap-1.5 cursor-pointer transition-colors`, title: `View telemetry details for ${(snapshot?.gridImportKw ?? 0) >= 0 ? 'ems_grid_import' : 'ems_grid_export'}`, children: [(snapshot?.gridImportKw ?? 0) < 0 && _jsx("i", { class: "fas fa-arrow-left text-[0.65rem]" }), (snapshot?.gridImportKw ?? 0) >= 0 ? t('ems_grid_import') : t('ems_grid_export'), (snapshot?.gridImportKw ?? 0) >= 0 && _jsx("i", { class: "fas fa-arrow-right text-[0.65rem]" })] })] }), _jsx("div", { class: "w-full bg-slate-800/80 rounded-full h-1.5 mt-3 overflow-hidden", children: _jsx("div", { class: "h-full rounded-full transition-all duration-500 bg-sky-400", style: { width: `${Math.min(100, Math.max(0, ((snapshot?.gridImportKw ?? 0) / (snapshot?.gridMaxImportKw || 8.0)) * 100))}%` } }) }), _jsxs("div", { class: "mt-3 pt-3 border-t border-slate-800/80 space-y-1", children: [_jsx(RollingEnergyLine, { label: t('ems_export'), h24: snapshot?.gridExport24hKwh, d30: snapshot?.gridExport30dKwh, y1: snapshot?.gridExport1yKwh, accentClass: "text-emerald-400", onOpen: handleOpenTelemetryDetails, telemetryKey: "ems_grid_export_24h" }), _jsx(RollingEnergyLine, { label: t('ems_import'), h24: snapshot?.gridImport24hKwh, d30: snapshot?.gridImport30dKwh, y1: snapshot?.gridImport1yKwh, accentClass: "text-amber-400", onOpen: handleOpenTelemetryDetails, telemetryKey: "ems_grid_import_24h" })] })] }), _jsxs("div", { class: "bg-slate-900/70 border border-slate-800/90 rounded-2xl p-5 shadow-lg relative overflow-hidden group hover:border-amber-500/30 transition-all", children: [_jsx("div", { class: "flex items-center justify-between mb-3", children: _jsxs("div", { class: "flex items-center gap-2.5", children: [_jsx("div", { class: "w-8 h-8 rounded-xl bg-amber-500/10 border border-amber-500/30 text-amber-400 flex items-center justify-center text-xs", children: _jsx("i", { class: "fas fa-solar-panel" }) }), _jsxs("div", { children: [_jsx("h3", { class: "text-sm font-bold text-slate-200", children: t('ems_pv_generation') }), _jsxs("div", { onClick: () => handleOpenTelemetryDetails(snapshot?.sources?.pvMeterKey || 'ems_pv_power'), class: "text-[0.65rem] text-slate-400 font-mono hover:text-amber-400 cursor-pointer transition-colors flex items-center gap-1 group/key", title: `View telemetry details for ${snapshot?.sources?.pvMeterKey || 'ems_pv_power'}`, children: [_jsx("span", { children: snapshot?.sources?.pvMeterKey || 'ems_pv_power' }), _jsx("i", { class: "fas fa-chart-line text-[0.55rem] opacity-0 group-hover/key:opacity-100 transition-opacity text-amber-400" })] })] })] }) }), _jsxs("div", { class: "flex items-baseline justify-between mt-2", children: [_jsxs("div", { onClick: () => handleOpenTelemetryDetails(snapshot?.sources?.pvMeterKey || 'ems_pv_power'), class: "cursor-pointer group/val inline-flex items-baseline hover:opacity-85 transition-all", title: `View telemetry details for ${snapshot?.sources?.pvMeterKey || 'ems_pv_power'}`, children: [_jsx("span", { class: "text-2xl font-black text-amber-400 group-hover/val:text-amber-300 transition-colors", children: Math.abs(snapshot?.pvPowerKw ?? snapshot?.pvGenerationKw ?? 0).toFixed(1) }), _jsx("span", { class: "text-xs font-bold text-slate-400 ml-1", children: "kW" }), _jsx("i", { class: "fas fa-chart-area text-[0.65rem] text-amber-600 group-hover/val:text-amber-400 ml-1.5 opacity-0 group-hover/val:opacity-100 transition-all" })] }), _jsxs("span", { onClick: () => handleOpenTelemetryDetails(snapshot?.sources?.pvMeterKey || 'ems_pv_power'), class: "text-xs text-amber-400 font-medium flex items-center gap-1.5 cursor-pointer hover:text-amber-300 transition-colors", title: `View telemetry details for ${snapshot?.sources?.pvMeterKey || 'ems_pv_power'}`, children: [(snapshot?.pvPowerKw ?? 0) < -0.2 && _jsx("i", { class: "fas fa-arrow-left text-[0.65rem]" }), t('ems_generation'), (snapshot?.pvPowerKw ?? 0) >= -0.2 && _jsx("i", { class: "fas fa-arrow-right text-[0.65rem]" })] })] }), _jsx("div", { class: "mt-3 pt-3 border-t border-slate-800/80", children: _jsx(RollingEnergyLine, { label: t('ems_generation'), h24: snapshot?.pvGeneration24hKwh, d30: snapshot?.pvGeneration30dKwh, y1: snapshot?.pvGeneration1yKwh, accentClass: "text-amber-400", onOpen: handleOpenTelemetryDetails, telemetryKey: "ems_pv_generation_24h" }) })] }), snapshot?.sources?.hasBattery !== false && (_jsxs("div", { class: `bg-slate-900/70 border rounded-2xl p-5 shadow-lg relative overflow-hidden transition-all ${snapshot?.isBatteryCharging
                                    ? 'border-emerald-500/40 shadow-emerald-500/5'
                                    : 'border-slate-800/90 hover:border-emerald-500/30'}`, children: [_jsxs("div", { class: "flex items-center justify-between mb-3", children: [_jsxs("div", { class: "flex items-center gap-2.5", children: [_jsx("div", { class: `w-8 h-8 rounded-xl flex items-center justify-center text-xs border ${snapshot?.isBatteryCharging
                                                            ? 'bg-emerald-500/20 border-emerald-500/50 text-emerald-400 animate-pulse'
                                                            : 'bg-slate-800 border-slate-700 text-slate-400'}`, children: _jsx("i", { class: "fas fa-battery-three-quarters" }) }), _jsxs("div", { children: [_jsx("h3", { class: "text-sm font-bold text-slate-200", children: t('ems_battery_storage') }), _jsxs("div", { onClick: () => handleOpenTelemetryDetails(snapshot?.sources?.batteryPowerKey || 'ems_battery_power'), class: "text-[0.65rem] text-slate-400 font-mono hover:text-emerald-400 cursor-pointer transition-colors flex items-center gap-1 group/key", title: `View telemetry details for ${snapshot?.sources?.batteryPowerKey || 'ems_battery_power'}`, children: [_jsx("span", { children: snapshot?.sources?.batteryPowerKey || 'ems_battery_power' }), _jsx("i", { class: "fas fa-chart-line text-[0.55rem] opacity-0 group-hover/key:opacity-100 transition-opacity text-emerald-400" })] })] })] }), _jsxs("span", { onClick: () => handleOpenTelemetryDetails(snapshot?.isBatteryCharging ? 'ems_battery_charge' : (snapshot?.sources?.batteryPowerKey || 'ems_battery_power')), class: `px-2 py-0.5 rounded-md text-[0.65rem] font-bold border flex items-center gap-1 cursor-pointer transition-all ${snapshot?.isBatteryCharging
                                                    ? 'bg-emerald-500/15 text-emerald-400 border-emerald-500/30 hover:bg-emerald-500/25 animate-pulse'
                                                    : (snapshot?.batteryPowerKw ?? 0) > 0.3
                                                        ? 'bg-amber-500/15 text-amber-400 border-amber-500/30 hover:bg-amber-500/25'
                                                        : 'bg-slate-800 text-slate-400 border-slate-700 hover:bg-slate-700'}`, title: `View telemetry details for ${snapshot?.isBatteryCharging ? 'ems_battery_charge' : (snapshot?.sources?.batteryPowerKey || 'ems_battery_power')}`, children: [snapshot?.isBatteryCharging && _jsx("i", { class: "fas fa-arrow-left text-[0.55rem]" }), snapshot?.isBatteryCharging ? t('ems_battery_charging') : (snapshot?.batteryPowerKw ?? 0) > 0.3 ? t('ems_battery_discharging') : t('ems_battery_idle'), !snapshot?.isBatteryCharging && (snapshot?.batteryPowerKw ?? 0) > 0.3 && _jsx("i", { class: "fas fa-arrow-right text-[0.55rem]" })] })] }), _jsxs("div", { class: "grid grid-cols-3 gap-3 mt-2", children: [_jsxs("div", { onClick: () => handleOpenTelemetryDetails(snapshot?.sources?.batteryPowerKey || 'ems_battery_power'), class: "cursor-pointer group/battp hover:opacity-85 transition-all", title: `View telemetry details for ${snapshot?.sources?.batteryPowerKey || 'ems_battery_power'}`, children: [_jsx("div", { class: "text-[0.65rem] uppercase tracking-wider text-slate-400 font-bold mb-0.5", children: "Power Flow" }), _jsx("span", { class: `text-xl font-black ${snapshot?.isBatteryCharging ? 'text-emerald-400 group-hover/battp:text-emerald-300' : 'text-slate-100 group-hover/battp:text-sky-400'} transition-colors`, children: Math.abs(snapshot?.batteryPowerKw ?? 0).toFixed(1) }), _jsx("span", { class: "text-xs font-bold text-slate-400 ml-1", children: "kW" })] }), _jsxs("div", { onClick: () => handleOpenTelemetryDetails(snapshot?.sources?.batteryPowerKey || 'ems_battery_power'), class: "cursor-pointer group/battm hover:opacity-85 transition-all", title: `View telemetry details for ${snapshot?.sources?.batteryPowerKey || 'ems_battery_power'}`, children: [_jsx("div", { class: "text-[0.65rem] uppercase tracking-wider text-slate-400 font-bold mb-0.5", children: "Max Power" }), _jsx("span", { class: "text-xl font-black text-slate-100 group-hover/battm:text-sky-400 transition-colors", children: (snapshot?.batteryMaxPowerKw ?? snapshot?.sources?.batteryMaxPowerKw ?? 5.0).toFixed(1) }), _jsx("span", { class: "text-xs font-bold text-slate-400 ml-1", children: "kW" })] }), _jsxs("div", { onClick: () => handleOpenTelemetryDetails(snapshot?.sources?.batterySocKey || 'ems_battery_soc'), class: "cursor-pointer group/soc hover:opacity-85 transition-all", title: `View telemetry details for ${snapshot?.sources?.batterySocKey || 'ems_battery_soc'}`, children: [_jsx("div", { class: "text-[0.65rem] uppercase tracking-wider text-slate-400 font-bold mb-0.5", children: t('ems_battery_soc') }), _jsxs("span", { class: "text-xl font-black text-slate-100 group-hover/soc:text-emerald-400 transition-colors", children: [(snapshot?.batterySocPct ?? 0).toFixed(0), "%"] })] })] }), _jsx("div", { onClick: () => handleOpenTelemetryDetails(snapshot?.sources?.batterySocKey || 'ems_battery_soc'), class: "w-full bg-slate-800/80 rounded-full h-2 mt-4 overflow-hidden cursor-pointer hover:opacity-85 transition-all", title: `View telemetry details for ${snapshot?.sources?.batterySocKey || 'ems_battery_soc'}`, children: _jsx("div", { class: `h-full rounded-full transition-all duration-500 ${(snapshot?.batterySocPct ?? 0) > 20 ? 'bg-emerald-400' : 'bg-amber-400'}`, style: { width: `${Math.min(100, Math.max(0, snapshot?.batterySocPct ?? 0))}%` } }) }), _jsxs("div", { class: "mt-3 pt-3 border-t border-slate-800/80 space-y-1", children: [_jsx(RollingEnergyLine, { label: t('ems_charged'), h24: snapshot?.batteryCharged24hKwh, d30: snapshot?.batteryCharged30dKwh, y1: snapshot?.batteryCharged1yKwh, accentClass: "text-emerald-400", onOpen: handleOpenTelemetryDetails, telemetryKey: "ems_battery_charged_24h" }), _jsx(RollingEnergyLine, { label: t('ems_discharged'), h24: snapshot?.batteryDischarged24hKwh, d30: snapshot?.batteryDischarged30dKwh, y1: snapshot?.batteryDischarged1yKwh, accentClass: "text-amber-400", onOpen: handleOpenTelemetryDetails, telemetryKey: "ems_battery_discharged_24h" })] })] }))] }), _jsxs("div", { class: "space-y-4", children: [_jsxs("div", { class: "flex items-center justify-between px-1", children: [_jsxs("h2", { class: "text-xs uppercase tracking-widest font-black text-slate-400 flex items-center gap-2", children: [_jsx("i", { class: "fas fa-charging-station text-cyan-400" }), t('ems_consumers_title')] }), _jsx("div", { class: "flex items-center gap-2", children: _jsxs("button", { onClick: handleOpenAddConsumer, class: "text-xs font-bold text-cyan-400 hover:text-cyan-300 transition-colors flex items-center gap-1", children: [_jsx("i", { class: "fas fa-plus" }), t('ems_add_consumer')] }) })] }), controllableConsumers.map((consumer) => {
                                const optQuota = consumer.maxOptionalKw ?? 8.0;
                                const isBoosted = consumer.allocatedPowerKw > (consumer.basePowerKw ?? 0) + optQuota + 0.1;
                                const isIdle = !consumer.isActivelyDemanding;
                                const hasUnused = (consumer.unusedPowerKw ?? 0) > 0.1;
                                const iconCfg = getConsumerIconConfig(consumer.name, consumer.id);
                                const cleanId = (consumer.id || '').toLowerCase().replace(/-/g, '_');
                                const actualKey = consumer.actualPowerKey || (cleanId ? `ems_${cleanId}_actual_power` : '');
                                const allocatedKey = consumer.forcePowerKey || (cleanId ? `ems_${cleanId}_allocated_power` : '');
                                const unusedKey = cleanId ? `ems_${cleanId}_unused_power` : '';
                                const energyKey = cleanId ? `ems_${cleanId}_energy_24h` : '';
                                const subtitleKey = consumer.actualPowerKey || consumer.forcePowerKey || consumer.id;
                                return (_jsxs("div", { class: `bg-slate-900/70 border rounded-2xl p-5 shadow-lg relative overflow-hidden transition-all ${isBoosted
                                        ? 'border-cyan-500/40 shadow-cyan-500/5'
                                        : isIdle
                                            ? 'border-amber-500/30 bg-amber-950/5'
                                            : 'border-slate-800/90 hover:border-cyan-500/30'}`, children: [_jsxs("div", { class: "flex items-center justify-between mb-3", children: [_jsxs("div", { class: "flex items-center gap-2.5", children: [_jsx("div", { class: `w-8 h-8 rounded-xl flex items-center justify-center text-xs border ${isBoosted
                                                                ? 'bg-cyan-500/20 border-cyan-500/50 text-cyan-300 animate-pulse'
                                                                : isIdle
                                                                    ? 'bg-amber-500/10 border-amber-500/30 text-amber-400'
                                                                    : `${iconCfg.bg} ${iconCfg.border} ${iconCfg.text}`}`, children: _jsx("i", { class: `fas ${iconCfg.icon}` }) }), _jsxs("div", { children: [_jsx("div", { class: "flex items-center gap-2", children: _jsx("h3", { class: "text-sm font-bold text-slate-200", children: consumer.name || consumer.id }) }), _jsxs("div", { onClick: () => handleOpenTelemetryDetails(actualKey || subtitleKey), class: "text-[0.65rem] text-slate-400 font-mono hover:text-cyan-400 cursor-pointer transition-colors flex items-center gap-1 group/ckey mt-0.5", title: `View telemetry details for ${actualKey || subtitleKey}`, children: [_jsx("span", { children: subtitleKey }), _jsx("i", { class: "fas fa-chart-line text-[0.55rem] opacity-0 group-hover/ckey:opacity-100 transition-opacity text-cyan-400" })] })] })] }), _jsxs("div", { class: "flex items-center gap-2", children: [_jsxs("span", { class: "px-1.5 py-0.5 rounded text-[0.65rem] font-bold bg-slate-800 text-slate-300 border border-slate-700", children: ["P", consumer.priority] }), _jsx("span", { class: `text-[0.65rem] font-bold px-2 py-0.5 rounded border ${isBoosted
                                                                ? 'bg-cyan-500/15 text-cyan-400 border-cyan-500/30'
                                                                : isIdle
                                                                    ? 'bg-amber-500/10 text-amber-400 border-amber-500/30'
                                                                    : 'bg-emerald-500/15 text-emerald-400 border-emerald-500/30'}`, children: consumer.status || (isIdle ? 'Idle' : 'Active') }), _jsx("button", { onClick: () => handleOpenEditConsumer(consumer), class: "w-7 h-7 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-400 hover:text-cyan-400 flex items-center justify-center text-xs transition-all", title: t('ems_edit_consumer'), children: _jsx("i", { class: "fas fa-pencil-alt" }) }), _jsx("button", { onClick: () => handleDeleteConsumer(consumer.id, consumer.name), class: "w-7 h-7 rounded-lg bg-slate-800 hover:bg-rose-900/40 text-slate-400 hover:text-rose-400 flex items-center justify-center text-xs transition-all", title: t('ems_delete_consumer'), children: _jsx("i", { class: "fas fa-trash" }) })] })] }), _jsxs("div", { class: "grid grid-cols-2 gap-4 mt-2", children: [_jsxs("div", { children: [_jsx("div", { class: "text-[0.65rem] uppercase tracking-wider text-slate-400 font-bold mb-0.5", children: t('ems_allocated_power') }), _jsxs("div", { onClick: () => handleOpenTelemetryDetails(allocatedKey), class: "cursor-pointer group/alloc inline-flex items-baseline hover:opacity-85 transition-all", title: `View telemetry details for ${allocatedKey}`, children: [_jsx("span", { class: `text-2xl font-black ${isBoosted ? 'text-cyan-400 group-hover/alloc:text-cyan-300' : 'text-slate-100 group-hover/alloc:text-sky-400'} transition-colors`, children: consumer.allocatedPowerKw.toFixed(1) }), _jsx("span", { class: "text-xs font-bold text-slate-400 ml-1", children: "kW" })] }), hasUnused && (_jsxs("div", { onClick: () => handleOpenTelemetryDetails(unusedKey), class: "text-[0.65rem] font-bold text-amber-400 hover:text-amber-300 cursor-pointer transition-colors flex items-center gap-1 mt-1", title: `View telemetry details for ${unusedKey}`, children: [_jsx("i", { class: "fas fa-share text-[0.55rem]" }), consumer.unusedPowerKw.toFixed(1), " kW shared"] }))] }), _jsxs("div", { children: [_jsx("div", { class: "text-[0.65rem] uppercase tracking-wider text-slate-400 font-bold mb-0.5", children: t('ems_actual_power') }), _jsxs("div", { onClick: () => handleOpenTelemetryDetails(actualKey), class: "cursor-pointer group/act inline-flex items-baseline hover:opacity-85 transition-all", title: `View telemetry details for ${actualKey}`, children: [_jsx("span", { class: "text-2xl font-black text-slate-200 group-hover/act:text-cyan-400 transition-colors", children: consumer.actualPowerKw.toFixed(1) }), _jsx("span", { class: "text-xs font-bold text-slate-400 ml-1", children: "kW" }), _jsx("i", { class: "fas fa-chart-area text-[0.65rem] text-slate-600 group-hover/act:text-cyan-400 ml-1.5 opacity-0 group-hover/act:opacity-100 transition-all" })] })] })] }), _jsxs("div", { class: "mt-3 pt-3 border-t border-slate-800/80 space-y-1", children: [_jsx(RollingEnergyLine, { label: t('ems_consumed'), h24: consumer.energy24hKwh, d30: consumer.energy30dKwh, y1: consumer.energy1yKwh, accentClass: "text-cyan-400", onOpen: handleOpenTelemetryDetails, telemetryKey: energyKey }), _jsxs("div", { class: "text-[0.68rem] text-slate-400 font-mono whitespace-nowrap text-right", children: ["Base: ", (consumer.basePowerKw ?? 0).toFixed(1), " kW \u00B7 Opt: ", (consumer.maxOptionalKw ?? 8.0).toFixed(1), " kW"] })] })] }, consumer.id));
                            }), _jsxs("div", { class: "bg-slate-900/70 border border-slate-800/90 rounded-2xl p-5 shadow-lg relative overflow-hidden space-y-3", children: [_jsx("div", { class: "flex items-center justify-between", children: _jsxs("div", { class: "flex items-center gap-2.5", children: [_jsx("div", { class: "w-8 h-8 rounded-xl bg-purple-500/10 border border-purple-500/30 text-purple-400 flex items-center justify-center text-xs", children: _jsx("i", { class: "fas fa-building" }) }), _jsxs("div", { children: [_jsx("h3", { class: "text-sm font-bold text-slate-200", children: t('ems_uncontrollable_loads') }), _jsx("div", { onClick: () => handleOpenTelemetryDetails('ems_uncontrollable_load'), class: "text-[0.65rem] text-slate-400 font-mono hover:text-purple-300 cursor-pointer transition-colors", title: "View telemetry details: ems_uncontrollable_load", children: uncontrollableConsumers.length > 0
                                                                ? 'Building Infrastructure & Sub-meters'
                                                                : t('ems_calculated_residual') })] })] }) }), _jsxs("div", { class: "mt-2", children: [_jsx("div", { class: "text-[0.65rem] uppercase tracking-wider text-slate-400 font-bold mb-0.5", children: t('ems_actual_power') }), _jsxs("div", { onClick: () => handleOpenTelemetryDetails('ems_uncontrollable_load'), class: "cursor-pointer group/uc inline-flex items-baseline hover:opacity-85 transition-all", title: "View telemetry details: ems_uncontrollable_load", children: [_jsx("span", { class: "text-2xl font-black text-slate-200 group-hover/uc:text-purple-300 transition-colors", children: (snapshot?.uncontrollableLoadKw ?? 0).toFixed(1) }), _jsx("span", { class: "text-xs font-bold text-slate-400 ml-1", children: "kW" }), _jsx("i", { class: "fas fa-chart-area text-[0.65rem] text-slate-600 group-hover/uc:text-purple-400 ml-1.5 opacity-0 group-hover/uc:opacity-100 transition-all" })] })] }), _jsx("div", { class: "mt-3 pt-3 border-t border-slate-800/80 space-y-1", children: _jsx(RollingEnergyLine, { label: t('ems_consumed'), h24: snapshot?.uncontrollable24hKwh, d30: snapshot?.uncontrollable30dKwh, y1: snapshot?.uncontrollable1yKwh, accentClass: "text-purple-400", onOpen: handleOpenTelemetryDetails, telemetryKey: "ems_uncontrollable_24h" }) }), uncontrollableConsumers.length === 0 ? (_jsxs("div", { onClick: () => handleOpenTelemetryDetails('ems_uncontrollable_load'), class: "pt-2 border-t border-slate-800/60 text-[0.68rem] text-slate-400 flex items-center justify-between font-mono bg-purple-500/5 rounded-lg px-2.5 py-1.5 border border-purple-500/10 cursor-pointer hover:bg-purple-500/10 hover:border-purple-500/25 transition-all", title: "View telemetry details: ems_uncontrollable_load", children: [_jsxs("span", { class: "text-slate-400 flex items-center gap-1.5", children: [_jsx("i", { class: "fas fa-calculator text-purple-400 text-[0.6rem]" }), _jsxs("span", { children: ["(Grid + PV", snapshot?.sources?.hasBattery !== false ? ' + Battery' : '', ") \u2212 Controllable:"] })] }), _jsxs("span", { class: "text-purple-300 font-bold", children: [(snapshot?.uncontrollableLoadKw ?? 0).toFixed(1), " kW"] })] })) : (_jsx("div", { class: "pt-2 border-t border-slate-800/60 space-y-1", children: uncontrollableConsumers.map(uc => {
                                            const ucCleanId = (uc.id || '').toLowerCase().replace(/-/g, '_');
                                            const ucActualKey = uc.actualPowerKey || (ucCleanId ? `ems_${ucCleanId}_actual_power` : '');
                                            const ucEnergyKey = ucCleanId ? `ems_${ucCleanId}_energy_24h` : '';
                                            return (_jsxs("div", { class: "flex items-center justify-between text-xs text-slate-400 group hover:text-slate-200 transition-colors py-0.5", children: [_jsxs("div", { class: "flex items-center gap-2", children: [_jsx("span", { children: uc.name }), _jsxs("span", { class: "text-[0.65rem] font-mono text-slate-500", children: ["P", uc.priority] })] }), _jsxs("div", { class: "flex items-center gap-3 font-mono", children: [_jsxs("span", { onClick: () => handleOpenTelemetryDetails(ucActualKey), class: "cursor-pointer hover:text-purple-300 transition-colors", title: `View telemetry details: ${ucActualKey}`, children: [uc.actualPowerKw.toFixed(1), " kW"] }), _jsxs("div", { onClick: () => handleOpenTelemetryDetails(ucEnergyKey), class: "flex items-center gap-1.5 text-purple-300 font-semibold cursor-pointer hover:text-purple-200 transition-colors whitespace-nowrap", title: `View telemetry details: ${ucEnergyKey}`, children: [_jsxs("span", { class: "text-slate-500", children: [t('ems_24h_short'), " ", formatKwh(uc.energy24hKwh)] }), _jsx("span", { class: "text-slate-600", children: "\u00B7" }), _jsxs("span", { class: "text-slate-500", children: [t('ems_30d_short'), " ", formatKwh(uc.energy30dKwh)] }), _jsx("span", { class: "text-slate-600", children: "\u00B7" }), _jsxs("span", { class: "text-slate-500", children: [t('ems_1y_short'), " ", formatKwh(uc.energy1yKwh)] }), _jsx("span", { class: "text-slate-500", children: "kWh" })] }), _jsxs("div", { class: "flex items-center gap-1", children: [_jsx("button", { onClick: () => handleOpenEditConsumer(uc), class: "w-6 h-6 rounded-md bg-slate-800 hover:bg-slate-700 text-slate-400 hover:text-cyan-400 flex items-center justify-center text-[0.65rem] transition-all", title: t('ems_edit_consumer'), children: _jsx("i", { class: "fas fa-pencil-alt" }) }), _jsx("button", { onClick: () => handleDeleteConsumer(uc.id, uc.name), class: "w-6 h-6 rounded-md bg-slate-800 hover:bg-rose-900/40 text-slate-400 hover:text-rose-400 flex items-center justify-center text-[0.65rem] transition-all", title: t('ems_delete_consumer'), children: _jsx("i", { class: "fas fa-trash" }) })] })] })] }, uc.id));
                                        }) }))] })] })] }), _jsxs("div", { class: "bg-slate-900/60 backdrop-blur-md border border-slate-800/80 rounded-3xl p-6 shadow-xl space-y-4", children: [_jsx("div", { class: "flex items-center justify-between", children: _jsxs("h2", { class: "text-sm uppercase tracking-widest font-black text-slate-300 flex items-center gap-2", children: [_jsx("i", { class: "fas fa-history text-cyan-400" }), t('ems_intervention_logs')] }) }), (!snapshot?.logs || snapshot.logs.length === 0) ? (_jsx("div", { class: "py-8 text-center text-slate-500 text-xs", children: t('ems_no_intervention') })) : (_jsx("div", { class: "overflow-x-auto", children: _jsxs("table", { class: "w-full text-left text-xs text-slate-300", children: [_jsx("thead", { class: "text-[0.65rem] uppercase tracking-wider text-slate-500 border-b border-slate-800", children: _jsxs("tr", { children: [_jsx("th", { class: "py-2.5 px-3", children: "Timestamp" }), _jsx("th", { class: "py-2.5 px-3", children: "Decision / Event" }), _jsx("th", { class: "py-2.5 px-3 text-right", children: "State" })] }) }), _jsx("tbody", { class: "divide-y divide-slate-800/60 font-mono text-[0.7rem]", children: snapshot.logs.slice().reverse().map((log, idx) => (_jsxs("tr", { class: "hover:bg-slate-800/30 transition-colors", children: [_jsx("td", { class: "py-2.5 px-3 text-slate-400 whitespace-nowrap", children: log.timestamp }), _jsx("td", { class: "py-2.5 px-3 font-sans text-slate-200", children: log.message }), _jsx("td", { class: "py-2.5 px-3 text-right whitespace-nowrap", children: _jsx("span", { class: `px-2 py-0.5 rounded text-[0.65rem] font-bold ${log.state.includes('Surplus')
                                                        ? 'bg-cyan-500/10 text-cyan-400 border border-cyan-500/30'
                                                        : 'bg-slate-800 text-slate-400 border border-slate-700'}`, children: log.state }) })] }, idx))) })] }) }))] }), showConfigModal && (_jsx("div", { class: "fixed inset-0 z-50 bg-slate-950/80 backdrop-blur-sm flex items-center justify-center p-4", children: _jsxs("div", { class: "bg-slate-900 border border-slate-800 rounded-3xl p-6 max-w-lg w-full shadow-2xl space-y-5", children: [_jsxs("div", { class: "flex items-center justify-between border-b border-slate-800 pb-4", children: [_jsxs("h3", { class: "text-base font-bold text-slate-100 flex items-center gap-2", children: [_jsx("i", { class: "fas fa-sliders-h text-cyan-400" }), _jsx("span", { children: t('ems_configure_system') })] }), _jsx("button", { onClick: () => setShowConfigModal(false), class: "w-8 h-8 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-400 flex items-center justify-center text-xs", children: _jsx("i", { class: "fas fa-times" }) })] }), _jsxs("form", { onSubmit: handleSaveSystemConfig, class: "space-y-4 text-xs", children: [_jsxs("div", { children: [_jsxs("label", { class: "block font-bold text-slate-300 mb-1", children: [t('ems_grid_limit'), " (kW)"] }), _jsx("input", { type: "number", step: "0.5", min: "1", max: "500", value: gridMaxKw, onInput: (e) => setGridMaxKw(parseFloat(e.target.value) || 8.0), class: "w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-100 font-bold focus:border-cyan-500 outline-none" }), _jsx("span", { class: "text-[0.65rem] text-slate-500 mt-1 block", children: t('ems_grid_limit_hint') })] }), _jsxs("div", { class: "bg-slate-800/60 border border-slate-800 rounded-2xl p-3.5 space-y-3", children: [_jsxs("div", { class: "flex items-center justify-between", children: [_jsxs("div", { class: "flex items-center gap-2", children: [_jsx("input", { type: "checkbox", id: "hasBattery", checked: hasBattery, onChange: (e) => setHasBattery(e.target.checked), class: "w-4 h-4 rounded accent-cyan-500 cursor-pointer" }), _jsx("label", { for: "hasBattery", class: "font-bold text-slate-200 cursor-pointer", children: t('ems_has_battery') })] }), _jsx("span", { class: "text-[0.65rem] text-slate-500", children: t('ems_has_battery_desc') })] }), hasBattery && (_jsxs("div", { class: "grid grid-cols-2 gap-3 pt-2 border-t border-slate-800", children: [_jsxs("div", { children: [_jsx("label", { class: "block font-bold text-slate-300 mb-1", children: t('ems_battery_max_power') }), _jsx("input", { type: "number", step: "0.5", min: "1", max: "50", value: battMaxKw, onInput: (e) => setBattMaxKw(parseFloat(e.target.value) || 5.0), class: "w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-100 font-bold focus:border-cyan-500 outline-none" }), _jsx("span", { class: "text-[0.65rem] text-slate-500 mt-1 block", children: t('ems_battery_max_hint') })] }), _jsxs("div", { children: [_jsxs("label", { class: "block font-bold text-slate-300 mb-1", children: [t('ems_reserve_headroom'), " (kW)"] }), _jsx("input", { type: "number", step: "0.5", min: "0", max: "10", value: battReserveKw, onInput: (e) => setBattReserveKw(parseFloat(e.target.value) || 1.0), class: "w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-100 font-bold focus:border-cyan-500 outline-none" }), _jsx("span", { class: "text-[0.65rem] text-slate-500 mt-1 block", children: t('ems_reserve_hint') })] })] }))] }), _jsxs("div", { class: "space-y-3 pt-2 border-t border-slate-800", children: [_jsx("div", { class: "text-[0.7rem] uppercase tracking-wider text-slate-400 font-bold", children: t('ems_key_mappings') }), _jsxs("div", { children: [_jsx("label", { class: "block text-slate-400 mb-1", children: t('ems_grid_power_key') }), _jsx("input", { type: "text", value: gridKey, onInput: (e) => setGridKey(e.target.value), class: "w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-200 font-mono focus:border-cyan-500 outline-none" }), _jsxs("div", { class: "flex items-center justify-between mt-1.5", children: [_jsx("span", { class: "text-[0.65rem] text-slate-500 font-mono", children: t('ems_sign_rule') }), _jsxs("label", { class: "flex items-center gap-1.5 cursor-pointer text-[0.65rem] text-slate-400", children: [_jsx("input", { type: "checkbox", checked: invertGridSign, onChange: (e) => setInvertGridSign(e.target.checked), class: "w-3.5 h-3.5 rounded accent-cyan-500 cursor-pointer" }), t('ems_invert_sign')] })] })] }), _jsxs("div", { children: [_jsx("label", { class: "block text-slate-400 mb-1", children: t('ems_pv_power_key') }), _jsx("input", { type: "text", value: pvKey, onInput: (e) => setPvKey(e.target.value), class: "w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-200 font-mono focus:border-cyan-500 outline-none" }), _jsxs("div", { class: "flex items-center justify-between mt-1.5", children: [_jsx("span", { class: "text-[0.65rem] text-slate-500 font-mono", children: t('ems_sign_rule') }), _jsxs("label", { class: "flex items-center gap-1.5 cursor-pointer text-[0.65rem] text-slate-400", children: [_jsx("input", { type: "checkbox", checked: invertPvSign, onChange: (e) => setInvertPvSign(e.target.checked), class: "w-3.5 h-3.5 rounded accent-cyan-500 cursor-pointer" }), t('ems_invert_sign')] })] })] }), hasBattery && (_jsxs(_Fragment, { children: [_jsxs("div", { children: [_jsx("label", { class: "block text-slate-400 mb-1", children: t('ems_battery_power_key') }), _jsx("input", { type: "text", value: battPowerKey, onInput: (e) => setBattPowerKey(e.target.value), class: "w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-200 font-mono focus:border-cyan-500 outline-none" }), _jsxs("div", { class: "flex items-center justify-between mt-1.5", children: [_jsx("span", { class: "text-[0.65rem] text-slate-500 font-mono", children: t('ems_sign_rule') }), _jsxs("label", { class: "flex items-center gap-1.5 cursor-pointer text-[0.65rem] text-slate-400", children: [_jsx("input", { type: "checkbox", checked: invertBattSign, onChange: (e) => setInvertBattSign(e.target.checked), class: "w-3.5 h-3.5 rounded accent-cyan-500 cursor-pointer" }), t('ems_invert_sign')] })] })] }), _jsxs("div", { children: [_jsx("label", { class: "block text-slate-400 mb-1", children: t('ems_battery_soc_key') }), _jsx("input", { type: "text", value: battSocKey, onInput: (e) => setBattSocKey(e.target.value), class: "w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-200 font-mono focus:border-cyan-500 outline-none" })] })] }))] }), _jsxs("div", { class: "flex items-center justify-end gap-3 pt-4 border-t border-slate-800", children: [_jsx("button", { type: "button", onClick: () => setShowConfigModal(false), class: "px-4 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-300 font-bold transition-all", children: t('ems_cancel') }), _jsxs("button", { type: "submit", disabled: savingConfig, class: "px-5 py-2 rounded-xl bg-cyan-500 hover:bg-cyan-400 text-slate-950 font-bold transition-all shadow-md flex items-center gap-2", children: [savingConfig && _jsx("i", { class: "fas fa-spinner fa-spin" }), t('ems_save_changes')] })] })] })] }) })), showConsumerModal && editingConsumer && (_jsx("div", { class: "fixed inset-0 z-50 bg-slate-950/80 backdrop-blur-sm flex items-center justify-center p-4", children: _jsxs("div", { class: "bg-slate-900 border border-slate-800 rounded-3xl p-6 max-w-lg w-full shadow-2xl space-y-5", children: [_jsxs("div", { class: "flex items-center justify-between border-b border-slate-800 pb-4", children: [_jsxs("h3", { class: "text-base font-bold text-slate-100 flex items-center gap-2", children: [_jsx("i", { class: `fas ${editingConsumer.id ? 'fa-edit' : 'fa-plus-circle'} text-cyan-400` }), _jsx("span", { children: editingConsumer.id ? t('ems_edit_consumer') : t('ems_add_consumer') }), editingConsumer.name && (_jsxs("span", { class: "text-xs text-slate-400 font-mono", children: ["(", editingConsumer.name, ")"] }))] }), _jsx("button", { onClick: () => setShowConsumerModal(false), class: "w-8 h-8 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-400 flex items-center justify-center text-xs", children: _jsx("i", { class: "fas fa-times" }) })] }), _jsxs("form", { onSubmit: handleSaveConsumer, class: "space-y-4 text-xs", children: [_jsxs("div", { class: "grid grid-cols-2 gap-4", children: [_jsxs("div", { children: [_jsx("label", { class: "block font-bold text-slate-300 mb-1", children: t('ems_consumer_name') }), _jsx("input", { type: "text", required: true, value: editingConsumer.name || '', onInput: (e) => setEditingConsumer({ ...editingConsumer, name: e.target.value }), placeholder: t('ems_consumer_name_placeholder'), class: "w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-100 font-bold focus:border-cyan-500 outline-none" })] }), _jsxs("div", { children: [_jsx("label", { class: "block font-bold text-slate-300 mb-1", children: t('ems_priority') }), _jsx("input", { type: "number", min: "1", max: "10", value: editingConsumer.priority ?? 1, onInput: (e) => setEditingConsumer({ ...editingConsumer, priority: parseInt(e.target.value) || 1 }), class: "w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-100 font-bold focus:border-cyan-500 outline-none" }), _jsx("span", { class: "text-[0.65rem] text-slate-500 mt-0.5 block", children: t('ems_priority_hint') })] })] }), _jsxs("div", { class: "bg-slate-800/60 border border-slate-800 rounded-2xl p-3.5 space-y-2", children: [_jsxs("div", { class: "flex items-center justify-between", children: [_jsx("label", { class: "font-bold text-slate-200", children: t('ems_base_tier') }), _jsx("span", { class: "text-[0.65rem] text-purple-400 font-mono", children: t('ems_tier1_badge') })] }), _jsxs("div", { class: "flex items-center gap-3", children: [_jsx("input", { type: "number", step: "0.1", min: "0", value: editingConsumer.basePowerKw ?? 0.0, onInput: (e) => setEditingConsumer({ ...editingConsumer, basePowerKw: parseFloat(e.target.value) || 0.0 }), class: "w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-100 font-bold focus:border-cyan-500 outline-none" }), _jsx("span", { class: "text-xs font-bold text-slate-400", children: "kW" })] }), _jsx("span", { class: "text-[0.65rem] text-slate-500 block", children: t('ems_base_tier_hint') })] }), _jsxs("div", { class: "bg-slate-800/60 border border-slate-800 rounded-2xl p-3.5 space-y-3", children: [_jsxs("div", { class: "flex items-center justify-between", children: [_jsxs("div", { class: "flex items-center gap-2", children: [_jsx("input", { type: "checkbox", id: "hasOptionalTier", checked: editingConsumer.hasOptionalTier ?? true, onChange: (e) => setEditingConsumer({ ...editingConsumer, hasOptionalTier: e.target.checked }), class: "w-4 h-4 rounded accent-cyan-500 cursor-pointer" }), _jsx("label", { for: "hasOptionalTier", class: "font-bold text-slate-200 cursor-pointer", children: t('ems_optional_tier') })] }), _jsx("span", { class: "text-[0.65rem] text-cyan-400 font-mono", children: t('ems_tier2_badge') })] }), editingConsumer.hasOptionalTier && (_jsxs("div", { class: "space-y-3 pt-2 border-t border-slate-800", children: [_jsxs("div", { class: "grid grid-cols-3 gap-3", children: [_jsxs("div", { children: [_jsxs("label", { class: "block font-bold text-slate-300 mb-1", children: [t('ems_max_power'), " (kW)"] }), _jsx("input", { type: "number", step: "0.5", value: editingConsumer.maxOptionalKw ?? 8.0, onInput: (e) => setEditingConsumer({ ...editingConsumer, maxOptionalKw: parseFloat(e.target.value) || 8.0 }), class: "w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-100 font-bold focus:border-cyan-500 outline-none" }), _jsx("span", { class: "text-[0.6rem] text-slate-500 mt-0.5 block", children: t('ems_max_optional_hint') })] }), _jsxs("div", { children: [_jsxs("label", { class: "block font-bold text-slate-300 mb-1", children: [t('ems_min_power'), " (kW)"] }), _jsx("input", { type: "number", step: "0.1", value: editingConsumer.minOptionalKw ?? 0.0, onInput: (e) => setEditingConsumer({ ...editingConsumer, minOptionalKw: parseFloat(e.target.value) || 0.0 }), class: "w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-100 font-bold focus:border-cyan-500 outline-none" }), _jsx("span", { class: "text-[0.6rem] text-slate-500 mt-0.5 block", children: t('ems_min_optional_hint') })] }), _jsxs("div", { children: [_jsx("label", { class: "block font-bold text-slate-300 mb-1", children: t('ems_standby_power') }), _jsx("input", { type: "number", step: "0.1", value: editingConsumer.standbyOptionalKw ?? 0.0, onInput: (e) => setEditingConsumer({ ...editingConsumer, standbyOptionalKw: parseFloat(e.target.value) || 0.0 }), class: "w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-100 font-bold focus:border-cyan-500 outline-none" }), _jsx("span", { class: "text-[0.6rem] text-slate-500 mt-0.5 block", children: t('ems_standby_hint') })] })] }), _jsxs("div", { children: [_jsx("label", { class: "block text-slate-400 mb-1", children: t('ems_force_power_key') }), _jsx("input", { type: "text", value: editingConsumer.forcePowerKey || '', onInput: (e) => setEditingConsumer({ ...editingConsumer, forcePowerKey: e.target.value }), placeholder: t('ems_force_key_placeholder'), class: "w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-200 font-mono focus:border-cyan-500 outline-none" })] })] }))] }), _jsxs("div", { children: [_jsx("label", { class: "block text-slate-400 mb-1", children: t('ems_actual_power_key') }), _jsx("input", { type: "text", value: editingConsumer.actualPowerKey || '', onInput: (e) => setEditingConsumer({ ...editingConsumer, actualPowerKey: e.target.value }), placeholder: t('ems_actual_key_placeholder'), class: "w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-200 font-mono focus:border-cyan-500 outline-none" })] }), _jsxs("div", { class: "flex items-center justify-end gap-3 pt-4 border-t border-slate-800", children: [_jsx("button", { type: "button", onClick: () => setShowConsumerModal(false), class: "px-4 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-300 font-bold transition-all", children: t('ems_cancel') }), _jsxs("button", { type: "submit", disabled: savingConsumer, class: "px-5 py-2 rounded-xl bg-cyan-500 hover:bg-cyan-400 text-slate-950 font-bold transition-all shadow-md flex items-center gap-2", children: [savingConsumer && _jsx("i", { class: "fas fa-spinner fa-spin" }), editingConsumer.id ? t('ems_save_changes') : t('ems_add_consumer')] })] })] })] }) }))] }));
}
