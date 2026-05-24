/// <reference path="../types/pulswerk.d.ts" />
import { h, render } from 'preact';
import { DashCard } from './components/DashCard';
import { PointCard } from './components/PointCard';
import { DashboardService } from '../dashboards/api';
let allPoints = [];
let allDashboards = [];
let liveUnsubscribe = null;
export async function loadFavoriteDashboards() {
    const favIds = window.pw_fav.get('pw_fav_dashboards');
    const list = document.getElementById('favoriteDashList');
    const empty = document.getElementById('emptyFavoriteDash');
    if (!list || !empty)
        return;
    if (favIds.length === 0) {
        list.style.display = 'none';
        empty.style.display = 'flex';
        return;
    }
    list.style.display = 'grid';
    empty.style.display = 'none';
    try {
        const response = await fetch('/plswk/api/dashboards');
        allDashboards = await response.json();
        list.innerHTML = '';
        favIds.forEach(id => {
            const d = allDashboards.find(x => x.id === id);
            if (d)
                renderDashCard(d, list);
        });
    }
    catch (err) {
        console.error("Failed to load favorite dashboards:", err);
    }
}
function renderDashCard(d, container) {
    const wrapper = document.createElement('div');
    container.appendChild(wrapper);
    render(h(DashCard, { dashboard: d }), wrapper);
}
export async function loadFavorites() {
    if (liveUnsubscribe) {
        liveUnsubscribe();
        liveUnsubscribe = null;
    }
    const favKeys = window.pw_fav.get('deziko_favorites');
    const list = document.getElementById('favoritesList');
    const empty = document.getElementById('emptyFavorites');
    if (!list || !empty)
        return;
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
                const el = document.querySelector(`.point-value[data-key="${key}"]`);
                if (el) {
                    el.textContent = PulswerkValue.formatDisplay(val, el.dataset.type || '');
                }
                if (window.currentHistoryKey === key &&
                    document.getElementById('historyModal')?.style.display === 'flex') {
                    const lv = document.getElementById('chartLiveValue');
                    if (lv)
                        lv.textContent = PulswerkValue.formatDisplay(val, el?.dataset.type || '');
                }
            });
        });
    }
    catch (err) {
        console.error("Failed to load favorites:", err);
    }
}
function renderPoint(point, container) {
    const wrapper = document.createElement('div');
    container.appendChild(wrapper);
    render(h(PointCard, { point, variant: 'index' }), wrapper);
}
export function initIndexPage() {
    loadFavorites();
    loadFavoriteDashboards();
    window.loadFavorites = loadFavorites;
    window.loadFavoriteDashboards = loadFavoriteDashboards;
}
initIndexPage();
