import { useState, useEffect } from 'preact/hooks';
import { Fragment } from 'preact';
import { createPortal } from 'preact/compat';
import { t } from '../i18n';

interface TariffConfig {
    ratePerKwh: number;
    baseMonthlyFee: number;
}

interface Customer {
    id: string;
    name: string;
    address: string | null;
    email: string | null;
    note: string | null;
}

interface RfidMapping {
    idTag: string;
    userName: string;
    customerId: string | null;
    customerName: string | null;
}

interface Tenant {
    id: string;
    name: string;
    meterKey: string;
    customerId: string | null;
    customerName: string | null;
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

interface InvoiceLineItem {
    date: string;
    description: string;
    idTag: string;
    quantity: number;
    unit: string;
    unitPrice: number;
    amount: number;
}

interface Invoice {
    invoiceId: string;
    source: 'ev' | 'meter';
    type: 'EV Charging' | 'Tenant Meter';
    customerId: string | null;
    customerName: string;
    customerAddress: string | null;
    customerEmail: string | null;
    details: string;
    transactionCount: number;
    totalKwh: number;
    ratePerKwh: number;
    baseFee: number;
    energyCost: number;
    totalCost: number;
    replacementCount: number;
    billingPeriod: string;
    lineItems: InvoiceLineItem[];
}

interface CustomerGroup {
    key: string;
    customerId: string | null;
    customerName: string;
    customerAddress: string | null;
    customerEmail: string | null;
    invoices: Invoice[];
    totalKwh: number;
    energyCost: number;
    baseFee: number;
    totalCost: number;
}

const money = (v: number) => `${v.toFixed(2)} €`;
const kwh = (v: number) => `${v.toFixed(2)} kWh`;

interface ModalProps {
    title: string;
    icon: string;
    onClose: () => void;
    children: any;
}

function Modal({ title, icon, onClose, children }: ModalProps) {
    useEffect(() => {
        const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
        window.addEventListener('keydown', onKey);
        return () => window.removeEventListener('keydown', onKey);
    }, [onClose]);

    return createPortal(
        <div
            class="fixed inset-0 bg-black/75 backdrop-blur-sm flex items-center justify-center z-[100] p-4"
            onClick={onClose}
        >
            <div
                class="bg-slate-800 border border-slate-700 rounded-xl shadow-2xl w-full max-w-lg max-h-[90vh] overflow-y-auto"
                onClick={(e) => e.stopPropagation()}
            >
                <div class="px-6 py-4 border-b border-slate-700 bg-black/15 flex items-center justify-between">
                    <h3 class="font-bold text-slate-50 text-base">
                        <i class={`fas ${icon} mr-2 text-sky-400`}></i>{title}
                    </h3>
                    <button onClick={onClose} class="text-slate-400 hover:text-slate-200 transition-colors">
                        <i class="fas fa-times"></i>
                    </button>
                </div>
                <div class="p-6">{children}</div>
            </div>
        </div>,
        document.body
    );
}

export function BillingPage() {
    const [activeTab, setActiveTab] = useState<'invoices' | 'customers' | 'rfids' | 'tenants' | 'tariffs'>('invoices');

    // General state
    const [tariffs, setTariffs] = useState<TariffConfig>({ ratePerKwh: 0.30, baseMonthlyFee: 10.00 });
    const [customers, setCustomers] = useState<Customer[]>([]);
    const [rfids, setRfids] = useState<RfidMapping[]>([]);
    const [tenants, setTenants] = useState<Tenant[]>([]);
    const [loading, setLoading] = useState(true);

    // Invoices state
    const [selectedYear, setSelectedYear] = useState<number>(new Date().getFullYear());
    const [selectedMonth, setSelectedMonth] = useState<number>(new Date().getMonth() + 1);
    const [invoices, setInvoices] = useState<Invoice[]>([]);
    const [invoiceLoading, setInvoiceLoading] = useState(false);
    const [expandedInvoiceId, setExpandedInvoiceId] = useState<string | null>(null);
    const [selectedInvoiceForPrint, setSelectedInvoiceForPrint] = useState<Invoice | null>(null);

    // Add forms state
    const [newCustomer, setNewCustomer] = useState({ id: '', name: '', address: '', email: '', note: '' });
    const [newRfid, setNewRfid] = useState({ idTag: '', userName: '', customerId: '' });
    const [newTenant, setNewTenant] = useState({ id: '', name: '', meterKey: '', customerId: '' });

    // Add-dialog visibility
    const [showCustomerDialog, setShowCustomerDialog] = useState(false);
    const [showRfidDialog, setShowRfidDialog] = useState(false);
    const [showTenantDialog, setShowTenantDialog] = useState(false);

    // Inline editing state
    const [editingCustomerId, setEditingCustomerId] = useState<string | null>(null);
    const [editingCustomer, setEditingCustomer] = useState({ id: '', name: '', address: '', email: '', note: '' });
    const [editingRfidTag, setEditingRfidTag] = useState<string | null>(null);
    const [editingRfidName, setEditingRfidName] = useState<string>('');
    const [editingRfidCustomerId, setEditingRfidCustomerId] = useState<string>('');
    const [editingTenantId, setEditingTenantId] = useState<string | null>(null);
    const [editingTenantName, setEditingTenantName] = useState<string>('');
    const [editingTenantMeterKey, setEditingTenantMeterKey] = useState<string>('');
    const [editingTenantCustomerId, setEditingTenantCustomerId] = useState<string>('');

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

    const fetchCustomers = async () => {
        try {
            const res = await fetch('/plswk/api/billing/customers');
            if (res.ok) setCustomers(await res.json());
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
            await Promise.all([fetchTariffs(), fetchCustomers(), fetchRfids(), fetchTenants()]);
            setLoading(false);
        };
        init();
    }, []);

    useEffect(() => {
        if (!loading) {
            loadInvoices();
        }
    }, [selectedYear, selectedMonth, loading]);

