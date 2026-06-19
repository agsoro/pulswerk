// Preact imports handled by jsxImportSource

interface PointCardProps {
    point: any;
    variant?: 'index' | 'favorites';
}

export function PointCard({ point, variant = 'favorites' }: PointCardProps) {
    const iconHtml = typeof (window as any).getPointIcon === 'function' 
        ? (window as any).getPointIcon(point.type || '') 
        : '<i class="fas fa-microchip"></i>';

    const pathHtml = point.parentPath.map((p: any, index: number) => {
        return (
            <span key={p.id}>
                <a href={`/plswk/Assets?node=${p.id}`} class="text-sky-400 no-underline hover:underline">
                    {p.name}
                </a>
                {index < point.parentPath.length - 1 && (
                    <i class="fas fa-chevron-right mx-2 text-[0.7rem] opacity-50"></i>
                )}
            </span>
        );
    });

    const isSchedule = point.type === 'OBJECT_SCHEDULE';
    const displayValue = (window as any).PulswerkValue?.formatDisplay(point.value, point.type) || point.value;

    if (variant === 'index') {
        return (
            <div class="glass rounded-xl p-5 flex flex-col transition-all duration-300">
                <div class="text-sky-400 text-sm font-medium w-full border-b border-white/5 pb-2.5 mb-3 break-words leading-relaxed">
                    {pathHtml}
                </div>
                <div class="flex items-center gap-5 w-full">
                    <div class="w-[42px] h-[42px] rounded-[10px] bg-cyan-500/10 text-cyan-500 flex items-center justify-center text-lg shrink-0" dangerouslySetInnerHTML={{ __html: iconHtml }}></div>
                    <div class="flex-1 min-w-0">
                        <a href="#" onClick={(e) => { e.preventDefault(); (window as any).openTelemetryDetails(point.key); }} class="font-bold text-white text-lg no-underline block hover:text-sky-400">{point.name}</a>
                        <div class="text-[0.62rem] opacity-40 font-mono">{point.fullName}</div>
                    </div>
                    <div class="text-right min-w-[90px] cursor-pointer hover:opacity-85" onClick={() => (window as any).openTelemetryDetails(point.key)}>
                        {isSchedule ? (
                            <span class="text-sky-400/50 text-[0.65rem] font-black tracking-widest uppercase"><i class="fas fa-clock mr-1 opacity-70"></i>Schedule</span>
                        ) : (
                            <span class="text-xl font-bold text-sky-400 point-value" data-key={point.key}>{displayValue}</span>
                        )}
                        <span class="text-xs text-slate-400 ml-0.5">{point.units}</span>
                    </div>
                </div>
            </div>
        );
    }

    // Default 'favorites' variant
    return (
        <div class="glass rounded-xl p-6 flex flex-col transition-all duration-300">
            <div class="text-sky-400/60 text-[0.7rem] font-bold tracking-wide w-full border-b border-white/5 pb-3 mb-5 break-words leading-relaxed">
                {pathHtml}
            </div>
            <div class="flex items-start gap-4 w-full mb-6">
                <div class="w-12 h-12 rounded-xl bg-sky-400/10 text-sky-400 flex items-center justify-center text-xl shrink-0" dangerouslySetInnerHTML={{ __html: iconHtml }}></div>
                <div class="flex-1 min-w-0">
                    <a href="#" onClick={(e) => { e.preventDefault(); (window as any).openTelemetryDetails(point.key); }} class="font-bold text-white text-xl no-underline block hover:text-sky-400 truncate">{point.name}</a>
                    <div class="text-[0.65rem] opacity-30 font-mono mt-1 truncate">{point.fullName}</div>
                </div>
            </div>
            <div class="flex items-baseline justify-between mt-auto bg-white/[0.02] rounded-lg p-3 border border-white/5 cursor-pointer hover:bg-white/[0.05]" onClick={() => (window as any).openTelemetryDetails(point.key)}>
                {isSchedule ? (
                    <div class="text-sky-400/50 text-[0.65rem] font-black tracking-widest uppercase"><i class="fas fa-clock mr-1 opacity-70"></i>Schedule</div>
                ) : (
                    <div class="text-3xl font-black text-sky-400 tabular-nums point-value" data-key={point.key} data-type={point.type || ''}>{displayValue}</div>
                )}
                <div class="text-[0.7rem] font-bold text-slate-500 uppercase tracking-tighter">{point.units || ''}</div>
            </div>
        </div>
    );
}
