// api.ts - Dedicated service for dashboard API calls

export const getCsrfToken = (): string => {
    return (document.querySelector('input[name="__RequestVerificationToken"]') as HTMLInputElement)?.value || '';
};

export const apiCall = async (handler: string, opts?: RequestInit): Promise<any> => {
    const response = await fetch(`${window.location.pathname}?handler=${handler}`, opts);
    if (!response.ok) {
        throw new Error(`API error: ${response.status} ${response.statusText}`);
    }
    return response.json();
};

export class DashboardService {
    static async fetchWidgetData(keys: string[], startTs: number, endTs: number): Promise<any> {
        return await apiCall(`WidgetData&keys=${keys.join(',')}&startTs=${startTs}&endTs=${endTs}`);
    }

    static async fetchLatestValues(keys: string | string[]): Promise<any> {
        const keysStr = Array.isArray(keys) ? keys.join(',') : keys;
        return await apiCall(`LatestValues&keys=${keysStr}`);
    }

    static async fetchAvailableTelemetries(): Promise<any> {
        return await apiCall('AvailableTelemetries');
    }
    
    static async fetchDashboardList(): Promise<any[]> {
        return await apiCall('List');
    }
    
    static async saveDashboard(dashboard: any): Promise<void> {
        await fetch('/plswk/Dashboards?handler=Save', { 
            method: 'POST', 
            headers: { 
                'Content-Type': 'application/json', 
                'RequestVerificationToken': getCsrfToken() 
            }, 
            body: JSON.stringify(dashboard) 
        });
    }

    static async createDashboard(name: string, description: string): Promise<any> {
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

    static async deleteDashboard(id: string): Promise<void> {
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
