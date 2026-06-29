import { jsx as _jsx, jsxs as _jsxs, Fragment as _Fragment } from "preact/jsx-runtime";
import { render } from 'preact';
import { useState, useEffect } from 'preact/hooks';
import { z } from 'zod';
// Zod Schema for strict API runtime validation
const TelemetryKeySchema = z.object({
    key: z.string(),
    name: z.string().optional(),
    units: z.string().optional()
});
// ── Module metadata ──────────────────────────────────────────────────────────
const MODULE_META = [
    { key: 'dashboards', label: 'Dashboards', icon: 'fa-table-cells-large', description: 'Custom dashboard builder & widget layout' },
    { key: 'assets', label: 'Assets', icon: 'fa-sitemap', description: 'Asset hierarchy browser & property editor' },
    { key: 'telemetry', label: 'Telemetry', icon: 'fa-database', description: 'Telemetry list, search, and CRUD management' },
    { key: 'historicalData', label: 'Historical Data', icon: 'fa-chart-line', description: 'Time-series query, charts, and data export' },
    { key: 'alarms', label: 'Alarms', icon: 'fa-bell', description: 'Active alarm monitoring and acknowledgement' },
    { key: 'logs', label: 'System Logs', icon: 'fa-scroll', description: 'Live system log stream and log history' },
    { key: 'heartbeat', label: 'Heartbeat', icon: 'fa-heart-pulse', description: 'System health, uptime, and performance stats' },
    { key: 'billing', label: 'Billing', icon: 'fa-dollar-sign', description: 'RFID card billing and tenant energy invoicing' },
    { key: 'wallbox', label: 'Wallboxes', icon: 'fa-charging-station', description: 'OCPP EV chargepoint management and monitoring' },
    { key: 'ems', label: 'Energy Management', icon: 'fa-bolt', description: 'Trajectory control, targets, and curtailment' },
    { key: 'connections', label: 'Connections', icon: 'fa-network-wired', description: 'Device connection management and diagnostics' },
];
const DEFAULT_MODULES = {
    ems: true, billing: true, wallbox: true, historicalData: true,
    alarms: true, logs: true, heartbeat: true, dashboards: true,
    assets: true, telemetry: true, connections: true,
};
const ModulesPanel = ({ modules, onChange }) => (_jsxs("div", { class: "bg-slate-800/80 border border-slate-700 rounded-xl p-6 shadow-xl backdrop-blur-sm", children: [_jsxs("div", { class: "flex items-center gap-3 mb-6 border-b border-slate-700 pb-4", children: [_jsxs("h2", { class: "text-xl font-bold text-slate-100 flex-1", children: [_jsx("i", { class: "fas fa-puzzle-piece mr-2 text-violet-400" }), "Feature Modules"] }), _jsxs("span", { class: "text-xs text-slate-500", children: [Object.values(modules).filter(Boolean).length, " / ", MODULE_META.length, " enabled"] })] }), _jsx("div", { class: "grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-3", children: MODULE_META.map(({ key, label, icon, description }) => {
                const enabled = modules[key];
                return (_jsxs("div", { class: `flex items-start gap-3 p-4 rounded-lg border transition-all cursor-pointer select-none ${enabled
                        ? 'bg-slate-700/40 border-slate-600 hover:border-slate-500'
                        : 'bg-slate-900/40 border-slate-700/50 opacity-60 hover:opacity-75'}`, onClick: () => onChange(key, !enabled), children: [_jsx("div", { class: `w-9 h-9 rounded-lg flex items-center justify-center flex-shrink-0 transition-colors ${enabled ? 'bg-violet-500/20 text-violet-400' : 'bg-slate-700/50 text-slate-500'}`, children: _jsx("i", { class: `fas ${icon} text-sm` }) }), _jsxs("div", { class: "flex-1 min-w-0", children: [_jsx("div", { class: `font-semibold text-sm ${enabled ? 'text-slate-100' : 'text-slate-400'}`, children: label }), _jsx("div", { class: "text-xs text-slate-500 mt-0.5 leading-relaxed", children: description })] }), _jsx("div", { class: "flex-shrink-0 mt-0.5", children: _jsx("div", { class: `w-9 h-5 rounded-full relative transition-colors ${enabled ? 'bg-violet-500' : 'bg-slate-600'}`, children: _jsx("div", { class: `absolute top-0.5 w-4 h-4 rounded-full bg-white shadow-md transition-transform ${enabled ? 'translate-x-4' : 'translate-x-0.5'}` }) }) })] }, key));
            }) })] }));
