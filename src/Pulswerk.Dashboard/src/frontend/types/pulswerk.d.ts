/**
 * pulswerk.d.ts – Comprehensive complex types and ambient declarations
 * for the Pulswerk Dashboard ecosystem.
 */

// ── DOMAIN MODELS ───────────────────────────────────────────────────────────

interface ITimeWindowConfig {
    mode: 'realtime' | 'history' | string;
    realtimeMs: number;
    startTs?: number;
    endTs?: number;
}

interface IAnimationRule {
    telemetryKey: string;
    elementId: string;
    formula: string;
    styles: string[];
}

interface IWidgetConfig {
    title?: string;
    keys?: string[];
    key?: string;
    chartType?: 'line' | 'bar' | string;
    stacked?: boolean;
    showLegend?: boolean;
    layout?: 'vertical' | 'horizontal' | string;
    svg?: string;
    color?: string;
    posX?: number;
    posY?: number;
    posW?: number;
    posH?: number;
    baseW?: number;
    baseH?: number;
    zoom?: number;
    anchorSvg?: string;
    anchorEdge?: string;
    anchorW?: number;
    anchorH?: number;
    animationRules?: IAnimationRule[];
    [key: string]: any;
}

interface IWidget {
    id: string;
    dashboardId?: string;
    title?: string;
    type: 'timeseries' | 'latest-values' | 'single-value' | 'scada-point' | 'background-svg' | string;
    x: number;
    y: number;
    w: number;
    h: number;
    config: IWidgetConfig;
}

interface IDashboard {
    id: string;
    name: string;
    description?: string;
    updatedAt?: string;
    timewindow?: ITimeWindowConfig;
    widgets?: IWidget[];
}

interface ITelemetryMeta {
    key: string;
    name: string;
    fullName?: string;
    units?: string;
    type?: string;
    parentId?: string;
    parentPath?: Array<{ id: string; name: string }>;
    isWritable?: boolean;
    enumValues?: Record<string, string> | null;
    [key: string]: any;
}

interface IUserIdentity {
    authenticated: boolean;
    user: string;
    name: string;
    email?: string;
    groups?: string[];
    isDefault?: boolean;
    canWriteValue: boolean;
    canAckAlarm: boolean;
    canEditDashboard: boolean;
    canEditFavorites: boolean;
}

interface ITimeWindowSelector {
    getRange: () => { startTs: number; endTs: number; mode: string; realtimeMs?: number };
    setPreset: (ms: number) => void;
    updateLabel: () => void;
    destroy: () => void;
    mode: string;
    realtimeMs: number;
}

interface IClientErrorPayload {
    msg: string;
    source: string;
    line: number;
    col: number;
    stack?: string;
    page?: string;
}

// ── AMBIENT GLOBAL DECLARATIONS ─────────────────────────────────────────────

declare var GridStack: any;
declare var ApexCharts: any;
declare var $: any;

declare var currentLang: string;
declare var allKeys: ITelemetryMeta[];
declare var pw_fav: {
    get: (key: string) => any;
    set: (key: string, val: any) => void;
};

declare var _currentUser: IUserIdentity | null;
declare var pwCanWriteValue: boolean;
declare var pwCanAckAlarm: boolean;
declare var pwCanEditDashboard: boolean;
declare var pwCanEditFavorites: boolean;

declare var Grid: any;
declare var dashboard: IDashboard | null;
declare var grid: any;
declare var isEditing: boolean;
declare var charts: Record<string, any>;
declare var pollTimer: any;
declare var pendingSvgContent: string;
declare var selectedType: string;
declare var editingWidgetId: string | null;
declare var activeKeyOrder: string[];
declare var isKeySelectorOpen: boolean;
declare var dashTw: ITimeWindowSelector | null;
declare var pendingRenders: Set<string>;

// Global Functions
declare function initI18n(lang?: string): void;
declare function setLanguage(lang: string): void;
declare function t(key: string): string;
declare function applyTranslations(): void;

