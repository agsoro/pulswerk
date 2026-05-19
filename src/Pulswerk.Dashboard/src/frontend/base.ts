// @ts-nocheck
/**
 * base.js — Shared utilities available on all pages.
 * Loaded in _Layout.cshtml before modals.js and page-specific scripts.
 */

// ── Global key metadata cache ─────────────────────────────────────────────
// Populated lazily on first use. Pages with their own allKeys (e.g. Dashboards)
// will have already set this before base.js functions are called.
let _allKeysVal: ITelemetryMeta[] = [];
if (typeof (window as any).allKeys === 'undefined') {
    Object.defineProperty(window, 'allKeys', {
        get: () => _allKeysVal,
        set: (v) => { _allKeysVal = v; },
        configurable: true
    });
} else {
    _allKeysVal = (window as any).allKeys;
}

/**
 * Resolve key metadata from the allKeys cache.
 * Falls back to basic info derived from the key string.
 */
function resolveKeyMeta(key: string): ITelemetryMeta {
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
async function ensureKeysMeta(): Promise<void> {
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
async function fetchLatestValues(keys: string | string[]): Promise<Record<string, any>> {
    const keyStr = Array.isArray(keys) ? keys.join(',') : keys;
    try {
        const r = await fetch(`?handler=LatestValues&keys=${encodeURIComponent(keyStr)}`);
        return r.ok ? await r.json() : {};
    } catch (e) { return {}; }
}

// ── HTML helpers ──────────────────────────────────────────────────────────
function esc(s: string | null | undefined): string { const d = document.createElement('div'); d.textContent = s ?? ''; return d.innerHTML; }

function friendlyName(key: string): string {
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

    function reportError(payload: IClientErrorPayload) {
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
        reportError({ msg: String(msg), source: String(source), line: Number(line), col: Number(col), stack: error?.stack || '' });
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

// ── Authelia User Identity ────────────────────────────────────────────────
// Fetches user info from the backend (reads Authelia reverse-proxy headers)
// and updates the sidebar badge + popover.

let _currentUserVal: IUserIdentity | null = null;
if (typeof (window as any)._currentUser === 'undefined') {
    Object.defineProperty(window, '_currentUser', {
        get: () => _currentUserVal,
        set: (v) => { _currentUserVal = v; },
        configurable: true
    });
} else {
    _currentUserVal = (window as any)._currentUser;
}

function getUserInitials(name: string): string {
    if (!name || name === 'Public' || name === 'public') return '';
    const parts = name.trim().split(/\s+/);
    if (parts.length >= 2) return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
    return name.substring(0, 2).toUpperCase();
}

window.pw_fav = {
    get: (key: string) => {
        const u = window._currentUser?.user || 'public';
        const userKey = `${u}_${key}`;
        const raw = localStorage.getItem(userKey);
        if (raw && raw !== '[]') return JSON.parse(raw);
        // Fallback/Migration 1: try public scoped key (if favorited before login loaded)
        if (u !== 'public') {
            const pubKey = `public_${key}`;
            const pub = localStorage.getItem(pubKey);
            if (pub && pub !== '[]') {
                const parsed = JSON.parse(pub);
                if (parsed.length > 0) {
                    localStorage.setItem(userKey, pub);
                    return parsed;
                }
            }
        }
        // Fallback/Migration 2: try global legacy key
        const legacy = localStorage.getItem(key);
        if (legacy && legacy !== '[]') {
            const parsed = JSON.parse(legacy);
            if (parsed.length > 0) {
                localStorage.setItem(userKey, legacy);
                return parsed;
            }
        }
        return raw ? JSON.parse(raw) : [];
    },
    set: (key: string, val: any) => {
        const u = window._currentUser?.user || 'public';
        localStorage.setItem(`${u}_${key}`, JSON.stringify(val));
    }
};

async function loadUserIdentity(): Promise<void> {
    try {
        const r = await fetch('/plswk/api/user');
        if (!r.ok) return;
        _currentUser = await r.json();
    } catch (e) {
        _currentUser = { authenticated: false, user: 'public', name: 'Public', email: '', groups: [], canWriteValue: true, canAckAlarm: true, canEditDashboard: true, canEditFavorites: true };
    }
    
    // Set global permission flags
    window.pwCanWriteValue = _currentUser!.canWriteValue;
    window.pwCanAckAlarm = _currentUser!.canAckAlarm;
    window.pwCanEditDashboard = _currentUser!.canEditDashboard;
    window.pwCanEditFavorites = _currentUser!.canEditFavorites;

    updateUserBadge(_currentUser!);
    applyRightsToUI(_currentUser!);

    if (typeof (window as any).loadFavorites === 'function') {
        (window as any).loadFavorites();
    }
    if (typeof (window as any).loadFavoriteDashboards === 'function') {
        (window as any).loadFavoriteDashboards();
    }
}

/** Hides/Disables UI elements based on user rights. */
function applyRightsToUI(u: IUserIdentity): void {
    if (!u.canWriteValue) {
        document.querySelectorAll('.auth-write-only').forEach(el => (el as HTMLElement).style.display = 'none');
        document.querySelectorAll('.auth-write-disable').forEach(el => {
            (el as HTMLInputElement).disabled = true;
            (el as HTMLElement).title = 'Insufficient rights (value edit permission required)';
        });
    }
    if (!u.canAckAlarm) {
        document.querySelectorAll('.auth-ack-only').forEach(el => (el as HTMLElement).style.display = 'none');
    }
    if (!u.canEditDashboard) {
        document.querySelectorAll('.auth-edit-dash-only').forEach(el => (el as HTMLElement).style.display = 'none');
    }
    if (!u.canEditFavorites) {
        document.querySelectorAll('.auth-edit-fav-only').forEach(el => (el as HTMLElement).style.display = 'none');
    }
}

function updateUserBadge(u: IUserIdentity): void {
    const avatar = document.getElementById('userAvatar');
    const nameLabel = document.getElementById('userNameLabel');
    const popAvatar = document.getElementById('popoverAvatar');
    const popName = document.getElementById('popoverName');
    const popEmail = document.getElementById('popoverEmail');
    const popGroups = document.getElementById('popoverGroups');
    const popGroupList = document.getElementById('popoverGroupList');
    const authChip = document.getElementById('authChip');

    if (!avatar) return; // layout not loaded yet

    if (u.user && u.user !== 'public') {
        const initials = getUserInitials(u.name);
        avatar.innerHTML = initials;
        avatar.classList.add('authenticated');
        avatar.title = u.name || u.user;
        if (nameLabel) nameLabel.textContent = u.name || u.user;

        if (popAvatar) {
            popAvatar.innerHTML = initials;
            popAvatar.classList.add('authenticated');
        }
        if (popName) popName.textContent = u.name || u.user;
        if (popEmail) popEmail.textContent = u.email || u.user;

        if (popGroups && popGroupList && u.groups && u.groups.length > 0) {
            popGroups.style.display = '';
            popGroupList.innerHTML = u.groups.map(g => {
                const isAdmin = g.toLowerCase().includes('admin');
                const icon = isAdmin ? 'fa-shield-alt' : 'fa-tag';
                return `<span class="user-group-chip${isAdmin ? ' admin' : ''}"><i class="fas ${icon}"></i>${esc(g)}</span>`;
            }).join('');
        } else if (popGroups) {
            popGroups.style.display = 'none';
        }

        if (authChip) {
            authChip.className = 'user-auth-chip authenticated';
            if (u.isDefault) {
                authChip.innerHTML = '<i class="fas fa-id-badge"></i> Default Profile';
            } else {
                authChip.innerHTML = '<i class="fas fa-shield-alt"></i> Authenticated';
            }
        }
    } else {
        avatar.innerHTML = '<i class="fas fa-globe"></i>';
        avatar.classList.remove('authenticated');
        avatar.title = 'Public';
        if (nameLabel) nameLabel.textContent = 'Public';

        if (popAvatar) {
            popAvatar.innerHTML = '<i class="fas fa-globe"></i>';
            popAvatar.classList.remove('authenticated');
        }
        if (popName) popName.textContent = 'Public';
        if (popEmail) popEmail.textContent = 'Not authenticated';
        if (popGroups) popGroups.style.display = 'none';
        if (authChip) {
            authChip.className = 'user-auth-chip public';
            authChip.innerHTML = '<i class="fas fa-globe"></i> Public Access';
        }
    }
}

function toggleUserPopover(e?: Event): void {
    e?.stopPropagation();
    const pop = document.getElementById('userPopover');
    if (!pop) return;
    pop.classList.toggle('open');
}

// Close popover when clicking outside
document.addEventListener('click', function(e: Event) {
    const pop = document.getElementById('userPopover');
    if (pop && pop.classList.contains('open') && !(e.target as HTMLElement).closest('#userPopover') && !(e.target as HTMLElement).closest('#userBadge')) {
        pop.classList.remove('open');
    }
});

// Load user on page ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', loadUserIdentity);
} else {
    loadUserIdentity();
}

(window as any).resolveKeyMeta = resolveKeyMeta;
(window as any).ensureKeysMeta = ensureKeysMeta;
(window as any).fetchLatestValues = fetchLatestValues;
(window as any).esc = esc;
(window as any).friendlyName = friendlyName;
(window as any).getUserInitials = getUserInitials;
(window as any).loadUserIdentity = loadUserIdentity;
(window as any).applyRightsToUI = applyRightsToUI;
(window as any).updateUserBadge = updateUserBadge;
(window as any).toggleUserPopover = toggleUserPopover;
