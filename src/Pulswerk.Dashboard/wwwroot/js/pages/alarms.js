import { jsx as _jsx, jsxs as _jsxs } from "preact/jsx-runtime";
import { useState, useEffect } from 'preact/hooks';
import { getCsrfToken } from '../dashboards/api';
import { t } from '../i18n';
export function AlarmsPage() {
    const [alarmsData, setAlarmsData] = useState({
        alarms: [],
        countCritical: 0,
        countMajor: 0,
        countMinor: 0,
        countWarning: 0,
        countMaintenance: 0,
        countAcked: 0
    });
    const [filter, setFilter] = useState(''); // '' | 'CRITICAL' | 'MAJOR' | 'MINOR' | 'WARNING' | 'ACKED'
    const [loading, setLoading] = useState(true);
    // Ack modal state
    const [ackAlarm, setAckAlarm] = useState(null);
    const [ackComment, setAckComment] = useState('');
    const [submittingAck, setSubmittingAck] = useState(false);
    const [ackStatusMsg, setAckStatusMsg] = useState(null);
    const fetchAlarms = async () => {
        try {
            const severityParam = filter ? `?severity=${filter}` : '';
            const res = await fetch(`/plswk/api/alarms${severityParam}`);
            if (res.ok) {
                const data = await res.json();
                setAlarmsData(data);
            }
        }
        catch (e) {
            console.error("Failed to load alarms:", e);
        }
        finally {
            setLoading(false);
        }
    };
    useEffect(() => {
        fetchAlarms();
    }, [filter]);
    const handleReset = async (alarm) => {
        if (!confirm('Are you sure you want to reset this alarm?'))
            return;
        try {
            const resp = await fetch('/plswk/api/alarm/reset', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': getCsrfToken()
                },
                body: JSON.stringify({ alarmId: alarm.alarmId })
            });
            const result = await resp.json();
            if (result.success) {
                fetchAlarms();
            }
            else {
                alert('Failed to reset alarm: ' + (result.error ?? 'Unknown error'));
            }
        }
        catch (err) {
            alert('Request failed: ' + err.message);
        }
    };
    const handleAcknowledge = async () => {
        if (!ackAlarm)
            return;
        setSubmittingAck(true);
        setAckStatusMsg({ type: 'sending', text: 'Sending\u2026' });
        try {
            const resp = await fetch('/plswk/api/alarm/acknowledge', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': getCsrfToken()
                },
                body: JSON.stringify({
                    alarmId: ackAlarm.alarmId,
                    comment: ackComment.trim(),
                    bacnetAckKey: ackAlarm.bacnetAckKey
                })
            });
            const result = await resp.json();
            if (result.success) {
                const bacnetNote = result.bacnetAcked
                    ? ' · BACnet ACK sent'
                    : (ackAlarm.bacnetAckKey ? ' · BACnet ACK skipped (context lost)' : '');
                setAckStatusMsg({ type: 'success', text: 'Acknowledged' + bacnetNote });
                setTimeout(() => {
                    setAckAlarm(null);
                    setAckComment('');
                    setAckStatusMsg(null);
                    setSubmittingAck(false);
                    fetchAlarms();
                }, 1400);
            }
            else {
                setAckStatusMsg({ type: 'error', text: 'Failed: ' + (result.error ?? 'Unknown error') });
                setSubmittingAck(false);
            }
        }
        catch (err) {
            setAckStatusMsg({ type: 'error', text: 'Request failed: ' + err.message });
            setSubmittingAck(false);
        }
    };
    if (loading) {
        return (_jsx("div", { class: "h-full flex items-center justify-center p-16", children: _jsxs("div", { class: "flex flex-col items-center gap-4 text-cyan-400", children: [_jsx("i", { class: "fas fa-spinner fa-spin text-3xl" }), _jsx("p", { class: "text-sm font-medium animate-pulse", children: t('loading') })] }) }));
    }
    const { alarms, countCritical, countMajor, countMinor, countWarning, countAcked } = alarmsData;
    const totalCount = countCritical + countMajor + countMinor + countWarning;
    return (_jsxs("div", { class: "flex flex-col gap-6 w-full page-enter", children: [_jsxs("div", { class: "flex gap-2 flex-wrap mb-2", "data-testid": "alarm-filters", children: [_jsxs("button", { class: `filter-chip ${filter === '' ? 'active' : ''}`, onClick: () => setFilter(''), children: ["All ", _jsx("span", { class: "opacity-65 ml-1", children: totalCount })] }), _jsxs("button", { class: `filter-chip chip-critical ${filter === 'CRITICAL' ? 'active' : ''}`, onClick: () => setFilter('CRITICAL'), children: [_jsx("i", { class: "fas fa-skull-crossbones mr-1.5" }), " Critical ", _jsx("span", { class: "opacity-65 ml-1", children: countCritical })] }), _jsxs("button", { class: `filter-chip chip-major ${filter === 'MAJOR' ? 'active' : ''}`, onClick: () => setFilter('MAJOR'), children: [_jsx("i", { class: "fas fa-exclamation-triangle mr-1.5" }), " Major ", _jsx("span", { class: "opacity-65 ml-1", children: countMajor })] }), _jsxs("button", { class: `filter-chip chip-minor ${filter === 'MINOR' ? 'active' : ''}`, onClick: () => setFilter('MINOR'), children: [_jsx("i", { class: "fas fa-exclamation-circle mr-1.5" }), " Minor ", _jsx("span", { class: "opacity-65 ml-1", children: countMinor })] }), _jsxs("button", { class: `filter-chip chip-warning ${filter === 'WARNING' ? 'active' : ''}`, onClick: () => setFilter('WARNING'), children: [_jsx("i", { class: "fas fa-bell mr-1.5" }), " Warning ", _jsx("span", { class: "opacity-65 ml-1", children: countWarning })] }), countAcked > 0 && (_jsxs("button", { class: `filter-chip chip-acked ${filter === 'ACKED' ? 'active' : ''}`, onClick: () => setFilter('ACKED'), children: [_jsx("i", { class: "fas fa-check-circle mr-1.5" }), " Acknowledged ", _jsx("span", { class: "opacity-65 ml-1", children: countAcked })] }))] }), _jsx("div", { class: "flex flex-col gap-4", "data-testid": "alarm-list", children: alarms.length === 0 ? (_jsxs("div", { class: "glass text-center py-16 text-slate-400 rounded-2xl", children: [_jsx("i", { class: "fas fa-bell-slash text-5xl opacity-20 mb-4 block" }), _jsx("p", { children: "No active alarms detected." })] })) : (alarms.map((alarm) => {
                    const isAcked = alarm.status.startsWith('ACTIVE_ACK') || alarm.status.startsWith('ACK');
                    const isWriteAllowed = window.pwCanWriteValue;
                    return (_jsxs("div", { class: `glass p-5 rounded-2xl border border-slate-700 grid grid-cols-[auto_1fr_auto] items-center gap-6 transition-all duration-200 hover:bg-white/[0.02] ${isAcked ? 'opacity-70' : ''}`, children: [_jsx("div", { class: `w-1.5 h-10 rounded ${alarm.severity === 'CRITICAL' ? 'bg-red-500 shadow-[0_0_10px_rgba(239,68,68,0.5)]' :
                                    alarm.severity === 'MAJOR' ? 'bg-amber-500' :
                                        alarm.severity === 'MINOR' ? 'bg-sky-400' : 'bg-slate-400'}` }), _jsxs("div", { class: "flex flex-col gap-1 min-w-0", children: [_jsxs("div", { class: "font-bold text-base text-slate-50 flex items-center gap-2 flex-wrap", children: [_jsx("span", { children: alarm.type }), isAcked && (_jsxs("span", { class: "text-[0.7rem] text-emerald-400 font-semibold inline-flex items-center gap-1", children: [_jsx("i", { class: "fas fa-check-circle" }), " Acknowledged"] }))] }), _jsxs("div", { class: "text-xs text-slate-400 flex gap-4 flex-wrap", children: [_jsxs("span", { children: [_jsx("i", { class: "fas fa-tag mr-1 text-slate-500" }), " ", alarm.severity] }), _jsxs("span", { children: [_jsx("i", { class: "fas fa-map-marker-alt mr-1 text-slate-500" }), " ", alarm.originator] }), _jsxs("span", { children: [_jsx("i", { class: "fas fa-info-circle mr-1 text-slate-500" }), " ", alarm.status] }), alarm.telemetryKey && (_jsxs("span", { children: [_jsx("i", { class: "fas fa-key mr-1 text-slate-500" }), " ", alarm.telemetryKey] }))] }), alarm.message && (_jsx("div", { class: "mt-2 text-sm text-slate-200 leading-relaxed max-w-4xl break-words", children: alarm.message })), alarm.ackComment && (_jsxs("div", { class: "mt-2 text-xs text-slate-400 italic flex items-start gap-1.5", children: [_jsx("i", { class: "fas fa-comment-alt mt-0.5 text-sky-400 shrink-0" }), _jsx("span", { children: alarm.ackComment })] }))] }), _jsxs("div", { class: "text-sm text-slate-400 text-right shrink-0", children: [_jsx("div", { class: "font-mono text-xs", children: new Date(alarm.time).toLocaleString() }), _jsxs("div", { class: "mt-3 flex items-center justify-end gap-2", children: [!isAcked && alarm.alarmId && (_jsxs("button", { class: `btn-sm border border-emerald-500 hover:bg-emerald-500/10 text-emerald-500 rounded px-3 py-1 text-xs font-semibold inline-flex items-center gap-1.5 bg-transparent ${isWriteAllowed ? '' : 'hidden'}`, onClick: () => setAckAlarm(alarm), children: [_jsx("i", { class: "fas fa-check" }), " Acknowledge"] })), alarm.telemetryKey && (_jsxs("button", { class: "btn-sm border border-slate-600 text-slate-400 hover:text-slate-300 hover:bg-slate-700/50 rounded px-3 py-1 text-xs inline-flex items-center gap-1.5 bg-transparent", onClick: () => window.openProperties(alarm.telemetryKey), children: [_jsx("i", { class: "fas fa-list" }), " Properties"] })), alarm.alarmId && (_jsxs("button", { class: `btn-sm border border-slate-600 text-slate-400 hover:text-red-400 hover:border-red-400/30 hover:bg-red-500/5 rounded px-3 py-1 text-xs inline-flex items-center gap-1.5 bg-transparent ${isWriteAllowed ? '' : 'hidden'}`, onClick: () => handleReset(alarm), children: [_jsx("i", { class: "fas fa-redo" }), " Reset"] }))] })] })] }, alarm.alarmId));
                })) }), ackAlarm && (_jsx("div", { class: "fixed inset-0 z-[9000] flex items-center justify-center bg-black/70 backdrop-blur-sm", "data-testid": "ack-modal", onClick: () => !submittingAck && setAckAlarm(null), children: _jsxs("div", { class: "glass w-[min(480px,90vw)] rounded-xl p-8 flex flex-col gap-5 relative border border-white/5 shadow-2xl", onClick: e => e.stopPropagation(), children: [_jsxs("div", { class: "flex justify-between items-center", children: [_jsxs("h3", { class: "text-lg font-bold", children: [_jsx("i", { class: "fas fa-check-circle text-emerald-500 mr-2" }), " Acknowledge Alarm"] }), _jsx("button", { onClick: () => !submittingAck && setAckAlarm(null), class: "bg-transparent border-0 text-slate-400 cursor-pointer text-xl hover:text-white", disabled: submittingAck, children: _jsx("i", { class: "fas fa-times" }) })] }), _jsx("div", { class: "text-sm text-slate-400 font-semibold bg-white/5 px-3 py-2 rounded-lg border border-white/[0.03]", children: ackAlarm.type || ackAlarm.alarmId }), _jsxs("div", { children: [_jsxs("label", { class: "text-xs text-slate-400 block mb-1.5 font-bold uppercase tracking-wider", children: ["Comment ", _jsx("span", { class: "opacity-50", children: "(optional)" })] }), _jsx("textarea", { rows: 3, placeholder: "Describe what action was taken\\u2026", value: ackComment, onInput: e => setAckComment(e.currentTarget.value), disabled: submittingAck, class: "w-full bg-slate-900/60 border border-slate-700 rounded-lg text-slate-50 px-3 py-2.5 font-sans text-sm resize-y outline-none focus:border-sky-400 transition-colors" })] }), ackAlarm.bacnetAckKey ? (_jsxs("p", { class: "text-[0.72rem] text-emerald-400 leading-relaxed font-semibold bg-emerald-400/5 p-2.5 rounded-lg border border-emerald-500/10", children: [_jsx("i", { class: "fas fa-plug mr-1.5" }), " A BACnet ", _jsx("code", { class: "bg-emerald-400/15 px-1.5 py-0.5 rounded text-[0.68rem] font-bold", children: "AlarmAcknowledgement" }), " will be sent to the originating controller."] })) : (_jsxs("p", { class: "text-[0.72rem] text-slate-400 opacity-70 leading-relaxed bg-white/5 p-2.5 rounded-lg border border-white/5", children: [_jsx("i", { class: "fas fa-info-circle mr-1.5" }), " This alarm has no active BACnet context. The acknowledgment will be recorded locally only."] })), ackStatusMsg && (_jsxs("div", { class: `text-xs p-2.5 rounded-lg font-bold flex items-center gap-2 ${ackStatusMsg.type === 'sending' ? 'bg-sky-400/5 text-sky-400 border border-sky-400/10' :
                                ackStatusMsg.type === 'success' ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20' :
                                    'bg-red-500/10 text-red-400 border border-red-500/20'}`, children: [ackStatusMsg.type === 'sending' ? _jsx("i", { class: "fas fa-spinner fa-spin" }) :
                                    ackStatusMsg.type === 'success' ? _jsx("i", { class: "fas fa-check-circle" }) : _jsx("i", { class: "fas fa-exclamation-circle" }), ackStatusMsg.text] })), _jsxs("div", { class: "flex gap-3 justify-end mt-2", children: [_jsx("button", { onClick: () => setAckAlarm(null), class: "bg-transparent border border-slate-700 text-slate-400 px-5 py-2 rounded-lg cursor-pointer hover:bg-white/5 transition-colors text-sm font-semibold", disabled: submittingAck, children: "Cancel" }), _jsxs("button", { onClick: handleAcknowledge, class: "bg-emerald-500 hover:bg-emerald-600 border-0 text-white font-semibold px-6 py-2 rounded-lg cursor-pointer transition-colors text-sm shadow-lg shadow-emerald-500/10", disabled: submittingAck, children: [_jsx("i", { class: "fas fa-check mr-1.5" }), " Confirm"] })] })] }) }))] }));
}
