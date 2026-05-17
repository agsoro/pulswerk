// Store class for dashboard state management
export class DashboardStore {
    static _dashboard = null;
    static _grid = null;
    static _isEditing = false;
    static _charts = {};
    static _pollTimer = null;
    static _pendingSvgContent = '';
    static _selectedType = 'timeseries';
    static _editingWidgetId = null;
    static _activeKeyOrder = [];
    static _isKeySelectorOpen = false;
    static _dashTw = null;
    static _pendingRenders = new Set();
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