// ── Editor modal ──────────────────────────────────────────────────────────────
const EditorModal = ({ title, isOpen, onClose, onSave, children }) => {
    if (!isOpen)
        return null;
    return (_jsx("div", { class: "fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-center justify-center", children: _jsxs("div", { class: "bg-slate-800 border border-slate-600 rounded-xl w-full max-w-2xl max-h-[90vh] flex flex-col shadow-2xl", children: [_jsxs("div", { class: "px-6 py-4 border-b border-slate-700 flex justify-between items-center bg-black/20 rounded-t-xl", children: [_jsx("h2", { class: "text-lg font-bold text-slate-100", children: title }), _jsx("button", { class: "text-slate-400 hover:text-white", onClick: onClose, children: _jsx("i", { class: "fas fa-times" }) })] }), _jsx("div", { class: "p-6 overflow-y-auto flex-1 custom-scrollbar", children: children }), _jsxs("div", { class: "px-6 py-4 border-t border-slate-700 flex justify-end gap-3 bg-black/20 rounded-b-xl", children: [_jsx("button", { class: "px-4 py-2 rounded-lg text-slate-300 hover:bg-white/5 transition-colors", onClick: onClose, children: "Cancel" }), _jsx("button", { class: "px-5 py-2 bg-sky-500 hover:bg-sky-400 text-white rounded-lg font-medium shadow-lg shadow-sky-500/20 transition-all", onClick: onSave, children: "Save Changes" })] })] }) }));
};
const FieldRenderer = ({ field, value, onChange, accent, options }) => {
    const accentClass = accent === 'amber' ? 'focus:border-amber-500' : 'focus:border-sky-500';
    const baseInput = `bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm ${accentClass} focus:outline-none`;
    return (_jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsxs("label", { class: "text-xs font-bold text-slate-400 uppercase tracking-wide", children: [field.label, field.required && _jsx("span", { class: "text-red-400 ml-0.5", children: "*" })] }), field.type === 'checkbox' ? (_jsxs("label", { class: "flex items-center gap-2 cursor-pointer select-none py-1", children: [_jsx("input", { type: "checkbox", checked: !!value, onChange: (e) => onChange(e.target.checked), class: "w-4 h-4 rounded accent-amber-500" }), _jsx("span", { class: "text-sm text-slate-300", children: "Enabled" })] })) : field.type === 'select' ? (_jsxs("select", { class: `${baseInput} appearance-none`, value: value || '', onChange: (e) => onChange(e.target.value), children: [_jsx("option", { value: "", children: "\u2014 Select \u2014" }), (options || field.options || []).map(o => _jsx("option", { value: o, children: o }))] })) : (_jsx("input", { type: field.type === 'password' ? 'password' : field.type === 'number' ? 'number' : 'text', class: baseInput, value: value ?? '', placeholder: field.placeholder, onInput: (e) => {
                    const v = e.target.value;
                    onChange(field.type === 'number' ? (v === '' ? undefined : Number(v)) : v);
                } })), field.help && _jsx("span", { class: "text-[11px] text-slate-500 leading-relaxed", children: field.help })] }));
};
const ScanModal = ({ isOpen, onClose, onAddConnections }) => {
    const [range, setRange] = useState('192.168.1.0/24');
    const [scanning, setScanning] = useState(false);
    const [result, setResult] = useState(null);
    const [error, setError] = useState(null);
    const [selected, setSelected] = useState(new Set());
    const runScan = async () => {
        if (!range.trim())
            return;
        setScanning(true);
        setError(null);
        setResult(null);
        setSelected(new Set());
        try {
            const res = await fetch('/plswk/api/scan-network', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ range })
            });
            if (!res.ok) {
                const txt = await res.text();
                throw new Error(txt || `Scan failed (${res.status})`);
            }
            const data = await res.json();
            setResult(data);
            // Pre-select all found hosts
            setSelected(new Set(data.hosts.map(h => h.ip)));
        }
        catch (e) {
            setError(e.message || 'Scan failed');
        }
        finally {
            setScanning(false);
        }
    };
    const toggleHost = (ip) => {
        setSelected(prev => {
            const next = new Set(prev);
            if (next.has(ip))
                next.delete(ip);
            else
                next.add(ip);
            return next;
        });
    };
    const handleAdd = () => {
        const chosen = (result?.hosts || []).filter(h => selected.has(h.ip));
        if (chosen.length === 0)
            return;
        onAddConnections(chosen);
        onClose();
        setResult(null);
        setSelected(new Set());
    };
    if (!isOpen)
        return null;
    return (_jsx("div", { class: "fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-center justify-center", children: _jsxs("div", { class: "bg-slate-800 border border-slate-600 rounded-xl w-full max-w-3xl max-h-[90vh] flex flex-col shadow-2xl", children: [_jsxs("div", { class: "px-6 py-4 border-b border-slate-700 flex justify-between items-center bg-black/20 rounded-t-xl", children: [_jsxs("h2", { class: "text-lg font-bold text-slate-100", children: [_jsx("i", { class: "fas fa-satellite-dish mr-2 text-cyan-400" }), "Discover Devices on Network"] }), _jsx("button", { class: "text-slate-400 hover:text-white", onClick: onClose, children: _jsx("i", { class: "fas fa-times" }) })] }), _jsxs("div", { class: "p-6 overflow-y-auto flex-1 custom-scrollbar flex flex-col gap-5", children: [_jsxs("div", { class: "flex flex-col gap-2", children: [_jsx("label", { class: "text-xs font-bold text-slate-400 uppercase tracking-wide", children: "IP Range / CIDR" }), _jsxs("div", { class: "flex gap-2", children: [_jsx("input", { type: "text", class: "flex-1 bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm font-mono focus:border-cyan-500 focus:outline-none", value: range, onInput: (e) => setRange(e.target.value), placeholder: "192.168.1.0/24 or 192.168.1.1-254" }), _jsx("button", { class: "px-5 py-2 bg-cyan-500 hover:bg-cyan-400 text-white rounded-lg font-bold shadow-lg shadow-cyan-500/20 transition-all disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2", onClick: runScan, disabled: scanning || !range.trim(), children: scanning ? _jsxs(_Fragment, { children: [_jsx("i", { class: "fas fa-spinner fa-spin" }), " Scanning\u2026"] }) : _jsxs(_Fragment, { children: [_jsx("i", { class: "fas fa-radar" }), " Scan"] }) })] }), _jsxs("p", { class: "text-[11px] text-slate-500", children: ["Accepted: CIDR (", _jsx("code", { children: "192.168.1.0/24" }), "), last-octet range (", _jsx("code", { children: "192.168.1.1-254" }), "), explicit range (", _jsx("code", { children: "10.0.0.1-10.0.0.20" }), "), or a single host. Probes ports: Modbus 502 \u00B7 BACnet 47808 \u00B7 KNX 3671 \u00B7 OCPP 9000."] })] }), error && (_jsxs("div", { class: "p-3 rounded-lg bg-red-500/10 border border-red-500/20 text-red-400 text-sm", children: [_jsx("i", { class: "fas fa-exclamation-triangle mr-2" }), error] })), result && (_jsxs("div", { class: "flex flex-col gap-3", children: [_jsxs("div", { class: "flex items-center justify-between text-xs text-slate-400", children: [_jsxs("span", { children: ["Scanned ", _jsx("strong", { class: "text-slate-200", children: result.hostsScanned }), " addresses in ", result.durationMs, " ms"] }), _jsxs("span", { class: result.hostsFound > 0 ? 'text-emerald-400' : 'text-slate-500', children: [_jsx("i", { class: "fas fa-circle-check mr-1" }), result.hostsFound, " host", result.hostsFound !== 1 ? 's' : '', " found"] })] }), result.hosts.length === 0 ? (_jsxs("div", { class: "p-6 border border-slate-700 border-dashed rounded-lg text-center text-slate-500 text-sm", children: [_jsx("i", { class: "fas fa-magnifying-glass text-2xl block mb-2 opacity-50" }), "No devices responded on known protocol ports in this range."] })) : (_jsx("div", { class: "flex flex-col gap-2", children: result.hosts.map(h => {
                                        const isSel = selected.has(h.ip);
                                        return (_jsxs("label", { class: `p-3 rounded-lg border flex items-center gap-3 cursor-pointer transition-colors ${isSel ? 'bg-cyan-500/10 border-cyan-500/40' : 'bg-slate-900/40 border-slate-700 hover:border-slate-500'}`, children: [_jsx("input", { type: "checkbox", checked: isSel, onChange: () => toggleHost(h.ip), class: "w-4 h-4 accent-cyan-500" }), _jsxs("div", { class: "flex-1", children: [_jsx("div", { class: "font-mono text-sm text-slate-100", children: h.ip }), _jsx("div", { class: "flex flex-wrap gap-1.5 mt-1", children: h.protocols.map(p => (_jsxs("span", { class: "text-[10px] px-1.5 py-0.5 rounded bg-slate-700/60 text-slate-300 border border-slate-600 uppercase tracking-wider", children: [p.label, " :", p.port] }))) })] })] }));
                                    }) }))] }))] }), _jsxs("div", { class: "px-6 py-4 border-t border-slate-700 flex justify-between items-center bg-black/20 rounded-b-xl", children: [_jsx("span", { class: "text-xs text-slate-500", children: selected.size > 0 ? `${selected.size} host${selected.size !== 1 ? 's' : ''} selected` : '' }), _jsxs("div", { class: "flex gap-3", children: [_jsx("button", { class: "px-4 py-2 rounded-lg text-slate-300 hover:bg-white/5 transition-colors", onClick: onClose, children: "Cancel" }), _jsxs("button", { class: "px-5 py-2 bg-emerald-500 hover:bg-emerald-400 text-white rounded-lg font-medium shadow-lg shadow-emerald-500/20 transition-all disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2", onClick: handleAdd, disabled: selected.size === 0, children: [_jsx("i", { class: "fas fa-plus" }), " Add ", selected.size > 0 ? `(${selected.size})` : ''] })] })] })] }) }));
};
const FormulaEditor = ({ value, deviceId, availableKeys, onChange }) => {
    const [liveResult, setLiveResult] = useState(null);
    const [showAutocomplete, setShowAutocomplete] = useState(false);
    const [autocompleteQuery, setAutocompleteQuery] = useState("");
    const [cursorPos, setCursorPos] = useState(0);
    const checkFormula = async (f) => {
        if (!f)
            return;
        try {
            const res = await fetch('/plswk/api/config/evaluate-formula', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ formula: f, deviceId })
            });
            const data = await res.json();
            setLiveResult(data);
        }
        catch (e) {
            setLiveResult({ success: false, error: e.message });
        }
    };
    const handleInput = (e) => {
        const target = e.target;
        const val = target.value;
        const cursor = target.selectionStart || 0;
        onChange(val);
        const textBeforeCursor = val.substring(0, cursor);
        const lastBracketMatch = textBeforeCursor.match(/\[([^\]]*)$/);
        if (lastBracketMatch) {
            setAutocompleteQuery(lastBracketMatch[1]);
            setShowAutocomplete(true);
            setCursorPos(cursor);
        }
        else {
            setShowAutocomplete(false);
        }
    };
    const handleSelectKey = (key) => {
        const textBeforeCursor = value.substring(0, cursorPos);
        const textAfterCursor = value.substring(cursorPos);
        const textBeforeBracket = textBeforeCursor.replace(/\[([^\]]*)$/, '');
        let remainder = textAfterCursor;
        if (remainder.startsWith(']')) {
            remainder = remainder.substring(1);
        }
        onChange(`${textBeforeBracket}[${key}]${remainder}`);
        setShowAutocomplete(false);
    };
    const filteredKeys = availableKeys?.filter(k => k?.key?.toLowerCase()?.includes((autocompleteQuery || '').toLowerCase())).slice(0, 100) || [];
    return (_jsxs("div", { class: "flex flex-col gap-2 relative", children: [_jsx("label", { class: "text-sm font-semibold text-slate-300", children: "Formula / Calculation" }), _jsxs("div", { class: "flex gap-2", children: [_jsx("input", { type: "text", class: "flex-1 bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-sm text-slate-100 font-mono focus:border-sky-500 focus:outline-none", value: value, onInput: handleInput, onClick: handleInput, onKeyUp: handleInput, placeholder: "e.g. [meter-main] - [meter-sub]" }), _jsx("button", { class: "px-3 py-2 bg-slate-700 hover:bg-slate-600 rounded-lg text-sm text-white", onClick: () => checkFormula(value), children: "Check" })] }), showAutocomplete && filteredKeys.length > 0 && (_jsx("div", { class: "absolute left-0 right-16 top-[70px] max-h-48 overflow-y-auto bg-slate-800 border border-slate-600 rounded-lg shadow-2xl z-50 flex flex-col custom-scrollbar", children: filteredKeys.map(k => (_jsxs("div", { class: "px-3 py-2 border-b border-slate-700/50 hover:bg-sky-500/20 cursor-pointer text-sm flex justify-between items-center transition-colors", onClick: () => handleSelectKey(k.key), children: [_jsxs("div", { class: "flex flex-col", children: [_jsx("span", { class: "font-mono text-slate-200", children: k.key }), k.name && _jsx("span", { class: "text-xs text-slate-400", children: k.name })] }), k.units && _jsx("span", { class: "text-xs bg-slate-700 text-slate-300 px-1.5 py-0.5 rounded", children: k.units })] }))) })), liveResult && (_jsx("div", { class: `text-xs p-2 rounded ${liveResult.success ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20' : 'bg-red-500/10 text-red-400 border border-red-500/20'}`, children: liveResult.success ? (_jsxs("span", { children: [_jsx("i", { class: "fas fa-check-circle mr-1" }), " Live result: ", _jsx("strong", { children: liveResult.result })] })) : (_jsxs("span", { children: [_jsx("i", { class: "fas fa-exclamation-circle mr-1" }), " Error: ", liveResult.error] })) }))] }));
};
const DeviceList = ({ baseDevices, overrideDevices, onEdit, onDelete }) => {
    const renderList = (items, isReadOnly) => (_jsx("div", { class: "flex flex-col gap-2", children: items.map(d => (_jsxs("div", { class: "p-4 bg-slate-800/50 border border-slate-700 rounded-lg flex justify-between items-center hover:border-slate-500 transition-colors", children: [_jsxs("div", { children: [_jsxs("div", { class: "font-bold text-slate-100", children: [d.name, " ", _jsxs("span", { class: "text-xs ml-2 text-slate-400 font-normal opacity-60", children: ["ID: ", d.id] })] }), _jsxs("div", { class: "text-xs text-slate-400 mt-1 flex items-center gap-3", children: [_jsx("span", { class: "px-1.5 py-0.5 rounded bg-sky-500/10 text-sky-400 border border-sky-500/20 uppercase tracking-wider", children: d.deviceType }), d.connectionId && _jsxs("span", { children: ["Conn: ", d.connectionId] }), isReadOnly && _jsxs("span", { class: "text-emerald-500/70", children: [_jsx("i", { class: "fas fa-lock mr-1" }), "Read-only"] })] })] }), !isReadOnly && (_jsxs("div", { class: "flex gap-2", children: [_jsx("button", { class: "w-8 h-8 rounded-lg bg-slate-700 hover:bg-slate-600 text-slate-300 flex items-center justify-center transition-colors", onClick: () => onEdit(d), children: _jsx("i", { class: "fas fa-pen" }) }), _jsx("button", { class: "w-8 h-8 rounded-lg bg-red-500/10 hover:bg-red-500/20 text-red-400 border border-red-500/20 flex items-center justify-center transition-colors", onClick: () => onDelete(d.id), children: _jsx("i", { class: "fas fa-trash" }) })] }))] }))) }));
    return (_jsxs("div", { class: "flex flex-col gap-4", children: [_jsx("h3", { class: "text-sm font-bold text-slate-400 uppercase tracking-widest mt-4", children: "Configured (Editable)" }), overrideDevices.length === 0 ? _jsx("div", { class: "text-slate-500 text-sm italic", children: "No custom devices." }) : renderList(overrideDevices, false), _jsx("h3", { class: "text-sm font-bold text-slate-400 uppercase tracking-widest mt-6", children: "From pulswerk.json (Read-only)" }), renderList(baseDevices, true)] }));
};
const ConfigPage = () => {
    const [config, setConfig] = useState(null);
    const [availableKeys, setAvailableKeys] = useState([]);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [modules, setModules] = useState({ ...DEFAULT_MODULES });
    const getAvailablePaths = () => {
        if (!config)
            return [];
        const paths = new Set();
        const collect = (devices) => {
            devices.forEach(d => {
                if (d.path)
                    paths.add(d.path.join('/'));
                if (d.telemetries) {
                    d.telemetries.forEach(t => {
                        if (t.path)
                            paths.add(t.path.join('/'));
                    });
                }
            });
        };
        collect(config.base.devices || []);
        collect(config.override.devices || []);
        return Array.from(paths).sort();
    };
    // Editor State
    const [editingDevice, setEditingDevice] = useState(null);
    const [editingConnection, setEditingConnection] = useState(null);
    const [scanOpen, setScanOpen] = useState(false);
    const [connectionTypes, setConnectionTypes] = useState([]);
    const [deviceTypes, setDeviceTypes] = useState([]);
    const loadConfig = async () => {
        setLoading(true);
        try {
            const res = await fetch('/plswk/api/config');
            const data = await res.json();
            if (!data.override.devices)
                data.override.devices = [];
            if (!data.override.connections)
                data.override.connections = [];
            if (!data.base.devices)
                data.base.devices = [];
            if (!data.base.connections)
                data.base.connections = [];
            setConfig(data);
            // Derive effective module state: base defaults → override patch
            const baseModules = { ...DEFAULT_MODULES, ...(data.base.modules ?? {}) };
            const effective = { ...baseModules, ...(data.override.modules ?? {}) };
            setModules(effective);
            const keysRes = await fetch('/plswk/api/telemetry-keys');
            if (keysRes.ok) {
                // RUNTIME VALIDATION
                const rawData = await keysRes.json();
                const parsedKeys = z.array(TelemetryKeySchema).parse(rawData);
                setAvailableKeys(parsedKeys);
            }
            // Load connection & device type metadata (drives the per-type editors)
            const [ctRes, dtRes] = await Promise.all([
                fetch('/plswk/api/connection-types'),
                fetch('/plswk/api/device-types')
            ]);
            if (ctRes.ok)
                setConnectionTypes(await ctRes.json());
            if (dtRes.ok)
                setDeviceTypes(await dtRes.json());
        }
        catch (e) {
            console.error("Failed to load config or invalid API shape:", e);
        }
        finally {
            setLoading(false);
        }
    };
    useEffect(() => {
        loadConfig();
    }, []);
    const saveConfig = async () => {
        if (!config)
            return;
        setSaving(true);
        try {
            // Include the current module toggles in the override payload
            const payload = { ...config.override, modules };
            const res = await fetch('/plswk/api/config/override', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });
            if (res.ok) {
                // Notify other tabs/windows (main SPA) that modules changed
                try {
                    localStorage.setItem('pw_modules_updated', Date.now().toString());
                }
                catch (_) { }
                window.pwToast("Configuration saved. Applying module changes…");
                // Brief pause so the toast is visible, then hard-reload so the main
                // SPA re-fetches /api/user/identity and the nav reflects the new state.
                setTimeout(() => { window.location.reload(); }, 800);
            }
            else {
                window.pwToast("Failed to save configuration.", "error");
            }
        }
        catch (e) {
            console.error(e);
        }
        finally {
            setSaving(false);
        }
    };
    const handleModuleToggle = (key, enabled) => {
        setModules(prev => ({ ...prev, [key]: enabled }));
    };
    const handleSaveDevice = () => {
        if (!editingDevice || !editingDevice.id || !editingDevice.name) {
            window.pwToast("ID and Name are required.", "error");
            return;
        }
        setConfig(prev => {
            if (!prev)
                return prev;
            const newOverride = { ...prev.override };
            const idx = newOverride.devices?.findIndex(d => d.id === editingDevice.id) ?? -1;
            if (idx >= 0) {
                newOverride.devices[idx] = editingDevice;
            }
            else {
                newOverride.devices.push(editingDevice);
            }
            return { ...prev, override: newOverride };
        });
        setEditingDevice(null);
    };
    const handleDeleteDevice = async (id) => {
        if (!await window.pwConfirm(`Delete custom device ${id}?`, 'Delete Device'))
            return;
        setConfig(prev => {
            if (!prev)
                return prev;
            const newOverride = { ...prev.override };
            newOverride.devices = newOverride.devices?.filter(d => d.id !== id);
            return { ...prev, override: newOverride };
        });
    };
    const handleSaveConnection = () => {
        if (!editingConnection || !editingConnection.id || !editingConnection.type) {
            window.pwToast("ID and Type are required.", "error");
            return;
        }
        setConfig(prev => {
            if (!prev)
                return prev;
            const newOverride = { ...prev.override };
            const idx = newOverride.connections?.findIndex(c => c.id === editingConnection.id) ?? -1;
            if (idx >= 0) {
                newOverride.connections[idx] = editingConnection;
            }
            else {
                newOverride.connections.push(editingConnection);
            }
            return { ...prev, override: newOverride };
        });
        setEditingConnection(null);
    };
    const handleDeleteConnection = async (id) => {
        if (!await window.pwConfirm(`Delete custom connection ${id}?`, 'Delete Connection'))
            return;
        setConfig(prev => {
            if (!prev)
                return prev;
            const newOverride = { ...prev.override };
            newOverride.connections = newOverride.connections?.filter(c => c.id !== id);
            return { ...prev, override: newOverride };
        });
    };
    // Bulk-add connections discovered by the network scan. Each selected host
    // becomes a new connection of the first detected protocol type, pre-filled
    // with the host IP and the protocol's default port. The user can then refine
    // the parameters in the connection editor.
    const handleAddScannedHosts = (hosts) => {
        setConfig(prev => {
            if (!prev)
                return prev;
            const newOverride = { ...prev.override, connections: [...(prev.override.connections || [])] };
            const existingIds = new Set(newOverride.connections.map(c => c.id));
            const baseIds = new Set((prev.base.connections || []).map(c => c.id));
            for (const host of hosts) {
                const proto = host.protocols[0];
                if (!proto)
                    continue;
                // Derive a unique connection id from the IP
                let baseId = `scan-${proto.type}-${host.ip.replace(/\./g, '-')}`;
                let id = baseId, n = 2;
                while (existingIds.has(id) || baseIds.has(id)) {
                    id = `${baseId}-${n++}`;
                }
                existingIds.add(id);
                const meta = connectionTypes.find(t => t.type === proto.type);
                const conn = { id, type: proto.type, name: `${meta?.label || proto.type} ${host.ip}` };
                // Populate the right address/port fields per protocol
                if (proto.type === 'bacnet-ip' || proto.type === 'ocpp') {
                    conn.localAddress = '0.0.0.0';
                    conn.localPort = proto.port;
                }
                else {
                    conn.address = host.ip;
                    conn.port = proto.port;
                }
                newOverride.connections.push(conn);
            }
            return { ...prev, override: newOverride };
        });
        window.pwToast(`Added ${hosts.length} connection${hosts.length !== 1 ? 's' : ''} from scan. Review and save.`);
    };
    if (loading)
        return _jsx("div", { class: "p-8 text-slate-400", children: "Loading configuration..." });
    if (!config)
        return _jsx("div", { class: "p-8 text-red-400", children: "Failed to load configuration." });
    const renderConnectionList = (items, isReadOnly) => (_jsx("div", { class: "flex flex-col gap-2", children: items.map(c => (_jsxs("div", { class: "p-4 bg-slate-800/50 border border-slate-700 rounded-lg flex justify-between items-center hover:border-slate-500 transition-colors", children: [_jsxs("div", { children: [_jsxs("div", { class: "font-bold text-slate-100", children: [c.name || c.id, " ", _jsxs("span", { class: "text-xs ml-2 text-slate-400 font-normal opacity-60", children: ["ID: ", c.id] })] }), _jsxs("div", { class: "text-xs text-slate-400 mt-1 flex items-center gap-3", children: [_jsx("span", { class: "px-1.5 py-0.5 rounded bg-amber-500/10 text-amber-400 border border-amber-500/20 uppercase tracking-wider", children: c.type }), c.address && _jsxs("span", { children: [c.address, ":", c.port] }), isReadOnly && _jsxs("span", { class: "text-emerald-500/70", children: [_jsx("i", { class: "fas fa-lock mr-1" }), "Read-only"] })] })] }), !isReadOnly && (_jsxs("div", { class: "flex gap-2", children: [_jsx("button", { class: "w-8 h-8 rounded-lg bg-slate-700 hover:bg-slate-600 text-slate-300 flex items-center justify-center transition-colors", onClick: () => setEditingConnection(c), children: _jsx("i", { class: "fas fa-pen" }) }), _jsx("button", { class: "w-8 h-8 rounded-lg bg-red-500/10 hover:bg-red-500/20 text-red-400 border border-red-500/20 flex items-center justify-center transition-colors", onClick: () => handleDeleteConnection(c.id), children: _jsx("i", { class: "fas fa-trash" }) })] }))] }))) }));
    return (_jsxs("div", { class: "p-6 max-w-6xl mx-auto flex flex-col gap-6", children: [_jsx("datalist", { id: "available-paths", children: getAvailablePaths().map(p => _jsx("option", { value: p })) }), _jsxs("div", { class: "flex justify-between items-center", children: [_jsxs("div", { children: [_jsx("h1", { class: "text-2xl font-bold text-white", children: "System Configuration" }), _jsx("p", { class: "text-slate-400 text-sm mt-1", children: "Manage interactive configurations (saved to override JSON)." })] }), _jsx("button", { class: `px-6 py-2.5 rounded-lg font-bold shadow-lg transition-all ${saving ? 'bg-slate-600 text-slate-400 cursor-not-allowed' : 'bg-emerald-500 hover:bg-emerald-400 text-white shadow-emerald-500/20'}`, onClick: saveConfig, disabled: saving, children: saving ? _jsxs("span", { children: [_jsx("i", { class: "fas fa-spinner fa-spin mr-2" }), "Saving..."] }) : _jsxs("span", { children: [_jsx("i", { class: "fas fa-save mr-2" }), "Save & Apply"] }) })] }), _jsx(ModulesPanel, { modules: modules, onChange: handleModuleToggle }), _jsxs("div", { class: "grid grid-cols-1 lg:grid-cols-2 gap-6", children: [_jsxs("div", { class: "bg-slate-800/80 border border-slate-700 rounded-xl p-6 shadow-xl backdrop-blur-sm", children: [_jsxs("div", { class: "flex justify-between items-center mb-6 border-b border-slate-700 pb-4", children: [_jsxs("h2", { class: "text-xl font-bold text-slate-100", children: [_jsx("i", { class: "fas fa-network-wired mr-2 text-amber-400" }), "Connections"] }), _jsxs("div", { class: "flex gap-2", children: [_jsxs("button", { class: "px-3 py-1.5 bg-cyan-500/10 hover:bg-cyan-500/20 text-cyan-400 border border-cyan-500/20 rounded-lg text-sm font-bold transition-colors", onClick: () => setScanOpen(true), children: [_jsx("i", { class: "fas fa-satellite-dish mr-1" }), " Discover Devices"] }), _jsxs("button", { class: "px-3 py-1.5 bg-amber-500/10 hover:bg-amber-500/20 text-amber-400 border border-amber-500/20 rounded-lg text-sm font-bold transition-colors", onClick: () => setEditingConnection({ id: '', type: 'modbus-tcp', address: '' }), children: [_jsx("i", { class: "fas fa-plus mr-1" }), " Add"] })] })] }), _jsx("h3", { class: "text-sm font-bold text-slate-400 uppercase tracking-widest mt-4", children: "Configured (Editable)" }), (config.override.connections?.length || 0) === 0 ? _jsx("div", { class: "text-slate-500 text-sm italic", children: "No custom connections." }) : renderConnectionList(config.override.connections || [], false), _jsx("h3", { class: "text-sm font-bold text-slate-400 uppercase tracking-widest mt-6", children: "From pulswerk.json (Read-only)" }), renderConnectionList(config.base.connections || [], true)] }), _jsxs("div", { class: "bg-slate-800/80 border border-slate-700 rounded-xl p-6 shadow-xl backdrop-blur-sm", children: [_jsxs("div", { class: "flex justify-between items-center mb-6 border-b border-slate-700 pb-4", children: [_jsxs("h2", { class: "text-xl font-bold text-slate-100", children: [_jsx("i", { class: "fas fa-microchip mr-2 text-sky-400" }), "Devices & Telemetry"] }), _jsxs("button", { class: "px-3 py-1.5 bg-sky-500/10 hover:bg-sky-500/20 text-sky-400 border border-sky-500/20 rounded-lg text-sm font-bold transition-colors", onClick: () => setEditingDevice({ id: '', name: '', deviceType: 'virtual', telemetries: [] }), children: [_jsx("i", { class: "fas fa-plus mr-1" }), " Add Device"] })] }), _jsx(DeviceList, { baseDevices: config.base.devices || [], overrideDevices: config.override.devices || [], onEdit: setEditingDevice, onDelete: handleDeleteDevice })] })] }), _jsx(ScanModal, { isOpen: scanOpen, onClose: () => setScanOpen(false), onAddConnections: handleAddScannedHosts }), _jsx(EditorModal, { title: editingConnection?.id ? `Edit Connection: ${editingConnection.id}` : "New Connection", isOpen: !!editingConnection, onClose: () => setEditingConnection(null), onSave: handleSaveConnection, children: editingConnection && (() => {
                    const meta = connectionTypes.find(t => t.type === editingConnection.type);
                    const fields = meta?.fields || [];
                    // Split fields: id & type always first (rendered manually), rest from metadata
                    const dynamicFields = fields.filter(f => f.key !== 'id' && f.key !== 'type');
                    return (_jsxs("div", { class: "flex flex-col gap-5", children: [_jsxs("div", { class: "grid grid-cols-2 gap-4", children: [_jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsxs("label", { class: "text-xs font-bold text-slate-400 uppercase tracking-wide", children: ["Connection ID ", _jsx("span", { class: "text-red-400", children: "*" })] }), _jsx("input", { type: "text", class: "bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-amber-500 focus:outline-none", value: editingConnection.id, onInput: (e) => setEditingConnection({ ...editingConnection, id: e.target.value }) })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsxs("label", { class: "text-xs font-bold text-slate-400 uppercase tracking-wide", children: ["Protocol Type ", _jsx("span", { class: "text-red-400", children: "*" })] }), _jsxs("select", { class: "bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-amber-500 focus:outline-none appearance-none", value: editingConnection.type, onChange: (e) => {
                                                    const newType = e.target.value;
                                                    const newMeta = connectionTypes.find(t => t.type === newType);
                                                    // Reset type-specific fields and apply default port
                                                    const updated = { id: editingConnection.id, type: newType, name: editingConnection.name };
                                                    if (newMeta) {
                                                        if (newType === 'bacnet-ip' || newType === 'ocpp') {
                                                            updated.localAddress = '0.0.0.0';
                                                            updated.localPort = newMeta.defaultPort;
                                                        }
                                                        else {
                                                            updated.port = newMeta.defaultPort;
                                                        }
                                                    }
                                                    setEditingConnection(updated);
                                                }, children: [connectionTypes.map(t => _jsx("option", { value: t.type, children: t.label })), connectionTypes.length === 0 && _jsxs(_Fragment, { children: [_jsx("option", { value: "modbus-tcp", children: "Modbus TCP" }), _jsx("option", { value: "bacnet-ip", children: "BACnet/IP" }), _jsx("option", { value: "knx-ip", children: "KNXnet/IP" }), _jsx("option", { value: "ocpp", children: "OCPP" })] })] })] })] }), dynamicFields.length > 0 && (_jsx("div", { class: "grid grid-cols-2 gap-4", children: dynamicFields.map(field => (_jsx(FieldRenderer, { field: field, accent: "amber", value: editingConnection[field.key], onChange: (val) => setEditingConnection({ ...editingConnection, [field.key]: val }) }, field.key))) }))] }));
                })() }), _jsx(EditorModal, { title: editingDevice?.id ? `Edit Device: ${editingDevice.name}` : "New Device", isOpen: !!editingDevice, onClose: () => setEditingDevice(null), onSave: handleSaveDevice, children: editingDevice && (() => {
                    const meta = deviceTypes.find(t => t.type === editingDevice.deviceType);
                    const fields = meta?.fields || [];
                    const dynamicFields = fields.filter(f => f.key !== 'id' && f.key !== 'name' && f.key !== 'deviceType');
                    // Build connection options from compatible connection types
                    const allConns = [...(config?.base.connections || []), ...(config?.override.connections || [])];
                    const connOptions = allConns
                        .filter(c => !meta || meta.compatibleConnections.length === 0 || meta.compatibleConnections.includes(c.type))
                        .map(c => c.id);
                    return (_jsxs("div", { class: "flex flex-col gap-5", children: [_jsxs("div", { class: "grid grid-cols-2 gap-4", children: [_jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsxs("label", { class: "text-xs font-bold text-slate-400 uppercase tracking-wide", children: ["Device ID ", _jsx("span", { class: "text-red-400", children: "*" })] }), _jsx("input", { type: "text", class: "bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-sky-500 focus:outline-none", value: editingDevice.id, onInput: (e) => setEditingDevice({ ...editingDevice, id: e.target.value }) })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsxs("label", { class: "text-xs font-bold text-slate-400 uppercase tracking-wide", children: ["Display Name ", _jsx("span", { class: "text-red-400", children: "*" })] }), _jsx("input", { type: "text", class: "bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-sky-500 focus:outline-none", value: editingDevice.name, onInput: (e) => setEditingDevice({ ...editingDevice, name: e.target.value }) })] })] }), _jsxs("div", { class: "grid grid-cols-2 gap-4", children: [_jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsxs("label", { class: "text-xs font-bold text-slate-400 uppercase tracking-wide", children: ["Device Type ", _jsx("span", { class: "text-red-400", children: "*" })] }), _jsxs("select", { class: "bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-sky-500 focus:outline-none appearance-none", value: editingDevice.deviceType, onChange: (e) => {
                                                    const newType = e.target.value;
                                                    // Clear type-specific fields when switching types
                                                    setEditingDevice({ id: editingDevice.id, name: editingDevice.name, deviceType: newType, telemetries: newType === 'virtual' ? (editingDevice.telemetries || []) : undefined });
                                                }, children: [deviceTypes.map(t => _jsx("option", { value: t.type, children: t.label })), deviceTypes.length === 0 && _jsxs(_Fragment, { children: [_jsx("option", { value: "virtual", children: "Virtual / Analytics" }), _jsx("option", { value: "janitza", children: "Janitza" }), _jsx("option", { value: "deziko", children: "Deziko BACnet" })] })] })] }), dynamicFields.find(f => f.key === 'connectionId') && (_jsx(FieldRenderer, { field: dynamicFields.find(f => f.key === 'connectionId'), accent: "sky", value: editingDevice.connectionId, options: connOptions, onChange: (val) => setEditingDevice({ ...editingDevice, connectionId: val }) }))] }), dynamicFields.filter(f => f.key !== 'connectionId').length > 0 && (_jsx("div", { class: "grid grid-cols-2 gap-4", children: dynamicFields.filter(f => f.key !== 'connectionId').map(field => (_jsx(FieldRenderer, { field: field, accent: "sky", value: editingDevice[field.key], onChange: (val) => {
                                        if (field.key === 'path') {
                                            setEditingDevice({ ...editingDevice, path: val ? String(val).split('/').map((s) => s.trim()).filter(Boolean) : undefined });
                                        }
                                        else {
                                            setEditingDevice({ ...editingDevice, [field.key]: val });
                                        }
                                    } }, field.key))) })), editingDevice.deviceType === 'virtual' && (_jsxs("div", { class: "mt-4 border-t border-slate-700 pt-5 flex flex-col gap-4", children: [_jsxs("div", { class: "flex justify-between items-center", children: [_jsx("h3", { class: "text-sm font-bold text-slate-300 uppercase tracking-widest", children: "Virtual Telemetries" }), _jsxs("button", { class: "px-3 py-1.5 bg-slate-700 hover:bg-slate-600 text-white rounded text-xs transition-colors", onClick: () => {
                                                    const t = [...(editingDevice.telemetries || []), { id: `point_${Date.now().toString().slice(-4)}`, name: 'New Point', formula: '', units: '' }];
                                                    setEditingDevice({ ...editingDevice, telemetries: t });
                                                }, children: [_jsx("i", { class: "fas fa-plus mr-1" }), " Add Point"] })] }), (!editingDevice.telemetries || editingDevice.telemetries.length === 0) && (_jsx("div", { class: "p-4 border border-slate-700 border-dashed rounded-lg text-center text-slate-500 text-sm", children: "No virtual telemetries defined." })), _jsx("div", { class: "flex flex-col gap-3", children: (editingDevice.telemetries || []).map((t, i) => (_jsxs("div", { class: "bg-black/20 border border-slate-700 rounded-lg p-4 flex flex-col gap-3 relative", children: [_jsx("button", { class: "absolute top-3 right-3 text-slate-500 hover:text-red-400", onClick: () => {
                                                        const newT = [...(editingDevice.telemetries || [])];
                                                        newT.splice(i, 1);
                                                        setEditingDevice({ ...editingDevice, telemetries: newT });
                                                    }, children: _jsx("i", { class: "fas fa-times" }) }), _jsxs("div", { class: "grid grid-cols-4 gap-3 pr-6", children: [_jsxs("div", { class: "flex flex-col gap-1", children: [_jsx("label", { class: "text-[10px] uppercase font-bold text-slate-400", children: "Point ID" }), _jsx("input", { class: "bg-slate-900 border border-slate-600 rounded px-2 py-1 text-sm text-white", value: t.id, onInput: (e) => {
                                                                        const newT = [...(editingDevice.telemetries || [])];
                                                                        newT[i].id = e.target.value;
                                                                        setEditingDevice({ ...editingDevice, telemetries: newT });
                                                                    } })] }), _jsxs("div", { class: "flex flex-col gap-1", children: [_jsx("label", { class: "text-[10px] uppercase font-bold text-slate-400", children: "Name" }), _jsx("input", { class: "bg-slate-900 border border-slate-600 rounded px-2 py-1 text-sm text-white", value: t.name, onInput: (e) => {
                                                                        const newT = [...(editingDevice.telemetries || [])];
                                                                        newT[i].name = e.target.value;
                                                                        setEditingDevice({ ...editingDevice, telemetries: newT });
                                                                    } })] }), _jsxs("div", { class: "flex flex-col gap-1", children: [_jsxs("label", { class: "text-[10px] uppercase font-bold text-slate-400", children: ["Path ", _jsx("span", { class: "opacity-50", children: "(optional)" })] }), _jsx("input", { list: "available-paths", class: "bg-slate-900 border border-slate-600 rounded px-2 py-1 text-sm text-white placeholder-slate-600", placeholder: "e.g. Building/Floor", value: t.path ? t.path.join('/') : '', onInput: (e) => {
                                                                        const val = e.target.value;
                                                                        const newT = [...(editingDevice.telemetries || [])];
                                                                        newT[i].path = val ? val.split('/').map((s) => s.trim()).filter(Boolean) : null;
                                                                        setEditingDevice({ ...editingDevice, telemetries: newT });
                                                                    } })] }), _jsxs("div", { class: "flex flex-col gap-1", children: [_jsx("label", { class: "text-[10px] uppercase font-bold text-slate-400", children: "Units" }), _jsx("input", { class: "bg-slate-900 border border-slate-600 rounded px-2 py-1 text-sm text-white", value: t.units || '', onInput: (e) => {
                                                                        const newT = [...(editingDevice.telemetries || [])];
                                                                        newT[i].units = e.target.value;
                                                                        setEditingDevice({ ...editingDevice, telemetries: newT });
                                                                    } })] })] }), _jsx(FormulaEditor, { value: t.formula, deviceId: editingDevice.id, availableKeys: availableKeys, onChange: (val) => {
                                                        const newT = [...(editingDevice.telemetries || [])];
                                                        newT[i].formula = val;
                                                        setEditingDevice({ ...editingDevice, telemetries: newT });
                                                    } })] }))) })] }))] }));
                })() })] }));
};
export function initConfigPage() {
    const root = document.getElementById('config-root');
    if (root) {
        render(_jsx(ConfigPage, {}), root);
    }
}
initConfigPage();
export { ConfigPage };
