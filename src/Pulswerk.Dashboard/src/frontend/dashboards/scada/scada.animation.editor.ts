import { IAnimationRule } from '../../types/scada.types';
import { ScadaAnimationController } from './scada.animation.controller';
import { DashboardStore } from '../store';

let currentRules: IAnimationRule[] = [];
let currentPreviewElement: SVGElement | null = null;
let currentPreviewRuleIdx: number | null = null;

export function initAnimRuleEditor(rules: IAnimationRule[]): void {
    currentRules = JSON.parse(JSON.stringify(rules || [])).map((r: any) => {
        if (r.dataKey && !r.dataKeys) r.dataKeys = [r.dataKey];
        return r;
    });
    renderAnimRules();
}

export function getAnimRules(): IAnimationRule[] {
    return currentRules;
}

export function addAnimRule(): void {
    currentRules.push({
        dataKeys: [],
        elementId: '',
        formula: '',
        styles: []
    });
    renderAnimRules();
}

export function removeAnimRule(idx: number): void {
    currentRules.splice(idx, 1);
    renderAnimRules();
}

export function updateAnimRule(idx: number, field: string, value: any): void {
    if (idx >= 0 && idx < currentRules.length) {
        (currentRules[idx] as any)[field] = value;
        if (field === 'styles' && typeof value === 'string') {
            const arr = value.split(',').map(s => s.trim()).filter(Boolean);
            currentRules[idx].styles = arr;
        }
    }
}

export function updateAnimRuleElementIds(idx: number, selectEl: HTMLSelectElement): void {
    const selected = Array.from(selectEl.selectedOptions).map(opt => opt.value);
    updateAnimRule(idx, 'elementId', selected.join(','));
}

export function addAnimRuleElementId(idx: number, id: string): void {
    if (!id || !id.trim()) return;
    const current = currentRules[idx].elementId ? currentRules[idx].elementId.split(',').map(s => s.trim()).filter(Boolean) : [];
    if (!current.includes(id)) {
        current.push(id);
        updateAnimRule(idx, 'elementId', current.join(','));
        renderAnimRules();
    }
}

export function addAnimRuleDataKey(idx: number, key: string): void {
    if (!key || !key.trim()) return;
    const current = currentRules[idx].dataKeys || [];
    if (!current.includes(key)) {
        current.push(key);
        updateAnimRule(idx, 'dataKeys', current);
        renderAnimRules();
    }
}

export function removeAnimRuleDataKey(idx: number, key: string): void {
    const current = currentRules[idx].dataKeys || [];
    const filtered = current.filter(x => x !== key);
    updateAnimRule(idx, 'dataKeys', filtered);
    renderAnimRules();
}

export function removeAnimRuleElementId(idx: number, id: string): void {
    const current = currentRules[idx].elementId ? currentRules[idx].elementId.split(',').map(s => s.trim()).filter(Boolean) : [];
    const filtered = current.filter(x => x !== id);
    updateAnimRule(idx, 'elementId', filtered.join(','));
    renderAnimRules();
}

