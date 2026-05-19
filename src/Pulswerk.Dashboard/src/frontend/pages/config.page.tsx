import { render, ComponentChildren } from 'preact';
import { useState, useEffect } from 'preact/hooks';
import { z } from 'zod';

// Zod Schema for strict API runtime validation
const TelemetryKeySchema = z.object({
    key: z.string(),
    name: z.string().optional(),
    units: z.string().optional()
});

type TelemetryKey = z.infer<typeof TelemetryKeySchema>;

// Strict TypeScript Interfaces
interface TelemetryConfig {
    id: string;
    name: string;
    formula: string;
    units?: string;
    path?: string[] | null;
}

interface DeviceConfig {
    id: string;
    name: string;
    deviceType: string;
    connectionId?: string;
    telemetries?: TelemetryConfig[];
    path?: string[];
}

interface ConnectionConfig {
    id: string;
    type: string;
    address?: string;
    port?: number;
    localAddress?: string;
    localPort?: number;
    name?: string;
}

interface AppConfig {
    connections?: ConnectionConfig[];
    devices?: DeviceConfig[];
    server?: unknown;
    influxdb?: unknown;
}

interface ConfigState {
    base: AppConfig;
    override: AppConfig;
}

interface EditorModalProps {
    title: string;
    isOpen: boolean;
    onClose: () => void;
    onSave: () => void;
    children: ComponentChildren;
}

const EditorModal = ({ title, isOpen, onClose, onSave, children }: EditorModalProps) => {
    if (!isOpen) return null;
    return (
        <div class="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-center justify-center">
            <div class="bg-slate-800 border border-slate-600 rounded-xl w-full max-w-2xl max-h-[90vh] flex flex-col shadow-2xl">
                <div class="px-6 py-4 border-b border-slate-700 flex justify-between items-center bg-black/20 rounded-t-xl">
                    <h2 class="text-lg font-bold text-slate-100">{title}</h2>
                    <button class="text-slate-400 hover:text-white" onClick={onClose}><i class="fas fa-times"></i></button>
                </div>
                <div class="p-6 overflow-y-auto flex-1 custom-scrollbar">
                    {children}
                </div>
                <div class="px-6 py-4 border-t border-slate-700 flex justify-end gap-3 bg-black/20 rounded-b-xl">
                    <button class="px-4 py-2 rounded-lg text-slate-300 hover:bg-white/5 transition-colors" onClick={onClose}>Cancel</button>
                    <button class="px-5 py-2 bg-sky-500 hover:bg-sky-400 text-white rounded-lg font-medium shadow-lg shadow-sky-500/20 transition-all" onClick={onSave}>Save Changes</button>
                </div>
            </div>
        </div>
    );
};

interface FormulaEditorProps {
    value: string;
    deviceId: string;
    availableKeys: TelemetryKey[];
    onChange: (val: string) => void;
}

