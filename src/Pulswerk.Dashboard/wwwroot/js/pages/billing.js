import { jsx as _jsx, jsxs as _jsxs, Fragment as _Fragment } from "preact/jsx-runtime";
import { useState, useEffect } from 'preact/hooks';
import { t } from '../i18n';
export function BillingPage() {
    // Tabs: 'invoices', 'rfid', 'tenants', 'tariffs'
    const [activeTab, setActiveTab] = useState('invoices');
    // General state
    const [tariffs, setTariffs] = useState({ ratePerKwh: 0.30, baseMonthlyFee: 10.00 });
    const [rfids, setRfids] = useState([]);
    const [tenants, setTenants] = useState([]);
    const [loading, setLoading] = useState(true);
    // Invoices state
    const [selectedYear, setSelectedYear] = useState(new Date().getFullYear());
    const [selectedMonth, setSelectedMonth] = useState(new Date().getMonth() + 1);
    const [invoices, setInvoices] = useState([]);
    const [invoiceLoading, setInvoiceLoading] = useState(false);
    const [selectedInvoiceForPrint, setSelectedInvoiceForPrint] = useState(null);
    // Add forms state
    const [newRfid, setNewRfid] = useState({ idTag: '', userName: '' });
    const [newTenant, setNewTenant] = useState({ id: '', name: '', meterKey: '' });
    // Inline editing state
    const [editingRfidTag, setEditingRfidTag] = useState(null);
    const [editingRfidName, setEditingRfidName] = useState('');
    const [editingTenantId, setEditingTenantId] = useState(null);
    const [editingTenantName, setEditingTenantName] = useState('');
    const [editingTenantMeterKey, setEditingTenantMeterKey] = useState('');
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
        if (!confirm('Remove this meter replacement event?'))
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
    const handleUpdateTariffs = async (e) => {
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
        }
        catch (e) {
            console.error(e);
        }
    };
    const handleAddRfid = async (e) => {
        e.preventDefault();
        if (!newRfid.idTag || !newRfid.userName)
            return;
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
        }
        catch (e) {
            console.error(e);
        }
    };
    const handleDeleteRfid = async (idTag) => {
        if (!confirm(`Remove RFID card ${idTag}?`))
            return;
        try {
            const res = await fetch(`/plswk/api/billing/rfid/${encodeURIComponent(idTag)}`, {
                method: 'DELETE'
            });
            if (res.ok)
                fetchRfids();
        }
        catch (e) {
            console.error(e);
        }
    };
    const handleAddTenant = async (e) => {
        e.preventDefault();
        if (!newTenant.id || !newTenant.name || !newTenant.meterKey)
            return;
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
        }
        catch (e) {
            console.error(e);
        }
    };
    const handleDeleteTenant = async (id) => {
        if (!confirm(`Remove Tenant ${id}?`))
            return;
        try {
            const res = await fetch(`/plswk/api/billing/tenants/${encodeURIComponent(id)}`, {
                method: 'DELETE'
            });
            if (res.ok)
                fetchTenants();
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
                body: JSON.stringify({ idTag, userName: editingRfidName })
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
    const handleSaveTenantEdit = async (id) => {
        if (!editingTenantName.trim() || !editingTenantMeterKey.trim())
            return;
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
        }
        catch (e) {
            console.error(e);
        }
    };
    const printInvoice = () => {
        window.print();
    };
    if (loading) {
        return (_jsx("div", { class: "h-full flex items-center justify-center p-16", children: _jsxs("div", { class: "flex flex-col items-center gap-4 text-cyan-400", children: [_jsx("i", { class: "fas fa-spinner fa-spin text-3xl" }), _jsx("p", { class: "text-sm font-medium animate-pulse", children: t('loading') })] }) }));
    }
    return (_jsxs("div", { class: "flex flex-col gap-6 w-full page-enter print:p-0", children: [_jsxs("div", { class: "flex border-b border-slate-700/60 pb-px gap-2 shrink-0 overflow-x-auto print:hidden", children: [_jsxs("button", { onClick: () => setActiveTab('invoices'), class: `py-2.5 px-4 text-sm font-semibold rounded-t-lg border-b-2 transition-all ${activeTab === 'invoices' ? 'text-sky-400 border-sky-400 bg-sky-400/5' : 'text-slate-400 border-transparent hover:text-slate-200'}`, children: [_jsx("i", { class: "fas fa-file-invoice-dollar mr-2" }), t('bill_monthly_invoices')] }), _jsxs("button", { onClick: () => setActiveTab('rfids'), class: `py-2.5 px-4 text-sm font-semibold rounded-t-lg border-b-2 transition-all ${activeTab === 'rfids' ? 'text-sky-400 border-sky-400 bg-sky-400/5' : 'text-slate-400 border-transparent hover:text-slate-200'}`, children: [_jsx("i", { class: "fas fa-id-card mr-2" }), t('bill_rfid_cards')] }), _jsxs("button", { onClick: () => setActiveTab('tenants'), class: `py-2.5 px-4 text-sm font-semibold rounded-t-lg border-b-2 transition-all ${activeTab === 'tenants' ? 'text-sky-400 border-sky-400 bg-sky-400/5' : 'text-slate-400 border-transparent hover:text-slate-200'}`, children: [_jsx("i", { class: "fas fa-users mr-2" }), t('bill_metered_tenants')] }), _jsxs("button", { onClick: () => setActiveTab('tariffs'), class: `py-2.5 px-4 text-sm font-semibold rounded-t-lg border-b-2 transition-all ${activeTab === 'tariffs' ? 'text-sky-400 border-sky-400 bg-sky-400/5' : 'text-slate-400 border-transparent hover:text-slate-200'}`, children: [_jsx("i", { class: "fas fa-sliders-h mr-2" }), t('bill_tariff_pricing')] })] }), activeTab === 'invoices' && (_jsxs("div", { class: "flex flex-col gap-4", children: [_jsxs("div", { class: "glass p-5 rounded-xl border border-slate-700/60 flex flex-wrap items-center justify-between gap-4 print:hidden", children: [_jsx("div", { class: "flex items-center gap-3", children: _jsxs("div", { children: [_jsx("label", { class: "text-[0.65rem] font-bold text-slate-400 uppercase tracking-wider block mb-1", children: t('bill_billing_period') }), _jsxs("div", { class: "flex items-center gap-2", children: [_jsx("select", { value: selectedYear, onChange: (e) => setSelectedYear(parseInt(e.target.value)), class: "bg-slate-900 border border-slate-700 rounded-lg px-3 py-1.5 text-sm text-slate-100 focus:outline-none focus:border-sky-400 font-semibold", children: [2024, 2025, 2026, 2027].map(y => _jsx("option", { value: y, children: y }, y)) }), _jsx("select", { value: selectedMonth, onChange: (e) => setSelectedMonth(parseInt(e.target.value)), class: "bg-slate-900 border border-slate-700 rounded-lg px-3 py-1.5 text-sm text-slate-100 focus:outline-none focus:border-sky-400 font-semibold", children: Array.from({ length: 12 }, (_, i) => i + 1).map(m => (_jsx("option", { value: m, children: new Date(2000, m - 1).toLocaleString('default', { month: 'long' }) }, m))) })] })] }) }), _jsxs("div", { class: "text-right", children: [_jsxs("span", { class: "text-xs text-slate-400 font-medium", children: [t('bill_standard_tariff'), ": "] }), _jsxs("span", { class: "text-sm font-bold text-sky-400", children: [tariffs.ratePerKwh.toFixed(2), " \u20AC/kWh \u00A0\u00B7\u00A0 ", tariffs.baseMonthlyFee.toFixed(2), " \u20AC ", t('bill_base')] })] })] }), _jsxs("div", { class: "bg-slate-800 border border-slate-700 rounded-xl overflow-hidden print:border-none print:bg-transparent", children: [_jsxs("div", { class: "px-6 py-4 border-b border-slate-700 bg-black/15 flex justify-between items-center print:hidden", children: [_jsx("h3", { class: "font-bold text-slate-50 text-base", children: t('bill_summary') }), _jsxs("span", { class: "text-xs text-slate-400 font-medium", children: [invoices.length, " ", t('bill_accounts_found')] })] }), invoiceLoading ? (_jsxs("div", { class: "p-16 text-center text-cyan-400", children: [_jsx("i", { class: "fas fa-spinner fa-spin text-2xl mr-2" }), t('loading')] })) : invoices.length === 0 ? (_jsx("div", { class: "p-16 text-center text-slate-500 font-medium", children: t('bill_no_records') })) : (_jsx("div", { class: "overflow-x-auto", children: _jsxs("table", { class: "lv-table text-sm", children: [_jsx("thead", { children: _jsxs("tr", { children: [_jsx("th", { children: t('bill_acc_name') }), _jsx("th", { children: t('bill_type') }), _jsx("th", { children: t('bill_details') }), _jsx("th", { class: "text-right", children: t('bill_consumption') }), _jsx("th", { class: "text-right", children: t('bill_energy_cost') }), _jsx("th", { class: "text-right", children: t('bill_total_cost') }), _jsx("th", { class: "text-center print:hidden", children: t('bill_action') })] }) }), _jsx("tbody", { children: invoices.map((inv, idx) => (_jsxs("tr", { children: [_jsx("td", { class: "font-bold text-slate-200", children: inv.userName }), _jsx("td", { children: _jsxs("div", { class: "flex items-center gap-1.5 flex-wrap", children: [_jsx("span", { class: `inline-block text-[0.68rem] font-bold px-2 py-0.5 rounded-full ${inv.type === 'EV Charging' ? 'bg-amber-400/10 text-amber-400' : 'bg-sky-400/10 text-sky-400'}`, children: inv.type === 'EV Charging' ? t('bill_ev_charging') : t('bill_tenant_meter') }), (inv.replacementCount ?? 0) > 0 && (_jsxs("span", { class: "inline-flex items-center gap-1 text-[0.65rem] font-bold px-1.5 py-0.5 rounded-full bg-orange-500/15 text-orange-400", title: `Meter replaced ${inv.replacementCount}× during this billing period — consumption split across segments`, children: [_jsx("i", { class: "fas fa-exchange-alt" }), inv.replacementCount, "\u00D7 replaced"] }))] }) }), _jsx("td", { class: "text-slate-400 font-mono text-xs", children: inv.details }), _jsxs("td", { class: "text-right font-mono text-slate-100 font-semibold", children: [inv.totalKwh.toFixed(2), " kWh"] }), _jsxs("td", { class: "text-right font-mono text-slate-300", children: [inv.energyCost.toFixed(2), " \u20AC"] }), _jsxs("td", { class: "text-right font-mono text-sky-400 font-bold", children: [inv.totalCost.toFixed(2), " \u20AC"] }), _jsx("td", { class: "text-center print:hidden", children: _jsxs("button", { onClick: () => setSelectedInvoiceForPrint(inv), class: "py-1 px-3 rounded bg-slate-700 text-slate-200 text-xs font-semibold hover:bg-slate-600 transition-colors", children: [_jsx("i", { class: "fas fa-file-invoice mr-1.5" }), t('bill_details')] }) })] }, idx))) })] }) }))] })] })), activeTab === 'rfids' && (_jsxs("div", { class: "grid grid-cols-1 lg:grid-cols-3 gap-6", children: [_jsxs("div", { class: "glass p-6 rounded-xl border border-slate-700/60 h-fit", children: [_jsx("h3", { class: "font-bold text-slate-50 text-base mb-4 border-b border-slate-700 pb-2", children: t('bill_reg_rfid') }), _jsxs("form", { onSubmit: handleAddRfid, class: "flex flex-col gap-4", children: [_jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide", children: t('bill_rfid_token') }), _jsx("input", { type: "text", required: true, value: newRfid.idTag, onInput: (e) => setNewRfid({ ...newRfid, idTag: e.target.value }), placeholder: "e.g. 04A1B2C3", class: "form-input font-mono" })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide", children: t('bill_owner_name') }), _jsx("input", { type: "text", required: true, value: newRfid.userName, onInput: (e) => setNewRfid({ ...newRfid, userName: e.target.value }), placeholder: "e.g. John Doe", class: "form-input" })] }), _jsxs("button", { type: "submit", class: "py-2 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors mt-2", children: [_jsx("i", { class: "fas fa-plus mr-1.5" }), t('bill_add_mapping')] })] })] }), _jsxs("div", { class: "bg-slate-800 border border-slate-700 rounded-xl overflow-hidden lg:col-span-2", children: [_jsx("div", { class: "px-6 py-4 border-b border-slate-700 bg-black/15", children: _jsx("h3", { class: "font-bold text-slate-50 text-base", children: t('bill_registered_cards') }) }), _jsx("div", { class: "max-h-[500px] overflow-y-auto", children: rfids.length === 0 ? (_jsx("div", { class: "p-16 text-center text-slate-500", children: "No RFID cards registered yet. Unrecognized cards will be rejected by chargers." })) : (_jsxs("table", { class: "lv-table text-sm", children: [_jsx("thead", { children: _jsxs("tr", { children: [_jsx("th", { children: t('bill_token_id') }), _jsx("th", { children: t('bill_user_name') }), _jsx("th", { class: "text-right", children: t('bill_actions') })] }) }), _jsx("tbody", { children: rfids.map((r) => {
                                                const isEditing = r.idTag === editingRfidTag;
                                                return (_jsxs("tr", { children: [_jsx("td", { class: "font-mono font-bold text-sky-400", children: r.idTag }), _jsx("td", { children: isEditing ? (_jsx("input", { type: "text", value: editingRfidName, onInput: (e) => setEditingRfidName(e.target.value), class: "form-input max-w-[200px]" })) : (r.userName) }), _jsx("td", { class: "text-right flex justify-end gap-1.5", children: isEditing ? (_jsxs(_Fragment, { children: [_jsx("button", { onClick: () => handleSaveRfidEdit(r.idTag), class: "p-1 px-2.5 rounded bg-emerald-500/20 text-emerald-400 hover:bg-emerald-500/30 text-xs font-semibold transition-colors", children: _jsx("i", { class: "fas fa-check" }) }), _jsx("button", { onClick: () => setEditingRfidTag(null), class: "p-1 px-2.5 rounded bg-slate-700/40 text-slate-300 hover:bg-slate-700/60 text-xs font-semibold transition-colors", children: _jsx("i", { class: "fas fa-times" }) })] })) : (_jsxs(_Fragment, { children: [_jsx("button", { onClick: () => {
                                                                            setEditingRfidTag(r.idTag);
                                                                            setEditingRfidName(r.userName);
                                                                        }, class: "p-1 px-2.5 rounded bg-sky-500/10 text-sky-400 hover:bg-sky-500/20 text-xs font-semibold transition-colors", children: _jsx("i", { class: "fas fa-edit" }) }), _jsx("button", { onClick: () => handleDeleteRfid(r.idTag), class: "p-1 px-2.5 rounded bg-red-500/10 text-red-400 hover:bg-red-500/20 text-xs font-semibold transition-colors", children: _jsx("i", { class: "fas fa-trash-alt" }) })] })) })] }, r.idTag));
                                            }) })] })) })] })] })), activeTab === 'tenants' && (_jsxs("div", { class: "grid grid-cols-1 lg:grid-cols-3 gap-6", children: [_jsxs("div", { class: "glass p-6 rounded-xl border border-slate-700/60 h-fit", children: [_jsx("h3", { class: "font-bold text-slate-50 text-base mb-4 border-b border-slate-700 pb-2", children: t('bill_add_tenant') }), _jsxs("form", { onSubmit: handleAddTenant, class: "flex flex-col gap-4", children: [_jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide", children: t('bill_tenant_id') }), _jsx("input", { type: "text", required: true, value: newTenant.id, onInput: (e) => setNewTenant({ ...newTenant, id: e.target.value }), placeholder: "e.g. tenant_101", class: "form-input font-mono" })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide", children: t('bill_display_name') }), _jsx("input", { type: "text", required: true, value: newTenant.name, onInput: (e) => setNewTenant({ ...newTenant, name: e.target.value }), placeholder: "e.g. Apartment 3B (Miller)", class: "form-input" })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide", children: t('bill_meter_key') }), _jsx("input", { type: "text", required: true, value: newTenant.meterKey, onInput: (e) => setNewTenant({ ...newTenant, meterKey: e.target.value }), placeholder: "e.g. dev-meter1_energy-active-kwh", class: "form-input font-mono" })] }), _jsxs("button", { type: "submit", class: "py-2 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors mt-2", children: [_jsx("i", { class: "fas fa-plus mr-1.5" }), t('bill_add_tenant_btn')] })] })] }), _jsxs("div", { class: "bg-slate-800 border border-slate-700 rounded-xl overflow-hidden lg:col-span-2", children: [_jsx("div", { class: "px-6 py-4 border-b border-slate-700 bg-black/15", children: _jsx("h3", { class: "font-bold text-slate-50 text-base", children: t('bill_metered_tenants_title') }) }), _jsx("div", { class: "max-h-[500px] overflow-y-auto", children: tenants.length === 0 ? (_jsx("div", { class: "p-16 text-center text-slate-500", children: "No metered tenants configured. Monthly consumption query from InfluxDB is disabled." })) : (_jsxs("table", { class: "lv-table text-sm", children: [_jsx("thead", { children: _jsxs("tr", { children: [_jsx("th", { children: t('bill_tenant_id') }), _jsx("th", { children: t('bill_display_name') }), _jsx("th", { children: t('bill_influx_key') }), _jsx("th", { class: "text-right", children: t('bill_actions') })] }) }), _jsx("tbody", { children: tenants.map((ten) => {
                                                const isEditing = ten.id === editingTenantId;
                                                const isExpanded = expandedReplacementTenantId === ten.id;
                                                const tenantReplacements = replacements[ten.id] ?? [];
                                                return (_jsxs(_Fragment, { children: [_jsxs("tr", { children: [_jsx("td", { class: "font-mono text-slate-300", children: ten.id }), _jsx("td", { class: "text-slate-100 font-bold", children: isEditing ? (_jsx("input", { type: "text", value: editingTenantName, onInput: (e) => setEditingTenantName(e.target.value), class: "form-input w-full" })) : (ten.name) }), _jsx("td", { class: "text-sky-400 font-mono text-xs", children: isEditing ? (_jsx("input", { type: "text", value: editingTenantMeterKey, onInput: (e) => setEditingTenantMeterKey(e.target.value), class: "form-input w-full font-mono" })) : (ten.meterKey) }), _jsxs("td", { class: "text-right flex justify-end gap-1.5", children: [_jsx("button", { title: "Meter replacements", onClick: () => {
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
                                                                                    }, class: "p-1 px-2.5 rounded bg-sky-500/10 text-sky-400 hover:bg-sky-500/20 text-xs font-semibold transition-colors", children: _jsx("i", { class: "fas fa-edit" }) }), _jsx("button", { onClick: () => handleDeleteTenant(ten.id), class: "p-1 px-2.5 rounded bg-red-500/10 text-red-400 hover:bg-red-500/20 text-xs font-semibold transition-colors", children: _jsx("i", { class: "fas fa-trash-alt" }) })] }))] })] }, ten.id), isExpanded && (_jsx("tr", { children: _jsx("td", { colspan: 4, class: "p-0", children: _jsxs("div", { class: "mx-4 mb-3 border border-orange-500/20 rounded-xl bg-orange-500/[0.03] overflow-hidden", children: [_jsxs("div", { class: "px-4 py-2.5 bg-orange-500/10 border-b border-orange-500/15 flex items-center justify-between", children: [_jsxs("span", { class: "text-[0.72rem] font-bold text-orange-400 uppercase tracking-wider", children: [_jsx("i", { class: "fas fa-exchange-alt mr-1.5" }), "Meter Replacement Events \u2014 ", ten.name] }), _jsxs("span", { class: "text-[0.65rem] text-orange-400/60", children: [tenantReplacements.length, " recorded"] })] }), tenantReplacements.length > 0 ? (_jsxs("table", { class: "w-full text-xs", children: [_jsx("thead", { children: _jsxs("tr", { class: "text-[0.62rem] text-slate-500 uppercase tracking-wider border-b border-white/5", children: [_jsx("th", { class: "px-4 py-2 text-left", children: "New Meter Start" }), _jsx("th", { class: "px-4 py-2 text-right", children: "Old Final (kWh)" }), _jsx("th", { class: "px-4 py-2 text-right", children: "New Start (kWh)" }), _jsx("th", { class: "px-4 py-2 text-left", children: "Note" }), _jsx("th", { class: "px-4 py-2" })] }) }), _jsx("tbody", { children: tenantReplacements.map(r => (_jsxs("tr", { class: "border-b border-white/5 hover:bg-white/[0.02]", children: [_jsx("td", { class: "px-4 py-2 font-mono text-slate-300", children: new Date(r.replacedAt).toLocaleString() }), _jsx("td", { class: "px-4 py-2 text-right font-mono text-slate-400", children: r.oldFinalKwh != null ? r.oldFinalKwh.toFixed(2) : '–' }), _jsx("td", { class: "px-4 py-2 text-right font-mono text-slate-400", children: r.newStartKwh != null ? r.newStartKwh.toFixed(2) : '–' }), _jsx("td", { class: "px-4 py-2 text-slate-500 italic", children: r.note || '–' }), _jsx("td", { class: "px-4 py-2 text-right", children: _jsx("button", { onClick: () => handleDeleteReplacement(ten.id, r.id), class: "p-1 px-2 rounded bg-red-500/10 text-red-400 hover:bg-red-500/20 text-xs font-semibold transition-colors", title: "Remove event", children: _jsx("i", { class: "fas fa-trash-alt" }) }) })] }, r.id))) })] })) : (_jsx("div", { class: "px-4 py-3 text-xs text-slate-600 italic", children: "No replacement events recorded for this tenant." })), _jsxs("form", { onSubmit: (e) => handleAddReplacement(ten.id, e), class: "px-4 py-3 border-t border-orange-500/10 flex flex-wrap items-end gap-3", children: [_jsxs("div", { class: "flex flex-col gap-1", children: [_jsx("label", { class: "text-[0.62rem] font-bold text-slate-500 uppercase tracking-wide", children: "New meter start *" }), _jsx("input", { type: "datetime-local", required: true, value: newReplacement.replacedAt, onInput: (e) => setNewReplacement(p => ({ ...p, replacedAt: e.target.value })), class: "form-input text-xs py-1.5" })] }), _jsxs("div", { class: "flex flex-col gap-1", children: [_jsx("label", { class: "text-[0.62rem] font-bold text-slate-500 uppercase tracking-wide", children: "Old final kWh" }), _jsx("input", { type: "number", step: "0.001", min: "0", value: newReplacement.oldFinalKwh, onInput: (e) => setNewReplacement(p => ({ ...p, oldFinalKwh: e.target.value })), placeholder: "e.g. 98751.2", class: "form-input text-xs py-1.5 font-mono w-32" })] }), _jsxs("div", { class: "flex flex-col gap-1", children: [_jsx("label", { class: "text-[0.62rem] font-bold text-slate-500 uppercase tracking-wide", children: "New start kWh" }), _jsx("input", { type: "number", step: "0.001", min: "0", value: newReplacement.newStartKwh, onInput: (e) => setNewReplacement(p => ({ ...p, newStartKwh: e.target.value })), placeholder: "e.g. 0.0", class: "form-input text-xs py-1.5 font-mono w-32" })] }), _jsxs("div", { class: "flex flex-col gap-1 flex-1 min-w-[160px]", children: [_jsx("label", { class: "text-[0.62rem] font-bold text-slate-500 uppercase tracking-wide", children: "Note" }), _jsx("input", { type: "text", value: newReplacement.note, onInput: (e) => setNewReplacement(p => ({ ...p, note: e.target.value })), placeholder: "e.g. Serial #OLD \u2192 #NEW", class: "form-input text-xs py-1.5" })] }), _jsxs("button", { type: "submit", class: "py-1.5 px-4 rounded-lg bg-orange-500/20 text-orange-400 hover:bg-orange-500/30 text-xs font-bold transition-colors shrink-0", children: [_jsx("i", { class: "fas fa-plus mr-1" }), "Record"] })] })] }) }) }, `${ten.id}-replacements`))] }));
                                            }) })] })) })] })] })), activeTab === 'tariffs' && (_jsxs("div", { class: "glass p-6 rounded-xl border border-slate-700/60 max-w-lg", children: [_jsx("h3", { class: "font-bold text-slate-50 text-base mb-4 border-b border-slate-700 pb-2", children: t('bill_tariffs_settings') }), _jsxs("form", { onSubmit: handleUpdateTariffs, class: "flex flex-col gap-5", children: [_jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide", children: t('bill_rate_kwh') }), _jsxs("div", { class: "relative", children: [_jsx("span", { class: "absolute left-3 top-2 text-slate-400 text-sm font-semibold", children: "\u20AC" }), _jsx("input", { type: "number", step: "0.001", required: true, value: tariffs.ratePerKwh, onInput: (e) => setTariffs({ ...tariffs, ratePerKwh: parseFloat(e.target.value) }), class: "form-input pl-8 font-mono" })] }), _jsx("span", { class: "text-[0.68rem] text-slate-500", children: t('bill_rate_desc') })] }), _jsxs("div", { class: "flex flex-col gap-1.5", children: [_jsx("label", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wide", children: t('bill_base_fee') }), _jsxs("div", { class: "relative", children: [_jsx("span", { class: "absolute left-3 top-2 text-slate-400 text-sm font-semibold", children: "\u20AC" }), _jsx("input", { type: "number", step: "0.01", required: true, value: tariffs.baseMonthlyFee, onInput: (e) => setTariffs({ ...tariffs, baseMonthlyFee: parseFloat(e.target.value) }), class: "form-input pl-8 font-mono" })] }), _jsx("span", { class: "text-[0.68rem] text-slate-500", children: t('bill_base_desc') })] }), _jsxs("button", { type: "submit", class: "py-2.5 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors mt-2", children: [_jsx("i", { class: "fas fa-check mr-1.5" }), t('bill_apply_model')] })] })] })), selectedInvoiceForPrint !== null && (_jsx("div", { class: "fixed inset-0 bg-black/75 backdrop-blur-sm flex items-center justify-center z-[100] p-4 print:static print:bg-transparent print:p-0 print:block", children: _jsxs("div", { class: "bg-slate-800 border border-slate-700 rounded-xl shadow-2xl max-w-2xl w-full p-8 text-slate-100 flex flex-col gap-6 print:border-none print:bg-transparent print:p-0 print:text-black", children: [_jsxs("div", { class: "flex justify-between items-start border-b border-slate-700/60 pb-5 print:border-black print:pb-2", children: [_jsxs("div", { children: [_jsx("h2", { class: "text-xl font-extrabold text-slate-50 print:text-black print:text-2xl", children: t('bill_invoice_header') }), _jsxs("p", { class: "text-xs text-slate-400 mt-1 print:text-black font-mono", children: [t('bill_invoice_date'), ": ", new Date().toLocaleDateString(), " \u00A0\u00B7\u00A0 ", t('bill_invoice_period'), ": ", selectedInvoiceForPrint.billingPeriod] })] }), _jsx("button", { onClick: () => setSelectedInvoiceForPrint(null), class: "text-slate-400 hover:text-slate-200 print:hidden", children: _jsx("i", { class: "fas fa-times text-xl" }) })] }), _jsxs("div", { class: "grid grid-cols-2 gap-4 bg-black/10 p-4 rounded-lg border border-slate-700/40 print:bg-transparent print:border-none print:p-0", children: [_jsxs("div", { children: [_jsx("span", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wider block print:text-black", children: t('bill_billed_to') }), _jsx("strong", { class: "text-slate-100 text-base mt-1 block print:text-black", children: selectedInvoiceForPrint.userName }), _jsx("span", { class: "text-xs text-slate-400 font-mono print:text-black", children: selectedInvoiceForPrint.details })] }), _jsxs("div", { class: "text-right", children: [_jsx("span", { class: "text-[0.68rem] font-bold text-slate-400 uppercase tracking-wider block print:text-black", children: t('bill_acc_type') }), _jsx("span", { class: "text-sm font-semibold text-slate-200 mt-1 block print:text-black", children: selectedInvoiceForPrint.type === 'EV Charging' ? t('bill_ev_charging') : t('bill_tenant_meter') })] })] }), _jsxs("table", { class: "w-full border-collapse text-sm text-slate-200 print:text-black", children: [_jsx("thead", { children: _jsxs("tr", { class: "text-left text-[0.68rem] font-bold uppercase tracking-wider text-slate-400 border-b border-slate-700 print:border-black print:text-black", children: [_jsx("th", { class: "py-2", children: t('bill_desc') }), _jsx("th", { class: "py-2 text-right", children: t('bill_quantity') }), _jsx("th", { class: "py-2 text-right", children: t('bill_unit_price') }), _jsx("th", { class: "py-2 text-right", children: t('bill_total') })] }) }), _jsxs("tbody", { children: [_jsxs("tr", { class: "border-b border-slate-700/30 print:border-gray-300", children: [_jsxs("td", { class: "py-3", children: [_jsx("div", { class: "font-semibold print:text-black", children: t('bill_energy_consumption') }), _jsx("div", { class: "text-[0.72rem] text-slate-400 print:text-black", children: t('bill_metered_active') })] }), _jsxs("td", { class: "py-3 text-right font-mono", children: [selectedInvoiceForPrint.totalKwh.toFixed(2), " kWh"] }), _jsxs("td", { class: "py-3 text-right font-mono", children: [selectedInvoiceForPrint.ratePerKwh.toFixed(2), " \u20AC"] }), _jsxs("td", { class: "py-3 text-right font-mono font-semibold", children: [selectedInvoiceForPrint.energyCost.toFixed(2), " \u20AC"] })] }), _jsxs("tr", { class: "border-b border-slate-700/30 print:border-gray-300", children: [_jsxs("td", { class: "py-3", children: [_jsx("div", { class: "font-semibold print:text-black", children: t('bill_base_service_fee') }), _jsx("div", { class: "text-[0.72rem] text-slate-400 print:text-black", children: t('bill_grid_flatrate') })] }), _jsx("td", { class: "py-3 text-right font-mono", children: "1 month" }), _jsxs("td", { class: "py-3 text-right font-mono", children: [selectedInvoiceForPrint.baseFee.toFixed(2), " \u20AC"] }), _jsxs("td", { class: "py-3 text-right font-mono font-semibold", children: [selectedInvoiceForPrint.baseFee.toFixed(2), " \u20AC"] })] })] })] }), _jsxs("div", { class: "flex justify-end items-center gap-6 mt-2 border-t border-slate-700/60 pt-4 print:border-black", children: [_jsx("span", { class: "text-sm font-bold text-slate-400 print:text-black", children: t('bill_total_due') }), _jsxs("span", { class: "text-2xl font-black text-sky-400 print:text-black", children: [selectedInvoiceForPrint.totalCost.toFixed(2), " \u20AC"] })] }), _jsxs("div", { class: "flex items-center justify-end gap-2 border-t border-slate-700/60 pt-4 mt-2 print:hidden", children: [_jsx("button", { onClick: () => setSelectedInvoiceForPrint(null), class: "py-2 px-4 rounded-lg bg-slate-700 text-slate-200 text-xs font-semibold hover:bg-slate-600 transition-colors", children: t('bill_close_btn') }), _jsxs("button", { onClick: printInvoice, class: "py-2 px-4 rounded-lg bg-sky-500 text-slate-950 text-xs font-bold hover:bg-sky-400 transition-colors", children: [_jsx("i", { class: "fas fa-print mr-1.5" }), t('bill_print_btn')] })] })] }) }))] }));
}
