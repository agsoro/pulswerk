// api.ts - Dedicated service for dashboard API calls
export const getCsrfToken = () => {
    return document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
};
export const apiCall = async (handler, opts) => {
    const response = await fetch(`${window.location.pathname}?handler=${handler}`, opts);
    if (!response.ok) {
        throw new Error(`API error: ${response.status} ${response.statusText}`);
    }
    return response.json();
};
export class DashboardService {
    static async fetchWidgetData(keys, startTs, endTs) {
        return await apiCall(`WidgetData&keys=${keys.join(',')}&startTs=${startTs}&endTs=${endTs}`);
    }
    static async fetchLatestValues(keys) {
        const keysStr = Array.isArray(keys) ? keys.join(',') : keys;
        return await apiCall(`LatestValues&keys=${keysStr}`);
    }
    static async fetchAvailableTelemetries() {
        return await apiCall('AvailableTelemetries');
    }
    static async fetchDashboardList() {
        return await apiCall('List');
    }
    static async saveDashboard(dashboard) {
        await fetch('/plswk/Dashboards?handler=Save', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getCsrfToken()
            },
            body: JSON.stringify(dashboard)
        });
    }
    static async createDashboard(name, description) {
        const response = await fetch('/plswk/Dashboards?handler=Create', {
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
        await fetch('/plswk/Dashboards?handler=Delete', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getCsrfToken()
            },
            body: JSON.stringify({ id })
        });
    }
}
