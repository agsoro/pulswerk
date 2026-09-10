import { jsx as _jsx, jsxs as _jsxs } from "preact/jsx-runtime";
import { render } from 'preact';
import { useState, useEffect, useRef } from 'preact/hooks';
// Bootstrapping legacy files
import './base';
import './app-utils';
import './timewindow';
import './modals';
import './dashboards.entry';
// Imports of pages
import { FavoritesPage } from './pages/favorites';
import { DashboardsPage } from './pages/dashboards';
import { AssetsPage } from './pages/assets';
import { TelemetryListPage } from './pages/telemetry';
import { ConnectionsPage } from './pages/connections';
import { AlarmsPage } from './pages/alarms';
import { LogsPage } from './pages/logs';
import { HeartbeatPage } from './pages/heartbeat';
import { WallboxesPage } from './pages/wallboxes';
import { BillingPage } from './pages/billing';
import { TrajectoryPage } from './pages/trajectory';
import { TelemetryCrudPage } from './pages/telemetryCrud';
// Imports of i18n
import { initI18n, setLanguage, t, currentLang } from './i18n';
export function App() {
    const [path, setPath] = useState(window.location.pathname);
    const [lang, setLang] = useState(() => {
        initI18n();
        return currentLang;
    });
    const [user, setUser] = useState(null);
    const [popoverOpen, setPopoverOpen] = useState(false);
    const [mobileDrawerOpen, setMobileDrawerOpen] = useState(false);
    const popoverRef = useRef(null);
    // Client-side navigation helper
    const navigate = (url) => {
        window.history.pushState(null, '', url);
        setPath(url.split('?')[0]);
        setMobileDrawerOpen(false);
    };
    // Global click listener to intercept relative anchors
    useEffect(() => {
        const handleLinkClick = (e) => {
            const target = e.target;
            const anchor = target.closest('a');
            if (anchor && anchor.href && anchor.host === window.location.host) {
                const url = new URL(anchor.href);
                if (url.pathname.startsWith('/plswk')) {
                    e.preventDefault();
                    navigate(url.pathname + url.search);
                }
            }
        };
        document.addEventListener('click', handleLinkClick);
        return () => document.removeEventListener('click', handleLinkClick);
    }, []);
    // popstate listener for back/forward buttons
    useEffect(() => {
        const handlePopState = () => {
            setPath(window.location.pathname);
            setMobileDrawerOpen(false);
        };
        window.addEventListener('popstate', handlePopState);
        return () => window.removeEventListener('popstate', handlePopState);
    }, []);
    // Load user identity & key metadata
    const loadUser = async () => {
        try {
            const res = await fetch('/plswk/api/user/identity');
            if (res.ok) {
                const data = await res.json();
                setUser(data);
                // Inject credentials globally for compatibility with legacy JS (modals.js etc.)
                window._currentUser = data;
                window.pwCanWriteValue = data.permissions.canWriteValue;
                window.pwCanAckAlarm = data.permissions.canAckAlarm;
                window.pwCanEditDashboard = data.permissions.canEditDashboard;
                window.pwCanEditFavorites = data.permissions.canEditFavorites;
                window.pwCanEditConfig = data.permissions.canEditConfig;
            }
        }
        catch (e) {
            console.error("Failed to load user identity:", e);
        }
    };
    useEffect(() => {
        const init = async () => {
            await loadUser();
        };
        init();
        // When the system config page saves module changes (same or other tab),
        // re-fetch identity so the nav reflects the new module state immediately.
        const handleStorageChange = (e) => {
            if (e.key === 'pw_modules_updated')
                loadUser();
        };
        window.addEventListener('storage', handleStorageChange);
        return () => window.removeEventListener('storage', handleStorageChange);
    }, []);
    // Close user popover when clicking outside
    useEffect(() => {
        const handleOutsideClick = (e) => {
            if (popoverOpen && popoverRef.current && !popoverRef.current.contains(e.target)) {
                const badge = document.getElementById('userBadge');
                if (badge && badge.contains(e.target))
                    return;
                setPopoverOpen(false);
            }
        };
        document.addEventListener('mousedown', handleOutsideClick);
        return () => document.removeEventListener('mousedown', handleOutsideClick);
    }, [popoverOpen]);
    const handleLanguageChange = (newLang) => {
        setLanguage(newLang);
        setLang(newLang);
    };
    const togglePopover = (e) => {
        e.stopPropagation();
        setPopoverOpen(prev => !prev);
    };
    // Simple Route Resolver
    const searchParams = new URLSearchParams(window.location.search);
    let routePath = path.replace(/\/$/, '') || '/plswk';
    if (routePath.startsWith('/plswk')) {
        routePath = routePath.slice('/plswk'.length);
    }
    if (!routePath)
        routePath = '/';
    let pageComponent = null;
    let pageTitle = 'Home';
    const dashboardDetailMatch = routePath.match(/^\/Dashboards\/([^/]+)(?:\/([^/]+))?$/);
    if (dashboardDetailMatch && user?.modules?.dashboards !== false && user?.permissions?.canAccessDashboards !== false) {
        pageTitle = 'Dashboards';
        pageComponent = (_jsx(DashboardsPage, { dashboardId: dashboardDetailMatch[1], slug: dashboardDetailMatch[2] || undefined }));
    }
    else if (routePath === '/Dashboards' && user?.modules?.dashboards !== false && user?.permissions?.canAccessDashboards !== false) {
        pageTitle = 'Dashboards';
        pageComponent = _jsx(DashboardsPage, {});
    }
    else if (routePath === '/Assets' && user?.modules?.assets !== false && user?.permissions?.canAccessAssets !== false) {
        pageTitle = 'Assets';
        pageComponent = _jsx(AssetsPage, { initialNodeId: searchParams.get('node') });
    }
    else if ((routePath === '/TelemetryList' || routePath === '/AssetsList') && user?.modules?.telemetry !== false && user?.permissions?.canAccessTelemetry !== false) {
        pageTitle = 'Data Points';
        pageComponent = _jsx(TelemetryListPage, {});
    }
    else if (routePath === '/Connections' && user?.modules?.connections !== false && user?.permissions?.canAccessConnections !== false) {
        pageTitle = 'Connections';
        pageComponent = _jsx(ConnectionsPage, { initialConnId: searchParams.get('conn') });
    }
    else if (routePath === '/Alarms' && user?.modules?.alarms !== false && user?.permissions?.canAccessAlarms !== false) {
        pageTitle = 'Active Alarms';
        pageComponent = _jsx(AlarmsPage, {});
    }
    else if (routePath === '/Logs' && user?.modules?.logs !== false && user?.permissions?.canAccessLogs !== false) {
        pageTitle = 'System Logs';
        pageComponent = _jsx(LogsPage, {});
    }
    else if (routePath === '/Heartbeat' && user?.modules?.heartbeat !== false && user?.permissions?.canAccessHeartbeat !== false) {
        pageTitle = 'System Heartbeat';
        pageComponent = _jsx(HeartbeatPage, {});
    }
    else if (routePath === '/Wallboxes' && user?.modules?.wallbox !== false && user?.permissions?.canAccessWallbox !== false) {
        pageTitle = 'Wallboxes';
        pageComponent = _jsx(WallboxesPage, {});
    }
    else if (routePath === '/Billing' && user?.modules?.billing !== false && user?.permissions?.canAccessBilling !== false) {
        pageTitle = 'Billing';
        pageComponent = _jsx(BillingPage, {});
    }
    else if (routePath === '/Trajectory' && user?.modules?.ems !== false && user?.permissions?.canAccessEms !== false) {
        pageTitle = 'Trajectory';
        pageComponent = _jsx(TrajectoryPage, {});
    }
    else if (routePath === '/TelemetryCrud' && user?.modules?.historicalData !== false && user?.permissions?.canAccessHistoricalData !== false) {
        pageTitle = 'Historical Data';
        pageComponent = _jsx(TelemetryCrudPage, { initialKey: searchParams.get('key') });
    }
    else {
        pageTitle = 'Home';
        pageComponent = _jsx(FavoritesPage, {});
    }
    const getUserInitials = (name) => {
        if (!name || name === 'Public' || name === 'public')
            return '';
        const parts = name.trim().split(/\s+/);
        if (parts.length >= 2)
            return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
        return name.substring(0, 2).toUpperCase();
    };
    const userInitials = user ? getUserInitials(user.name) : '';
    const isUserAuth = user && user.user !== 'Public';
    const navItems = [
        { id: 'home', path: '/plswk/', icon: 'fa-home', labelKey: 'nav_home', title: 'Home' },
        user?.modules?.dashboards !== false && user?.permissions?.canAccessDashboards !== false ? { id: 'dashboards', path: '/plswk/Dashboards', icon: 'fa-th-large', labelKey: 'nav_dashboards', title: 'Dashboards' } : null,
        user?.modules?.assets !== false && user?.permissions?.canAccessAssets !== false ? { id: 'assets', path: '/plswk/Assets', icon: 'fa-sitemap', labelKey: 'nav_assets', title: 'Assets' } : null,
        user?.modules?.telemetry !== false && user?.permissions?.canAccessTelemetry !== false ? { id: 'telemetry', path: '/plswk/TelemetryList', icon: 'fa-table', labelKey: 'nav_telemetries', title: 'Data Points' } : null,
        user?.modules?.connections !== false && user?.permissions?.canAccessConnections !== false ? { id: 'connections', path: '/plswk/Connections', icon: 'fa-network-wired', labelKey: 'nav_connections', title: 'Connections' } : null,
        user?.modules?.wallbox !== false && user?.permissions?.canAccessWallbox !== false ? { id: 'wallboxes', path: '/plswk/Wallboxes', icon: 'fa-charging-station', labelKey: 'nav_wallboxes', title: 'Wallboxes' } : null,
        user?.modules?.billing !== false && user?.permissions?.canAccessBilling !== false ? { id: 'billing', path: '/plswk/Billing', icon: 'fa-file-invoice-dollar', labelKey: 'nav_billing', title: 'Billing' } : null,
        user?.modules?.ems !== false && user?.permissions?.canAccessEms !== false ? { id: 'trajectory', path: '/plswk/Trajectory', icon: 'fa-chart-line', labelKey: 'nav_trajectory', title: 'Trajectory' } : null,
        user?.modules?.historicalData !== false && user?.permissions?.canAccessHistoricalData !== false ? { id: 'telemetryCrud', path: '/plswk/TelemetryCrud', icon: 'fa-history', labelKey: 'nav_historical_data', title: 'Historical Data' } : null,
        user?.modules?.alarms !== false && user?.permissions?.canAccessAlarms !== false ? { id: 'alarms', path: '/plswk/Alarms', icon: 'fa-bell', labelKey: 'nav_alarms', title: 'Alarms' } : null,
        user?.modules?.logs !== false && user?.permissions?.canAccessLogs !== false ? { id: 'logs', path: '/plswk/Logs', icon: 'fa-terminal', labelKey: 'nav_logs', title: 'Logs' } : null,
        user?.modules?.heartbeat !== false && user?.permissions?.canAccessHeartbeat !== false ? { id: 'heartbeat', path: '/plswk/Heartbeat', icon: 'fa-heartbeat', labelKey: 'nav_heartbeat', title: 'Heartbeat' } : null
    ].filter(Boolean);
    // Determine current navigation tab for active highlighting
    const getActiveNavId = () => {
        if (path.startsWith('/plswk/Dashboards'))
            return 'dashboards';
        if (path.startsWith('/plswk/Assets'))
            return 'assets';
        if (path.startsWith('/plswk/TelemetryList') || path.startsWith('/plswk/AssetsList'))
            return 'telemetry';
        if (path.startsWith('/plswk/Connections'))
            return 'connections';
        if (path.startsWith('/plswk/Wallboxes'))
            return 'wallboxes';
        if (path.startsWith('/plswk/Billing'))
            return 'billing';
        if (path.startsWith('/plswk/Trajectory'))
            return 'trajectory';
        if (path.startsWith('/plswk/TelemetryCrud'))
            return 'telemetryCrud';
        if (path.startsWith('/plswk/Alarms'))
            return 'alarms';
        if (path.startsWith('/plswk/Logs'))
            return 'logs';
        if (path.startsWith('/plswk/Heartbeat'))
            return 'heartbeat';
        return 'home';
    };
    const activeNavId = getActiveNavId();
    return (_jsxs("div", { class: "w-full flex min-h-screen", children: [_jsxs("aside", { class: "hidden md:flex w-[72px] bg-slate-800 border-r border-slate-700 py-6 px-3 flex-col items-center fixed h-screen z-50 transition-all duration-300 ease-in-out overflow-hidden whitespace-nowrap", "data-testid": "sidebar", children: [_jsx("a", { href: "/plswk/", class: "flex items-center mb-10 no-underline w-full pl-2 overflow-hidden", "data-testid": "sidebar-brand", children: _jsx("img", { src: "/plswk/img/pulswerk_logo_sm.png", alt: "Pulswerk", class: "h-8 min-w-[170px] brightness-0 invert" }) }), _jsx("ul", { class: "nav-links list-none flex flex-col gap-2 w-full", "data-testid": "nav-links", children: navItems.map(item => {
                            const isActive = activeNavId === item.id;
                            return (_jsx("li", { children: _jsxs("a", { href: item.path, class: `flex items-center py-3 px-[0.85rem] text-slate-400 no-underline rounded-lg transition-all duration-200 font-medium w-full whitespace-nowrap hover:bg-sky-400/10 hover:text-sky-400 ${isActive ? '!bg-sky-400/10 !text-sky-400' : ''}`, "data-testid": `nav-${item.id}`, children: [_jsx("i", { class: `fas ${item.icon} w-5 text-xl flex-shrink-0 text-center` }), _jsx("span", { class: "nav-label nav-text opacity-0 transition-opacity duration-200 ml-4", children: t(item.labelKey) })] }) }, item.id));
                        }) }), _jsxs("footer", { class: "mt-auto pt-4 pb-2 px-2 text-xs text-slate-400 border-t border-slate-700 w-full flex flex-col items-center gap-3 overflow-hidden", "data-testid": "sidebar-footer", children: [_jsxs("div", { class: "user-badge", id: "userBadge", onClick: togglePopover, "data-testid": "user-badge", children: [isUserAuth ? (_jsx("div", { class: "user-avatar authenticated", id: "userAvatar", title: user.name || user.user, children: userInitials })) : (_jsx("div", { class: "user-avatar", id: "userAvatar", title: "Public", children: _jsx("i", { class: "fas fa-globe" }) })), _jsx("span", { class: "user-name nav-label", id: "userNameLabel", children: user ? (user.name || user.user) : 'Public' })] }), popoverOpen && (_jsxs("div", { ref: popoverRef, class: "user-popover open", id: "userPopover", "data-testid": "user-popover", children: [_jsxs("div", { class: "user-popover-header", children: [isUserAuth ? (_jsx("div", { class: "user-popover-avatar authenticated", id: "popoverAvatar", children: userInitials })) : (_jsx("div", { class: "user-popover-avatar", id: "popoverAvatar", children: _jsx("i", { class: "fas fa-globe" }) })), _jsxs("div", { class: "user-popover-info", children: [_jsx("div", { class: "user-popover-name", id: "popoverName", children: user ? (user.name || user.user) : 'Public' }), _jsx("div", { class: "user-popover-email", id: "popoverEmail", children: user ? (user.email || 'No email') : 'Not authenticated' })] })] }), user && user.groups && user.groups.length > 0 && (_jsxs("div", { class: "user-popover-groups", id: "popoverGroups", children: [_jsxs("div", { class: "user-popover-section-title", children: [_jsx("i", { class: "fas fa-users", style: { marginRight: '0.4rem', opacity: 0.5 } }), "Groups"] }), _jsx("div", { id: "popoverGroupList", class: "user-popover-group-list", children: user.groups.map(g => {
                                                    const isAdmin = g.toLowerCase().includes('admin');
                                                    return (_jsxs("span", { class: `user-group-chip ${isAdmin ? 'admin' : ''}`, children: [_jsx("i", { class: `fas ${isAdmin ? 'fa-shield-alt' : 'fa-tag'}` }), g] }, g));
                                                }) })] })), _jsx("div", { class: "user-popover-footer", id: "popoverAuthStatus", children: isUserAuth ? (_jsxs("span", { class: "user-auth-chip authenticated", id: "authChip", children: [_jsx("i", { class: "fas fa-shield-alt" }), " Authenticated"] })) : (_jsxs("span", { class: "user-auth-chip public", id: "authChip", children: [_jsx("i", { class: "fas fa-globe" }), " Public Access"] })) })] })), _jsxs("div", { class: "lang-switcher", "data-testid": "lang-switcher", children: [_jsx("button", { onClick: () => handleLanguageChange('en'), id: "lang-en", class: `lang-btn ${lang === 'en' ? 'active' : ''}`, children: "EN" }), _jsx("div", { class: "lang-sep" }), _jsx("button", { onClick: () => handleLanguageChange('de'), id: "lang-de", class: `lang-btn ${lang === 'de' ? 'active' : ''}`, children: "DE" })] }), _jsx("span", { class: "sidebar-version opacity-0 transition-opacity duration-200", children: __APP_VERSION__ })] })] }), _jsxs("main", { class: "flex-1 w-full ml-0 px-3 py-3 pb-24 md:ml-[72px] md:w-[calc(100%-72px)] md:px-6 md:py-4 md:pb-6 transition-[margin-left] duration-300", children: [_jsxs("header", { class: "md:hidden flex items-center justify-between px-3 py-2.5 bg-slate-800/80 backdrop-blur-md border border-white/5 rounded-xl mb-4 sticky top-1 z-30", "data-testid": "mobile-header", children: [_jsx("a", { href: "/plswk/", class: "flex items-center no-underline", children: _jsx("img", { src: "/plswk/img/pulswerk_logo_sm.png", alt: "Pulswerk", class: "h-6 brightness-0 invert" }) }), _jsx("div", { class: "flex items-center gap-2", children: _jsx("span", { class: "text-xs font-bold text-slate-200 truncate max-w-[130px]", "data-testid": "mobile-page-title", children: pageTitle === 'Home' ? t('nav_home') :
                                        pageTitle === 'Dashboards' ? t('nav_dashboards') :
                                            pageTitle === 'Assets' ? t('nav_assets') :
                                                pageTitle === 'Data Points' ? t('nav_telemetries') :
                                                    pageTitle === 'Connections' ? t('nav_connections') :
                                                        pageTitle === 'Active Alarms' ? t('nav_alarms') :
                                                            pageTitle === 'System Logs' ? t('nav_logs') :
                                                                pageTitle === 'Wallboxes' ? t('nav_wallboxes') :
                                                                    pageTitle === 'Billing' ? t('nav_billing') :
                                                                        pageTitle === 'Trajectory' ? t('nav_trajectory') :
                                                                            pageTitle === 'Historical Data' ? t('nav_historical_data') :
                                                                                pageTitle === 'System Heartbeat' ? t('nav_heartbeat') : pageTitle }) }), _jsxs("div", { class: "flex items-center gap-2", children: [_jsxs("div", { class: "flex items-center gap-1 text-[0.65rem] bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 rounded-full px-2 py-0.5", children: [_jsx("span", { class: "w-1.5 h-1.5 rounded-full bg-emerald-400 animate-pulse" }), _jsx("span", { class: "font-mono", children: "Live" })] }), _jsx("button", { type: "button", onClick: () => setMobileDrawerOpen(true), class: "w-8 h-8 rounded-lg bg-slate-800 border border-slate-700 text-slate-300 hover:text-white flex items-center justify-center text-xs cursor-pointer", title: "Menu & Profile", "data-testid": "mobile-menu-btn", children: isUserAuth ? (_jsx("span", { class: "font-bold text-[0.65rem] text-sky-400", children: userInitials })) : (_jsx("i", { class: "fas fa-bars" })) })] })] }), _jsx("header", { id: "pageHeader", class: "hidden md:flex justify-between items-center mb-4 border-b border-white/5 pb-2.5", "data-testid": "page-header", children: _jsx("h1", { class: "text-xl font-extrabold tracking-tight text-white/95", "data-testid": "page-title", children: pageTitle === 'Home' ? t('nav_home') :
                                pageTitle === 'Dashboards' ? t('nav_dashboards') :
                                    pageTitle === 'Assets' ? t('nav_assets') :
                                        pageTitle === 'Data Points' ? t('nav_telemetries') :
                                            pageTitle === 'Connections' ? t('nav_connections') :
                                                pageTitle === 'Active Alarms' ? t('nav_alarms') :
                                                    pageTitle === 'System Logs' ? t('nav_logs') :
                                                        pageTitle === 'Wallboxes' ? t('nav_wallboxes') :
                                                            pageTitle === 'Billing' ? t('nav_billing') :
                                                                pageTitle === 'Trajectory' ? t('nav_trajectory') :
                                                                    pageTitle === 'Historical Data' ? t('nav_historical_data') :
                                                                        pageTitle === 'System Heartbeat' ? t('nav_heartbeat') : pageTitle }) }), _jsx("div", { class: "w-full", children: pageComponent })] }), _jsxs("nav", { class: "md:hidden fixed bottom-0 inset-x-0 h-16 bg-slate-900/95 backdrop-blur-xl border-t border-slate-800 z-40 flex items-center justify-around px-2 pb-safe", "data-testid": "bottom-nav", children: [[
                        { id: 'home', path: '/plswk/', icon: 'fa-home', labelKey: 'nav_home' },
                        user?.modules?.wallbox !== false && user?.permissions?.canAccessWallbox !== false
                            ? { id: 'wallboxes', path: '/plswk/Wallboxes', icon: 'fa-charging-station', labelKey: 'nav_wallboxes' }
                            : null,
                        user?.modules?.dashboards !== false && user?.permissions?.canAccessDashboards !== false
                            ? { id: 'dashboards', path: '/plswk/Dashboards', icon: 'fa-th-large', labelKey: 'nav_dashboards' }
                            : null,
                        user?.modules?.alarms !== false && user?.permissions?.canAccessAlarms !== false
                            ? { id: 'alarms', path: '/plswk/Alarms', icon: 'fa-bell', labelKey: 'nav_alarms' }
                            : null
                    ].filter(Boolean).map((item) => {
                        const isActive = activeNavId === item.id;
                        return (_jsxs("a", { href: item.path, class: `flex flex-col items-center justify-center flex-1 py-1 no-underline transition-colors ${isActive ? 'text-sky-400 font-bold' : 'text-slate-400 hover:text-slate-200'}`, "data-testid": `bottom-nav-${item.id}`, children: [_jsx("i", { class: `fas ${item.icon} text-lg mb-1` }), _jsx("span", { class: "text-[0.62rem] font-medium tracking-tight truncate max-w-[64px]", children: t(item.labelKey) })] }, item.id));
                    }), _jsxs("button", { type: "button", onClick: () => setMobileDrawerOpen(prev => !prev), class: "flex flex-col items-center justify-center flex-1 py-1 bg-transparent border-0 text-slate-400 hover:text-slate-200 transition-colors cursor-pointer", "data-testid": "bottom-nav-more", children: [_jsx("i", { class: "fas fa-ellipsis-h text-lg mb-1" }), _jsx("span", { class: "text-[0.62rem] font-medium tracking-tight", children: "More" })] })] }), mobileDrawerOpen && (_jsx("div", { class: "md:hidden fixed inset-0 z-50 bg-black/70 backdrop-blur-sm flex flex-col justify-end animate-fade-in", onClick: () => setMobileDrawerOpen(false), "data-testid": "mobile-drawer-overlay", children: _jsxs("div", { class: "bg-slate-900 border-t border-slate-700/80 rounded-t-3xl p-5 max-h-[85vh] overflow-y-auto flex flex-col gap-4 animate-slide-up-mobile shadow-2xl", onClick: (e) => e.stopPropagation(), "data-testid": "mobile-drawer", children: [_jsxs("div", { class: "flex items-center justify-between border-b border-slate-800 pb-3", children: [_jsxs("div", { class: "flex items-center gap-3", children: [_jsx("div", { class: "w-10 h-10 rounded-xl bg-sky-500/10 text-sky-400 border border-sky-500/20 flex items-center justify-center text-sm font-bold", children: isUserAuth ? userInitials : _jsx("i", { class: "fas fa-user text-base" }) }), _jsxs("div", { children: [_jsx("div", { class: "text-sm font-bold text-slate-100", children: user?.name || user?.user || 'Public' }), _jsx("div", { class: "text-xs text-slate-400", children: user?.email || 'Public Access' })] })] }), _jsx("button", { class: "w-8 h-8 rounded-full bg-slate-800 border border-slate-700 text-slate-400 hover:text-white flex items-center justify-center cursor-pointer", onClick: () => setMobileDrawerOpen(false), "data-testid": "close-drawer", children: _jsx("i", { class: "fas fa-times" }) })] }), _jsx("div", { class: "grid grid-cols-2 gap-2.5 pt-1", children: navItems.map(item => {
                                const isActive = activeNavId === item.id;
                                return (_jsxs("a", { href: item.path, class: `flex items-center gap-3 p-3 rounded-xl border no-underline text-xs font-semibold transition-all ${isActive
                                        ? 'bg-sky-500/15 border-sky-500/30 text-sky-400 font-bold'
                                        : 'bg-slate-800/60 border-slate-800 text-slate-300 hover:bg-slate-800 hover:text-white'}`, children: [_jsx("i", { class: `fas ${item.icon} text-base w-5 text-center text-slate-400` }), _jsx("span", { class: "truncate", children: t(item.labelKey) })] }, item.id));
                            }) }), _jsxs("div", { class: "flex items-center justify-between pt-3 mt-1 border-t border-slate-800 text-xs text-slate-400", children: [_jsxs("div", { class: "flex items-center gap-2", children: [_jsx("span", { class: "text-slate-500 uppercase tracking-wider text-[0.65rem] font-bold", children: "Lang:" }), _jsx("button", { onClick: () => handleLanguageChange('en'), class: `px-3 py-1.5 rounded-lg border text-xs font-bold transition-colors ${lang === 'en' ? 'bg-sky-500/20 border-sky-500/40 text-sky-400' : 'bg-slate-800 border-slate-700 text-slate-400'}`, children: "EN" }), _jsx("button", { onClick: () => handleLanguageChange('de'), class: `px-3 py-1.5 rounded-lg border text-xs font-bold transition-colors ${lang === 'de' ? 'bg-sky-500/20 border-sky-500/40 text-sky-400' : 'bg-slate-800 border-slate-700 text-slate-400'}`, children: "DE" })] }), _jsx("span", { class: "text-[0.65rem] font-mono text-slate-500", children: __APP_VERSION__ })] })] }) }))] }));
}
// Mount the Preact application
const container = document.getElementById('app');
if (container) {
    render(_jsx(App, {}), container);
}