function renderAnimRules(): void {
    const container = document.getElementById('animRuleList');
    if (!container) return;
    
    // Get available keys directly from the DOM picker
    let availableKeys: string[] = [...document.querySelectorAll('#keyList input[name="wkey"]')]
        .filter(i => (i as HTMLInputElement).type === 'hidden' || (i as HTMLInputElement).checked)
        .map(c => (c as HTMLInputElement).value);
        
    // Get all SVG elements IDs for the dropdown
    let svgContent = DashboardStore.pendingSvgContent;
    if (DashboardStore.editingWidgetId && !svgContent) {
        const w = DashboardStore.dashboard?.widgets?.find((x: any) => x.id === DashboardStore.editingWidgetId);
        if (w && w.config?.svg) {
            svgContent = w.config.svg;
        }
    }
    const allIds = new Set<string>();
    if (svgContent) {
        const parser = new DOMParser();
        const doc = parser.parseFromString(svgContent, 'image/svg+xml');
        doc.querySelectorAll('[id], [data-cell-id]').forEach(el => {
            const id = el.getAttribute('id') || el.getAttribute('data-cell-id');
            if (id && id.trim() !== '' && !/^\d+$/.test(id.trim())) allIds.add(id);
        });
    }

    if (currentRules.length === 0) {
        container.innerHTML = '<div style="color:#64748b;font-size:0.78rem;text-align:center;padding:1rem;">No animation rules defined.</div>';
        return;
    }

    container.innerHTML = currentRules.map((rule, idx) => {
        return `
            <div class="anim-rule-item" style="border:1px solid #334155; border-radius:0.5rem; padding:0.75rem; margin-bottom:0.75rem; background:rgba(0,0,0,0.2);">
                <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:0.5rem;">
                    <span style="font-size:0.75rem; font-weight:bold; color:#94a3b8;">Rule #${idx + 1}</span>
                    <button class="btn-ghost btn-sm" style="color:#ef4444;" onclick="event.preventDefault();removeAnimRule(${idx})"><i class="fas fa-trash"></i></button>
                </div>
                
                <div style="display:grid; grid-template-columns: 1fr 1fr; gap:0.5rem; margin-bottom:0.5rem;">
                    <div>
                        <label style="font-size:0.65rem; color:#64748b; display:block; margin-bottom:0.2rem;">Data Keys</label>
                        <div style="display:flex; flex-wrap:wrap; gap:0.25rem; min-height: 28px; padding:0.25rem; background:rgba(0,0,0,0.2); border:1px solid #334155; border-radius:0.3rem; align-items:center;">
                            ${(rule.dataKeys || []).map((key, kIdx) => `
                                <span style="background:#0f172a; color:#e2e8f0; font-size:0.65rem; padding:0.1rem 0.4rem; border-radius:0.2rem; display:inline-flex; align-items:center; gap:0.3rem;" title="Use v${kIdx} in formula">
                                    <span style="opacity:0.5;margin-right:2px;">v${kIdx}:</span>${(window as any).esc ? (window as any).esc(key) : key}
                                    <i class="fas fa-times" style="cursor:pointer; color:#ef4444;" onclick="event.preventDefault();removeAnimRuleDataKey(${idx}, '${(window as any).esc ? (window as any).esc(key) : key}')"></i>
                                </span>
                            `).join('')}
                            
                            <div style="position:relative; display:inline-block;">
                                <button class="btn-ghost btn-sm h-5 !py-0 !px-1.5" style="font-size:0.6rem; opacity:0.7; border-radius:0.2rem;" onclick="event.preventDefault(); const p = this.nextElementSibling; p.style.display = p.style.display === 'none' ? 'block' : 'none'; if(p.style.display==='block') { const inp = p.querySelector('input'); if(inp) inp.focus(); }"><i class="fas fa-plus"></i></button>
                                <div style="display:none; position:absolute; top:100%; left:0; z-index:100; margin-top:0.2rem; background:#1e293b; border:1px solid #334155; border-radius:0.3rem; padding:0.3rem; width:200px; box-shadow:0 10px 15px -3px rgba(0,0,0,0.5);">
                                    <input type="text" list="dl_keys_${idx}" class="form-input" style="padding:0.25rem 0.5rem; font-size:0.75rem; width:100%;" placeholder="Select or type..." onkeydown="if(event.key==='Enter') { event.preventDefault(); addAnimRuleDataKey(${idx}, this.value); }">
                                    <datalist id="dl_keys_${idx}">
                                        ${availableKeys.filter(k => !(rule.dataKeys || []).includes(k)).map(k => `<option value="${(window as any).esc ? (window as any).esc(k) : k}">`).join('')}
                                    </datalist>
                                </div>
                            </div>
                        </div>
                    </div>
                    <div>
                        <label style="font-size:0.65rem; color:#64748b; display:block; margin-bottom:0.2rem;">Element IDs</label>
                        <div style="display:flex; flex-wrap:wrap; gap:0.25rem; min-height: 28px; padding:0.25rem; background:rgba(0,0,0,0.2); border:1px solid #334155; border-radius:0.3rem; align-items:center;">
                            ${(rule.elementId || '').split(',').map(s => s.trim()).filter(Boolean).map(id => `
                                <span style="background:#0f172a; color:#e2e8f0; font-size:0.65rem; padding:0.1rem 0.4rem; border-radius:0.2rem; display:inline-flex; align-items:center; gap:0.3rem;">
                                    ${(window as any).esc ? (window as any).esc(id) : id}
                                    <i class="fas fa-times" style="cursor:pointer; color:#ef4444;" onclick="event.preventDefault();removeAnimRuleElementId(${idx}, '${(window as any).esc ? (window as any).esc(id) : id}')"></i>
                                </span>
                            `).join('')}
                            
                            <div style="position:relative; display:inline-block;">
                                <button class="btn-ghost btn-sm h-5 !py-0 !px-1.5" style="font-size:0.6rem; opacity:0.7; border-radius:0.2rem;" onclick="event.preventDefault(); const p = this.nextElementSibling; p.style.display = p.style.display === 'none' ? 'block' : 'none'; if(p.style.display==='block') { const inp = p.querySelector('input'); if(inp) inp.focus(); }"><i class="fas fa-plus"></i></button>
                                <div style="display:none; position:absolute; top:100%; left:0; z-index:100; margin-top:0.2rem; background:#1e293b; border:1px solid #334155; border-radius:0.3rem; padding:0.3rem; width:200px; box-shadow:0 10px 15px -3px rgba(0,0,0,0.5);">
                                    <input type="text" list="dl_elements_${idx}" class="form-input" style="padding:0.25rem 0.5rem; font-size:0.75rem; width:100%;" placeholder="Select or type..." onkeydown="if(event.key==='Enter') { event.preventDefault(); addAnimRuleElementId(${idx}, this.value); }">
                                    <datalist id="dl_elements_${idx}">
                                        ${Array.from(allIds)
                                            .filter(id => !(rule.elementId || '').split(',').map(s => s.trim()).includes(id))
                                            .map(id => `<option value="${(window as any).esc ? (window as any).esc(id) : id}">`)
                                            .join('')}
                                    </datalist>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
                
                <div style="margin-bottom:0.5rem;">
                    <label style="font-size:0.65rem; color:#64748b; display:block; margin-bottom:0.2rem;">Condition Formula (e.g. 'v0 > 10' or 'v0 + v1 < 100')</label>
                    <input type="text" class="form-input" style="padding:0.25rem 0.5rem; font-size:0.75rem;" value="${(window as any).esc ? (window as any).esc(rule.formula || '') : rule.formula}" onchange="updateAnimRule(${idx}, 'formula', this.value)" placeholder="JS expression using v0, v1...">
                </div>
                
                <div>
                    <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:0.2rem;">
                        <label style="font-size:0.65rem; color:#64748b; display:block;">CSS Classes (comma separated)</label>
                        <button class="btn-ghost btn-sm h-6 !py-0 text-[0.65rem] !px-2" onclick="event.preventDefault();openAnimPreview(${idx})"><i class="fas fa-magic mr-1"></i> Visual Editor</button>
                    </div>
                    <input type="text" class="form-input" style="padding:0.25rem 0.5rem; font-size:0.75rem;" value="${(window as any).esc ? (window as any).esc((rule.styles || []).join(', ')) : (rule.styles || []).join(', ')}" onchange="updateAnimRule(${idx}, 'styles', this.value)">
                </div>
            </div>
        `;
    }).join('');
}

