"use strict";
// @ts-nocheck
/**
 * Global utility functions for the Pulswerk Dashboard
 */
// ─────────────────────────────────────────────────────────────────────────────
//  PulswerkValue — Bidirectional value+type → display conversion
//
//  JavaScript counterpart of C# BacnetValueConverter.
//  Ensures binary objects display as "0" / "1" (or StateText),
//  analog objects display with locale-formatted decimals, and
//  parsing back from display text produces the correct numeric type.
// ─────────────────────────────────────────────────────────────────────────────
const PulswerkValue = (() => {
    /**
     * Returns true if the BACnet object type is a binary type.
     */
    function isBinary(type) {
        if (!type)
            return false;
        const t = type.toUpperCase();
        return t.includes('BINARY') || t === 'OBJECT_CALENDAR';
    }
    /**
     * Returns true if the BACnet object type is a multi-state type.
     */
    function isMultiState(type) {
        if (!type)
            return false;
        return type.toUpperCase().includes('MULTI_STATE');
    }
    /**
     * Returns true if the object type is a schedule.
     */
    function isSchedule(type) {
        if (!type)
            return false;
        return type.toUpperCase().includes('SCHEDULE');
    }
    /**
     * Format a raw value for display, respecting the object type.
     *
     * @param val      - The raw value (number, string, null, undefined)
     * @param type     - BACnet object type string (e.g. "OBJECT_BINARY_VALUE")
     * @param decimals - Decimal places for analog values (default 2)
     * @returns The formatted display string
     */
    function formatDisplay(val, type, decimals = 2) {
        // Null / missing → placeholder
        if (val === null || val === undefined || val === '---' || val === '')
            return '---';
        // Schedule → label only (rendered separately)
        if (isSchedule(type))
            return 'Schedule';
        // Non-numeric strings (state text from backend) → pass through as-is
        const s = String(val);
        const num = typeof val === 'number' ? val : parseFloat(s);
        if (isNaN(num)) {
            // It's a StateText string like "Ein" or "Auto" → show as-is
            return s;
        }
        // Binary objects → integer display (0 or 1), never decimals
        if (isBinary(type)) {
            return String(Math.round(num));
        }
        // Multi-state that arrived as a raw number → show as integer
        if (isMultiState(type)) {
            return String(Math.round(num));
        }
        // Analog/integer → locale-formatted with decimals
        return num.toLocaleString(typeof currentLang !== 'undefined' ? currentLang : undefined, { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
    }
    /**
     * Parse a display string back to a numeric value for writing.
     * Handles locale-formatted numbers (e.g. "21,50" → 21.5).
     *
     * @param displayVal - The display string from the UI
     * @param type       - BACnet object type string
     * @returns The parsed numeric value
     */
    function parseDisplay(displayVal, type) {
        if (!displayVal || displayVal === '---')
            return 0;
        const s = String(displayVal).trim();
        // Binary: check for common boolean labels
        if (isBinary(type)) {
            const lower = s.toLowerCase();
            if (['on', '1', 'true', 'active', 'ein', 'ja', 'yes'].includes(lower))
                return 1;
            if (['off', '0', 'false', 'inactive', 'aus', 'nein', 'no'].includes(lower))
                return 0;
            const n = parseFloat(s);
            return (!isNaN(n) && n !== 0) ? 1 : 0;
        }
        // Analog: handle locale separators (replace comma with dot if needed)
        let cleaned = s.replace(/\s/g, '');
        // If the string has both '.' and ',', determine which is the decimal separator
        if (cleaned.includes(',') && cleaned.includes('.')) {
            // e.g. "1.234,56" (de) or "1,234.56" (en)
            if (cleaned.lastIndexOf(',') > cleaned.lastIndexOf('.')) {
                // comma is decimal separator
                cleaned = cleaned.replace(/\./g, '').replace(',', '.');
            }
            else {
                // dot is decimal separator
                cleaned = cleaned.replace(/,/g, '');
            }
        }
        else if (cleaned.includes(',')) {
            // Single comma → treat as decimal separator
            cleaned = cleaned.replace(',', '.');
        }
        const n = parseFloat(cleaned);
        return isNaN(n) ? 0 : n;
    }
    /**
     * Returns the CSS class for a value based on type.
     */
    function valueClass(type) {
        if (isBinary(type))
            return 'pw-val-binary';
        if (isMultiState(type))
            return 'pw-val-multistate';
        if (isSchedule(type))
            return 'pw-val-schedule';
        return 'pw-val-analog';
    }
    // Public API
    return { isBinary, isMultiState, isSchedule, formatDisplay, parseDisplay, valueClass };
})();
// ─────────────────────────────────────────────────────────────────────────────
//  Legacy compat: formatNumber still available but delegates to PulswerkValue
// ─────────────────────────────────────────────────────────────────────────────
/**
 * @deprecated Use PulswerkValue.formatDisplay(val, type, decimals) instead.
 * Kept for backward compatibility in chart axis formatters etc.
 */
function formatNumber(val, decimals = 2) {
    if (val === null || val === undefined || val === '---')
        return '---';
    const num = typeof val === 'number' ? val : parseFloat(val);
    if (isNaN(num))
        return String(val);
    return num.toLocaleString(typeof currentLang !== 'undefined' ? currentLang : undefined, {
        minimumFractionDigits: decimals,
        maximumFractionDigits: decimals
    });
}
// ─────────────────────────────────────────────────────────────────────────────
//  Point icon helper
// ─────────────────────────────────────────────────────────────────────────────
/**
 * Returns a FontAwesome icon based on the point type
 */
function getPointIcon(type) {
    if (!type)
        return '<i class="fas fa-tag"></i>';
    const t = type.toLowerCase();
    if (t.includes('analog'))
        return '<i class="fas fa-microchip"></i>';
    if (t.includes('binary') || t.includes('bool'))
        return '<i class="fas fa-toggle-on"></i>';
    if (t.includes('multistate') || t.includes('enum'))
        return '<i class="fas fa-list-ul"></i>';
    if (t.includes('temp'))
        return '<i class="fas fa-thermometer-half"></i>';
    if (t.includes('power') || t.includes('energy') || t.includes('kw'))
        return '<i class="fas fa-bolt"></i>';
    if (t.includes('water') || t.includes('flow'))
        return '<i class="fas fa-tint"></i>';
    if (t.includes('pressure'))
        return '<i class="fas fa-tachometer-alt"></i>';
    if (t.includes('fan') || t.includes('air'))
        return '<i class="fas fa-wind"></i>';
    if (t.includes('pump'))
        return '<i class="fas fa-fill-drip"></i>';
    if (t.includes('calendar'))
        return '<i class="fas fa-calendar-alt"></i>';
    if (t.includes('schedule'))
        return '<i class="fas fa-clock"></i>';
    return '<i class="fas fa-tag"></i>';
}
// ─────────────────────────────────────────────────────────────────────────────
//  Favorites (localStorage)
// ─────────────────────────────────────────────────────────────────────────────
/**
 * Toggles a key in localStorage favorites and updates the UI star
 */
function toggleFavorite(key, btn) {
    let favs = pw_fav.get('deziko_favorites');
    const index = favs.indexOf(key);
    if (index > -1) {
        favs.splice(index, 1);
        if (btn) {
            const iEl = btn.querySelector('i');
            if (iEl)
                iEl.className = 'far fa-star';
            btn.classList.remove('active');
        }
    }
    else {
        favs.push(key);
        if (btn) {
            const iEl = btn.querySelector('i');
            if (iEl)
                iEl.className = 'fas fa-star text-amber-400';
            btn.classList.add('active');
        }
    }
    pw_fav.set('deziko_favorites', favs);
    // If we are on the Index page, reload the favorites list
    if (typeof loadFavorites === 'function') {
        loadFavorites();
    }
}
/**
 * Updates the star icon state based on localStorage
 */
function updateStarState(key, btn) {
    if (!btn)
        return;
    const favs = pw_fav.get('deziko_favorites');
    const iEl = btn.querySelector('i');
    if (favs.includes(key)) {
        if (iEl)
            iEl.className = 'fas fa-star text-amber-400';
        btn.classList.add('active');
    }
    else {
        if (iEl)
            iEl.className = 'far fa-star';
        btn.classList.remove('active');
    }
}
window.PulswerkValue = PulswerkValue;
window.formatNumber = formatNumber;
window.getPointIcon = getPointIcon;
window.toggleFavorite = toggleFavorite;
window.updateStarState = updateStarState;
