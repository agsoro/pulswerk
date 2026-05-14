/**
 * base.js — Shared utilities available on all pages.
 * Loaded in _Layout.cshtml before modals.js and page-specific scripts.
 */

// ── Global key metadata cache ─────────────────────────────────────────────
// Populated lazily on first use. Pages with their own allKeys (e.g. Dashboards)
// will have already set this before base.js functions are called.
if (typeof allKeys === 'undefined') var allKeys = [];

/**
 * Resolve key metadata from the allKeys cache.
 * Falls back to basic info derived from the key string.
 */
function resolveKeyMeta(key) {
    if (allKeys.length) {
        const found = allKeys.find(k => k.key === key);
        if (found) return found;
    }
    const parts = key.split('_');
    return {
        key, name: parts.length > 2 ? parts.slice(1, -1).join(' ') : key,
        fullName: key, units: '', type: '', parentPath: [],
        isWritable: false, enumValues: null
    };
}

/**
 * Lazily load allKeys from the AvailableKeys API if not already populated.
 */
async function ensureKeysMeta() {
    if (allKeys.length) return;
    try {
        const r = await fetch('?handler=AvailableKeys');
        if (r.ok) allKeys = await r.json();
    } catch (e) { /* non-critical */ }
}

/**
 * Fetch current value(s) for one or more keys from the LatestValues API.
 * Returns the raw data object: { key: value, ... }
 */
async function fetchLatestValues(keys) {
    const keyStr = Array.isArray(keys) ? keys.join(',') : keys;
    try {
        const r = await fetch(`?handler=LatestValues&keys=${encodeURIComponent(keyStr)}`);
        return r.ok ? await r.json() : {};
    } catch (e) { return {}; }
}

// ── HTML helpers ──────────────────────────────────────────────────────────
function esc(s) { const d = document.createElement('div'); d.textContent = s ?? ''; return d.innerHTML; }

function friendlyName(key) {
    if (allKeys.length) {
        const meta = allKeys.find(k => k.key === key);
        if (meta) return meta.name || meta.fullName || key;
    }
    const parts = (key || '').split('_');
    return parts.length > 2 ? parts.slice(1, -1).join(' ') : key;
}

// ── Client-side error reporting ───────────────────────────────────────────
// Reports uncaught JS errors to the server for logging (visible in Docker logs).
(function() {
    let errorCount = 0;
    const MAX_ERRORS_PER_MIN = 10;
    setInterval(() => { errorCount = 0; }, 60000);

    function reportError(payload) {
        if (++errorCount > MAX_ERRORS_PER_MIN) return; // rate-limit
        payload.page = location.pathname;
        try {
            navigator.sendBeacon('/plswk/api/client-error', JSON.stringify(payload));
        } catch (_) {
            // sendBeacon not available, try fetch fire-and-forget
            fetch('/plswk/api/client-error', {
                method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload), keepalive: true
            }).catch(() => {});
        }
    }

    window.onerror = function(msg, source, line, col, error) {
        reportError({ msg, source, line, col, stack: error?.stack || '' });
    };

    window.onunhandledrejection = function(event) {
        const reason = event.reason;
        reportError({
            msg: 'Unhandled Promise: ' + (reason?.message || String(reason)),
            source: reason?.fileName || '',
            line: reason?.lineNumber || 0,
            col: reason?.columnNumber || 0,
            stack: reason?.stack || ''
        });
    };
})();