const FormulaEditor = ({ value, deviceId, availableKeys, onChange }: FormulaEditorProps) => {
    const [liveResult, setLiveResult] = useState<{result?: string, error?: string, success: boolean} | null>(null);
    const [showAutocomplete, setShowAutocomplete] = useState(false);
    const [autocompleteQuery, setAutocompleteQuery] = useState("");
    const [cursorPos, setCursorPos] = useState(0);

    const checkFormula = async (f: string) => {
        if (!f) return;
        try {
            const res = await fetch('/plswk/api/config/evaluate-formula', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ formula: f, deviceId })
            });
            const data = await res.json();
            setLiveResult(data);
        } catch (e: any) {
            setLiveResult({ success: false, error: e.message });
        }
    };

    const handleInput = (e: Event) => {
        const target = e.target as HTMLInputElement;
        const val = target.value;
        const cursor = target.selectionStart || 0;
        onChange(val);

        const textBeforeCursor = val.substring(0, cursor);
        const lastBracketMatch = textBeforeCursor.match(/\[([^\]]*)$/);
        
        if (lastBracketMatch) {
            setAutocompleteQuery(lastBracketMatch[1]);
            setShowAutocomplete(true);
            setCursorPos(cursor);
        } else {
            setShowAutocomplete(false);
        }
    };

    const handleSelectKey = (key: string) => {
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

    return (
        <div class="flex flex-col gap-2 relative">
            <label class="text-sm font-semibold text-slate-300">Formula / Calculation</label>
            <div class="flex gap-2">
                <input 
                    type="text" 
                    class="flex-1 bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-sm text-slate-100 font-mono focus:border-sky-500 focus:outline-none" 
                    value={value} 
                    onInput={handleInput}
                    onClick={handleInput}
                    onKeyUp={handleInput}
                    placeholder="e.g. [meter-main] - [meter-sub]" 
                />
                <button class="px-3 py-2 bg-slate-700 hover:bg-slate-600 rounded-lg text-sm text-white" onClick={() => checkFormula(value)}>Check</button>
            </div>
            {showAutocomplete && filteredKeys.length > 0 && (
                <div class="absolute left-0 right-16 top-[70px] max-h-48 overflow-y-auto bg-slate-800 border border-slate-600 rounded-lg shadow-2xl z-50 flex flex-col custom-scrollbar">
                    {filteredKeys.map(k => (
                        <div 
                            class="px-3 py-2 border-b border-slate-700/50 hover:bg-sky-500/20 cursor-pointer text-sm flex justify-between items-center transition-colors"
                            onClick={() => handleSelectKey(k.key)}
                        >
                            <div class="flex flex-col">
                                <span class="font-mono text-slate-200">{k.key}</span>
                                {k.name && <span class="text-xs text-slate-400">{k.name}</span>}
                            </div>
                            {k.units && <span class="text-xs bg-slate-700 text-slate-300 px-1.5 py-0.5 rounded">{k.units}</span>}
                        </div>
                    ))}
                </div>
            )}
            {liveResult && (
                <div class={`text-xs p-2 rounded ${liveResult.success ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20' : 'bg-red-500/10 text-red-400 border border-red-500/20'}`}>
                    {liveResult.success ? (
                        <span><i class="fas fa-check-circle mr-1"></i> Live result: <strong>{liveResult.result}</strong></span>
                    ) : (
                        <span><i class="fas fa-exclamation-circle mr-1"></i> Error: {liveResult.error}</span>
                    )}
                </div>
            )}
        </div>
    );
};

interface DeviceListProps {
    baseDevices: DeviceConfig[];
    overrideDevices: DeviceConfig[];
    onEdit: (d: DeviceConfig) => void;
    onDelete: (id: string) => void;
}

const DeviceList = ({ baseDevices, overrideDevices, onEdit, onDelete }: DeviceListProps) => {
    const renderList = (items: DeviceConfig[], isReadOnly: boolean) => (
        <div class="flex flex-col gap-2">
            {items.map(d => (
                <div class="p-4 bg-slate-800/50 border border-slate-700 rounded-lg flex justify-between items-center hover:border-slate-500 transition-colors">
                    <div>
                        <div class="font-bold text-slate-100">{d.name} <span class="text-xs ml-2 text-slate-400 font-normal opacity-60">ID: {d.id}</span></div>
                        <div class="text-xs text-slate-400 mt-1 flex items-center gap-3">
                            <span class="px-1.5 py-0.5 rounded bg-sky-500/10 text-sky-400 border border-sky-500/20 uppercase tracking-wider">{d.deviceType}</span>
                            {d.connectionId && <span>Conn: {d.connectionId}</span>}
                            {isReadOnly && <span class="text-emerald-500/70"><i class="fas fa-lock mr-1"></i>Read-only</span>}
                        </div>
                    </div>
                    {!isReadOnly && (
                        <div class="flex gap-2">
                            <button class="w-8 h-8 rounded-lg bg-slate-700 hover:bg-slate-600 text-slate-300 flex items-center justify-center transition-colors" onClick={() => onEdit(d)}><i class="fas fa-pen"></i></button>
                            <button class="w-8 h-8 rounded-lg bg-red-500/10 hover:bg-red-500/20 text-red-400 border border-red-500/20 flex items-center justify-center transition-colors" onClick={() => onDelete(d.id)}><i class="fas fa-trash"></i></button>
                        </div>
                    )}
                </div>
            ))}
        </div>
    );

    return (
        <div class="flex flex-col gap-4">
            <h3 class="text-sm font-bold text-slate-400 uppercase tracking-widest mt-4">Configured (Editable)</h3>
            {overrideDevices.length === 0 ? <div class="text-slate-500 text-sm italic">No custom devices.</div> : renderList(overrideDevices, false)}
            
            <h3 class="text-sm font-bold text-slate-400 uppercase tracking-widest mt-6">From pulswerk.json (Read-only)</h3>
            {renderList(baseDevices, true)}
        </div>
    );
};

