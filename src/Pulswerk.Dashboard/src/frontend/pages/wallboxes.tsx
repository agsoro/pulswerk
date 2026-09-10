import { useState, useEffect } from 'preact/hooks';
import { t } from '../i18n';

interface Wallbox {
    id: string;
    name: string;
    connected: boolean;
    status: string;
    power: number;
    energyImport: number;
    current: number;
    voltage: number;
    phases?: number;
    activeUser: string;
}

interface RfidMapping {
    idTag: string;
    userName: string;
}

export function WallboxesPage() {
    const [wallboxes, setWallboxes] = useState<Wallbox[]>([]);
    const [rfids, setRfids] = useState<RfidMapping[]>([]);
    const [loading, setLoading] = useState(true);
    const [submitting, setSubmitting] = useState<string | null>(null);
    const [showStartModal, setShowStartModal] = useState<string | null>(null); // holds chargePointId
    const [selectedRfid, setSelectedRfid] = useState<string>('');
    const [customRfid, setCustomRfid] = useState<string>('');

    const fetchWallboxes = async () => {
        try {
            const res = await fetch('/plswk/api/wallboxes');
            if (res.ok) {
                const data = await res.json();
                setWallboxes(data);
            }
        } catch (e) {
            console.error("Failed to fetch wallboxes:", e);
        }
    };

    const fetchRfids = async () => {
        try {
            const res = await fetch('/plswk/api/billing/rfid');
            if (res.ok) {
                const data = await res.json();
                setRfids(data);
                if (data.length > 0) {
                    setSelectedRfid(data[0].idTag);
                }
            }
        } catch (e) {
            console.error("Failed to fetch RFIDs:", e);
        }
    };

    useEffect(() => {
        const init = async () => {
            await Promise.all([fetchWallboxes(), fetchRfids()]);
            setLoading(false);
        };
        init();

        const interval = setInterval(fetchWallboxes, 3000);
        return () => clearInterval(interval);
    }, []);

    const handleCommand = async (chargepointId: string, command: 'start' | 'stop' | 'unlock', extra?: { rfid?: string }) => {
        setSubmitting(`${chargepointId}-${command}`);
        try {
            const body: any = {
                chargePointId: chargepointId,
                command: command
            };
            if (command === 'start') {
                body.rfidTag = extra?.rfid || 'RemoteUser';
            } else if (command === 'stop') {
                // In a real environment, we'd pass transaction ID.
                // Our API handles stop if we find an active transaction. 
                // We pass transactionId = 1 as placeholder or leave it to backend to lookup.
                body.transactionId = 1;
            }

            const res = await fetch('/plswk/api/wallboxes/command', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body)
            });

            if (res.ok) {
                const result = await res.json();
                if (result.success) {
                    alert(`${command.toUpperCase()} command sent successfully!`);
                } else {
                    alert(`Failed to execute ${command} command.`);
                }
            } else {
                alert(`Error: ${res.statusText}`);
            }
        } catch (e) {
            console.error(e);
            alert("Error communicating with server.");
        } finally {
            setSubmitting(null);
            setShowStartModal(null);
            fetchWallboxes();
        }
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

    return (
        <div class="flex flex-col gap-6 w-full page-enter">
            {/* Stats Summary cards */}
            <div class="grid grid-cols-2 lg:grid-cols-4 gap-2.5 sm:gap-4">
                <div class="glass p-3.5 sm:p-5 rounded-xl border border-slate-700/60 flex items-center justify-between">
                    <div>
                        <div class="text-2xl font-black text-slate-100">{wallboxes.length}</div>
                        <div class="text-[0.65rem] sm:text-[0.7rem] uppercase tracking-wider text-slate-400 font-bold mt-0.5 sm:mt-1">{t('wb_total_wb')}</div>
                    </div>
                    <div class="w-8 h-8 sm:w-10 sm:h-10 rounded-lg bg-sky-400/10 text-sky-400 flex items-center justify-center text-sm sm:text-lg shrink-0">
                        <i class="fas fa-charging-station"></i>
                    </div>
                </div>

                <div class="glass p-3.5 sm:p-5 rounded-xl border border-slate-700/60 flex items-center justify-between">
                    <div>
                        <div class="text-2xl font-black text-emerald-400">
                            {wallboxes.filter(w => w.connected).length}
                        </div>
                        <div class="text-[0.65rem] sm:text-[0.7rem] uppercase tracking-wider text-slate-400 font-bold mt-0.5 sm:mt-1">{t('wb_online')}</div>
                    </div>
                    <div class="w-8 h-8 sm:w-10 sm:h-10 rounded-lg bg-emerald-400/10 text-emerald-400 flex items-center justify-center text-sm sm:text-lg shrink-0">
                        <i class="fas fa-link"></i>
                    </div>
                </div>

                <div class="glass p-3.5 sm:p-5 rounded-xl border border-slate-700/60 flex items-center justify-between">
                    <div>
                        <div class="text-2xl font-black text-amber-400">
                            {wallboxes.filter(w => w.status === 'Charging').length}
                        </div>
                        <div class="text-[0.65rem] sm:text-[0.7rem] uppercase tracking-wider text-slate-400 font-bold mt-0.5 sm:mt-1">{t('wb_active_charging')}</div>
                    </div>
                    <div class="w-8 h-8 sm:w-10 sm:h-10 rounded-lg bg-amber-400/10 text-amber-400 flex items-center justify-center text-sm sm:text-lg shrink-0">
                        <i class="fas fa-bolt animate-pulse"></i>
                    </div>
                </div>

                <div class="glass p-3.5 sm:p-5 rounded-xl border border-slate-700/60 flex items-center justify-between">
                    <div>
                        <div class="text-2xl font-black text-sky-400">
                            {wallboxes.reduce((acc, curr) => acc + curr.power, 0).toFixed(1)} kW
                        </div>
                        <div class="text-[0.65rem] sm:text-[0.7rem] uppercase tracking-wider text-slate-400 font-bold mt-0.5 sm:mt-1">{t('wb_total_power')}</div>
                    </div>
                    <div class="w-8 h-8 sm:w-10 sm:h-10 rounded-lg bg-sky-400/10 text-sky-400 flex items-center justify-center text-sm sm:text-lg shrink-0">
                        <i class="fas fa-plug"></i>
                    </div>
                </div>
            </div>

            {/* Wallbox Grid */}
            <div class="grid grid-cols-1 lg:grid-cols-2 gap-4 sm:gap-6">
                {wallboxes.map(wb => {
                    const isBusy = submitting !== null;
                    return (
                        <div key={wb.id} class="bg-slate-800 border border-slate-700/70 rounded-xl overflow-hidden flex flex-col shadow-lg transition-transform duration-150 hover:scale-[1.01]">
                            {/* Card Header */}
                            <div class="px-4 sm:px-6 py-3.5 sm:py-4 border-b border-slate-700/70 flex justify-between items-center bg-black/10">
                                <div>
                                    <h3 class="font-bold text-slate-100 text-base">{wb.name}</h3>
                                    <p class="text-xs text-slate-400 font-mono mt-0.5">{wb.id}</p>
                                </div>
                                <div class="flex items-center gap-2">
                                    <span class={`text-xs font-bold uppercase tracking-wider px-2 py-0.5 rounded ${
                                        wb.connected ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20' : 'bg-red-500/10 text-red-400 border border-red-500/20'
                                    }`}>
                                        {wb.connected ? t('wb_online') : t('status_offline').charAt(0).toUpperCase() + t('status_offline').slice(1)}
                                    </span>
                                </div>
                            </div>

                            {/* Card Body */}
                            <div class="p-4 sm:p-6 grid grid-cols-2 gap-3 sm:gap-4 flex-1">
                                <div class="flex flex-col gap-1">
                                    <span class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wider">{t('wb_status')}</span>
                                    <span class={`text-sm font-semibold flex items-center gap-1.5 ${
                                        wb.status === 'Charging' ? 'text-amber-400' :
                                        wb.status === 'Available' ? 'text-emerald-400' :
                                        wb.status === 'Preparing' ? 'text-sky-400' : 'text-slate-300'
                                    }`}>
                                        <span class={`w-2 h-2 rounded-full ${
                                            wb.status === 'Charging' ? 'bg-amber-400 animate-pulse' :
                                            wb.status === 'Available' ? 'bg-emerald-400' :
                                            wb.status === 'Preparing' ? 'bg-sky-400' : 'bg-slate-500'
                                        }`}></span>
                                        {wb.status}
                                    </span>
                                </div>

                                <div class="flex flex-col gap-1">
                                    <span class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wider">{t('wb_active_user')}</span>
                                    <span class="text-sm font-semibold text-slate-100 font-mono truncate">{wb.activeUser}</span>
                                </div>

                                <div class="flex flex-col gap-1">
                                    <span class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wider">{t('wb_power')}</span>
                                    <span class="text-lg font-black text-sky-400">{wb.power.toFixed(2)} kW</span>
                                </div>

                                <div class="flex flex-col gap-1">
                                    <span class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wider">{t('wb_total_energy')}</span>
                                    <span class="text-sm font-semibold text-slate-100 font-mono">{wb.energyImport.toFixed(1)} kWh</span>
                                </div>

                                <div class="flex flex-col gap-1 col-span-2 border-t border-slate-700/40 pt-3 mt-1 grid grid-cols-2 gap-4">
                                    <div>
                                        <span class="text-[0.65rem] font-bold text-slate-500 uppercase tracking-wider">{t('wb_curr_volt')}</span>
                                        <div class="text-xs font-semibold text-slate-300 mt-0.5">{wb.current.toFixed(1)} A &nbsp;·&nbsp; {wb.voltage.toFixed(0)} V &nbsp;·&nbsp; {wb.phases || 3}P</div>
                                    </div>
                                    {wb.status === 'Charging' && (
                                        <div class="flex flex-col gap-1">
                                            <div class="flex justify-between items-center text-[0.65rem] font-bold text-slate-500 uppercase tracking-wider">
                                                <span>{t('wb_charge_rate')}</span>
                                                <span class="text-amber-400">{((wb.power / 11) * 100).toFixed(0)}%</span>
                                            </div>
                                            <div class="w-full bg-slate-900 rounded-full h-2 overflow-hidden border border-slate-700/50 mt-1">
                                                <div
                                                    class="bg-gradient-to-r from-amber-500 to-yellow-400 h-full rounded-full transition-all duration-500"
                                                    style={{ width: `${Math.min(100, (wb.power / 11) * 100)}%` }}
                                                ></div>
                                            </div>
                                        </div>
                                    )}
                                </div>
                            </div>

                            {/* Card Footer Actions */}
                            <div class="px-4 sm:px-6 py-3 sm:py-4 border-t border-slate-700/60 bg-black/10 flex items-center justify-end gap-2">
                                <button
                                    onClick={() => handleCommand(wb.id, 'unlock')}
                                    disabled={isBusy || !wb.connected}
                                    class="py-2 sm:py-1.5 px-3 rounded-lg border border-slate-600 bg-slate-700/40 text-slate-200 text-xs font-semibold hover:bg-slate-700 hover:border-slate-500 disabled:opacity-40 disabled:pointer-events-none transition-colors min-h-[44px] flex items-center justify-center"
                                >
                                    <i class="fas fa-key mr-1.5"></i>{t('wb_unlock')}
                                </button>
                                {wb.status === 'Charging' ? (
                                    <button
                                        onClick={() => handleCommand(wb.id, 'stop')}
                                        disabled={isBusy || !wb.connected}
                                        class="py-2 sm:py-1.5 px-4 sm:px-3.5 rounded-lg border border-red-500/30 bg-red-500/10 text-red-400 text-xs font-semibold hover:bg-red-500/20 disabled:opacity-40 disabled:pointer-events-none transition-colors min-h-[44px] flex items-center justify-center"
                                    >
                                        <i class="fas fa-stop mr-1.5"></i>{t('wb_stop_charge')}
                                    </button>
                                ) : (
                                    <button
                                        onClick={() => {
                                            setSelectedRfid(rfids.length > 0 ? rfids[0].idTag : '');
                                            setCustomRfid('');
                                            setShowStartModal(wb.id);
                                        }}
                                        disabled={isBusy || !wb.connected}
                                        class="py-2 sm:py-1.5 px-4 sm:px-3.5 rounded-lg border border-emerald-500/30 bg-emerald-500/10 text-emerald-400 text-xs font-semibold hover:bg-emerald-500/20 disabled:opacity-40 disabled:pointer-events-none transition-colors min-h-[44px] flex items-center justify-center"
                                    >
                                        <i class="fas fa-play mr-1.5"></i>{t('wb_start_charge')}
                                    </button>
                                )}
                            </div>
                        </div>
                    );
                })}
            </div>

            {/* Remote Start Modal / Bottom Sheet */}
            {showStartModal !== null && (
                <div class="fixed inset-0 bg-black/70 backdrop-blur-sm flex items-end sm:items-center justify-center z-[100] p-0 sm:p-4 animate-fade-in" data-testid="start-charge-modal" onClick={() => setShowStartModal(null)}>
                    <div 
                        class="bg-slate-800 border border-slate-700 rounded-t-3xl sm:rounded-xl shadow-2xl max-w-md w-full p-5 sm:p-6 text-slate-100 flex flex-col gap-4 animate-slide-up-mobile sm:animate-fade-in pb-safe"
                        onClick={(e) => e.stopPropagation()}
                    >
                        <div class="flex justify-between items-center border-b border-slate-700 pb-3">
                            <h3 class="font-bold text-base sm:text-lg text-slate-50">{t('wb_auth_title')}</h3>
                            <button onClick={() => setShowStartModal(null)} class="w-8 h-8 rounded-lg bg-slate-700/50 text-slate-400 hover:text-slate-200 flex items-center justify-center cursor-pointer">
                                <i class="fas fa-times text-base"></i>
                            </button>
                        </div>

                        <div class="flex flex-col gap-3">
                            <p class="text-xs text-slate-400">
                                {t('wb_auth_desc').split('{0}')[0]}<strong>{showStartModal}</strong>{t('wb_auth_desc').split('{0}')[1] || ''}
                            </p>
                            
                            <div class="flex flex-col gap-1.5">
                                <label class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide">{t('wb_known_users')}</label>
                                <select 
                                    value={selectedRfid}
                                    onChange={(e) => setSelectedRfid((e.target as HTMLSelectElement).value)}
                                    class="bg-slate-900 border border-slate-700 rounded-lg px-3 py-2 text-sm text-slate-100 focus:outline-none focus:border-sky-400 font-mono"
                                >
                                    <option value="">{t('wb_custom_card')}</option>
                                    {rfids.map(r => (
                                        <option key={r.idTag} value={r.idTag}>{r.userName} ({r.idTag})</option>
                                    ))}
                                </select>

                                {rfids.length > 0 && (
                                    <div class="flex flex-wrap gap-1.5 mt-1">
                                        {rfids.map(r => (
                                            <button 
                                                key={r.idTag}
                                                type="button"
                                                onClick={() => setSelectedRfid(r.idTag)}
                                                class={`text-[0.68rem] px-2.5 py-1 rounded-md font-mono border transition-all ${
                                                    selectedRfid === r.idTag 
                                                        ? 'bg-sky-500/20 border-sky-400/40 text-sky-300 font-bold' 
                                                        : 'bg-slate-900 border-slate-700 text-slate-400 hover:border-slate-500'
                                                }`}
                                            >
                                                <i class="fas fa-id-badge mr-1 opacity-70"></i>{r.userName}
                                            </button>
                                        ))}
                                    </div>
                                )}
                            </div>

                            {selectedRfid === '' && (
                                <div class="flex flex-col gap-1.5">
                                    <label class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide">{t('wb_custom_rfid')}</label>
                                    <input
                                        type="text"
                                        value={customRfid}
                                        onInput={(e) => setCustomRfid((e.target as HTMLInputElement).value)}
                                        placeholder="e.g. A1B2C3D4"
                                        class="bg-slate-900 border border-slate-700 rounded-lg px-3 py-2 text-sm text-slate-100 focus:outline-none focus:border-sky-400 font-mono"
                                    />
                                </div>
                            )}
                        </div>

                        <div class="flex items-center justify-end gap-2 border-t border-slate-700 pt-3 mt-2">
                            <button
                                onClick={() => setShowStartModal(null)}
                                class="py-2.5 px-4 rounded-lg bg-slate-700 text-slate-200 text-xs font-semibold hover:bg-slate-600 transition-colors min-h-[44px] flex items-center justify-center cursor-pointer"
                            >
                                {t('btn_cancel')}
                            </button>
                            <button
                                onClick={() => handleCommand(showStartModal, 'start', { rfid: selectedRfid || customRfid })}
                                disabled={selectedRfid === '' && !customRfid}
                                class="py-2.5 px-5 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 disabled:opacity-40 disabled:pointer-events-none transition-colors min-h-[44px] flex items-center justify-center cursor-pointer"
                            >
                                {t('wb_auth_start')}
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
}
