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

interface UserIdentity {
    user: string;
    email: string;
    name: string;
    groups: string[];
    permissions: {
        canWriteValue: boolean;
        canAckAlarm: boolean;
        canEditDashboard: boolean;
        canEditFavorites: boolean;
        canEditConfig: boolean;
        canAccessEms?: boolean;
        canAccessBilling?: boolean;
        canAccessWallbox?: boolean;
        canAccessHistoricalData?: boolean;
        canAccessAlarms?: boolean;
        canAccessLogs?: boolean;
        canAccessHeartbeat?: boolean;
        canAccessDashboards?: boolean;
        canAccessAssets?: boolean;
        canAccessTelemetry?: boolean;
        canAccessConnections?: boolean;
    };
    modules?: {
        ems?: boolean;
        billing?: boolean;
        wallbox?: boolean;
        historicalData?: boolean;
        alarms?: boolean;
        logs?: boolean;
        heartbeat?: boolean;
        dashboards?: boolean;
        assets?: boolean;
        telemetry?: boolean;
        connections?: boolean;
    };
}

export function App() {
    const [path, setPath] = useState(window.location.pathname);
    const [lang, setLang] = useState(() => {
        initI18n();
        return currentLang;
    });
    const [user, setUser] = useState<UserIdentity | null>(null);
    const [popoverOpen, setPopoverOpen] = useState(false);
    const [mobileDrawerOpen, setMobileDrawerOpen] = useState(false);
    const popoverRef = useRef<HTMLDivElement>(null);

    // Client-side navigation helper
    const navigate = (url: string) => {
        window.history.pushState(null, '', url);
        setPath(url.split('?')[0]);
        setMobileDrawerOpen(false);
    };

    // Global click listener to intercept relative anchors
    useEffect(() => {
        const handleLinkClick = (e: MouseEvent) => {
            const target = e.target as HTMLElement;
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
                (window as any)._currentUser = data;
                (window as any).pwCanWriteValue = data.permissions.canWriteValue;
                (window as any).pwCanAckAlarm = data.permissions.canAckAlarm;
                (window as any).pwCanEditDashboard = data.permissions.canEditDashboard;
                (window as any).pwCanEditFavorites = data.permissions.canEditFavorites;
                (window as any).pwCanEditConfig = data.permissions.canEditConfig;
            }
        } catch (e) {
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
        const handleStorageChange = (e: StorageEvent) => {
            if (e.key === 'pw_modules_updated') loadUser();
        };
        window.addEventListener('storage', handleStorageChange);
        return () => window.removeEventListener('storage', handleStorageChange);
    }, []);

    // Close user popover when clicking outside
    useEffect(() => {
        const handleOutsideClick = (e: MouseEvent) => {
            if (popoverOpen && popoverRef.current && !popoverRef.current.contains(e.target as Node)) {
                const badge = document.getElementById('userBadge');
                if (badge && badge.contains(e.target as Node)) return;
                setPopoverOpen(false);
            }
        };
        document.addEventListener('mousedown', handleOutsideClick);
        return () => document.removeEventListener('mousedown', handleOutsideClick);
    }, [popoverOpen]);

    const handleLanguageChange = (newLang: string) => {
        setLanguage(newLang);
        setLang(newLang);
    };

    const togglePopover = (e: MouseEvent) => {
        e.stopPropagation();
        setPopoverOpen(prev => !prev);
    };

    // Simple Route Resolver
    const searchParams = new URLSearchParams(window.location.search);
    let routePath = path.replace(/\/$/, '') || '/plswk';
    if (routePath.startsWith('/plswk')) {
        routePath = routePath.slice('/plswk'.length);
    }
    if (!routePath) routePath = '/';

    let pageComponent = null;
    let pageTitle = 'Home';


    const dashboardDetailMatch = routePath.match(/^\/Dashboards\/([^/]+)(?:\/([^/]+))?$/);
    if (dashboardDetailMatch && user?.modules?.dashboards !== false && user?.permissions?.canAccessDashboards !== false) {
        pageTitle = 'Dashboards';
        pageComponent = (
            <DashboardsPage 
                dashboardId={dashboardDetailMatch[1]} 
                slug={dashboardDetailMatch[2] || undefined} 
            />
        );
    } else if (routePath === '/Dashboards' && user?.modules?.dashboards !== false && user?.permissions?.canAccessDashboards !== false) {
        pageTitle = 'Dashboards';
        pageComponent = <DashboardsPage />;
    } else if (routePath === '/Assets' && user?.modules?.assets !== false && user?.permissions?.canAccessAssets !== false) {
        pageTitle = 'Assets';
        pageComponent = <AssetsPage initialNodeId={searchParams.get('node')} />;
    } else if ((routePath === '/TelemetryList' || routePath === '/AssetsList') && user?.modules?.telemetry !== false && user?.permissions?.canAccessTelemetry !== false) {
        pageTitle = 'Data Points';
        pageComponent = <TelemetryListPage />;
    } else if (routePath === '/Connections' && user?.modules?.connections !== false && user?.permissions?.canAccessConnections !== false) {
        pageTitle = 'Connections';
        pageComponent = <ConnectionsPage initialConnId={searchParams.get('conn')} />;
    } else if (routePath === '/Alarms' && user?.modules?.alarms !== false && user?.permissions?.canAccessAlarms !== false) {
        pageTitle = 'Active Alarms';
        pageComponent = <AlarmsPage />;
    } else if (routePath === '/Logs' && user?.modules?.logs !== false && user?.permissions?.canAccessLogs !== false) {
        pageTitle = 'System Logs';
        pageComponent = <LogsPage />;
    } else if (routePath === '/Heartbeat' && user?.modules?.heartbeat !== false && user?.permissions?.canAccessHeartbeat !== false) {
        pageTitle = 'System Heartbeat';
        pageComponent = <HeartbeatPage />;
    } else if (routePath === '/Wallboxes' && user?.modules?.wallbox !== false && user?.permissions?.canAccessWallbox !== false) {
        pageTitle = 'Wallboxes';
        pageComponent = <WallboxesPage />;
    } else if (routePath === '/Billing' && user?.modules?.billing !== false && user?.permissions?.canAccessBilling !== false) {
        pageTitle = 'Billing';
        pageComponent = <BillingPage />;
    } else if (routePath === '/Trajectory' && user?.modules?.ems !== false && user?.permissions?.canAccessEms !== false) {
        pageTitle = 'Trajectory';
        pageComponent = <TrajectoryPage />;
    } else if (routePath === '/TelemetryCrud' && user?.modules?.historicalData !== false && user?.permissions?.canAccessHistoricalData !== false) {
        pageTitle = 'Historical Data';
        pageComponent = <TelemetryCrudPage initialKey={searchParams.get('key')} />;
    } else {
        pageTitle = 'Home';
        pageComponent = <FavoritesPage />;
    }

    const getUserInitials = (name: string): string => {
        if (!name || name === 'Public' || name === 'public') return '';
        const parts = name.trim().split(/\s+/);
        if (parts.length >= 2) return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
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
    ].filter(Boolean) as any[];

    // Determine current navigation tab for active highlighting
    const getActiveNavId = () => {
        if (path.startsWith('/plswk/Dashboards')) return 'dashboards';
        if (path.startsWith('/plswk/Assets')) return 'assets';
        if (path.startsWith('/plswk/TelemetryList') || path.startsWith('/plswk/AssetsList')) return 'telemetry';
        if (path.startsWith('/plswk/Connections')) return 'connections';
        if (path.startsWith('/plswk/Wallboxes')) return 'wallboxes';
        if (path.startsWith('/plswk/Billing')) return 'billing';
        if (path.startsWith('/plswk/Trajectory')) return 'trajectory';
        if (path.startsWith('/plswk/TelemetryCrud')) return 'telemetryCrud';
        if (path.startsWith('/plswk/Alarms')) return 'alarms';
        if (path.startsWith('/plswk/Logs')) return 'logs';
        if (path.startsWith('/plswk/Heartbeat')) return 'heartbeat';
        return 'home';
    };

    const activeNavId = getActiveNavId();

    return (
        <div class="w-full flex min-h-screen">
            {/* Sidebar element */}
            <aside 
                class="hidden md:flex w-[72px] bg-slate-800 border-r border-slate-700 py-6 px-3 flex-col items-center fixed h-screen z-50 transition-all duration-300 ease-in-out overflow-hidden whitespace-nowrap"
                data-testid="sidebar"
            >
                <a href="/plswk/" class="flex items-center mb-10 no-underline w-full pl-2 overflow-hidden" data-testid="sidebar-brand">
                    <img src="/plswk/img/pulswerk_logo_sm.png" alt="Pulswerk" class="h-8 min-w-[170px] brightness-0 invert" />
                </a>

                <ul class="nav-links list-none flex flex-col gap-2 w-full" data-testid="nav-links">
                    {navItems.map(item => {
                        const isActive = activeNavId === item.id;
                        return (
                            <li key={item.id}>
                                <a 
                                    href={item.path}
                                    class={`flex items-center py-3 px-[0.85rem] text-slate-400 no-underline rounded-lg transition-all duration-200 font-medium w-full whitespace-nowrap hover:bg-sky-400/10 hover:text-sky-400 ${
                                        isActive ? '!bg-sky-400/10 !text-sky-400' : ''
                                    }`}
                                    data-testid={`nav-${item.id}`}
                                >
                                    <i class={`fas ${item.icon} w-5 text-xl flex-shrink-0 text-center`}></i>
                                    <span class="nav-label nav-text opacity-0 transition-opacity duration-200 ml-4">{t(item.labelKey)}</span>
                                </a>
                            </li>
                        );
                    })}
                </ul>

                {/* Footer section */}
                <footer class="mt-auto pt-4 pb-2 px-2 text-xs text-slate-400 border-t border-slate-700 w-full flex flex-col items-center gap-3 overflow-hidden" data-testid="sidebar-footer">
                    {/* User profile identifier badge */}
                    <div 
                        class="user-badge" 
                        id="userBadge" 
                        onClick={togglePopover}
                        data-testid="user-badge"
                    >
                        {isUserAuth ? (
                            <div class="user-avatar authenticated" id="userAvatar" title={user.name || user.user}>
                                {userInitials}
                            </div>
                        ) : (
                            <div class="user-avatar" id="userAvatar" title="Public">
                                <i class="fas fa-globe"></i>
                            </div>
                        )}
                        <span class="user-name nav-label" id="userNameLabel">
                            {user ? (user.name || user.user) : 'Public'}
                        </span>
                    </div>

                    {/* User info popover container */}
                    {popoverOpen && (
                        <div 
                            ref={popoverRef}
                            class="user-popover open" 
                            id="userPopover" 
                            data-testid="user-popover"
                        >
                            <div class="user-popover-header">
                                {isUserAuth ? (
                                    <div class="user-popover-avatar authenticated" id="popoverAvatar">
                                        {userInitials}
                                    </div>
                                ) : (
                                    <div class="user-popover-avatar" id="popoverAvatar">
                                        <i class="fas fa-globe"></i>
                                    </div>
                                )}
                                <div class="user-popover-info">
                                    <div class="user-popover-name" id="popoverName">
                                        {user ? (user.name || user.user) : 'Public'}
                                    </div>
                                    <div class="user-popover-email" id="popoverEmail">
                                        {user ? (user.email || 'No email') : 'Not authenticated'}
                                    </div>
                                </div>
                            </div>

                            {user && user.groups && user.groups.length > 0 && (
                                <div class="user-popover-groups" id="popoverGroups">
                                    <div class="user-popover-section-title">
                                        <i class="fas fa-users" style={{ marginRight: '0.4rem', opacity: 0.5 }}></i>Groups
                                    </div>
                                    <div id="popoverGroupList" class="user-popover-group-list">
                                        {user.groups.map(g => {
                                            const isAdmin = g.toLowerCase().includes('admin');
                                            return (
                                                <span key={g} class={`user-group-chip ${isAdmin ? 'admin' : ''}`}>
                                                    <i class={`fas ${isAdmin ? 'fa-shield-alt' : 'fa-tag'}`}></i>
                                                    {g}
                                                </span>
                                            );
                                        })}
                                    </div>
                                </div>
                            )}

                            <div class="user-popover-footer" id="popoverAuthStatus">
                                {isUserAuth ? (
                                    <span class="user-auth-chip authenticated" id="authChip">
                                        <i class="fas fa-shield-alt"></i> Authenticated
                                    </span>
                                ) : (
                                    <span class="user-auth-chip public" id="authChip">
                                        <i class="fas fa-globe"></i> Public Access
                                    </span>
                                )}
                            </div>
                        </div>
                    )}

                    {/* Language Toggler */}
                    <div class="lang-switcher" data-testid="lang-switcher">
                        <button 
                            onClick={() => handleLanguageChange('en')} 
                            id="lang-en" 
                            class={`lang-btn ${lang === 'en' ? 'active' : ''}`}
                        >
                            EN
                        </button>
                        <div class="lang-sep"></div>
                        <button 
                            onClick={() => handleLanguageChange('de')} 
                            id="lang-de" 
                            class={`lang-btn ${lang === 'de' ? 'active' : ''}`}
                        >
                            DE
                        </button>
                    </div>

                    <span class="sidebar-version opacity-0 transition-opacity duration-200">{__APP_VERSION__}</span>
                </footer>
            </aside>

            {/* Main content viewport */}
            <main class="flex-1 w-full ml-0 px-3 py-3 pb-24 md:ml-[72px] md:w-[calc(100%-72px)] md:px-6 md:py-4 md:pb-6 transition-[margin-left] duration-300">
                {/* Mobile Top Header */}
                <header class="md:hidden flex items-center justify-between px-3 py-2.5 bg-slate-800/80 backdrop-blur-md border border-white/5 rounded-xl mb-4 sticky top-1 z-30" data-testid="mobile-header">
                    <a href="/plswk/" class="flex items-center no-underline">
                        <img src="/plswk/img/pulswerk_logo_sm.png" alt="Pulswerk" class="h-6 brightness-0 invert" />
                    </a>
                    <div class="flex items-center gap-2">
                        <span class="text-xs font-bold text-slate-200 truncate max-w-[130px]" data-testid="mobile-page-title">
                            {pageTitle === 'Home' ? t('nav_home') : 
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
                             pageTitle === 'System Heartbeat' ? t('nav_heartbeat') : pageTitle}
                        </span>
                    </div>
                    <div class="flex items-center gap-2">
                        <div class="flex items-center gap-1 text-[0.65rem] bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 rounded-full px-2 py-0.5">
                            <span class="w-1.5 h-1.5 rounded-full bg-emerald-400 animate-pulse"></span>
                            <span class="font-mono">Live</span>
                        </div>
                        <button 
                            type="button"
                            onClick={() => setMobileDrawerOpen(true)}
                            class="w-8 h-8 rounded-lg bg-slate-800 border border-slate-700 text-slate-300 hover:text-white flex items-center justify-center text-xs cursor-pointer"
                            title="Menu & Profile"
                            data-testid="mobile-menu-btn"
                        >
                            {isUserAuth ? (
                                <span class="font-bold text-[0.65rem] text-sky-400">{userInitials}</span>
                            ) : (
                                <i class="fas fa-bars"></i>
                            )}
                        </button>
                    </div>
                </header>

                <header id="pageHeader" class="hidden md:flex justify-between items-center mb-4 border-b border-white/5 pb-2.5" data-testid="page-header">
                    <h1 class="text-xl font-extrabold tracking-tight text-white/95" data-testid="page-title">
                        {pageTitle === 'Home' ? t('nav_home') : 
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
                         pageTitle === 'System Heartbeat' ? t('nav_heartbeat') : pageTitle}
                    </h1>
                </header>

                <div class="w-full">
                    {pageComponent}
                </div>
            </main>

            {/* Mobile Bottom Navigation Bar */}
            <nav class="md:hidden fixed bottom-0 inset-x-0 h-16 bg-slate-900/95 backdrop-blur-xl border-t border-slate-800 z-40 flex items-center justify-around px-2 pb-safe" data-testid="bottom-nav">
                {[
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
                ].filter(Boolean).map((item: any) => {
                    const isActive = activeNavId === item.id;
                    return (
                        <a 
                            key={item.id}
                            href={item.path}
                            class={`flex flex-col items-center justify-center flex-1 py-1 no-underline transition-colors ${
                                isActive ? 'text-sky-400 font-bold' : 'text-slate-400 hover:text-slate-200'
                            }`}
                            data-testid={`bottom-nav-${item.id}`}
                        >
                            <i class={`fas ${item.icon} text-lg mb-1`}></i>
                            <span class="text-[0.62rem] font-medium tracking-tight truncate max-w-[64px]">{t(item.labelKey)}</span>
                        </a>
                    );
                })}
                <button 
                    type="button"
                    onClick={() => setMobileDrawerOpen(prev => !prev)}
                    class="flex flex-col items-center justify-center flex-1 py-1 bg-transparent border-0 text-slate-400 hover:text-slate-200 transition-colors cursor-pointer"
                    data-testid="bottom-nav-more"
                >
                    <i class="fas fa-ellipsis-h text-lg mb-1"></i>
                    <span class="text-[0.62rem] font-medium tracking-tight">More</span>
                </button>
            </nav>

            {/* Mobile "More" Slide-up Drawer */}
            {mobileDrawerOpen && (
                <div 
                    class="md:hidden fixed inset-0 z-50 bg-black/70 backdrop-blur-sm flex flex-col justify-end animate-fade-in" 
                    onClick={() => setMobileDrawerOpen(false)}
                    data-testid="mobile-drawer-overlay"
                >
                    <div 
                        class="bg-slate-900 border-t border-slate-700/80 rounded-t-3xl p-5 max-h-[85vh] overflow-y-auto flex flex-col gap-4 animate-slide-up-mobile shadow-2xl"
                        onClick={(e) => e.stopPropagation()}
                        data-testid="mobile-drawer"
                    >
                        {/* Drawer Header */}
                        <div class="flex items-center justify-between border-b border-slate-800 pb-3">
                            <div class="flex items-center gap-3">
                                <div class="w-10 h-10 rounded-xl bg-sky-500/10 text-sky-400 border border-sky-500/20 flex items-center justify-center text-sm font-bold">
                                    {isUserAuth ? userInitials : <i class="fas fa-user text-base"></i>}
                                </div>
                                <div>
                                    <div class="text-sm font-bold text-slate-100">{user?.name || user?.user || 'Public'}</div>
                                    <div class="text-xs text-slate-400">{user?.email || 'Public Access'}</div>
                                </div>
                            </div>
                            <button 
                                class="w-8 h-8 rounded-full bg-slate-800 border border-slate-700 text-slate-400 hover:text-white flex items-center justify-center cursor-pointer"
                                onClick={() => setMobileDrawerOpen(false)}
                                data-testid="close-drawer"
                            >
                                <i class="fas fa-times"></i>
                            </button>
                        </div>

                        {/* All Modules Grid */}
                        <div class="grid grid-cols-2 gap-2.5 pt-1">
                            {navItems.map(item => {
                                const isActive = activeNavId === item.id;
                                return (
                                    <a 
                                        key={item.id}
                                        href={item.path}
                                        class={`flex items-center gap-3 p-3 rounded-xl border no-underline text-xs font-semibold transition-all ${
                                            isActive 
                                                ? 'bg-sky-500/15 border-sky-500/30 text-sky-400 font-bold' 
                                                : 'bg-slate-800/60 border-slate-800 text-slate-300 hover:bg-slate-800 hover:text-white'
                                        }`}
                                    >
                                        <i class={`fas ${item.icon} text-base w-5 text-center text-slate-400`}></i>
                                        <span class="truncate">{t(item.labelKey)}</span>
                                    </a>
                                );
                            })}
                        </div>

                        {/* Language & Info */}
                        <div class="flex items-center justify-between pt-3 mt-1 border-t border-slate-800 text-xs text-slate-400">
                            <div class="flex items-center gap-2">
                                <span class="text-slate-500 uppercase tracking-wider text-[0.65rem] font-bold">Lang:</span>
                                <button 
                                    onClick={() => handleLanguageChange('en')} 
                                    class={`px-3 py-1.5 rounded-lg border text-xs font-bold transition-colors ${lang === 'en' ? 'bg-sky-500/20 border-sky-500/40 text-sky-400' : 'bg-slate-800 border-slate-700 text-slate-400'}`}
                                >EN</button>
                                <button 
                                    onClick={() => handleLanguageChange('de')} 
                                    class={`px-3 py-1.5 rounded-lg border text-xs font-bold transition-colors ${lang === 'de' ? 'bg-sky-500/20 border-sky-500/40 text-sky-400' : 'bg-slate-800 border-slate-700 text-slate-400'}`}
                                >DE</button>
                            </div>
                            <span class="text-[0.65rem] font-mono text-slate-500">{__APP_VERSION__}</span>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
}

// Mount the Preact application
const container = document.getElementById('app');
if (container) {
    render(<App />, container);
}
