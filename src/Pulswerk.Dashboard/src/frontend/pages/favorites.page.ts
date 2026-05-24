/// <reference path="../types/pulswerk.d.ts" />
import { h, render } from 'preact';
import { PointCard } from './components/PointCard';
import { DashboardService } from '../dashboards/api';

let allPoints: any[] = [];
let liveUnsubscribe: (() => void) | null = null;

export async function loadFavorites(): Promise<void> {
    if (liveUnsubscribe) {
        liveUnsubscribe();
        liveUnsubscribe = null;
    }

    const favKeys: string[] = window.pw_fav.get('deziko_favorites');
    const list = document.getElementById('favoritesList');
    const empty = document.getElementById('emptyFavorites');

    if (!list || !empty) return;

    if (favKeys.length === 0) {
        list.style.display = 'none';
        empty.style.display = 'flex';
        return;
    }

    list.style.display = 'grid';
    empty.style.display = 'none';

    try {
        allPoints = await DashboardService.fetchAvailableTelemetries(favKeys, true);
        
        list.innerHTML = '';

        allPoints.forEach(point => {
            renderPoint(point, list);
        });

        // Subscribe to live SSE updates for the favorite keys
        liveUnsubscribe = DashboardService.listenToLiveUpdates(favKeys, (newData) => {
            Object.entries(newData).forEach(([key, val]) => {
                const el = document.querySelector(`.point-value[data-key="${key}"]`) as HTMLElement;
                if (el) {
                    el.textContent = PulswerkValue.formatDisplay(val, el.dataset.type || '');
                }
                if (window.currentHistoryKey === key && document.getElementById('historyModal')?.style.display === 'flex') {
                    const lv = document.getElementById('chartLiveValue');
                    if (lv) lv.textContent = PulswerkValue.formatDisplay(val, el?.dataset.type || '');
                }
            });
        });
    } catch (err) { console.error("Failed to load favorites:", err); }
}

function renderPoint(point: any, container: HTMLElement): void {
    const wrapper = document.createElement('div');
    container.appendChild(wrapper);
    render(h(PointCard, { point, variant: 'favorites' }), wrapper);
}

export function initFavoritesPage(): void {
    loadFavorites();

    (window as any).loadFavorites = loadFavorites;
}

initFavoritesPage();