export function openAnimPreview(ruleIdx?: any): void {
    currentPreviewRuleIdx = typeof ruleIdx === 'number' ? ruleIdx : null;
    const popup = document.getElementById('scadaAnimPreview');
    if (!popup) return;
    
    // Copy SVG content into preview
    const previewSvg = popup.querySelector('.scada-preview-svg');
    if (!previewSvg) return;
    
    let svgContent = DashboardStore.pendingSvgContent;
    if (DashboardStore.editingWidgetId && !svgContent) {
        const w = DashboardStore.dashboard?.widgets?.find((x: any) => x.id === DashboardStore.editingWidgetId);
        if (w && w.config?.svg) {
            svgContent = w.config.svg;
        }
    }
    
    if (svgContent) {
        previewSvg.innerHTML = svgContent;
        // Collect element IDs
        const svgEl = previewSvg.querySelector('svg');
        if (svgEl) {
            const select = document.getElementById('previewElementSelect') as HTMLSelectElement;
            if (select) {
                const elements = svgEl.querySelectorAll('[id], [data-cell-id]');
                const ids = new Set<string>();
                elements.forEach(el => {
                    const id = el.getAttribute('id') || el.getAttribute('data-cell-id');
                    if (id && id.trim() !== '' && !/^\d+$/.test(id.trim())) ids.add(id);
                });
                select.innerHTML = '<option value="">— Select Element —</option>' + 
                    Array.from(ids).map(id => `<option value="${(window as any).esc ? (window as any).esc(id) : id}">${(window as any).esc ? (window as any).esc(id) : id}</option>`).join('');
            }
        }
    } else {
        previewSvg.innerHTML = '<div style="color:#64748b;font-size:0.85rem">No SVG content available</div>';
    }
    
    popup.classList.add('open');
    renderAnimChips('');
    
    // Auto-select rule's element and show Apply button
    if (typeof currentPreviewRuleIdx === 'number' && currentRules[currentPreviewRuleIdx]) {
        const rule = currentRules[currentPreviewRuleIdx];
        const select = document.getElementById('previewElementSelect') as HTMLSelectElement;
        
        const firstId = (rule.elementId || '').split(',').map(s => s.trim()).filter(Boolean)[0];
        if (firstId && select.querySelector(`option[value="${firstId}"]`)) {
            select.value = firstId;
            onPreviewElementChange();
            
            if (currentPreviewElement && rule.styles) {
                rule.styles.forEach(c => currentPreviewElement!.classList.add(c));
                renderAnimChips(firstId);
            }
        }
        
        let actionsContainer = document.getElementById('previewApplyActions');
        if (!actionsContainer) {
            actionsContainer = document.createElement('div');
            actionsContainer.id = 'previewApplyActions';
            actionsContainer.style.gridColumn = '1/-1';
            actionsContainer.style.display = 'flex';
            actionsContainer.style.justifyContent = 'flex-end';
            actionsContainer.style.gap = '0.5rem';
            actionsContainer.style.marginTop = '1rem';
            popup.querySelector('.scada-preview-body')?.appendChild(actionsContainer);
        }
        actionsContainer.innerHTML = `
            <button class="btn-ghost btn-sm" onclick="closeAnimPreview()">Cancel</button>
            <button class="btn btn-sm" style="background:#38bdf8;color:#0f172a;border:none;" onclick="applyPreviewClasses()">Apply to Rule #${currentPreviewRuleIdx + 1}</button>
        `;
    } else {
        const actionsContainer = document.getElementById('previewApplyActions');
        if (actionsContainer) actionsContainer.innerHTML = '';
    }
}

