// Preact imports handled by jsxImportSource

interface DashCardProps {
    dashboard: {
        id: string;
        name: string;
        description?: string;
        widgets?: any[];
    };
}

function slugify(text: string): string {
    return text.toString().toLowerCase()
        .replace(/\s+/g, '-')           
        .replace(/[^\w\-]+/g, '')       
        .replace(/\-\-+/g, '-')         
        .replace(/^-+/, '')             
        .replace(/-+$/, '');            
}

export function DashCard({ dashboard }: DashCardProps) {
    const wc = dashboard.widgets?.length || 0;
    
    return (
        <div class="dash-card animate-scale-in" onClick={() => location.href = `/plswk/Dashboards/${dashboard.id}/${slugify(dashboard.name)}`}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.6rem', marginBottom: '0.75rem' }}>
                <div style={{ width: '36px', height: '36px', borderRadius: '10px', background: 'rgba(56,189,248,0.1)', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0 }}>
                    <i class="fas fa-th-large" style={{ color: '#38bdf8', fontSize: '1rem' }}></i>
                </div>
                <div style={{ flex: 1, minWidth: 0 }}>
                    <div class="dash-card-title">{dashboard.name}</div>
                </div>
            </div>
            <div class="dash-card-desc">{dashboard.description || 'No description'}</div>
            <div class="dash-card-meta">
                <span><i class="fas fa-puzzle-piece" style={{ marginRight: '0.3rem' }}></i>{wc} widget{wc !== 1 ? 's' : ''}</span>
            </div>
        </div>
    );
}
