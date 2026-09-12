import { jsx as _jsx, jsxs as _jsxs } from "preact/jsx-runtime";
import { useState, useEffect } from 'preact/hooks';
import { t } from '../i18n';
export function WallboxesPage() {
    const [wallboxes, setWallboxes] = useState([]);
    const [rfids, setRfids] = useState([]);
    const [forcePower, setForcePower] = useState(0);
    const [isEditingForcePower, setIsEditingForcePower] = useState(false);
    const [editForcePowerValue, setEditForcePowerValue] = useState('0');
    const [savingForcePower, setSavingForcePower] = useState(false);
    const [loading, setLoading] = useState(true);
    const [submitting, setSubmitting] = useState(null);
    const [showStartModal, setShowStartModal] = useState(null); // holds chargePointId
    const [selectedRfid, setSelectedRfid] = useState('');
    const [customRfid, setCustomRfid] = useState('');
    const fetchWallboxes = async () => {
        try {
            const res = await fetch('/plswk/api/wallboxes');
            if (res.ok) {
                const data = await res.json();
                setWallboxes(data);
            }
        }
        catch (e) {
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
        }
        catch (e) {
            console.error("Failed to fetch RFIDs:", e);
        }
    };
    const fetchForcePower = async () => {
        try {
            const res = await fetch('/plswk/api/wallboxes/force-power');
            if (res.ok) {
                const data = await res.json();
                setForcePower(data.forcePower ?? 0);
            }
        }
        catch (e) {
            console.error("Failed to fetch force_power:", e);
        }
    };
    useEffect(() => {
        const init = async () => {
            await Promise.all([fetchWallboxes(), fetchRfids(), fetchForcePower()]);
            setLoading(false);
        };
        init();
        const interval = setInterval(() => {
            fetchWallboxes();
            if (!isEditingForcePower) {
                fetchForcePower();
            }
        }, 3000);
        return () => clearInterval(interval);
    }, [isEditingForcePower]);
    const handleSaveForcePower = async () => {
        const cleanStr = (editForcePowerValue || '').toString().trim().replace(',', '.');
        const val = parseFloat(cleanStr);
        if (isNaN(val) || val < 0) {
            alert("Please enter a valid power limit in kW (>= 0).");
            return;
        }
        setSavingForcePower(true);
        try {
            const res = await fetch('/plswk/api/wallboxes/force-power', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ forcePower: val })
            });
            if (res.ok) {
                setForcePower(val);
                setIsEditingForcePower(false);
                fetchWallboxes();
            }
            else {
                const errText = await res.text().catch(() => '');
                alert(`Failed to update force_power: ${errText || res.statusText || 'Error'}`);
            }
        }
        catch (e) {
            console.error("Failed to save force_power:", e);
            alert("Error communicating with server.");
        }
        finally {
            setSavingForcePower(false);
        }
    };
    const handleCommand = async (chargepointId, command, extra) => {
        setSubmitting(`${chargepointId}-${command}`);
        try {
            const body = {
                chargePointId: chargepointId,
                command: command
            };
            if (command === 'start') {
                body.rfidTag = extra?.rfid || 'RemoteUser';
            }
            else if (command === 'stop') {
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
                }
                else {
                    alert(`Failed to execute ${command} command.`);
                }
            }
            else {
                alert(`Error: ${res.statusText}`);
            }
        }
        catch (e) {
            console.error(e);
            alert("Error communicating with server.");
        }
        finally {
            setSubmitting(null);
            setShowStartModal(null);
            fetchWallboxes();
        }
    };
    if (loading) {
        return (_jsx("div", { class: "h-full flex items-center justify-center p-16", children: _jsxs("div", { class: "flex flex-col items-center gap-4 text-cyan-400", children: [_jsx("i", { class: "fas fa-spinner fa-spin text-3xl" }), _jsx("p", { class: "text-sm font-medium animate-pulse", children: t('loading') })] }) }));
    }
    return (_jsxs("div", { class: "flex flex-col gap-6 w-full page-enter", children: [_jsxs("div", { class: "grid grid-cols-2 lg:grid-cols-4 gap-2.5 sm:gap-4", children: [_jsxs("div", { class: "glass p-3.5 sm:p-5 rounded-xl border border-slate-700/60 flex items-center justify-between", children: [_jsxs("div", { children: [_jsx("div", { class: "text-2xl font-black text-emerald-400", children: wallboxes.filter(w => w.connected).length }), _jsx("div", { class: "text-[0.65rem] sm:text-[0.7rem] uppercase tracking-wider text-slate-400 font-bold mt-0.5 sm:mt-1", children: t('wb_online') })] }), _jsx("div", { class: "w-8 h-8 sm:w-10 sm:h-10 rounded-lg bg-emerald-400/10 text-emerald-400 flex items-center justify-center text-sm sm:text-lg shrink-0", children: _jsx("i", { class: "fas fa-link" }) })] }), _jsxs("div", { class: "glass p-3.5 sm:p-5 rounded-xl border border-slate-700/60 flex items-center justify-between", children: [_jsxs("div", { children: [_jsx("div", { class: "text-2xl font-black text-amber-400", children: wallboxes.filter(w => w.status === 'Charging').length }), _jsx("div", { class: "text-[0.65rem] sm:text-[0.7rem] uppercase tracking-wider text-slate-400 font-bold mt-0.5 sm:mt-1", children: t('wb_active_charging') })] }), _jsx("div", { class: "w-8 h-8 sm:w-10 sm:h-10 rounded-lg bg-amber-400/10 text-amber-400 flex items-center justify-center text-sm sm:text-lg shrink-0", children: _jsx("i", { class: "fas fa-bolt animate-pulse" }) })] }), _jsxs("div", { class: "glass p-3.5 sm:p-5 rounded-xl border border-slate-700/60 flex items-center justify-between", children: [_jsxs("div", { children: [_jsxs("div", { class: "text-2xl font-black text-sky-400", children: [wallboxes.reduce((acc, curr) => acc + curr.power, 0).toFixed(1), " kW"] }), _jsx("div", { class: "text-[0.65rem] sm:text-[0.7rem] uppercase tracking-wider text-slate-400 font-bold mt-0.5 sm:mt-1", children: t('wb_total_power') })] }), _jsx("div", { class: "w-8 h-8 sm:w-10 sm:h-10 rounded-lg bg-sky-400/10 text-sky-400 flex items-center justify-center text-sm sm:text-lg shrink-0", children: _jsx("i", { class: "fas fa-plug" }) })] }), _jsxs("div", { class: "glass p-3.5 sm:p-5 rounded-xl border border-slate-700/60 flex items-center justify-between group hover:border-cyan-500/40 transition-colors", "data-testid": "force-power-card", children: [_jsx("div", { class: "flex-1 min-w-0 pr-2", children: isEditingForcePower ? (_jsxs("div", { class: "flex flex-col gap-1.5", onClick: e => e.stopPropagation(), children: [_jsxs("div", { class: "flex items-center gap-2", children: [_jsx("input", { type: "number", step: "0.5", min: "0", max: "100", class: "w-24 bg-slate-900 border border-cyan-500/70 rounded-lg px-2.5 py-1 text-lg font-black text-cyan-300 focus:outline-none focus:ring-2 focus:ring-cyan-400", value: editForcePowerValue, onInput: e => setEditForcePowerValue(e.currentTarget.value), onKeyDown: e => {
                                                        if (e.key === 'Enter')
                                                            handleSaveForcePower();
                                                        if (e.key === 'Escape')
                                                            setIsEditingForcePower(false);
                                                    }, autoFocus: true }), _jsx("span", { class: "text-xs font-bold text-slate-400", children: "kW" }), _jsx("button", { onClick: handleSaveForcePower, disabled: savingForcePower, title: t('save'), class: "w-8 h-8 rounded-lg bg-cyan-500 hover:bg-cyan-400 text-slate-950 flex items-center justify-center text-xs font-bold transition-all shadow-sm disabled:opacity-50", children: _jsx("i", { class: `fas ${savingForcePower ? 'fa-spinner fa-spin' : 'fa-check'}` }) }), _jsx("button", { onClick: () => setIsEditingForcePower(false), title: t('cancel'), class: "w-8 h-8 rounded-lg bg-slate-700 hover:bg-slate-600 text-slate-300 flex items-center justify-center text-xs transition-all", children: _jsx("i", { class: "fas fa-times" }) })] }), _jsx("div", { class: "text-[0.65rem] text-slate-400 font-medium", children: t('wb_force_power_hint') })] })) : (_jsxs("div", { class: "cursor-pointer select-none", onClick: () => {
                                        setEditForcePowerValue(forcePower > 0 ? forcePower.toString() : '0');
                                        setIsEditingForcePower(true);
                                    }, children: [_jsxs("div", { class: "flex items-baseline gap-2", children: [_jsx("span", { class: `text-2xl font-black ${forcePower > 0 ? 'text-amber-400' : 'text-slate-100'}`, "data-testid": "force-power-value", children: forcePower > 0 ? `${forcePower.toFixed(1)} kW` : t('wb_force_power_unrestricted') }), _jsx("button", { type: "button", class: "text-xs text-slate-400 hover:text-cyan-400 opacity-60 group-hover:opacity-100 transition-opacity", title: t('edit'), children: _jsx("i", { class: "fas fa-pencil-alt" }) })] }), _jsx("div", { class: "text-[0.65rem] sm:text-[0.7rem] uppercase tracking-wider text-slate-400 font-bold mt-0.5 sm:mt-1", children: t('wb_force_power') })] })) }), _jsx("div", { onClick: () => {
                                    if (!isEditingForcePower) {
                                        setEditForcePowerValue(forcePower > 0 ? forcePower.toString() : '0');
                                        setIsEditingForcePower(true);
                                    }
                                }, class: `w-8 h-8 sm:w-10 sm:h-10 rounded-lg flex items-center justify-center text-sm sm:text-lg shrink-0 cursor-pointer transition-colors ${forcePower > 0 ? 'bg-amber-400/10 text-amber-400' : 'bg-cyan-400/10 text-cyan-400'}`, title: t('edit'), children: _jsx("i", { class: "fas fa-tachometer-alt" }) })] })] }), _jsx("div", { class: "grid grid-cols-1 lg:grid-cols-2 gap-4 sm:gap-6", children: wallboxes.map(wb => {
                    const isBusy = submitting !== null;
                    return (_jsxs("div", { class: "bg-slate-800 border border-slate-700/70 rounded-xl overflow-hidden flex flex-col shadow-lg transition-transform duration-150 hover:scale-[1.01]", children: [_jsxs("div", { class: "px-4 sm:px-6 py-3.5 sm:py-4 border-b border-slate-700/70 flex justify-between items-center bg-black/10", children: [_jsxs("div", { children: [_jsx("h3", { class: "font-bold text-slate-100 text-base", children: wb.name }), _jsx("p", { class: "text-xs text-slate-400 font-mono mt-0.5", children: wb.id })] }), _jsx("div", { class: "flex items-center gap-2", children: _jsx("span", { class: `text-xs font-bold uppercase tracking-wider px-2 py-0.5 rounded ${wb.connected ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20' : 'bg-red-500/10 text-red-400 border border-red-500/20'}`, children: wb.connected ? t('wb_online') : t('status_offline').charAt(0).toUpperCase() + t('status_offline').slice(1) }) })] }), _jsxs("div", { class: "p-4 sm:p-6 grid grid-cols-2 gap-3 sm:gap-4 flex-1", children: [_jsxs("div", { class: "flex flex-col gap-1", children: [_jsx("span", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wider", children: t('wb_status') }), _jsxs("span", { class: `text-sm font-semibold flex items-center gap-1.5 ${wb.status === 'Charging' ? 'text-amber-400' :
                                                    wb.status === 'Available' ? 'text-emerald-400' :
                                                        wb.status === 'Preparing' ? 'text-sky-400' : 'text-slate-300'}`, children: [_jsx("span", { class: `w-2 h-2 rounded-full ${wb.status === 'Charging' ? 'bg-amber-400 animate-pulse' :
                                                            wb.status === 'Available' ? 'bg-emerald-400' :
                                                                wb.status === 'Preparing' ? 'bg-sky-400' : 'bg-slate-500'}` }), wb.status] })] }), _jsxs("div", { class: "flex flex-col gap-1", children: [_jsx("span", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wider", children: t('wb_active_user') }), _jsx("span", { class: "text-sm font-semibold text-slate-100 font-mono truncate", children: wb.activeUser })] }), _jsxs("div", { class: "flex flex-col gap-1", children: [_jsx("span", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wider", children: t('wb_power') }), _jsxs("span", { class: "text-lg font-black text-sky-400", children: [wb.power.toFixed(2), " kW"] })] }), _jsxs("div", { class: "flex flex-col gap-1", children: [_jsx("span", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wider", children: t('wb_total_energy') }), _jsxs("span", { class: "text-sm font-semibold text-slate-100 font-mono", children: [wb.energyImport.toFixed(1), " kWh"] })] }), _jsxs("div", { class: "flex flex-col gap-1 col-span-2 border-t border-slate-700/40 pt-3 mt-1 grid grid-cols-2 gap-4", children: [_jsxs("div", { children: [_jsx("span", { class: "text-[0.65rem] font-bold text-slate-500 uppercase tracking-wider", children: t('wb_curr_volt') }), _jsxs("div", { class: "text-xs font-semibold text-slate-300 mt-0.5", children: [wb.current.toFixed(1), " A \u00A0\u00B7\u00A0 ", wb.voltage.toFixed(0), " V \u00A0\u00B7\u00A0 ", wb.phases || 3, "P"] })] }), wb.status === 'Charging' && (_jsxs("div", { class: "flex flex-col gap-1", children: [_jsxs("div", { class: "flex justify-between items-center text-[0.65rem] font-bold text-slate-500 uppercase tracking-wider", children: [_jsx("span", { children: t('wb_charge_rate') }), _jsxs("span", { class: "text-amber-400", children: [((wb.power / 11) * 100).toFixed(0), "%"] })] }), _jsx("div", { class: "w-full bg-slate-900 rounded-full h-2 overflow-hidden border border-slate-700/50 mt-1", children: _jsx("div", { class: "bg-gradient-to-r from-amber-500 to-yellow-400 h-full rounded-full transition-all duration-500", style: { width: `${Math.min(100, (wb.power / 11) * 100)}%` } }) })] }))] })] }), _jsxs("div", { class: "px-4 sm:px-6 py-3 sm:py-4 border-t border-slate-700/60 bg-black/10 flex items-center justify-end gap-2", children: [_jsxs("button", { onClick: () => handleCommand(wb.id, 'unlock'), disabled: isBusy || !wb.connected, class: "py-2 sm:py-1.5 px-3 rounded-lg border border-slate-600 bg-slate-700/40 text-slate-200 text-xs font-semibold hover:bg-slate-700 hover:border-slate-500 disabled:opacity-40 disabled:pointer-events-none transition-colors min-h-[44px] flex items-center justify-center", children: [_jsx("i", { class: "fas fa-key mr-1.5" }), t('wb_unlock')] }), wb.status === 'Charging' ? (_jsxs("button", { onClick: () => handleCommand(wb.id, 'stop'), disabled: isBusy || !wb.connected, class: "py-2 sm:py-1.5 px-4 sm:px-3.5 rounded-lg border border-red-500/30 bg-red-500/10 text-red-400 text-xs font-semibold hover:bg-red-500/20 disabled:opacity-40 disabled:pointer-events-none transition-colors min-h-[44px] flex items-center justify-center", children: [_jsx("i", { class: "fas fa-stop mr-1.5" }), t('wb_stop_charge')] })) : (_jsxs("button", { onClick: () => {
                                            setSelectedRfid(rfids.length > 0 ? rfids[0].idTag : '');
                                            setCustomRfid('');
                                            setShowStartModal(wb.id);
                                        }, disabled: isBusy || !wb.connected, class: "py-2 sm:py-1.5 px-4 sm:px-3.5 rounded-lg border border-emerald-500/30 bg-emerald-500/10 text-emerald-400 text-xs font-semibold hover:bg-emerald-500/20 disabled:opacity-40 disabled:pointer-events-none transition-colors min-h-[44px] flex items-center justify-center", children: [_jsx("i", { class: "fas fa-play mr-1.5" }), t('wb_start_charge')] }))] })] }, wb.id));
                }) }), showStartModal !== null && (_jsx("div", { class: "fixed inset-0 bg-black/70 backdrop-blur-sm flex items-end sm:items-center justify-center z-[100] p-0 sm:p-4 animate-fade-in", "data-testid": "start-charge-modal", onClick: () => setShowStartModal(null), children: _jsxs("div", { class: "bg-slate-800 border border-slate-700 rounded-t-3xl sm:rounded-xl shadow-2xl max-w-md w-full p-5 sm:p-6 text-slate-100 flex flex-col gap-4 animate-slide-up-mobile sm:animate-fade-in pb-safe", onClick: (e) => e.stopPropagation(), children: [_jsxs("div", { class: "flex justify-between items-center border-b border-slate-700 pb-3", children: [_jsx("h3", { class: "font-bold text-base sm:text-lg text-slate-50", children: t('wb_auth_title') }), _jsx("button", { onClick: () => setShowStartModal(null), class: "w-8 h-8 rounded-lg bg-slate-700/50 text-slate-400 hover:text-slate-200 flex items-center justify-center cursor-pointer", children: _jsx("i", { class: "fas fa-times text-base" }) })] }), _jsxs("div", { class: "flex flex-col gap-3", children: [_jsxs("p", { class: "text-xs text-slate-400", children: [t('wb_auth_desc').split('{0}')[0], _jsx("strong", { children: showStartModal }), t('wb_auth_desc').split('{0}')[1] || ''] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide", children: t('wb_known_users') }), _jsxs("select", { value: selectedRfid, onChange: (e) => setSelectedRfid(e.target.value), class: "bg-slate-900 border border-slate-700 rounded-lg px-3 py-2 text-sm text-slate-100 focus:outline-none focus:border-sky-400 font-mono", children: [_jsx("option", { value: "", children: t('wb_custom_card') }), rfids.map(r => (_jsxs("option", { value: r.idTag, children: [r.userName, " (", r.idTag, ")"] }, r.idTag)))] }), rfids.length > 0 && (_jsx("div", { class: "flex flex-wrap gap-1.5 mt-1", children: rfids.map(r => (_jsxs("button", { type: "button", onClick: () => setSelectedRfid(r.idTag), class: `text-[0.68rem] px-2.5 py-1 rounded-md font-mono border transition-all ${selectedRfid === r.idTag
                                                    ? 'bg-sky-500/20 border-sky-400/40 text-sky-300 font-bold'
                                                    : 'bg-slate-900 border-slate-700 text-slate-400 hover:border-slate-500'}`, children: [_jsx("i", { class: "fas fa-id-badge mr-1 opacity-70" }), r.userName] }, r.idTag))) }))] }), selectedRfid === '' && (_jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide", children: t('wb_custom_rfid') }), _jsx("input", { type: "text", value: customRfid, onInput: (e) => setCustomRfid(e.target.value), placeholder: "e.g. A1B2C3D4", class: "bg-slate-900 border border-slate-700 rounded-lg px-3 py-2 text-sm text-slate-100 focus:outline-none focus:border-sky-400 font-mono" })] }))] }), _jsxs("div", { class: "flex items-center justify-end gap-2 border-t border-slate-700 pt-3 mt-2", children: [_jsx("button", { onClick: () => setShowStartModal(null), class: "py-2.5 px-4 rounded-lg bg-slate-700 text-slate-200 text-xs font-semibold hover:bg-slate-600 transition-colors min-h-[44px] flex items-center justify-center cursor-pointer", children: t('btn_cancel') }), _jsx("button", { onClick: () => handleCommand(showStartModal, 'start', { rfid: selectedRfid || customRfid }), disabled: selectedRfid === '' && !customRfid, class: "py-2.5 px-5 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 disabled:opacity-40 disabled:pointer-events-none transition-colors min-h-[44px] flex items-center justify-center cursor-pointer", children: t('wb_auth_start') })] })] }) }))] }));
}
