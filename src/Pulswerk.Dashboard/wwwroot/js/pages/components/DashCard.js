import { jsx as _jsx, jsxs as _jsxs } from "preact/jsx-runtime";
function slugify(text) {
    return text.toString().toLowerCase()
        .replace(/\s+/g, '-')
        .replace(/[^\w\-]+/g, '')
        .replace(/\-\-+/g, '-')
        .replace(/^-+/, '')
        .replace(/-+$/, '');
}
export function DashCard({ dashboard }) {
    const wc = dashboard.widgets?.length || 0;
    return (_jsxs("div", { class: "dash-card animate-scale-in", onClick: () => location.href = `/plswk/Dashboards/${dashboard.id}/${slugify(dashboard.name)}`, children: [_jsxs("div", { style: { display: 'flex', alignItems: 'center', gap: '0.6rem', marginBottom: '0.75rem' }, children: [_jsx("div", { style: { width: '36px', height: '36px', borderRadius: '10px', background: 'rgba(56,189,248,0.1)', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0 }, children: _jsx("i", { class: "fas fa-th-large", style: { color: '#38bdf8', fontSize: '1rem' } }) }), _jsx("div", { style: { flex: 1, minWidth: 0 }, children: _jsx("div", { class: "dash-card-title", children: dashboard.name }) })] }), _jsx("div", { class: "dash-card-desc", children: dashboard.description || 'No description' }), _jsx("div", { class: "dash-card-meta", children: _jsxs("span", { children: [_jsx("i", { class: "fas fa-puzzle-piece", style: { marginRight: '0.3rem' } }), wc, " widget", wc !== 1 ? 's' : ''] }) })] }));
}
