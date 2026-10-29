import { jsx as _jsx, jsxs as _jsxs, Fragment as _Fragment } from "preact/jsx-runtime";
import { useState, useEffect } from 'preact/hooks';
import { Fragment } from 'preact';
import { createPortal } from 'preact/compat';
import { t } from '../i18n';
const money = (v) => `${v.toFixed(2)} €`;
const kwh = (v) => `${v.toFixed(2)} kWh`;
function Modal({ title, icon, onClose, children }) {
    useEffect(() => {
        const onKey = (e) => { if (e.key === 'Escape')
            onClose(); };
        window.addEventListener('keydown', onKey);
        return () => window.removeEventListener('keydown', onKey);
    }, [onClose]);
    return createPortal(_jsx("div", { class: "fixed inset-0 bg-black/75 backdrop-blur-sm flex items-center justify-center z-[100] p-4", onClick: onClose, children: _jsxs("div", { class: "bg-slate-800 border border-slate-700 rounded-xl shadow-2xl w-full max-w-lg max-h-[90vh] overflow-y-auto", onClick: (e) => e.stopPropagation(), children: [_jsxs("div", { class: "px-6 py-4 border-b border-slate-700 bg-black/15 flex items-center justify-between", children: [_jsxs("h3", { class: "font-bold text-slate-50 text-base", children: [_jsx("i", { class: `fas ${icon} mr-2 text-sky-400` }), title] }), _jsx("button", { onClick: onClose, class: "text-slate-400 hover:text-slate-200 transition-colors", children: _jsx("i", { class: "fas fa-times" }) })] }), _jsx("div", { class: "p-6", children: children })] }) }), document.body);
}
export function BillingPage() {
    const [activeTab, setActiveTab] = useState('invoices');
    // General state
    const [tariffs, setTariffs] = useState({ ratePerKwh: 0.30, baseMonthlyFee: 10.00 });
    const [customers, setCustomers] = useState([]);
    const [rfids, setRfids] = useState([]);
    const [tenants, setTenants] = useState([]);
    const [loading, setLoading] = useState(true);
    // Invoices state
    const [selectedYear, setSelectedYear] = useState(new Date().getFullYear());
    const [selectedMonth, setSelectedMonth] = useState(new Date().getMonth() + 1);
    const [invoices, setInvoices] = useState([]);
    const [invoiceLoading, setInvoiceLoading] = useState(false);
    const [expandedInvoiceId, setExpandedInvoiceId] = useState(null);
    const [selectedInvoiceForPrint, setSelectedInvoiceForPrint] = useState(null);
    // Add forms state
    const [newCustomer, setNewCustomer] = useState({ id: '', name: '', address: '', email: '', note: '' });
    const [newRfid, setNewRfid] = useState({ idTag: '', userName: '', customerId: '' });
    const [newTenant, setNewTenant] = useState({ id: '', name: '', meterKey: '', customerId: '' });
    // Add-dialog visibility
    const [showCustomerDialog, setShowCustomerDialog] = useState(false);
    const [showRfidDialog, setShowRfidDialog] = useState(false);
    const [showTenantDialog, setShowTenantDialog] = useState(false);
    // Inline editing state
    const [editingCustomerId, setEditingCustomerId] = useState(null);
    const [editingCustomer, setEditingCustomer] = useState({ id: '', name: '', address: '', email: '', note: '' });
    const [editingRfidTag, setEditingRfidTag] = useState(null);
    const [editingRfidName, setEditingRfidName] = useState('');
    const [editingRfidCustomerId, setEditingRfidCustomerId] = useState('');
    const [editingTenantId, setEditingTenantId] = useState(null);
    const [editingTenantName, setEditingTenantName] = useState('');
    const [editingTenantMeterKey, setEditingTenantMeterKey] = useState('');
    const [editingTenantCustomerId, setEditingTenantCustomerId] = useState('');
    // Meter replacements state
    const [expandedReplacementTenantId, setExpandedReplacementTenantId] = useState(null);
    const [replacements, setReplacements] = useState({});
    const [newReplacement, setNewReplacement] = useState({ replacedAt: '', oldFinalKwh: '', newStartKwh: '', note: '' });
    const fetchTariffs = async () => {
        try {
            const res = await fetch('/plswk/api/billing/tariffs');
            if (res.ok)
                setTariffs(await res.json());
        }
        catch (e) {
            console.error(e);
        }
    };
    const fetchCustomers = async () => {
        try {
            const res = await fetch('/plswk/api/billing/customers');
            if (res.ok)
                setCustomers(await res.json());
        }
        catch (e) {
            console.error(e);
        }
    };
    const fetchRfids = async () => {
        try {
            const res = await fetch('/plswk/api/billing/rfid');
            if (res.ok)
                setRfids(await res.json());
        }
        catch (e) {
            console.error(e);
        }
    };
    const fetchTenants = async () => {
        try {
            const res = await fetch('/plswk/api/billing/tenants');
            if (res.ok)
                setTenants(await res.json());
        }
        catch (e) {
            console.error(e);
        }
    };
    const fetchReplacements = async (tenantId) => {
        try {
            const res = await fetch(`/plswk/api/billing/meter-replacements?tenantId=${encodeURIComponent(tenantId)}`);
            if (res.ok) {
                const data = await res.json();
                setReplacements(prev => ({ ...prev, [tenantId]: data }));
            }
        }
        catch (e) {
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
        }
        catch (e) {
            console.error(e);
        }
        finally {
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
    const handleUpdateTariffs = async (e) => {
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
        }
        catch (e) {
            console.error(e);
        }
    };
    // ── Customers ────────────────────────────────────────────────────────
    const handleAddCustomer = async (e) => {
        e.preventDefault();
        if (!newCustomer.id.trim() || !newCustomer.name.trim())
            return;
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
        }
        catch (e) {
            console.error(e);
        }
    };
    const handleSaveCustomerEdit = async () => {
        if (!editingCustomer.id || !editingCustomer.name.trim())
            return;
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
        }
        catch (e) {
            console.error(e);
        }
    };
    const handleDeleteCustomer = async (id) => {
        if (!confirm(t('bill_delete_customer_confirm')))
            return;
        try {
            const res = await fetch(`/plswk/api/billing/customers/${encodeURIComponent(id)}`, { method: 'DELETE' });
            if (res.ok) {
                await fetchCustomers();
                await Promise.all([fetchRfids(), fetchTenants()]);
            }
        }
        catch (e) {
            console.error(e);
        }
    };
    // ── RFID cards ───────────────────────────────────────────────────────
    const handleAddRfid = async (e) => {
        e.preventDefault();
        if (!newRfid.idTag.trim() || !newRfid.userName.trim())
            return;
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
        }
        catch (e) {
            console.error(e);
        }
    };
    const handleDeleteRfid = async (idTag) => {
        if (!confirm(`${t('bill_remove_rfid_confirm')} ${idTag}?`))
            return;
        try {
            const res = await fetch(`/plswk/api/billing/rfid/${encodeURIComponent(idTag)}`, { method: 'DELETE' });
            if (res.ok)
                fetchRfids();
        }
        catch (e) {
            console.error(e);
        }
    };
    const handleSaveRfidEdit = async (idTag) => {
        if (!editingRfidName.trim())
            return;
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
        }
        catch (e) {
            console.error(e);
        }
    };
    // ── Tenants ──────────────────────────────────────────────────────────
    const handleAddTenant = async (e) => {
        e.preventDefault();
        if (!newTenant.id.trim() || !newTenant.name.trim() || !newTenant.meterKey.trim())
            return;
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
        }
        catch (e) {
            console.error(e);
        }
    };
    const handleDeleteTenant = async (id) => {
        if (!confirm(`${t('bill_remove_tenant_confirm')} ${id}?`))
            return;
        try {
            const res = await fetch(`/plswk/api/billing/tenants/${encodeURIComponent(id)}`, { method: 'DELETE' });
            if (res.ok)
                fetchTenants();
        }
        catch (e) {
            console.error(e);
        }
    };
    const handleSaveTenantEdit = async (id) => {
        if (!editingTenantName.trim() || !editingTenantMeterKey.trim())
            return;
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
        }
        catch (e) {
            console.error(e);
        }
    };
    // ── Meter replacements ───────────────────────────────────────────────
    const handleAddReplacement = async (tenantId, e) => {
        e.preventDefault();
        if (!newReplacement.replacedAt)
            return;
        const replacedAtMs = new Date(newReplacement.replacedAt).getTime();
        if (isNaN(replacedAtMs))
            return;
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
        }
        catch (e) {
            console.error(e);
        }
    };
    const handleDeleteReplacement = async (tenantId, id) => {
        if (!confirm(t('bill_remove_event_confirm')))
            return;
        try {
            const res = await fetch(`/plswk/api/billing/meter-replacements/${id}`, { method: 'DELETE' });
            if (res.ok)
                fetchReplacements(tenantId);
        }
        catch (e) {
            console.error(e);
        }
    };
    // ── Derived invoice data ─────────────────────────────────────────────
    const customerGroups = (() => {
        const map = new Map();
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
            if (a.customerId === null)
                return 1;
            if (b.customerId === null)
                return -1;
            return a.customerName.localeCompare(b.customerName);
        });
    })();
    const grandTotal = customerGroups.reduce((sum, g) => sum + g.totalCost, 0);
    const grandKwh = customerGroups.reduce((sum, g) => sum + g.totalKwh, 0);
    const customerOptions = (_jsxs(_Fragment, { children: [_jsx("option", { value: "", children: t('bill_no_customer') }), customers.map(c => _jsx("option", { value: c.id, children: c.name }, c.id))] }));
    if (loading) {
        return (_jsx("div", { class: "h-full flex items-center justify-center p-16", children: _jsxs("div", { class: "flex flex-col items-center gap-4 text-cyan-400", children: [_jsx("i", { class: "fas fa-spinner fa-spin text-3xl" }), _jsx("p", { class: "text-sm font-medium animate-pulse", children: t('loading') })] }) }));
    }
    const tabClass = (tab) => `py-2.5 px-4 text-sm font-semibold rounded-t-lg border-b-2 transition-all whitespace-nowrap ${activeTab === tab ? 'text-sky-400 border-sky-400 bg-sky-400/5' : 'text-slate-400 border-transparent hover:text-slate-200'}`;
    return (_jsxs("div", { class: "flex flex-col gap-6 w-full page-enter print:p-0", children: [_jsxs("div", { class: "flex border-b border-slate-700/60 pb-px gap-2 shrink-0 overflow-x-auto print:hidden", children: [_jsxs("button", { onClick: () => setActiveTab('invoices'), class: tabClass('invoices'), children: [_jsx("i", { class: "fas fa-file-invoice-dollar mr-2" }), t('bill_monthly_invoices')] }), _jsxs("button", { onClick: () => setActiveTab('customers'), class: tabClass('customers'), children: [_jsx("i", { class: "fas fa-address-book mr-2" }), t('bill_customers')] }), _jsxs("button", { onClick: () => setActiveTab('rfids'), class: tabClass('rfids'), children: [_jsx("i", { class: "fas fa-id-card mr-2" }), t('bill_rfid_cards')] }), _jsxs("button", { onClick: () => setActiveTab('tenants'), class: tabClass('tenants'), children: [_jsx("i", { class: "fas fa-users mr-2" }), t('bill_metered_tenants')] }), _jsxs("button", { onClick: () => setActiveTab('tariffs'), class: tabClass('tariffs'), children: [_jsx("i", { class: "fas fa-sliders-h mr-2" }), t('bill_tariff_pricing')] })] }), activeTab === 'invoices' && (_jsxs("div", { class: "flex flex-col gap-5", children: [_jsxs("div", { class: "glass p-5 rounded-xl border border-slate-700/60 flex flex-wrap items-center justify-between gap-4 print:hidden", children: [_jsx("div", { class: "flex items-center gap-3", children: _jsxs("div", { children: [_jsx("label", { class: "text-[0.65rem] font-bold text-slate-400 uppercase tracking-wider block mb-1", children: t('bill_billing_period') }), _jsxs("div", { class: "flex items-center gap-2", children: [_jsx("select", { value: selectedYear, onChange: (e) => setSelectedYear(parseInt(e.target.value)), class: "bg-slate-900 border border-slate-700 rounded-lg px-3 py-1.5 text-sm text-slate-100 focus:outline-none focus:border-sky-400 font-semibold", children: [2024, 2025, 2026, 2027].map(y => _jsx("option", { value: y, children: y }, y)) }), _jsx("select", { value: selectedMonth, onChange: (e) => setSelectedMonth(parseInt(e.target.value)), class: "bg-slate-900 border border-slate-700 rounded-lg px-3 py-1.5 text-sm text-slate-100 focus:outline-none focus:border-sky-400 font-semibold", children: Array.from({ length: 12 }, (_, i) => i + 1).map(m => (_jsx("option", { value: m, children: new Date(2000, m - 1).toLocaleString('default', { month: 'long' }) }, m))) })] })] }) }), _jsxs("div", { class: "flex items-center gap-6", children: [_jsxs("div", { class: "text-right", children: [_jsx("span", { class: "text-[0.65rem] font-bold text-slate-400 uppercase tracking-wider block", children: t('bill_consumption') }), _jsx("span", { class: "text-sm font-bold text-slate-100 font-mono", children: kwh(grandKwh) })] }), _jsxs("div", { class: "text-right", children: [_jsx("span", { class: "text-[0.65rem] font-bold text-slate-400 uppercase tracking-wider block", children: t('bill_grand_total') }), _jsx("span", { class: "text-lg font-black text-sky-400 font-mono", children: money(grandTotal) })] }), _jsxs("div", { class: "text-right border-l border-slate-700/60 pl-6", children: [_jsx("span", { class: "text-[0.65rem] font-bold text-slate-400 uppercase tracking-wider block", children: t('bill_standard_tariff') }), _jsxs("span", { class: "text-xs font-semibold text-slate-300 font-mono", children: [tariffs.ratePerKwh.toFixed(2), " \u20AC/kWh \u00B7 ", tariffs.baseMonthlyFee.toFixed(2), " \u20AC ", t('bill_base')] })] })] })] }), invoiceLoading ? (_jsxs("div", { class: "p-16 text-center text-cyan-400", children: [_jsx("i", { class: "fas fa-spinner fa-spin text-2xl mr-2" }), t('loading')] })) : customerGroups.length === 0 ? (_jsx("div", { class: "p-16 text-center text-slate-500 font-medium bg-slate-800 border border-slate-700 rounded-xl", children: t('bill_no_records') })) : (_jsxs(_Fragment, { children: [_jsxs("div", { children: [_jsx("h3", { class: "text-[0.7rem] font-bold text-slate-400 uppercase tracking-wider mb-2", children: t('bill_per_customer_summary') }), _jsx("div", { class: "grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-3 gap-3", children: customerGroups.map(g => (_jsxs("div", { class: "glass rounded-xl border border-slate-700/60 p-4 flex flex-col gap-2", children: [_jsxs("div", { class: "flex items-start justify-between gap-2", children: [_jsxs("div", { class: "min-w-0", children: [_jsx("div", { class: `font-bold text-sm truncate ${g.customerId ? 'text-slate-100' : 'text-amber-400'}`, children: g.customerId ? g.customerName : t('bill_unassigned') }), _jsx("div", { class: "text-[0.68rem] text-slate-500 truncate", children: g.customerId ? (g.customerAddress || g.customerEmail || g.customerId) : t('bill_no_customer') })] }), _jsxs("span", { class: "text-[0.62rem] font-bold px-2 py-0.5 rounded-full bg-slate-700/60 text-slate-300 shrink-0", children: [g.invoices.length, " ", t('bill_invoices_count')] })] }), _jsxs("div", { class: "flex items-end justify-between gap-2 pt-1 border-t border-white/5", children: [_jsxs("div", { children: [_jsx("div", { class: "text-[0.6rem] font-bold text-slate-500 uppercase tracking-wider", children: t('bill_consumption') }), _jsx("div", { class: "text-xs font-mono text-slate-300", children: kwh(g.totalKwh) })] }), _jsxs("div", { class: "text-right", children: [_jsx("div", { class: "text-[0.6rem] font-bold text-slate-500 uppercase tracking-wider", children: t('bill_total_cost') }), _jsx("div", { class: "text-base font-black text-sky-400 font-mono", children: money(g.totalCost) })] })] })] }, g.key))) })] }), customerGroups.map(g => (_jsxs("div", { class: "bg-slate-800 border border-slate-700 rounded-xl overflow-hidden", children: [_jsxs("div", { class: "px-5 py-3.5 border-b border-slate-700 bg-black/20 flex flex-wrap items-center justify-between gap-3", children: [_jsxs("div", { class: "flex items-center gap-3 min-w-0", children: [_jsx("div", { class: `w-9 h-9 rounded-lg flex items-center justify-center shrink-0 ${g.customerId ? 'bg-sky-500/15 text-sky-400' : 'bg-amber-500/15 text-amber-400'}`, children: _jsx("i", { class: `fas ${g.customerId ? 'fa-user' : 'fa-user-slash'} text-sm` }) }), _jsxs("div", { class: "min-w-0", children: [_jsx("div", { class: "font-bold text-slate-50 text-sm truncate", children: g.customerId ? g.customerName : t('bill_unassigned') }), _jsx("div", { class: "text-[0.68rem] text-slate-500 truncate", children: [g.customerAddress, g.customerEmail].filter(Boolean).join(' · ') || g.customerId || '—' })] })] }), _jsxs("div", { class: "flex items-center gap-5", children: [_jsxs("div", { class: "text-right", children: [_jsx("div", { class: "text-[0.6rem] font-bold text-slate-500 uppercase tracking-wider", children: t('bill_consumption') }), _jsx("div", { class: "text-xs font-mono text-slate-300", children: kwh(g.totalKwh) })] }), _jsxs("div", { class: "text-right", children: [_jsx("div", { class: "text-[0.6rem] font-bold text-slate-500 uppercase tracking-wider", children: t('bill_total_cost') }), _jsx("div", { class: "text-sm font-black text-sky-400 font-mono", children: money(g.totalCost) })] })] })] }), _jsx("div", { class: "overflow-x-auto", children: _jsxs("table", { class: "lv-table text-sm", children: [_jsx("thead", { children: _jsxs("tr", { children: [_jsx("th", { children: t('bill_invoice_no') }), _jsx("th", { children: t('bill_source') }), _jsx("th", { children: t('bill_details') }), _jsx("th", { class: "text-right", children: t('bill_consumption') }), _jsx("th", { class: "text-right", children: t('bill_energy_cost') }), _jsx("th", { class: "text-right", children: t('bill_base') }), _jsx("th", { class: "text-right", children: t('bill_total_cost') }), _jsx("th", { class: "text-center print:hidden", children: t('bill_action') })] }) }), _jsx("tbody", { children: g.invoices.map(inv => {
                                                        const isOpen = expandedInvoiceId === inv.invoiceId;
                                                        return (_jsxs(Fragment, { children: [_jsxs("tr", { children: [_jsx("td", { class: "font-mono text-[0.7rem] text-slate-400", children: inv.invoiceId }), _jsx("td", { children: _jsxs("div", { class: "flex items-center gap-1.5 flex-wrap", children: [_jsx("span", { class: `inline-block text-[0.68rem] font-bold px-2 py-0.5 rounded-full ${inv.source === 'ev' ? 'bg-amber-400/10 text-amber-400' : 'bg-sky-400/10 text-sky-400'}`, children: inv.source === 'ev' ? t('bill_ev_charging') : t('bill_tenant_meter') }), inv.replacementCount > 0 && (_jsxs("span", { class: "inline-flex items-center gap-1 text-[0.65rem] font-bold px-1.5 py-0.5 rounded-full bg-orange-500/15 text-orange-400", title: t('bill_meter_replaced_title'), children: [_jsx("i", { class: "fas fa-exchange-alt" }), inv.replacementCount, "\u00D7 ", t('bill_replaced_badge')] }))] }) }), _jsx("td", { class: "text-slate-400 font-mono text-[0.7rem] max-w-[220px] truncate", title: inv.details, children: inv.details }), _jsx("td", { class: "text-right font-mono text-slate-100 font-semibold", children: kwh(inv.totalKwh) }), _jsx("td", { class: "text-right font-mono text-slate-300", children: money(inv.energyCost) }), _jsx("td", { class: "text-right font-mono text-slate-400", children: money(inv.baseFee) }), _jsx("td", { class: "text-right font-mono text-sky-400 font-bold", children: money(inv.totalCost) }), _jsx("td", { class: "text-center print:hidden", children: _jsxs("div", { class: "flex items-center justify-center gap-1.5", children: [_jsxs("button", { onClick: () => setExpandedInvoiceId(isOpen ? null : inv.invoiceId), class: `py-1 px-2.5 rounded text-xs font-semibold transition-colors ${isOpen ? 'bg-sky-500/20 text-sky-300' : 'bg-slate-700 text-slate-200 hover:bg-slate-600'}`, title: isOpen ? t('bill_hide_items') : t('bill_show_items'), children: [_jsx("i", { class: `fas ${isOpen ? 'fa-chevron-up' : 'fa-list-ul'} mr-1` }), inv.transactionCount] }), _jsx("button", { onClick: () => setSelectedInvoiceForPrint(inv), class: "py-1 px-2.5 rounded bg-slate-700 text-slate-200 text-xs font-semibold hover:bg-slate-600 transition-colors", title: t('bill_print_btn'), children: _jsx("i", { class: "fas fa-file-invoice" }) })] }) })] }), isOpen && (_jsx("tr", { children: _jsx("td", { colspan: 8, class: "p-0", children: _jsxs("div", { class: "mx-4 mb-3 border border-sky-500/20 rounded-xl bg-sky-500/[0.03] overflow-hidden", children: [_jsxs("div", { class: "px-4 py-2 bg-sky-500/10 border-b border-sky-500/15 flex items-center justify-between", children: [_jsxs("span", { class: "text-[0.7rem] font-bold text-sky-400 uppercase tracking-wider", children: [_jsx("i", { class: "fas fa-list-ul mr-1.5" }), t('bill_line_items')] }), _jsxs("span", { class: "text-[0.65rem] text-sky-400/70", children: [inv.lineItems.length, " ", t('bill_items')] })] }), inv.lineItems.length === 0 ? (_jsx("div", { class: "px-4 py-3 text-xs text-slate-600 italic", children: t('bill_no_line_items') })) : (_jsxs("table", { class: "w-full text-xs", children: [_jsx("thead", { children: _jsxs("tr", { class: "text-[0.6rem] text-slate-500 uppercase tracking-wider border-b border-white/5", children: [_jsx("th", { class: "px-4 py-2 text-left", children: t('bill_date') }), _jsx("th", { class: "px-4 py-2 text-left", children: t('bill_desc') }), _jsx("th", { class: "px-4 py-2 text-left", children: t('bill_token_id') }), _jsx("th", { class: "px-4 py-2 text-right", children: t('bill_quantity') }), _jsx("th", { class: "px-4 py-2 text-right", children: t('bill_unit_price') }), _jsx("th", { class: "px-4 py-2 text-right", children: t('bill_amount') })] }) }), _jsx("tbody", { children: inv.lineItems.map((li, i) => (_jsxs("tr", { class: "border-b border-white/5 hover:bg-white/[0.02]", children: [_jsx("td", { class: "px-4 py-1.5 font-mono text-slate-400 whitespace-nowrap", children: li.date }), _jsx("td", { class: "px-4 py-1.5 text-slate-200", children: li.description }), _jsx("td", { class: "px-4 py-1.5 font-mono text-slate-500", children: li.idTag }), _jsxs("td", { class: "px-4 py-1.5 text-right font-mono text-slate-300", children: [li.quantity.toFixed(3), " ", li.unit] }), _jsxs("td", { class: "px-4 py-1.5 text-right font-mono text-slate-500", children: [li.unitPrice.toFixed(2), " \u20AC"] }), _jsx("td", { class: "px-4 py-1.5 text-right font-mono font-semibold text-slate-100", children: money(li.amount) })] }, i))) })] }))] }) }) }))] }, inv.invoiceId));
                                                    }) })] }) })] }, g.key)))] }))] })), activeTab === 'customers' && (_jsxs("div", { class: "bg-slate-800 border border-slate-700 rounded-xl overflow-hidden", children: [_jsxs("div", { class: "px-6 py-4 border-b border-slate-700 bg-black/15 flex items-center justify-between gap-4", children: [_jsxs("h3", { class: "font-bold text-slate-50 text-base", children: [t('bill_customers_title'), _jsx("span", { class: "ml-2 text-xs text-slate-400 font-medium", children: customers.length })] }), _jsxs("button", { onClick: () => setShowCustomerDialog(true), class: "px-4 py-2 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors", children: [_jsx("i", { class: "fas fa-plus mr-1.5" }), t('bill_new_customer')] })] }), _jsx("div", { class: "max-h-[640px] overflow-y-auto", children: customers.length === 0 ? (_jsx("div", { class: "p-16 text-center text-slate-500", children: t('bill_no_customers') })) : (_jsxs("table", { class: "lv-table text-sm", children: [_jsx("thead", { children: _jsxs("tr", { children: [_jsx("th", { children: t('bill_customer_id') }), _jsx("th", { children: t('bill_customer_name') }), _jsx("th", { children: t('bill_customer_address') }), _jsx("th", { children: t('bill_customer_email') }), _jsx("th", { class: "text-right", children: t('bill_actions') })] }) }), _jsx("tbody", { children: customers.map(c => {
                                        const isEditing = c.id === editingCustomerId;
                                        return (_jsxs("tr", { children: [_jsx("td", { class: "font-mono text-sky-400 text-xs", children: c.id }), _jsx("td", { class: "text-slate-100 font-bold", children: isEditing ? (_jsx("input", { type: "text", value: editingCustomer.name, onInput: (e) => setEditingCustomer({ ...editingCustomer, name: e.target.value }), class: "form-input w-full" })) : c.name }), _jsx("td", { class: "text-slate-400 text-xs", children: isEditing ? (_jsx("input", { type: "text", value: editingCustomer.address, onInput: (e) => setEditingCustomer({ ...editingCustomer, address: e.target.value }), class: "form-input w-full" })) : (c.address || '–') }), _jsx("td", { class: "text-slate-400 text-xs", children: isEditing ? (_jsx("input", { type: "text", value: editingCustomer.email, onInput: (e) => setEditingCustomer({ ...editingCustomer, email: e.target.value }), class: "form-input w-full" })) : (c.email || '–') }), _jsx("td", { class: "text-right", children: _jsx("div", { class: "flex justify-end gap-1.5", children: isEditing ? (_jsxs(_Fragment, { children: [_jsx("button", { onClick: handleSaveCustomerEdit, class: "p-1 px-2.5 rounded bg-emerald-500/20 text-emerald-400 hover:bg-emerald-500/30 text-xs font-semibold transition-colors", children: _jsx("i", { class: "fas fa-check" }) }), _jsx("button", { onClick: () => setEditingCustomerId(null), class: "p-1 px-2.5 rounded bg-slate-700/40 text-slate-300 hover:bg-slate-700/60 text-xs font-semibold transition-colors", children: _jsx("i", { class: "fas fa-times" }) })] })) : (_jsxs(_Fragment, { children: [_jsx("button", { onClick: () => {
                                                                        setEditingCustomerId(c.id);
                                                                        setEditingCustomer({
                                                                            id: c.id,
                                                                            name: c.name,
                                                                            address: c.address || '',
                                                                            email: c.email || '',
                                                                            note: c.note || ''
                                                                        });
                                                                    }, class: "p-1 px-2.5 rounded bg-sky-500/10 text-sky-400 hover:bg-sky-500/20 text-xs font-semibold transition-colors", children: _jsx("i", { class: "fas fa-edit" }) }), _jsx("button", { onClick: () => handleDeleteCustomer(c.id), class: "p-1 px-2.5 rounded bg-red-500/10 text-red-400 hover:bg-red-500/20 text-xs font-semibold transition-colors", children: _jsx("i", { class: "fas fa-trash-alt" }) })] })) }) })] }, c.id));
                                    }) })] })) })] })), activeTab === 'rfids' && (_jsxs("div", { class: "bg-slate-800 border border-slate-700 rounded-xl overflow-hidden", children: [_jsxs("div", { class: "px-6 py-4 border-b border-slate-700 bg-black/15 flex items-center justify-between gap-4", children: [_jsxs("h3", { class: "font-bold text-slate-50 text-base", children: [t('bill_registered_cards'), _jsx("span", { class: "ml-2 text-xs text-slate-400 font-medium", children: rfids.length })] }), _jsxs("button", { onClick: () => setShowRfidDialog(true), class: "px-4 py-2 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors", children: [_jsx("i", { class: "fas fa-plus mr-1.5" }), t('bill_new_rfid')] })] }), _jsx("div", { class: "max-h-[640px] overflow-y-auto", children: rfids.length === 0 ? (_jsx("div", { class: "p-16 text-center text-slate-500", children: t('bill_no_rfid') })) : (_jsxs("table", { class: "lv-table text-sm", children: [_jsx("thead", { children: _jsxs("tr", { children: [_jsx("th", { children: t('bill_token_id') }), _jsx("th", { children: t('bill_user_name') }), _jsx("th", { children: t('bill_customer') }), _jsx("th", { class: "text-right", children: t('bill_actions') })] }) }), _jsx("tbody", { children: rfids.map(r => {
                                        const isEditing = r.idTag === editingRfidTag;
                                        return (_jsxs("tr", { children: [_jsx("td", { class: "font-mono font-bold text-sky-400", children: r.idTag }), _jsx("td", { class: "text-slate-100", children: isEditing ? (_jsx("input", { type: "text", value: editingRfidName, onInput: (e) => setEditingRfidName(e.target.value), class: "form-input w-full max-w-[200px]" })) : r.userName }), _jsx("td", { children: isEditing ? (_jsx("select", { value: editingRfidCustomerId, onChange: (e) => setEditingRfidCustomerId(e.target.value), class: "form-input w-full max-w-[200px]", children: customerOptions })) : r.customerId ? (_jsxs("span", { class: "inline-flex items-center gap-1.5 text-xs font-semibold px-2 py-0.5 rounded-full bg-sky-500/10 text-sky-400", children: [_jsx("i", { class: "fas fa-user text-[0.6rem]" }), r.customerName || r.customerId] })) : (_jsx("span", { class: "text-xs text-amber-400/80 italic", children: t('bill_unassigned') })) }), _jsx("td", { class: "text-right", children: _jsx("div", { class: "flex justify-end gap-1.5", children: isEditing ? (_jsxs(_Fragment, { children: [_jsx("button", { onClick: () => handleSaveRfidEdit(r.idTag), class: "p-1 px-2.5 rounded bg-emerald-500/20 text-emerald-400 hover:bg-emerald-500/30 text-xs font-semibold transition-colors", children: _jsx("i", { class: "fas fa-check" }) }), _jsx("button", { onClick: () => setEditingRfidTag(null), class: "p-1 px-2.5 rounded bg-slate-700/40 text-slate-300 hover:bg-slate-700/60 text-xs font-semibold transition-colors", children: _jsx("i", { class: "fas fa-times" }) })] })) : (_jsxs(_Fragment, { children: [_jsx("button", { onClick: () => {
                                                                        setEditingRfidTag(r.idTag);
                                                                        setEditingRfidName(r.userName);
                                                                        setEditingRfidCustomerId(r.customerId || '');
                                                                    }, class: "p-1 px-2.5 rounded bg-sky-500/10 text-sky-400 hover:bg-sky-500/20 text-xs font-semibold transition-colors", children: _jsx("i", { class: "fas fa-edit" }) }), _jsx("button", { onClick: () => handleDeleteRfid(r.idTag), class: "p-1 px-2.5 rounded bg-red-500/10 text-red-400 hover:bg-red-500/20 text-xs font-semibold transition-colors", children: _jsx("i", { class: "fas fa-trash-alt" }) })] })) }) })] }, r.idTag));
                                    }) })] })) })] })), activeTab === 'tenants' && (_jsxs("div", { class: "bg-slate-800 border border-slate-700 rounded-xl overflow-hidden", children: [_jsxs("div", { class: "px-6 py-4 border-b border-slate-700 bg-black/15 flex items-center justify-between gap-4", children: [_jsxs("h3", { class: "font-bold text-slate-50 text-base", children: [t('bill_metered_tenants_title'), _jsx("span", { class: "ml-2 text-xs text-slate-400 font-medium", children: tenants.length })] }), _jsxs("button", { onClick: () => setShowTenantDialog(true), class: "px-4 py-2 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors", children: [_jsx("i", { class: "fas fa-plus mr-1.5" }), t('bill_new_tenant')] })] }), _jsx("div", { class: "max-h-[640px] overflow-y-auto", children: tenants.length === 0 ? (_jsx("div", { class: "p-16 text-center text-slate-500", children: t('bill_no_tenants') })) : (_jsxs("table", { class: "lv-table text-sm", children: [_jsx("thead", { children: _jsxs("tr", { children: [_jsx("th", { children: t('bill_tenant_id') }), _jsx("th", { children: t('bill_display_name') }), _jsx("th", { children: t('bill_influx_key') }), _jsx("th", { children: t('bill_customer') }), _jsx("th", { class: "text-right", children: t('bill_actions') })] }) }), _jsx("tbody", { children: tenants.map(ten => {
                                        const isEditing = ten.id === editingTenantId;
                                        const isExpanded = expandedReplacementTenantId === ten.id;
                                        const tenantReplacements = replacements[ten.id] ?? [];
                                        return (_jsxs(Fragment, { children: [_jsxs("tr", { children: [_jsx("td", { class: "font-mono text-slate-300", children: ten.id }), _jsx("td", { class: "text-slate-100 font-bold", children: isEditing ? (_jsx("input", { type: "text", value: editingTenantName, onInput: (e) => setEditingTenantName(e.target.value), class: "form-input w-full" })) : ten.name }), _jsx("td", { class: "text-sky-400 font-mono text-xs", children: isEditing ? (_jsx("input", { type: "text", value: editingTenantMeterKey, onInput: (e) => setEditingTenantMeterKey(e.target.value), class: "form-input w-full font-mono" })) : ten.meterKey }), _jsx("td", { children: isEditing ? (_jsx("select", { value: editingTenantCustomerId, onChange: (e) => setEditingTenantCustomerId(e.target.value), class: "form-input w-full max-w-[180px]", children: customerOptions })) : ten.customerId ? (_jsxs("span", { class: "inline-flex items-center gap-1.5 text-xs font-semibold px-2 py-0.5 rounded-full bg-sky-500/10 text-sky-400", children: [_jsx("i", { class: "fas fa-user text-[0.6rem]" }), ten.customerName || ten.customerId] })) : (_jsx("span", { class: "text-xs text-amber-400/80 italic", children: t('bill_unassigned') })) }), _jsx("td", { class: "text-right", children: _jsxs("div", { class: "flex justify-end gap-1.5", children: [_jsx("button", { title: t('bill_meter_replacements'), onClick: () => {
                                                                            const next = isExpanded ? null : ten.id;
                                                                            setExpandedReplacementTenantId(next);
                                                                            if (next)
                                                                                fetchReplacements(next);
                                                                        }, class: `p-1 px-2.5 rounded text-xs font-semibold transition-colors ${isExpanded
                                                                            ? 'bg-orange-500/20 text-orange-400'
                                                                            : 'bg-orange-500/5 text-orange-400/60 hover:bg-orange-500/15'}`, children: _jsx("i", { class: "fas fa-exchange-alt" }) }), isEditing ? (_jsxs(_Fragment, { children: [_jsx("button", { onClick: () => handleSaveTenantEdit(ten.id), class: "p-1 px-2.5 rounded bg-emerald-500/20 text-emerald-400 hover:bg-emerald-500/30 text-xs font-semibold transition-colors", children: _jsx("i", { class: "fas fa-check" }) }), _jsx("button", { onClick: () => setEditingTenantId(null), class: "p-1 px-2.5 rounded bg-slate-700/40 text-slate-300 hover:bg-slate-700/60 text-xs font-semibold transition-colors", children: _jsx("i", { class: "fas fa-times" }) })] })) : (_jsxs(_Fragment, { children: [_jsx("button", { onClick: () => {
                                                                                    setEditingTenantId(ten.id);
                                                                                    setEditingTenantName(ten.name);
                                                                                    setEditingTenantMeterKey(ten.meterKey);
                                                                                    setEditingTenantCustomerId(ten.customerId || '');
                                                                                }, class: "p-1 px-2.5 rounded bg-sky-500/10 text-sky-400 hover:bg-sky-500/20 text-xs font-semibold transition-colors", children: _jsx("i", { class: "fas fa-edit" }) }), _jsx("button", { onClick: () => handleDeleteTenant(ten.id), class: "p-1 px-2.5 rounded bg-red-500/10 text-red-400 hover:bg-red-500/20 text-xs font-semibold transition-colors", children: _jsx("i", { class: "fas fa-trash-alt" }) })] }))] }) })] }), isExpanded && (_jsx("tr", { children: _jsx("td", { colspan: 5, class: "p-0", children: _jsxs("div", { class: "mx-4 mb-3 border border-orange-500/20 rounded-xl bg-orange-500/[0.03] overflow-hidden", children: [_jsxs("div", { class: "px-4 py-2.5 bg-orange-500/10 border-b border-orange-500/15 flex items-center justify-between", children: [_jsxs("span", { class: "text-[0.72rem] font-bold text-orange-400 uppercase tracking-wider", children: [_jsx("i", { class: "fas fa-exchange-alt mr-1.5" }), t('bill_meter_replacements'), " \u2014 ", ten.name] }), _jsxs("span", { class: "text-[0.65rem] text-orange-400/60", children: [tenantReplacements.length, " ", t('bill_recorded')] })] }), tenantReplacements.length > 0 ? (_jsxs("table", { class: "w-full text-xs", children: [_jsx("thead", { children: _jsxs("tr", { class: "text-[0.62rem] text-slate-500 uppercase tracking-wider border-b border-white/5", children: [_jsx("th", { class: "px-4 py-2 text-left", children: t('bill_new_meter_start') }), _jsx("th", { class: "px-4 py-2 text-right", children: t('bill_old_final_kwh') }), _jsx("th", { class: "px-4 py-2 text-right", children: t('bill_new_start_kwh') }), _jsx("th", { class: "px-4 py-2 text-left", children: t('bill_note') }), _jsx("th", { class: "px-4 py-2" })] }) }), _jsx("tbody", { children: tenantReplacements.map(r => (_jsxs("tr", { class: "border-b border-white/5 hover:bg-white/[0.02]", children: [_jsx("td", { class: "px-4 py-2 font-mono text-slate-300", children: new Date(r.replacedAt).toLocaleString() }), _jsx("td", { class: "px-4 py-2 text-right font-mono text-slate-400", children: r.oldFinalKwh != null ? r.oldFinalKwh.toFixed(2) : '–' }), _jsx("td", { class: "px-4 py-2 text-right font-mono text-slate-400", children: r.newStartKwh != null ? r.newStartKwh.toFixed(2) : '–' }), _jsx("td", { class: "px-4 py-2 text-slate-500 italic", children: r.note || '–' }), _jsx("td", { class: "px-4 py-2 text-right", children: _jsx("button", { onClick: () => handleDeleteReplacement(ten.id, r.id), class: "p-1 px-2 rounded bg-red-500/10 text-red-400 hover:bg-red-500/20 text-xs font-semibold transition-colors", title: t('bill_remove_event'), children: _jsx("i", { class: "fas fa-trash-alt" }) }) })] }, r.id))) })] })) : (_jsx("div", { class: "px-4 py-3 text-xs text-slate-600 italic", children: t('bill_no_replacements') })), _jsxs("form", { onSubmit: (e) => handleAddReplacement(ten.id, e), class: "px-4 py-3 border-t border-orange-500/10 flex flex-wrap items-end gap-3", children: [_jsxs("div", { class: "flex flex-col gap-1", children: [_jsxs("label", { class: "text-[0.62rem] font-bold text-slate-500 uppercase tracking-wide", children: [t('bill_new_meter_start'), " *"] }), _jsx("input", { type: "datetime-local", required: true, value: newReplacement.replacedAt, onInput: (e) => setNewReplacement(p => ({ ...p, replacedAt: e.target.value })), class: "form-input text-xs py-1.5" })] }), _jsxs("div", { class: "flex flex-col gap-1", children: [_jsx("label", { class: "text-[0.62rem] font-bold text-slate-500 uppercase tracking-wide", children: t('bill_old_final_kwh') }), _jsx("input", { type: "number", step: "0.001", min: "0", value: newReplacement.oldFinalKwh, onInput: (e) => setNewReplacement(p => ({ ...p, oldFinalKwh: e.target.value })), placeholder: "e.g. 98751.2", class: "form-input text-xs py-1.5 font-mono w-32" })] }), _jsxs("div", { class: "flex flex-col gap-1", children: [_jsx("label", { class: "text-[0.62rem] font-bold text-slate-500 uppercase tracking-wide", children: t('bill_new_start_kwh') }), _jsx("input", { type: "number", step: "0.001", min: "0", value: newReplacement.newStartKwh, onInput: (e) => setNewReplacement(p => ({ ...p, newStartKwh: e.target.value })), placeholder: "e.g. 0.0", class: "form-input text-xs py-1.5 font-mono w-32" })] }), _jsxs("div", { class: "flex flex-col gap-1 flex-1 min-w-[160px]", children: [_jsx("label", { class: "text-[0.62rem] font-bold text-slate-500 uppercase tracking-wide", children: t('bill_note') }), _jsx("input", { type: "text", value: newReplacement.note, onInput: (e) => setNewReplacement(p => ({ ...p, note: e.target.value })), placeholder: "e.g. Serial #OLD \u2192 #NEW", class: "form-input text-xs py-1.5" })] }), _jsxs("button", { type: "submit", class: "py-1.5 px-4 rounded-lg bg-orange-500/20 text-orange-400 hover:bg-orange-500/30 text-xs font-bold transition-colors shrink-0", children: [_jsx("i", { class: "fas fa-plus mr-1" }), t('bill_record')] })] })] }) }) }))] }, ten.id));
                                    }) })] })) })] })), activeTab === 'tariffs' && (_jsxs("div", { class: "glass p-6 rounded-xl border border-slate-700/60 max-w-lg", children: [_jsx("h3", { class: "font-bold text-slate-50 text-base mb-4 border-b border-slate-700 pb-2", children: t('bill_tariffs_settings') }), _jsxs("form", { onSubmit: handleUpdateTariffs, class: "flex flex-col gap-5", children: [_jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide", children: t('bill_rate_kwh') }), _jsxs("div", { class: "relative", children: [_jsx("span", { class: "absolute left-3 top-2 text-slate-400 text-sm font-semibold", children: "\u20AC" }), _jsx("input", { type: "number", step: "0.001", required: true, value: tariffs.ratePerKwh, onInput: (e) => setTariffs({ ...tariffs, ratePerKwh: parseFloat(e.target.value) }), class: "form-input pl-8 font-mono" })] }), _jsx("span", { class: "text-[0.68rem] text-slate-500", children: t('bill_rate_desc') })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide", children: t('bill_base_fee') }), _jsxs("div", { class: "relative", children: [_jsx("span", { class: "absolute left-3 top-2 text-slate-400 text-sm font-semibold", children: "\u20AC" }), _jsx("input", { type: "number", step: "0.01", required: true, value: tariffs.baseMonthlyFee, onInput: (e) => setTariffs({ ...tariffs, baseMonthlyFee: parseFloat(e.target.value) }), class: "form-input pl-8 font-mono" })] }), _jsx("span", { class: "text-[0.68rem] text-slate-500", children: t('bill_base_desc') })] }), _jsxs("button", { type: "submit", class: "py-2.5 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors mt-2", children: [_jsx("i", { class: "fas fa-check mr-1.5" }), t('bill_apply_model')] })] })] })), showCustomerDialog && (_jsx(Modal, { title: t('bill_new_customer'), icon: "fa-user-plus", onClose: () => setShowCustomerDialog(false), children: _jsxs("form", { onSubmit: handleAddCustomer, class: "flex flex-col gap-4", children: [_jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide", children: t('bill_customer_id') }), _jsx("input", { type: "text", required: true, value: newCustomer.id, onInput: (e) => setNewCustomer({ ...newCustomer, id: e.target.value }), placeholder: "e.g. cust_mueller", class: "form-input font-mono" })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide", children: t('bill_customer_name') }), _jsx("input", { type: "text", required: true, value: newCustomer.name, onInput: (e) => setNewCustomer({ ...newCustomer, name: e.target.value }), placeholder: "e.g. M\u00FCller GmbH", class: "form-input" })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide", children: t('bill_customer_address') }), _jsx("textarea", { rows: 2, value: newCustomer.address, onInput: (e) => setNewCustomer({ ...newCustomer, address: e.target.value }), placeholder: "Street 1, 12345 City", class: "form-input resize-none" })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide", children: t('bill_customer_email') }), _jsx("input", { type: "email", value: newCustomer.email, onInput: (e) => setNewCustomer({ ...newCustomer, email: e.target.value }), placeholder: "billing@example.com", class: "form-input" })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide", children: t('bill_customer_note') }), _jsx("input", { type: "text", value: newCustomer.note, onInput: (e) => setNewCustomer({ ...newCustomer, note: e.target.value }), class: "form-input" })] }), _jsxs("div", { class: "flex justify-end gap-2 mt-2", children: [_jsx("button", { type: "button", onClick: () => setShowCustomerDialog(false), class: "px-4 py-2 rounded-lg bg-slate-700/50 text-slate-300 text-xs font-bold hover:bg-slate-700 transition-colors", children: t('cancel') }), _jsxs("button", { type: "submit", class: "px-4 py-2 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors", children: [_jsx("i", { class: "fas fa-plus mr-1.5" }), t('bill_add_customer_btn')] })] })] }) })), showRfidDialog && (_jsx(Modal, { title: t('bill_new_rfid'), icon: "fa-id-card", onClose: () => setShowRfidDialog(false), children: _jsxs("form", { onSubmit: handleAddRfid, class: "flex flex-col gap-4", children: [_jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide", children: t('bill_rfid_token') }), _jsx("input", { type: "text", required: true, value: newRfid.idTag, onInput: (e) => setNewRfid({ ...newRfid, idTag: e.target.value }), placeholder: "e.g. 04A1B2C3", class: "form-input font-mono" })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide", children: t('bill_owner_name') }), _jsx("input", { type: "text", required: true, value: newRfid.userName, onInput: (e) => setNewRfid({ ...newRfid, userName: e.target.value }), placeholder: "e.g. John Doe", class: "form-input" })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide", children: t('bill_customer') }), _jsx("select", { value: newRfid.customerId, onChange: (e) => setNewRfid({ ...newRfid, customerId: e.target.value }), class: "form-input", children: customerOptions })] }), _jsxs("div", { class: "flex justify-end gap-2 mt-2", children: [_jsx("button", { type: "button", onClick: () => setShowRfidDialog(false), class: "px-4 py-2 rounded-lg bg-slate-700/50 text-slate-300 text-xs font-bold hover:bg-slate-700 transition-colors", children: t('cancel') }), _jsxs("button", { type: "submit", class: "px-4 py-2 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors", children: [_jsx("i", { class: "fas fa-plus mr-1.5" }), t('bill_add_mapping')] })] })] }) })), showTenantDialog && (_jsx(Modal, { title: t('bill_new_tenant'), icon: "fa-gauge-high", onClose: () => setShowTenantDialog(false), children: _jsxs("form", { onSubmit: handleAddTenant, class: "flex flex-col gap-4", children: [_jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide", children: t('bill_tenant_id') }), _jsx("input", { type: "text", required: true, value: newTenant.id, onInput: (e) => setNewTenant({ ...newTenant, id: e.target.value }), placeholder: "e.g. tenant_101", class: "form-input font-mono" })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide", children: t('bill_display_name') }), _jsx("input", { type: "text", required: true, value: newTenant.name, onInput: (e) => setNewTenant({ ...newTenant, name: e.target.value }), placeholder: "e.g. Apartment 3B (Miller)", class: "form-input" })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide", children: t('bill_meter_key') }), _jsx("input", { type: "text", required: true, value: newTenant.meterKey, onInput: (e) => setNewTenant({ ...newTenant, meterKey: e.target.value }), placeholder: "e.g. dev-meter1_energy-active-kwh", class: "form-input font-mono" })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide", children: t('bill_customer') }), _jsx("select", { value: newTenant.customerId, onChange: (e) => setNewTenant({ ...newTenant, customerId: e.target.value }), class: "form-input", children: customerOptions })] }), _jsxs("div", { class: "flex justify-end gap-2 mt-2", children: [_jsx("button", { type: "button", onClick: () => setShowTenantDialog(false), class: "px-4 py-2 rounded-lg bg-slate-700/50 text-slate-300 text-xs font-bold hover:bg-slate-700 transition-colors", children: t('cancel') }), _jsxs("button", { type: "submit", class: "px-4 py-2 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors", children: [_jsx("i", { class: "fas fa-plus mr-1.5" }), t('bill_add_tenant_btn')] })] })] }) })), selectedInvoiceForPrint !== null && createPortal(_jsx("div", { class: "fixed inset-0 bg-black/75 backdrop-blur-sm flex items-center justify-center z-[100] p-4 print:static print:bg-transparent print:p-0 print:block", children: _jsxs("div", { class: "bg-slate-800 border border-slate-700 rounded-xl shadow-2xl max-w-2xl w-full max-h-[90vh] overflow-y-auto p-8 text-slate-100 flex flex-col gap-6 print:border-none print:bg-transparent print:p-0 print:text-black print:max-h-none print:overflow-visible", children: [_jsxs("div", { class: "flex justify-between items-start border-b border-slate-700/60 pb-5 print:border-black print:pb-2", children: [_jsxs("div", { children: [_jsx("h2", { class: "text-xl font-extrabold text-slate-50 print:text-black print:text-2xl", children: t('bill_invoice_header') }), _jsxs("p", { class: "text-xs text-slate-400 mt-1 print:text-black font-mono", children: [t('bill_invoice_no'), ": ", selectedInvoiceForPrint.invoiceId] }), _jsxs("p", { class: "text-xs text-slate-400 mt-0.5 print:text-black font-mono", children: [t('bill_invoice_date'), ": ", new Date().toLocaleDateString(), " \u00B7 ", t('bill_invoice_period'), ": ", selectedInvoiceForPrint.billingPeriod] })] }), _jsx("button", { onClick: () => setSelectedInvoiceForPrint(null), class: "text-slate-400 hover:text-slate-200 print:hidden", children: _jsx("i", { class: "fas fa-times text-xl" }) })] }), _jsxs("div", { class: "grid grid-cols-2 gap-4 bg-black/10 p-4 rounded-lg border border-slate-700/40 print:bg-transparent print:border-none print:p-0", children: [_jsxs("div", { children: [_jsx("span", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wider block print:text-black", children: t('bill_billed_to') }), _jsx("strong", { class: "text-slate-100 text-base mt-1 block print:text-black", children: selectedInvoiceForPrint.customerId ? selectedInvoiceForPrint.customerName : t('bill_unassigned') }), selectedInvoiceForPrint.customerAddress && (_jsx("span", { class: "text-xs text-slate-400 block whitespace-pre-line print:text-black", children: selectedInvoiceForPrint.customerAddress })), selectedInvoiceForPrint.customerEmail && (_jsx("span", { class: "text-xs text-slate-400 font-mono block print:text-black", children: selectedInvoiceForPrint.customerEmail }))] }), _jsxs("div", { class: "text-right", children: [_jsx("span", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wider block print:text-black", children: t('bill_acc_type') }), _jsx("span", { class: "text-sm font-semibold text-slate-200 mt-1 block print:text-black", children: selectedInvoiceForPrint.source === 'ev' ? t('bill_ev_charging') : t('bill_tenant_meter') }), _jsx("span", { class: "text-xs text-slate-500 font-mono block print:text-black", children: selectedInvoiceForPrint.details })] })] }), _jsxs("table", { class: "w-full border-collapse text-sm text-slate-200 print:text-black", children: [_jsx("thead", { children: _jsxs("tr", { class: "text-left text-[0.68rem] font-bold uppercase tracking-wider text-slate-400 border-b border-slate-700 print:border-black print:text-black", children: [_jsx("th", { class: "py-2", children: t('bill_date') }), _jsx("th", { class: "py-2", children: t('bill_desc') }), _jsx("th", { class: "py-2 text-right", children: t('bill_quantity') }), _jsx("th", { class: "py-2 text-right", children: t('bill_unit_price') }), _jsx("th", { class: "py-2 text-right", children: t('bill_total') })] }) }), _jsxs("tbody", { children: [selectedInvoiceForPrint.lineItems.map((li, i) => (_jsxs("tr", { class: "border-b border-slate-700/30 print:border-gray-300", children: [_jsx("td", { class: "py-2 font-mono text-xs whitespace-nowrap", children: li.date }), _jsxs("td", { class: "py-2", children: [_jsx("div", { class: "font-semibold print:text-black", children: li.description }), _jsx("div", { class: "text-[0.7rem] text-slate-500 font-mono print:text-black", children: li.idTag })] }), _jsxs("td", { class: "py-2 text-right font-mono", children: [li.quantity.toFixed(3), " ", li.unit] }), _jsxs("td", { class: "py-2 text-right font-mono", children: [li.unitPrice.toFixed(2), " \u20AC"] }), _jsx("td", { class: "py-2 text-right font-mono font-semibold", children: money(li.amount) })] }, i))), _jsxs("tr", { class: "border-b border-slate-700/30 print:border-gray-300", children: [_jsxs("td", { class: "py-3", colspan: 4, children: [_jsx("div", { class: "font-semibold print:text-black", children: t('bill_base_service_fee') }), _jsx("div", { class: "text-[0.72rem] text-slate-400 print:text-black", children: t('bill_grid_flatrate') })] }), _jsx("td", { class: "py-3 text-right font-mono font-semibold", children: money(selectedInvoiceForPrint.baseFee) })] })] })] }), _jsxs("div", { class: "flex justify-end items-center gap-6 mt-2 border-t border-slate-700/60 pt-4 print:border-black", children: [_jsx("span", { class: "text-sm font-bold text-slate-400 print:text-black", children: t('bill_total_due') }), _jsx("span", { class: "text-2xl font-black text-sky-400 print:text-black", children: money(selectedInvoiceForPrint.totalCost) })] }), _jsxs("div", { class: "flex items-center justify-end gap-2 border-t border-slate-700/60 pt-4 mt-2 print:hidden", children: [_jsx("button", { onClick: () => setSelectedInvoiceForPrint(null), class: "py-2 px-4 rounded-lg bg-slate-700 text-slate-200 text-xs font-semibold hover:bg-slate-600 transition-colors", children: t('bill_close_btn') }), _jsxs("button", { onClick: () => window.print(), class: "py-2 px-4 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors", children: [_jsx("i", { class: "fas fa-print mr-1.5" }), t('bill_print_btn')] })] })] }) }), document.body)] }));
}
