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
    // Extended fields surfaced by the per-type editor
    deviceId?: number;
    address?: string;
    assetType?: string;
    pollIntervalSeconds?: number;
    writeback?: boolean;
    knxGroupAddressXml?: string;
}

interface ConnectionConfig {
    id: string;
    type: string;
    address?: string;
    port?: number;
    localAddress?: string;
    localPort?: number;
    localDeviceId?: number;
    name?: string;
    knxSecureEnabled?: boolean;
    knxCommissioningPassword?: string;
    knxMaxConcurrentConnects?: number;
    knxStaleSeconds?: number;
}

interface ModulesConfig {
    ems?: boolean;
    billing?: boolean;
    wallbox?: boolean;
    historicalData?: boolean;
    alarms?: boolean;
    logs?: boolean;
    heartbeat?: boolean;
    dashboards?: boolean;
    assets?: boolean;
    telemetry?: boolean;
    connections?: boolean;
}

interface AppConfig {
    connections?: ConnectionConfig[];
    devices?: DeviceConfig[];
    modules?: ModulesConfig;
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

// ── Backend metadata types (mirror ConnectionTypeMetadata.cs) ────────────────

interface ConnectionField {
    key: string;
    label: string;
    type: 'text' | 'number' | 'password' | 'select' | 'checkbox';
    required: boolean;
    placeholder?: string;
    help?: string;
    default?: string;
    options?: string[];
}

interface ConnectionTypeMeta {
    type: string;
    label: string;
    icon: string;
    defaultPort: number;
    fields: ConnectionField[];
}

interface DeviceField {
    key: string;
    label: string;
    type: 'text' | 'number' | 'password' | 'select' | 'checkbox';
    required: boolean;
    placeholder?: string;
    help?: string;
    default?: string;
    options?: string[];
}

interface DeviceTypeMeta {
    type: string;
    label: string;
    icon: string;
    compatibleConnections: string[];
    fields: DeviceField[];
}

// ── Network scan result types ───────────────────────────────────────────────

interface ScanResultProtocol {
    type: string;
    label: string;
    port: number;
}

interface ScanResultHost {
    ip: string;
    protocols: ScanResultProtocol[];
}

interface NetworkScanResult {
    range: string;
    hostsScanned: number;
    hostsFound: number;
    hosts: ScanResultHost[];
    durationMs: number;
}

// ── Module metadata ──────────────────────────────────────────────────────────

const MODULE_META: Array<{ key: keyof ModulesConfig; label: string; icon: string; description: string }> = [
    { key: 'dashboards',    label: 'Dashboards',      icon: 'fa-table-cells-large', description: 'Custom dashboard builder & widget layout' },
    { key: 'assets',        label: 'Assets',           icon: 'fa-sitemap',          description: 'Asset hierarchy browser & property editor' },
    { key: 'telemetry',     label: 'Telemetry',        icon: 'fa-database',         description: 'Telemetry list, search, and CRUD management' },
    { key: 'historicalData',label: 'Historical Data',  icon: 'fa-chart-line',       description: 'Time-series query, charts, and data export' },
    { key: 'alarms',        label: 'Alarms',           icon: 'fa-bell',             description: 'Active alarm monitoring and acknowledgement' },
    { key: 'logs',          label: 'System Logs',      icon: 'fa-scroll',           description: 'Live system log stream and log history' },
    { key: 'heartbeat',     label: 'Heartbeat',        icon: 'fa-heart-pulse',      description: 'System health, uptime, and performance stats' },
    { key: 'billing',       label: 'Billing',          icon: 'fa-dollar-sign',      description: 'RFID card billing and tenant energy invoicing' },
    { key: 'wallbox',       label: 'Wallboxes',        icon: 'fa-charging-station', description: 'OCPP EV chargepoint management and monitoring' },
    { key: 'ems',           label: 'Energy Management',icon: 'fa-bolt',             description: 'Trajectory control, targets, and curtailment' },
    { key: 'connections',   label: 'Connections',      icon: 'fa-network-wired',    description: 'Device connection management and diagnostics' },
];

const DEFAULT_MODULES: Required<ModulesConfig> = {
    ems: true, billing: true, wallbox: true, historicalData: true,
    alarms: true, logs: true, heartbeat: true, dashboards: true,
    assets: true, telemetry: true, connections: true,
};

// ── Modules panel ─────────────────────────────────────────────────────────────

interface ModulesPanelProps {
    modules: Required<ModulesConfig>;
    onChange: (key: keyof ModulesConfig, enabled: boolean) => void;
}

const ModulesPanel = ({ modules, onChange }: ModulesPanelProps) => (
    <div class="bg-slate-800/80 border border-slate-700 rounded-xl p-6 shadow-xl backdrop-blur-sm">
        <div class="flex items-center gap-3 mb-6 border-b border-slate-700 pb-4">
            <h2 class="text-xl font-bold text-slate-100 flex-1">
                <i class="fas fa-puzzle-piece mr-2 text-violet-400"></i>Feature Modules
            </h2>
            <span class="text-xs text-slate-500">
                {Object.values(modules).filter(Boolean).length} / {MODULE_META.length} enabled
            </span>
        </div>

        <div class="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-3">
            {MODULE_META.map(({ key, label, icon, description }) => {
                const enabled = modules[key];
                return (
                    <div
                        key={key}
                        class={`flex items-start gap-3 p-4 rounded-lg border transition-all cursor-pointer select-none ${
                            enabled
                                ? 'bg-slate-700/40 border-slate-600 hover:border-slate-500'
                                : 'bg-slate-900/40 border-slate-700/50 opacity-60 hover:opacity-75'
                        }`}
                        onClick={() => onChange(key, !enabled)}
                    >
                        {/* Icon */}
                        <div class={`w-9 h-9 rounded-lg flex items-center justify-center flex-shrink-0 transition-colors ${
                            enabled ? 'bg-violet-500/20 text-violet-400' : 'bg-slate-700/50 text-slate-500'
                        }`}>
                            <i class={`fas ${icon} text-sm`}></i>
                        </div>

                        {/* Text */}
                        <div class="flex-1 min-w-0">
                            <div class={`font-semibold text-sm ${enabled ? 'text-slate-100' : 'text-slate-400'}`}>{label}</div>
                            <div class="text-xs text-slate-500 mt-0.5 leading-relaxed">{description}</div>
                        </div>

                        {/* Toggle */}
                        <div class="flex-shrink-0 mt-0.5">
                            <div class={`w-9 h-5 rounded-full relative transition-colors ${
                                enabled ? 'bg-violet-500' : 'bg-slate-600'
                            }`}>
                                <div class={`absolute top-0.5 w-4 h-4 rounded-full bg-white shadow-md transition-transform ${
                                    enabled ? 'translate-x-4' : 'translate-x-0.5'
                                }`} />
                            </div>
                        </div>
                    </div>
                );
            })}
        </div>
    </div>
);

// ── Editor modal ──────────────────────────────────────────────────────────────

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

// ── Generic field renderer (connection & device editors) ────────────────────

interface FieldRendererProps {
    field: ConnectionField | DeviceField;
    value: any;
    onChange: (val: any) => void;
    accent: 'amber' | 'sky';
    options?: string[]; // for select fields that depend on external data (e.g. connections)
}

const FieldRenderer = ({ field, value, onChange, accent, options }: FieldRendererProps) => {
    const accentClass = accent === 'amber' ? 'focus:border-amber-500' : 'focus:border-sky-500';
    const baseInput = `bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm ${accentClass} focus:outline-none`;

    return (
        <div class="flex flex-col gap-1.5">
            <label class="text-xs font-bold text-slate-400 uppercase tracking-wide">
                {field.label}
                {field.required && <span class="text-red-400 ml-0.5">*</span>}
            </label>
            {field.type === 'checkbox' ? (
                <label class="flex items-center gap-2 cursor-pointer select-none py-1">
                    <input type="checkbox" checked={!!value}
                        onChange={(e) => onChange((e.target as HTMLInputElement).checked)}
                        class="w-4 h-4 rounded accent-amber-500" />
                    <span class="text-sm text-slate-300">Enabled</span>
                </label>
            ) : field.type === 'select' ? (
                <select class={`${baseInput} appearance-none`}
                    value={value || ''}
                    onChange={(e) => onChange((e.target as HTMLSelectElement).value)}>
                    <option value="">— Select —</option>
                    {(options || field.options || []).map(o => <option value={o}>{o}</option>)}
                </select>
            ) : (
                <input
                    type={field.type === 'password' ? 'password' : field.type === 'number' ? 'number' : 'text'}
                    class={baseInput}
                    value={value ?? ''}
                    placeholder={field.placeholder}
                    onInput={(e) => {
                        const v = (e.target as HTMLInputElement).value;
                        onChange(field.type === 'number' ? (v === '' ? undefined : Number(v)) : v);
                    }} />
            )}
            {field.help && <span class="text-[11px] text-slate-500 leading-relaxed">{field.help}</span>}
        </div>
    );
};

// ── Network scan modal ──────────────────────────────────────────────────────

interface ScanModalProps {
    isOpen: boolean;
    onClose: () => void;
    onAddConnections: (hosts: ScanResultHost[]) => void;
}

const ScanModal = ({ isOpen, onClose, onAddConnections }: ScanModalProps) => {
    const [range, setRange] = useState('192.168.1.0/24');
    const [scanning, setScanning] = useState(false);
    const [result, setResult] = useState<NetworkScanResult | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [selected, setSelected] = useState<Set<string>>(new Set());

    const runScan = async () => {
        if (!range.trim()) return;
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
            const data: NetworkScanResult = await res.json();
            setResult(data);
            // Pre-select all found hosts
            setSelected(new Set(data.hosts.map(h => h.ip)));
        } catch (e: any) {
            setError(e.message || 'Scan failed');
        } finally {
            setScanning(false);
        }
    };