declare function isBinary(type: string): boolean;
declare function isMultiState(type: string): boolean;
declare function isSchedule(type: string): boolean;
declare function formatDisplay(val: any, type: string, decimals?: number): string;
declare function parseDisplay(displayVal: string, type: string): number;
declare function valueClass(type: string): string;
declare var PulswerkValue: {
    isBinary: typeof isBinary;
    isMultiState: typeof isMultiState;
    isSchedule: typeof isSchedule;
    formatDisplay: typeof formatDisplay;
    parseDisplay: typeof parseDisplay;
    valueClass: typeof valueClass;
};

declare function formatNumber(val: any, decimals?: number): string;
declare function getPointIcon(type: string): string;
declare function toggleFavorite(key: string, btn?: HTMLElement): void;
declare function updateStarState(key: string, btn?: HTMLElement): void;

declare function createTimeWindowSelector(container: HTMLElement, opts?: any): ITimeWindowSelector;

declare function resolveKeyMeta(key: string): ITelemetryMeta;
declare function ensureKeysMeta(): Promise<void>;
declare function fetchLatestValues(keys: string | string[]): Promise<Record<string, any>>;
declare function esc(s: string | null | undefined): string;
declare function friendlyName(key: string): string;
declare function getUserInitials(name: string): string;
declare function loadUserIdentity(): Promise<void>;
declare function applyRightsToUI(u: IUserIdentity): void;
declare function updateUserBadge(u: IUserIdentity): void;
declare function toggleUserPopover(e?: Event): void;

declare function openHistory(key: string): Promise<void>;
declare function closeHistory(): void;
declare function reloadHistory(): Promise<void>;
declare function renderChart(data: any[]): void;
declare function startHistoryRefresh(): void;
declare function stopHistoryRefresh(): void;
declare function refreshHistoryData(): Promise<void>;

declare function openEdit(key: string): Promise<void>;
declare function closeEdit(): void;
declare function step(n: number): void;
declare function updateBoolLabel(): void;
declare function submitEdit(e?: Event): Promise<void>;

declare function openProperties(key: string): Promise<void>;
declare function closeProperties(): void;
declare function renderModalBreadcrumb(id: string, path: any[]): void;

declare function openScheduleView(key: string): Promise<void>;
declare function closeSchedule(): void;
declare function formatScheduleValue(val: any): any;
declare function renderScheduleValueInput(dayIndex: number, entryIndex: number, value: any): string;
declare function renderSchedule(): void;
declare function toggleScheduleEdit(edit: boolean): void;
declare function updateScheduleEntry(dayIdx: number, entryIdx: number, field: string, value: any): void;
declare function addScheduleEntry(dayIdx: number): void;
declare function removeScheduleEntry(dayIdx: number, entryIdx: number): void;
declare function saveSchedule(): Promise<void>;

declare function initDashboards(initial: IDashboard | null, editMode: boolean): void;
declare function loadList(): Promise<void>;
declare function toggleFavoriteDash(id: string): void;
declare function createDashboard(): void;
declare function confirmCreate(): Promise<void>;
declare function deleteDash(id: string): Promise<void>;
declare function showDashboard(): Promise<void>;
declare function initGrid(): void;
declare function enterEditMode(): void;
declare function cancelEdit(): void;
declare function saveDashboard(): Promise<void>;
declare function startPolling(): void;
declare function refreshAllWidgets(): void;
declare function keyName(key: string): string;
declare function hexToRgba(hex: string, a: number): string;
declare function timeAgo(isoStr: string): string;
declare function slugify(text: string): string;
declare function updateHistoryLiveValue(key: string, value: any): void;

declare function openAddWidget(): Promise<void>;
declare function editWidget(wid: string): void;
declare function closeAddWidget(): void;
declare function selectWidgetType(el: HTMLElement | null): void;
declare function confirmAddWidget(): void;
declare function removeWidget(wid: string): void;
declare function duplicateWidget(wid: string): void;

declare function loadKeyPicker(): Promise<void>;
declare function openKeySelector(): void;
declare function closeKeySelector(e?: Event): void;
declare function cancelKeySelector(e?: Event): void;
declare function dismissKeySelector(): void;
declare function filterKeys(): void;
declare function renderKeyList(keys: ITelemetryMeta[], isSortedView?: boolean, checkedKeys?: string[]): void;
declare function removeKeyFromSelection(key: string): void;
declare function moveKey(key: string, dir: number): void;
declare function handleKeyToggle(input: HTMLInputElement): void;