    // ── Tariffs ──────────────────────────────────────────────────────────
    const handleUpdateTariffs = async (e: Event) => {
        e.preventDefault();
        try {
            const res = await fetch('/plswk/api/billing/tariffs', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(tariffs)
            });
            if (res.ok) {
                alert(t('bill_tariffs_updated'));
                fetchTariffs();
            }
        } catch (e) {
            console.error(e);
        }
    };

    // ── Customers ────────────────────────────────────────────────────────
    const handleAddCustomer = async (e: Event) => {
        e.preventDefault();
        if (!newCustomer.id.trim() || !newCustomer.name.trim()) return;
        try {
            const res = await fetch('/plswk/api/billing/customers', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    id: newCustomer.id.trim(),
                    name: newCustomer.name.trim(),
                    address: newCustomer.address.trim() || null,
                    email: newCustomer.email.trim() || null,
                    note: newCustomer.note.trim() || null
                })
            });
            if (res.ok) {
                setNewCustomer({ id: '', name: '', address: '', email: '', note: '' });
                setShowCustomerDialog(false);
                await fetchCustomers();
                await Promise.all([fetchRfids(), fetchTenants()]);
            }
        } catch (e) {
            console.error(e);
        }
    };

    const handleSaveCustomerEdit = async () => {
        if (!editingCustomer.id || !editingCustomer.name.trim()) return;
        try {
            const res = await fetch('/plswk/api/billing/customers', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    id: editingCustomer.id,
                    name: editingCustomer.name.trim(),
                    address: editingCustomer.address.trim() || null,
                    email: editingCustomer.email.trim() || null,
                    note: editingCustomer.note.trim() || null
                })
            });
            if (res.ok) {
                setEditingCustomerId(null);
                await fetchCustomers();
                await Promise.all([fetchRfids(), fetchTenants()]);
            }
        } catch (e) {
            console.error(e);
        }
    };

    const handleDeleteCustomer = async (id: string) => {
        if (!confirm(t('bill_delete_customer_confirm'))) return;
        try {
            const res = await fetch(`/plswk/api/billing/customers/${encodeURIComponent(id)}`, { method: 'DELETE' });
            if (res.ok) {
                await fetchCustomers();
                await Promise.all([fetchRfids(), fetchTenants()]);
            }
        } catch (e) {
            console.error(e);
        }
    };

    // ── RFID cards ───────────────────────────────────────────────────────
    const handleAddRfid = async (e: Event) => {
        e.preventDefault();
        if (!newRfid.idTag.trim() || !newRfid.userName.trim()) return;
        try {
            const res = await fetch('/plswk/api/billing/rfid', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    idTag: newRfid.idTag.trim(),
                    userName: newRfid.userName.trim(),
                    customerId: newRfid.customerId || null
                })
            });
            if (res.ok) {
                setNewRfid({ idTag: '', userName: '', customerId: '' });
                setShowRfidDialog(false);
                fetchRfids();
            }
        } catch (e) {
            console.error(e);
        }
    };

    const handleDeleteRfid = async (idTag: string) => {
        if (!confirm(`${t('bill_remove_rfid_confirm')} ${idTag}?`)) return;
        try {
            const res = await fetch(`/plswk/api/billing/rfid/${encodeURIComponent(idTag)}`, { method: 'DELETE' });
            if (res.ok) fetchRfids();
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
                body: JSON.stringify({
                    idTag,
                    userName: editingRfidName.trim(),
                    customerId: editingRfidCustomerId || null
                })
            });
            if (res.ok) {
                setEditingRfidTag(null);
                fetchRfids();
            }
        } catch (e) {
            console.error(e);
        }
    };

    // ── Tenants ──────────────────────────────────────────────────────────
    const handleAddTenant = async (e: Event) => {
        e.preventDefault();
        if (!newTenant.id.trim() || !newTenant.name.trim() || !newTenant.meterKey.trim()) return;
        try {
            const res = await fetch('/plswk/api/billing/tenants', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    id: newTenant.id.trim(),
                    name: newTenant.name.trim(),
                    meterKey: newTenant.meterKey.trim(),
                    customerId: newTenant.customerId || null
                })
            });
            if (res.ok) {
                setNewTenant({ id: '', name: '', meterKey: '', customerId: '' });
                setShowTenantDialog(false);
                fetchTenants();
            }
        } catch (e) {
            console.error(e);
        }
    };

    const handleDeleteTenant = async (id: string) => {
        if (!confirm(`${t('bill_remove_tenant_confirm')} ${id}?`)) return;
        try {
            const res = await fetch(`/plswk/api/billing/tenants/${encodeURIComponent(id)}`, { method: 'DELETE' });
            if (res.ok) fetchTenants();
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
                body: JSON.stringify({
                    id,
                    name: editingTenantName.trim(),
                    meterKey: editingTenantMeterKey.trim(),
                    customerId: editingTenantCustomerId || null
                })
            });
            if (res.ok) {
                setEditingTenantId(null);
                fetchTenants();
            }
        } catch (e) {
            console.error(e);
        }
    };

    // ── Meter replacements ───────────────────────────────────────────────
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
        if (!confirm(t('bill_remove_event_confirm'))) return;
        try {
            const res = await fetch(`/plswk/api/billing/meter-replacements/${id}`, { method: 'DELETE' });
            if (res.ok) fetchReplacements(tenantId);
        } catch (e) { console.error(e); }
    };

    // ── Derived invoice data ─────────────────────────────────────────────
    const customerGroups: CustomerGroup[] = (() => {
        const map = new Map<string, CustomerGroup>();
        for (const inv of invoices) {
            const key = inv.customerId ?? '__unassigned__';
            let g = map.get(key);
            if (!g) {
                g = {
                    key,
                    customerId: inv.customerId,
                    customerName: inv.customerName,
                    customerAddress: inv.customerAddress,
                    customerEmail: inv.customerEmail,
                    invoices: [],
                    totalKwh: 0,
                    energyCost: 0,
                    baseFee: 0,
                    totalCost: 0
                };
                map.set(key, g);
            }
            g.invoices.push(inv);
            g.totalKwh += inv.totalKwh;
            g.energyCost += inv.energyCost;
            g.baseFee += inv.baseFee;
            g.totalCost += inv.totalCost;
        }
        return Array.from(map.values()).sort((a, b) => {
            if (a.customerId === null) return 1;
            if (b.customerId === null) return -1;
            return a.customerName.localeCompare(b.customerName);
        });
    })();

    const grandTotal = customerGroups.reduce((sum, g) => sum + g.totalCost, 0);
    const grandKwh = customerGroups.reduce((sum, g) => sum + g.totalKwh, 0);

    const customerOptions = (
        <>
            <option value="">{t('bill_no_customer')}</option>
            {customers.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
        </>
    );

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

    const tabClass = (tab: typeof activeTab) =>
        `py-2.5 px-4 text-sm font-semibold rounded-t-lg border-b-2 transition-all whitespace-nowrap ${
            activeTab === tab ? 'text-sky-400 border-sky-400 bg-sky-400/5' : 'text-slate-400 border-transparent hover:text-slate-200'
        }`;

    return (
        <div class="flex flex-col gap-6 w-full page-enter print:p-0">
            {/* Header Tabs */}
            <div class="flex border-b border-slate-700/60 pb-px gap-2 shrink-0 overflow-x-auto print:hidden">
                <button onClick={() => setActiveTab('invoices')} class={tabClass('invoices')}>
                    <i class="fas fa-file-invoice-dollar mr-2"></i>{t('bill_monthly_invoices')}
                </button>
                <button onClick={() => setActiveTab('customers')} class={tabClass('customers')}>
                    <i class="fas fa-address-book mr-2"></i>{t('bill_customers')}
                </button>
                <button onClick={() => setActiveTab('rfids')} class={tabClass('rfids')}>
                    <i class="fas fa-id-card mr-2"></i>{t('bill_rfid_cards')}
                </button>
                <button onClick={() => setActiveTab('tenants')} class={tabClass('tenants')}>
                    <i class="fas fa-users mr-2"></i>{t('bill_metered_tenants')}
                </button>
                <button onClick={() => setActiveTab('tariffs')} class={tabClass('tariffs')}>
                    <i class="fas fa-sliders-h mr-2"></i>{t('bill_tariff_pricing')}
                </button>
            </div>

            {/* ══ TAB: INVOICES ══════════════════════════════════════════════ */}
            {activeTab === 'invoices' && (
                <div class="flex flex-col gap-5">
                    {/* Period picker + totals */}
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

                        <div class="flex items-center gap-6">
                            <div class="text-right">
                                <span class="text-[0.65rem] font-bold text-slate-400 uppercase tracking-wider block">{t('bill_consumption')}</span>
                                <span class="text-sm font-bold text-slate-100 font-mono">{kwh(grandKwh)}</span>
                            </div>
                            <div class="text-right">
                                <span class="text-[0.65rem] font-bold text-slate-400 uppercase tracking-wider block">{t('bill_grand_total')}</span>
                                <span class="text-lg font-black text-sky-400 font-mono">{money(grandTotal)}</span>
                            </div>
                            <div class="text-right border-l border-slate-700/60 pl-6">
                                <span class="text-[0.65rem] font-bold text-slate-400 uppercase tracking-wider block">{t('bill_standard_tariff')}</span>
                                <span class="text-xs font-semibold text-slate-300 font-mono">{tariffs.ratePerKwh.toFixed(2)} €/kWh · {tariffs.baseMonthlyFee.toFixed(2)} € {t('bill_base')}</span>
                            </div>
                        </div>
                    </div>

                    {invoiceLoading ? (
                        <div class="p-16 text-center text-cyan-400">
                            <i class="fas fa-spinner fa-spin text-2xl mr-2"></i>{t('loading')}
                        </div>
                    ) : customerGroups.length === 0 ? (
                        <div class="p-16 text-center text-slate-500 font-medium bg-slate-800 border border-slate-700 rounded-xl">
                            {t('bill_no_records')}
                        </div>
                    ) : (
                        <>
                            {/* Per-customer summary cards */}
                            <div>
                                <h3 class="text-[0.7rem] font-bold text-slate-400 uppercase tracking-wider mb-2">{t('bill_per_customer_summary')}</h3>
                                <div class="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-3 gap-3">
                                    {customerGroups.map(g => (
                                        <div key={g.key} class="glass rounded-xl border border-slate-700/60 p-4 flex flex-col gap-2">
                                            <div class="flex items-start justify-between gap-2">
                                                <div class="min-w-0">
                                                    <div class={`font-bold text-sm truncate ${g.customerId ? 'text-slate-100' : 'text-amber-400'}`}>
                                                        {g.customerId ? g.customerName : t('bill_unassigned')}
                                                    </div>
                                                    <div class="text-[0.68rem] text-slate-500 truncate">
                                                        {g.customerId ? (g.customerAddress || g.customerEmail || g.customerId) : t('bill_no_customer')}
                                                    </div>
                                                </div>
                                                <span class="text-[0.62rem] font-bold px-2 py-0.5 rounded-full bg-slate-700/60 text-slate-300 shrink-0">
                                                    {g.invoices.length} {t('bill_invoices_count')}
                                                </span>
                                            </div>
                                            <div class="flex items-end justify-between gap-2 pt-1 border-t border-white/5">
                                                <div>
                                                    <div class="text-[0.6rem] font-bold text-slate-500 uppercase tracking-wider">{t('bill_consumption')}</div>
                                                    <div class="text-xs font-mono text-slate-300">{kwh(g.totalKwh)}</div>
                                                </div>
                                                <div class="text-right">
                                                    <div class="text-[0.6rem] font-bold text-slate-500 uppercase tracking-wider">{t('bill_total_cost')}</div>
                                                    <div class="text-base font-black text-sky-400 font-mono">{money(g.totalCost)}</div>
                                                </div>
                                            </div>
                                        </div>
                                    ))}
                                </div>
                            </div>

                            {/* Invoice documents per customer */}
                            {customerGroups.map(g => (
                                <div key={g.key} class="bg-slate-800 border border-slate-700 rounded-xl overflow-hidden">
                                    <div class="px-5 py-3.5 border-b border-slate-700 bg-black/20 flex flex-wrap items-center justify-between gap-3">
                                        <div class="flex items-center gap-3 min-w-0">
                                            <div class={`w-9 h-9 rounded-lg flex items-center justify-center shrink-0 ${
                                                g.customerId ? 'bg-sky-500/15 text-sky-400' : 'bg-amber-500/15 text-amber-400'
                                            }`}>
                                                <i class={`fas ${g.customerId ? 'fa-user' : 'fa-user-slash'} text-sm`}></i>
                                            </div>
                                            <div class="min-w-0">
                                                <div class="font-bold text-slate-50 text-sm truncate">
                                                    {g.customerId ? g.customerName : t('bill_unassigned')}
                                                </div>
                                                <div class="text-[0.68rem] text-slate-500 truncate">
                                                    {[g.customerAddress, g.customerEmail].filter(Boolean).join(' · ') || g.customerId || '—'}
                                                </div>
                                            </div>
                                        </div>
                                        <div class="flex items-center gap-5">
                                            <div class="text-right">
                                                <div class="text-[0.6rem] font-bold text-slate-500 uppercase tracking-wider">{t('bill_consumption')}</div>
                                                <div class="text-xs font-mono text-slate-300">{kwh(g.totalKwh)}</div>
                                            </div>
                                            <div class="text-right">
                                                <div class="text-[0.6rem] font-bold text-slate-500 uppercase tracking-wider">{t('bill_total_cost')}</div>
                                                <div class="text-sm font-black text-sky-400 font-mono">{money(g.totalCost)}</div>
                                            </div>
                                        </div>
                                    </div>

                                    <div class="overflow-x-auto">
                                        <table class="lv-table text-sm">
                                            <thead>
                                                <tr>
                                                    <th>{t('bill_invoice_no')}</th>
                                                    <th>{t('bill_source')}</th>
                                                    <th>{t('bill_details')}</th>
                                                    <th class="text-right">{t('bill_consumption')}</th>
                                                    <th class="text-right">{t('bill_energy_cost')}</th>
                                                    <th class="text-right">{t('bill_base')}</th>
                                                    <th class="text-right">{t('bill_total_cost')}</th>
                                                    <th class="text-center print:hidden">{t('bill_action')}</th>
                                                </tr>
                                            </thead>
                                            <tbody>
                                                {g.invoices.map(inv => {
                                                    const isOpen = expandedInvoiceId === inv.invoiceId;
                                                    return (
                                                        <Fragment key={inv.invoiceId}>
                                                            <tr>
                                                                <td class="font-mono text-[0.7rem] text-slate-400">{inv.invoiceId}</td>
                                                                <td>
                                                                    <div class="flex items-center gap-1.5 flex-wrap">
                                                                        <span class={`inline-block text-[0.68rem] font-bold px-2 py-0.5 rounded-full ${
                                                                            inv.source === 'ev' ? 'bg-amber-400/10 text-amber-400' : 'bg-sky-400/10 text-sky-400'
                                                                        }`}>
                                                                            {inv.source === 'ev' ? t('bill_ev_charging') : t('bill_tenant_meter')}
                                                                        </span>
                                                                        {inv.replacementCount > 0 && (
                                                                            <span
                                                                                class="inline-flex items-center gap-1 text-[0.65rem] font-bold px-1.5 py-0.5 rounded-full bg-orange-500/15 text-orange-400"
                                                                                title={t('bill_meter_replaced_title')}
                                                                            >
                                                                                <i class="fas fa-exchange-alt"></i>
                                                                                {inv.replacementCount}× {t('bill_replaced_badge')}
                                                                            </span>
                                                                        )}
                                                                    </div>
                                                                </td>
                                                                <td class="text-slate-400 font-mono text-[0.7rem] max-w-[220px] truncate" title={inv.details}>
                                                                    {inv.details}
                                                                </td>
                                                                <td class="text-right font-mono text-slate-100 font-semibold">{kwh(inv.totalKwh)}</td>
                                                                <td class="text-right font-mono text-slate-300">{money(inv.energyCost)}</td>
                                                                <td class="text-right font-mono text-slate-400">{money(inv.baseFee)}</td>
                                                                <td class="text-right font-mono text-sky-400 font-bold">{money(inv.totalCost)}</td>
                                                                <td class="text-center print:hidden">
                                                                    <div class="flex items-center justify-center gap-1.5">
                                                                        <button
                                                                            onClick={() => setExpandedInvoiceId(isOpen ? null : inv.invoiceId)}
                                                                            class={`py-1 px-2.5 rounded text-xs font-semibold transition-colors ${
                                                                                isOpen ? 'bg-sky-500/20 text-sky-300' : 'bg-slate-700 text-slate-200 hover:bg-slate-600'
                                                                            }`}
                                                                            title={isOpen ? t('bill_hide_items') : t('bill_show_items')}
                                                                        >
                                                                            <i class={`fas ${isOpen ? 'fa-chevron-up' : 'fa-list-ul'} mr-1`}></i>
                                                                            {inv.transactionCount}
                                                                        </button>
                                                                        <button
                                                                            onClick={() => setSelectedInvoiceForPrint(inv)}
                                                                            class="py-1 px-2.5 rounded bg-slate-700 text-slate-200 text-xs font-semibold hover:bg-slate-600 transition-colors"
                                                                            title={t('bill_print_btn')}
                                                                        >
                                                                            <i class="fas fa-file-invoice"></i>
                                                                        </button>
                                                                    </div>
                                                                </td>
                                                            </tr>
                                                            {isOpen && (
                                                                <tr>
                                                                    <td colspan={8} class="p-0">
                                                                        <div class="mx-4 mb-3 border border-sky-500/20 rounded-xl bg-sky-500/[0.03] overflow-hidden">
                                                                            <div class="px-4 py-2 bg-sky-500/10 border-b border-sky-500/15 flex items-center justify-between">
                                                                                <span class="text-[0.7rem] font-bold text-sky-400 uppercase tracking-wider">
                                                                                    <i class="fas fa-list-ul mr-1.5"></i>{t('bill_line_items')}
                                                                                </span>
                                                                                <span class="text-[0.65rem] text-sky-400/70">{inv.lineItems.length} {t('bill_items')}</span>
                                                                            </div>
                                                                            {inv.lineItems.length === 0 ? (
                                                                                <div class="px-4 py-3 text-xs text-slate-600 italic">{t('bill_no_line_items')}</div>
                                                                            ) : (
                                                                                <table class="w-full text-xs">
                                                                                    <thead>
                                                                                        <tr class="text-[0.6rem] text-slate-500 uppercase tracking-wider border-b border-white/5">
                                                                                            <th class="px-4 py-2 text-left">{t('bill_date')}</th>
                                                                                            <th class="px-4 py-2 text-left">{t('bill_desc')}</th>
                                                                                            <th class="px-4 py-2 text-left">{t('bill_token_id')}</th>
                                                                                            <th class="px-4 py-2 text-right">{t('bill_quantity')}</th>
                                                                                            <th class="px-4 py-2 text-right">{t('bill_unit_price')}</th>
                                                                                            <th class="px-4 py-2 text-right">{t('bill_amount')}</th>
                                                                                        </tr>
                                                                                    </thead>
                                                                                    <tbody>
                                                                                        {inv.lineItems.map((li, i) => (
                                                                                            <tr key={i} class="border-b border-white/5 hover:bg-white/[0.02]">
                                                                                                <td class="px-4 py-1.5 font-mono text-slate-400 whitespace-nowrap">{li.date}</td>
                                                                                                <td class="px-4 py-1.5 text-slate-200">{li.description}</td>
                                                                                                <td class="px-4 py-1.5 font-mono text-slate-500">{li.idTag}</td>
                                                                                                <td class="px-4 py-1.5 text-right font-mono text-slate-300">{li.quantity.toFixed(3)} {li.unit}</td>
                                                                                                <td class="px-4 py-1.5 text-right font-mono text-slate-500">{li.unitPrice.toFixed(2)} €</td>
                                                                                                <td class="px-4 py-1.5 text-right font-mono font-semibold text-slate-100">{money(li.amount)}</td>
                                                                                            </tr>
                                                                                        ))}
                                                                                    </tbody>
                                                                                </table>
                                                                            )}
                                                                        </div>
                                                                    </td>
                                                                </tr>
                                                            )}
                                                        </Fragment>
                                                    );
                                                })}
                                            </tbody>
                                        </table>
                                    </div>
                                </div>
                            ))}
                        </>
                    )}
                </div>
            )}

            {/* ══ TAB: CUSTOMERS ═════════════════════════════════════════════ */}
            {activeTab === 'customers' && (
                <div class="bg-slate-800 border border-slate-700 rounded-xl overflow-hidden">
                    <div class="px-6 py-4 border-b border-slate-700 bg-black/15 flex items-center justify-between gap-4">
                        <h3 class="font-bold text-slate-50 text-base">
                            {t('bill_customers_title')}
                            <span class="ml-2 text-xs text-slate-400 font-medium">{customers.length}</span>
                        </h3>
                        <button
                            onClick={() => setShowCustomerDialog(true)}
                            class="px-4 py-2 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors"
                        >
                            <i class="fas fa-plus mr-1.5"></i>{t('bill_new_customer')}
                        </button>
                    </div>
                    <div class="max-h-[640px] overflow-y-auto">
                        {customers.length === 0 ? (
                            <div class="p-16 text-center text-slate-500">{t('bill_no_customers')}</div>
                        ) : (
                            <table class="lv-table text-sm">
                                <thead>
                                    <tr>
                                        <th>{t('bill_customer_id')}</th>
                                        <th>{t('bill_customer_name')}</th>
                                        <th>{t('bill_customer_address')}</th>
                                        <th>{t('bill_customer_email')}</th>
                                        <th class="text-right">{t('bill_actions')}</th>
                                    </tr>
                                </thead>
                                    <tbody>
                                        {customers.map(c => {
                                            const isEditing = c.id === editingCustomerId;
                                            return (
                                                <tr key={c.id}>
                                                    <td class="font-mono text-sky-400 text-xs">{c.id}</td>
                                                    <td class="text-slate-100 font-bold">
                                                        {isEditing ? (
                                                            <input
                                                                type="text"
                                                                value={editingCustomer.name}
                                                                onInput={(e) => setEditingCustomer({ ...editingCustomer, name: (e.target as HTMLInputElement).value })}
                                                                class="form-input w-full"
                                                            />
                                                        ) : c.name}
                                                    </td>
                                                    <td class="text-slate-400 text-xs">
                                                        {isEditing ? (
                                                            <input
                                                                type="text"
                                                                value={editingCustomer.address}
                                                                onInput={(e) => setEditingCustomer({ ...editingCustomer, address: (e.target as HTMLInputElement).value })}
                                                                class="form-input w-full"
                                                            />
                                                        ) : (c.address || '–')}
                                                    </td>
                                                    <td class="text-slate-400 text-xs">
                                                        {isEditing ? (
                                                            <input
                                                                type="text"
                                                                value={editingCustomer.email}
                                                                onInput={(e) => setEditingCustomer({ ...editingCustomer, email: (e.target as HTMLInputElement).value })}
                                                                class="form-input w-full"
                                                            />
                                                        ) : (c.email || '–')}
                                                    </td>
                                                    <td class="text-right">
                                                        <div class="flex justify-end gap-1.5">
                                                            {isEditing ? (
                                                                <>
                                                                    <button
                                                                        onClick={handleSaveCustomerEdit}
                                                                        class="p-1 px-2.5 rounded bg-emerald-500/20 text-emerald-400 hover:bg-emerald-500/30 text-xs font-semibold transition-colors"
                                                                    >
                                                                        <i class="fas fa-check"></i>
                                                                    </button>
                                                                    <button
                                                                        onClick={() => setEditingCustomerId(null)}
                                                                        class="p-1 px-2.5 rounded bg-slate-700/40 text-slate-300 hover:bg-slate-700/60 text-xs font-semibold transition-colors"
                                                                    >
                                                                        <i class="fas fa-times"></i>
                                                                    </button>
                                                                </>
                                                            ) : (
                                                                <>
                                                                    <button
                                                                        onClick={() => {
                                                                            setEditingCustomerId(c.id);
                                                                            setEditingCustomer({
                                                                                id: c.id,
                                                                                name: c.name,
                                                                                address: c.address || '',
                                                                                email: c.email || '',
                                                                                note: c.note || ''
                                                                            });
                                                                        }}
                                                                        class="p-1 px-2.5 rounded bg-sky-500/10 text-sky-400 hover:bg-sky-500/20 text-xs font-semibold transition-colors"
                                                                    >
                                                                        <i class="fas fa-edit"></i>
                                                                    </button>
                                                                    <button
                                                                        onClick={() => handleDeleteCustomer(c.id)}
                                                                        class="p-1 px-2.5 rounded bg-red-500/10 text-red-400 hover:bg-red-500/20 text-xs font-semibold transition-colors"
                                                                    >
                                                                        <i class="fas fa-trash-alt"></i>
                                                                    </button>
                                                                </>
                                                            )}
                                                        </div>
                                                    </td>
                                                </tr>
                                            );
                                        })}
                                    </tbody>
                                </table>
                            )}
                        </div>
                    </div>
            )}

            {/* ══ TAB: RFID CARDS ════════════════════════════════════════════ */}
            {activeTab === 'rfids' && (
                <div class="bg-slate-800 border border-slate-700 rounded-xl overflow-hidden">
                    <div class="px-6 py-4 border-b border-slate-700 bg-black/15 flex items-center justify-between gap-4">
                        <h3 class="font-bold text-slate-50 text-base">
                            {t('bill_registered_cards')}
                            <span class="ml-2 text-xs text-slate-400 font-medium">{rfids.length}</span>
                        </h3>
                        <button
                            onClick={() => setShowRfidDialog(true)}
                            class="px-4 py-2 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors"
                        >
                            <i class="fas fa-plus mr-1.5"></i>{t('bill_new_rfid')}
                        </button>
                    </div>
                    <div class="max-h-[640px] overflow-y-auto">
                        {rfids.length === 0 ? (
                            <div class="p-16 text-center text-slate-500">{t('bill_no_rfid')}</div>
                        ) : (
                            <table class="lv-table text-sm">
                                <thead>
                                    <tr>
                                        <th>{t('bill_token_id')}</th>
                                        <th>{t('bill_user_name')}</th>
                                        <th>{t('bill_customer')}</th>
                                        <th class="text-right">{t('bill_actions')}</th>
                                    </tr>
                                </thead>
                                    <tbody>
                                        {rfids.map(r => {
                                            const isEditing = r.idTag === editingRfidTag;
                                            return (
                                                <tr key={r.idTag}>
                                                    <td class="font-mono font-bold text-sky-400">{r.idTag}</td>
                                                    <td class="text-slate-100">
                                                        {isEditing ? (
                                                            <input
                                                                type="text"
                                                                value={editingRfidName}
                                                                onInput={(e) => setEditingRfidName((e.target as HTMLInputElement).value)}
                                                                class="form-input w-full max-w-[200px]"
                                                            />
                                                        ) : r.userName}
                                                    </td>
                                                    <td>
                                                        {isEditing ? (
                                                            <select
                                                                value={editingRfidCustomerId}
                                                                onChange={(e) => setEditingRfidCustomerId((e.target as HTMLSelectElement).value)}
                                                                class="form-input w-full max-w-[200px]"
                                                            >
                                                                {customerOptions}
                                                            </select>
                                                        ) : r.customerId ? (
                                                            <span class="inline-flex items-center gap-1.5 text-xs font-semibold px-2 py-0.5 rounded-full bg-sky-500/10 text-sky-400">
                                                                <i class="fas fa-user text-[0.6rem]"></i>{r.customerName || r.customerId}
                                                            </span>
                                                        ) : (
                                                            <span class="text-xs text-amber-400/80 italic">{t('bill_unassigned')}</span>
                                                        )}
                                                    </td>
                                                    <td class="text-right">
                                                        <div class="flex justify-end gap-1.5">
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
                                                                            setEditingRfidCustomerId(r.customerId || '');
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
                                                        </div>
                                                    </td>
                                                </tr>
                                            );
                                        })}
                                    </tbody>
                                </table>
                            )}
                        </div>
                    </div>
            )}

            {/* ══ TAB: METERED TENANTS ═══════════════════════════════════════ */}
            {activeTab === 'tenants' && (
                <div class="bg-slate-800 border border-slate-700 rounded-xl overflow-hidden">
                    <div class="px-6 py-4 border-b border-slate-700 bg-black/15 flex items-center justify-between gap-4">
                        <h3 class="font-bold text-slate-50 text-base">
                            {t('bill_metered_tenants_title')}
                            <span class="ml-2 text-xs text-slate-400 font-medium">{tenants.length}</span>
                        </h3>
                        <button
                            onClick={() => setShowTenantDialog(true)}
                            class="px-4 py-2 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors"
                        >
                            <i class="fas fa-plus mr-1.5"></i>{t('bill_new_tenant')}
                        </button>
                    </div>
                    <div class="max-h-[640px] overflow-y-auto">
                        {tenants.length === 0 ? (
                            <div class="p-16 text-center text-slate-500">{t('bill_no_tenants')}</div>
                        ) : (
                            <table class="lv-table text-sm">
                                <thead>
                                    <tr>
                                        <th>{t('bill_tenant_id')}</th>
                                        <th>{t('bill_display_name')}</th>
                                        <th>{t('bill_influx_key')}</th>
                                        <th>{t('bill_customer')}</th>
                                        <th class="text-right">{t('bill_actions')}</th>
                                    </tr>
                                </thead>
                                    <tbody>
                                        {tenants.map(ten => {
                                            const isEditing = ten.id === editingTenantId;
                                            const isExpanded = expandedReplacementTenantId === ten.id;
                                            const tenantReplacements = replacements[ten.id] ?? [];
                                            return (
                                                <Fragment key={ten.id}>
                                                    <tr>
                                                        <td class="font-mono text-slate-300">{ten.id}</td>
                                                        <td class="text-slate-100 font-bold">
                                                            {isEditing ? (
                                                                <input
                                                                    type="text"
                                                                    value={editingTenantName}
                                                                    onInput={(e) => setEditingTenantName((e.target as HTMLInputElement).value)}
                                                                    class="form-input w-full"
                                                                />
                                                            ) : ten.name}
                                                        </td>
                                                        <td class="text-sky-400 font-mono text-xs">
                                                            {isEditing ? (
                                                                <input
                                                                    type="text"
                                                                    value={editingTenantMeterKey}
                                                                    onInput={(e) => setEditingTenantMeterKey((e.target as HTMLInputElement).value)}
                                                                    class="form-input w-full font-mono"
                                                                />
                                                            ) : ten.meterKey}
                                                        </td>
                                                        <td>
                                                            {isEditing ? (
                                                                <select
                                                                    value={editingTenantCustomerId}
                                                                    onChange={(e) => setEditingTenantCustomerId((e.target as HTMLSelectElement).value)}
                                                                    class="form-input w-full max-w-[180px]"
                                                                >
                                                                    {customerOptions}
                                                                </select>
                                                            ) : ten.customerId ? (
                                                                <span class="inline-flex items-center gap-1.5 text-xs font-semibold px-2 py-0.5 rounded-full bg-sky-500/10 text-sky-400">
                                                                    <i class="fas fa-user text-[0.6rem]"></i>{ten.customerName || ten.customerId}
                                                                </span>
                                                            ) : (
                                                                <span class="text-xs text-amber-400/80 italic">{t('bill_unassigned')}</span>
                                                            )}
                                                        </td>
                                                        <td class="text-right">
                                                            <div class="flex justify-end gap-1.5">
                                                                <button
                                                                    title={t('bill_meter_replacements')}
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
                                                                                setEditingTenantCustomerId(ten.customerId || '');
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
                                                            </div>
                                                        </td>
                                                    </tr>
                                                    {isExpanded && (
                                                        <tr>
                                                            <td colspan={5} class="p-0">
                                                                <div class="mx-4 mb-3 border border-orange-500/20 rounded-xl bg-orange-500/[0.03] overflow-hidden">
                                                                    <div class="px-4 py-2.5 bg-orange-500/10 border-b border-orange-500/15 flex items-center justify-between">
                                                                        <span class="text-[0.72rem] font-bold text-orange-400 uppercase tracking-wider">
                                                                            <i class="fas fa-exchange-alt mr-1.5"></i>{t('bill_meter_replacements')} — {ten.name}
                                                                        </span>
                                                                        <span class="text-[0.65rem] text-orange-400/60">{tenantReplacements.length} {t('bill_recorded')}</span>
                                                                    </div>

                                                                    {tenantReplacements.length > 0 ? (
                                                                        <table class="w-full text-xs">
                                                                            <thead>
                                                                                <tr class="text-[0.62rem] text-slate-500 uppercase tracking-wider border-b border-white/5">
                                                                                    <th class="px-4 py-2 text-left">{t('bill_new_meter_start')}</th>
                                                                                    <th class="px-4 py-2 text-right">{t('bill_old_final_kwh')}</th>
                                                                                    <th class="px-4 py-2 text-right">{t('bill_new_start_kwh')}</th>
                                                                                    <th class="px-4 py-2 text-left">{t('bill_note')}</th>
                                                                                    <th class="px-4 py-2"></th>
                                                                                </tr>
                                                                            </thead>
                                                                            <tbody>
                                                                                {tenantReplacements.map(r => (
                                                                                    <tr key={r.id} class="border-b border-white/5 hover:bg-white/[0.02]">
                                                                                        <td class="px-4 py-2 font-mono text-slate-300">{new Date(r.replacedAt).toLocaleString()}</td>
                                                                                        <td class="px-4 py-2 text-right font-mono text-slate-400">{r.oldFinalKwh != null ? r.oldFinalKwh.toFixed(2) : '–'}</td>
                                                                                        <td class="px-4 py-2 text-right font-mono text-slate-400">{r.newStartKwh != null ? r.newStartKwh.toFixed(2) : '–'}</td>
                                                                                        <td class="px-4 py-2 text-slate-500 italic">{r.note || '–'}</td>
                                                                                        <td class="px-4 py-2 text-right">
                                                                                            <button
                                                                                                onClick={() => handleDeleteReplacement(ten.id, r.id)}
                                                                                                class="p-1 px-2 rounded bg-red-500/10 text-red-400 hover:bg-red-500/20 text-xs font-semibold transition-colors"
                                                                                                title={t('bill_remove_event')}
                                                                                            >
                                                                                                <i class="fas fa-trash-alt"></i>
                                                                                            </button>
                                                                                        </td>
                                                                                    </tr>
                                                                                ))}
                                                                            </tbody>
                                                                        </table>
                                                                    ) : (
                                                                        <div class="px-4 py-3 text-xs text-slate-600 italic">{t('bill_no_replacements')}</div>
                                                                    )}

                                                                    <form
                                                                        onSubmit={(e) => handleAddReplacement(ten.id, e)}
                                                                        class="px-4 py-3 border-t border-orange-500/10 flex flex-wrap items-end gap-3"
                                                                    >
                                                                        <div class="flex flex-col gap-1">
                                                                            <label class="text-[0.62rem] font-bold text-slate-500 uppercase tracking-wide">{t('bill_new_meter_start')} *</label>
                                                                            <input
                                                                                type="datetime-local"
                                                                                required
                                                                                value={newReplacement.replacedAt}
                                                                                onInput={(e) => setNewReplacement(p => ({ ...p, replacedAt: (e.target as HTMLInputElement).value }))}
                                                                                class="form-input text-xs py-1.5"
                                                                            />
                                                                        </div>
                                                                        <div class="flex flex-col gap-1">
                                                                            <label class="text-[0.62rem] font-bold text-slate-500 uppercase tracking-wide">{t('bill_old_final_kwh')}</label>
                                                                            <input
                                                                                type="number" step="0.001" min="0"
                                                                                value={newReplacement.oldFinalKwh}
                                                                                onInput={(e) => setNewReplacement(p => ({ ...p, oldFinalKwh: (e.target as HTMLInputElement).value }))}
                                                                                placeholder="e.g. 98751.2"
                                                                                class="form-input text-xs py-1.5 font-mono w-32"
                                                                            />
                                                                        </div>
                                                                        <div class="flex flex-col gap-1">
                                                                            <label class="text-[0.62rem] font-bold text-slate-500 uppercase tracking-wide">{t('bill_new_start_kwh')}</label>
                                                                            <input
                                                                                type="number" step="0.001" min="0"
                                                                                value={newReplacement.newStartKwh}
                                                                                onInput={(e) => setNewReplacement(p => ({ ...p, newStartKwh: (e.target as HTMLInputElement).value }))}
                                                                                placeholder="e.g. 0.0"
                                                                                class="form-input text-xs py-1.5 font-mono w-32"
                                                                            />
                                                                        </div>
                                                                        <div class="flex flex-col gap-1 flex-1 min-w-[160px]">
                                                                            <label class="text-[0.62rem] font-bold text-slate-500 uppercase tracking-wide">{t('bill_note')}</label>
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
                                                                            <i class="fas fa-plus mr-1"></i>{t('bill_record')}
                                                                        </button>
                                                                    </form>
                                                                </div>
                                                            </td>
                                                        </tr>
                                                    )}
                                                </Fragment>
                                            );
                                        })}
                                    </tbody>
                                </table>
                            )}
                        </div>
                    </div>
            )}

            {/* ══ TAB: TARIFF PRICING ════════════════════════════════════════ */}
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

                        <button type="submit" class="py-2.5 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors mt-2">
                            <i class="fas fa-check mr-1.5"></i>{t('bill_apply_model')}
                        </button>
                    </form>
                </div>
            )}

            {/* ══ Add-customer dialog ════════════════════════════════════════ */}
            {showCustomerDialog && (
                <Modal title={t('bill_new_customer')} icon="fa-user-plus" onClose={() => setShowCustomerDialog(false)}>
                    <form onSubmit={handleAddCustomer} class="flex flex-col gap-4">
                        <div class="flex flex-col gap-1.5">
                            <label class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide">{t('bill_customer_id')}</label>
                            <input
                                type="text"
                                required
                                value={newCustomer.id}
                                onInput={(e) => setNewCustomer({ ...newCustomer, id: (e.target as HTMLInputElement).value })}
                                placeholder="e.g. cust_mueller"
                                class="form-input font-mono"
                            />
                        </div>
                        <div class="flex flex-col gap-1.5">
                            <label class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide">{t('bill_customer_name')}</label>
                            <input
                                type="text"
                                required
                                value={newCustomer.name}
                                onInput={(e) => setNewCustomer({ ...newCustomer, name: (e.target as HTMLInputElement).value })}
                                placeholder="e.g. Müller GmbH"
                                class="form-input"
                            />
                        </div>
                        <div class="flex flex-col gap-1.5">
                            <label class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide">{t('bill_customer_address')}</label>
                            <textarea
                                rows={2}
                                value={newCustomer.address}
                                onInput={(e) => setNewCustomer({ ...newCustomer, address: (e.target as HTMLTextAreaElement).value })}
                                placeholder="Street 1, 12345 City"
                                class="form-input resize-none"
                            ></textarea>
                        </div>
                        <div class="flex flex-col gap-1.5">
                            <label class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide">{t('bill_customer_email')}</label>
                            <input
                                type="email"
                                value={newCustomer.email}
                                onInput={(e) => setNewCustomer({ ...newCustomer, email: (e.target as HTMLInputElement).value })}
                                placeholder="billing@example.com"
                                class="form-input"
                            />
                        </div>
                        <div class="flex flex-col gap-1.5">
                            <label class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide">{t('bill_customer_note')}</label>
                            <input
                                type="text"
                                value={newCustomer.note}
                                onInput={(e) => setNewCustomer({ ...newCustomer, note: (e.target as HTMLInputElement).value })}
                                class="form-input"
                            />
                        </div>
                        <div class="flex justify-end gap-2 mt-2">
                            <button
                                type="button"
                                onClick={() => setShowCustomerDialog(false)}
                                class="px-4 py-2 rounded-lg bg-slate-700/50 text-slate-300 text-xs font-bold hover:bg-slate-700 transition-colors"
                            >
                                {t('cancel')}
                            </button>
                            <button type="submit" class="px-4 py-2 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors">
                                <i class="fas fa-plus mr-1.5"></i>{t('bill_add_customer_btn')}
                            </button>
                        </div>
                    </form>
                </Modal>
            )}

            {/* ══ Add-RFID dialog ════════════════════════════════════════════ */}
            {showRfidDialog && (
                <Modal title={t('bill_new_rfid')} icon="fa-id-card" onClose={() => setShowRfidDialog(false)}>
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
                        <div class="flex flex-col gap-1.5">
                            <label class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide">{t('bill_customer')}</label>
                            <select
                                value={newRfid.customerId}
                                onChange={(e) => setNewRfid({ ...newRfid, customerId: (e.target as HTMLSelectElement).value })}
                                class="form-input"
                            >
                                {customerOptions}
                            </select>
                        </div>
                        <div class="flex justify-end gap-2 mt-2">
                            <button
                                type="button"
                                onClick={() => setShowRfidDialog(false)}
                                class="px-4 py-2 rounded-lg bg-slate-700/50 text-slate-300 text-xs font-bold hover:bg-slate-700 transition-colors"
                            >
                                {t('cancel')}
                            </button>
                            <button type="submit" class="px-4 py-2 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors">
                                <i class="fas fa-plus mr-1.5"></i>{t('bill_add_mapping')}
                            </button>
                        </div>
                    </form>
                </Modal>
            )}

            {/* ══ Add-tenant dialog ══════════════════════════════════════════ */}
            {showTenantDialog && (
                <Modal title={t('bill_new_tenant')} icon="fa-gauge-high" onClose={() => setShowTenantDialog(false)}>
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
                        <div class="flex flex-col gap-1.5">
                            <label class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide">{t('bill_customer')}</label>
                            <select
                                value={newTenant.customerId}
                                onChange={(e) => setNewTenant({ ...newTenant, customerId: (e.target as HTMLSelectElement).value })}
                                class="form-input"
                            >
                                {customerOptions}
                            </select>
                        </div>
                        <div class="flex justify-end gap-2 mt-2">
                            <button
                                type="button"
                                onClick={() => setShowTenantDialog(false)}
                                class="px-4 py-2 rounded-lg bg-slate-700/50 text-slate-300 text-xs font-bold hover:bg-slate-700 transition-colors"
                            >
                                {t('cancel')}
                            </button>
                            <button type="submit" class="px-4 py-2 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors">
                                <i class="fas fa-plus mr-1.5"></i>{t('bill_add_tenant_btn')}
                            </button>
                        </div>
                    </form>
                </Modal>
            )}

            {/* ══ Printable invoice document ═════════════════════════════════ */}
            {selectedInvoiceForPrint !== null && createPortal(
                <div class="fixed inset-0 bg-black/75 backdrop-blur-sm flex items-center justify-center z-[100] p-4 print:static print:bg-transparent print:p-0 print:block">
                    <div class="bg-slate-800 border border-slate-700 rounded-xl shadow-2xl max-w-2xl w-full max-h-[90vh] overflow-y-auto p-8 text-slate-100 flex flex-col gap-6 print:border-none print:bg-transparent print:p-0 print:text-black print:max-h-none print:overflow-visible">
                        <div class="flex justify-between items-start border-b border-slate-700/60 pb-5 print:border-black print:pb-2">
                            <div>
                                <h2 class="text-xl font-extrabold text-slate-50 print:text-black print:text-2xl">{t('bill_invoice_header')}</h2>
                                <p class="text-xs text-slate-400 mt-1 print:text-black font-mono">
                                    {t('bill_invoice_no')}: {selectedInvoiceForPrint.invoiceId}
                                </p>
                                <p class="text-xs text-slate-400 mt-0.5 print:text-black font-mono">
                                    {t('bill_invoice_date')}: {new Date().toLocaleDateString()} · {t('bill_invoice_period')}: {selectedInvoiceForPrint.billingPeriod}
                                </p>
                            </div>
                            <button onClick={() => setSelectedInvoiceForPrint(null)} class="text-slate-400 hover:text-slate-200 print:hidden">
                                <i class="fas fa-times text-xl"></i>
                            </button>
                        </div>

                        <div class="grid grid-cols-2 gap-4 bg-black/10 p-4 rounded-lg border border-slate-700/40 print:bg-transparent print:border-none print:p-0">
                            <div>
                                <span class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wider block print:text-black">{t('bill_billed_to')}</span>
                                <strong class="text-slate-100 text-base mt-1 block print:text-black">
                                    {selectedInvoiceForPrint.customerId ? selectedInvoiceForPrint.customerName : t('bill_unassigned')}
                                </strong>
                                {selectedInvoiceForPrint.customerAddress && (
                                    <span class="text-xs text-slate-400 block whitespace-pre-line print:text-black">{selectedInvoiceForPrint.customerAddress}</span>
                                )}
                                {selectedInvoiceForPrint.customerEmail && (
                                    <span class="text-xs text-slate-400 font-mono block print:text-black">{selectedInvoiceForPrint.customerEmail}</span>
                                )}
                            </div>
                            <div class="text-right">
                                <span class="text-[0.68rem] font-bold text-slate-400 uppercase tracking-wider block print:text-black">{t('bill_acc_type')}</span>
                                <span class="text-sm font-semibold text-slate-200 mt-1 block print:text-black">
                                    {selectedInvoiceForPrint.source === 'ev' ? t('bill_ev_charging') : t('bill_tenant_meter')}
                                </span>
                                <span class="text-xs text-slate-500 font-mono block print:text-black">{selectedInvoiceForPrint.details}</span>
                            </div>
                        </div>

                        <table class="w-full border-collapse text-sm text-slate-200 print:text-black">
                            <thead>
                                <tr class="text-left text-[0.68rem] font-bold uppercase tracking-wider text-slate-400 border-b border-slate-700 print:border-black print:text-black">
                                    <th class="py-2">{t('bill_date')}</th>
                                    <th class="py-2">{t('bill_desc')}</th>
                                    <th class="py-2 text-right">{t('bill_quantity')}</th>
                                    <th class="py-2 text-right">{t('bill_unit_price')}</th>
                                    <th class="py-2 text-right">{t('bill_total')}</th>
                                </tr>
                            </thead>
                            <tbody>
                                {selectedInvoiceForPrint.lineItems.map((li, i) => (
                                    <tr key={i} class="border-b border-slate-700/30 print:border-gray-300">
                                        <td class="py-2 font-mono text-xs whitespace-nowrap">{li.date}</td>
                                        <td class="py-2">
                                            <div class="font-semibold print:text-black">{li.description}</div>
                                            <div class="text-[0.7rem] text-slate-500 font-mono print:text-black">{li.idTag}</div>
                                        </td>
                                        <td class="py-2 text-right font-mono">{li.quantity.toFixed(3)} {li.unit}</td>
                                        <td class="py-2 text-right font-mono">{li.unitPrice.toFixed(2)} €</td>
                                        <td class="py-2 text-right font-mono font-semibold">{money(li.amount)}</td>
                                    </tr>
                                ))}
                                <tr class="border-b border-slate-700/30 print:border-gray-300">
                                    <td class="py-3" colspan={4}>
                                        <div class="font-semibold print:text-black">{t('bill_base_service_fee')}</div>
                                        <div class="text-[0.72rem] text-slate-400 print:text-black">{t('bill_grid_flatrate')}</div>
                                    </td>
                                    <td class="py-3 text-right font-mono font-semibold">{money(selectedInvoiceForPrint.baseFee)}</td>
                                </tr>
                            </tbody>
                        </table>

                        <div class="flex justify-end items-center gap-6 mt-2 border-t border-slate-700/60 pt-4 print:border-black">
                            <span class="text-sm font-bold text-slate-400 print:text-black">{t('bill_total_due')}</span>
                            <span class="text-2xl font-black text-sky-400 print:text-black">{money(selectedInvoiceForPrint.totalCost)}</span>
                        </div>

                        <div class="flex items-center justify-end gap-2 border-t border-slate-700/60 pt-4 mt-2 print:hidden">
                            <button
                                onClick={() => setSelectedInvoiceForPrint(null)}
                                class="py-2 px-4 rounded-lg bg-slate-700 text-slate-200 text-xs font-semibold hover:bg-slate-600 transition-colors"
                            >
                                {t('bill_close_btn')}
                            </button>
                            <button
                                onClick={() => window.print()}
                                class="py-2 px-4 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors"
                            >
                                <i class="fas fa-print mr-1.5"></i>{t('bill_print_btn')}
                            </button>
                        </div>
                    </div>
                </div>,
                document.body
            )}
        </div>
    );
}
