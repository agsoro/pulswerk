"use strict";
// @ts-nocheck
/**
 * Reusable Time-Window Selector
 *
 * Usage:
 *   const tw = createTimeWindowSelector(containerEl, {
 *       onChange: (range) => { ... },
 *       mode: 'realtime',         // default mode
 *       realtimeMs: 3600000       // default preset
 *   });
 *   tw.getRange();   // { startTs, endTs }
 *   tw.setPreset(ms);
 *   tw.destroy();
 */
const TW_PRESETS = [
    { label: 'Last 5 min', ms: 300000 },
    { label: 'Last 15 min', ms: 900000 },
    { label: 'Last 1 hour', ms: 3600000 },
    { label: 'Last 6 hours', ms: 21600000 },
    { label: 'Last 12 hours', ms: 43200000 },
    { label: 'Last 24 hours', ms: 86400000 },
    { label: 'Last 7 days', ms: 604800000 },
    { label: 'Last 30 days', ms: 2592000000 }
];
function createTimeWindowSelector(container, opts = {}) {
    let mode = opts.mode || 'realtime';
    let realtimeMs = opts.realtimeMs || 3600000;
    let histFrom = opts.histFrom || null;
    let histTo = opts.histTo || null;
    const onChange = opts.onChange || (() => { });
    const uid = 'tw_' + Math.random().toString(36).slice(2, 8);
    // Build HTML
    const wrapper = document.createElement('div');
    wrapper.className = 'tw-selector';
    wrapper.setAttribute('data-tw-uid', uid);
    wrapper.innerHTML = `
        <i class="fas fa-clock text-[0.9rem] tw-mode-icon"></i>
        <span class="tw-label">Last 1 hour</span>
        <i class="fas fa-caret-down text-[0.7rem]"></i>
        <div class="tw-dropdown" onclick="event.stopPropagation()">
            <div class="flex gap-1 mb-2">
                <button class="tw-tab active" data-tw-mode="realtime">Realtime</button>
                <button class="tw-tab" data-tw-mode="history">History</button>
            </div>
            <div class="tw-realtime-panel">
                <div class="tw-presets tw-presets-grid"></div>
            </div>
            <div class="tw-history-panel" style="display:none">
                <div class="tw-hist-fields">
                    <div class="tw-hist-row">
                        <label>From</label>
                        <input type="datetime-local" class="tw-hist-input tw-hist-from">
                    </div>
                    <div class="tw-hist-row">
                        <label>To</label>
                        <input type="datetime-local" class="tw-hist-input tw-hist-to">
                    </div>
                </div>
                <button class="btn-primary btn-sm tw-apply-btn"><i class="fas fa-check"></i> Apply</button>
            </div>
        </div>`;
    container.appendChild(wrapper);
    // Cache DOM refs
    const labelEl = wrapper.querySelector('.tw-label');
    const iconEl = wrapper.querySelector('.tw-mode-icon');
    const dropdown = wrapper.querySelector('.tw-dropdown');
    const presetGrid = wrapper.querySelector('.tw-presets-grid');
    const realtimePanel = wrapper.querySelector('.tw-realtime-panel');
    const histPanel = wrapper.querySelector('.tw-history-panel');
    const tabs = wrapper.querySelectorAll('.tw-tab');
    const histFromEl = wrapper.querySelector('.tw-hist-from');
    const histToEl = wrapper.querySelector('.tw-hist-to');
    const applyBtn = wrapper.querySelector('.tw-apply-btn');
    // Build presets
    function buildPresets() {
        presetGrid.innerHTML = TW_PRESETS.map(p => `<button class="tw-preset${p.ms === realtimeMs ? ' active' : ''}" data-ms="${p.ms}">${p.label}</button>`).join('');
    }
    buildPresets();
    // Update label
    function updateLabel() {
        if (mode === 'realtime') {
            const p = TW_PRESETS.find(x => x.ms === realtimeMs);
            labelEl.textContent = p ? p.label : `Last ${Math.round(realtimeMs / 60000)} min`;
            iconEl.className = 'fas fa-clock text-[0.9rem] tw-mode-icon';
        }
        else {
            const f = histFrom ? new Date(histFrom).toLocaleDateString() : '?';
            const t = histTo ? new Date(histTo).toLocaleDateString() : '?';
            labelEl.textContent = `${f} → ${t}`;
            iconEl.className = 'fas fa-calendar-alt text-[0.9rem] tw-mode-icon';
        }
    }
    updateLabel();
    // Toggle dropdown
    wrapper.addEventListener('click', e => {
        if (e.target.closest('.tw-dropdown'))
            return;
        e.stopPropagation();
        dropdown.classList.toggle('open');
    });
    // Tab switching
    tabs.forEach(tab => {
        tab.addEventListener('click', () => {
            const m = tab.dataset.twMode;
            if (!m)
                return;
            mode = m;
            tabs.forEach(t => t.classList.toggle('active', t.dataset.twMode === m));
            realtimePanel.style.display = m === 'realtime' ? '' : 'none';
            histPanel.style.display = m === 'history' ? '' : 'none';
            if (m === 'history' && !histFromEl.value) {
                const now = new Date();
                const from = new Date(now.getTime() - realtimeMs);
                histFromEl.value = from.toISOString().slice(0, 16);
                histToEl.value = now.toISOString().slice(0, 16);
            }
        });
    });
    // Preset click
    presetGrid.addEventListener('click', e => {
        const btn = e.target.closest('.tw-preset');
        if (!btn)
            return;
        realtimeMs = parseInt(btn.dataset.ms || '3600000');
        mode = 'realtime';
        presetGrid.querySelectorAll('.tw-preset').forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        dropdown.classList.remove('open');
        updateLabel();
        onChange(getRange());
    });
    // Apply history
    applyBtn.addEventListener('click', () => {
        const from = histFromEl.value, to = histToEl.value;
        if (!from || !to)
            return;
        histFrom = new Date(from).getTime();
        histTo = new Date(to).getTime();
        mode = 'history';
        dropdown.classList.remove('open');
        updateLabel();
        onChange(getRange());
    });
    // Close on outside click
    function onDocClick(e) {
        if (!wrapper.contains(e.target))
            dropdown.classList.remove('open');
    }
    document.addEventListener('click', onDocClick);
    // Public API
    function getRange() {
        if (mode === 'history' && histFrom && histTo)
            return { startTs: histFrom, endTs: histTo, mode: 'history' };
        const now = Date.now();
        return { startTs: now - realtimeMs, endTs: now, mode: 'realtime', realtimeMs };
    }
    function setPreset(ms) {
        realtimeMs = ms;
        mode = 'realtime';
        buildPresets();
        updateLabel();
        onChange(getRange());
    }
    function destroy() {
        document.removeEventListener('click', onDocClick);
        wrapper.remove();
    }
    return { getRange, setPreset, updateLabel, destroy, get mode() { return mode; }, get realtimeMs() { return realtimeMs; } };
}
window.createTimeWindowSelector = createTimeWindowSelector;
