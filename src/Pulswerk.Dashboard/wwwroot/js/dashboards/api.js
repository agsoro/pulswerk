// api.ts - Dedicated service for dashboard API calls
export const getCsrfToken = () => {
    return document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
};
export const apiCall = async (endpoint, opts) => {
    const response = await fetch(`/plswk/api/${endpoint}`, opts);
    if (!response.ok) {
        throw new Error(`API error: ${response.status} ${response.statusText}`);
    }
    return response.json();
};
export class DashboardService {
    static async fetchWidgetData(keys, startTs, endTs) {
        return await apiCall(`widget-data?keys=${encodeURIComponent(keys.join(','))}&startTs=${startTs}&endTs=${endTs}`);
    }
    static async fetchLatestValues(keys) {
        const keysArray = Array.isArray(keys) ? keys : (keys ? keys.split(',') : []);
        if (keysArray.length > 0) {
            return await apiCall('latest-values', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': getCsrfToken()
                },
                body: JSON.stringify({ keys: keysArray })
            });
        }
        return await apiCall('latest-values?keys=');
    }
    static async fetchAvailableTelemetries(keys, includeLiveValues = false) {
        const query = includeLiveValues ? '?includeLiveValues=true' : '';
        if (keys && keys.length > 0) {
            return await apiCall(`telemetries${query}`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': getCsrfToken()
                },
                body: JSON.stringify({ keys })
            });
        }
        return await apiCall(`telemetries${query}`);
    }
    static async fetchDashboardList() {
        return await apiCall('dashboards');
    }
    static async saveDashboard(dashboard) {
        await fetch('/plswk/api/dashboards/save', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getCsrfToken()
            },
            body: JSON.stringify(dashboard)
        });
    }
    static async createDashboard(name, description) {
        const response = await fetch('/plswk/api/dashboards', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getCsrfToken()
            },
            body: JSON.stringify({ name, description })
        });
        return response.json();
    }
    static async deleteDashboard(id) {
        await fetch('/plswk/api/dashboards/delete', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getCsrfToken()
            },
            body: JSON.stringify({ id })
        });
    }
    static _es = null;
    static _listeners = new Map();
    static _reconnectTimeout = null;
    static _scheduleReconnect() {
        if (this._reconnectTimeout) {
            clearTimeout(this._reconnectTimeout);
        }
        this._reconnectTimeout = setTimeout(() => {
            this._reconnectSSE();
            this._reconnectTimeout = null;
        }, 50);
    }
    static async _reconnectSSE() {
        if (this._es) {
            this._es.close();
            this._es = null;
        }
        if (this._listeners.size === 0)
            return;
        const allKeys = new Set();
        for (const keys of this._listeners.values()) {
            keys.forEach(k => allKeys.add(k));
        }
        const keysArray = Array.from(allKeys);
        if (keysArray.length === 0)
            return;
        try {
            const response = await fetch('/plswk/api/sse/subscribe', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': getCsrfToken()
                },
                body: JSON.stringify({ keys: keysArray })
            });
            if (!response.ok) {
                throw new Error(`Subscribe failed: ${response.status}`);
            }
            const { subscriptionId } = await response.json();
            if (this._listeners.size === 0)
                return;
            const activeEs = this._es;
            if (activeEs) {
                activeEs.close();
            }
            this._es = new EventSource(`/plswk/api/sse?subscriptionId=${encodeURIComponent(subscriptionId)}`);
            this._es.onmessage = (e) => {
                try {
                    const data = JSON.parse(e.data);
                    this._listeners.forEach((_, cb) => cb(data));
                }
                catch (err) {
                    console.error('Failed to parse SSE data', err);
                }
            };
        }
        catch (err) {
            console.warn('SSE subscription failed, falling back to query string:', err);
            if (this._listeners.size === 0)
                return;
            const query = `?keys=${encodeURIComponent(keysArray.join(','))}`;
            this._es = new EventSource('/plswk/api/sse' + query);
            this._es.onmessage = (e) => {
                try {
                    const data = JSON.parse(e.data);
                    this._listeners.forEach((_, cb) => cb(data));
                }
                catch (parseErr) {
                    console.error('Failed to parse SSE data', parseErr);
                }
            };
        }
    }
    static listenToLiveUpdates(keys, callback) {
        const oldKeys = this._listeners.get(callback);
        let keysChanged = false;
        if (oldKeys) {
            if (oldKeys.length !== keys.length || oldKeys.some((k, i) => k !== keys[i])) {
                keysChanged = true;
            }
        }
        else {
            keysChanged = true;
        }
        this._listeners.set(callback, keys);
        if (!this._es || keysChanged) {
            this._scheduleReconnect();
        }
        // Return unsubscribe function
        return () => {
            this._listeners.delete(callback);
            this._scheduleReconnect();
        };
    }
}
