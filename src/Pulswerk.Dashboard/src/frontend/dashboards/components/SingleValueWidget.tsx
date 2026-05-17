// Preact imports handled by jsxImportSource
import { useState, useEffect } from 'preact/hooks';
import { DashboardService } from '../api';

interface SingleValueWidgetProps {
    widgetId: string;
    keyName: string;
    allKeysMeta: any[];
}

export function SingleValueWidget({ widgetId, keyName, allKeysMeta }: SingleValueWidgetProps) {
    const [val, setVal] = useState<any>('---');

    const meta = allKeysMeta.find(k => k.key === keyName) || { key: keyName, name: keyName };
    const iconHtml = typeof (window as any).getPointIcon === 'function' 
        ? (window as any).getPointIcon(meta.type || '') 
        : '<i class="fas fa-microchip"></i>';

    const pp = meta.parentPath || [];
    
    const fetchData = async () => {
        if (!keyName) return;
        try {
            const data = await DashboardService.fetchLatestValues(keyName);
            const rawVal = data?.[keyName] || '---';
            const display = (window as any).PulswerkValue?.formatDisplay(rawVal, meta.type) || rawVal;
            setVal(display);
            
            if ((window as any).updateHistoryLiveValue) {
                (window as any).updateHistoryLiveValue(keyName, display);
            }
        } catch (e) {
            console.error('Failed to fetch single value', e);
        }
    };

    useEffect(() => {
        fetchData();
        const timer = setInterval(fetchData, 10000);
        return () => clearInterval(timer);
    }, [keyName]);

    if (!keyName) {
        return (
            <div class="empty-state" style={{ padding: '1rem' }}>
                <p style={{ fontSize: '0.8rem' }}>{(window as any).t ? (window as any).t('no_key') : 'No key configured'}</p>
            </div>
        );
    }

    const friendlyName = (window as any).friendlyName ? (window as any).friendlyName(keyName) : keyName;

    return (
        <div class="sv-card">
            <div class="sv-card-path">
                {pp.length > 0 ? pp.map((p: any, i: number) => (
                    <span key={p.id}>
                        <a href={`/plswk/Assets?node=${p.id}`} style={{ color: 'inherit', textDecoration: 'none' }}>
                            {p.name}
                        </a>
                        {i < pp.length - 1 && <i class="fas fa-chevron-right" style={{ margin: '0 0.4rem', fontSize: '0.55rem', opacity: 0.4 }}></i>}
                    </span>
                )) : <span style={{ opacity: 0.4 }}>—</span>}
            </div>
            <div class="sv-card-body">
                <div class="sv-card-icon" dangerouslySetInnerHTML={{ __html: iconHtml }}></div>
                <div class="sv-card-info">
                    <a href={`/plswk/Assets?node=${meta.parentId || ''}`} class="sv-card-name" style={{ textDecoration: 'none', color: '#fff', display: 'block' }}>
                        {meta.name || friendlyName}
                    </a>
                    <div class="sv-card-fullname">{meta.fullName || keyName}</div>
                </div>
                <div class="sv-card-valbox">
                    <span class="sv-card-val" id={`svv_${widgetId}`} data-key={keyName}>{val}</span>
                    <span class="sv-card-units">{meta.units || ''}</span>
                </div>
            </div>
            <div class="sv-card-actions">
                <button class="btn-icon" title="Trend" onClick={() => (window as any).openHistory(keyName)}><i class="fas fa-chart-area"></i></button>
                {meta.isWritable && (
                    <button class="btn-icon" title="Edit Value" onClick={() => (window as any).openEdit(keyName)}><i class="fas fa-pen"></i></button>
                )}
                <button class="btn-icon" title="Properties" onClick={() => (window as any).openProperties(keyName)}><i class="fas fa-cog"></i></button>
            </div>
        </div>
    );
}
