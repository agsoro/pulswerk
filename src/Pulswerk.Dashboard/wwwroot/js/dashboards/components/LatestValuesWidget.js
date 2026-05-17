import { jsx as _jsx, jsxs as _jsxs } from "preact/jsx-runtime";
// Preact imports handled by jsxImportSource
import { useState, useEffect } from 'preact/hooks';
import { DashboardService } from '../api';
import { COLORS } from '../core';
export function MiniSpark({ val, color }) {
    const hSize = 16, w = 32;
    const barWidth = Math.min(w, Math.max(4, w * Math.abs(val % 100) / 100));
    return (_jsxs("svg", { width: w, height: hSize, style: { verticalAlign: 'middle' }, children: [_jsx("rect", { x: "0", y: hSize / 4, width: w, height: hSize / 2, rx: "2", fill: hexToRgba(color, 0.15) }), _jsx("rect", { x: "0", y: hSize / 4, width: barWidth, height: hSize / 2, rx: "2", fill: color, opacity: "0.6" })] }));
}
function hexToRgba(hex, a) {
    const r = parseInt(hex.slice(1, 3), 16), g = parseInt(hex.slice(3, 5), 16), b = parseInt(hex.slice(5, 7), 16);
    return `rgba(${r},${g},${b},${a})`;
}
export function LatestValuesWidget({ keys, allKeysMeta }) {
    const [data, setData] = useState({});
    const fetchData = async () => {
        if (!keys.length)
            return;
        try {
            const result = await DashboardService.fetchLatestValues(keys);
            setData(result);
            // Legacy interop to update the history modal live value
            keys.forEach(key => {
                const val = result?.[key] || '---';
                const meta = allKeysMeta.find(k => k.key === key) || {};
                const displayVal = window.PulswerkValue?.formatDisplay(val, meta.type) || val;
                if (window.updateHistoryLiveValue) {
                    window.updateHistoryLiveValue(key, displayVal);
                }
            });
        }
        catch (e) {
            console.error('Failed to fetch latest values', e);
        }
    };
    useEffect(() => {
        fetchData();
        const timer = setInterval(fetchData, 10000);
        return () => clearInterval(timer);
    }, [keys]);
    if (!keys.length) {
        return (_jsx("div", { class: "empty-state", style: { padding: '1rem' }, children: _jsx("p", { style: { fontSize: '0.8rem' }, children: "No keys configured" }) }));
    }
    const keyMetaMap = allKeysMeta.reduce((m, k) => { m[k.key] = k; return m; }, {});
    return (_jsx("div", { style: { overflow: 'auto', height: '100%' }, children: _jsxs("table", { class: "lv-table", children: [_jsx("thead", { children: _jsxs("tr", { children: [_jsx("th", { children: "Name" }), _jsx("th", { children: "Value" }), _jsx("th", { style: { width: '50px' }, children: "Trend" })] }) }), _jsx("tbody", { children: keys.map((key, i) => {
                        const val = data[key];
                        const displayVal = val !== undefined && val !== null ? val : '---';
                        const meta = keyMetaMap[key] || {};
                        const color = COLORS[i % COLORS.length];
                        const formattedVal = window.PulswerkValue?.formatDisplay(displayVal, meta.type) || displayVal;
                        const numVal = parseFloat(val);
                        const isNum = !isNaN(numVal);
                        return (_jsxs("tr", { children: [_jsxs("td", { children: [_jsx("span", { style: { display: 'inline-block', width: '8px', height: '8px', borderRadius: '50%', background: color, marginRight: '0.5rem' } }), window.friendlyName ? window.friendlyName(key) : key] }), _jsxs("td", { children: [_jsx("span", { class: "lv-value", "data-key": key, children: formattedVal }), _jsx("span", { class: "lv-units", children: meta.units || '' })] }), _jsx("td", { children: isNum && _jsx(MiniSpark, { val: numVal, color: color }) })] }, key));
                    }) })] }) }));
}