export function applyPreviewClasses(): void {
    if (currentPreviewRuleIdx !== null && currentPreviewElement) {
        const classes = Array.from(currentPreviewElement.classList).filter(c => ScadaAnimationController.allClasses.includes(c));
        updateAnimRule(currentPreviewRuleIdx, 'styles', classes.join(', '));
        renderAnimRules();
    }
    closeAnimPreview();
}

export function closeAnimPreview(): void {
    const popup = document.getElementById('scadaAnimPreview');
    if (popup) popup.classList.remove('open');
    if (ScadaAnimationController.dotRafId) {
        cancelAnimationFrame(ScadaAnimationController.dotRafId);
        ScadaAnimationController.dotRafId = null;
    }
    ScadaAnimationController.dotAnimations.clear();
}

export function onPreviewElementChange(): void {
    const select = document.getElementById('previewElementSelect') as HTMLSelectElement;
    if (!select) return;
    
    const popup = document.getElementById('scadaAnimPreview');
    const svgEl = popup?.querySelector('svg');
    if (!svgEl) return;
    
    const id = select.value;
    if (id) {
        const target = svgEl.querySelector('#' + CSS.escape(id)) || svgEl.querySelector(`[data-cell-id="${CSS.escape(id)}"]`);
        if (target) {
            currentPreviewElement = target as SVGElement;
            renderAnimChips(id);
            return;
        }
    }
    currentPreviewElement = null;
    renderAnimChips('');
}

export function clearPreviewClasses(): void {
    if (currentPreviewElement) {
        ScadaAnimationController.allClasses.forEach(c => currentPreviewElement!.classList.remove(c));
        renderAnimChips((document.getElementById('previewElementSelect') as HTMLSelectElement)?.value || '');
        
        // Remove dots animation if it was active
        const id = currentPreviewElement.getAttribute('id') || currentPreviewElement.getAttribute('data-cell-id');
        if (id) {
            ScadaAnimationController.cleanupDots('preview:' + id);
        }
    }
}

const animCategories = [
    { name: 'Flow Direction', classes: ['scada-flow-right', 'scada-flow-left'] },
    { name: 'Flow Speed', classes: ['scada-flow-fast'] },
    { name: 'Base Color', classes: ['scada-green', 'scada-red', 'scada-amber', 'scada-blue', 'scada-gray'] },
    { name: 'Stroke Color', classes: ['scada-stroke-green', 'scada-stroke-red', 'scada-stroke-amber', 'scada-stroke-blue', 'scada-stroke-gray'] },
    { name: 'Fill Color', classes: ['scada-fill-green', 'scada-fill-red', 'scada-fill-amber', 'scada-fill-blue', 'scada-fill-gray'] },
    { name: 'Level', classes: ['scada-level-10', 'scada-level-20', 'scada-level-30', 'scada-level-40', 'scada-level-50', 'scada-level-60', 'scada-level-70', 'scada-level-80', 'scada-level-90', 'scada-level-100'] },
    { name: 'Opacity Effect', classes: ['scada-pulse', 'scada-blink'] },
    { name: 'Glow Effect', classes: ['scada-glow-green', 'scada-glow-red', 'scada-shimmer'] },
    { name: 'Rotation', classes: ['scada-rotate', 'scada-rotate-ccw'] },
    { name: 'Particle Direction', classes: ['scada-dots-right', 'scada-dots-left'] },
    { name: 'Particle Speed', classes: ['scada-dots-fast'] },
    { name: 'Particle Color', classes: ['scada-dots-green', 'scada-dots-red', 'scada-dots-amber'] },
    { name: 'Visibility', classes: ['scada-hide', 'scada-show'] }
];

