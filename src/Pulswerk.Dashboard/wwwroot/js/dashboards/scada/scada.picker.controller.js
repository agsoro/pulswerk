import { DrawioCodec } from './drawio.codec';
export class ScadaPickerController {
    static widgetId = null;
    static hoveredEl = null;
    static selectedEl = null;
    static tags = new Set([
        'g', 'path', 'rect', 'circle', 'ellipse', 'line',
        'polyline', 'polygon', 'text', 'use', 'image', 'switch'
    ]);
    static toggle(wid) {
        if (this.widgetId === wid) {
            this.close();
            return;
        }
        this.close();
        this.widgetId = wid;
        const widgetEl = document.querySelector(`.scada-svg-widget[data-wid="${wid}"]`);
        if (!widgetEl)
            return;
        widgetEl.classList.add('id-picker-mode');
        const svg = widgetEl.querySelector('svg');
        if (!svg)
            return;
        svg.addEventListener('mouseover', this.onMouseOver);
        svg.addEventListener('mouseout', this.onMouseOut);
        svg.addEventListener('click', this.onClick);
        this.renderPanel(wid);
    }
    static close() {
        if (!this.widgetId)
            return;
        const widgetEl = document.querySelector(`.scada-svg-widget[data-wid="${this.widgetId}"]`);
        if (widgetEl) {
            widgetEl.classList.remove('id-picker-mode');
            const svg = widgetEl.querySelector('svg');
            if (svg) {
                svg.removeEventListener('mouseover', this.onMouseOver);
                svg.removeEventListener('mouseout', this.onMouseOut);
                svg.removeEventListener('click', this.onClick);
            }
        }
        if (this.hoveredEl) {
            this.hoveredEl.classList.remove('scada-id-hover-highlight');
            this.hoveredEl = null;
        }
        if (this.selectedEl) {
            this.selectedEl.classList.remove('scada-id-selected');
            this.selectedEl = null;
        }
        const panel = document.getElementById('scadaIdPanel');
        if (panel)
            panel.classList.remove('open');
        const popup = document.getElementById('scadaRenamePopup');
        if (popup)
            popup.classList.remove('open');
        this.widgetId = null;
    }
    static findTarget(e) {
        let el = e.target;
        let target = el?.closest('[id]:not(svg), [data-cell-id]:not(svg)');
        if (!target) {
            while (el && el.tagName !== 'svg') {
                if (ScadaPickerController.tags.has(el.tagName?.toLowerCase()))
                    return el;
                el = el.parentElement;
            }
            return null;
        }
        return target;
    }
    static onMouseOver = (e) => {
        const target = ScadaPickerController.findTarget(e);
        if (!target || target === ScadaPickerController.hoveredEl)
            return;
        if (ScadaPickerController.hoveredEl)
            ScadaPickerController.hoveredEl.classList.remove('scada-id-hover-highlight');
        ScadaPickerController.hoveredEl = target;
        target.classList.add('scada-id-hover-highlight');
        ScadaPickerController.highlightPanelItem(target.getAttribute('id') || target.getAttribute('data-cell-id'));
    };
    static onMouseOut = (_e) => {
        if (ScadaPickerController.hoveredEl) {
            ScadaPickerController.hoveredEl.classList.remove('scada-id-hover-highlight');
            ScadaPickerController.hoveredEl = null;
        }
        ScadaPickerController.highlightPanelItem(null);
    };
    static onClick = (e) => {
        e.preventDefault();
        e.stopPropagation();
        const target = ScadaPickerController.findTarget(e);
        if (!target)
            return;
        if (ScadaPickerController.selectedEl)
            ScadaPickerController.selectedEl.classList.remove('scada-id-selected');
        ScadaPickerController.selectedEl = target;
        target.classList.add('scada-id-selected');
        ScadaPickerController.showRenamePopup(target, e.clientX, e.clientY);
    };
    static showRenamePopup(el, x, y) {
        let popup = document.getElementById('scadaRenamePopup');
        if (!popup)
            return;
        const tagName = el.tagName?.toLowerCase() || '?';
        const currentId = el.getAttribute('id') || el.getAttribute('data-cell-id') || '';
        const esc = window.esc || ((s) => s);
        popup.querySelector('.scada-rename-tag').innerHTML =
            `&lt;<span>${esc(tagName)}</span>&gt; ${currentId ? 'id="' + esc(currentId) + '"' : '<em>no id</em>'}`;
        const input = popup.querySelector('.scada-rename-input');
        input.value = currentId;
        input.dataset.tagName = tagName;
        const pw = 260, ph = 160;
        let popX = Math.min(x + 12, window.innerWidth - pw - 16);
        let popY = Math.min(y - 20, window.innerHeight - ph - 16);
        popX = Math.max(16, popX);
        popY = Math.max(16, popY);
        popup.style.left = popX + 'px';
        popup.style.top = popY + 'px';
        popup.classList.add('open');
        setTimeout(() => { input.focus(); input.select(); }, 50);
    }
    static async confirmRename() {
        const popup = document.getElementById('scadaRenamePopup');
        if (!popup || !this.selectedEl || !this.widgetId)
            return;
        const input = popup.querySelector('.scada-rename-input');
        const newId = (input.value || '').trim().replace(/[^a-zA-Z0-9_-]/g, '');
        if (!newId) {
            input.style.borderColor = '#ef4444';
            setTimeout(() => { input.style.borderColor = ''; }, 800);
            return;
        }
        const widgetEl = document.querySelector(`.scada-svg-widget[data-wid="${this.widgetId}"]`);
        const svg = widgetEl?.querySelector('svg');
        if (svg) {
            const existing = svg.querySelector('#' + window.CSS.escape(newId)) || svg.querySelector(`[data-cell-id="${window.CSS.escape(newId)}"]`);
            if (existing && existing !== this.selectedEl) {
                input.style.borderColor = '#ef4444';
                input.value = newId + ' ← duplicate!';
                setTimeout(() => { input.value = newId; input.style.borderColor = ''; }, 1200);
                return;
            }
        }
        const oldId = this.selectedEl.getAttribute('id') || this.selectedEl.getAttribute('data-cell-id');
        if (!oldId)
            return;
        if (this.selectedEl.hasAttribute('id'))
            this.selectedEl.setAttribute('id', newId);
        if (this.selectedEl.hasAttribute('data-cell-id'))
            this.selectedEl.setAttribute('data-cell-id', newId);
        if (svg) {
            let contentXml = svg.getAttribute('content');
            if (contentXml) {
                try {
                    const newContentXml = await DrawioCodec.renameCellId(contentXml, oldId, newId);
                    svg.setAttribute('content', newContentXml);
                }
                catch (e) {
                    console.warn('Error updating draw.io XML content:', e);
                }
            }
        }
        this.selectedEl.classList.remove('scada-id-selected');
        this.selectedEl = null;
        popup.classList.remove('open');
        this.persistSvgContent(this.widgetId);
        this.renderPanel(this.widgetId);
    }
    static cancelRename() {
        const popup = document.getElementById('scadaRenamePopup');
        if (popup)
            popup.classList.remove('open');
        if (this.selectedEl) {
            this.selectedEl.classList.remove('scada-id-selected');
            this.selectedEl = null;
        }
    }
    static persistSvgContent(wid) {
        const dashboard = window.dashboard;
        const w = dashboard?.widgets?.find((x) => x.id === wid);
        if (!w || w.type !== 'background-svg')
            return;
        const widgetEl = document.querySelector(`.scada-svg-widget[data-wid="${wid}"]`);
        const svg = widgetEl?.querySelector('svg');
        if (!svg)
            return;
        svg.querySelectorAll('.scada-id-hover-highlight').forEach(el => el.classList.remove('scada-id-hover-highlight'));
        svg.querySelectorAll('.scada-id-selected').forEach(el => el.classList.remove('scada-id-selected'));
        svg.querySelectorAll('.scada-dot').forEach(el => el.remove());
        const serializer = new XMLSerializer();
        w.config.svg = serializer.serializeToString(svg);
    }
    static renderPanel(wid) {
        let panel = document.getElementById('scadaIdPanel');
        if (!panel)
            return;
        const widgetEl = document.querySelector(`.scada-svg-widget[data-wid="${wid}"]`);
        const svg = widgetEl?.querySelector('svg');
        if (!svg) {
            panel.classList.remove('open');
            return;
        }
        const elements = [];
        svg.querySelectorAll('*').forEach(el => {
            const tag = el.tagName?.toLowerCase();
            if (!ScadaPickerController.tags.has(tag))
                return;
            const id = el.getAttribute('id') || el.getAttribute('data-cell-id') || '';
            if (!id && el.closest('[id]:not(svg), [data-cell-id]:not(svg)'))
                return;
            if (/^(mxGraphModel|root)\b/i.test(id))
                return;
            if (/^\d+$/.test(id))
                return;
            elements.push({ el, tag, id });
        });
        const listEl = panel.querySelector('.scada-id-panel-list');
        if (!listEl)
            return;
        const esc = window.esc || ((s) => s);
        if (elements.length === 0) {
            listEl.innerHTML = '<div style="padding:1rem;text-align:center;color:#64748b;font-size:0.78rem">No SVG elements found</div>';
        }
        else {
            listEl.innerHTML = elements.map((item, idx) => {
                const nameHtml = item.id
                    ? `<span class="scada-id-item-name">${esc(item.id)}</span>`
                    : `<span class="scada-id-item-noname">unnamed</span>`;
                return `<div class="scada-id-item" data-picker-idx="${idx}" data-el-id="${esc(item.id)}"
                             onmouseenter="highlightPickerElement(${idx}, '${wid}')"
                             onmouseleave="unhighlightPickerElement(${idx}, '${wid}')"
                             onclick="clickPickerElement(${idx}, '${wid}')">
                    <span class="scada-id-item-tag">&lt;${esc(item.tag)}&gt;</span>
                    ${nameHtml}
                </div>`;
            }).join('');
        }
        panel._elements = elements;
        panel.querySelector('.scada-id-panel-header h4').textContent = `Elements (${elements.length})`;
        panel.classList.add('open');
    }
    static highlightPanelItem(id) {
        const panel = document.getElementById('scadaIdPanel');
        if (!panel)
            return;
        panel.querySelectorAll('.scada-id-item').forEach(item => {
            item.classList.toggle('active', !!id && item.dataset.elId === id);
        });
    }
    static highlightElement(idx, _wid) {
        const panel = document.getElementById('scadaIdPanel');
        if (!panel?._elements?.[idx])
            return;
        const el = panel._elements[idx].el;
        if (this.hoveredEl)
            this.hoveredEl.classList.remove('scada-id-hover-highlight');
        this.hoveredEl = el;
        el.classList.add('scada-id-hover-highlight');
    }
    static unhighlightElement(_idx, _wid) {
        if (this.hoveredEl) {
            this.hoveredEl.classList.remove('scada-id-hover-highlight');
            this.hoveredEl = null;
        }
    }
    static clickElement(idx, _wid) {
        const panel = document.getElementById('scadaIdPanel');
        if (!panel?._elements?.[idx])
            return;
        const el = panel._elements[idx].el;
        if (this.selectedEl)
            this.selectedEl.classList.remove('scada-id-selected');
        this.selectedEl = el;
        el.classList.add('scada-id-selected');
        const rect = el.getBoundingClientRect();
        this.showRenamePopup(el, rect.left + rect.width / 2, rect.top + rect.height / 2);
    }
}
