import { useState, useEffect } from 'preact/hooks';
import { DashCard } from './components/DashCard';
import { PointCard } from './components/PointCard';
import { DashboardService } from '../dashboards/api';
import { t } from '../i18n';

export function FavoritesPage() {
    const [dashboards, setDashboards] = useState<any[]>([]);
    const [points, setPoints] = useState<any[]>([]);
    const [loading, setLoading] = useState(true);

    const favDashIds: string[] = (window as any).pw_fav?.get('pw_fav_dashboards') || [];
    const favKeys: string[] = (window as any).pw_fav?.get('deziko_favorites') || [];

    useEffect(() => {
        let liveUnsubscribe: (() => void) | null = null;

        const loadData = async () => {
            try {
                // Load dashboards
                const dResponse = await fetch('/plswk/api/dashboards');
                if (dResponse.ok) {
                    const allDashes = await dResponse.json();
                    setDashboards(allDashes.filter((d: any) => favDashIds.includes(d.id)));
                }

                // Load points
                if (favKeys.length > 0) {
                    const pointsData = await DashboardService.fetchAvailableTelemetries(favKeys, true);
                    setPoints(pointsData);

                    // Subscribe to live SSE updates
                    liveUnsubscribe = DashboardService.listenToLiveUpdates(favKeys, (newData) => {
                        setPoints(prevPoints => 
                            prevPoints.map(p => {
                                if (newData[p.key] !== undefined) {
                                    return { ...p, value: newData[p.key] };
                                }
                                return p;
                            })
                        );
                    });
                }
            } catch (err) {
                console.error("Failed to load favorites page data:", err);
            } finally {
                setLoading(false);
            }
        };

        loadData();

        return () => {
            if (liveUnsubscribe) {
                liveUnsubscribe();
            }
        };
    }, []);

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

    const favoriteDashboards = dashboards.filter((d: any) => favDashIds.includes(d.id));

    return (
        <div class="flex flex-col gap-8 w-full page-enter">
            {/* Dashboard section */}
            <div>
                <h2 class="text-sm uppercase tracking-wider text-slate-500 font-bold mb-4 flex items-center gap-2">
                    <i class="fas fa-th-large text-slate-500/70"></i>
                    {t('fav_dashboards')}
                </h2>
                {favoriteDashboards.length === 0 ? (
                    <div class="flex flex-col items-center justify-center gap-3 text-center text-slate-400 p-8 glass rounded-2xl">
                        <i class="fas fa-th-large text-3xl opacity-20 text-sky-400"></i>
                        <p class="text-xs">{t('no_fav_dashboards')}</p>
                        <p class="text-[0.72rem] text-slate-500">
                            {t('fav_dashboards_hint').replace('{0}', t('nav_dashboards')).replace('{1}', '★')}
                        </p>
                    </div>
                ) : (
                    <div class="grid grid-cols-[repeat(auto-fill,minmax(320px,1fr))] gap-5">
                        {favoriteDashboards.map(d => (
                            <DashCard key={d.id} dashboard={d} />
                        ))}
                    </div>
                )}
            </div>

            {/* Points section */}
            <div>
                <h2 class="text-sm uppercase tracking-wider text-slate-500 font-bold mb-4 flex items-center gap-2">
                    <i class="fas fa-star text-amber-400/80"></i>
                    {t('favorites')}
                </h2>
                {points.length === 0 ? (
                    <div class="flex flex-col items-center justify-center gap-3 text-center text-slate-400 p-8 glass rounded-2xl">
                        <i class="fas fa-tag text-3xl opacity-20 text-sky-400"></i>
                        <p class="text-xs">{t('no_favorites')}</p>
                        <p class="text-[0.72rem] text-slate-500">
                            {t('favorites_hint').replace('{0}', t('nav_assets')).replace('{1}', '★')}
                        </p>
                    </div>
                ) : (
                    <div class="grid grid-cols-[repeat(auto-fill,minmax(320px,1fr))] gap-5">
                        {points.map(p => (
                            <PointCard key={p.key} point={p} variant="index" />
                        ))}
                    </div>
                )}
            </div>
        </div>
    );
}
