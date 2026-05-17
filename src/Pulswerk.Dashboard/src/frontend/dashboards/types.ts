export interface IWidgetConfig {
    keys?: string[];
    key?: string;
    chartType?: string;
    stacked?: boolean;
    showLegend?: boolean;
    svgSource?: string;
    svgData?: string;
    pointLayout?: string;
    [key: string]: any;
}

export interface IWidget {
    id: string;
    type: string;
    title?: string;
    x: number;
    y: number;
    w: number;
    h: number;
    config?: IWidgetConfig;
}

export interface IDashboard {
    id: string;
    name: string;
    description?: string;
    widgets?: IWidget[];
    timewindow?: {
        mode: string;
        realtimeMs: number;
    };
    updatedAt?: string;
}

export interface ITimeWindowSelector {
    mode: string;
    realtimeMs: number;
    getRange: () => { startTs: number; endTs: number; mode: string; realtimeMs: number };
}
