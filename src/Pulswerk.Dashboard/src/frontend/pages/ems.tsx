import { useState, useEffect } from 'preact/hooks';
import { t } from '../i18n';

interface EnergyConsumer {
    id: string;
    name: string;
    basePowerKw: number;
    hasOptionalTier: boolean;
    maxOptionalKw: number;
    minOptionalKw: number;
    standbyOptionalKw: number;
    actualPowerKey: string;
    forcePowerKey: string;
    priority: number;
    actualPowerKw: number;
    isActivelyDemanding: boolean;
    allocatedOptionalKw: number;
    allocatedPowerKw: number;
    unusedPowerKw: number;
    status: string;

    // Legacy compatibility
    isControllable?: boolean;
    baseLimitKw?: number;
    minPowerKw?: number;
    maxPowerKw?: number;
    energy24hKwh?: number;
}

interface EnergySourcesConfig {
    gridMeterKey: string;
    gridMaxImportKw: number;
    pvMeterKey: string;
    hasBattery?: boolean;
    batteryPowerKey: string;
    batterySocKey: string;
    batteryMaxPowerKw?: number;
    batteryMaxChargeKw?: number;
    batteryMaxDischargeKw?: number;
    batteryMinReserveKw: number;
    batteryMinSocPct: number;
    batteryFullSocPct: number;
}

interface EnergySystemSnapshot {
    enabled: boolean;
    gridImportKw: number;
    gridMaxImportKw: number;
    pvPowerKw?: number;
    pvGenerationKw: number;
    batteryPowerKw: number;
    batteryChargeKw: number;
    batterySocPct: number;
    batteryMaxPowerKw?: number;
    batteryMaxChargeKw?: number;
    batteryMaxDischargeKw?: number;
    isBatteryCharging: boolean;
    totalSurplusAvailableKw: number;
    totalBaseLoadKw?: number;
    totalOptionalLoadKw?: number;
    totalReclaimedPowerKw?: number;
    uncontrollableLoadKw: number;
    totalControllableLoadKw: number;

    // Rolling 24-hour calculated energy
    gridImport24hKwh?: number;
    gridExport24hKwh?: number;
    pvGeneration24hKwh?: number;
    batteryCharged24hKwh?: number;
    batteryDischarged24hKwh?: number;
    uncontrollable24hKwh?: number;
    totalSurplus24hKwh?: number;
    totalControllable24hKwh?: number;

    consumers: EnergyConsumer[];
    sources: EnergySourcesConfig;
    logs: LogEntry[];
}

interface LogEntry {
    timestamp: string;
    message: string;
    state: string;
}