const ConfigPage = () => {
    const [config, setConfig] = useState<ConfigState | null>(null);
    const [availableKeys, setAvailableKeys] = useState<TelemetryKey[]>([]);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);

    const getAvailablePaths = () => {
        if (!config) return [];
        const paths = new Set<string>();
        const collect = (devices: DeviceConfig[]) => {
            devices.forEach(d => {
                if (d.path) paths.add(d.path.join('/'));
                if (d.telemetries) {
                    d.telemetries.forEach(t => {
                        if (t.path) paths.add(t.path.join('/'));
                    });
                }
            });
        };
        collect(config.base.devices || []);
        collect(config.override.devices || []);
        return Array.from(paths).sort();
    };
    
    // Editor State
    const [editingDevice, setEditingDevice] = useState<DeviceConfig | null>(null);
    const [editingConnection, setEditingConnection] = useState<ConnectionConfig | null>(null);

    const loadConfig = async () => {
        setLoading(true);
        try {
            const res = await fetch('/plswk/api/config');
            const data = await res.json();
            if (!data.override.devices) data.override.devices = [];
            if (!data.override.connections) data.override.connections = [];
            if (!data.base.devices) data.base.devices = [];
            if (!data.base.connections) data.base.connections = [];
            setConfig(data);
            
            const keysRes = await fetch('/plswk/api/telemetry-keys');
            if (keysRes.ok) {
                // RUNTIME VALIDATION
                const rawData = await keysRes.json();
                const parsedKeys = z.array(TelemetryKeySchema).parse(rawData);
                setAvailableKeys(parsedKeys);
            }
        } catch(e) {
            console.error("Failed to load config or invalid API shape:", e);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        loadConfig();
    }, []);

    const saveConfig = async () => {
        if (!config) return;
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
            } else {
                window.pwToast("Failed to save configuration.", "error");
            }
        } catch(e) {
            console.error(e);
        } finally {
            setSaving(false);
        }
    };

    const handleSaveDevice = () => {
        if (!editingDevice || !editingDevice.id || !editingDevice.name) {
            window.pwToast("ID and Name are required.", "error");
            return;
        }
        
        setConfig(prev => {
            if (!prev) return prev;
            const newOverride = { ...prev.override };
            const idx = newOverride.devices?.findIndex(d => d.id === editingDevice.id) ?? -1;
            
            if (idx >= 0) {
                newOverride.devices![idx] = editingDevice;
            } else {
                newOverride.devices!.push(editingDevice);
            }
            return { ...prev, override: newOverride };
        });
        setEditingDevice(null);
    };

    const handleDeleteDevice = async (id: string) => {
        if (!await window.pwConfirm(`Delete custom device ${id}?`, 'Delete Device')) return;
        setConfig(prev => {
            if (!prev) return prev;
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
            if (!prev) return prev;
            const newOverride = { ...prev.override };
            const idx = newOverride.connections?.findIndex(c => c.id === editingConnection.id) ?? -1;
            
            if (idx >= 0) {
                newOverride.connections![idx] = editingConnection;
            } else {
                newOverride.connections!.push(editingConnection);
            }
            return { ...prev, override: newOverride };
        });
        setEditingConnection(null);
    };

    const handleDeleteConnection = async (id: string) => {
        if (!await window.pwConfirm(`Delete custom connection ${id}?`, 'Delete Connection')) return;
        setConfig(prev => {
            if (!prev) return prev;
            const newOverride = { ...prev.override };
            newOverride.connections = newOverride.connections?.filter(c => c.id !== id);
            return { ...prev, override: newOverride };
        });
    };

    if (loading) return <div class="p-8 text-slate-400">Loading configuration...</div>;
    if (!config) return <div class="p-8 text-red-400">Failed to load configuration.</div>;

    const renderConnectionList = (items: ConnectionConfig[], isReadOnly: boolean) => (
        <div class="flex flex-col gap-2">
            {items.map(c => (
                <div class="p-4 bg-slate-800/50 border border-slate-700 rounded-lg flex justify-between items-center hover:border-slate-500 transition-colors">
                    <div>
                        <div class="font-bold text-slate-100">{c.name || c.id} <span class="text-xs ml-2 text-slate-400 font-normal opacity-60">ID: {c.id}</span></div>
                        <div class="text-xs text-slate-400 mt-1 flex items-center gap-3">
                            <span class="px-1.5 py-0.5 rounded bg-amber-500/10 text-amber-400 border border-amber-500/20 uppercase tracking-wider">{c.type}</span>
                            {c.address && <span>{c.address}:{c.port}</span>}
                            {isReadOnly && <span class="text-emerald-500/70"><i class="fas fa-lock mr-1"></i>Read-only</span>}
                        </div>
                    </div>
                    {!isReadOnly && (
                        <div class="flex gap-2">
                            <button class="w-8 h-8 rounded-lg bg-slate-700 hover:bg-slate-600 text-slate-300 flex items-center justify-center transition-colors" onClick={() => setEditingConnection(c)}><i class="fas fa-pen"></i></button>
                            <button class="w-8 h-8 rounded-lg bg-red-500/10 hover:bg-red-500/20 text-red-400 border border-red-500/20 flex items-center justify-center transition-colors" onClick={() => handleDeleteConnection(c.id)}><i class="fas fa-trash"></i></button>
                        </div>
                    )}
                </div>
            ))}
        </div>
    );

    return (
        <div class="p-6 max-w-6xl mx-auto flex flex-col gap-6">
            <datalist id="available-paths">
                {getAvailablePaths().map(p => <option value={p} />)}
            </datalist>
            
            <div class="flex justify-between items-center">
                <div>
                    <h1 class="text-2xl font-bold text-white">System Configuration</h1>
                    <p class="text-slate-400 text-sm mt-1">Manage interactive configurations (saved to override JSON).</p>
                </div>
                <button 
                    class={`px-6 py-2.5 rounded-lg font-bold shadow-lg transition-all ${saving ? 'bg-slate-600 text-slate-400 cursor-not-allowed' : 'bg-emerald-500 hover:bg-emerald-400 text-white shadow-emerald-500/20'}`}
                    onClick={saveConfig}
                    disabled={saving}
                >
                    {saving ? <span><i class="fas fa-spinner fa-spin mr-2"></i>Saving...</span> : <span><i class="fas fa-save mr-2"></i>Save & Apply</span>}
                </button>
            </div>

            <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
                <div class="bg-slate-800/80 border border-slate-700 rounded-xl p-6 shadow-xl backdrop-blur-sm">
                    <div class="flex justify-between items-center mb-6 border-b border-slate-700 pb-4">
                        <h2 class="text-xl font-bold text-slate-100"><i class="fas fa-network-wired mr-2 text-amber-400"></i>Connections</h2>
                        <button class="px-3 py-1.5 bg-amber-500/10 hover:bg-amber-500/20 text-amber-400 border border-amber-500/20 rounded-lg text-sm font-bold transition-colors"
                            onClick={() => setEditingConnection({ id: '', type: 'modbus-tcp', address: '' })}
                        >
                            <i class="fas fa-plus mr-1"></i> Add Connection
                        </button>
                    </div>
                    
                    <h3 class="text-sm font-bold text-slate-400 uppercase tracking-widest mt-4">Configured (Editable)</h3>
                    {(config.override.connections?.length || 0) === 0 ? <div class="text-slate-500 text-sm italic">No custom connections.</div> : renderConnectionList(config.override.connections || [], false)}
                    
                    <h3 class="text-sm font-bold text-slate-400 uppercase tracking-widest mt-6">From pulswerk.json (Read-only)</h3>
                    {renderConnectionList(config.base.connections || [], true)}
                </div>

                <div class="bg-slate-800/80 border border-slate-700 rounded-xl p-6 shadow-xl backdrop-blur-sm">
                    <div class="flex justify-between items-center mb-6 border-b border-slate-700 pb-4">
                        <h2 class="text-xl font-bold text-slate-100"><i class="fas fa-microchip mr-2 text-sky-400"></i>Devices & Telemetry</h2>
                        <button class="px-3 py-1.5 bg-sky-500/10 hover:bg-sky-500/20 text-sky-400 border border-sky-500/20 rounded-lg text-sm font-bold transition-colors"
                            onClick={() => setEditingDevice({ id: '', name: '', deviceType: 'virtual', telemetries: [] })}
                        >
                            <i class="fas fa-plus mr-1"></i> Add Device
                        </button>
                    </div>
                    
                    <DeviceList 
                        baseDevices={config.base.devices || []} 
                        overrideDevices={config.override.devices || []} 
                        onEdit={setEditingDevice}
                        onDelete={handleDeleteDevice}
                    />
                </div>
            </div>

            <EditorModal 
                title={editingConnection?.id ? `Edit Connection: ${editingConnection.id}` : "New Connection"} 
                isOpen={!!editingConnection} 
                onClose={() => setEditingConnection(null)}
                onSave={handleSaveConnection}
            >
                {editingConnection && (
                    <div class="flex flex-col gap-5">
                        <div class="grid grid-cols-2 gap-4">
                            <div class="flex flex-col gap-1.5">
                                <label class="text-xs font-bold text-slate-400 uppercase tracking-wide">Connection ID</label>
                                <input type="text" class="bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-amber-500 focus:outline-none" 
                                    value={editingConnection!.id} onInput={(e) => setEditingConnection({...editingConnection!, id: (e.target as HTMLInputElement).value})} />
                            </div>
                            <div class="flex flex-col gap-1.5">
                                <label class="text-xs font-bold text-slate-400 uppercase tracking-wide">Protocol Type</label>
                                <select class="bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-amber-500 focus:outline-none appearance-none"
                                    value={editingConnection!.type} onChange={(e) => setEditingConnection({...editingConnection!, type: (e.target as HTMLSelectElement).value})}>
                                    <option value="modbus-tcp">Modbus TCP</option>
                                    <option value="bacnet-ip">BACnet/IP</option>
                                </select>
                            </div>
                        </div>

                        <div class="grid grid-cols-2 gap-4">
                            <div class="flex flex-col gap-1.5">
                                <label class="text-xs font-bold text-slate-400 uppercase tracking-wide">Host / Address</label>
                                <input type="text" class="bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-amber-500 focus:outline-none" 
                                    value={editingConnection!.address || editingConnection!.localAddress || ''} 
                                    onInput={(e) => {
                                        const v = (e.target as HTMLInputElement).value;
                                        if (editingConnection!.type === 'bacnet-ip') setEditingConnection({...editingConnection!, localAddress: v});
                                        else setEditingConnection({...editingConnection!, address: v});
                                    }} />
                            </div>
                            <div class="flex flex-col gap-1.5">
                                <label class="text-xs font-bold text-slate-400 uppercase tracking-wide">Port</label>
                                <input type="number" class="bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-amber-500 focus:outline-none" 
                                    value={editingConnection!.port || editingConnection!.localPort || ''} 
                                    onInput={(e) => {
                                        const v = parseInt((e.target as HTMLInputElement).value);
                                        if (editingConnection!.type === 'bacnet-ip') setEditingConnection({...editingConnection!, localPort: v});
                                        else setEditingConnection({...editingConnection!, port: v});
                                    }} />
                            </div>
                        </div>
                    </div>
                )}
            </EditorModal>

            <EditorModal 
                title={editingDevice?.id ? `Edit Device: ${editingDevice.name}` : "New Device"} 
                isOpen={!!editingDevice} 
                onClose={() => setEditingDevice(null)}
                onSave={handleSaveDevice}
            >
                {editingDevice && (
                    <div class="flex flex-col gap-5">
                        <div class="grid grid-cols-2 gap-4">
                            <div class="flex flex-col gap-1.5">
                                <label class="text-xs font-bold text-slate-400 uppercase tracking-wide">Device ID</label>
                                <input type="text" class="bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-sky-500 focus:outline-none" 
                                    value={editingDevice.id} onInput={(e) => setEditingDevice({...editingDevice, id: (e.target as HTMLInputElement).value})} />
                            </div>
                            <div class="flex flex-col gap-1.5">
                                <label class="text-xs font-bold text-slate-400 uppercase tracking-wide">Display Name</label>
                                <input type="text" class="bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-sky-500 focus:outline-none" 
                                    value={editingDevice.name} onInput={(e) => setEditingDevice({...editingDevice, name: (e.target as HTMLInputElement).value})} />
                            </div>
                        </div>

                        <div class="grid grid-cols-2 gap-4">
                            <div class="flex flex-col gap-1.5">
                                <label class="text-xs font-bold text-slate-400 uppercase tracking-wide">Device Type</label>
                                <select class="bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-sky-500 focus:outline-none appearance-none"
                                    value={editingDevice.deviceType} onChange={(e) => setEditingDevice({...editingDevice, deviceType: (e.target as HTMLSelectElement).value})}>
                                    <option value="virtual">Virtual / Analytics</option>
                                    <option value="janitza">Janitza</option>
                                    <option value="deziko">Deziko BACnet</option>
                                    <option value="modbus-tcp">Modbus TCP Generic</option>
                                </select>
                            </div>
                            <div class="flex flex-col gap-1.5">
                                <label class="text-xs font-bold text-slate-400 uppercase tracking-wide">Connection ID</label>
                                <input type="text" class="bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-sky-500 focus:outline-none" 
                                    value={editingDevice.connectionId || ''} placeholder="Optional" onInput={(e) => setEditingDevice({...editingDevice, connectionId: (e.target as HTMLInputElement).value})} />
                            </div>
                        </div>

                        <div class="flex flex-col gap-1.5">
                            <label class="text-xs font-bold text-slate-400 uppercase tracking-wide">Device Path <span class="opacity-50">(optional)</span></label>
                            <input list="available-paths" type="text" class="bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-sky-500 focus:outline-none" 
                                value={editingDevice.path ? editingDevice.path.join('/') : ''} placeholder="e.g. Building/Floor" onInput={(e) => {
                                    const val = (e.target as HTMLInputElement).value;
                                    setEditingDevice({...editingDevice, path: val ? val.split('/').map(s => s.trim()).filter(Boolean) : undefined});
                                }} />
                        </div>

                        {editingDevice.deviceType === 'virtual' && (
                            <div class="mt-4 border-t border-slate-700 pt-5 flex flex-col gap-4">
                                <div class="flex justify-between items-center">
                                    <h3 class="text-sm font-bold text-slate-300 uppercase tracking-widest">Virtual Telemetries</h3>
                                    <button class="px-3 py-1.5 bg-slate-700 hover:bg-slate-600 text-white rounded text-xs transition-colors"
                                        onClick={() => {
                                            const t = [...(editingDevice.telemetries || []), { id: `point_${Date.now().toString().slice(-4)}`, name: 'New Point', formula: '', units: '' }];
                                            setEditingDevice({...editingDevice, telemetries: t});
                                        }}
                                    ><i class="fas fa-plus mr-1"></i> Add Point</button>
                                </div>
                                
                                {(!editingDevice.telemetries || editingDevice.telemetries.length === 0) && (
                                    <div class="p-4 border border-slate-700 border-dashed rounded-lg text-center text-slate-500 text-sm">No virtual telemetries defined.</div>
                                )}
                                
                                <div class="flex flex-col gap-3">
                                    {(editingDevice.telemetries || []).map((t, i: number) => (
                                        <div class="bg-black/20 border border-slate-700 rounded-lg p-4 flex flex-col gap-3 relative">
                                            <button class="absolute top-3 right-3 text-slate-500 hover:text-red-400"
                                                onClick={() => {
                                                    const newT = [...(editingDevice.telemetries || [])];
                                                    newT.splice(i, 1);
                                                    setEditingDevice({...editingDevice, telemetries: newT});
                                                }}
                                            ><i class="fas fa-times"></i></button>
                                            
                                            <div class="grid grid-cols-4 gap-3 pr-6">
                                                <div class="flex flex-col gap-1">
                                                    <label class="text-[10px] uppercase font-bold text-slate-400">Point ID</label>
                                                    <input class="bg-slate-900 border border-slate-600 rounded px-2 py-1 text-sm text-white" value={t.id} 
                                                        onInput={(e) => {
                                                            const newT = [...(editingDevice.telemetries || [])];
                                                            newT[i].id = (e.target as HTMLInputElement).value;
                                                            setEditingDevice({...editingDevice, telemetries: newT});
                                                        }} />
                                                </div>
                                                <div class="flex flex-col gap-1">
                                                    <label class="text-[10px] uppercase font-bold text-slate-400">Name</label>
                                                    <input class="bg-slate-900 border border-slate-600 rounded px-2 py-1 text-sm text-white" value={t.name} 
                                                        onInput={(e) => {
                                                            const newT = [...(editingDevice.telemetries || [])];
                                                            newT[i].name = (e.target as HTMLInputElement).value;
                                                            setEditingDevice({...editingDevice, telemetries: newT});
                                                        }} />
                                                </div>
                                                <div class="flex flex-col gap-1">
                                                    <label class="text-[10px] uppercase font-bold text-slate-400">Path <span class="opacity-50">(optional)</span></label>
                                                    <input list="available-paths" class="bg-slate-900 border border-slate-600 rounded px-2 py-1 text-sm text-white placeholder-slate-600" 
                                                        placeholder="e.g. Building/Floor"
                                                        value={t.path ? t.path.join('/') : ''} 
                                                        onInput={(e) => {
                                                            const val = (e.target as HTMLInputElement).value;
                                                            const newT = [...(editingDevice.telemetries || [])];
                                                            newT[i].path = val ? val.split('/').map((s: string) => s.trim()).filter(Boolean) : null;
                                                            setEditingDevice({...editingDevice, telemetries: newT});
                                                        }} />
                                                </div>
                                                <div class="flex flex-col gap-1">
                                                    <label class="text-[10px] uppercase font-bold text-slate-400">Units</label>
                                                    <input class="bg-slate-900 border border-slate-600 rounded px-2 py-1 text-sm text-white" value={t.units || ''} 
                                                        onInput={(e) => {
                                                            const newT = [...(editingDevice.telemetries || [])];
                                                            newT[i].units = (e.target as HTMLInputElement).value;
                                                            setEditingDevice({...editingDevice, telemetries: newT});
                                                        }} />
                                                </div>
                                            </div>
                                            
                                            <FormulaEditor 
                                                value={t.formula} 
                                                deviceId={editingDevice.id}
                                                availableKeys={availableKeys}
                                                onChange={(val: string) => {
                                                    const newT = [...(editingDevice.telemetries || [])];
                                                    newT[i].formula = val;
                                                    setEditingDevice({...editingDevice, telemetries: newT});
                                                }} 
                                            />
                                        </div>
                                    ))}
                                </div>
                            </div>
                        )}
                    </div>
                )}
            </EditorModal>
        </div>
    );
};

export function initConfigPage() {
    const root = document.getElementById('config-root');
    if (root) {
        render(<ConfigPage />, root);
    }
}

initConfigPage();
