export interface IAnimationRule {
    telemetryKey?: string;
    telemetryKeys?: string[];
    elementId: string;
    formula: string;
    styles: string[];
}

export interface IWidgetConfig {
    title?: string;
    keys?: string[];
    svg?: string;
    animationRules?: IAnimationRule[];
    anchorSvg?: string;
    anchorEdge?: string;
    posX?: number;
    posY?: number;
    anchorW?: number;
    anchorH?: number;
    zoom?: number;
    [key: string]: any;
}

export interface IWidget {
    id: string;
    dashboardId: string;
    type: 'scada-point' | 'background-svg' | 'line-chart' | 'gauge' | string;
    config: IWidgetConfig;
    posX: number;
    posY: number;
    posW: number;
    posH: number;
}

export interface IDotParticle {
    el: SVGCircleElement;
    offset: number;
    totalLen: number;
    speed: number;
    born: number;
}

export interface IDotAnimationState {
    dots: IDotParticle[];
    pathEl: SVGPathElement | SVGLineElement | SVGPolylineElement;
    reverse: boolean;
    fast: boolean;
    color: string;
    lastTime: number;
}
