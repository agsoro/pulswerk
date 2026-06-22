// api.ts - Dedicated service for dashboard API calls

export const getCsrfToken = (): string => {
    return (document.querySelector('input[name="__RequestVerificationToken"]') as HTMLInputElement)?.value || '';
};

export const apiCall = async (endpoint: string, opts?: RequestInit): Promise<any> => {
    const response = await fetch(`/plswk/api/${endpoint}`, opts);
    if (!response.ok) {
        throw new Error(`API error: ${response.status} ${response.statusText}`);
    }
    return response.json();
};

export class DashboardService {
    static async fetchWidgetData(keys: string[], startTs: number, endTs: number, barGranularity?: string, barMode?: string): Promise<any> {
        let url = `widget-data?keys=${encodeURIComponent(keys.join(','))}&startTs=${startTs}&endTs=${endTs}`;
        if (barGranularity) url += `&barGranularity=${encodeURIComponent(barGranularity)}`;
        if (barMode) url += `&barMode=${encodeURIComponent(barMode)}`;
        return await apiCall(url);
    }

    static async fetchLatestValues(keys: string | string[]): Promise<any> {
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

    static async fetchAvailableTelemetries(keys?: string[], includeLiveValues: boolean = false): Promise<any> {
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
    
    static async fetchDashboardList(): Promise<any[]> {
        return await apiCall('dashboards');
    }
    
    static async saveDashboard(dashboard: any): Promise<void> {
        await fetch('/plswk/api/dashboards/save', { 
            method: 'POST', 
            headers: { 
                'Content-Type': 'application/json', 
                'RequestVerificationToken': getCsrfToken() 
            }, 
            body: JSON.stringify(dashboard) 
        });
    }

    static async createDashboard(name: string, description: string): Promise<any> {
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

    static async deleteDashboard(id: string): Promise<void> {
        await fetch('/plswk/api/dashboards/delete', { 
            method: 'POST', 
            headers: { 
                'Content-Type': 'application/json', 
                'RequestVerificationToken': getCsrfToken() 
            }, 
            body: JSON.stringify({ id }) 
        });
    }

    private static _es: EventSource | null = null;
    private static _listeners: Map<(data: Record<string, string>) => void, string[]> = new Map();
    private static _reconnectTimeout: any = null;
    private static _reconnectDelay: number = 50;
    private static _lastMessageAt: number = 0;
    private static _stalenessTimer: any = null;
    private static readonly STALENESS_MS = 45000; // reconnect if no message (data or keepalive) for 45s

    private static _scheduleReconnect(delayMs: number = 50) {
        if (this._reconnectTimeout) {
            clearTimeout(this._reconnectTimeout);
        }
        this._reconnectTimeout = setTimeout(() => {
            this._reconnectSSE();
            this._reconnectTimeout = null;
        }, delayMs);
    }

    private static _ensureStalenessTimer(): void {
        if (this._stalenessTimer) return;
        this._stalenessTimer = setInterval(() => {
            // Only enforce staleness when the tab is visible — background tabs
            // are throttled by the browser and would otherwise trigger spurious
            // reconnects. Visibility change handling takes care of background tabs.
            if (document.hidden) return;
            if (!this._es) return;
            if (this._listeners.size === 0) return;
            if (Date.now() - this._lastMessageAt > this.STALENESS_MS) {
                console.warn(`SSE connection stale (no data for ${Math.round((Date.now() - this._lastMessageAt) / 1000)}s). Reconnecting...`);
                this._es?.close();
                this._es = null;
                this._reconnectDelay = 50;
                this._scheduleReconnect();
            }
        }, 15000);
    }

    private static _stopStalenessTimer(): void {
        if (this._stalenessTimer) {
            clearInterval(this._stalenessTimer);
            this._stalenessTimer = null;
        }
    }

    static {
        // Pause SSE while the tab is hidden (browser throttles background timers
        // and rAF anyway, so live updates would just queue up uselessly). On
        // return to foreground, reconnect immediately and re-fetch fresh values
        // so the dashboard doesn't show stale data after a long background period.
        if (typeof document !== 'undefined') {
            document.addEventListener('visibilitychange', () => {
                if (document.hidden) {
                    // Drop the connection while hidden to avoid the browser
                    // buffering a huge backlog of SSE messages.
                    if (DashboardService._es) {
                        DashboardService._es.close();
                        DashboardService._es = null;
                    }
                } else if (DashboardService._listeners.size > 0) {
                    // Tab became visible again — reconnect and re-fetch fresh values.
                    DashboardService._lastMessageAt = Date.now();
                    DashboardService._reconnectDelay = 50;
                    DashboardService._scheduleReconnect();
                    DashboardService._refetchAllListeners();
                }
            });
        }
    }

    private static _refetchAllListeners(): void {
        // After a long background period (or a stale connection), re-fetch the
        // latest values for every listener so widgets show fresh data instead
        // of whatever was last received over SSE. This is especially important
        // for virtual telemetries, which are only pushed when their source
        // values change — a stale tab can miss many intermediate evaluations.
        for (const [cb, keys] of this._listeners.entries()) {
            if (!keys.length) continue;
            DashboardService.fetchLatestValues(keys).then(data => {
                if (data && typeof data === 'object') cb(data);
            }).catch(() => { /* ignore — SSE will deliver updates */ });
        }
    }

    private static async _reconnectSSE() {
        if (this._es) {
            this._es.close();
            this._es = null;
        }

        if (this._listeners.size === 0) return;

        const allKeys = new Set<string>();
        for (const keys of this._listeners.values()) {
            keys.forEach(k => allKeys.add(k));
        }

        const keysArray = Array.from(allKeys);
        if (keysArray.length === 0) return;

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
                if (response.status === 404 || response.status === 405) {
                    throw new Error('FallbackToQueryString');
                }
                throw new Error(`Subscribe failed: ${response.status}`);
            }

            const { subscriptionId } = await response.json();

            if (this._listeners.size === 0) return;

            const activeEs = this._es as EventSource | null;
            if (activeEs) {
                activeEs.close();
            }

            this._es = new EventSource(`/plswk/api/sse?subscriptionId=${encodeURIComponent(subscriptionId)}`);
            
            this._es.onopen = () => {
                this._reconnectDelay = 50; // Reset backoff on success
                this._lastMessageAt = Date.now();
                this._ensureStalenessTimer();
            };

            this._es.onerror = () => {
                console.warn(`SSE connection error. Reconnecting in ${this._reconnectDelay}ms...`);
                this._es?.close();
                this._es = null;
                this._reconnectDelay = Math.min(this._reconnectDelay === 50 ? 1000 : this._reconnectDelay * 2, 30000);
                this._scheduleReconnect(this._reconnectDelay);
            };

            this._es.onmessage = (e) => {
                this._lastMessageAt = Date.now();
                try {
                    const data = JSON.parse(e.data);
                    this._listeners.forEach((_, cb) => cb(data));
                } catch (err) {
                    console.error('Failed to parse SSE data', err);
                }
            };
        } catch (err: any) {
            if (err?.message === 'FallbackToQueryString') {
                console.warn('SSE subscription endpoint not found, falling back to query string');
                if (this._listeners.size === 0) return;

                const query = `?keys=${encodeURIComponent(keysArray.join(','))}`;
                this._es = new EventSource('/plswk/api/sse' + query);
                
                this._es.onopen = () => {
                    this._reconnectDelay = 50;
                    this._lastMessageAt = Date.now();
                    this._ensureStalenessTimer();
                };

                this._es.onerror = () => {
                    console.warn(`SSE fallback connection error. Reconnecting in ${this._reconnectDelay}ms...`);
                    this._es?.close();
                    this._es = null;
                    this._reconnectDelay = Math.min(this._reconnectDelay === 50 ? 1000 : this._reconnectDelay * 2, 30000);
                    this._scheduleReconnect(this._reconnectDelay);
                };

                this._es.onmessage = (e) => {
                    this._lastMessageAt = Date.now();
                    try {
                        const data = JSON.parse(e.data);
                        this._listeners.forEach((_, cb) => cb(data));
                    } catch (parseErr) {
                        console.error('Failed to parse SSE data', parseErr);
                    }
                };
            } else {
                console.warn(`SSE subscription failed: ${err}. Reconnecting in ${this._reconnectDelay}ms...`);
                this._reconnectDelay = Math.min(this._reconnectDelay === 50 ? 1000 : this._reconnectDelay * 2, 30000);
                this._scheduleReconnect(this._reconnectDelay);
            }
        }
    }

    static listenToLiveUpdates(keys: string[], callback: (data: Record<string, string>) => void): () => void {
        const oldKeys = this._listeners.get(callback);
        let keysChanged = false;

        if (oldKeys) {
            if (oldKeys.length !== keys.length || oldKeys.some((k, i) => k !== keys[i])) {
                keysChanged = true;
            }
        } else {
            keysChanged = true;
        }

        this._listeners.set(callback, keys);

        if (!this._es || keysChanged) {
            this._scheduleReconnect();
        }

        // Return unsubscribe function
        return () => {
            this._listeners.delete(callback);
            if (this._listeners.size === 0) {
                // No more listeners — close the connection and stop monitoring.
                if (this._es) { this._es.close(); this._es = null; }
                this._stopStalenessTimer();
            } else {
                this._scheduleReconnect();
            }
        };
    }
}

