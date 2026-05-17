import { IDashboard } from './types';

// Store class for dashboard state management
export class DashboardStore {
    private static _dashboard: IDashboard | null = null;
    private static _grid: any = null;
    private static _isEditing: boolean = false;
    private static _charts: Record<string, any> = {};
    private static _pollTimer: any = null;
    private static _pendingSvgContent: string = '';
    private static _selectedType: string = 'timeseries';
    private static _editingWidgetId: string | null = null;
    private static _activeKeyOrder: string[] = [];
    private static _isKeySelectorOpen: boolean = false;
    private static _dashTw: any = null;
    private static _pendingRenders = new Set<string>();

    static get dashboard() { return this._dashboard; }
    static set dashboard(v) { this._dashboard = v; }

    static get grid() { return this._grid; }
    static set grid(v) { this._grid = v; }

    static get isEditing() { return this._isEditing; }
    static set isEditing(v) { this._isEditing = v; }

    static get charts() { return this._charts; }
    static set charts(v) { this._charts = v; }

    static get pollTimer() { return this._pollTimer; }
    static set pollTimer(v) { this._pollTimer = v; }

    static get pendingSvgContent() { return this._pendingSvgContent; }
    static set pendingSvgContent(v) { this._pendingSvgContent = v; }

    static get selectedType() { return this._selectedType; }
    static set selectedType(v) { this._selectedType = v; }

    static get editingWidgetId() { return this._editingWidgetId; }
    static set editingWidgetId(v) { this._editingWidgetId = v; }

    static get activeKeyOrder() { return this._activeKeyOrder; }
    static set activeKeyOrder(v) { this._activeKeyOrder = v; }

    static get isKeySelectorOpen() { return this._isKeySelectorOpen; }
    static set isKeySelectorOpen(v) { this._isKeySelectorOpen = v; }

    static get dashTw() { return this._dashTw; }
    static set dashTw(v) { this._dashTw = v; }

    static get pendingRenders() { return this._pendingRenders; }
}
