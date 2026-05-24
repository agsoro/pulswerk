import { jsx as _jsx, jsxs as _jsxs } from "preact/jsx-runtime";
import { useState, useEffect } from 'preact/hooks';
import { DashCard } from './components/DashCard';
import { PointCard } from './components/PointCard';
import { DashboardService } from '../dashboards/api';
import { t } from '../i18n';
export function FavoritesPage() {
    const [dashboards, setDashboards] = useState([]);
    const [points, setPoints] = useState([]);
    const [loading, setLoading] = useState(true);
    const favDashIds = window.pw_fav?.get('pw_fav_dashboards') || [];
    const favKeys = window.pw_fav?.get('deziko_favorites') || [];
    useEffect(() => {
        let liveUnsubscribe = null;
        const loadData = async () => {
            try {
                // Load dashboards
                const dResponse = await fetch('/plswk/api/dashboards');
                if (dResponse.ok) {
                    const allDashes = await dResponse.json();
                    setDashboards(allDashes.filter((d) => favDashIds.includes(d.id)));
                }
                // Load points
                if (favKeys.length > 0) {
                    const pointsData = await DashboardService.fetchAvailableTelemetries(favKeys, true);
                    setPoints(pointsData);
                    // Subscribe to live SSE updates
                    liveUnsubscribe = DashboardService.listenToLiveUpdates(favKeys, (newData) => {
                        setPoints(prevPoints => prevPoints.map(p => {
                            if (newData[p.key] !== undefined) {
                                return { ...p, value: newData[p.key] };
                            }
                            return p;
                        }));
                    });
                }
            }
            catch (err) {
                console.error("Failed to load favorites page data:", err);
            }
            finally {
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
        return (_jsx("div", { class: "h-full flex items-center justify-center p-16", children: _jsxs("div", { class: "flex flex-col items-center gap-4 text-cyan-400", children: [_jsx("i", { class: "fas fa-spinner fa-spin text-3xl" }), _jsx("p", { class: "text-sm font-medium animate-pulse", children: t('loading') })] }) }));
    }
    const favoriteDashboards = dashboards.filter((d) => favDashIds.includes(d.id));
    return (_jsxs("div", { class: "flex flex-col gap-8 w-full page-enter", children: [_jsxs("div", { children: [_jsxs("h2", { class: "text-sm uppercase tracking-wider text-slate-500 font-bold mb-4 flex items-center gap-2", children: [_jsx("i", { class: "fas fa-th-large text-slate-500/70" }), t('fav_dashboards')] }), favoriteDashboards.length === 0 ? (_jsxs("div", { class: "flex flex-col items-center justify-center gap-3 text-center text-slate-400 p-8 glass rounded-2xl", children: [_jsx("i", { class: "fas fa-th-large text-3xl opacity-20 text-sky-400" }), _jsx("p", { class: "text-xs", children: t('no_fav_dashboards') }), _jsx("p", { class: "text-[0.72rem] text-slate-500", children: t('fav_dashboards_hint').replace('{0}', t('nav_dashboards')).replace('{1}', '★') })] })) : (_jsx("div", { class: "grid grid-cols-[repeat(auto-fill,minmax(320px,1fr))] gap-5", children: favoriteDashboards.map(d => (_jsx(DashCard, { dashboard: d }, d.id))) }))] }), _jsxs("div", { children: [_jsxs("h2", { class: "text-sm uppercase tracking-wider text-slate-500 font-bold mb-4 flex items-center gap-2", children: [_jsx("i", { class: "fas fa-star text-amber-400/80" }), t('favorites')] }), points.length === 0 ? (_jsxs("div", { class: "flex flex-col items-center justify-center gap-3 text-center text-slate-400 p-8 glass rounded-2xl", children: [_jsx("i", { class: "fas fa-tag text-3xl opacity-20 text-sky-400" }), _jsx("p", { class: "text-xs", children: t('no_favorites') }), _jsx("p", { class: "text-[0.72rem] text-slate-500", children: t('favorites_hint').replace('{0}', t('nav_assets')).replace('{1}', '★') })] })) : (_jsx("div", { class: "grid grid-cols-[repeat(auto-fill,minmax(320px,1fr))] gap-5", children: points.map(p => (_jsx(PointCard, { point: p, variant: "index" }, p.key))) }))] })] }));
}