export function EmsPage() {
    const [snapshot, setSnapshot] = useState<EnergySystemSnapshot | null>(null);
    const [loading, setLoading] = useState(true);
    const [toggling, setToggling] = useState(false);

    // Modal state
    const [showConfigModal, setShowConfigModal] = useState(false);
    const [showConsumerModal, setShowConsumerModal] = useState(false);
    const [editingConsumer, setEditingConsumer] = useState<Partial<EnergyConsumer> | null>(null);
    const [savingConsumer, setSavingConsumer] = useState(false);
    const [savingConfig, setSavingConfig] = useState(false);

    // Config form
    const [gridMaxKw, setGridMaxKw] = useState(8.0);
    const [gridKey, setGridKey] = useState('meter-main-a_power');
    const [pvKey, setPvKey] = useState('pv-rooftop_power');
    const [hasBattery, setHasBattery] = useState(true);
    const [battPowerKey, setBattPowerKey] = useState('solis-battery_power');
    const [battSocKey, setBattSocKey] = useState('solis-battery_battery_soc');
    const [battReserveKw, setBattReserveKw] = useState(1.0);
    const [battMaxKw, setBattMaxKw] = useState(5.0);

    const fetchSnapshot = async () => {
        try {
            let res = await fetch('/plswk/api/ems/status');
            if (!res.ok) {
                res = await fetch('/plswk/api/trajectory/status');
            }
            if (res.ok) {
                const data: EnergySystemSnapshot = await res.json();
                if (data) {
                    setSnapshot(data);
                }
            }
        } catch (e) {
            console.error("Failed to fetch EMS status:", e);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchSnapshot();
        const interval = setInterval(fetchSnapshot, 4000);
        return () => clearInterval(interval);
    }, []);

    const handleToggleEnabled = async () => {
        if (!snapshot || toggling) return;
        setToggling(true);
        try {
            const res = await fetch('/plswk/api/ems/config', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ enabled: !snapshot.enabled })
            });
            if (res.ok) await fetchSnapshot();
        } catch (e) {
            console.error("Failed to toggle EMS:", e);
        } finally {
            setToggling(false);
        }
    };

    const handleOpenConfigModal = () => {
        if (snapshot?.sources) {
            setGridMaxKw(snapshot.sources.gridMaxImportKw ?? 8.0);
            setGridKey(snapshot.sources.gridMeterKey ?? 'meter-main-a_power');
            setPvKey(snapshot.sources.pvMeterKey ?? 'pv-rooftop_power');
            setHasBattery(snapshot.sources.hasBattery ?? true);
            setBattPowerKey(snapshot.sources.batteryPowerKey ?? 'solis-battery_power');
            setBattSocKey(snapshot.sources.batterySocKey ?? 'solis-battery_battery_soc');
            setBattReserveKw(snapshot.sources.batteryMinReserveKw ?? 1.0);
            setBattMaxKw(snapshot.sources.batteryMaxPowerKw ?? 5.0);
        }
        setShowConfigModal(true);
    };

    const handleSaveSystemConfig = async (e: Event) => {
        e.preventDefault();
        setSavingConfig(true);
        try {
            const res = await fetch('/plswk/api/ems/config', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    enabled: snapshot?.enabled ?? true,
                    baseLimitKw: gridMaxKw,
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
                        batteryFullSocPct: snapshot?.sources?.batteryFullSocPct ?? 98.0
                    }
                })
            });
            if (res.ok) {
                setShowConfigModal(false);
                await fetchSnapshot();
            } else {
                const errText = await res.text();
                alert(`Failed to save system configuration: ${errText || res.statusText}`);
            }
        } catch (err: any) {
            console.error("Failed to save config:", err);
            alert(`Error saving configuration: ${err?.message || err}`);
        } finally {
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

    const handleOpenEditConsumer = (c: EnergyConsumer) => {
        setEditingConsumer({
            ...c,
            id: c.id,
            name: c.name,
            basePowerKw: c.basePowerKw ?? 0.0,
            hasOptionalTier: c.hasOptionalTier ?? c.isControllable ?? true,
            maxOptionalKw: c.maxOptionalKw ?? c.baseLimitKw ?? 8.0,
            minOptionalKw: c.minOptionalKw ?? c.minPowerKw ?? 0.0,
            standbyOptionalKw: c.standbyOptionalKw ?? 0.0,
            actualPowerKey: c.actualPowerKey ?? '',
            forcePowerKey: c.forcePowerKey ?? '',
            priority: c.priority ?? 1,
            maxPowerKw: c.maxPowerKw ?? 22.0
        });
        setShowConsumerModal(true);
    };

    const handleDeleteConsumer = async (id: string, name: string) => {
        if (!confirm(`Delete consumer '${name}'?`)) return;
        try {
            const res = await fetch(`/plswk/api/ems/consumers/${encodeURIComponent(id)}`, {
                method: 'DELETE'
            });
            if (res.ok) await fetchSnapshot();
            else alert("Failed to delete consumer");
        } catch (e) {
            console.error("Failed to delete consumer:", e);
        }
    };

    const handleSaveConsumer = async (e: Event) => {
        e.preventDefault();
        if (!editingConsumer) return;
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
            } else {
                const errText = await res.text();
                alert(`Failed to save consumer: ${errText || res.statusText}`);
            }
        } catch (err: any) {
            console.error("Failed to save consumer:", err);
            alert(`Error saving consumer: ${err?.message || err}`);
        } finally {
            setSavingConsumer(false);
        }
    };

    const handleOpenTelemetryDetails = (key?: string | null) => {
        if (!key) return;
        if (typeof (window as any).openTelemetryDetails === 'function') {
            (window as any).openTelemetryDetails(key);
        } else {
            console.warn("openTelemetryDetails is not available on window");
        }
    };

    const getConsumerIconConfig = (name: string = '', id: string = '') => {
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
        return (
            <div class="flex items-center justify-center min-h-[60vh]">
                <div class="flex items-center gap-3 text-cyan-400">
                    <i class="fas fa-spinner fa-spin text-2xl"></i>
                    <span class="font-medium text-slate-300">Loading Energy Management...</span>
                </div>
            </div>
        );
    }

    const controllableConsumers = snapshot?.consumers?.filter(c => c.hasOptionalTier ?? c.isControllable) ?? [];
    const uncontrollableConsumers = snapshot?.consumers?.filter(c => !(c.hasOptionalTier ?? c.isControllable)) ?? [];

    const isSurplusActive = (snapshot?.totalSurplusAvailableKw ?? 0) > 0.1;
    const hasReclaimedPower = (snapshot?.totalReclaimedPowerKw ?? 0) > 0.1;

    return (
        <div class="max-w-7xl mx-auto px-4 py-8 space-y-8">
            {/* Header with Glassmorphism and Status */}
            <div class="flex flex-col md:flex-row md:items-center justify-between gap-4 bg-slate-900/60 backdrop-blur-md border border-slate-800/80 p-6 rounded-3xl shadow-xl">
                <div>
                    <div class="flex items-center gap-3">
                        <div class="w-10 h-10 rounded-2xl bg-cyan-500/10 border border-cyan-500/30 flex items-center justify-center text-cyan-400 shadow-sm">
                            <i class="fas fa-bolt text-lg"></i>
                        </div>
                        <div>
                            <h1 class="text-2xl font-black text-slate-100 tracking-tight">
                                {t('ems_page_title')}
                            </h1>
                            <p class="text-xs text-slate-400 mt-0.5">
                                {t('ems_page_subtitle')}
                            </p>
                        </div>
                    </div>
                </div>

                <div class="flex items-center gap-3">
                    <button
                        onClick={handleToggleEnabled}
                        disabled={toggling}
                        class={`px-4 py-2.5 rounded-xl font-bold text-xs tracking-wider uppercase transition-all flex items-center gap-2.5 shadow-sm ${
                            snapshot?.enabled
                                ? 'bg-emerald-500/15 border border-emerald-500/40 text-emerald-400 hover:bg-emerald-500/25'
                                : 'bg-slate-800 border border-slate-700 text-slate-400 hover:bg-slate-750'
                        }`}
                    >
                        <span class={`w-2.5 h-2.5 rounded-full ${snapshot?.enabled ? 'bg-emerald-400 animate-pulse' : 'bg-slate-500'}`}></span>
                        {snapshot?.enabled ? 'EMS Active' : 'EMS Disabled'}
                    </button>

                    <button
                        onClick={handleOpenConfigModal}
                        class="px-4 py-2.5 rounded-xl bg-slate-800 hover:bg-slate-700 border border-slate-700 text-slate-200 text-xs font-bold transition-all flex items-center gap-2"
                        title={t('ems_configure_system')}
                    >
                        <i class="fas fa-sliders-h text-cyan-400"></i>
                        <span>{t('ems_configure_system')}</span>
                    </button>
                </div>
            </div>

            {/* Two-Sided Grid Layout: Sources & Storage (Left) <-> Center Dispatch <-> Consumers (Right) */}
            <div class="grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">
                
                {/* ── LEFT COLUMN: SOURCES & STORAGE (5 Cols) ────────────────── */}
                <div class="lg:col-span-5 space-y-4">
                    <div class="flex items-center justify-between px-1">
                        <h2 class="text-xs uppercase tracking-widest font-black text-slate-400 flex items-center gap-2">
                            <i class="fas fa-layer-group text-emerald-400"></i>
                            {t('ems_sources_title')}
                        </h2>
                        <span class="text-[0.65rem] text-slate-500 font-mono">SUPPLY & STORAGE</span>
                    </div>

                    {/* Grid Connection Card */}
                    <div class="bg-slate-900/70 border border-slate-800/90 rounded-2xl p-5 shadow-lg relative overflow-hidden group hover:border-sky-500/30 transition-all">
                        <div class="flex items-center justify-between mb-3">
                            <div class="flex items-center gap-2.5">
                                <div class="w-8 h-8 rounded-xl bg-sky-500/10 border border-sky-500/30 text-sky-400 flex items-center justify-center text-xs">
                                    <i class="fas fa-network-wired"></i>
                                </div>
                                <div>
                                    <h3 class="text-sm font-bold text-slate-200">{t('ems_grid_connection')}</h3>
                                    <div 
                                        onClick={() => handleOpenTelemetryDetails(snapshot?.sources?.gridMeterKey || 'ems_grid_power')}
                                        class="text-[0.65rem] text-slate-400 font-mono hover:text-sky-400 cursor-pointer transition-colors flex items-center gap-1 group/key"
                                        title={`View telemetry details for ${snapshot?.sources?.gridMeterKey || 'ems_grid_power'}`}
                                    >
                                        <span>{snapshot?.sources?.gridMeterKey || 'ems_grid_power'}</span>
                                        <i class="fas fa-chart-line text-[0.55rem] opacity-0 group-hover/key:opacity-100 transition-opacity text-sky-400"></i>
                                    </div>
                                </div>
                            </div>
                            <div class="text-right">
                                <span 
                                    onClick={() => handleOpenTelemetryDetails('ems_grid_max_import')}
                                    class="px-2 py-0.5 rounded-md bg-sky-500/10 text-sky-400 text-[0.65rem] font-bold border border-sky-500/20 hover:bg-sky-500/20 hover:border-sky-500/40 cursor-pointer transition-all"
                                    title="View / edit telemetry details: ems_grid_max_import"
                                >
                                    Max {snapshot?.gridMaxImportKw?.toFixed(1) ?? '8.0'} kW
                                </span>
                            </div>
                        </div>

                        <div class="flex items-baseline justify-between mt-2">
                            <div 
                                onClick={() => handleOpenTelemetryDetails(snapshot?.sources?.gridMeterKey || 'ems_grid_power')}
                                class="cursor-pointer group/val inline-flex items-baseline hover:opacity-85 transition-all"
                                title={`View telemetry details for ${snapshot?.sources?.gridMeterKey || 'ems_grid_power'}`}
                            >
                                <span class="text-2xl font-black text-slate-100 group-hover/val:text-sky-400 transition-colors">
                                    {Math.abs(snapshot?.gridImportKw ?? 0).toFixed(1)}
                                </span>
                                <span class="text-xs font-bold text-slate-400 ml-1">kW</span>
                                <i class="fas fa-chart-area text-[0.65rem] text-slate-600 group-hover/val:text-sky-400 ml-1.5 opacity-0 group-hover/val:opacity-100 transition-all"></i>
                            </div>
                            <span 
                                onClick={() => handleOpenTelemetryDetails((snapshot?.gridImportKw ?? 0) >= 0 ? 'ems_grid_import' : 'ems_grid_export')}
                                class={`text-xs font-bold ${(snapshot?.gridImportKw ?? 0) >= 0 ? 'text-amber-400 hover:text-amber-300' : 'text-emerald-400 hover:text-emerald-300'} flex items-center gap-1.5 cursor-pointer transition-colors`}
                                title={`View telemetry details for ${(snapshot?.gridImportKw ?? 0) >= 0 ? 'ems_grid_import' : 'ems_grid_export'}`}
                            >
                                {(snapshot?.gridImportKw ?? 0) < 0 && <i class="fas fa-arrow-left text-[0.65rem]"></i>}
                                {(snapshot?.gridImportKw ?? 0) >= 0 ? t('ems_grid_import') : t('ems_grid_export')}
                                {(snapshot?.gridImportKw ?? 0) >= 0 && <i class="fas fa-arrow-right text-[0.65rem]"></i>}
                            </span>
                        </div>

                        {/* Utilization Bar against Grid Max */}
                        <div class="w-full bg-slate-800/80 rounded-full h-1.5 mt-3 overflow-hidden">
                            <div
                                class="h-full rounded-full transition-all duration-500 bg-sky-400"
                                style={{ width: `${Math.min(100, Math.max(0, ((snapshot?.gridImportKw ?? 0) / (snapshot?.gridMaxImportKw || 8.0)) * 100))}%` }}
                            ></div>
                        </div>

                        {/* Rolling 24h Energy Footer */}
                        <div class="mt-3 pt-3 border-t border-slate-800/80 flex items-center justify-between text-xs">
                            <span class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wider">{t('ems_24h_rolling')}</span>
                            <div class="flex items-center gap-2">
                                <div 
                                    onClick={() => handleOpenTelemetryDetails('ems_grid_export_24h')}
                                    class="flex items-center gap-1 text-[0.68rem] bg-emerald-500/10 text-emerald-300 border border-emerald-500/20 rounded-md px-2 py-0.5 cursor-pointer hover:bg-emerald-500/20 hover:border-emerald-500/40 transition-all" 
                                    title="View telemetry details: ems_grid_export_24h"
                                >
                                    <i class="fas fa-arrow-left text-[0.55rem] text-emerald-400"></i>
                                    <span class="font-bold">{snapshot?.gridExport24hKwh?.toFixed(1) ?? '0.0'} kWh</span>
                                    <span class="text-[0.6rem] text-emerald-400/80 uppercase font-semibold">{t('ems_export')}</span>
                                </div>
                                <div 
                                    onClick={() => handleOpenTelemetryDetails('ems_grid_import_24h')}
                                    class="flex items-center gap-1 text-[0.68rem] bg-amber-500/10 text-amber-300 border border-amber-500/20 rounded-md px-2 py-0.5 cursor-pointer hover:bg-amber-500/20 hover:border-amber-500/40 transition-all" 
                                    title="View telemetry details: ems_grid_import_24h"
                                >
                                    <span class="text-[0.6rem] text-amber-400/80 uppercase font-semibold">{t('ems_import')}</span>
                                    <span class="font-bold">{snapshot?.gridImport24hKwh?.toFixed(1) ?? '0.0'} kWh</span>
                                    <i class="fas fa-arrow-right text-[0.55rem] text-amber-400"></i>
                                </div>
                            </div>
                        </div>
                    </div>

                    {/* Solar PV Generation Card */}
                    <div class="bg-slate-900/70 border border-slate-800/90 rounded-2xl p-5 shadow-lg relative overflow-hidden group hover:border-amber-500/30 transition-all">
                        <div class="flex items-center justify-between mb-3">
                            <div class="flex items-center gap-2.5">
                                <div class="w-8 h-8 rounded-xl bg-amber-500/10 border border-amber-500/30 text-amber-400 flex items-center justify-center text-xs">
                                    <i class="fas fa-solar-panel"></i>
                                </div>
                                <div>
                                    <h3 class="text-sm font-bold text-slate-200">{t('ems_pv_generation')}</h3>
                                    <div 
                                        onClick={() => handleOpenTelemetryDetails(snapshot?.sources?.pvMeterKey || 'ems_pv_power')}
                                        class="text-[0.65rem] text-slate-400 font-mono hover:text-amber-400 cursor-pointer transition-colors flex items-center gap-1 group/key"
                                        title={`View telemetry details for ${snapshot?.sources?.pvMeterKey || 'ems_pv_power'}`}
                                    >
                                        <span>{snapshot?.sources?.pvMeterKey || 'ems_pv_power'}</span>
                                        <i class="fas fa-chart-line text-[0.55rem] opacity-0 group-hover/key:opacity-100 transition-opacity text-amber-400"></i>
                                    </div>
                                </div>
                            </div>
                            <span 
                                onClick={() => handleOpenTelemetryDetails(snapshot?.sources?.pvMeterKey || 'ems_pv_power')}
                                class="px-2 py-0.5 rounded-md bg-amber-500/10 text-amber-400 text-[0.65rem] font-bold border border-amber-500/20 hover:bg-amber-500/20 hover:border-amber-500/40 cursor-pointer transition-all"
                                title={`View telemetry details for ${snapshot?.sources?.pvMeterKey || 'ems_pv_power'}`}
                            >
                                PV Solar
                            </span>
                        </div>

                        <div class="flex items-baseline justify-between mt-2">
                            <div 
                                onClick={() => handleOpenTelemetryDetails(snapshot?.sources?.pvMeterKey || 'ems_pv_power')}
                                class="cursor-pointer group/val inline-flex items-baseline hover:opacity-85 transition-all"
                                title={`View telemetry details for ${snapshot?.sources?.pvMeterKey || 'ems_pv_power'}`}
                            >
                                <span class="text-2xl font-black text-amber-400 group-hover/val:text-amber-300 transition-colors">
                                    {Math.abs(snapshot?.pvGenerationKw ?? snapshot?.pvPowerKw ?? 0).toFixed(1)}
                                </span>
                                <span class="text-xs font-bold text-slate-400 ml-1">kW</span>
                                <i class="fas fa-chart-area text-[0.65rem] text-amber-600 group-hover/val:text-amber-400 ml-1.5 opacity-0 group-hover/val:opacity-100 transition-all"></i>
                            </div>
                            <span 
                                onClick={() => handleOpenTelemetryDetails(snapshot?.sources?.pvMeterKey || 'ems_pv_power')}
                                class="text-xs text-amber-400 font-medium flex items-center gap-1.5 cursor-pointer hover:text-amber-300 transition-colors"
                                title={`View telemetry details for ${snapshot?.sources?.pvMeterKey || 'ems_pv_power'}`}
                            >
                                {(snapshot?.pvPowerKw ?? snapshot?.pvGenerationKw ?? 0) < -0.2 && <i class="fas fa-arrow-left text-[0.65rem]"></i>}
                                {t('ems_generation')}
                                {(snapshot?.pvPowerKw ?? snapshot?.pvGenerationKw ?? 0) >= -0.2 && <i class="fas fa-arrow-right text-[0.65rem]"></i>}
                            </span>
                        </div>

                        {/* Rolling 24h Energy Footer */}
                        <div class="mt-3 pt-3 border-t border-slate-800/80 flex items-center justify-between text-xs">
                            <span class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wider">{t('ems_24h_rolling')}</span>
                            <div 
                                onClick={() => handleOpenTelemetryDetails('ems_pv_generation_24h')}
                                class="flex items-center gap-1 text-[0.68rem] bg-amber-500/10 text-amber-300 border border-amber-500/20 rounded-md px-2 py-0.5 cursor-pointer hover:bg-amber-500/20 hover:border-amber-500/40 transition-all" 
                                title="View telemetry details: ems_pv_generation_24h"
                            >
                                <span class="text-[0.6rem] text-amber-400/80 uppercase font-semibold">{t('ems_generation')}</span>
                                <span class="font-bold">{snapshot?.pvGeneration24hKwh?.toFixed(1) ?? '0.0'} kWh</span>
                                <i class="fas fa-arrow-right text-[0.55rem] text-amber-400"></i>
                            </div>
                        </div>
                    </div>

                    {/* Battery Storage Card */}
                    {snapshot?.sources?.hasBattery !== false && (
                    <div class={`bg-slate-900/70 border rounded-2xl p-5 shadow-lg relative overflow-hidden transition-all ${
                        snapshot?.isBatteryCharging 
                            ? 'border-emerald-500/40 shadow-emerald-500/5' 
                            : 'border-slate-800/90 hover:border-emerald-500/30'
                    }`}>
                        <div class="flex items-center justify-between mb-3">
                            <div class="flex items-center gap-2.5">
                                <div class={`w-8 h-8 rounded-xl flex items-center justify-center text-xs border ${
                                    snapshot?.isBatteryCharging
                                        ? 'bg-emerald-500/20 border-emerald-500/50 text-emerald-400 animate-pulse'
                                        : 'bg-slate-800 border-slate-700 text-slate-400'
                                }`}>
                                    <i class="fas fa-battery-three-quarters"></i>
                                </div>
                                <div>
                                    <h3 class="text-sm font-bold text-slate-200">{t('ems_battery_storage')}</h3>
                                    <div 
                                        onClick={() => handleOpenTelemetryDetails(snapshot?.sources?.batteryPowerKey || 'ems_battery_power')}
                                        class="text-[0.65rem] text-slate-400 font-mono hover:text-emerald-400 cursor-pointer transition-colors flex items-center gap-1 group/key"
                                        title={`View telemetry details for ${snapshot?.sources?.batteryPowerKey || 'ems_battery_power'}`}
                                    >
                                        <span>{snapshot?.sources?.batteryPowerKey || 'ems_battery_power'}</span>
                                        <i class="fas fa-chart-line text-[0.55rem] opacity-0 group-hover/key:opacity-100 transition-opacity text-emerald-400"></i>
                                    </div>
                                </div>
                            </div>
                            <span 
                                onClick={() => handleOpenTelemetryDetails(snapshot?.isBatteryCharging ? 'ems_battery_charge' : (snapshot?.sources?.batteryPowerKey || 'ems_battery_power'))}
                                class={`px-2 py-0.5 rounded-md text-[0.65rem] font-bold border flex items-center gap-1 cursor-pointer transition-all ${
                                    snapshot?.isBatteryCharging
                                        ? 'bg-emerald-500/15 text-emerald-400 border-emerald-500/30 hover:bg-emerald-500/25 animate-pulse'
                                        : (snapshot?.batteryPowerKw ?? 0) > 0.3
                                            ? 'bg-amber-500/15 text-amber-400 border-amber-500/30 hover:bg-amber-500/25'
                                            : 'bg-slate-800 text-slate-400 border-slate-700 hover:bg-slate-750'
                                }`}
                                title={`View telemetry details for ${snapshot?.isBatteryCharging ? 'ems_battery_charge' : (snapshot?.sources?.batteryPowerKey || 'ems_battery_power')}`}
                            >
                                {snapshot?.isBatteryCharging && <i class="fas fa-arrow-left text-[0.55rem]"></i>}
                                {snapshot?.isBatteryCharging ? t('ems_battery_charging') : (snapshot?.batteryPowerKw ?? 0) > 0.3 ? t('ems_battery_discharging') : t('ems_battery_idle')}
                                {!snapshot?.isBatteryCharging && (snapshot?.batteryPowerKw ?? 0) > 0.3 && <i class="fas fa-arrow-right text-[0.55rem]"></i>}
                            </span>
                        </div>

                        <div class="grid grid-cols-3 gap-3 mt-2">
                            <div 
                                onClick={() => handleOpenTelemetryDetails(snapshot?.sources?.batteryPowerKey || 'ems_battery_power')}
                                class="cursor-pointer group/battp hover:opacity-85 transition-all"
                                title={`View telemetry details for ${snapshot?.sources?.batteryPowerKey || 'ems_battery_power'}`}
                            >
                                <div class="text-[0.65rem] uppercase tracking-wider text-slate-400 font-bold mb-0.5">Power Flow</div>
                                <span class={`text-xl font-black ${snapshot?.isBatteryCharging ? 'text-emerald-400 group-hover/battp:text-emerald-300' : 'text-slate-100 group-hover/battp:text-sky-400'} transition-colors`}>
                                    {Math.abs(snapshot?.batteryPowerKw ?? 0).toFixed(1)}
                                </span>
                                <span class="text-xs font-bold text-slate-400 ml-1">kW</span>
                            </div>

                            <div 
                                onClick={() => handleOpenTelemetryDetails(snapshot?.sources?.batteryPowerKey || 'ems_battery_power')}
                                class="cursor-pointer group/battm hover:opacity-85 transition-all"
                                title={`View telemetry details for ${snapshot?.sources?.batteryPowerKey || 'ems_battery_power'}`}
                            >
                                <div class="text-[0.65rem] uppercase tracking-wider text-slate-400 font-bold mb-0.5">Max Power</div>
                                <span class="text-xl font-black text-slate-100 group-hover/battm:text-sky-400 transition-colors">
                                    {(snapshot?.batteryMaxPowerKw ?? snapshot?.sources?.batteryMaxPowerKw ?? 5.0).toFixed(1)}
                                </span>
                                <span class="text-xs font-bold text-slate-400 ml-1">kW</span>
                            </div>

                            <div 
                                onClick={() => handleOpenTelemetryDetails(snapshot?.sources?.batterySocKey || 'ems_battery_soc')}
                                class="cursor-pointer group/soc hover:opacity-85 transition-all"
                                title={`View telemetry details for ${snapshot?.sources?.batterySocKey || 'ems_battery_soc'}`}
                            >
                                <div class="text-[0.65rem] uppercase tracking-wider text-slate-400 font-bold mb-0.5">{t('ems_battery_soc')}</div>
                                <span class="text-xl font-black text-slate-100 group-hover/soc:text-emerald-400 transition-colors">
                                    {(snapshot?.batterySocPct ?? 0).toFixed(0)}%
                                </span>
                            </div>
                        </div>

                        {/* SoC Progress Bar */}
                        <div 
                            onClick={() => handleOpenTelemetryDetails(snapshot?.sources?.batterySocKey || 'ems_battery_soc')}
                            class="w-full bg-slate-800/80 rounded-full h-2 mt-4 overflow-hidden cursor-pointer hover:opacity-85 transition-all"
                            title={`View telemetry details for ${snapshot?.sources?.batterySocKey || 'ems_battery_soc'}`}
                        >
                            <div
                                class={`h-full rounded-full transition-all duration-500 ${
                                    (snapshot?.batterySocPct ?? 0) > 20 ? 'bg-emerald-400' : 'bg-amber-400'
                                }`}
                                style={{ width: `${Math.min(100, Math.max(0, snapshot?.batterySocPct ?? 0))}%` }}
                            ></div>
                        </div>

                        {/* Rolling 24h Energy Footer */}
                        <div class="mt-3 pt-3 border-t border-slate-800/80 flex items-center justify-between text-xs">
                            <span class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wider">{t('ems_24h_rolling')}</span>
                            <div class="flex items-center gap-2">
                                <div 
                                    onClick={() => handleOpenTelemetryDetails('ems_battery_charged_24h')}
                                    class="flex items-center gap-1 text-[0.68rem] bg-emerald-500/10 text-emerald-300 border border-emerald-500/20 rounded-md px-2 py-0.5 cursor-pointer hover:bg-emerald-500/20 hover:border-emerald-500/40 transition-all" 
                                    title="View telemetry details: ems_battery_charged_24h"
                                >
                                    <i class="fas fa-arrow-left text-[0.55rem] text-emerald-400"></i>
                                    <span class="font-bold">{snapshot?.batteryCharged24hKwh?.toFixed(1) ?? '0.0'} kWh</span>
                                    <span class="text-[0.6rem] text-emerald-400/80 uppercase font-semibold">{t('ems_charged')}</span>
                                </div>
                                <div 
                                    onClick={() => handleOpenTelemetryDetails('ems_battery_discharged_24h')}
                                    class="flex items-center gap-1 text-[0.68rem] bg-amber-500/10 text-amber-300 border border-amber-500/20 rounded-md px-2 py-0.5 cursor-pointer hover:bg-amber-500/20 hover:border-amber-500/40 transition-all" 
                                    title="View telemetry details: ems_battery_discharged_24h"
                                >
                                    <span class="text-[0.6rem] text-amber-400/80 uppercase font-semibold">{t('ems_discharged')}</span>
                                    <span class="font-bold">{snapshot?.batteryDischarged24hKwh?.toFixed(1) ?? '0.0'} kWh</span>
                                    <i class="fas fa-arrow-right text-[0.55rem] text-amber-400"></i>
                                </div>
                            </div>
                        </div>
                    </div>
                    )}
                </div>

                {/* ── CENTER: POWER FLOW & SURPLUS DISPATCH (2 Cols) ───────────── */}
                <div class="lg:col-span-2 flex flex-col items-center justify-center gap-3 py-4">
                    <div class="hidden lg:flex flex-col items-center gap-2 text-slate-600">
                        <i class="fas fa-chevron-right text-lg text-slate-500 animate-pulse"></i>
                    </div>

                    {/* Surplus Card */}
                    <div class={`w-full bg-slate-900/80 border rounded-2xl p-4 text-center shadow-lg transition-all ${
                        isSurplusActive 
                            ? 'border-cyan-500/50 shadow-cyan-500/10 bg-cyan-950/20' 
                            : 'border-slate-800/90'
                    }`}>
                        <div class="text-[0.65rem] uppercase tracking-wider font-bold text-slate-400 mb-1">
                            {t('ems_surplus_pool')}
                        </div>
                        <div 
                            onClick={() => handleOpenTelemetryDetails('ems_surplus_power')}
                            class={`text-2xl font-black ${isSurplusActive ? 'text-cyan-400 hover:text-cyan-300' : 'text-slate-500 hover:text-slate-400'} cursor-pointer transition-colors inline-block`}
                            title="View telemetry details: ems_surplus_power"
                        >
                            {isSurplusActive ? `+${snapshot?.totalSurplusAvailableKw.toFixed(1)} kW` : '0.0 kW'}
                        </div>
                        <div class="text-[0.65rem] text-slate-400 mt-1 font-medium">
                            {isSurplusActive ? t('ems_surplus_active') : t('ems_base_active')}
                        </div>
                        <div 
                            onClick={() => handleOpenTelemetryDetails('ems_total_surplus_24h')}
                            class="text-[0.65rem] text-cyan-300/90 font-mono mt-2 pt-2 border-t border-slate-800/80 flex items-center justify-center gap-1.5 cursor-pointer hover:text-cyan-200 transition-colors"
                            title="View telemetry details: ems_total_surplus_24h"
                        >
                            <span class="text-slate-400">{t('ems_24h_surplus')}:</span>
                            <span class="font-bold text-cyan-300">{snapshot?.totalSurplus24hKwh?.toFixed(1) ?? '0.0'} kWh</span>
                            <i class="fas fa-arrow-right text-[0.55rem] text-cyan-400"></i>
                        </div>
                    </div>

                    {/* Reclaimed & Redistributed Card */}
                    {hasReclaimedPower && (
                        <div 
                            onClick={() => handleOpenTelemetryDetails('ems_reclaimed_power')}
                            class="w-full bg-amber-950/25 border border-amber-500/40 rounded-2xl p-3 text-center shadow-lg transition-all animate-fade-in cursor-pointer hover:bg-amber-950/40 hover:border-amber-500/60"
                            title="View telemetry details: ems_reclaimed_power"
                        >
                            <div class="text-[0.65rem] uppercase tracking-wider font-bold text-amber-400 mb-0.5 flex items-center justify-center gap-1">
                                <i class="fas fa-redo-alt text-[0.6rem]"></i>
                                {t('ems_reclaimed_power')}
                            </div>
                            <div class="text-xl font-black text-amber-300">
                                {snapshot?.totalReclaimedPowerKw?.toFixed(1)} kW
                            </div>
                            <div class="text-[0.6rem] text-amber-400/80 mt-0.5">
                                Idle capacity redistributed
                            </div>
                        </div>
                    )}

                    <div class="hidden lg:flex flex-col items-center gap-2 text-slate-600">
                        <i class="fas fa-chevron-right text-lg text-slate-500 animate-pulse"></i>
                    </div>
                </div>

                {/* ── RIGHT COLUMN: CONSUMERS & SINKS (5 Cols) ────────────────── */}
                <div class="lg:col-span-5 space-y-4">
                    <div class="flex items-center justify-between px-1">
                        <h2 class="text-xs uppercase tracking-widest font-black text-slate-400 flex items-center gap-2">
                            <i class="fas fa-charging-station text-cyan-400"></i>
                            {t('ems_consumers_title')}
                        </h2>
                        <div class="flex items-center gap-2">
                            <button
                                onClick={handleOpenAddConsumer}
                                class="text-xs font-bold text-cyan-400 hover:text-cyan-300 transition-colors flex items-center gap-1"
                            >
                                <i class="fas fa-plus"></i>
                                {t('ems_add_consumer')}
                            </button>
                        </div>
                    </div>

                    {/* Controllable Consumers List */}
                    {controllableConsumers.map((consumer) => {
                        const optQuota = consumer.maxOptionalKw ?? consumer.baseLimitKw ?? 8.0;
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

                        return (
                            <div
                                key={consumer.id}
                                class={`bg-slate-900/70 border rounded-2xl p-5 shadow-lg relative overflow-hidden transition-all ${
                                    isBoosted 
                                        ? 'border-cyan-500/40 shadow-cyan-500/5' 
                                        : isIdle
                                            ? 'border-amber-500/30 bg-amber-950/5'
                                            : 'border-slate-800/90 hover:border-cyan-500/30'
                                }`}
                            >
                                <div class="flex items-center justify-between mb-3">
                                    <div class="flex items-center gap-2.5">
                                        <div class={`w-8 h-8 rounded-xl flex items-center justify-center text-xs border ${
                                            isBoosted
                                                ? 'bg-cyan-500/20 border-cyan-500/50 text-cyan-300 animate-pulse'
                                                : isIdle
                                                    ? 'bg-amber-500/10 border-amber-500/30 text-amber-400'
                                                    : `${iconCfg.bg} ${iconCfg.border} ${iconCfg.text}`
                                        }`}>
                                            <i class={`fas ${iconCfg.icon}`}></i>
                                        </div>
                                        <div>
                                            <div class="flex items-center gap-2">
                                                <h3 class="text-sm font-bold text-slate-200">{consumer.name || consumer.id}</h3>
                                            </div>
                                            <div 
                                                onClick={() => handleOpenTelemetryDetails(actualKey || subtitleKey)}
                                                class="text-[0.65rem] text-slate-400 font-mono hover:text-cyan-400 cursor-pointer transition-colors flex items-center gap-1 group/ckey mt-0.5"
                                                title={`View telemetry details for ${actualKey || subtitleKey}`}
                                            >
                                                <span>{subtitleKey}</span>
                                                <i class="fas fa-chart-line text-[0.55rem] opacity-0 group-hover/ckey:opacity-100 transition-opacity text-cyan-400"></i>
                                            </div>
                                        </div>
                                    </div>

                                    <div class="flex items-center gap-2">
                                        <span class="px-1.5 py-0.5 rounded text-[0.65rem] font-bold bg-slate-800 text-slate-300 border border-slate-700">
                                            P{consumer.priority}
                                        </span>

                                        <span class={`text-[0.65rem] font-bold px-2 py-0.5 rounded border ${
                                            isBoosted
                                                ? 'bg-cyan-500/15 text-cyan-400 border-cyan-500/30'
                                                : isIdle
                                                    ? 'bg-amber-500/10 text-amber-400 border-amber-500/30'
                                                    : 'bg-emerald-500/15 text-emerald-400 border-emerald-500/30'
                                        }`}>
                                            {consumer.status || (isIdle ? 'Idle' : 'Active')}
                                        </span>

                                        <button
                                            onClick={() => handleOpenEditConsumer(consumer)}
                                            class="w-7 h-7 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-400 hover:text-cyan-400 flex items-center justify-center text-xs transition-all"
                                            title={t('ems_edit_consumer')}
                                        >
                                            <i class="fas fa-pencil-alt"></i>
                                        </button>
                                        <button
                                            onClick={() => handleDeleteConsumer(consumer.id, consumer.name)}
                                            class="w-7 h-7 rounded-lg bg-slate-800 hover:bg-rose-900/40 text-slate-400 hover:text-rose-400 flex items-center justify-center text-xs transition-all"
                                            title={t('ems_delete_consumer')}
                                        >
                                            <i class="fas fa-trash"></i>
                                        </button>
                                    </div>
                                </div>

                                <div class="grid grid-cols-2 gap-4 mt-2">
                                    <div>
                                        <div class="text-[0.65rem] uppercase tracking-wider text-slate-400 font-bold mb-0.5">
                                            {t('ems_allocated_power')}
                                        </div>
                                        <div
                                            onClick={() => handleOpenTelemetryDetails(allocatedKey)}
                                            class="cursor-pointer group/alloc inline-flex items-baseline hover:opacity-85 transition-all"
                                            title={`View telemetry details for ${allocatedKey}`}
                                        >
                                            <span class={`text-2xl font-black ${isBoosted ? 'text-cyan-400 group-hover/alloc:text-cyan-300' : 'text-slate-100 group-hover/alloc:text-sky-400'} transition-colors`}>
                                                {consumer.allocatedPowerKw.toFixed(1)}
                                            </span>
                                            <span class="text-xs font-bold text-slate-400 ml-1">kW</span>
                                        </div>
                                        {hasUnused && (
                                            <div 
                                                onClick={() => handleOpenTelemetryDetails(unusedKey)}
                                                class="text-[0.65rem] font-bold text-amber-400 hover:text-amber-300 cursor-pointer transition-colors flex items-center gap-1 mt-1"
                                                title={`View telemetry details for ${unusedKey}`}
                                            >
                                                <i class="fas fa-share text-[0.55rem]"></i>
                                                {consumer.unusedPowerKw.toFixed(1)} kW shared
                                            </div>
                                        )}
                                    </div>

                                    <div>
                                        <div class="text-[0.65rem] uppercase tracking-wider text-slate-400 font-bold mb-0.5">
                                            {t('ems_actual_power')}
                                        </div>
                                        <div
                                            onClick={() => handleOpenTelemetryDetails(actualKey)}
                                            class="cursor-pointer group/act inline-flex items-baseline hover:opacity-85 transition-all"
                                            title={`View telemetry details for ${actualKey}`}
                                        >
                                            <span class="text-2xl font-black text-slate-200 group-hover/act:text-cyan-400 transition-colors">
                                                {consumer.actualPowerKw.toFixed(1)}
                                            </span>
                                            <span class="text-xs font-bold text-slate-400 ml-1">kW</span>
                                            <i class="fas fa-chart-area text-[0.65rem] text-slate-600 group-hover/act:text-cyan-400 ml-1.5 opacity-0 group-hover/act:opacity-100 transition-all"></i>
                                        </div>
                                    </div>
                                </div>

                                {/* Rolling 24h Energy Footer (Consistent with Supply Cards) */}
                                <div class="mt-3 pt-3 border-t border-slate-800/80 flex items-center justify-between text-xs gap-2">
                                    <div class="flex items-center gap-2 whitespace-nowrap shrink-0">
                                        <span class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wider">{t('ems_24h_rolling')}</span>
                                        <div 
                                            onClick={() => handleOpenTelemetryDetails(energyKey)}
                                            class="flex items-center gap-1 text-[0.68rem] bg-cyan-500/10 text-cyan-300 border border-cyan-500/20 rounded-md px-2 py-0.5 cursor-pointer hover:bg-cyan-500/20 hover:border-cyan-500/40 transition-all" 
                                            title={`View telemetry details: ${energyKey}`}
                                        >
                                            <span class="text-[0.6rem] text-cyan-400/80 uppercase font-semibold">{t('ems_consumed')}</span>
                                            <span class="font-bold">{(consumer.energy24hKwh ?? 0).toFixed(1)} kWh</span>
                                            <i class="fas fa-arrow-right text-[0.55rem] text-cyan-400"></i>
                                        </div>
                                    </div>
                                    <span class="text-[0.68rem] text-slate-400 font-mono whitespace-nowrap text-right">
                                        Base: {(consumer.basePowerKw ?? 0).toFixed(1)} kW · Opt: {(consumer.maxOptionalKw ?? consumer.baseLimitKw ?? 8.0).toFixed(1)} kW
                                    </span>
                                </div>
                            </div>
                        );
                    })}

                    {/* Uncontrollable Base Loads Card */}
                    <div class="bg-slate-900/70 border border-slate-800/90 rounded-2xl p-5 shadow-lg relative overflow-hidden space-y-3">
                        <div class="flex items-center justify-between">
                            <div class="flex items-center gap-2.5">
                                <div class="w-8 h-8 rounded-xl bg-purple-500/10 border border-purple-500/30 text-purple-400 flex items-center justify-center text-xs">
                                    <i class="fas fa-building"></i>
                                </div>
                                <div>
                                    <h3 class="text-sm font-bold text-slate-200">{t('ems_uncontrollable_loads')}</h3>
                                    <div 
                                        onClick={() => handleOpenTelemetryDetails('ems_uncontrollable_load')}
                                        class="text-[0.65rem] text-slate-400 font-mono hover:text-purple-300 cursor-pointer transition-colors"
                                        title="View telemetry details: ems_uncontrollable_load"
                                    >
                                        {uncontrollableConsumers.length > 0 
                                            ? 'Building Infrastructure & Sub-meters' 
                                            : t('ems_calculated_residual')}
                                    </div>
                                </div>
                            </div>
                            <span 
                                onClick={() => handleOpenTelemetryDetails('ems_uncontrollable_load')}
                                class="px-2 py-0.5 rounded-md bg-purple-500/10 text-purple-400 text-[0.65rem] font-bold border border-purple-500/20 hover:bg-purple-500/20 cursor-pointer transition-all"
                                title="View telemetry details: ems_uncontrollable_load"
                            >
                                {uncontrollableConsumers.length > 0 ? 'Essential Load' : t('ems_calculated_balance')}
                            </span>
                        </div>

                        <div class="grid grid-cols-2 gap-4 mt-2">
                            <div>
                                <div class="text-[0.65rem] uppercase tracking-wider text-slate-400 font-bold mb-0.5">
                                    {t('ems_actual_power')}
                                </div>
                                <div
                                    onClick={() => handleOpenTelemetryDetails('ems_uncontrollable_load')}
                                    class="cursor-pointer group/uc inline-flex items-baseline hover:opacity-85 transition-all"
                                    title="View telemetry details: ems_uncontrollable_load"
                                >
                                    <span class="text-2xl font-black text-slate-200 group-hover/uc:text-purple-300 transition-colors">
                                        {(snapshot?.uncontrollableLoadKw ?? 0).toFixed(1)}
                                    </span>
                                    <span class="text-xs font-bold text-slate-400 ml-1">kW</span>
                                    <i class="fas fa-chart-area text-[0.65rem] text-slate-600 group-hover/uc:text-purple-400 ml-1.5 opacity-0 group-hover/uc:opacity-100 transition-all"></i>
                                </div>
                            </div>
                            <div>
                                <div class="text-[0.65rem] uppercase tracking-wider text-slate-400 font-bold mb-0.5">
                                    Load Profile
                                </div>
                                <span class="text-sm font-bold text-slate-300">
                                    {uncontrollableConsumers.length > 0 ? 'Continuous Draw' : 'Dynamic Balance'}
                                </span>
                            </div>
                        </div>

                        {/* Rolling 24h Energy Footer */}
                        <div class="mt-3 pt-3 border-t border-slate-800/80 flex items-center justify-between text-xs">
                            <div class="flex items-center gap-2">
                                <span class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wider">{t('ems_24h_rolling')}</span>
                                <div 
                                    onClick={() => handleOpenTelemetryDetails('ems_uncontrollable_24h')}
                                    class="flex items-center gap-1 text-[0.68rem] bg-purple-500/10 text-purple-300 border border-purple-500/20 rounded-md px-2 py-0.5 cursor-pointer hover:bg-purple-500/20 hover:border-purple-500/40 transition-all" 
                                    title="View telemetry details: ems_uncontrollable_24h"
                                >
                                    <span class="text-[0.6rem] text-purple-400/80 uppercase font-semibold">{t('ems_consumed')}</span>
                                    <span class="font-bold">{(snapshot?.uncontrollable24hKwh ?? 0).toFixed(1)} kWh</span>
                                    <i class="fas fa-arrow-right text-[0.55rem] text-purple-400"></i>
                                </div>
                            </div>
                            <span class="text-[0.7rem] text-slate-400 font-mono">
                                {uncontrollableConsumers.length > 0 ? 'Uncurtailable Base' : 'Energy Balance'}
                            </span>
                        </div>

                        {uncontrollableConsumers.length === 0 ? (
                            <div 
                                onClick={() => handleOpenTelemetryDetails('ems_uncontrollable_load')}
                                class="pt-2 border-t border-slate-800/60 text-[0.68rem] text-slate-400 flex items-center justify-between font-mono bg-purple-500/5 rounded-lg px-2.5 py-1.5 border border-purple-500/10 cursor-pointer hover:bg-purple-500/10 hover:border-purple-500/25 transition-all"
                                title="View telemetry details: ems_uncontrollable_load"
                            >
                                <span class="text-slate-400 flex items-center gap-1.5">
                                    <i class="fas fa-calculator text-purple-400 text-[0.6rem]"></i>
                                    <span>(Grid + PV{snapshot?.sources?.hasBattery !== false ? ' + Battery' : ''}) − Controllable:</span>
                                </span>
                                <span class="text-purple-300 font-bold">
                                    {(snapshot?.uncontrollableLoadKw ?? 0).toFixed(1)} kW
                                </span>
                            </div>
                        ) : (
                            <div class="pt-2 border-t border-slate-800/60 space-y-1">
                                {uncontrollableConsumers.map(uc => {
                                    const ucCleanId = (uc.id || '').toLowerCase().replace(/-/g, '_');
                                    const ucActualKey = uc.actualPowerKey || (ucCleanId ? `ems_${ucCleanId}_actual_power` : '');
                                    const ucEnergyKey = ucCleanId ? `ems_${ucCleanId}_energy_24h` : '';
                                    return (
                                        <div key={uc.id} class="flex items-center justify-between text-xs text-slate-400 group hover:text-slate-200 transition-colors py-0.5">
                                            <div class="flex items-center gap-2">
                                                <span>{uc.name}</span>
                                                <span class="text-[0.65rem] font-mono text-slate-500">P{uc.priority}</span>
                                            </div>
                                            <div class="flex items-center gap-3 font-mono">
                                                <span 
                                                    onClick={() => handleOpenTelemetryDetails(ucActualKey)}
                                                    class="cursor-pointer hover:text-purple-300 transition-colors"
                                                    title={`View telemetry details: ${ucActualKey}`}
                                                >
                                                    {uc.actualPowerKw.toFixed(1)} kW
                                                </span>
                                                <div 
                                                    onClick={() => handleOpenTelemetryDetails(ucEnergyKey)}
                                                    class="flex items-center gap-1 text-purple-300 font-semibold cursor-pointer hover:text-purple-200 transition-colors"
                                                    title={`View telemetry details: ${ucEnergyKey}`}
                                                >
                                                    <span>{(uc.energy24hKwh ?? 0).toFixed(1)} kWh</span>
                                                    <i class="fas fa-arrow-right text-[0.55rem] text-purple-400/70"></i>
                                                </div>
                                                <div class="flex items-center gap-1">
                                                    <button
                                                        onClick={() => handleOpenEditConsumer(uc)}
                                                        class="w-6 h-6 rounded-md bg-slate-800 hover:bg-slate-700 text-slate-400 hover:text-cyan-400 flex items-center justify-center text-[0.65rem] transition-all"
                                                        title={t('ems_edit_consumer')}
                                                    >
                                                        <i class="fas fa-pencil-alt"></i>
                                                    </button>
                                                    <button
                                                        onClick={() => handleDeleteConsumer(uc.id, uc.name)}
                                                        class="w-6 h-6 rounded-md bg-slate-800 hover:bg-rose-900/40 text-slate-400 hover:text-rose-400 flex items-center justify-center text-[0.65rem] transition-all"
                                                        title={t('ems_delete_consumer')}
                                                    >
                                                        <i class="fas fa-trash"></i>
                                                    </button>
                                                </div>
                                            </div>
                                        </div>
                                    );
                                })}
                            </div>
                        )}
                    </div>
                </div>
            </div>

            {/* ── REAL-TIME DECISION & EVENT LOG TABLE ───────────────────────── */}
            <div class="bg-slate-900/60 backdrop-blur-md border border-slate-800/80 rounded-3xl p-6 shadow-xl space-y-4">
                <div class="flex items-center justify-between">
                    <h2 class="text-sm uppercase tracking-widest font-black text-slate-300 flex items-center gap-2">
                        <i class="fas fa-history text-cyan-400"></i>
                        {t('ems_intervention_logs')}
                    </h2>
                    <span class="text-xs text-slate-500 font-mono">Live Stream</span>
                </div>

                {(!snapshot?.logs || snapshot.logs.length === 0) ? (
                    <div class="py-8 text-center text-slate-500 text-xs">
                        {t('ems_no_intervention')}
                    </div>
                ) : (
                    <div class="overflow-x-auto">
                        <table class="w-full text-left text-xs text-slate-300">
                            <thead class="text-[0.65rem] uppercase tracking-wider text-slate-500 border-b border-slate-800">
                                <tr>
                                    <th class="py-2.5 px-3">Timestamp</th>
                                    <th class="py-2.5 px-3">Decision / Event</th>
                                    <th class="py-2.5 px-3 text-right">State</th>
                                </tr>
                            </thead>
                            <tbody class="divide-y divide-slate-800/60 font-mono text-[0.7rem]">
                                {snapshot.logs.slice().reverse().map((log, idx) => (
                                    <tr key={idx} class="hover:bg-slate-800/30 transition-colors">
                                        <td class="py-2.5 px-3 text-slate-400 whitespace-nowrap">{log.timestamp}</td>
                                        <td class="py-2.5 px-3 font-sans text-slate-200">{log.message}</td>
                                        <td class="py-2.5 px-3 text-right whitespace-nowrap">
                                            <span class={`px-2 py-0.5 rounded text-[0.65rem] font-bold ${
                                                log.state.includes('Surplus')
                                                    ? 'bg-cyan-500/10 text-cyan-400 border border-cyan-500/30'
                                                    : 'bg-slate-800 text-slate-400 border border-slate-700'
                                            }`}>
                                                {log.state}
                                            </span>
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}
            </div>

            {/* ── CONFIGURE SYSTEM MODAL ────────────────────────────────────── */}
            {showConfigModal && (
                <div class="fixed inset-0 z-50 bg-slate-950/80 backdrop-blur-sm flex items-center justify-center p-4">
                    <div class="bg-slate-900 border border-slate-800 rounded-3xl p-6 max-w-lg w-full shadow-2xl space-y-5">
                        <div class="flex items-center justify-between border-b border-slate-800 pb-4">
                            <h3 class="text-base font-bold text-slate-100 flex items-center gap-2">
                                <i class="fas fa-sliders-h text-cyan-400"></i>
                                <span>{t('ems_configure_system')}</span>
                            </h3>
                            <button
                                onClick={() => setShowConfigModal(false)}
                                class="w-8 h-8 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-400 flex items-center justify-center text-xs"
                            >
                                <i class="fas fa-times"></i>
                            </button>
                        </div>

                        <form onSubmit={handleSaveSystemConfig} class="space-y-4 text-xs">
                            <div>
                                <label class="block font-bold text-slate-300 mb-1">{t('ems_grid_limit')} (kW)</label>
                                <input
                                    type="number"
                                    step="0.5"
                                    min="1"
                                    max="100"
                                    value={gridMaxKw}
                                    onInput={(e: any) => setGridMaxKw(parseFloat(e.target.value) || 8.0)}
                                    class="w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-100 font-bold focus:border-cyan-500 outline-none"
                                />
                                <span class="text-[0.65rem] text-slate-500 mt-1 block">Contract/fuse ceiling</span>
                            </div>

                            {/* Battery Storage Options */}
                            <div class="bg-slate-850/80 border border-slate-800 rounded-2xl p-3.5 space-y-3">
                                <div class="flex items-center justify-between">
                                    <div class="flex items-center gap-2">
                                        <input
                                            type="checkbox"
                                            id="hasBattery"
                                            checked={hasBattery}
                                            onChange={(e: any) => setHasBattery(e.target.checked)}
                                            class="w-4 h-4 rounded text-cyan-500 focus:ring-0 bg-slate-800 border-slate-700 cursor-pointer"
                                        />
                                        <label for="hasBattery" class="font-bold text-slate-200 cursor-pointer">
                                            {t('ems_has_battery')}
                                        </label>
                                    </div>
                                    <span class="text-[0.65rem] text-slate-500">{t('ems_has_battery_desc')}</span>
                                </div>

                                {hasBattery && (
                                    <div class="grid grid-cols-2 gap-3 pt-2 border-t border-slate-800">
                                        <div>
                                            <label class="block font-bold text-slate-300 mb-1">{t('ems_battery_max_power')}</label>
                                            <input
                                                type="number"
                                                step="0.5"
                                                min="1"
                                                max="50"
                                                value={battMaxKw}
                                                onInput={(e: any) => setBattMaxKw(parseFloat(e.target.value) || 5.0)}
                                                class="w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-100 font-bold focus:border-cyan-500 outline-none"
                                            />
                                            <span class="text-[0.65rem] text-slate-500 mt-1 block">Max rating (5.0 kW)</span>
                                        </div>

                                        <div>
                                            <label class="block font-bold text-slate-300 mb-1">{t('ems_reserve_headroom')} (kW)</label>
                                            <input
                                                type="number"
                                                step="0.5"
                                                min="0"
                                                max="10"
                                                value={battReserveKw}
                                                onInput={(e: any) => setBattReserveKw(parseFloat(e.target.value) || 1.0)}
                                                class="w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-100 font-bold focus:border-cyan-500 outline-none"
                                            />
                                            <span class="text-[0.65rem] text-slate-500 mt-1 block">Trickle buffer (1.0 kW)</span>
                                        </div>
                                    </div>
                                )}
                            </div>

                            <div class="space-y-3 pt-2 border-t border-slate-800">
                                <div class="text-[0.7rem] uppercase tracking-wider text-slate-400 font-bold">Telemetry Key Mappings</div>

                                <div>
                                    <label class="block text-slate-400 mb-1">Grid Power Key</label>
                                    <input
                                        type="text"
                                        value={gridKey}
                                        onInput={(e: any) => setGridKey(e.target.value)}
                                        class="w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-200 font-mono focus:border-cyan-500 outline-none"
                                    />
                                </div>

                                <div>
                                    <label class="block text-slate-400 mb-1">PV Solar Power Key</label>
                                    <input
                                        type="text"
                                        value={pvKey}
                                        onInput={(e: any) => setPvKey(e.target.value)}
                                        class="w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-200 font-mono focus:border-cyan-500 outline-none"
                                    />
                                </div>

                                {hasBattery && (
                                    <>
                                        <div>
                                            <label class="block text-slate-400 mb-1">{t('ems_battery_power_key')}</label>
                                            <input
                                                type="text"
                                                value={battPowerKey}
                                                onInput={(e: any) => setBattPowerKey(e.target.value)}
                                                class="w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-200 font-mono focus:border-cyan-500 outline-none"
                                            />
                                        </div>
                                        <div>
                                            <label class="block text-slate-400 mb-1">{t('ems_battery_soc_key')}</label>
                                            <input
                                                type="text"
                                                value={battSocKey}
                                                onInput={(e: any) => setBattSocKey(e.target.value)}
                                                class="w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-200 font-mono focus:border-cyan-500 outline-none"
                                            />
                                        </div>
                                    </>
                                )}
                            </div>

                            <div class="flex items-center justify-end gap-3 pt-4 border-t border-slate-800">
                                <button
                                    type="button"
                                    onClick={() => setShowConfigModal(false)}
                                    class="px-4 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-300 font-bold transition-all"
                                >
                                    {t('ems_cancel')}
                                </button>
                                <button
                                    type="submit"
                                    disabled={savingConfig}
                                    class="px-5 py-2 rounded-xl bg-cyan-500 hover:bg-cyan-400 text-slate-950 font-bold transition-all shadow-md flex items-center gap-2"
                                >
                                    {savingConfig && <i class="fas fa-spinner fa-spin"></i>}
                                    {t('ems_save_changes')}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}

            {/* ── ADD/EDIT CONSUMER MODAL ────────────────────────────────────── */}
            {showConsumerModal && editingConsumer && (
                <div class="fixed inset-0 z-50 bg-slate-950/80 backdrop-blur-sm flex items-center justify-center p-4">
                    <div class="bg-slate-900 border border-slate-800 rounded-3xl p-6 max-w-lg w-full shadow-2xl space-y-5">
                        <div class="flex items-center justify-between border-b border-slate-800 pb-4">
                            <h3 class="text-base font-bold text-slate-100 flex items-center gap-2">
                                <i class={`fas ${editingConsumer.id ? 'fa-edit' : 'fa-plus-circle'} text-cyan-400`}></i>
                                <span>{editingConsumer.id ? t('ems_edit_consumer') : t('ems_add_consumer')}</span>
                                {editingConsumer.name && (
                                    <span class="text-xs text-slate-400 font-mono">({editingConsumer.name})</span>
                                )}
                            </h3>
                            <button
                                onClick={() => setShowConsumerModal(false)}
                                class="w-8 h-8 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-400 flex items-center justify-center text-xs"
                            >
                                <i class="fas fa-times"></i>
                            </button>
                        </div>

                        <form onSubmit={handleSaveConsumer} class="space-y-4 text-xs">
                            <div class="grid grid-cols-2 gap-4">
                                <div>
                                    <label class="block font-bold text-slate-300 mb-1">{t('ems_consumer_name')}</label>
                                    <input
                                        type="text"
                                        required
                                        value={editingConsumer.name || ''}
                                        onInput={(e: any) => setEditingConsumer({ ...editingConsumer, name: e.target.value })}
                                        placeholder="e.g. Garage Wallbox 1"
                                        class="w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-100 font-bold focus:border-cyan-500 outline-none"
                                    />
                                </div>

                                <div>
                                    <label class="block font-bold text-slate-300 mb-1">{t('ems_priority')}</label>
                                    <input
                                        type="number"
                                        min="1"
                                        max="10"
                                        value={editingConsumer.priority ?? 1}
                                        onInput={(e: any) => setEditingConsumer({ ...editingConsumer, priority: parseInt(e.target.value) || 1 })}
                                        class="w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-100 font-bold focus:border-cyan-500 outline-none"
                                    />
                                    <span class="text-[0.65rem] text-slate-500 mt-0.5 block">1 = Highest Priority</span>
                                </div>
                            </div>

                            {/* Tier 1: Uncontrollable Base Power */}
                            <div class="bg-slate-850/80 border border-slate-800 rounded-2xl p-3.5 space-y-2">
                                <div class="flex items-center justify-between">
                                    <label class="font-bold text-slate-200">{t('ems_base_tier')}</label>
                                    <span class="text-[0.65rem] text-purple-400 font-mono">Tier 1 (Base Load)</span>
                                </div>
                                <div class="flex items-center gap-3">
                                    <input
                                        type="number"
                                        step="0.1"
                                        min="0"
                                        value={editingConsumer.basePowerKw ?? 0.0}
                                        onInput={(e: any) => setEditingConsumer({ ...editingConsumer, basePowerKw: parseFloat(e.target.value) || 0.0 })}
                                        class="w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-100 font-bold focus:border-cyan-500 outline-none"
                                    />
                                    <span class="text-xs font-bold text-slate-400">kW</span>
                                </div>
                                <span class="text-[0.65rem] text-slate-500 block">
                                    Essential continuous load drawn without curtailment (e.g. electronics, compressor).
                                </span>
                            </div>

                            {/* Tier 2: Optional Controllable Tier */}
                            <div class="bg-slate-850/80 border border-slate-800 rounded-2xl p-3.5 space-y-3">
                                <div class="flex items-center justify-between">
                                    <div class="flex items-center gap-2">
                                        <input
                                            type="checkbox"
                                            id="hasOptionalTier"
                                            checked={editingConsumer.hasOptionalTier ?? true}
                                            onChange={(e: any) => setEditingConsumer({ ...editingConsumer, hasOptionalTier: e.target.checked, isControllable: e.target.checked })}
                                            class="w-4 h-4 rounded text-cyan-500 focus:ring-0 bg-slate-800 border-slate-700"
                                        />
                                        <label for="hasOptionalTier" class="font-bold text-slate-200 cursor-pointer">
                                            {t('ems_optional_tier')}
                                        </label>
                                    </div>
                                    <span class="text-[0.65rem] text-cyan-400 font-mono">Tier 2 (Modulatable)</span>
                                </div>

                                {editingConsumer.hasOptionalTier && (
                                    <div class="space-y-3 pt-2 border-t border-slate-800">
                                        <div class="grid grid-cols-3 gap-3">
                                            <div>
                                                <label class="block font-bold text-slate-300 mb-1">{t('ems_max_power')} (kW)</label>
                                                <input
                                                    type="number"
                                                    step="0.5"
                                                    value={editingConsumer.maxOptionalKw ?? editingConsumer.baseLimitKw ?? 8.0}
                                                    onInput={(e: any) => setEditingConsumer({ ...editingConsumer, maxOptionalKw: parseFloat(e.target.value) || 8.0 })}
                                                    class="w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-100 font-bold focus:border-cyan-500 outline-none"
                                                />
                                                <span class="text-[0.6rem] text-slate-500 mt-0.5 block">Max optional quota</span>
                                            </div>

                                            <div>
                                                <label class="block font-bold text-slate-300 mb-1">{t('ems_min_power')} (kW)</label>
                                                <input
                                                    type="number"
                                                    step="0.1"
                                                    value={editingConsumer.minOptionalKw ?? editingConsumer.minPowerKw ?? 0.0}
                                                    onInput={(e: any) => setEditingConsumer({ ...editingConsumer, minOptionalKw: parseFloat(e.target.value) || 0.0 })}
                                                    class="w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-100 font-bold focus:border-cyan-500 outline-none"
                                                />
                                                <span class="text-[0.6rem] text-slate-500 mt-0.5 block">Min operating threshold</span>
                                            </div>

                                            <div>
                                                <label class="block font-bold text-slate-300 mb-1">{t('ems_standby_power')}</label>
                                                <input
                                                    type="number"
                                                    step="0.1"
                                                    value={editingConsumer.standbyOptionalKw ?? 0.0}
                                                    onInput={(e: any) => setEditingConsumer({ ...editingConsumer, standbyOptionalKw: parseFloat(e.target.value) || 0.0 })}
                                                    class="w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-100 font-bold focus:border-cyan-500 outline-none"
                                                />
                                                <span class="text-[0.6rem] text-slate-500 mt-0.5 block">Standby when idle</span>
                                            </div>
                                        </div>

                                        <div>
                                            <label class="block text-slate-400 mb-1">Force Power Key (Setpoint write)</label>
                                            <input
                                                type="text"
                                                value={editingConsumer.forcePowerKey || ''}
                                                onInput={(e: any) => setEditingConsumer({ ...editingConsumer, forcePowerKey: e.target.value })}
                                                placeholder="e.g. wallbox-sim-01_force_power"
                                                class="w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-200 font-mono focus:border-cyan-500 outline-none"
                                            />
                                        </div>
                                    </div>
                                )}
                            </div>

                            <div>
                                <label class="block text-slate-400 mb-1">Actual Power Key (Telemetry read)</label>
                                <input
                                    type="text"
                                    value={editingConsumer.actualPowerKey || ''}
                                    onInput={(e: any) => setEditingConsumer({ ...editingConsumer, actualPowerKey: e.target.value })}
                                    placeholder="e.g. wallbox-sim-01_power"
                                    class="w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-slate-200 font-mono focus:border-cyan-500 outline-none"
                                />
                            </div>

                            <div class="flex items-center justify-end gap-3 pt-4 border-t border-slate-800">
                                <button
                                    type="button"
                                    onClick={() => setShowConsumerModal(false)}
                                    class="px-4 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-300 font-bold transition-all"
                                >
                                    {t('ems_cancel')}
                                </button>
                                <button
                                    type="submit"
                                    disabled={savingConsumer}
                                    class="px-5 py-2 rounded-xl bg-cyan-500 hover:bg-cyan-400 text-slate-950 font-bold transition-all shadow-md flex items-center gap-2"
                                >
                                    {savingConsumer && <i class="fas fa-spinner fa-spin"></i>}
                                    {editingConsumer.id ? t('ems_save_changes') : t('ems_add_consumer')}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    );
}

export const TrajectoryPage = EmsPage;
