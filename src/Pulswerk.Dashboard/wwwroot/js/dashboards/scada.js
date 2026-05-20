// @ts-nocheck
import './scada/scada.animation.editor';
// scada.js – SCADA Background SVGs and Data Points
// ── SCADA: BACKGROUND SVG ────────────────────────────────────────────────
function svgColorFilter(hex) {
    const r = parseInt(hex.slice(1, 3), 16) / 255, g = parseInt(hex.slice(3, 5), 16) / 255, b = parseInt(hex.slice(5, 7), 16) / 255;
    const max = Math.max(r, g, b), min = Math.min(r, g, b), l = (max + min) / 2;
    let h = 0, s = 0;
    if (max !== min) {
        const d = max - min;
        s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        if (max === r)
            h = ((g - b) / d + (g < b ? 6 : 0)) / 6;
        else if (max === g)
            h = ((b - r) / d + 2) / 6;
        else
            h = ((r - g) / d + 4) / 6;
    }
    const brightness = Math.max(0.3, l * 1.8);
    return `brightness(0) saturate(100%) invert(${Math.round(l * 100)}%) sepia(80%) saturate(${Math.round(s * 500)}%) hue-rotate(${Math.round(h * 360)}deg) brightness(${brightness.toFixed(2)})`;
}
export function renderBackgroundSvg(w) {
    const bg = document.getElementById('scadaBg');
    if (!bg)
        return;
    const cfg = w.config || {};
    if (!cfg.svg)
        return;
    const canvas = document.getElementById('dashCanvas');
    const cw = canvas.getBoundingClientRect().width;
    const el = document.createElement('div');
    el.className = 'scada-svg-widget' + (isEditing ? ' edit-mode' : '');
    el.dataset.wid = w.id;
    el.style.left = (cfg.posX ?? 5) + '%';
    el.style.top = ((cfg.posY ?? 5) / 100 * cw) + 'px';
    el.style.width = (cfg.posW ?? 90) + '%';
    if (!cfg.baseW) {
        cfg.baseW = cfg.posW || 90;
        cfg.zoom = 100;
    }
    el.innerHTML = cfg.svg;
    const svg = el.querySelector('svg');
    if (svg) {
        svg.removeAttribute('width');
        svg.removeAttribute('height');
        svg.style.width = '100%';
        svg.style.display = 'block';
        svg.style.pointerEvents = 'none';
        // Convert black lines/fills to white (for SVGs created without dark mode)
        svg.querySelectorAll('*').forEach((child) => {
            const stroke = child.getAttribute('stroke');
            if (stroke === 'rgb(0, 0, 0)' || stroke === '#000000') {
                child.setAttribute('stroke', '#ffffff');
            }
            const fill = child.getAttribute('fill');
            if (fill === 'rgb(0, 0, 0)' || fill === '#000000') {
                child.setAttribute('fill', '#ffffff');
            }
            // Also check for inline styles
            let style = child.getAttribute('style') || '';
            if (style) {
                let newStyle = style;
                newStyle = newStyle.replace(/stroke:\s*(rgb\(0,\s*0,\s*0\)|#000000);?/g, 'stroke: #ffffff;');
                newStyle = newStyle.replace(/fill:\s*(rgb\(0,\s*0,\s*0\)|#000000);?/g, 'fill: #ffffff;');
                if (newStyle !== style) {
                    child.setAttribute('style', newStyle);
                    style = newStyle;
                }
            }
            // Enforce transparent strokes
            if (stroke === 'none' || stroke === 'transparent' || style.includes('stroke: none') || style.includes('stroke: transparent')) {
                child.setAttribute('style', (child.getAttribute('style') || '') + '; stroke: none !important;');
            }
            // Enforce transparent fills
            if (fill === 'none' || fill === 'transparent' || style.includes('fill: none') || style.includes('fill: transparent')) {
                child.setAttribute('style', (child.getAttribute('style') || '') + '; fill: none !important;');
            }
            // Enforce invisibility for pure bounding boxes
            if ((fill === 'none' && stroke === 'none') || (fill === 'transparent' && stroke === 'transparent')) {
                child.setAttribute('style', (child.getAttribute('style') || '') + '; opacity: 0 !important; stroke: none !important; fill: none !important;');
            }
        });
    }
    const toolbar = document.createElement('div');
    toolbar.className = 'svg-toolbar';
    toolbar.innerHTML = `<button class="svg-zoom-btn" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();setSvgZoom('${w.id}',(parseInt(this.parentNode.querySelector('.svg-zoom-input').value)||100)-10)" title="Zoom out"><i class="fas fa-minus"></i></button>
        <input type="number" class="svg-zoom-input" value="${cfg.zoom || 100}" min="10" max="500" step="10" onmousedown="event.stopPropagation()" onclick="event.stopPropagation()" oninput="setSvgZoom('${w.id}',this.value)">
        <span style="color:#64748b;font-size:0.65rem">%</span>
        <button class="svg-zoom-btn" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();setSvgZoom('${w.id}',(parseInt(this.parentNode.querySelector('.svg-zoom-input').value)||100)+10)" title="Zoom in"><i class="fas fa-plus"></i></button>
        <button class="svg-zoom-btn" style="font-size:0.6rem;font-weight:700;letter-spacing:-0.5px" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();setSvgZoom('${w.id}',100)" title="Reset to 1:1">1:1</button>
        <span style="width:1px;height:14px;background:rgba(255,255,255,0.1);margin:0 2px"></span>
        <button class="svg-zoom-btn" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();editBackgroundSvg('${w.id}')" title="Edit in draw.io"><i class="fas fa-drafting-compass"></i></button>
        <button class="svg-zoom-btn" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();toggleIdPicker('${w.id}')" title="Name / rename SVG elements"><i class="fas fa-tag"></i></button>
        <button class="svg-zoom-btn" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();editWidget('${w.id}')" title="Configure"><i class="fas fa-cog"></i></button>
        <button class="svg-zoom-btn" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();duplicateWidget('${w.id}')" title="Duplicate"><i class="fas fa-copy"></i></button>
        <button class="svg-zoom-btn sp-del" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();removeWidget('${w.id}')" title="Delete"><i class="fas fa-trash"></i></button>`;
    el.appendChild(toolbar);
    const rh = document.createElement('div');
    rh.className = 'svg-resize';
    el.appendChild(rh);
    el.addEventListener('click', e => {
        if (!isEditing || e.target.closest('.svg-toolbar'))
            return;
        e.stopPropagation();
        const wasSelected = el.classList.contains('selected');
        document.querySelectorAll('.scada-svg-widget.selected').forEach(s => s.classList.remove('selected'));
        if (!wasSelected)
            el.classList.add('selected');
    });
    bg.appendChild(el);
    if (isEditing)
        initSvgWidgetDrag(el, w);
    expandCanvasToFit();
}
function expandCanvasToFit() {
    const canvas = document.getElementById('dashCanvas');
    if (!canvas)
        return;
    let maxBottom = 500;
    document.querySelectorAll('.scada-svg-widget').forEach(el => {
        const rect = el.getBoundingClientRect();
        const canvasRect = canvas.getBoundingClientRect();
        const bottom = rect.top - canvasRect.top + rect.height;
        maxBottom = Math.max(maxBottom, bottom + 40);
    });
    canvas.style.minHeight = Math.ceil(maxBottom) + 'px';
}
document.addEventListener('click', e => {
    if (!e.target.closest('.scada-svg-widget')) {
        document.querySelectorAll('.scada-svg-widget.selected').forEach(s => s.classList.remove('selected'));
    }
});
function setSvgZoom(wid, val) {
    const w = dashboard.widgets.find(x => x.id === wid);
    if (!w)
        return;
    const cfg = w.config || {};
    const newZoom = Math.max(10, Math.min(500, parseInt(val) || 100));
    const baseW = cfg.baseW || 90;
    cfg.posW = Math.round(baseW * newZoom / 100 * 10) / 10;
    cfg.zoom = newZoom;
    const el = document.querySelector(`.scada-svg-widget[data-wid="${wid}"]`);
    if (el) {
        el.style.width = cfg.posW + '%';
        const inp = el.querySelector('.svg-zoom-input');
        if (inp)
            inp.value = String(newZoom);
    }
    expandCanvasToFit();
}
function initSvgWidgetDrag(el, w) {
    const canvas = document.getElementById('dashCanvas');
    let startX, startY, startLeft, startTop, dragging = false;
    el.addEventListener('mousedown', e => {
        if (!isEditing || e.target.closest('.svg-toolbar') || e.target.closest('.svg-resize'))
            return;
        e.preventDefault();
        dragging = true;
        el.classList.add('dragging');
        const cw = canvas.getBoundingClientRect().width;
        startX = e.clientX;
        startY = e.clientY;
        startLeft = parseFloat(el.style.left) || 5;
        startTop = (parseFloat(el.style.top) || 0) / cw * 100;
        const onMove = (ev) => {
            const r = canvas.getBoundingClientRect();
            const newLeft = startLeft + (ev.clientX - startX) / r.width * 100;
            const newTop = startTop + (ev.clientY - startY) / r.width * 100;
            el.style.left = Math.max(0, newLeft) + '%';
            el.style.top = (Math.max(0, newTop) / 100 * r.width) + 'px';
            repositionAnchoredPoints();
        };
        const onUp = () => {
            dragging = false;
            el.classList.remove('dragging');
            const r = canvas.getBoundingClientRect();
            if (w.config) {
                w.config.posX = parseFloat(el.style.left);
                w.config.posY = parseFloat(el.style.top) / r.width * 100;
            }
            document.removeEventListener('mousemove', onMove);
            document.removeEventListener('mouseup', onUp);
            expandCanvasToFit();
        };
        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup', onUp);
    });
    const rh = el.querySelector('.svg-resize');
    if (rh)
        rh.addEventListener('mousedown', e => {
            e.preventDefault();
            e.stopPropagation();
            const startW = parseFloat(el.style.width) || 90;
            const sx = e.clientX;
            const onMove = (ev) => {
                const r = canvas.getBoundingClientRect();
                const newW = Math.max(5, startW + (ev.clientX - sx) / r.width * 100);
                el.style.width = newW + '%';
                repositionAnchoredPoints();
            };
            const onUp = () => {
                if (w.config) {
                    w.config.posW = parseFloat(el.style.width);
                    const baseW = w.config.baseW || 90;
                    w.config.zoom = Math.round(w.config.posW / baseW * 100);
                    const zoomInput = el.querySelector('.svg-zoom-input');
                    if (zoomInput)
                        zoomInput.value = String(w.config.zoom);
                }
                expandCanvasToFit();
                document.removeEventListener('mousemove', onMove);
                document.removeEventListener('mouseup', onUp);
            };
            document.addEventListener('mousemove', onMove);
            document.addEventListener('mouseup', onUp);
        });
}
// ── SCADA: DATA POINT RENDERING ──────────────────────────────────────────
export function renderScadaPoint(w) {
    const layer = document.getElementById('scadaPoints');
    if (!layer)
        return;
    const cfg = w.config || {};
    const keys = cfg.keys || [];
    if (!keys.length)
        return;
    const layout = cfg.layout || 'vertical';
    const el = document.createElement('div');
    el.className = 'scada-point layout-' + layout + (isEditing ? ' edit-mode' : '');
    el.dataset.wid = w.id;
    if (cfg.anchorSvg)
        el.dataset.anchorSvg = cfg.anchorSvg;
    const edge = cfg.anchorEdge || '';
    const edgeDots = ['top', 'right', 'bottom', 'left'].map(d => `<div class="sp-edge-dot sp-edge-${d}${d === edge ? ' active' : ''}" data-edge="${d}" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();setPointEdge('${w.id}','${d}',this)"></div>`).join('');
    const hasTitle = w.title && w.title !== 'Untitled';
    let html = edgeDots;
    if (hasTitle) {
        html += `<div style="font-size:0.72rem;font-weight:600;color:#cbd5e1;letter-spacing:0.02em;padding:0.2rem 0.35rem;margin-bottom:0.2rem;border-bottom:1px solid rgba(255,255,255,0.08);display:flex;align-items:center;gap:0.3rem;background:rgba(255,255,255,0.03);border-radius:4px 4px 0 0">
            <span style="flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${esc(w.title || '')}</span>
            <i class="fas fa-cog sp-action" title="Edit" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();editWidget('${w.id}')"></i>
            <i class="fas fa-copy sp-action" title="Duplicate" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();duplicateWidget('${w.id}')"></i>
            <i class="fas fa-trash sp-delete" title="Delete" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();removeWidget('${w.id}')"></i>
        </div>`;
    }
    else {
        html += `<div class="sp-action-bar" style="position:absolute;top:-8px;right:-6px;display:none;gap:2px;z-index:5">
            <i class="fas fa-cog sp-action-btn" title="Edit" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();editWidget('${w.id}')"></i>
            <i class="fas fa-copy sp-action-btn" title="Duplicate" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();duplicateWidget('${w.id}')"></i>
            <i class="fas fa-trash sp-action-btn sp-del" title="Delete" onmousedown="event.stopPropagation()" onclick="event.stopPropagation();removeWidget('${w.id}')"></i>
        </div>`;
    }
    html += '<div class="sp-rows">';
    keys.forEach((key, i) => {
        const meta = allKeys.find(k => k.key === key) || { key, name: key };
        const name = meta.name || friendlyName(key);
        const units = meta.units || '';
        const borderStyle = layout === 'vertical' && i < keys.length - 1 ? 'border-bottom:1px solid rgba(255,255,255,0.04);' : '';
        html += `<div class="sp-row" style="display:flex;align-items:center;gap:0.4rem;padding:0.15rem 0;${borderStyle}">
            <span style="flex:1;font-size:0.68rem;color:#94a3b8;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;max-width:120px">${esc(name)}</span>
            <span class="sp-val" data-key="${key}">---</span>
            ${units ? `<span class="sp-unit">${esc(units)}</span>` : ''}
            <i class="fas fa-info-circle sp-info" onclick="event.stopPropagation();showScadaPopup('${key}',this)"></i>
        </div>`;
    });
    html += '</div>';
    el.innerHTML = html;
    layer.appendChild(el);
    requestAnimationFrame(() => positionScadaPoint(el, cfg));
    if (isEditing)
        initScadaPointDrag(el, w);
    updateScadaPointValues(w);
}
function getSvgContentBox(containerEl) {
    const r = containerEl.getBoundingClientRect();
    const canvas = document.getElementById('dashCanvas');
    const cr = canvas ? canvas.getBoundingClientRect() : { left: 0, top: 0 };
    const bl = containerEl.clientLeft || 0, bt = containerEl.clientTop || 0;
    const br = containerEl.offsetWidth - containerEl.clientWidth - bl;
    const bb = containerEl.offsetHeight - containerEl.clientHeight - bt;
    return { left: r.left - cr.left + bl, top: r.top - cr.top + bt, width: r.width - bl - br, height: r.height - bt - bb };
}
function centerDotOffset(edge, w, h) {
    if (edge === 'top')
        return { x: 0, y: -h / 2 };
    if (edge === 'right')
        return { x: w / 2, y: 0 };
    if (edge === 'bottom')
        return { x: 0, y: h / 2 };
    if (edge === 'left')
        return { x: -w / 2, y: 0 };
    return { x: 0, y: 0 };
}
function positionScadaPoint(el, cfg) {
    const anchorId = cfg.anchorSvg;
    if (anchorId) {
        const svgEl = document.querySelector(`.scada-svg-widget[data-wid="${anchorId}"]`);
        if (svgEl) {
            const box = getSvgContentBox(svgEl);
            const dotPx = box.left + (cfg.posX || 0) / 100 * box.width;
            const dotPy = box.top + (cfg.posY || 0) / 100 * box.height;
            const r = el.getBoundingClientRect();
            const off = centerDotOffset(cfg.anchorEdge || '', r.width, r.height);
            el.style.left = (dotPx - off.x) + 'px';
            el.style.top = (dotPy - off.y) + 'px';
            return;
        }
    }
    el.style.left = (cfg.posX || 50) + '%';
    el.style.top = (cfg.posY || 50) + '%';
}
function repositionAnchoredPoints() {
    if (!dashboard?.widgets)
        return;
    document.querySelectorAll('.scada-point[data-anchor-svg]').forEach(el => {
        const w = dashboard.widgets.find(x => x.id === el.dataset.wid);
        if (w && w.config?.anchorSvg)
            positionScadaPoint(el, w.config);
    });
}
function findOverlappingSvg(pointEl) {
    const pr = pointEl.getBoundingClientRect();
    const cx = pr.left + pr.width / 2, cy = pr.top + pr.height / 2;
    let best = null;
    document.querySelectorAll('.scada-svg-widget').forEach(svgEl => {
        const sr = svgEl.getBoundingClientRect();
        if (cx >= sr.left && cx <= sr.right && cy >= sr.top && cy <= sr.bottom)
            best = svgEl;
    });
    return best;
}
function setPointEdge(wid, edge, dotEl) {
    const w = dashboard?.widgets?.find(x => x.id === wid);
    if (!w)
        return;
    const cfg = w.config || {}, parent = dotEl.closest('.scada-point');
    if (!parent)
        return;
    if (cfg.anchorEdge === edge && cfg.anchorSvg) {
        cfg.anchorEdge = '';
        cfg.anchorSvg = '';
        delete cfg.anchorW;
        delete cfg.anchorH;
        delete parent.dataset.anchorSvg;
    }
    else {
        const svgEl = findOverlappingSvg(parent);
        if (!svgEl)
            return;
        const svgWid = svgEl.dataset.wid;
        cfg.anchorSvg = svgWid;
        cfg.anchorEdge = edge;
        parent.dataset.anchorSvg = svgWid;
        const box = getSvgContentBox(svgEl);
        const off = centerDotOffset(edge, parent.offsetWidth, parent.offsetHeight);
        cfg.posX = (parent.offsetLeft + off.x - box.left) / box.width * 100;
        cfg.posY = (parent.offsetTop + off.y - box.top) / box.height * 100;
        cfg.anchorW = box.width;
        cfg.anchorH = box.height;
    }
    w.config = cfg;
    parent.querySelectorAll('.sp-edge-dot').forEach(d => d.classList.toggle('active', d.dataset.edge === cfg.anchorEdge));
}
function updateEdgeDotVisibility(pointEl) {
    if (!isEditing)
        return;
    const overSvg = !!findOverlappingSvg(pointEl), anchored = !!pointEl.dataset.anchorSvg;
    pointEl.querySelectorAll('.sp-edge-dot').forEach(d => d.style.display = (overSvg || anchored) ? 'block' : 'none');
}
window.addEventListener('resize', () => { repositionAnchoredPoints(); });
async function updateScadaPointValues(w) {
    const cfg = w.config || {}, keys = cfg.keys || [];
    if (!keys.length)
        return;
    let data;
    try {
        data = await api(`LatestValues&keys=${keys.join(',')}`);
    }
    catch (e) {
        return;
    }
    renderScadaValues(w.id, keys, data);
    if (cfg.anchorSvg) {
        const el = document.querySelector(`.scada-point[data-wid="${w.id}"]`);
        if (el)
            requestAnimationFrame(() => positionScadaPoint(el, cfg));
    }
}
function renderScadaValues(wid, keys, data) {
    keys.forEach(key => {
        const el = document.querySelector(`.scada-point[data-wid="${wid}"] .sp-val[data-key="${key}"]`);
        if (!el)
            return;
        const val = data?.[key] || '---', meta = allKeys.find(k => k.key === key) || { key, name: key }, type = meta.type || '', display = PulswerkValue.formatDisplay(val, type);
        if (PulswerkValue.isBinary(type)) {
            const isOn = display === '1' || ['on', 'ein', 'active', 'ja', 'yes'].includes(display.toLowerCase());
            el.innerHTML = `<span class="sp-dot" style="background:${isOn ? '#34d399' : '#64748b'}"></span><span class="sp-bool ${isOn ? 'on' : 'off'}">${esc(display)}</span>`;
        }
        else {
            el.textContent = display;
        }
        updateHistoryLiveValue(key, display);
    });
}
async function updateAllScadaPoints() {
    if (!dashboard?.widgets)
        return;
    const scadaWidgets = dashboard.widgets.filter(w => w.type === 'scada-point'), allScadaKeys = [...new Set(scadaWidgets.flatMap(w => (w.config?.keys || [])))];
    if (!allScadaKeys.length)
        return;
    let data;
    try {
        data = await api(`LatestValues&keys=${allScadaKeys.join(',')}`);
    }
    catch (e) {
        return;
    }
    scadaWidgets.forEach(w => renderScadaValues(w.id, w.config?.keys || [], data));
}
async function showScadaPopup(key, triggerEl) {
    hideScadaPopup();
    const popup = document.getElementById('scadaPopup'), content = document.getElementById('scadaPopupContent');
    if (!popup || !content)
        return;
    if (!allKeys.length) {
        try {
            allKeys = await api('AvailableKeys');
        }
        catch (e) {
            allKeys = [];
        }
    }
    const meta = allKeys.find(k => k.key === key) || { key, name: key }, icon = typeof getPointIcon === 'function' ? getPointIcon(meta.type || '') : '<i class="fas fa-microchip"></i>', keyJs = key.replace(/'/g, "\\'");
    const pp = meta.parentPath || [], pathHtml = pp.map((p, i) => `<a href="/plswk/Assets?node=${p.id}" style="color:inherit;text-decoration:none">${esc(p.name)}</a>${i < pp.length - 1 ? '<i class="fas fa-chevron-right" style="margin:0 0.4rem;font-size:0.55rem;opacity:0.4"></i>' : ''}`).join('');
    let currentVal = '---';
    try {
        const data = await api(`LatestValues&keys=${key}`), raw = data?.[key];
        if (raw != null)
            currentVal = PulswerkValue.formatDisplay(raw, meta.type);
    }
    catch (e) { }
    content.innerHTML = `<div class="sv-card" style="height:auto"><div class="sv-card-path">${pathHtml || '<span style="opacity:0.4">\u2014</span>'}</div><div class="sv-card-body"><div class="sv-card-icon">${icon}</div><div class="sv-card-info"><div class="sv-card-name">${esc(meta.name || friendlyName(key))}</div><div class="sv-card-fullname">${esc(meta.fullName || key)}</div></div><div class="sv-card-valbox"><span class="sv-card-val">${esc(currentVal)}</span><span class="sv-card-units">${esc(meta.units || '')}</span></div></div><div class="sv-card-actions"><button class="btn-icon" title="Trend" onclick="hideScadaPopup();openHistory('${keyJs}')"><i class="fas fa-chart-area"></i></button>${meta.isWritable ? `<button class="btn-icon" title="Edit" onclick="hideScadaPopup();openEdit('${keyJs}')"><i class="fas fa-pen"></i></button>` : ''}<button class="btn-icon" title="Properties" onclick="hideScadaPopup();openProperties('${keyJs}')"><i class="fas fa-cog"></i></button></div></div>`;
    const rect = triggerEl.getBoundingClientRect();
    popup.style.left = Math.min(rect.right + 8, window.innerWidth - 360) + 'px';
    popup.style.top = Math.max(rect.top - 20, 8) + 'px';
    popup.style.display = 'block';
    popup.classList.add('show');
    setTimeout(() => { const closer = (e) => { if (!popup.contains(e.target)) {
        hideScadaPopup();
        document.removeEventListener('click', closer);
    } }; document.addEventListener('click', closer); }, 10);
}
function hideScadaPopup() { const popup = document.getElementById('scadaPopup'); if (popup) {
    popup.style.display = 'none';
    popup.classList.remove('show');
} }
function initScadaPointDrag(el, w) {
    let startX, startY, startPx, startPy, dragging = false;
    const canvas = document.getElementById('dashCanvas');
    el.addEventListener('mousedown', e => {
        if (!isEditing || e.target.closest('.sp-info') || e.target.closest('.sp-delete') || e.target.closest('.sp-edge-dot'))
            return;
        e.preventDefault();
        dragging = true;
        el.classList.add('dragging');
        startX = e.clientX;
        startY = e.clientY;
        startPx = el.offsetLeft;
        startPy = el.offsetTop;
        const onMove = (ev) => { if (!dragging)
            return; el.style.left = (startPx + (ev.clientX - startX)) + 'px'; el.style.top = (startPy + (ev.clientY - startY)) + 'px'; };
        const onUp = () => {
            dragging = false;
            el.classList.remove('dragging');
            if (w) {
                const cfg = w.config || {}, anchorId = cfg.anchorSvg;
                if (anchorId) {
                    const svgEl = document.querySelector(`.scada-svg-widget[data-wid="${anchorId}"]`);
                    if (svgEl) {
                        const box = getSvgContentBox(svgEl), edge = cfg.anchorEdge || '', off = centerDotOffset(edge, el.offsetWidth, el.offsetHeight);
                        cfg.posX = (el.offsetLeft + off.x - box.left) / box.width * 100;
                        cfg.posY = (el.offsetTop + off.y - box.top) / box.height * 100;
                        cfg.anchorW = box.width;
                        cfg.anchorH = box.height;
                    }
                }
                else {
                    cfg.posX = el.offsetLeft / canvas.clientWidth * 100;
                    cfg.posY = el.offsetTop / canvas.clientHeight * 100;
                }
                w.config = cfg;
            }
            updateEdgeDotVisibility(el);
            document.removeEventListener('mousemove', onMove);
            document.removeEventListener('mouseup', onUp);
        };
        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup', onUp);
    });
}
function handleSvgFileSelect(input) {
    if (!input.files?.length)
        return;
    const reader = new FileReader();
    reader.onload = e => {
        pendingSvgContent = e.target?.result;
        const preview = document.getElementById('svgPreviewThumb'), label = document.getElementById('svgDropLabel');
        if (preview) {
            preview.innerHTML = pendingSvgContent;
            const svg = preview.querySelector('svg');
            if (svg) {
                svg.style.width = '100%';
                svg.style.height = 'auto';
                svg.style.maxHeight = '160px';
            }
            preview.classList.remove('hidden');
        }
        if (label)
            label.classList.add('hidden');
    };
    reader.readAsText(input.files[0]);
}
let drawioCallback = null, drawioWindow = null, _drawioPending = '', _drawioIsXml = false, _drawioExitAfterSave = false;
function openDrawioEditor(svgContent, onSave) {
    drawioCallback = onSave;
    _drawioPending = '';
    _drawioIsXml = false;
    if (svgContent) {
        const trimmed = svgContent.trim();
        if (trimmed.startsWith('<mxfile') || trimmed.startsWith('<mxGraphModel')) {
            _drawioPending = svgContent;
            _drawioIsXml = true;
        }
        else if (trimmed.startsWith('<svg') && svgContent.includes('content=')) {
            try {
                const parser = new DOMParser(), doc = parser.parseFromString(svgContent, 'image/svg+xml'), svgEl = doc.querySelector('svg'), diagramXml = svgEl?.getAttribute('content');
                if (diagramXml) {
                    _drawioPending = diagramXml;
                    _drawioIsXml = true;
                }
                else {
                    _drawioPending = svgContent;
                }
            }
            catch (e) {
                _drawioPending = svgContent;
            }
        }
        else {
            _drawioPending = svgContent;
        }
    }
    drawioWindow = window.open('https://embed.diagrams.net/?embed=1&ui=dark&spin=1&proto=json&saveAndExit=1&noExitBtn=0&modified=unsavedChanges', '_blank');
}
function closeDrawioEditor() { if (drawioWindow && !drawioWindow.closed)
    drawioWindow.close(); drawioWindow = null; drawioCallback = null; }
export function fitWidgetToSvg(w) {
    const cfg = w.config;
    if (!cfg?.svg)
        return;
    try {
        const parser = new DOMParser(), doc = parser.parseFromString(cfg.svg, 'image/svg+xml'), svgEl = doc.querySelector('svg');
        if (!svgEl)
            return;
        let svgW, svgH;
        const vb = svgEl.getAttribute('viewBox');
        if (vb) {
            const parts = vb.split(/[\s,]+/).map(Number);
            if (parts.length >= 4) {
                svgW = parts[2];
                svgH = parts[3];
            }
        }
        if (!svgW)
            svgW = parseFloat(svgEl.getAttribute('width') || '0') || 0;
        if (!svgH)
            svgH = parseFloat(svgEl.getAttribute('height') || '0') || 0;
        if (svgW > 0 && svgH > 0) {
            const canvas = document.getElementById('dashCanvas'), cr = canvas?.getBoundingClientRect(), containerW = cr?.width || 1200, containerH = cr?.height || 800;
            cfg.baseW = Math.round(svgW / containerW * 1000) / 10;
            cfg.baseH = Math.round(svgH / containerH * 1000) / 10;
            const zoom = cfg.zoom || 100;
            cfg.posW = Math.round(cfg.baseW * zoom / 100 * 10) / 10;
            cfg.posH = Math.round(cfg.baseH * zoom / 100 * 10) / 10;
        }
    }
    catch (e) { }
}
function editBackgroundSvg(wid) {
    const bgWidget = dashboard.widgets.find(w => w.id === wid && w.type === 'background-svg');
    if (!bgWidget)
        return;
    openDrawioEditor(bgWidget.config?.svg || '', newSvg => {
        bgWidget.config = bgWidget.config || {};
        bgWidget.config.svg = newSvg;
        fitWidgetToSvg(bgWidget);
        const old = document.querySelector(`.scada-svg-widget[data-wid="${wid}"]`);
        if (old)
            old.remove();
        renderBackgroundSvg(bgWidget);
    });
}
function createSvgInDrawio() {
    openDrawioEditor('', newSvg => {
        pendingSvgContent = newSvg;
        const preview = document.getElementById('svgPreviewThumb');
        if (preview) {
            preview.innerHTML = newSvg;
            const svg = preview.querySelector('svg');
            if (svg) {
                svg.style.width = '100%';
                svg.style.height = 'auto';
                svg.style.maxHeight = '160px';
                const color = document.getElementById('svgColor')?.value || '#38bdf8';
                svg.style.filter = svgColorFilter(color);
                svg.style.opacity = '0.6';
            }
            preview.classList.remove('hidden');
        }
    });
}
window.addEventListener('message', function (evt) {
    if (!evt.data || typeof evt.data !== 'string')
        return;
    let msg;
    try {
        msg = JSON.parse(evt.data);
    }
    catch {
        return;
    }
    const target = drawioWindow && !drawioWindow.closed ? drawioWindow : null;
    if (!target)
        return;
    if (msg.event === 'init') {
        const content = _drawioPending || '';
        if (!content)
            target.postMessage(JSON.stringify({ action: 'load', xml: '<mxGraphModel><root><mxCell id="0"/><mxCell id="1" parent="0"/></root></mxGraphModel>', autosave: 0 }), '*');
        else if (_drawioIsXml)
            target.postMessage(JSON.stringify({ action: 'load', xml: content, autosave: 0 }), '*');
        else
            target.postMessage(JSON.stringify({ action: 'load', xml: '<mxGraphModel><root><mxCell id="0"/><mxCell id="1" parent="0"/></root></mxGraphModel>', autosave: 0 }), '*');
    }
    else if (msg.event === 'save') {
        _drawioExitAfterSave = !!msg.exit;
        target.postMessage(JSON.stringify({ action: 'export', format: 'xmlsvg', border: 0 }), '*');
    }
    else if (msg.event === 'export') {
        let svgContent = msg.data || '';
        if (svgContent.startsWith('data:image/svg+xml;base64,')) {
            try {
                svgContent = atob(svgContent.split(',')[1]);
            }
            catch (_) { }
        }
        else if (svgContent.startsWith('data:image/svg+xml,')) {
            try {
                svgContent = decodeURIComponent(svgContent.split(',')[1]);
            }
            catch (_) { }
        }
        if (drawioCallback && svgContent)
            drawioCallback(svgContent);
        if (_drawioExitAfterSave)
            closeDrawioEditor();
        else
            target.postMessage(JSON.stringify({ action: 'status', message: 'Saved', modified: false }), '*');
    }
    else if (msg.event === 'exit')
        closeDrawioEditor();
});
function parseSvgElementIds(svgContent) {
    if (!svgContent)
        return [];
    try {
        const parser = new DOMParser();
        const doc = parser.parseFromString(svgContent, 'image/svg+xml');
        const ids = [];
        doc.querySelectorAll('[id], [data-cell-id]').forEach(el => {
            const id = el.getAttribute('id') || el.getAttribute('data-cell-id');
            if (!id)
                return;
            if (/^(cell|edge|mxCell|mxGraphModel|graph|foreignObject)\b/i.test(id))
                return;
            if (el.tagName === 'svg')
                return;
            ids.push(id);
        });
        return [...new Set(ids)].sort();
    }
    catch (e) {
        return [];
    }
}
async function updateAllSvgAnimations() {
    if (window.ScadaAnimationController)
        await window.ScadaAnimationController.updateAll();
}
function toggleIdPicker(wid) {
    if (window.ScadaPickerController)
        window.ScadaPickerController.toggle(wid);
}
function closeIdPicker() {
    if (window.ScadaPickerController)
        window.ScadaPickerController.close();
}
function confirmRenameElement() {
    if (window.ScadaPickerController)
        window.ScadaPickerController.confirmRename();
}
function cancelRenameElement() {
    if (window.ScadaPickerController)
        window.ScadaPickerController.cancelRename();
}
function highlightPickerElement(idx, wid) {
    if (window.ScadaPickerController)
        window.ScadaPickerController.highlightElement(idx, wid);
}
function unhighlightPickerElement(idx, wid) {
    if (window.ScadaPickerController)
        window.ScadaPickerController.unhighlightElement(idx, wid);
}
function clickPickerElement(idx, wid) {
    if (window.ScadaPickerController)
        window.ScadaPickerController.clickElement(idx, wid);
}
window.svgColorFilter = svgColorFilter;
window.renderBackgroundSvg = renderBackgroundSvg;
window.expandCanvasToFit = expandCanvasToFit;
window.setSvgZoom = setSvgZoom;
window.initSvgWidgetDrag = initSvgWidgetDrag;
window.renderScadaPoint = renderScadaPoint;
window.getSvgContentBox = getSvgContentBox;
window.centerDotOffset = centerDotOffset;
window.positionScadaPoint = positionScadaPoint;
window.repositionAnchoredPoints = repositionAnchoredPoints;
window.findOverlappingSvg = findOverlappingSvg;
window.setPointEdge = setPointEdge;
window.updateEdgeDotVisibility = updateEdgeDotVisibility;
window.updateScadaPointValues = updateScadaPointValues;
window.renderScadaValues = renderScadaValues;
window.updateAllScadaPoints = updateAllScadaPoints;
window.showScadaPopup = showScadaPopup;
window.hideScadaPopup = hideScadaPopup;
window.initScadaPointDrag = initScadaPointDrag;
window.handleSvgFileSelect = handleSvgFileSelect;
window.openDrawioEditor = openDrawioEditor;
window.closeDrawioEditor = closeDrawioEditor;
window.fitWidgetToSvg = fitWidgetToSvg;
window.editBackgroundSvg = editBackgroundSvg;
window.createSvgInDrawio = createSvgInDrawio;
window.parseSvgElementIds = parseSvgElementIds;
window.updateAllSvgAnimations = updateAllSvgAnimations;
window.toggleIdPicker = toggleIdPicker;
window.closeIdPicker = closeIdPicker;
window.confirmRenameElement = confirmRenameElement;
window.cancelRenameElement = cancelRenameElement;
window.highlightPickerElement = highlightPickerElement;
window.unhighlightPickerElement = unhighlightPickerElement;
window.clickPickerElement = clickPickerElement;
