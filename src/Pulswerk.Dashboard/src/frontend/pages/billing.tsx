import { useState, useEffect } from 'preact/hooks';
import { t } from '../i18n';

interface TariffConfig {
    ratePerKwh: number;
    baseMonthlyFee: number;
}

interface RfidMapping {
    idTag: string;
    userName: string;
}

interface Tenant {
    id: string;
    name: string;
    meterKey: string;
}

interface MeterReplacement {
    id: number;
    tenantId: string;
    replacedAt: number;     // unix ms
    replacedAtIso: string;  // ISO 8601
    oldFinalKwh: number | null;
    newStartKwh: number | null;
    note: string | null;
}

interface InvoiceItem {
    type: 'EV Charging' | 'Tenant Meter';
    idTag: string;
    userName: string;
    details: string;
    transactionCount: number;
    totalKwh: number;
    ratePerKwh: number;
    baseFee: number;
    energyCost: number;
    totalCost: number;
    replacementCount?: number;
    billingPeriod: string;
}

export function BillingPage() {
    // Tabs: 'invoices', 'rfid', 'tenants', 'tariffs'
    const [activeTab, setActiveTab] = useState<'invoices' | 'rfids' | 'tenants' | 'tariffs'>('invoices');

    // General state
    const [tariffs, setTariffs] = useState<TariffConfig>({ ratePerKwh: 0.30, baseMonthlyFee: 10.00 });
    const [rfids, setRfids] = useState<RfidMapping[]>([]);
    const [tenants, setTenants] = useState<Tenant[]>([]);
    const [loading, setLoading] = useState(true);

    // Invoices state
    const [selectedYear, setSelectedYear] = useState<number>(new Date().getFullYear());
    const [selectedMonth, setSelectedMonth] = useState<number>(new Date().getMonth() + 1);
    const [invoices, setInvoices] = useState<InvoiceItem[]>([]);
    const [invoiceLoading, setInvoiceLoading] = useState(false);
    const [selectedInvoiceForPrint, setSelectedInvoiceForPrint] = useState<InvoiceItem | null>(null);

    // Add forms state
    const [newRfid, setNewRfid] = useState({ idTag: '', userName: '' });
    const [newTenant, setNewTenant] = useState({ id: '', name: '', meterKey: '' });

    // Inline editing state
    const [editingRfidTag, setEditingRfidTag] = useState<string | null>(null);
    const [editingRfidName, setEditingRfidName] = useState<string>('');
    const [editingTenantId, setEditingTenantId] = useState<string | null>(null);
    const [editingTenantName, setEditingTenantName] = useState<string>('');
    const [editingTenantMeterKey, setEditingTenantMeterKey] = useState<string>('');

    // Meter replacements state
    const [expandedReplacementTenantId, setExpandedReplacementTenantId] = useState<string | null>(null);
    const [replacements, setReplacements] = useState<Record<string, MeterReplacement[]>>({});
    const [newReplacement, setNewReplacement] = useState({ replacedAt: '', oldFinalKwh: '', newStartKwh: '', note: '' });

    const fetchTariffs = async () => {
        try {
            const res = await fetch('/plswk/api/billing/tariffs');
            if (res.ok) setTariffs(await res.json());
        } catch (e) {
            console.error(e);
        }
    };

    const fetchRfids = async () => {
        try {
            const res = await fetch('/plswk/api/billing/rfid');
            if (res.ok) setRfids(await res.json());
        } catch (e) {
            console.error(e);
        }
    };

    const fetchTenants = async () => {
        try {
            const res = await fetch('/plswk/api/billing/tenants');
            if (res.ok) setTenants(await res.json());
        } catch (e) {
            console.error(e);
        }
    };

    const fetchReplacements = async (tenantId: string) => {
        try {
            const res = await fetch(`/plswk/api/billing/meter-replacements?tenantId=${encodeURIComponent(tenantId)}`);
            if (res.ok) {
                const data: MeterReplacement[] = await res.json();
                setReplacements(prev => ({ ...prev, [tenantId]: data }));
            }
        } catch (e) {
            console.error(e);
        }
    };

    const handleAddReplacement = async (tenantId: string, e: Event) => {
        e.preventDefault();
        if (!newReplacement.replacedAt) return;
        const replacedAtMs = new Date(newReplacement.replacedAt).getTime();
        if (isNaN(replacedAtMs)) return;
        try {
            const res = await fetch('/plswk/api/billing/meter-replacements', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    tenantId,
                    replacedAt: replacedAtMs,
                    oldFinalKwh: newReplacement.oldFinalKwh ? parseFloat(newReplacement.oldFinalKwh) : null,
                    newStartKwh: newReplacement.newStartKwh ? parseFloat(newReplacement.newStartKwh) : null,
                    note: newReplacement.note || null,
                })
            });
            if (res.ok) {
                setNewReplacement({ replacedAt: '', oldFinalKwh: '', newStartKwh: '', note: '' });
                fetchReplacements(tenantId);
            }
        } catch (e) { console.error(e); }
    };

    const handleDeleteReplacement = async (tenantId: string, id: number) => {
        if (!confirm('Remove this meter replacement event?')) return;
        try {
            const res = await fetch(`/plswk/api/billing/meter-replacements/${id}`, { method: 'DELETE' });
            if (res.ok) fetchReplacements(tenantId);
        } catch (e) { console.error(e); }
    };

    const loadInvoices = async () => {
        setInvoiceLoading(true);
        try {
            const res = await fetch(`/plswk/api/billing/invoice?year=${selectedYear}&month=${selectedMonth}`);
            if (res.ok) {
                const data = await res.json();
                setInvoices(data.invoices || []);
            }
        } catch (e) {
            console.error(e);
        } finally {
            setInvoiceLoading(false);
        }
    };

    useEffect(() => {
        const init = async () => {
            await Promise.all([fetchTariffs(), fetchRfids(), fetchTenants()]);
            setLoading(false);
        };
        init();
    }, []);

    useEffect(() => {
        if (!loading) {
            loadInvoices();
        }
    }, [selectedYear, selectedMonth, loading]);

    // Handle Forms
    const handleUpdateTariffs = async (e: Event) => {
        e.preventDefault();
        try {
            const res = await fetch('/plswk/api/billing/tariffs', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(tariffs)
            });
            if (res.ok) {
                alert("Tariffs updated successfully!");
                fetchTariffs();
            }
        } catch (e) {
            console.error(e);
        }
    };

    const handleAddRfid = async (e: Event) => {
        e.preventDefault();
        if (!newRfid.idTag || !newRfid.userName) return;
        try {
            const res = await fetch('/plswk/api/billing/rfid', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(newRfid)
            });
            if (res.ok) {
                setNewRfid({ idTag: '', userName: '' });
                fetchRfids();
            }
        } catch (e) {
            console.error(e);
        }
    };

    const handleDeleteRfid = async (idTag: string) => {
        if (!confirm(`Remove RFID card ${idTag}?`)) return;
        try {
            const res = await fetch(`/plswk/api/billing/rfid/${encodeURIComponent(idTag)}`, {
                method: 'DELETE'
            });
            if (res.ok) fetchRfids();
        } catch (e) {
            console.error(e);
        }
    };

    const handleAddTenant = async (e: Event) => {
        e.preventDefault();
        if (!newTenant.id || !newTenant.name || !newTenant.meterKey) return;
        try {
            const res = await fetch('/plswk/api/billing/tenants', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(newTenant)
            });
            if (res.ok) {
                setNewTenant({ id: '', name: '', meterKey: '' });
                fetchTenants();
            }
        } catch (e) {
            console.error(e);
        }
    };

    const handleDeleteTenant = async (id: string) => {
        if (!confirm(`Remove Tenant ${id}?`)) return;
        try {
            const res = await fetch(`/plswk/api/billing/tenants/${encodeURIComponent(id)}`, {
                method: 'DELETE'
            });
            if (res.ok) fetchTenants();
        } catch (e) {
            console.error(e);
        }
    };

    const handleSaveRfidEdit = async (idTag: string) => {
        if (!editingRfidName.trim()) return;
        try {
            const res = await fetch('/plswk/api/billing/rfid', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ idTag, userName: editingRfidName })
            });
            if (res.ok) {
                setEditingRfidTag(null);
                fetchRfids();
            }
        } catch (e) {
            console.error(e);
        }
    };

    const handleSaveTenantEdit = async (id: string) => {
        if (!editingTenantName.trim() || !editingTenantMeterKey.trim()) return;
        try {
            const res = await fetch('/plswk/api/billing/tenants', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ id, name: editingTenantName, meterKey: editingTenantMeterKey })
            });
            if (res.ok) {
                setEditingTenantId(null);
                fetchTenants();
            }
        } catch (e) {
            console.error(e);
        }
    };

    const printInvoice = () => {
        window.print();
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
        <div class="flex flex-col gap-6 w-full page-enter print:p-0">
            {/* Header Tabs */}
            <div class="flex border-b border-slate-700/60 pb-px gap-2 shrink-0 overflow-x-auto print:hidden">
                <button
                    onClick={() => setActiveTab('invoices')}
                    class={`py-2.5 px-4 text-sm font-semibold rounded-t-lg border-b-2 transition-all ${
                        activeTab === 'invoices' ? 'text-sky-400 border-sky-400 bg-sky-400/5' : 'text-slate-400 border-transparent hover:text-slate-200'
                    }`}
                >
                    <i class="fas fa-file-invoice-dollar mr-2"></i>{t('bill_monthly_invoices')}
                </button>
                <button
                    onClick={() => setActiveTab('rfids')}
                    class={`py-2.5 px-4 text-sm font-semibold rounded-t-lg border-b-2 transition-all ${
                        activeTab === 'rfids' ? 'text-sky-400 border-sky-400 bg-sky-400/5' : 'text-slate-400 border-transparent hover:text-slate-200'
                    }`}
                >
                    <i class="fas fa-id-card mr-2"></i>{t('bill_rfid_cards')}
                </button>
                <button
                    onClick={() => setActiveTab('tenants')}
                    class={`py-2.5 px-4 text-sm font-semibold rounded-t-lg border-b-2 transition-all ${
                        activeTab === 'tenants' ? 'text-sky-400 border-sky-400 bg-sky-400/5' : 'text-slate-400 border-transparent hover:text-slate-200'
                    }`}
                >
                    <i class="fas fa-users mr-2"></i>{t('bill_metered_tenants')}
                </button>
                <button
                    onClick={() => setActiveTab('tariffs')}
                    class={`py-2.5 px-4 text-sm font-semibold rounded-t-lg border-b-2 transition-all ${
                        activeTab === 'tariffs' ? 'text-sky-400 border-sky-400 bg-sky-400/5' : 'text-slate-400 border-transparent hover:text-slate-200'
                    }`}
                >
                    <i class="fas fa-sliders-h mr-2"></i>{t('bill_tariff_pricing')}
                </button>
            </div>

            {/* TAB CONTENT: INVOICES */}
            {activeTab === 'invoices' && (
                <div class="flex flex-col gap-4">
                    {/* Month Picker */}
                    <div class="glass p-5 rounded-xl border border-slate-700/60 flex flex-wrap items-center justify-between gap-4 print:hidden">
                        <div class="flex items-center gap-3">
                            <div>
                                <label class="text-[0.65rem] font-bold text-slate-400 uppercase tracking-wider block mb-1">{t('bill_billing_period')}</label>
                                <div class="flex items-center gap-2">
                                    <select
                                        value={selectedYear}
                                        onChange={(e) => setSelectedYear(parseInt((e.target as HTMLSelectElement).value))}
                                        class="bg-slate-900 border border-slate-700 rounded-lg px-3 py-1.5 text-sm text-slate-100 focus:outline-none focus:border-sky-400 font-semibold"
                                    >
                                        {[2024, 2025, 2026, 2027].map(y => <option key={y} value={y}>{y}</option>)}
                                    </select>
                                    <select
                                        value={selectedMonth}
                                        onChange={(e) => setSelectedMonth(parseInt((e.target as HTMLSelectElement).value))}
                                        class="bg-slate-900 border border-slate-700 rounded-lg px-3 py-1.5 text-sm text-slate-100 focus:outline-none focus:border-sky-400 font-semibold"
                                    >
                                        {Array.from({ length: 12 }, (_, i) => i + 1).map(m => (
                                            <option key={m} value={m}>{new Date(2000, m - 1).toLocaleString('default', { month: 'long' })}</option>
                                        ))}
                                    </select>
                                </div>
                            </div>
                        </div>

                        <div class="text-right">
                            <span class="text-xs text-slate-400 font-medium">{t('bill_standard_tariff')}: </span>
                            <span class="text-sm font-bold text-sky-400">{tariffs.ratePerKwh.toFixed(2)} €/kWh &nbsp;·&nbsp; {tariffs.baseMonthlyFee.toFixed(2)} € {t('bill_base')}</span>
                        </div>
                    </div>

                    {/* Invoices List */}
                    <div class="bg-slate-800 border border-slate-700 rounded-xl overflow-hidden print:border-none print:bg-transparent">
                        <div class="px-6 py-4 border-b border-slate-700 bg-black/15 flex justify-between items-center print:hidden">
                            <h3 class="font-bold text-slate-50 text-base">{t('bill_summary')}</h3>
                            <span class="text-xs text-slate-400 font-medium">{invoices.length} {t('bill_accounts_found')}</span>
                        </div>

                        {invoiceLoading ? (
                            <div class="p-16 text-center text-cyan-400">
                                <i class="fas fa-spinner fa-spin text-2xl mr-2"></i>{t('loading')}
                            </div>
                        ) : invoices.length === 0 ? (
                            <div class="p-16 text-center text-slate-500 font-medium">
                                {t('bill_no_records')}
                            </div>
                        ) : (
                            <div class="overflow-x-auto">
                                <table class="lv-table text-sm">
                                    <thead>
                                        <tr>
                                            <th>{t('bill_acc_name')}</th>
                                            <th>{t('bill_type')}</th>
                                            <th>{t('bill_details')}</th>
                                            <th class="text-right">{t('bill_consumption')}</th>
                                            <th class="text-right">{t('bill_energy_cost')}</th>
                                            <th class="text-right">{t('bill_total_cost')}</th>
                                            <th class="text-center print:hidden">{t('bill_action')}</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {invoices.map((inv, idx) => (
                                            <tr key={idx}>
                                                <td class="font-bold text-slate-200">
                                                    {inv.userName}
                                                </td>
                                                <td>
                                                    <div class="flex items-center gap-1.5 flex-wrap">
                                                        <span class={`inline-block text-[0.68rem] font-bold px-2 py-0.5 rounded-full ${
                                                            inv.type === 'EV Charging' ? 'bg-amber-400/10 text-amber-400' : 'bg-sky-400/10 text-sky-400'
                                                        }`}>
                                                            {inv.type === 'EV Charging' ? t('bill_ev_charging') : t('bill_tenant_meter')}
                                                        </span>
                                                        {(inv.replacementCount ?? 0) > 0 && (
                                                            <span
                                                                class="inline-flex items-center gap-1 text-[0.65rem] font-bold px-1.5 py-0.5 rounded-full bg-orange-500/15 text-orange-400"
                                                                title={`Meter replaced ${inv.replacementCount}× during this billing period — consumption split across segments`}
                                                            >
                                                                <i class="fas fa-exchange-alt"></i>
                                                                {inv.replacementCount}× replaced
                                                            </span>
                                                        )}
                                                    </div>
                                                </td>
                                                <td class="text-slate-400 font-mono text-xs">
                                                    {inv.details}
                                                </td>
                                                <td class="text-right font-mono text-slate-100 font-semibold">
                                                    {inv.totalKwh.toFixed(2)} kWh
                                                </td>
                                                <td class="text-right font-mono text-slate-300">
                                                    {inv.energyCost.toFixed(2)} €
                                                </td>
                                                <td class="text-right font-mono text-sky-400 font-bold">
                                                    {inv.totalCost.toFixed(2)} €
                                                </td>
                                                <td class="text-center print:hidden">
                                                    <button
                                                        onClick={() => setSelectedInvoiceForPrint(inv)}
                                                        class="py-1 px-3 rounded bg-slate-700 text-slate-200 text-xs font-semibold hover:bg-slate-600 transition-colors"
                                                    >
                                                        <i class="fas fa-file-invoice mr-1.5"></i>{t('bill_details')}
                                                    </button>
                                                </td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            </div>
                        )}
                    </div>
                </div>
            )}

            {/* TAB CONTENT: RFID CARDS */}
            {activeTab === 'rfids' && (
                <div class="grid grid-cols-1 lg:grid-cols-3 gap-6">
                    {/* Add Mappings Form */}
                    <div class="glass p-6 rounded-xl border border-slate-700/60 h-fit">
                        <h3 class="font-bold text-slate-50 text-base mb-4 border-b border-slate-700 pb-2">{t('bill_reg_rfid')}</h3>
                        <form onSubmit={handleAddRfid} class="flex flex-col gap-4">
                            <div class="flex flex-col gap-1.5">
                                <label class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide">{t('bill_rfid_token')}</label>
                                <input
                                    type="text"
                                    required
                                    value={newRfid.idTag}
                                    onInput={(e) => setNewRfid({ ...newRfid, idTag: (e.target as HTMLInputElement).value })}
                                    placeholder="e.g. 04A1B2C3"
                                    class="form-input font-mono"
                                />
                            </div>

                            <div class="flex flex-col gap-1.5">
                                <label class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide">{t('bill_owner_name')}</label>
                                <input
                                    type="text"
                                    required
                                    value={newRfid.userName}
                                    onInput={(e) => setNewRfid({ ...newRfid, userName: (e.target as HTMLInputElement).value })}
                                    placeholder="e.g. John Doe"
                                    class="form-input"
                                />
                            </div>

                            <button
                                type="submit"
                                class="py-2 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors mt-2"
                            >
                                <i class="fas fa-plus mr-1.5"></i>{t('bill_add_mapping')}
                            </button>
                        </form>
                    </div>

                    {/* Mappings Table */}
                    <div class="bg-slate-800 border border-slate-700 rounded-xl overflow-hidden lg:col-span-2">
                        <div class="px-6 py-4 border-b border-slate-700 bg-black/15">
                            <h3 class="font-bold text-slate-50 text-base">{t('bill_registered_cards')}</h3>
                        </div>
                        <div class="max-h-[500px] overflow-y-auto">
                            {rfids.length === 0 ? (
                                <div class="p-16 text-center text-slate-500">No RFID cards registered yet. Unrecognized cards will be rejected by chargers.</div>
                            ) : (
                                <table class="lv-table text-sm">
                                    <thead>
                                        <tr>
                                            <th>{t('bill_token_id')}</th>
                                            <th>{t('bill_user_name')}</th>
                                            <th class="text-right">{t('bill_actions')}</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {rfids.map((r) => {
                                            const isEditing = r.idTag === editingRfidTag;
                                            return (
                                                <tr key={r.idTag}>
                                                    <td class="font-mono font-bold text-sky-400">
                                                        {r.idTag}
                                                    </td>
                                                    <td>
                                                        {isEditing ? (
                                                            <input
                                                                type="text"
                                                                value={editingRfidName}
                                                                onInput={(e) => setEditingRfidName((e.target as HTMLInputElement).value)}
                                                                class="form-input max-w-[200px]"
                                                            />
                                                        ) : (
                                                            r.userName
                                                        )}
                                                    </td>
                                                    <td class="text-right flex justify-end gap-1.5">
                                                        {isEditing ? (
                                                            <>
                                                                <button
                                                                    onClick={() => handleSaveRfidEdit(r.idTag)}
                                                                    class="p-1 px-2.5 rounded bg-emerald-500/20 text-emerald-400 hover:bg-emerald-500/30 text-xs font-semibold transition-colors"
                                                                >
                                                                    <i class="fas fa-check"></i>
                                                                </button>
                                                                <button
                                                                    onClick={() => setEditingRfidTag(null)}
                                                                    class="p-1 px-2.5 rounded bg-slate-700/40 text-slate-300 hover:bg-slate-700/60 text-xs font-semibold transition-colors"
                                                                >
                                                                    <i class="fas fa-times"></i>
                                                                </button>
                                                            </>
                                                        ) : (
                                                            <>
                                                                <button
                                                                    onClick={() => {
                                                                        setEditingRfidTag(r.idTag);
                                                                        setEditingRfidName(r.userName);
                                                                    }}
                                                                    class="p-1 px-2.5 rounded bg-sky-500/10 text-sky-400 hover:bg-sky-500/20 text-xs font-semibold transition-colors"
                                                                >
                                                                    <i class="fas fa-edit"></i>
                                                                </button>
                                                                <button
                                                                    onClick={() => handleDeleteRfid(r.idTag)}
                                                                    class="p-1 px-2.5 rounded bg-red-500/10 text-red-400 hover:bg-red-500/20 text-xs font-semibold transition-colors"
                                                                >
                                                                    <i class="fas fa-trash-alt"></i>
                                                                </button>
                                                            </>
                                                        )}
                                                    </td>
                                                </tr>
                                            );
                                        })}
                                    </tbody>
                                </table>
                            )}
                        </div>
                    </div>
                </div>
            )}

            {/* TAB CONTENT: METERED TENANTS */}
            {activeTab === 'tenants' && (
                <div class="grid grid-cols-1 lg:grid-cols-3 gap-6">
                    {/* Add Tenant Form */}
                    <div class="glass p-6 rounded-xl border border-slate-700/60 h-fit">
                        <h3 class="font-bold text-slate-50 text-base mb-4 border-b border-slate-700 pb-2">{t('bill_add_tenant')}</h3>
                        <form onSubmit={handleAddTenant} class="flex flex-col gap-4">
                            <div class="flex flex-col gap-1.5">
                                <label class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide">{t('bill_tenant_id')}</label>
                                <input
                                    type="text"
                                    required
                                    value={newTenant.id}
                                    onInput={(e) => setNewTenant({ ...newTenant, id: (e.target as HTMLInputElement).value })}
                                    placeholder="e.g. tenant_101"
                                    class="form-input font-mono"
                                />
                            </div>

                            <div class="flex flex-col gap-1.5">
                                <label class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide">{t('bill_display_name')}</label>
                                <input
                                    type="text"
                                    required
                                    value={newTenant.name}
                                    onInput={(e) => setNewTenant({ ...newTenant, name: (e.target as HTMLInputElement).value })}
                                    placeholder="e.g. Apartment 3B (Miller)"
                                    class="form-input"
                                />
                            </div>

                            <div class="flex flex-col gap-1.5">
                                <label class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide">{t('bill_meter_key')}</label>
                                <input
                                    type="text"
                                    required
                                    value={newTenant.meterKey}
                                    onInput={(e) => setNewTenant({ ...newTenant, meterKey: (e.target as HTMLInputElement).value })}
                                    placeholder="e.g. dev-meter1_energy-active-kwh"
                                    class="form-input font-mono"
                                />
                            </div>

                            <button
                                type="submit"
                                class="py-2 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors mt-2"
                            >
                                <i class="fas fa-plus mr-1.5"></i>{t('bill_add_tenant_btn')}
                            </button>
                        </form>
                    </div>

                    {/* Tenants Table */}
                    <div class="bg-slate-800 border border-slate-700 rounded-xl overflow-hidden lg:col-span-2">
                        <div class="px-6 py-4 border-b border-slate-700 bg-black/15">
                            <h3 class="font-bold text-slate-50 text-base">{t('bill_metered_tenants_title')}</h3>
                        </div>
                        <div class="max-h-[500px] overflow-y-auto">
                            {tenants.length === 0 ? (
                                <div class="p-16 text-center text-slate-500">No metered tenants configured. Monthly consumption query from InfluxDB is disabled.</div>
                            ) : (
                                <table class="lv-table text-sm">
                                    <thead>
                                        <tr>
                                            <th>{t('bill_tenant_id')}</th>
                                            <th>{t('bill_display_name')}</th>
                                            <th>{t('bill_influx_key')}</th>
                                            <th class="text-right">{t('bill_actions')}</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {tenants.map((ten) => {
                                            const isEditing = ten.id === editingTenantId;
                                            const isExpanded = expandedReplacementTenantId === ten.id;
                                            const tenantReplacements = replacements[ten.id] ?? [];
                                            return (
                                                <>
                                                    <tr key={ten.id}>
                                                        <td class="font-mono text-slate-300">
                                                            {ten.id}
                                                        </td>
                                                        <td class="text-slate-100 font-bold">
                                                            {isEditing ? (
                                                                <input
                                                                    type="text"
                                                                    value={editingTenantName}
                                                                    onInput={(e) => setEditingTenantName((e.target as HTMLInputElement).value)}
                                                                    class="form-input w-full"
                                                                />
                                                            ) : (
                                                                ten.name
                                                            )}
                                                        </td>
                                                        <td class="text-sky-400 font-mono text-xs">
                                                            {isEditing ? (
                                                                <input
                                                                    type="text"
                                                                    value={editingTenantMeterKey}
                                                                    onInput={(e) => setEditingTenantMeterKey((e.target as HTMLInputElement).value)}
                                                                    class="form-input w-full font-mono"
                                                                />
                                                            ) : (
                                                                ten.meterKey
                                                            )}
                                                        </td>
                                                        <td class="text-right flex justify-end gap-1.5">
                                                            {/* Replacements toggle */}
                                                            <button
                                                                title="Meter replacements"
                                                                onClick={() => {
                                                                    const next = isExpanded ? null : ten.id;
                                                                    setExpandedReplacementTenantId(next);
                                                                    if (next) fetchReplacements(next);
                                                                }}
                                                                class={`p-1 px-2.5 rounded text-xs font-semibold transition-colors ${
                                                                    isExpanded
                                                                        ? 'bg-orange-500/20 text-orange-400'
                                                                        : 'bg-orange-500/5 text-orange-400/60 hover:bg-orange-500/15'
                                                                }`}
                                                            >
                                                                <i class="fas fa-exchange-alt"></i>
                                                            </button>
                                                            {isEditing ? (
                                                                <>
                                                                    <button
                                                                        onClick={() => handleSaveTenantEdit(ten.id)}
                                                                        class="p-1 px-2.5 rounded bg-emerald-500/20 text-emerald-400 hover:bg-emerald-500/30 text-xs font-semibold transition-colors"
                                                                    >
                                                                        <i class="fas fa-check"></i>
                                                                    </button>
                                                                    <button
                                                                        onClick={() => setEditingTenantId(null)}
                                                                        class="p-1 px-2.5 rounded bg-slate-700/40 text-slate-300 hover:bg-slate-700/60 text-xs font-semibold transition-colors"
                                                                    >
                                                                        <i class="fas fa-times"></i>
                                                                    </button>
                                                                </>
                                                            ) : (
                                                                <>
                                                                    <button
                                                                        onClick={() => {
                                                                            setEditingTenantId(ten.id);
                                                                            setEditingTenantName(ten.name);
                                                                            setEditingTenantMeterKey(ten.meterKey);
                                                                        }}
                                                                        class="p-1 px-2.5 rounded bg-sky-500/10 text-sky-400 hover:bg-sky-500/20 text-xs font-semibold transition-colors"
                                                                    >
                                                                        <i class="fas fa-edit"></i>
                                                                    </button>
                                                                    <button
                                                                        onClick={() => handleDeleteTenant(ten.id)}
                                                                        class="p-1 px-2.5 rounded bg-red-500/10 text-red-400 hover:bg-red-500/20 text-xs font-semibold transition-colors"
                                                                    >
                                                                        <i class="fas fa-trash-alt"></i>
                                                                    </button>
                                                                </>
                                                            )}
                                                        </td>
                                                    </tr>
                                                    {/* Replacements expandable panel */}
                                                    {isExpanded && (
                                                        <tr key={`${ten.id}-replacements`}>
                                                            <td colspan={4} class="p-0">
                                                                <div class="mx-4 mb-3 border border-orange-500/20 rounded-xl bg-orange-500/[0.03] overflow-hidden">
                                                                    <div class="px-4 py-2.5 bg-orange-500/10 border-b border-orange-500/15 flex items-center justify-between">
                                                                        <span class="text-[0.72rem] font-bold text-orange-400 uppercase tracking-wider">
                                                                            <i class="fas fa-exchange-alt mr-1.5"></i>Meter Replacement Events — {ten.name}
                                                                        </span>
                                                                        <span class="text-[0.65rem] text-orange-400/60">{tenantReplacements.length} recorded</span>
                                                                    </div>

                                                                    {/* Existing replacements list */}
                                                                    {tenantReplacements.length > 0 ? (
                                                                        <table class="w-full text-xs">
                                                                            <thead>
                                                                                <tr class="text-[0.62rem] text-slate-500 uppercase tracking-wider border-b border-white/5">
                                                                                    <th class="px-4 py-2 text-left">New Meter Start</th>
                                                                                    <th class="px-4 py-2 text-right">Old Final (kWh)</th>
                                                                                    <th class="px-4 py-2 text-right">New Start (kWh)</th>
                                                                                    <th class="px-4 py-2 text-left">Note</th>
                                                                                    <th class="px-4 py-2"></th>
                                                                                </tr>
                                                                            </thead>
                                                                            <tbody>
                                                                                {tenantReplacements.map(r => (
                                                                                    <tr key={r.id} class="border-b border-white/5 hover:bg-white/[0.02]">
                                                                                        <td class="px-4 py-2 font-mono text-slate-300">
                                                                                            {new Date(r.replacedAt).toLocaleString()}
                                                                                        </td>
                                                                                        <td class="px-4 py-2 text-right font-mono text-slate-400">
                                                                                            {r.oldFinalKwh != null ? r.oldFinalKwh.toFixed(2) : '–'}
                                                                                        </td>
                                                                                        <td class="px-4 py-2 text-right font-mono text-slate-400">
                                                                                            {r.newStartKwh != null ? r.newStartKwh.toFixed(2) : '–'}
                                                                                        </td>
                                                                                        <td class="px-4 py-2 text-slate-500 italic">
                                                                                            {r.note || '–'}
                                                                                        </td>
                                                                                        <td class="px-4 py-2 text-right">
                                                                                            <button
                                                                                                onClick={() => handleDeleteReplacement(ten.id, r.id)}
                                                                                                class="p-1 px-2 rounded bg-red-500/10 text-red-400 hover:bg-red-500/20 text-xs font-semibold transition-colors"
                                                                                                title="Remove event"
                                                                                            >
                                                                                                <i class="fas fa-trash-alt"></i>
                                                                                            </button>
                                                                                        </td>
                                                                                    </tr>
                                                                                ))}
                                                                            </tbody>
                                                                        </table>
                                                                    ) : (
                                                                        <div class="px-4 py-3 text-xs text-slate-600 italic">No replacement events recorded for this tenant.</div>
                                                                    )}

                                                                    {/* Record a new replacement */}
                                                                    <form
                                                                        onSubmit={(e) => handleAddReplacement(ten.id, e)}
                                                                        class="px-4 py-3 border-t border-orange-500/10 flex flex-wrap items-end gap-3"
                                                                    >
                                                                        <div class="flex flex-col gap-1">
                                                                            <label class="text-[0.62rem] font-bold text-slate-500 uppercase tracking-wide">New meter start *</label>
                                                                            <input
                                                                                type="datetime-local"
                                                                                required
                                                                                value={newReplacement.replacedAt}
                                                                                onInput={(e) => setNewReplacement(p => ({ ...p, replacedAt: (e.target as HTMLInputElement).value }))}
                                                                                class="form-input text-xs py-1.5"
                                                                            />
                                                                        </div>
                                                                        <div class="flex flex-col gap-1">
                                                                            <label class="text-[0.62rem] font-bold text-slate-500 uppercase tracking-wide">Old final kWh</label>
                                                                            <input
                                                                                type="number" step="0.001" min="0"
                                                                                value={newReplacement.oldFinalKwh}
                                                                                onInput={(e) => setNewReplacement(p => ({ ...p, oldFinalKwh: (e.target as HTMLInputElement).value }))}
                                                                                placeholder="e.g. 98751.2"
                                                                                class="form-input text-xs py-1.5 font-mono w-32"
                                                                            />
                                                                        </div>
                                                                        <div class="flex flex-col gap-1">
                                                                            <label class="text-[0.62rem] font-bold text-slate-500 uppercase tracking-wide">New start kWh</label>
                                                                            <input
                                                                                type="number" step="0.001" min="0"
                                                                                value={newReplacement.newStartKwh}
                                                                                onInput={(e) => setNewReplacement(p => ({ ...p, newStartKwh: (e.target as HTMLInputElement).value }))}
                                                                                placeholder="e.g. 0.0"
                                                                                class="form-input text-xs py-1.5 font-mono w-32"
                                                                            />
                                                                        </div>
                                                                        <div class="flex flex-col gap-1 flex-1 min-w-[160px]">
                                                                            <label class="text-[0.62rem] font-bold text-slate-500 uppercase tracking-wide">Note</label>
                                                                            <input
                                                                                type="text"
                                                                                value={newReplacement.note}
                                                                                onInput={(e) => setNewReplacement(p => ({ ...p, note: (e.target as HTMLInputElement).value }))}
                                                                                placeholder="e.g. Serial #OLD → #NEW"
                                                                                class="form-input text-xs py-1.5"
                                                                            />
                                                                        </div>
                                                                        <button
                                                                            type="submit"
                                                                            class="py-1.5 px-4 rounded-lg bg-orange-500/20 text-orange-400 hover:bg-orange-500/30 text-xs font-bold transition-colors shrink-0"
                                                                        >
                                                                            <i class="fas fa-plus mr-1"></i>Record
                                                                        </button>
                                                                    </form>
                                                                </div>
                                                            </td>
                                                        </tr>
                                                    )}
                                                </>
                                            );
                                        })}
                                    </tbody>
                                </table>
                            )}
                        </div>
                    </div>
                </div>
            )}

            {/* TAB CONTENT: TARIFF PRICING */}
            {activeTab === 'tariffs' && (
                <div class="glass p-6 rounded-xl border border-slate-700/60 max-w-lg">
                    <h3 class="font-bold text-slate-50 text-base mb-4 border-b border-slate-700 pb-2">{t('bill_tariffs_settings')}</h3>
                    <form onSubmit={handleUpdateTariffs} class="flex flex-col gap-5">
                        <div class="flex flex-col gap-1.5">
                            <label class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide">{t('bill_rate_kwh')}</label>
                            <div class="relative">
                                <span class="absolute left-3 top-2 text-slate-400 text-sm font-semibold">€</span>
                                <input
                                    type="number"
                                    step="0.001"
                                    required
                                    value={tariffs.ratePerKwh}
                                    onInput={(e) => setTariffs({ ...tariffs, ratePerKwh: parseFloat((e.target as HTMLInputElement).value) })}
                                    class="form-input pl-8 font-mono"
                                />
                            </div>
                            <span class="text-[0.68rem] text-slate-500">{t('bill_rate_desc')}</span>
                        </div>

                        <div class="flex flex-col gap-1.5">
                            <label class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide">{t('bill_base_fee')}</label>
                            <div class="relative">
                                <span class="absolute left-3 top-2 text-slate-400 text-sm font-semibold">€</span>
                                <input
                                    type="number"
                                    step="0.01"
                                    required
                                    value={tariffs.baseMonthlyFee}
                                    onInput={(e) => setTariffs({ ...tariffs, baseMonthlyFee: parseFloat((e.target as HTMLInputElement).value) })}
                                    class="form-input pl-8 font-mono"
                                />
                            </div>
                            <span class="text-[0.68rem] text-slate-500">{t('bill_base_desc')}</span>
                        </div>

                        <button
                            type="submit"
                            class="py-2.5 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors mt-2"
                        >
                            <i class="fas fa-check mr-1.5"></i>{t('bill_apply_model')}
                        </button>
                    </form>
                </div>
            )}

            {/* Detailed Invoice Overlay Modal (Printable) */}
            {selectedInvoiceForPrint !== null && (
                <div class="fixed inset-0 bg-black/75 backdrop-blur-sm flex items-center justify-center z-[100] p-4 print:static print:bg-transparent print:p-0 print:block">
                    <div class="bg-slate-800 border border-slate-700 rounded-xl shadow-2xl max-w-2xl w-full p-8 text-slate-100 flex flex-col gap-6 print:border-none print:bg-transparent print:p-0 print:text-black">
                        {/* Printable Header */}
                        <div class="flex justify-between items-start border-b border-slate-700/60 pb-5 print:border-black print:pb-2">
                            <div>
                                <h2 class="text-xl font-extrabold text-slate-50 print:text-black print:text-2xl">{t('bill_invoice_header')}</h2>
                                <p class="text-xs text-slate-400 mt-1 print:text-black font-mono">{t('bill_invoice_date')}: {new Date().toLocaleDateString()} &nbsp;·&nbsp; {t('bill_invoice_period')}: {selectedInvoiceForPrint.billingPeriod}</p>
                            </div>
                            <button onClick={() => setSelectedInvoiceForPrint(null)} class="text-slate-400 hover:text-slate-200 print:hidden">
                                <i class="fas fa-times text-xl"></i>
                            </button>
                        </div>

                        {/* Customer Info */}
                        <div class="grid grid-cols-2 gap-4 bg-black/10 p-4 rounded-lg border border-slate-700/40 print:bg-transparent print:border-none print:p-0">
                            <div>
                                <span class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wider block print:text-black">{t('bill_billed_to')}</span>
                                <strong class="text-slate-100 text-base mt-1 block print:text-black">{selectedInvoiceForPrint.userName}</strong>
                                <span class="text-xs text-slate-400 font-mono print:text-black">{selectedInvoiceForPrint.details}</span>
                            </div>
                            <div class="text-right">
                                <span class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wider block print:text-black">{t('bill_acc_type')}</span>
                                <span class="text-sm font-semibold text-slate-200 mt-1 block print:text-black">
                                    {selectedInvoiceForPrint.type === 'EV Charging' ? t('bill_ev_charging') : t('bill_tenant_meter')}
                                </span>
                            </div>
                        </div>

                        {/* Invoice Table details */}
                        <table class="w-full border-collapse text-sm text-slate-200 print:text-black">
                            <thead>
                                <tr class="text-left text-[0.68rem] font-bold uppercase tracking-wider text-slate-400 border-b border-slate-700 print:border-black print:text-black">
                                    <th class="py-2">{t('bill_desc')}</th>
                                    <th class="py-2 text-right">{t('bill_quantity')}</th>
                                    <th class="py-2 text-right">{t('bill_unit_price')}</th>
                                    <th class="py-2 text-right">{t('bill_total')}</th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr class="border-b border-slate-700/30 print:border-gray-300">
                                    <td class="py-3">
                                        <div class="font-semibold print:text-black">{t('bill_energy_consumption')}</div>
                                        <div class="text-[0.72rem] text-slate-400 print:text-black">{t('bill_metered_active')}</div>
                                    </td>
                                    <td class="py-3 text-right font-mono">{selectedInvoiceForPrint.totalKwh.toFixed(2)} kWh</td>
                                    <td class="py-3 text-right font-mono">{selectedInvoiceForPrint.ratePerKwh.toFixed(2)} €</td>
                                    <td class="py-3 text-right font-mono font-semibold">{selectedInvoiceForPrint.energyCost.toFixed(2)} €</td>
                                </tr>
                                <tr class="border-b border-slate-700/30 print:border-gray-300">
                                    <td class="py-3">
                                        <div class="font-semibold print:text-black">{t('bill_base_service_fee')}</div>
                                        <div class="text-[0.72rem] text-slate-400 print:text-black">{t('bill_grid_flatrate')}</div>
                                    </td>
                                    <td class="py-3 text-right font-mono">1 month</td>
                                    <td class="py-3 text-right font-mono">{selectedInvoiceForPrint.baseFee.toFixed(2)} €</td>
                                    <td class="py-3 text-right font-mono font-semibold">{selectedInvoiceForPrint.baseFee.toFixed(2)} €</td>
                                </tr>
                            </tbody>
                        </table>

                        {/* Grand Total */}
                        <div class="flex justify-end items-center gap-6 mt-2 border-t border-slate-700/60 pt-4 print:border-black">
                            <span class="text-sm font-bold text-slate-400 print:text-black">{t('bill_total_due')}</span>
                            <span class="text-2xl font-black text-sky-400 print:text-black">{selectedInvoiceForPrint.totalCost.toFixed(2)} €</span>
                        </div>

                        {/* Action buttons */}
                        <div class="flex items-center justify-end gap-2 border-t border-slate-700/60 pt-4 mt-2 print:hidden">
                            <button
                                onClick={() => setSelectedInvoiceForPrint(null)}
                                class="py-2 px-4 rounded-lg bg-slate-700 text-slate-200 text-xs font-semibold hover:bg-slate-600 transition-colors"
                            >
                                {t('bill_close_btn')}
                            </button>
                            <button
                                onClick={printInvoice}
                                class="py-2 px-4 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors"
                            >
                                <i class="fas fa-print mr-1.5"></i>{t('bill_print_btn')}
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
}
