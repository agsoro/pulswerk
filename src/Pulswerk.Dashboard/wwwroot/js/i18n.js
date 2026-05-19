"use strict";
// @ts-nocheck
const TRANSLATIONS = {
    'en': {
        'nav_home': 'Home',
        'nav_dashboards': 'Dashboards',
        'nav_assets': 'Assets',
        'nav_telemetries': 'Data Points',
        'nav_inventory': 'Inventory',
        'nav_connections': 'Connections',
        'nav_alarms': 'Alarms',
        'nav_logs': 'Logs',
        'nav_heartbeat': 'Heartbeat',
        'nav_config': 'Configuration',
        'status_connected': 'Connected to',
        'status_online': 'online',
        'status_offline': 'offline',
        'btn_save': 'Save',
        'btn_cancel': 'Cancel',
        'btn_edit': 'Edit',
        'btn_delete': 'Delete',
        'btn_add': 'Add',
        'btn_refresh': 'Refresh',
        'search_placeholder': 'Search...',
        'no_data': 'No data available',
        'loading': 'Loading...',
        'last_seen': 'Last seen',
        'uptime': 'Uptime',
        'throughput': 'Throughput',
        'storage': 'Storage',
        'active_alarms': 'Active Alarms',
        'points': 'Points',
        'devices': 'Devices',
        'trend': 'Trend',
        'properties': 'Properties',
        'edit_value': 'Edit Value',
        'history': 'History',
        'consumption': 'Consumption',
        'hourly': 'Hourly',
        'daily': 'Daily',
        'monthly': 'Monthly',
        'yearly': 'Yearly',
        'settings': 'Settings',
        'diagnostics': 'Diagnostics',
        'uptime_d': 'Days',
        'uptime_h': 'Hrs',
        'uptime_m': 'Min',
        'uptime_s': 'Sec',
        'total_devices': 'Total Devices',
        'online_devices': 'Online Devices',
        'offline_devices': 'Offline Devices',
        'active_alarms_total': 'Active Alarms',
        'version': 'Version',
        'log_buffer': 'Log Buffer',
        'updates_min': 'Updates/Min',
        'refresh_hint': 'Auto-refresh every 3s',
        'severity_critical': 'Critical',
        'severity_major': 'Major',
        'severity_minor': 'Minor',
        'severity_warning': 'Warning',
        'severity_total': 'Total',
        'favorites': 'Favorites',
        'no_favorites': 'No favorites yet.',
        'favorites_hint': 'Go to {0} and click {1} to pin a point.',
        'fav_dashboards': 'Favorite Dashboards',
        'no_fav_dashboards': 'No favorite dashboards yet.',
        'fav_dashboards_hint': 'Go to {0} and click the {1} to pin one.',
        'loading_tree': 'Loading assets...',
        'select_asset': 'Select an asset',
        'select_node_hint': 'Select a node from the tree to view data points',
        'consumption_hourly': 'Consumption (Hourly)',
        'consumption_daily': 'Consumption (Daily)',
        'consumption_monthly': 'Consumption (Monthly)',
        'consumption_yearly': 'Consumption (Yearly)',
        'hour': 'Hour',
        'system_heartbeat': 'System Heartbeat',
        'system_heartbeat_desc': 'Real-time health monitoring and performance statistics.',
        'system_operational': 'System Operational',
        'scanning': 'Scanning Discovery/History...',
        'system_info': 'System Information',
        'environment': 'Environment',
        'production': 'Production',
        'data_point_keys': 'Data Point Keys',
        'total_updates_processed': 'Total updates processed',
        'update_throughput': 'Update Throughput',
        'live_window': 'Live (60s window)',
        'no_keys': 'No keys configured',
        'no_key': 'No key configured',
        'confirm_remove_widget': 'Remove this widget?',
        'last_hour': 'Last Hour',
        'last_4h': 'Last 4 Hours',
        'last_24h': 'Last 24 Hours',
        'last_7d': 'Last 7 Days',
        'last_14d': 'Last 14 Days',
        'last_30d': 'Last 30 Days',
        'last_year': 'Last Year',
        'no_history_data': 'No historical data found',
        'current_value': 'Current Value',
        'new_value': 'New Value',
        'btn_save_changes': 'Save Changes',
        'prop_name': 'Property',
        'prop_value': 'Value',
        'no_properties_found': 'No extended properties found.',
        'error_loading_properties': 'Error loading properties.',
        'weekly_schedule': 'Weekly Schedule',
        'schedule_configuration': 'Schedule Configuration',
        'edit': 'Edit',
        'cancel': 'Cancel',
        'save': 'Save'
    },
    'de': {
        'nav_home': 'Übersicht',
        'nav_dashboards': 'Dashboards',
        'nav_assets': 'Anlagen',
        'nav_telemetries': 'Datenpunkte',
        'nav_inventory': 'Bestandsliste',
        'nav_connections': 'Verbindungen',
        'nav_alarms': 'Alarme',
        'nav_logs': 'Protokolle',
        'nav_heartbeat': 'Diagnose',
        'nav_config': 'Konfiguration',
        'status_connected': 'Verbunden mit',
        'status_online': 'online',
        'status_offline': 'offline',
        'btn_save': 'Speichern',
        'btn_cancel': 'Abbrechen',
        'btn_edit': 'Bearbeiten',
        'btn_delete': 'Löschen',
        'btn_add': 'Hinzufügen',
        'btn_refresh': 'Aktualisieren',
        'search_placeholder': 'Suchen...',
        'no_data': 'Keine Daten verfügbar',
        'loading': 'Lade...',
        'last_seen': 'Zuletzt gesehen',
        'uptime': 'Betriebszeit',
        'throughput': 'Durchsatz',
        'storage': 'Speicher',
        'active_alarms': 'Aktive Alarme',
        'points': 'Datenpunkte',
        'devices': 'Geräte',
        'trend': 'Verlauf',
        'properties': 'Eigenschaften',
        'edit_value': 'Wert bearbeiten',
        'history': 'Historie',
        'consumption': 'Verbrauch',
        'hourly': 'Stündlich',
        'daily': 'Täglich',
        'monthly': 'Monatlich',
        'yearly': 'Jährlich',
        'settings': 'Einstellungen',
        'diagnostics': 'Diagnose',
        'uptime_d': 'Tage',
        'uptime_h': 'Std',
        'uptime_m': 'Min',
        'uptime_s': 'Sek',
        'total_devices': 'Geräte Gesamt',
        'online_devices': 'Geräte Online',
        'offline_devices': 'Geräte Offline',
        'active_alarms_total': 'Aktive Alarme',
        'version': 'Version',
        'log_buffer': 'Log-Puffer',
        'updates_min': 'Updates/Min',
        'refresh_hint': 'Automatische Aktualisierung alle 3 Sek.',
        'severity_critical': 'Kritisch',
        'severity_major': 'Hoch',
        'severity_minor': 'Gering',
        'severity_warning': 'Warnung',
        'severity_total': 'Gesamt',
        'favorites': 'Favoriten',
        'no_favorites': 'Noch keine Favoriten.',
        'favorites_hint': 'Gehen Sie zu {0} und klicken Sie auf {1}, um einen Punkt anzuheften.',
        'fav_dashboards': 'Favorisierte Dashboards',
        'no_fav_dashboards': 'Noch keine favorisierten Dashboards.',
        'fav_dashboards_hint': 'Gehen Sie zu {0} und klicken Sie auf das {1}, um eines anzuheften.',
        'loading_tree': 'Struktur wird geladen...',
        'select_asset': 'Wählen Sie eine Anlage aus',
        'select_node_hint': 'Wählen Sie einen Knoten aus dem Baum aus, um Datenpunkte anzuzeigen',
        'consumption_hourly': 'Verbrauch (Stündlich)',
        'consumption_daily': 'Verbrauch (Täglich)',
        'consumption_monthly': 'Verbrauch (Monatlich)',
        'consumption_yearly': 'Verbrauch (Jährlich)',
        'hour': 'Stunde',
        'system_heartbeat': 'System Diagnose',
        'system_heartbeat_desc': 'Echtzeit-Zustandsüberwachung und Leistungsstatistiken.',
        'system_operational': 'System Betriebsbereit',
        'scanning': 'Suche/Historie wird geladen...',
        'system_info': 'System Informationen',
        'environment': 'Umgebung',
        'production': 'Produktion',
        'data_point_keys': 'Datenpunkt-Keys',
        'total_updates_processed': 'Verarbeitete Updates Gesamt',
        'update_throughput': 'Update Durchsatz',
        'live_window': 'Live (60s Fenster)',
        'no_keys': 'Keine Daten-Keys konfiguriert',
        'no_key': 'Kein Daten-Key konfiguriert',
        'confirm_remove_widget': 'Diesen Widget entfernen?',
        'last_hour': 'Letzte Stunde',
        'last_4h': 'Letzte 4 Stunden',
        'last_24h': 'Letzte 24 Stunden',
        'last_7d': 'Letzte 7 Tage',
        'last_14d': 'Letzte 14 Tage',
        'last_30d': 'Letzte 30 Tage',
        'last_year': 'Letztes Jahr',
        'no_history_data': 'Keine historischen Daten gefunden',
        'current_value': 'Aktueller Wert',
        'new_value': 'Neuer Wert',
        'btn_save_changes': 'Änderungen speichern',
        'prop_name': 'Eigenschaft',
        'prop_value': 'Wert',
        'no_properties_found': 'Keine erweiterten Eigenschaften gefunden.',
        'error_loading_properties': 'Fehler beim Laden der Eigenschaften.',
        'weekly_schedule': 'Wochenplan',
        'schedule_configuration': 'Zeitplan-Konfiguration',
        'edit': 'Bearbeiten',
        'cancel': 'Abbrechen',
        'save': 'Speichern'
    }
};
let currentLang = 'en';
function initI18n(lang) {
    const saved = localStorage.getItem('plswk_lang');
    if (saved && TRANSLATIONS[saved]) {
        currentLang = saved;
    }
    else if (lang && TRANSLATIONS[lang]) {
        currentLang = lang;
    }
    else {
        const browserLang = navigator.language.split('-')[0];
        if (TRANSLATIONS[browserLang])
            currentLang = browserLang;
    }
    applyTranslations();
}
function setLanguage(lang) {
    if (!TRANSLATIONS[lang])
        return;
    currentLang = lang;
    localStorage.setItem('plswk_lang', lang);
    applyTranslations();
}
function t(key) {
    if (!key)
        return '';
    return (TRANSLATIONS[currentLang] && TRANSLATIONS[currentLang][key]) || key;
}
function applyTranslations() {
    document.querySelectorAll('[data-i18n]').forEach(el => {
        const key = el.dataset.i18n;
        if (!key)
            return;
        if (el.tagName === 'INPUT' && (el.type === 'text' || el.type === 'search')) {
            el.placeholder = t(key);
        }
        else {
            el.textContent = t(key);
        }
    });
    document.querySelectorAll('[data-i18n-title]').forEach(el => {
        const titleKey = el.dataset.i18nTitle;
        if (titleKey)
            el.title = t(titleKey);
    });
    // Highlight active language button
    document.querySelectorAll('[id^="lang-"]').forEach(btn => {
        const lang = btn.id.replace('lang-', '');
        btn.classList.toggle('active', lang === currentLang);
    });
}
window.initI18n = initI18n;
window.setLanguage = setLanguage;
window.t = t;
window.applyTranslations = applyTranslations;