declare function renderAllWidgets(): void;
declare function addWidgetToGrid(w: IWidget): void;
declare function renderWidgetContent(w: IWidget): void;
declare function renderTimeseries(w: IWidget, body: HTMLElement, cfg: IWidgetConfig): Promise<void>;
declare function renderLatestValues(w: IWidget, body: HTMLElement, cfg: IWidgetConfig): Promise<void>;
declare function updateLatestValues(w: IWidget, cfg: IWidgetConfig): Promise<void>;
declare function renderSingleValue(w: IWidget, body: HTMLElement, cfg: IWidgetConfig): Promise<void>;
declare function updateSingleValue(w: IWidget, cfg: IWidgetConfig): Promise<void>;
declare function miniSparkSvg(val: number, color: string): string;
declare function getTimeRange(): { startTs: number; endTs: number };

declare function svgColorFilter(hex: string): string;
declare function renderBackgroundSvg(w: IWidget): void;
declare function expandCanvasToFit(): void;
declare function setSvgZoom(wid: string, val: any): void;
declare function initSvgWidgetDrag(el: HTMLElement, w: IWidget): void;
declare function renderScadaPoint(w: IWidget): void;
declare function getSvgContentBox(containerEl: HTMLElement): { left: number; top: number; width: number; height: number };
declare function centerDotOffset(edge: string, w: number, h: number): { x: number; y: number };
declare function positionScadaPoint(el: HTMLElement, cfg: IWidgetConfig): void;
declare function repositionAnchoredPoints(): void;
declare function findOverlappingSvg(pointEl: HTMLElement): HTMLElement | null;
declare function setPointEdge(wid: string, edge: string, dotEl: HTMLElement): void;
declare function updateEdgeDotVisibility(pointEl: HTMLElement): void;
declare function updateScadaPointValues(w: IWidget): Promise<void>;
declare function renderScadaValues(wid: string, keys: string[], data: any): void;
declare function updateAllScadaPoints(): Promise<void>;
declare function showScadaPopup(key: string, triggerEl: HTMLElement): Promise<void>;
declare function hideScadaPopup(): void;
declare function initScadaPointDrag(el: HTMLElement, w?: IWidget): void;
declare function handleSvgFileSelect(input: HTMLInputElement): void;
declare function openDrawioEditor(svgContent: string, onSave: (newSvg: string) => void): void;
declare function closeDrawioEditor(): void;
declare function fitWidgetToSvg(w: IWidget): void;
declare function editBackgroundSvg(wid: string): void;
declare function createSvgInDrawio(): void;
declare function parseSvgElementIds(svgContent: string): string[];
declare function evaluateCondition(value: any, condition: string): boolean;
declare function applyAnimationRules(svgWidgetEl: HTMLElement | null, rules: IAnimationRule[], data: any): void;
declare function updateAllSvgAnimations(): Promise<void>;
declare function updateDotAnimations(): void;
declare function toggleIdPicker(wid: string): void;
declare function closeIdPicker(): void;
declare function highlightPickerElement(idx: number, wid: string): void;
declare function unhighlightPickerElement(idx: number, wid: string): void;
declare function clickPickerElement(idx: number, wid: string): void;
declare function confirmRenameElement(): Promise<void>;
declare function cancelRenameElement(): void;

interface Window {
    DrawioCodec?: any;
    ConditionEvaluator?: any;
    pwCanWriteValue?: boolean;
    pwCanAckAlarm?: boolean;
    pwCanEditDashboard?: boolean;
    pwCanEditFavorites?: boolean;
    _currentUser?: IUserIdentity | null;
    pw_fav?: any;
    openKeySelector?: () => void;
    closeKeySelector?: (e?: Event) => void;
    cancelKeySelector?: (e?: Event) => void;
    filterKeys?: () => void;
    [key: string]: any;
}
