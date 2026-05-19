/// <reference path="../types/pulswerk.d.ts" />
import { h, render } from 'preact';
import { PointCard } from './components/PointCard';
let allPoints: any[] = [];

export async function loadFavorites(): Promise<void> {
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
        const response = await fetch('?handler=Tree');
        const trees = await response.json();
        
        allPoints = [];
        const extractPoints = (nodes: any[]) => {
            nodes.forEach(n => {
                if (n.telemetries) allPoints.push(...n.telemetries);
                if (n.children) extractPoints(n.children);
            });
        };
        extractPoints(trees);

        list.innerHTML = '';

        favKeys.forEach(key => {
            const point = allPoints.find(p => p.key === key);
            if (point) renderPoint(point, list);
        });
    } catch (err) { console.error("Failed to load favorites:", err); }
}

function renderPoint(point: any, container: HTMLElement): void {
    const wrapper = document.createElement('div');
    container.appendChild(wrapper);
    render(h(PointCard, { point, variant: 'favorites' }), wrapper);
}

async function refreshValues(): Promise<void> {
    try {
        const response = await fetch('?handler=Tree');
        const newTrees = await response.json();
        const updateValues = (nodes: any[]) => {
            nodes.forEach(node => {
                if (node.telemetries) {
                    node.telemetries.forEach((p: any) => {
                        const el = document.querySelector(`.point-value[data-key="${p.key}"]`) as HTMLElement;
                        if (el) el.textContent = PulswerkValue.formatDisplay(p.value, el.dataset.type || p.type);
                        if (window.currentHistoryKey === p.key && document.getElementById('historyModal')?.style.display === 'flex') {
                            const lv = document.getElementById('chartLiveValue');
                            if (lv) lv.textContent = PulswerkValue.formatDisplay(p.value, p.type);
                        }
                    });
                }
                if (node.children) updateValues(node.children);
            });
        };
        updateValues(newTrees);
    } catch (e) {}
}

export function initFavoritesPage(): void {
    loadFavorites();
    setInterval(refreshValues, 2000);

    (window as any).loadFavorites = loadFavorites;
}

initFavoritesPage();
