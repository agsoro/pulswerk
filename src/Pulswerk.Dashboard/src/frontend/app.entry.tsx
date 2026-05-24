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

// Imports of i18n
import { initI18n, setLanguage, t, currentLang } from './i18n';

interface UserIdentity {
    username: string;
    email: string;
    name: string;
    groups: string[];
    permissions: {
        canWriteValue: boolean;
        canAckAlarm: boolean;
        canEditDashboard: boolean;
        canEditFavorites: boolean;
        canEditConfig: boolean;
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
    const popoverRef = useRef<HTMLDivElement>(null);

    // Client-side navigation helper
    const navigate = (url: string) => {
        window.history.pushState(null, '', url);
        setPath(url.split('?')[0]);
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
                
                // Inject credentials globally for compatibility
                (window as any)._currentUser = {
                    authenticated: data.username !== 'Public',
                    user: data.username,
                    name: data.name,
                    email: data.email,
                    groups: data.groups,
                    ...data.permissions
                };
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
            await (window as any).ensureKeysMeta();
            await loadUser();
        };
        init();
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
    if (dashboardDetailMatch) {
        pageTitle = 'Dashboards';
        pageComponent = (
            <DashboardsPage 
                dashboardId={dashboardDetailMatch[1]} 
                slug={dashboardDetailMatch[2] || undefined} 
            />
        );
    } else if (routePath === '/Dashboards') {
        pageTitle = 'Dashboards';
        pageComponent = <DashboardsPage />;
    } else if (routePath === '/Assets') {
        pageTitle = 'Assets';
        pageComponent = <AssetsPage initialNodeId={searchParams.get('node')} />;
    } else if (routePath === '/TelemetryList' || routePath === '/AssetsList') {
        pageTitle = 'Data Points';
        pageComponent = <TelemetryListPage />;
    } else if (routePath === '/Connections') {
        pageTitle = 'Connections';
        pageComponent = <ConnectionsPage initialConnId={searchParams.get('conn')} />;
    } else if (routePath === '/Alarms') {
        pageTitle = 'Active Alarms';
        pageComponent = <AlarmsPage />;
    } else if (routePath === '/Logs') {
        pageTitle = 'System Logs';
        pageComponent = <LogsPage />;
    } else if (routePath === '/Heartbeat') {
        pageTitle = 'System Heartbeat';
        pageComponent = <HeartbeatPage />;
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
    const isUserAuth = user && user.username !== 'Public';

    const navItems = [
        { id: 'home', path: '/plswk/', icon: 'fa-home', labelKey: 'nav_home', title: 'Home' },
        { id: 'dashboards', path: '/plswk/Dashboards', icon: 'fa-th-large', labelKey: 'nav_dashboards', title: 'Dashboards' },
        { id: 'assets', path: '/plswk/Assets', icon: 'fa-sitemap', labelKey: 'nav_assets', title: 'Assets' },
        { id: 'telemetry', path: '/plswk/TelemetryList', icon: 'fa-table', labelKey: 'nav_telemetries', title: 'Data Points' },
        { id: 'connections', path: '/plswk/Connections', icon: 'fa-network-wired', labelKey: 'nav_connections', title: 'Connections' },
        { id: 'alarms', path: '/plswk/Alarms', icon: 'fa-bell', labelKey: 'nav_alarms', title: 'Alarms' },
        { id: 'logs', path: '/plswk/Logs', icon: 'fa-terminal', labelKey: 'nav_logs', title: 'Logs' },
        { id: 'heartbeat', path: '/plswk/Heartbeat', icon: 'fa-heartbeat', labelKey: 'nav_heartbeat', title: 'Heartbeat' }
    ];

    // Determine current navigation tab for active highlighting
    const getActiveNavId = () => {
        if (path.startsWith('/plswk/Dashboards')) return 'dashboards';
        if (path.startsWith('/plswk/Assets')) return 'assets';
        if (path.startsWith('/plswk/TelemetryList') || path.startsWith('/plswk/AssetsList')) return 'telemetry';
        if (path.startsWith('/plswk/Connections')) return 'connections';
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
                class="w-[72px] bg-slate-800 border-r border-slate-700 py-6 px-3 flex flex-col items-center fixed h-screen z-50 transition-all duration-300 ease-in-out overflow-hidden whitespace-nowrap"
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
                            <div class="user-avatar authenticated" id="userAvatar" title={user.name || user.username}>
                                {userInitials}
                            </div>
                        ) : (
                            <div class="user-avatar" id="userAvatar" title="Public">
                                <i class="fas fa-globe"></i>
                            </div>
                        )}
                        <span class="user-name nav-label" id="userNameLabel">
                            {user ? (user.name || user.username) : 'Public'}
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
                                        {user ? (user.name || user.username) : 'Public'}
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

                    <span class="sidebar-version opacity-0 transition-opacity duration-200">v2.6.0</span>
                </footer>
            </aside>

            {/* Main content viewport */}
            <main class="flex-1 ml-[72px] px-6 py-4 w-[calc(100%-72px)] transition-[margin-left] duration-300">
                <header id="pageHeader" class="flex justify-between items-center mb-4 border-b border-white/5 pb-2.5" data-testid="page-header">
                    <h1 class="text-xl font-extrabold tracking-tight text-white/95" data-testid="page-title">
                        {pageTitle === 'Home' ? t('nav_home') : 
                         pageTitle === 'Dashboards' ? t('nav_dashboards') :
                         pageTitle === 'Assets' ? t('nav_assets') :
                         pageTitle === 'Data Points' ? t('nav_telemetries') :
                         pageTitle === 'Connections' ? t('nav_connections') :
                         pageTitle === 'Active Alarms' ? t('nav_alarms') :
                         pageTitle === 'System Logs' ? t('nav_logs') :
                         pageTitle === 'System Heartbeat' ? t('nav_heartbeat') : pageTitle}
                    </h1>
                </header>

                <div class="w-full">
                    {pageComponent}
                </div>
            </main>
        </div>
    );
}

// Mount the Preact application
const container = document.getElementById('app');
if (container) {
    render(<App />, container);
}