export function togglePreviewClass(cls: string): void {
    if (!currentPreviewElement) return;
    
    // Find category for this class
    const category = animCategories.find(cat => cat.classes.includes(cls));
    
    if (currentPreviewElement.classList.contains(cls)) {
        // Toggle OFF
        currentPreviewElement.classList.remove(cls);
    } else {
        // Toggle ON
        if (category) {
            // Remove other classes in this category
            category.classes.forEach(c => {
                if (c !== cls) currentPreviewElement!.classList.remove(c);
            });
        }
        currentPreviewElement.classList.add(cls);
    }
    
    renderAnimChips((document.getElementById('previewElementSelect') as HTMLSelectElement)?.value || '');
    
    const popup = document.getElementById('scadaAnimPreview');
    const svgEl = popup?.querySelector('svg');
    
    if (svgEl) {
        // Trigger dot animation update on preview
        const id = currentPreviewElement.getAttribute('id') || currentPreviewElement.getAttribute('data-cell-id');
        if (id) {
            const hasDots = ScadaAnimationController.dotClasses.some(c => currentPreviewElement!.classList.contains(c));
            const key = 'preview:' + id;
            if (hasDots) {
                let pathEl = currentPreviewElement.tagName === 'path' || currentPreviewElement.tagName === 'line' || currentPreviewElement.tagName === 'polyline' ? currentPreviewElement : currentPreviewElement.querySelector('path, line, polyline');
                if (pathEl) {
                    const reverse = currentPreviewElement.classList.contains('scada-dots-left');
                    const fast = currentPreviewElement.classList.contains('scada-dots-fast');
                    const colorClass = currentPreviewElement.classList.contains('scada-dots-green') ? 'green' : currentPreviewElement.classList.contains('scada-dots-red') ? 'red' : currentPreviewElement.classList.contains('scada-dots-amber') ? 'amber' : '';
                    
                    const existing = ScadaAnimationController.dotAnimations.get(key);
                    if (!existing || existing.reverse !== reverse || existing.fast !== fast || existing.color !== colorClass) {
                        ScadaAnimationController.cleanupDots(key);
                        ScadaAnimationController.spawnDots(key, svgEl, pathEl as SVGGeometryElement, reverse, fast, colorClass);
                        if (!ScadaAnimationController.dotRafId) {
                            ScadaAnimationController.dotRafId = requestAnimationFrame(ScadaAnimationController.animateDots);
                        }
                    }
                }
            } else {
                ScadaAnimationController.cleanupDots(key);
            }
        }
    }
}

function renderAnimChips(elementId: string): void {
    const container = document.getElementById('previewAnimChips');
    if (!container) return;
    
    if (!elementId || !currentPreviewElement) {
        container.innerHTML = '<span style="color:#64748b;font-size:0.78rem">Select an element above to see available classes</span>';
        return;
    }
    
    let html = '';
    animCategories.forEach(cat => {
        const chipsHtml = cat.classes.map(cls => {
            const active = currentPreviewElement!.classList.contains(cls);
            return `<span class="scada-anim-chip ${active ? 'active' : ''}" onclick="togglePreviewClass('${cls}')">${cls}</span>`;
        }).join('');
        
        html += `
            <div style="margin-bottom:0.75rem;">
                <div style="font-size:0.65rem; color:#94a3b8; font-weight:600; margin-bottom:0.3rem;">${cat.name}</div>
                <div style="display:flex; flex-wrap:wrap; gap:0.4rem;">
                    ${chipsHtml}
                </div>
            </div>
        `;
    });
    
    container.innerHTML = html;
}

Object.assign(window, {
    initAnimRuleEditor,
    getAnimRules,
    addAnimRule,
    removeAnimRule,
    updateAnimRule,
    openAnimPreview,
    closeAnimPreview,
    clearPreviewClasses,
    onPreviewElementChange,
    togglePreviewClass,
    updateAnimRuleElementIds,
    addAnimRuleElementId,
    removeAnimRuleElementId,
    addAnimRuleDataKey,
    removeAnimRuleDataKey,
    applyPreviewClasses
});
