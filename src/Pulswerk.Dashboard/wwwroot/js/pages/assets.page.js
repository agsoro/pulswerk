/// <reference path="../types/pulswerk.d.ts" />
let allTrees = [];
let selectedNodeId = null;
let isResizing = false;
export function initAssetsPage() {
    // Splitter Logic
    const resizer = document.getElementById('dragMe');
    const leftSide = document.getElementById('assetTree');
    if (!resizer || !leftSide)
        return;
    resizer.addEventListener('mousedown', function () {
        isResizing = true;
        document.body.style.cursor = 'col-resize';
        document.body.style.userSelect = 'none';
        resizer.classList.add('dragging');
        document.addEventListener('mousemove', mouseMoveHandler);
        document.addEventListener('mouseup', mouseUpHandler);
    });
    function mouseMoveHandler(e) {
        if (!isResizing)
            return;
        requestAnimationFrame(() => {
            const containerRect = leftSide.parentElement.getBoundingClientRect();
            const newWidth = e.clientX - containerRect.left;
            if (newWidth > 150 && newWidth < 800) {
                leftSide.style.width = `${newWidth}px`;
            }
        });
    }
    function mouseUpHandler() {
        isResizing = false;
        document.body.style.cursor = 'default';
        document.body.style.userSelect = 'auto';
        resizer.classList.remove('dragging');
        document.removeEventListener('mousemove', mouseMoveHandler);
        document.removeEventListener('mouseup', mouseUpHandler);
    }
    window.addEventListener('popstate', () => {
        const params = new URLSearchParams(window.location.search);
        const nodeId = params.get('node');
        if (nodeId) {
            selectNodeById(nodeId, true);
        }
    });
    loadTree();
    setInterval(refreshValues, 2000);
    window.addEventListener('resize', () => {
        if (window.historyChart)
            window.historyChart.resize();
    });
    window.selectNodeById = selectNodeById;
}
async function loadTree() {
    try {
        const response = await fetch('?handler=Tree');
        allTrees = await response.json();
        renderTree();
        const params = new URLSearchParams(window.location.search);
        const nodeId = params.get('node');
        if (nodeId) {
            selectNodeById(nodeId, true);
        }
    }
    catch (error) {
        console.error("Failed to load tree:", error);
        const container = document.getElementById('assetTree');
        if (container)
            container.innerHTML = '<div class="h-full flex flex-col items-center justify-center text-slate-400 opacity-50"><p>Error loading hierarchy</p></div>';
    }
}
export function selectNodeById(nodeId, skipPush = false) {
    const row = document.querySelector(`.tree-row[data-id="${nodeId}"]`);
    if (row) {
        let parent = row.closest('.tree-children');
        while (parent) {
            parent.classList.add('expanded');
            const toggle = parent.previousElementSibling?.querySelector('.tree-toggle');
            if (toggle)
                toggle.classList.add('expanded');
            parent = parent.parentElement?.closest('.tree-children') || null;
        }
        if (skipPush)
            row.dataset.skipPush = "true";
        row.click();
        row.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }
}
function renderTree() {
    const container = document.getElementById('assetTree');
    if (!container)
        return;
    container.innerHTML = '';
    allTrees.forEach(device => {
        container.appendChild(createTreeNode(device, 0, []));
    });
}
function createTreeNode(node, depth, path = []) {
    const currentPath = [...path, { id: node.id, name: node.name }];
    const wrapper = document.createElement('div');
    wrapper.className = 'select-none relative';
    const row = document.createElement('div');
    row.className = 'tree-row flex items-center px-4 py-1.5 cursor-pointer transition-all duration-200 text-sm text-slate-400 whitespace-nowrap mx-2 my-px rounded hover:bg-white/5 hover:text-slate-50';
    row.dataset.id = node.id;
    if (node.id === selectedNodeId)
        row.classList.add('active');
    for (let i = 0; i < depth; i++) {
        const indent = document.createElement('div');
        indent.className = 'tree-indent w-5 shrink-0 h-full relative';
        row.appendChild(indent);
    }
    const toggle = document.createElement('div');
    toggle.className = 'tree-toggle w-5 h-5 flex items-center justify-center mr-2 text-[0.7rem] transition-transform duration-200 text-slate-400 z-[1]';
    if (node.children && node.children.length > 0) {
        toggle.innerHTML = '<i class="fas fa-chevron-right"></i>';
        toggle.onclick = (e) => {
            e.stopPropagation();
            const childContainer = wrapper.querySelector('.tree-children');
            if (childContainer)
                childContainer.classList.toggle('expanded');
            toggle.classList.toggle('expanded');
        };
    }
    row.appendChild(toggle);
    const icon = document.createElement('div');
    icon.className = 'tree-icon w-5 mr-3 text-center text-[0.9rem]';
    const iconClass = node.type === 'BACnet Device' ? 'fa-server' :
        (node.isView ? 'fa-folder' : 'fa-tag');
    icon.innerHTML = `<i class="fas ${iconClass}"></i>`;
    row.appendChild(icon);
    const label = document.createElement('span');
    label.textContent = node.name;
    row.appendChild(label);
    row.onclick = () => {
        document.querySelectorAll('.tree-row').forEach(r => r.classList.remove('active'));
        row.classList.add('active');
        selectedNodeId = node.id;
        const url = new URL(window.location.href);
        url.searchParams.set('node', node.id);
        window.history.replaceState({}, '', url);
        showNode(node, currentPath);
    };
    wrapper.appendChild(row);
    if (node.children && node.children.length > 0) {
        const childContainer = document.createElement('div');
        childContainer.className = 'tree-children';
        node.children.forEach((child) => {
            childContainer.appendChild(createTreeNode(child, depth + 1, currentPath));
        });
        wrapper.appendChild(childContainer);
    }
    return wrapper;
}
function showNode(node, path = []) {
    const emptyView = document.getElementById('emptyView');
    const contentView = document.getElementById('contentView');
    const assetContent = document.querySelector('.asset-content');
    const assetSidebar = document.getElementById('assetTree');
    const resizer = document.getElementById('dragMe');
    if (!emptyView || !contentView || !assetContent || !assetSidebar || !resizer)
        return;
    const row = document.querySelector(`.tree-row[data-id="${node.id}"]`);
    if (row && !row.dataset.skipPush) {
        const newUrl = window.location.pathname + '?node=' + node.id;
        history.pushState({ nodeId: node.id }, "", newUrl);
    }
    if (row)
        delete row.dataset.skipPush;
    emptyView.style.display = 'none';
    contentView.style.display = 'flex';
    assetContent.classList.remove('hidden');
    assetSidebar.classList.remove('full-width');
    resizer.style.display = 'block';
    document.getElementById('nodeTitle').textContent = node.name;
    document.getElementById('nodeMeta').textContent = node.id;
    const breadcrumb = document.getElementById('breadcrumb');
    breadcrumb.innerHTML = '';
    path.forEach((p, index) => {
        const link = document.createElement('a');
        link.href = `/Assets?node=${p.id}`;
        link.textContent = p.name;
        link.className = 'text-slate-400 no-underline hover:text-sky-400';
        link.onclick = (e) => {
            e.preventDefault();
            selectNodeById(p.id);
        };
        breadcrumb.appendChild(link);
        if (index < path.length - 1) {
            const sep = document.createElement('i');
            sep.className = 'fas fa-chevron-right text-[0.6rem] opacity-50';
            breadcrumb.appendChild(sep);
        }
    });
    const list = document.getElementById('pointList');
    list.innerHTML = '';
    if (!node.dataPoints || node.dataPoints.length === 0) {
        list.innerHTML = '<div class="h-full flex flex-col items-center justify-center text-slate-400 opacity-50"><i class="fas fa-info-circle text-5xl mb-4"></i><p>No data points in this view</p></div>';
        return;
    }
    node.dataPoints.forEach((point) => {
        const item = document.createElement('div');
        item.className = 'glass border border-slate-700 rounded-lg p-4 mb-3 flex items-center gap-5 transition-all duration-200 hover:border-sky-400 hover:translate-x-1';
        const icon = getPointIcon(point.type || '');
        let displayName = point.name || 'Unnamed';
        const isSchedule = point.type === 'OBJECT_SCHEDULE';
        const valueDisplay = isSchedule
            ? `<span class="text-sky-400/50 text-[0.65rem] font-black tracking-widest uppercase"><i class="fas fa-clock mr-1 opacity-70"></i>Schedule</span>`
            : `<span class="text-lg font-bold text-sky-400 point-value" data-key="${point.key}" data-type="${point.type || ''}">${PulswerkValue.formatDisplay(point.value, point.type)}</span>`;
        item.innerHTML = `
            <div class="w-10 h-10 bg-sky-400/10 rounded-lg flex items-center justify-center text-sky-400 text-xl">${icon}</div>
            <div class="flex-1">
                <div class="font-semibold text-slate-50">${displayName}</div>
                <div class="text-[0.7rem] text-slate-400 font-mono">${point.fullName || ''}</div>
            </div>
            <div class="text-right min-w-[120px]">
                ${valueDisplay}
                <span class="text-xs text-slate-400 ml-1">${point.units || ''}</span>
            </div>
            <div class="flex gap-2">
                <button class="btn-icon star-btn ${window.pwCanEditFavorites ? '' : 'hidden'}" data-key="${point.key}" title="Favorite" onclick="toggleFavorite('${point.key}', this)"><i class="far fa-star"></i></button>
                <button class="btn-icon" title="Trend" onclick="openHistory('${point.key}')"><i class="fas fa-chart-area"></i></button>
                ${point.type === 'OBJECT_SCHEDULE' ? `<button class="btn-icon ${window.pwCanWriteValue ? '' : 'hidden'}" title="Schedule View" data-testid="schedule-btn" onclick="openScheduleView('${point.key}')"><i class="fas fa-calendar-check"></i></button>` : ''}
                ${point.isWritable && point.type !== 'OBJECT_SCHEDULE' ? `<button class="btn-icon ${window.pwCanWriteValue ? '' : 'hidden'}" title="Edit Value" onclick="openEdit('${point.key}')"><i class="fas fa-pen"></i></button>` : ''}
                <button class="btn-icon" title="Properties" onclick="openProperties('${point.key}')"><i class="fas fa-cog"></i></button>
            </div>
        `;
        updateStarState(point.key, item.querySelector('.star-btn'));
        list.appendChild(item);
    });
}
async function refreshValues() {
    try {
        const response = await fetch('?handler=Tree');
        const newTrees = await response.json();
        const updateValues = (nodes) => {
            nodes.forEach(node => {
                if (node.dataPoints) {
                    node.dataPoints.forEach((p) => {
                        const el = document.querySelector(`.point-value[data-key="${p.key}"]`);
                        if (el)
                            el.textContent = PulswerkValue.formatDisplay(p.value, el.dataset.type || p.type);
                        if (window.currentHistoryKey === p.key && document.getElementById('historyModal')?.style.display === 'flex') {
                            const lv = document.getElementById('chartLiveValue');
                            if (lv)
                                lv.textContent = PulswerkValue.formatDisplay(p.value, p.type);
                        }
                    });
                }
                if (node.children)
                    updateValues(node.children);
            });
        };
        updateValues(newTrees);
    }
    catch (e) { }
}
initAssetsPage();
