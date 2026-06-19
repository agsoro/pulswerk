import { jsx as _jsx, jsxs as _jsxs } from "preact/jsx-runtime";
// Preact imports handled by jsxImportSource
import { useState, useEffect } from 'preact/hooks';
import { DashboardService } from '../api';
export function SingleValueWidget({ widgetId, keyName, allKeysMeta }) {
    const [val, setVal] = useState('---');
    const meta = allKeysMeta.find(k => k.key === keyName) || { key: keyName, name: keyName };
    const iconHtml = typeof window.getPointIcon === 'function'
        ? window.getPointIcon(meta.type || '')
        : '<i class="fas fa-microchip"></i>';
    const pp = meta.parentPath || [];
    const fetchData = async () => {
        if (!keyName)
            return;
        try {
            const data = await DashboardService.fetchLatestValues(keyName);
            const rawVal = data?.[keyName] || '---';
            const display = window.PulswerkValue?.formatDisplay(rawVal, meta.type) || rawVal;
            setVal(display);
            if (window.updateHistoryLiveValue) {
                window.updateHistoryLiveValue(keyName, display);
            }
        }
        catch (e) {
            console.error('Failed to fetch single value', e);
        }
    };
    useEffect(() => {
        fetchData();
        const unsubscribe = DashboardService.listenToLiveUpdates([keyName], (newData) => {
            if (newData[keyName] !== undefined) {
                const rawVal = newData[keyName] || '---';
                const display = window.PulswerkValue?.formatDisplay(rawVal, meta.type) || rawVal;
                setVal(display);
                if (window.updateHistoryLiveValue) {
                    window.updateHistoryLiveValue(keyName, display);
                }
            }
        });
        return unsubscribe;
    }, [keyName]);
    if (!keyName) {
        return (_jsx("div", { class: "empty-state", style: { padding: '1rem' }, children: _jsx("p", { style: { fontSize: '0.8rem' }, children: window.t ? window.t('no_key') : 'No key configured' }) }));
    }
    const friendlyName = window.friendlyName ? window.friendlyName(keyName) : keyName;
    return (_jsxs("div", { class: "sv-card cursor-pointer hover:border-sky-500/55 transition-all", onClick: () => window.openTelemetryDetails(keyName), children: [_jsx("div", { class: "sv-card-path", children: pp.length > 0 ? pp.map((p, i) => (_jsxs("span", { children: [_jsx("a", { href: `/plswk/Assets?node=${p.id}`, style: { color: 'inherit', textDecoration: 'none' }, onClick: (e) => e.stopPropagation(), children: p.name }), i < pp.length - 1 && _jsx("i", { class: "fas fa-chevron-right", style: { margin: '0 0.4rem', fontSize: '0.55rem', opacity: 0.4 } })] }, p.id))) : _jsx("span", { style: { opacity: 0.4 }, children: "\u2014" }) }), _jsxs("div", { class: "sv-card-body", children: [_jsx("div", { class: "sv-card-icon", dangerouslySetInnerHTML: { __html: iconHtml } }), _jsxs("div", { class: "sv-card-info", children: [_jsx("a", { href: `/plswk/Assets?node=${meta.parentId || ''}`, class: "sv-card-name", style: { textDecoration: 'none', color: '#fff', display: 'block' }, onClick: (e) => e.stopPropagation(), children: meta.name || friendlyName }), _jsx("div", { class: "sv-card-fullname", children: meta.fullName || keyName })] }), _jsxs("div", { class: "sv-card-valbox", children: [_jsx("span", { class: "sv-card-val", id: `svv_${widgetId}`, "data-key": keyName, children: val }), _jsx("span", { class: "sv-card-units", children: meta.units || '' })] })] })] }));
}
