import { jsx as _jsx, jsxs as _jsxs } from "preact/jsx-runtime";
import { render } from 'preact';
import { useState, useEffect } from 'preact/hooks';
import { z } from 'zod';
// Zod Schema for strict API runtime validation
const TelemetryKeySchema = z.object({
    key: z.string(),
    name: z.string().optional(),
    units: z.string().optional()
});
const EditorModal = ({ title, isOpen, onClose, onSave, children }) => {
    if (!isOpen)
        return null;
    return (_jsx("div", { class: "fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-center justify-center", children: _jsxs("div", { class: "bg-slate-800 border border-slate-600 rounded-xl w-full max-w-2xl max-h-[90vh] flex flex-col shadow-2xl", children: [_jsxs("div", { class: "px-6 py-4 border-b border-slate-700 flex justify-between items-center bg-black/20 rounded-t-xl", children: [_jsx("h2", { class: "text-lg font-bold text-slate-100", children: title }), _jsx("button", { class: "text-slate-400 hover:text-white", onClick: onClose, children: _jsx("i", { class: "fas fa-times" }) })] }), _jsx("div", { class: "p-6 overflow-y-auto flex-1 custom-scrollbar", children: children }), _jsxs("div", { class: "px-6 py-4 border-t border-slate-700 flex justify-end gap-3 bg-black/20 rounded-b-xl", children: [_jsx("button", { class: "px-4 py-2 rounded-lg text-slate-300 hover:bg-white/5 transition-colors", onClick: onClose, children: "Cancel" }), _jsx("button", { class: "px-5 py-2 bg-sky-500 hover:bg-sky-400 text-white rounded-lg font-medium shadow-lg shadow-sky-500/20 transition-all", onClick: onSave, children: "Save Changes" })] })] }) }));
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
            const keysRes = await fetch('/plswk/api/telemetry-keys');
            if (keysRes.ok) {
                // RUNTIME VALIDATION
                const rawData = await keysRes.json();
                const parsedKeys = z.array(TelemetryKeySchema).parse(rawData);
                setAvailableKeys(parsedKeys);
            }
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
            const res = await fetch('/plswk/api/config/override', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(config.override)
            });
            if (res.ok) {
                window.pwToast("Configuration saved successfully.");
                await loadConfig(); // reload to get merged views
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
    if (loading)
        return _jsx("div", { class: "p-8 text-slate-400", children: "Loading configuration..." });
    if (!config)
        return _jsx("div", { class: "p-8 text-red-400", children: "Failed to load configuration." });
    const renderConnectionList = (items, isReadOnly) => (_jsx("div", { class: "flex flex-col gap-2", children: items.map(c => (_jsxs("div", { class: "p-4 bg-slate-800/50 border border-slate-700 rounded-lg flex justify-between items-center hover:border-slate-500 transition-colors", children: [_jsxs("div", { children: [_jsxs("div", { class: "font-bold text-slate-100", children: [c.name || c.id, " ", _jsxs("span", { class: "text-xs ml-2 text-slate-400 font-normal opacity-60", children: ["ID: ", c.id] })] }), _jsxs("div", { class: "text-xs text-slate-400 mt-1 flex items-center gap-3", children: [_jsx("span", { class: "px-1.5 py-0.5 rounded bg-amber-500/10 text-amber-400 border border-amber-500/20 uppercase tracking-wider", children: c.type }), c.address && _jsxs("span", { children: [c.address, ":", c.port] }), isReadOnly && _jsxs("span", { class: "text-emerald-500/70", children: [_jsx("i", { class: "fas fa-lock mr-1" }), "Read-only"] })] })] }), !isReadOnly && (_jsxs("div", { class: "flex gap-2", children: [_jsx("button", { class: "w-8 h-8 rounded-lg bg-slate-700 hover:bg-slate-600 text-slate-300 flex items-center justify-center transition-colors", onClick: () => setEditingConnection(c), children: _jsx("i", { class: "fas fa-pen" }) }), _jsx("button", { class: "w-8 h-8 rounded-lg bg-red-500/10 hover:bg-red-500/20 text-red-400 border border-red-500/20 flex items-center justify-center transition-colors", onClick: () => handleDeleteConnection(c.id), children: _jsx("i", { class: "fas fa-trash" }) })] }))] }))) }));
    return (_jsxs("div", { class: "p-6 max-w-6xl mx-auto flex flex-col gap-6", children: [_jsx("datalist", { id: "available-paths", children: getAvailablePaths().map(p => _jsx("option", { value: p })) }), _jsxs("div", { class: "flex justify-between items-center", children: [_jsxs("div", { children: [_jsx("h1", { class: "text-2xl font-bold text-white", children: "System Configuration" }), _jsx("p", { class: "text-slate-400 text-sm mt-1", children: "Manage interactive configurations (saved to override JSON)." })] }), _jsx("button", { class: `px-6 py-2.5 rounded-lg font-bold shadow-lg transition-all ${saving ? 'bg-slate-600 text-slate-400 cursor-not-allowed' : 'bg-emerald-500 hover:bg-emerald-400 text-white shadow-emerald-500/20'}`, onClick: saveConfig, disabled: saving, children: saving ? _jsxs("span", { children: [_jsx("i", { class: "fas fa-spinner fa-spin mr-2" }), "Saving..."] }) : _jsxs("span", { children: [_jsx("i", { class: "fas fa-save mr-2" }), "Save & Apply"] }) })] }), _jsxs("div", { class: "grid grid-cols-1 lg:grid-cols-2 gap-6", children: [_jsxs("div", { class: "bg-slate-800/80 border border-slate-700 rounded-xl p-6 shadow-xl backdrop-blur-sm", children: [_jsxs("div", { class: "flex justify-between items-center mb-6 border-b border-slate-700 pb-4", children: [_jsxs("h2", { class: "text-xl font-bold text-slate-100", children: [_jsx("i", { class: "fas fa-network-wired mr-2 text-amber-400" }), "Connections"] }), _jsxs("button", { class: "px-3 py-1.5 bg-amber-500/10 hover:bg-amber-500/20 text-amber-400 border border-amber-500/20 rounded-lg text-sm font-bold transition-colors", onClick: () => setEditingConnection({ id: '', type: 'modbus-tcp', address: '' }), children: [_jsx("i", { class: "fas fa-plus mr-1" }), " Add Connection"] })] }), _jsx("h3", { class: "text-sm font-bold text-slate-400 uppercase tracking-widest mt-4", children: "Configured (Editable)" }), (config.override.connections?.length || 0) === 0 ? _jsx("div", { class: "text-slate-500 text-sm italic", children: "No custom connections." }) : renderConnectionList(config.override.connections || [], false), _jsx("h3", { class: "text-sm font-bold text-slate-400 uppercase tracking-widest mt-6", children: "From pulswerk.json (Read-only)" }), renderConnectionList(config.base.connections || [], true)] }), _jsxs("div", { class: "bg-slate-800/80 border border-slate-700 rounded-xl p-6 shadow-xl backdrop-blur-sm", children: [_jsxs("div", { class: "flex justify-between items-center mb-6 border-b border-slate-700 pb-4", children: [_jsxs("h2", { class: "text-xl font-bold text-slate-100", children: [_jsx("i", { class: "fas fa-microchip mr-2 text-sky-400" }), "Devices & Telemetry"] }), _jsxs("button", { class: "px-3 py-1.5 bg-sky-500/10 hover:bg-sky-500/20 text-sky-400 border border-sky-500/20 rounded-lg text-sm font-bold transition-colors", onClick: () => setEditingDevice({ id: '', name: '', deviceType: 'virtual', telemetries: [] }), children: [_jsx("i", { class: "fas fa-plus mr-1" }), " Add Device"] })] }), _jsx(DeviceList, { baseDevices: config.base.devices || [], overrideDevices: config.override.devices || [], onEdit: setEditingDevice, onDelete: handleDeleteDevice })] })] }), _jsx(EditorModal, { title: editingConnection?.id ? `Edit Connection: ${editingConnection.id}` : "New Connection", isOpen: !!editingConnection, onClose: () => setEditingConnection(null), onSave: handleSaveConnection, children: editingConnection && (_jsxs("div", { class: "flex flex-col gap-5", children: [_jsxs("div", { class: "grid grid-cols-2 gap-4", children: [_jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-xs font-bold text-slate-400 uppercase tracking-wide", children: "Connection ID" }), _jsx("input", { type: "text", class: "bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-amber-500 focus:outline-none", value: editingConnection.id, onInput: (e) => setEditingConnection({ ...editingConnection, id: e.target.value }) })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-xs font-bold text-slate-400 uppercase tracking-wide", children: "Protocol Type" }), _jsxs("select", { class: "bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-amber-500 focus:outline-none appearance-none", value: editingConnection.type, onChange: (e) => setEditingConnection({ ...editingConnection, type: e.target.value }), children: [_jsx("option", { value: "modbus-tcp", children: "Modbus TCP" }), _jsx("option", { value: "bacnet-ip", children: "BACnet/IP" })] })] })] }), _jsxs("div", { class: "grid grid-cols-2 gap-4", children: [_jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-xs font-bold text-slate-400 uppercase tracking-wide", children: "Host / Address" }), _jsx("input", { type: "text", class: "bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-amber-500 focus:outline-none", value: editingConnection.address || editingConnection.localAddress || '', onInput: (e) => {
                                                const v = e.target.value;
                                                if (editingConnection.type === 'bacnet-ip')
                                                    setEditingConnection({ ...editingConnection, localAddress: v });
                                                else
                                                    setEditingConnection({ ...editingConnection, address: v });
                                            } })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-xs font-bold text-slate-400 uppercase tracking-wide", children: "Port" }), _jsx("input", { type: "number", class: "bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-amber-500 focus:outline-none", value: editingConnection.port || editingConnection.localPort || '', onInput: (e) => {
                                                const v = parseInt(e.target.value);
                                                if (editingConnection.type === 'bacnet-ip')
                                                    setEditingConnection({ ...editingConnection, localPort: v });
                                                else
                                                    setEditingConnection({ ...editingConnection, port: v });
                                            } })] })] })] })) }), _jsx(EditorModal, { title: editingDevice?.id ? `Edit Device: ${editingDevice.name}` : "New Device", isOpen: !!editingDevice, onClose: () => setEditingDevice(null), onSave: handleSaveDevice, children: editingDevice && (_jsxs("div", { class: "flex flex-col gap-5", children: [_jsxs("div", { class: "grid grid-cols-2 gap-4", children: [_jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-xs font-bold text-slate-400 uppercase tracking-wide", children: "Device ID" }), _jsx("input", { type: "text", class: "bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-sky-500 focus:outline-none", value: editingDevice.id, onInput: (e) => setEditingDevice({ ...editingDevice, id: e.target.value }) })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-xs font-bold text-slate-400 uppercase tracking-wide", children: "Display Name" }), _jsx("input", { type: "text", class: "bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-sky-500 focus:outline-none", value: editingDevice.name, onInput: (e) => setEditingDevice({ ...editingDevice, name: e.target.value }) })] })] }), _jsxs("div", { class: "grid grid-cols-2 gap-4", children: [_jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-xs font-bold text-slate-400 uppercase tracking-wide", children: "Device Type" }), _jsxs("select", { class: "bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-sky-500 focus:outline-none appearance-none", value: editingDevice.deviceType, onChange: (e) => setEditingDevice({ ...editingDevice, deviceType: e.target.value }), children: [_jsx("option", { value: "virtual", children: "Virtual / Analytics" }), _jsx("option", { value: "janitza", children: "Janitza" }), _jsx("option", { value: "deziko", children: "Deziko BACnet" }), _jsx("option", { value: "modbus-tcp", children: "Modbus TCP Generic" })] })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-xs font-bold text-slate-400 uppercase tracking-wide", children: "Connection ID" }), _jsx("input", { type: "text", class: "bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-sky-500 focus:outline-none", value: editingDevice.connectionId || '', placeholder: "Optional", onInput: (e) => setEditingDevice({ ...editingDevice, connectionId: e.target.value }) })] })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsxs("label", { class: "text-xs font-bold text-slate-400 uppercase tracking-wide", children: ["Device Path ", _jsx("span", { class: "opacity-50", children: "(optional)" })] }), _jsx("input", { list: "available-paths", type: "text", class: "bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-sky-500 focus:outline-none", value: editingDevice.path ? editingDevice.path.join('/') : '', placeholder: "e.g. Building/Floor", onInput: (e) => {
                                        const val = e.target.value;
                                        setEditingDevice({ ...editingDevice, path: val ? val.split('/').map(s => s.trim()).filter(Boolean) : undefined });
                                    } })] }), editingDevice.deviceType === 'virtual' && (_jsxs("div", { class: "mt-4 border-t border-slate-700 pt-5 flex flex-col gap-4", children: [_jsxs("div", { class: "flex justify-between items-center", children: [_jsx("h3", { class: "text-sm font-bold text-slate-300 uppercase tracking-widest", children: "Virtual Telemetries" }), _jsxs("button", { class: "px-3 py-1.5 bg-slate-700 hover:bg-slate-600 text-white rounded text-xs transition-colors", onClick: () => {
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
                                                } })] }))) })] }))] })) })] }));
};
export function initConfigPage() {
    const root = document.getElementById('config-root');
    if (root) {
        render(_jsx(ConfigPage, {}), root);
    }
}
initConfigPage();
