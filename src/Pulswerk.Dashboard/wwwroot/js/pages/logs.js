import { jsx as _jsx, jsxs as _jsxs } from "preact/jsx-runtime";
import { useState, useEffect, useRef } from 'preact/hooks';
import { t } from '../i18n';
export function LogsPage() {
    const [logs, setLogs] = useState([]);
    const [level, setLevel] = useState(() => localStorage.getItem('logLevelPref') || 'all');
    const [loading, setLoading] = useState(true);
    const [autoRefresh, setAutoRefresh] = useState(true);
    const logContainerRef = useRef(null);
    const fetchLogs = async () => {
        try {
            const res = await fetch(`/plswk/api/logs?count=500&level=${level}`);
            if (res.ok) {
                const data = await res.json();
                setLogs(data || []);
            }
        }
        catch (e) {
            console.error("Failed to load logs:", e);
        }
        finally {
            setLoading(false);
        }
    };
    useEffect(() => {
        fetchLogs();
    }, [level]);
    // Setup periodic polling for logs if auto-refresh is active
    useEffect(() => {
        if (!autoRefresh)
            return;
        const timer = setInterval(fetchLogs, 4000);
        return () => clearInterval(timer);
    }, [autoRefresh, level]);
    // Scroll to bottom on logs load
    useEffect(() => {
        if (logContainerRef.current) {
            logContainerRef.current.scrollTop = logContainerRef.current.scrollHeight;
        }
    }, [logs.length]);
    const handleLevelChange = (newLevel) => {
        setLevel(newLevel);
        localStorage.setItem('logLevelPref', newLevel);
    };
    if (loading) {
        return (_jsx("div", { class: "h-full flex items-center justify-center p-16", children: _jsxs("div", { class: "flex flex-col items-center gap-4 text-cyan-400", children: [_jsx("i", { class: "fas fa-spinner fa-spin text-3xl" }), _jsx("p", { class: "text-sm font-medium animate-pulse", children: t('loading') })] }) }));
    }
    return (_jsxs("div", { class: "flex flex-col gap-5 h-[calc(100vh-110px)] w-full page-enter", children: [_jsxs("div", { class: "flex justify-between items-center bg-slate-800/40 backdrop-blur-md border border-white/5 rounded-xl px-5 py-3 shrink-0", children: [_jsxs("div", { class: "flex gap-2 items-center", children: [_jsx("div", { class: "w-3 h-3 rounded-full bg-red-500 animate-pulse" }), _jsx("div", { class: "w-3 h-3 rounded-full bg-amber-500 animate-pulse" }), _jsx("div", { class: "w-3 h-3 rounded-full bg-emerald-500 animate-pulse" }), _jsxs("select", { id: "logLevelSelect", value: level, onChange: (e) => handleLevelChange(e.currentTarget.value), class: "ml-4 bg-slate-900 border border-slate-700 rounded px-2.5 py-1 text-xs font-semibold text-slate-300 focus:outline-none focus:border-sky-500 cursor-pointer", children: [_jsx("option", { value: "all", children: "All Levels" }), _jsx("option", { value: "info", children: "Info & Above" }), _jsx("option", { value: "warning", children: "Warning & Error" }), _jsx("option", { value: "error", children: "Error Only" })] })] }), _jsxs("div", { class: "flex items-center gap-4 text-slate-400 text-xs select-none", children: [_jsxs("label", { class: "flex items-center gap-1.5 cursor-pointer", children: [_jsx("input", { type: "checkbox", checked: autoRefresh, onChange: (e) => setAutoRefresh(e.currentTarget.checked), class: "accent-sky-400" }), "Auto-Refresh"] }), _jsx("span", { class: "text-slate-600", children: "|" }), _jsxs("span", { children: ["Showing last ", logs.length, " entries"] }), _jsx("span", { class: "text-slate-600", children: "|" }), _jsxs("button", { onClick: fetchLogs, class: "text-sky-400 hover:text-sky-300 font-bold bg-transparent border-0 p-0 cursor-pointer inline-flex items-center gap-1", children: [_jsx("i", { class: "fas fa-sync-alt text-[0.7rem]" }), " ", t('btn_refresh')] })] })] }), _jsx("div", { ref: logContainerRef, class: "flex-1 glass rounded-2xl border border-slate-700/60 p-5 font-mono text-[0.78rem] overflow-y-auto flex flex-col gap-1.5 shadow-2xl custom-scrollbar", style: { backgroundColor: '#020617' }, "data-testid": "log-container", children: logs.length === 0 ? (_jsxs("div", { class: "h-full flex items-center justify-center text-slate-500 italic", children: ["No log records found for severity \"", level, "\""] })) : (logs.map((log, idx) => {
                    const sev = log.severity || 'info';
                    const isInfo = sev === 'info';
                    const isWarn = sev === 'warning' || sev === 'warn';
                    const isErr = sev === 'error' || sev === 'crit' || sev === 'fatal';
                    const sevColor = isInfo ? 'text-emerald-500' : isWarn ? 'text-amber-500' : isErr ? 'text-red-500' : 'text-slate-400';
                    return (_jsxs("div", { class: "flex gap-4 leading-relaxed whitespace-pre-wrap break-all log-entry hover:bg-white/[0.02] py-0.5 rounded px-1", "data-level": sev, children: [_jsxs("span", { class: "text-slate-500 shrink-0 w-[180px] select-none", children: ["[", log.timestamp, "]"] }), _jsx("span", { class: `shrink-0 w-[60px] font-extrabold uppercase ${sevColor} select-none`, children: sev }), _jsxs("span", { class: "text-sky-400 shrink-0 w-[120px] truncate select-none", children: ["[", log.source || 'system', "]"] }), _jsx("span", { class: "text-slate-200", children: log.message })] }, idx));
                })) })] }));
}
