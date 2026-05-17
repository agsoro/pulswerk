// Preact imports handled by jsxImportSource
import { useState, useEffect } from 'preact/hooks';
import { DashboardService } from '../api';
import { COLORS } from '../core';

interface LatestValuesWidgetProps {
    keys: string[];
    allKeysMeta: any[];
}

export function MiniSpark({ val, color }: { val: number, color: string }) {
    const hSize = 16, w = 32;
    const barWidth = Math.min(w, Math.max(4, w * Math.abs(val % 100) / 100));
    return (
        <svg width={w} height={hSize} style={{ verticalAlign: 'middle' }}>
            <rect x="0" y={hSize / 4} width={w} height={hSize / 2} rx="2" fill={hexToRgba(color, 0.15)} />
            <rect x="0" y={hSize / 4} width={barWidth} height={hSize / 2} rx="2" fill={color} opacity="0.6" />
        </svg>
    );
}

function hexToRgba(hex: string, a: number): string { 
    const r = parseInt(hex.slice(1, 3), 16), g = parseInt(hex.slice(3, 5), 16), b = parseInt(hex.slice(5, 7), 16); 
    return `rgba(${r},${g},${b},${a})`; 
}

export function LatestValuesWidget({ keys, allKeysMeta }: LatestValuesWidgetProps) {
    const [data, setData] = useState<any>({});

    const fetchData = async () => {
        if (!keys.length) return;
        try {
            const result = await DashboardService.fetchLatestValues(keys);
            setData(result);
            
            // Legacy interop to update the history modal live value
            keys.forEach(key => {
                const val = result?.[key] || '---';
                const meta = allKeysMeta.find(k => k.key === key) || {};
                const displayVal = (window as any).PulswerkValue?.formatDisplay(val, meta.type) || val;
                if ((window as any).updateHistoryLiveValue) {
                    (window as any).updateHistoryLiveValue(key, displayVal);
                }
            });
        } catch (e) {
            console.error('Failed to fetch latest values', e);
        }
    };

    useEffect(() => {
        fetchData();
        const timer = setInterval(fetchData, 10000);
        return () => clearInterval(timer);
    }, [keys]);

    if (!keys.length) {
        return (
            <div class="empty-state" style={{ padding: '1rem' }}>
                <p style={{ fontSize: '0.8rem' }}>No keys configured</p>
            </div>
        );
    }

    const keyMetaMap = allKeysMeta.reduce((m: any, k: any) => { m[k.key] = k; return m; }, {});

    return (
        <div style={{ overflow: 'auto', height: '100%' }}>
            <table class="lv-table">
                <thead>
                    <tr>
                        <th>Name</th>
                        <th>Value</th>
                        <th style={{ width: '50px' }}>Trend</th>
                    </tr>
                </thead>
                <tbody>
                    {keys.map((key, i) => {
                        const val = data[key];
                        const displayVal = val !== undefined && val !== null ? val : '---';
                        const meta = keyMetaMap[key] || {};
                        const color = COLORS[i % COLORS.length];
                        const formattedVal = (window as any).PulswerkValue?.formatDisplay(displayVal, meta.type) || displayVal;
                        
                        const numVal = parseFloat(val);
                        const isNum = !isNaN(numVal);

                        return (
                            <tr key={key}>
                                <td>
                                    <span style={{ display: 'inline-block', width: '8px', height: '8px', borderRadius: '50%', background: color, marginRight: '0.5rem' }}></span>
                                    {(window as any).friendlyName ? (window as any).friendlyName(key) : key}
                                </td>
                                <td>
                                    <span class="lv-value" data-key={key}>{formattedVal}</span>
                                    <span class="lv-units">{meta.units || ''}</span>
                                </td>
                                <td>
                                    {isNum && <MiniSpark val={numVal} color={color} />}
                                </td>
                            </tr>
                        );
                    })}
                </tbody>
            </table>
        </div>
    );
}