    const toggleHost = (ip: string) => {
        setSelected(prev => {
            const next = new Set(prev);
            if (next.has(ip)) next.delete(ip); else next.add(ip);
            return next;
        });
    };

    const handleAdd = () => {
        const chosen = (result?.hosts || []).filter(h => selected.has(h.ip));
        if (chosen.length === 0) return;
        onAddConnections(chosen);
        onClose();
        setResult(null);
        setSelected(new Set());
    };

    if (!isOpen) return null;

    return (
        <div class="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-center justify-center">
            <div class="bg-slate-800 border border-slate-600 rounded-xl w-full max-w-3xl max-h-[90vh] flex flex-col shadow-2xl">
                <div class="px-6 py-4 border-b border-slate-700 flex justify-between items-center bg-black/20 rounded-t-xl">
                    <h2 class="text-lg font-bold text-slate-100">
                        <i class="fas fa-satellite-dish mr-2 text-cyan-400"></i>Discover Devices on Network
                    </h2>
                    <button class="text-slate-400 hover:text-white" onClick={onClose}><i class="fas fa-times"></i></button>
                </div>

                <div class="p-6 overflow-y-auto flex-1 custom-scrollbar flex flex-col gap-5">
                    {/* Range input */}
                    <div class="flex flex-col gap-2">
                        <label class="text-xs font-bold text-slate-400 uppercase tracking-wide">IP Range / CIDR</label>
                        <div class="flex gap-2">
                            <input type="text" class="flex-1 bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm font-mono focus:border-cyan-500 focus:outline-none"
                                value={range} onInput={(e) => setRange((e.target as HTMLInputElement).value)}
                                placeholder="192.168.1.0/24 or 192.168.1.1-254" />
                            <button class="px-5 py-2 bg-cyan-500 hover:bg-cyan-400 text-white rounded-lg font-bold shadow-lg shadow-cyan-500/20 transition-all disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2"
                                onClick={runScan} disabled={scanning || !range.trim()}>
                                {scanning ? <><i class="fas fa-spinner fa-spin"></i> Scanning…</> : <><i class="fas fa-radar"></i> Scan</>}
                            </button>
                        </div>
                        <p class="text-[11px] text-slate-500">
                            Accepted: CIDR (<code>192.168.1.0/24</code>), last-octet range (<code>192.168.1.1-254</code>),
                            explicit range (<code>10.0.0.1-10.0.0.20</code>), or a single host.
                            Probes ports: Modbus 502 · BACnet 47808 · KNX 3671 · OCPP 9000.
                        </p>
                    </div>

                    {error && (
                        <div class="p-3 rounded-lg bg-red-500/10 border border-red-500/20 text-red-400 text-sm">
                            <i class="fas fa-exclamation-triangle mr-2"></i>{error}
                        </div>
                    )}

                    {result && (
                        <div class="flex flex-col gap-3">
                            <div class="flex items-center justify-between text-xs text-slate-400">
                                <span>
                                    Scanned <strong class="text-slate-200">{result.hostsScanned}</strong> addresses in {result.durationMs} ms
                                </span>
                                <span class={result.hostsFound > 0 ? 'text-emerald-400' : 'text-slate-500'}>
                                    <i class="fas fa-circle-check mr-1"></i>{result.hostsFound} host{result.hostsFound !== 1 ? 's' : ''} found
                                </span>
                            </div>

                            {result.hosts.length === 0 ? (
                                <div class="p-6 border border-slate-700 border-dashed rounded-lg text-center text-slate-500 text-sm">
                                    <i class="fas fa-magnifying-glass text-2xl block mb-2 opacity-50"></i>
                                    No devices responded on known protocol ports in this range.
                                </div>
                            ) : (
                                <div class="flex flex-col gap-2">
                                    {result.hosts.map(h => {
                                        const isSel = selected.has(h.ip);
                                        return (
                                            <label class={`p-3 rounded-lg border flex items-center gap-3 cursor-pointer transition-colors ${
                                                isSel ? 'bg-cyan-500/10 border-cyan-500/40' : 'bg-slate-900/40 border-slate-700 hover:border-slate-500'
                                            }`}>
                                                <input type="checkbox" checked={isSel} onChange={() => toggleHost(h.ip)}
                                                    class="w-4 h-4 accent-cyan-500" />
                                                <div class="flex-1">
                                                    <div class="font-mono text-sm text-slate-100">{h.ip}</div>
                                                    <div class="flex flex-wrap gap-1.5 mt-1">
                                                        {h.protocols.map(p => (
                                                            <span class="text-[10px] px-1.5 py-0.5 rounded bg-slate-700/60 text-slate-300 border border-slate-600 uppercase tracking-wider">
                                                                {p.label} :{p.port}
                                                            </span>
                                                        ))}
                                                    </div>
                                                </div>
                                            </label>
                                        );
                                    })}
                                </div>
                            )}
                        </div>
                    )}
                </div>

                <div class="px-6 py-4 border-t border-slate-700 flex justify-between items-center bg-black/20 rounded-b-xl">
                    <span class="text-xs text-slate-500">
                        {selected.size > 0 ? `${selected.size} host${selected.size !== 1 ? 's' : ''} selected` : ''}
                    </span>
                    <div class="flex gap-3">
                        <button class="px-4 py-2 rounded-lg text-slate-300 hover:bg-white/5 transition-colors" onClick={onClose}>Cancel</button>
                        <button class="px-5 py-2 bg-emerald-500 hover:bg-emerald-400 text-white rounded-lg font-medium shadow-lg shadow-emerald-500/20 transition-all disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2"
                            onClick={handleAdd} disabled={selected.size === 0}>
                            <i class="fas fa-plus"></i> Add {selected.size > 0 ? `(${selected.size})` : ''}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

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
    const [modules, setModules] = useState<Required<ModulesConfig>>({ ...DEFAULT_MODULES });

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
    const [scanOpen, setScanOpen] = useState(false);
    const [connectionTypes, setConnectionTypes] = useState<ConnectionTypeMeta[]>([]);
    const [deviceTypes, setDeviceTypes] = useState<DeviceTypeMeta[]>([]);

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

            // Derive effective module state: base defaults → override patch
            const baseModules: Required<ModulesConfig> = { ...DEFAULT_MODULES, ...(data.base.modules ?? {}) };
            const effective: Required<ModulesConfig> = { ...baseModules, ...(data.override.modules ?? {}) };
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
            if (ctRes.ok) setConnectionTypes(await ctRes.json());
            if (dtRes.ok) setDeviceTypes(await dtRes.json());
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
            // Include the current module toggles in the override payload
            const payload = { ...config.override, modules };
            const res = await fetch('/plswk/api/config/override', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });
            if (res.ok) {
                // Notify other tabs/windows (main SPA) that modules changed
                try { localStorage.setItem('pw_modules_updated', Date.now().toString()); } catch (_) {}
                window.pwToast("Configuration saved. Applying module changes…");
                // Brief pause so the toast is visible, then hard-reload so the main
                // SPA re-fetches /api/user/identity and the nav reflects the new state.
                setTimeout(() => { window.location.reload(); }, 800);
            } else {
                window.pwToast("Failed to save configuration.", "error");
            }
        } catch(e) {
            console.error(e);
        } finally {
            setSaving(false);
        }
    };

    const handleModuleToggle = (key: keyof ModulesConfig, enabled: boolean) => {
        setModules(prev => ({ ...prev, [key]: enabled }));
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

    // Bulk-add connections discovered by the network scan. Each selected host
    // becomes a new connection of the first detected protocol type, pre-filled
    // with the host IP and the protocol's default port. The user can then refine
    // the parameters in the connection editor.
    const handleAddScannedHosts = (hosts: ScanResultHost[]) => {
        setConfig(prev => {
            if (!prev) return prev;
            const newOverride = { ...prev.override, connections: [...(prev.override.connections || [])] };
            const existingIds = new Set(newOverride.connections.map(c => c.id));
            const baseIds = new Set((prev.base.connections || []).map(c => c.id));

            for (const host of hosts) {
                const proto = host.protocols[0];
                if (!proto) continue;
                // Derive a unique connection id from the IP
                let baseId = `scan-${proto.type}-${host.ip.replace(/\./g, '-')}`;
                let id = baseId, n = 2;
                while (existingIds.has(id) || baseIds.has(id)) { id = `${baseId}-${n++}`; }
                existingIds.add(id);

                const meta = connectionTypes.find(t => t.type === proto.type);
                const conn: ConnectionConfig = { id, type: proto.type, name: `${meta?.label || proto.type} ${host.ip}` };
                // Populate the right address/port fields per protocol
                if (proto.type === 'bacnet-ip' || proto.type === 'ocpp-ws') {
                    conn.localAddress = '0.0.0.0';
                    conn.localPort = proto.port;
                } else {
                    conn.address = host.ip;
                    conn.port = proto.port;
                }
                newOverride.connections.push(conn);
            }
            return { ...prev, override: newOverride };
        });
        window.pwToast(`Added ${hosts.length} connection${hosts.length !== 1 ? 's' : ''} from scan. Review and save.`);
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

            <ModulesPanel modules={modules} onChange={handleModuleToggle} />

            <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
                <div class="bg-slate-800/80 border border-slate-700 rounded-xl p-6 shadow-xl backdrop-blur-sm">
                    <div class="flex justify-between items-center mb-6 border-b border-slate-700 pb-4">
                        <h2 class="text-xl font-bold text-slate-100"><i class="fas fa-network-wired mr-2 text-amber-400"></i>Connections</h2>
                        <div class="flex gap-2">
                            <button class="px-3 py-1.5 bg-cyan-500/10 hover:bg-cyan-500/20 text-cyan-400 border border-cyan-500/20 rounded-lg text-sm font-bold transition-colors"
                                onClick={() => setScanOpen(true)}
                            >
                                <i class="fas fa-satellite-dish mr-1"></i> Discover Devices
                            </button>
                            <button class="px-3 py-1.5 bg-amber-500/10 hover:bg-amber-500/20 text-amber-400 border border-amber-500/20 rounded-lg text-sm font-bold transition-colors"
                                onClick={() => setEditingConnection({ id: '', type: 'modbus-tcp', address: '' })}
                            >
                                <i class="fas fa-plus mr-1"></i> Add
                            </button>
                        </div>
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

            <ScanModal isOpen={scanOpen} onClose={() => setScanOpen(false)} onAddConnections={handleAddScannedHosts} />

            <EditorModal 
                title={editingConnection?.id ? `Edit Connection: ${editingConnection.id}` : "New Connection"} 
                isOpen={!!editingConnection} 
                onClose={() => setEditingConnection(null)}
                onSave={handleSaveConnection}
            >
                {editingConnection && (() => {
                    const meta = connectionTypes.find(t => t.type === editingConnection.type);
                    const fields = meta?.fields || [];
                    // Split fields: id & type always first (rendered manually), rest from metadata
                    const dynamicFields = fields.filter(f => f.key !== 'id' && f.key !== 'type');
                    return (
                        <div class="flex flex-col gap-5">
                            <div class="grid grid-cols-2 gap-4">
                                <div class="flex flex-col gap-1.5">
                                    <label class="text-xs font-bold text-slate-400 uppercase tracking-wide">Connection ID <span class="text-red-400">*</span></label>
                                    <input type="text" class="bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-amber-500 focus:outline-none" 
                                        value={editingConnection.id} onInput={(e) => setEditingConnection({...editingConnection, id: (e.target as HTMLInputElement).value})} />
                                </div>
                                <div class="flex flex-col gap-1.5">
                                    <label class="text-xs font-bold text-slate-400 uppercase tracking-wide">Protocol Type <span class="text-red-400">*</span></label>
                                    <select class="bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-amber-500 focus:outline-none appearance-none"
                                        value={editingConnection.type} onChange={(e) => {
                                            const newType = (e.target as HTMLSelectElement).value;
                                            const newMeta = connectionTypes.find(t => t.type === newType);
                                            // Reset type-specific fields and apply default port
                                            const updated: ConnectionConfig = { id: editingConnection.id, type: newType, name: editingConnection.name };
                                            if (newMeta) {
                                                if (newType === 'bacnet-ip' || newType === 'ocpp-ws') {
                                                    updated.localAddress = '0.0.0.0';
                                                    updated.localPort = newMeta.defaultPort;
                                                } else {
                                                    updated.port = newMeta.defaultPort;
                                                }
                                            }
                                            setEditingConnection(updated);
                                        }}>
                                        {connectionTypes.map(t => <option value={t.type}>{t.label}</option>)}
                                        {connectionTypes.length === 0 && <>
                                            <option value="modbus-tcp">Modbus TCP</option>
                                            <option value="bacnet-ip">BACnet/IP</option>
                                            <option value="knx-ip">KNXnet/IP</option>
                                            <option value="ocpp-ws">OCPP (WS)</option>
                                        </>}
                                    </select>
                                </div>
                            </div>

                            {dynamicFields.length > 0 && (
                                <div class="grid grid-cols-2 gap-4">
                                    {dynamicFields.map(field => (
                                        <FieldRenderer
                                            key={field.key}
                                            field={field}
                                            accent="amber"
                                            value={(editingConnection as any)[field.key]}
                                            onChange={(val) => setEditingConnection({...editingConnection, [field.key]: val})}
                                        />
                                    ))}
                                </div>
                            )}
                        </div>
                    );
                })()}
            </EditorModal>

            <EditorModal 
                title={editingDevice?.id ? `Edit Device: ${editingDevice.name}` : "New Device"} 
                isOpen={!!editingDevice} 
                onClose={() => setEditingDevice(null)}
                onSave={handleSaveDevice}
            >
                {editingDevice && (() => {
                    const meta = deviceTypes.find(t => t.type === editingDevice.deviceType);
                    const fields = meta?.fields || [];
                    const dynamicFields = fields.filter(f => f.key !== 'id' && f.key !== 'name' && f.key !== 'deviceType');
                    // Build connection options from compatible connection types
                    const allConns = [...(config?.base.connections || []), ...(config?.override.connections || [])];
                    const connOptions = allConns
                        .filter(c => !meta || meta.compatibleConnections.length === 0 || meta.compatibleConnections.includes(c.type))
                        .map(c => c.id);
                    return (
                        <div class="flex flex-col gap-5">
                            <div class="grid grid-cols-2 gap-4">
                                <div class="flex flex-col gap-1.5">
                                    <label class="text-xs font-bold text-slate-400 uppercase tracking-wide">Device ID <span class="text-red-400">*</span></label>
                                    <input type="text" class="bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-sky-500 focus:outline-none" 
                                        value={editingDevice.id} onInput={(e) => setEditingDevice({...editingDevice, id: (e.target as HTMLInputElement).value})} />
                                </div>
                                <div class="flex flex-col gap-1.5">
                                    <label class="text-xs font-bold text-slate-400 uppercase tracking-wide">Display Name <span class="text-red-400">*</span></label>
                                    <input type="text" class="bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-sky-500 focus:outline-none" 
                                        value={editingDevice.name} onInput={(e) => setEditingDevice({...editingDevice, name: (e.target as HTMLInputElement).value})} />
                                </div>
                            </div>

                            <div class="grid grid-cols-2 gap-4">
                                <div class="flex flex-col gap-1.5">
                                    <label class="text-xs font-bold text-slate-400 uppercase tracking-wide">Device Type <span class="text-red-400">*</span></label>
                                    <select class="bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-slate-100 text-sm focus:border-sky-500 focus:outline-none appearance-none"
                                        value={editingDevice.deviceType} onChange={(e) => {
                                            const newType = (e.target as HTMLSelectElement).value;
                                            // Clear type-specific fields when switching types
                                            setEditingDevice({ id: editingDevice.id, name: editingDevice.name, deviceType: newType, telemetries: newType === 'virtual' ? (editingDevice.telemetries || []) : undefined });
                                        }}>
                                        {deviceTypes.map(t => <option value={t.type}>{t.label}</option>)}
                                        {deviceTypes.length === 0 && <>
                                            <option value="virtual">Virtual / Analytics</option>
                                            <option value="janitza">Janitza</option>
                                            <option value="deziko">Deziko BACnet</option>
                                        </>}
                                    </select>
                                </div>
                                {dynamicFields.find(f => f.key === 'connectionId') && (
                                    <FieldRenderer
                                        field={dynamicFields.find(f => f.key === 'connectionId')!}
                                        accent="sky"
                                        value={editingDevice.connectionId}
                                        options={connOptions}
                                        onChange={(val) => setEditingDevice({...editingDevice, connectionId: val})}
                                    />
                                )}
                            </div>

                            {dynamicFields.filter(f => f.key !== 'connectionId').length > 0 && (
                                <div class="grid grid-cols-2 gap-4">
                                    {dynamicFields.filter(f => f.key !== 'connectionId').map(field => (
                                        <FieldRenderer
                                            key={field.key}
                                            field={field}
                                            accent="sky"
                                            value={(editingDevice as any)[field.key]}
                                            onChange={(val) => {
                                                if (field.key === 'path') {
                                                    setEditingDevice({...editingDevice, path: val ? String(val).split('/').map((s: string) => s.trim()).filter(Boolean) : undefined});
                                                } else {
                                                    setEditingDevice({...editingDevice, [field.key]: val});
                                                }
                                            }}
                                        />
                                    ))}
                                </div>
                            )}

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
                    );
                })()}
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

export { ConfigPage };
