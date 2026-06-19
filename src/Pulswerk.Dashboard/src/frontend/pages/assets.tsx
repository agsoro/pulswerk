import { useState, useEffect, useRef } from 'preact/hooks';
import { DashboardService } from '../dashboards/api';
import { t } from '../i18n';

interface AssetsPageProps {
    initialNodeId?: string | null;
}

export function AssetsPage({ initialNodeId }: AssetsPageProps) {
    const [tree, setTree] = useState<any[]>([]);
    const [selectedNode, setSelectedNode] = useState<any>(null);
    const [selectedPath, setSelectedPath] = useState<any[]>([]);
    const [expandedNodes, setExpandedNodes] = useState<Set<string>>(new Set());
    const [sidebarWidth, setSidebarWidth] = useState(300);
    const [loading, setLoading] = useState(true);
    const [liveValues, setLiveValues] = useState<Record<string, any>>({});
    
    const sidebarRef = useRef<HTMLDivElement>(null);
    const observerRef = useRef<IntersectionObserver | null>(null);
    const visibleKeysRef = useRef<Set<string>>(new Set());
    const pointsListRef = useRef<HTMLDivElement>(null);

    // Load Tree on Mount
    useEffect(() => {
        const loadTreeData = async () => {
            try {
                const response = await fetch('/plswk/api/tree');
                if (!response.ok) throw new Error("Tree API error");
                const data = await response.json();
                setTree(data || []);

                // Find and select initial node if present in URL
                const params = new URLSearchParams(window.location.search);
                const queryNodeId = params.get('node') || initialNodeId;
                if (queryNodeId && data) {
                    const found = findNodeAndPath(data, queryNodeId);
                    if (found) {
                        setSelectedNode(found.node);
                        setSelectedPath(found.path);
                        // Auto expand parents
                        const newExpanded = new Set<string>();
                        found.path.forEach(p => newExpanded.add(p.id));
                        setExpandedNodes(newExpanded);
                    }
                }
            } catch (err) {
                console.error("Failed to load assets hierarchy:", err);
            } finally {
                setLoading(false);
            }
        };
        loadTreeData();

        // Listen for history state pop
        const handlePop = () => {
            const params = new URLSearchParams(window.location.search);
            const queryNodeId = params.get('node');
            if (queryNodeId && tree.length > 0) {
                const found = findNodeAndPath(tree, queryNodeId);
                if (found) {
                    setSelectedNode(found.node);
                    setSelectedPath(found.path);
                }
            }
        };
        window.addEventListener('popstate', handlePop);
        return () => {
            window.removeEventListener('popstate', handlePop);
        };
    }, [initialNodeId, tree.length]);

    // Resizing splitter logic
    const startResize = (e: MouseEvent) => {
        e.preventDefault();
        const startX = e.clientX;
        const startWidth = sidebarWidth;

        const doDrag = (moveEvent: MouseEvent) => {
            const newWidth = startWidth + (moveEvent.clientX - startX);
            if (newWidth > 150 && newWidth < 800) {
                setSidebarWidth(newWidth);
            }
        };

        const stopDrag = () => {
            document.removeEventListener('mousemove', doDrag);
            document.removeEventListener('mouseup', stopDrag);
            document.body.style.cursor = 'default';
        };

        document.addEventListener('mousemove', doDrag);
        document.addEventListener('mouseup', stopDrag);
        document.body.style.cursor = 'col-resize';
    };

    // IntersectionObserver and SSE sub logic for selected node telemetries
    useEffect(() => {
        if (!selectedNode || !selectedNode.telemetries || selectedNode.telemetries.length === 0) {
            return;
        }

        const keys = selectedNode.telemetries.map((point: any) => point.key).filter(Boolean);
        if (keys.length === 0) return;

        // Sync selected node's telemetry metadata to global allKeys cache so the details modal can resolve writable state
        const existingAllKeys = (window as any).allKeys || [];
        const currentKeys = selectedNode.telemetries.map((point: any) => ({
            key: point.key,
            name: point.name,
            fullName: point.fullName || point.name,
            units: point.units || '',
            type: point.type || '',
            isWritable: !!point.isWritable,
            parentPath: []
        }));
        const mergedKeys = [...existingAllKeys];
        currentKeys.forEach((nk: any) => {
            if (!mergedKeys.some((ek: any) => ek.key === nk.key)) {
                mergedKeys.push(nk);
            }
        });
        (window as any).allKeys = mergedKeys;

        // Fetch initial latest values
        DashboardService.fetchLatestValues(keys).then(data => {
            setLiveValues(prev => ({ ...prev, ...data }));
        }).catch(err => console.error("Failed to fetch initial latest values:", err));

        // Prepopulate visible keys with first 15 keys
        const initialVisible = keys.slice(0, 15);
        visibleKeysRef.current = new Set(initialVisible);

        let liveUnsubscribe: (() => void) | null = null;

        const subToKeys = (keysToSub: string[]) => {
            if (liveUnsubscribe) {
                liveUnsubscribe();
            }
            if (keysToSub.length === 0) return;
            liveUnsubscribe = DashboardService.listenToLiveUpdates(keysToSub, (newData) => {
                setLiveValues(prev => ({ ...prev, ...newData }));
            });
        };

        subToKeys(initialVisible);

        // Setup observer
        observerRef.current = new IntersectionObserver((entries) => {
            let changed = false;
            entries.forEach(entry => {
                const key = (entry.target as HTMLElement).dataset.key;
                if (!key) return;
                if (entry.isIntersecting) {
                    if (!visibleKeysRef.current.has(key)) {
                        visibleKeysRef.current.add(key);
                        changed = true;
                    }
                } else {
                    if (visibleKeysRef.current.has(key)) {
                        visibleKeysRef.current.delete(key);
                        changed = true;
                    }
                }
            });
            if (changed) {
                subToKeys(Array.from(visibleKeysRef.current));
            }
        }, {
            rootMargin: '100px 0px 100px 0px',
            threshold: 0.01
        });

        // Delay observing briefly to allow DOM items to render
        const timer = setTimeout(() => {
            if (pointsListRef.current && observerRef.current) {
                pointsListRef.current.querySelectorAll('[data-key]').forEach(el => {
                    observerRef.current?.observe(el);
                });
            }
        }, 100);

        return () => {
            clearTimeout(timer);
            if (observerRef.current) {
                observerRef.current.disconnect();
            }
            if (liveUnsubscribe) {
                liveUnsubscribe();
            }
            visibleKeysRef.current.clear();
        };
    }, [selectedNode]);

    // Find node helper
    const findNodeAndPath = (nodes: any[], targetId: string, currentPath: any[] = []): { node: any, path: any[] } | null => {
        for (const node of nodes) {
            const nextPath = [...currentPath, { id: node.id, name: node.name }];
            if (node.id === targetId) {
                return { node, path: nextPath };
            }
            if (node.children && node.children.length > 0) {
                const found = findNodeAndPath(node.children, targetId, nextPath);
                if (found) return found;
            }
        }
        return null;
    };

    const handleSelectNode = (node: any, path: any[]) => {
        setSelectedNode(node);
        setSelectedPath(path);

        // Update URL
        const url = new URL(window.location.href);
        url.searchParams.set('node', node.id);
        window.history.pushState({ nodeId: node.id }, '', url.toString());
    };

    const toggleExpand = (id: string, e: MouseEvent) => {
        e.stopPropagation();
        const nextExpanded = new Set(expandedNodes);
        if (nextExpanded.has(id)) {
            nextExpanded.delete(id);
        } else {
            nextExpanded.add(id);
        }
        setExpandedNodes(nextExpanded);
    };

    if (loading) {
        return (
            <div class="h-full flex items-center justify-center p-16">
                <div class="flex flex-col items-center gap-4 text-cyan-400">
                    <i class="fas fa-spinner fa-spin text-3xl"></i>
                    <p class="text-sm font-medium animate-pulse">{t('loading')}</p>
                </div>
            </div>
        );
    }

    // Recursive Tree node renderer
    const renderTreeNode = (node: any, depth: number, path: any[] = []) => {
        const currentPath = [...path, { id: node.id, name: node.name }];
        const isExpanded = expandedNodes.has(node.id);
        const hasChildren = node.children && node.children.length > 0;
        const isActive = selectedNode?.id === node.id;

        const iconClass = node.type === 'BACnet Device' ? 'fa-server' : 
                         (node.isView ? 'fa-folder' : 'fa-tag');

        return (
            <div key={node.id} class="select-none relative">
                <div 
                    class={`tree-row flex items-center px-4 py-1.5 cursor-pointer transition-all duration-200 text-sm whitespace-nowrap mx-2 my-px rounded hover:bg-white/5 hover:text-slate-50 ${
                        isActive ? 'active bg-sky-500/10 text-sky-400 font-bold border border-sky-500/20' : 'text-slate-400'
                    }`}
                    onClick={() => handleSelectNode(node, currentPath)}
                >
                    {/* Indentation Spacers */}
                    {Array.from({ length: depth }).map((_, i) => (
                        <div key={i} class="tree-indent w-5 shrink-0 h-full relative"></div>
                    ))}

                    {/* Toggle Icon */}
                    <div 
                        class={`tree-toggle w-5 h-5 flex items-center justify-center mr-2 text-[0.7rem] transition-transform duration-200 text-slate-400 z-[1] ${
                            isExpanded ? 'expanded rotate-90' : ''
                        }`}
                        onClick={(e) => hasChildren && toggleExpand(node.id, e)}
                    >
                        {hasChildren && <i class="fas fa-chevron-right"></i>}
                    </div>

                    {/* Node Type Icon */}
                    <div class="tree-icon w-5 mr-3 text-center text-[0.9rem]">
                        <i class={`fas ${iconClass}`}></i>
                    </div>

                    {/* Label */}
                    <span>{node.name}</span>
                </div>

                {hasChildren && (
                    <div class={`tree-children overflow-hidden transition-all duration-300 ${isExpanded ? 'block max-h-none' : 'hidden max-h-0'}`}>
                        {node.children.map((child: any) => renderTreeNode(child, depth + 1, currentPath))}
                    </div>
                )}
            </div>
        );
    };

    const getPointIcon = (type: string) => {
        const tLower = type.toLowerCase();
        if (tLower.includes('analog') || tLower.includes('input') || tLower.includes('value')) return 'fa-wave-square';
        if (tLower.includes('binary') || tLower.includes('digital') || tLower.includes('status')) return 'fa-toggle-on';
        if (tLower.includes('multi')) return 'fa-list-ol';
        if (tLower.includes('schedule')) return 'fa-calendar-alt';
        return 'fa-microchip';
    };

    return (
        <div class="flex gap-6 h-[calc(100vh-110px)] animate-fade-in w-full">
            {/* Left: Tree pane */}
            <div 
                ref={sidebarRef}
                style={{ width: `${sidebarWidth}px` }} 
                class="shrink-0 flex flex-col glass rounded-xl border border-slate-700 overflow-y-auto overflow-x-hidden relative"
            >
                <div class="p-4 border-b border-white/5 font-bold tracking-tight text-sm text-slate-300 uppercase select-none">
                    Asset Hierarchy
                </div>
                <div class="py-3">
                    {tree.map(node => renderTreeNode(node, 0))}
                </div>
            </div>

            {/* Splitter Resizer */}
            <div 
                class="w-1.5 shrink-0 bg-slate-900 border-x border-slate-800 hover:bg-sky-500/30 cursor-col-resize transition-colors flex items-center justify-center"
                onMouseDown={startResize}
            >
                <div class="w-[2px] h-8 bg-slate-700/60 rounded"></div>
            </div>

            {/* Right: Point list pane */}
            <div class="flex-1 flex flex-col bg-slate-800 border border-slate-700 rounded-xl overflow-hidden relative">
                {selectedNode ? (
                    <div class="h-full flex flex-col">
                        {/* Header & Breadcrumb */}
                        <div class="px-6 py-5 border-b border-slate-700 shrink-0 bg-black/15">
                            <div class="flex items-center gap-2 mb-2 text-xs font-semibold text-slate-400" id="breadcrumb">
                                {selectedPath.map((p, idx) => (
                                    <span key={p.id} class="flex items-center gap-1.5">
                                        <button 
                                            class="hover:text-sky-400 text-slate-400 no-underline cursor-pointer bg-transparent border-0 p-0 text-xs font-semibold"
                                            onClick={() => {
                                                const found = findNodeAndPath(tree, p.id);
                                                if (found) handleSelectNode(found.node, found.path);
                                            }}
                                        >
                                            {p.name}
                                        </button>
                                        {idx < selectedPath.length - 1 && <i class="fas fa-chevron-right text-[0.55rem] opacity-40"></i>}
                                    </span>
                                ))}
                            </div>
                            <h2 class="text-xl font-bold text-white leading-tight">{selectedNode.name}</h2>
                            <div class="text-[0.7rem] text-slate-400 font-mono mt-1">{selectedNode.id}</div>
                        </div>

                        {/* List of Data Points */}
                        <div ref={pointsListRef} class="flex-1 overflow-y-auto px-6 py-4">
                            {!selectedNode.telemetries || selectedNode.telemetries.length === 0 ? (
                                <div class="h-full flex flex-col items-center justify-center text-slate-400 opacity-30">
                                    <i class="fas fa-info-circle text-5xl mb-4"></i>
                                    <p>No data points in this view</p>
                                </div>
                            ) : (
                                selectedNode.telemetries.map((point: any) => {
                                    const isSchedule = point.type === 'OBJECT_SCHEDULE';
                                    const curValue = liveValues[point.key] !== undefined ? liveValues[point.key] : point.value;
                                    const displayVal = (window as any).PulswerkValue?.formatDisplay(curValue, point.type) || curValue;
                                    const isFav = (window as any).pw_fav?.get('deziko_favorites')?.includes(point.key);

                                    return (
                                        <div 
                                            key={point.key}
                                            data-key={point.key}
                                            class="glass border border-slate-700 rounded-lg p-4 mb-3 flex items-center gap-5 transition-all duration-200 hover:border-sky-400 hover:translate-x-1 cursor-pointer"
                                            onClick={() => (window as any).openTelemetryDetails(point.key)}
                                        >
                                            <div class="w-10 h-10 bg-sky-400/10 rounded-lg flex items-center justify-center text-sky-400 text-xl shrink-0">
                                                <i class={`fas ${getPointIcon(point.type || '')}`}></i>
                                            </div>
                                            
                                            <div class="flex-1 min-w-0">
                                                <div class="font-semibold text-slate-50 truncate">{point.name || 'Unnamed'}</div>
                                                <div class="text-[0.7rem] text-slate-400 font-mono truncate">{point.fullName || ''}</div>
                                            </div>

                                            <div class="text-right min-w-[120px] shrink-0">
                                                {isSchedule ? (
                                                    <span class="text-sky-400/50 text-[0.65rem] font-black tracking-widest uppercase">
                                                        <i class="fas fa-clock mr-1 opacity-70"></i>Schedule
                                                    </span>
                                                ) : (
                                                    <span class="text-lg font-bold text-sky-400">
                                                        {displayVal}
                                                    </span>
                                                )}
                                                <span class="text-xs text-slate-400 ml-1">{point.units || ''}</span>
                                            </div>

                                            <div class="flex gap-2 shrink-0">
                                                <button 
                                                    class={`btn-icon star-btn ${(window as any).pwCanEditFavorites ? '' : 'hidden'} ${isFav ? 'active text-amber-400' : ''}`}
                                                    onClick={(e) => {
                                                        e.stopPropagation();
                                                        if (typeof (window as any).toggleFavorite === 'function') {
                                                            (window as any).toggleFavorite(point.key);
                                                            setLiveValues(prev => ({ ...prev }));
                                                        }
                                                    }}
                                                    title="Favorite"
                                                >
                                                    <i class={`${isFav ? 'fas' : 'far'} fa-star`}></i>
                                                </button>
                                            </div>
                                        </div>
                                    );
                                })
                            )}
                        </div>
                    </div>
                ) : (
                    <div class="flex-1 flex flex-col items-center justify-center gap-4 text-slate-400 opacity-45 text-sm select-none">
                        <i class="fas fa-network-wired text-4xl"></i>
                        <p>{t('select_node_hint')}</p>
                    </div>
                )}
            </div>
        </div>
    );
}
