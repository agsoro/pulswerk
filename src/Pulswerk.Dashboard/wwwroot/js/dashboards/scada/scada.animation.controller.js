import { ConditionEvaluator } from './condition.evaluator';
export class ScadaAnimationController {
    static allClasses = [
        'scada-flow-right', 'scada-flow-left', 'scada-flow-fast',
        'scada-green', 'scada-red', 'scada-amber', 'scada-blue', 'scada-gray',
        'scada-stroke-green', 'scada-stroke-red', 'scada-stroke-amber', 'scada-stroke-blue', 'scada-stroke-gray',
        'scada-fill-green', 'scada-fill-red', 'scada-fill-amber', 'scada-fill-blue', 'scada-fill-gray',
        'scada-level-10', 'scada-level-20', 'scada-level-30', 'scada-level-40', 'scada-level-50',
        'scada-level-60', 'scada-level-70', 'scada-level-80', 'scada-level-90', 'scada-level-100',
        'scada-pulse', 'scada-blink', 'scada-glow-green', 'scada-glow-red',
        'scada-rotate', 'scada-rotate-ccw', 'scada-shimmer',
        'scada-dots-right', 'scada-dots-left', 'scada-dots-fast',
        'scada-dots-green', 'scada-dots-red', 'scada-dots-amber',
        'scada-hide', 'scada-show'
    ];
    static dotClasses = [
        'scada-dots-right', 'scada-dots-left', 'scada-dots-fast',
        'scada-dots-green', 'scada-dots-red', 'scada-dots-amber'
    ];
    static dotAnimations = new Map();
    static dotRafId = null;
    static evaluateCondition(value, condition) {
        return ConditionEvaluator.evaluate(value, condition);
    }
    static applyAnimationRules(svgWidgetEl, rules, data) {
        if (!rules?.length || !svgWidgetEl)
            return;
        const svg = svgWidgetEl.querySelector('svg');
        if (!svg)
            return;
        // Find all target elements and clear previous classes
        const targets = new Set();
        for (const rule of rules) {
            if (!rule.elementId)
                continue;
            const elementIds = rule.elementId.split(',').map((s) => s.trim()).filter(Boolean);
            elementIds.forEach((id) => {
                const target = svg.querySelector('#' + window.CSS.escape(id)) ||
                    svg.querySelector(`[data-cell-id="${window.CSS.escape(id)}"]`);
                if (target)
                    targets.add(target);
            });
        }
        targets.forEach(target => {
            ScadaAnimationController.allClasses.forEach(c => target.classList.remove(c));
        });
        // Apply new classes based on conditions
        for (const rule of rules) {
            if (!rule.elementId)
                continue;
            const elementIds = rule.elementId.split(',').map((s) => s.trim()).filter(Boolean);
            const dataKeys = rule.dataKeys || (rule.dataKey ? [rule.dataKey] : []);
            const values = dataKeys.map((k) => data?.[k]);
            // If there are keys required but none are present in data, skip applying styles
            if (dataKeys.length > 0 && values.every((v) => v == null))
                continue;
            if (!rule.formula || ScadaAnimationController.evaluateCondition(values, rule.formula)) {
                const styles = rule.styles || [];
                elementIds.forEach((id) => {
                    const target = svg.querySelector('#' + window.CSS.escape(id)) ||
                        svg.querySelector(`[data-cell-id="${window.CSS.escape(id)}"]`);
                    if (target) {
                        styles.forEach((cls) => {
                            if (ScadaAnimationController.allClasses.includes(cls))
                                target.classList.add(cls);
                        });
                    }
                });
            }
        }
    }
    static async updateAll() {
        const dashboard = window.dashboard;
        const api = window.api;
        if (!dashboard?.widgets)
            return;
        const svgWidgets = dashboard.widgets.filter((w) => w.type === 'background-svg' && w.config?.animationRules?.length);
        if (!svgWidgets.length)
            return;
        const allAnimKeys = [...new Set(svgWidgets.flatMap((w) => w.config.animationRules.flatMap((r) => r.dataKeys || (r.dataKey ? [r.dataKey] : []))).filter(Boolean))];
        if (!allAnimKeys.length)
            return;
        let data;
        try {
            data = await api(`LatestValues&keys=${allAnimKeys.join(',')}`);
        }
        catch (e) {
            return;
        }
        svgWidgets.forEach((w) => {
            const el = document.querySelector(`.scada-svg-widget[data-wid="${w.id}"]`);
            ScadaAnimationController.applyAnimationRules(el, w.config.animationRules, data);
        });
        ScadaAnimationController.updateDotAnimations();
    }
    static updateDotAnimations() {
        document.querySelectorAll('.scada-svg-widget').forEach(widget => {
            const svg = widget.querySelector('svg');
            if (!svg)
                return;
            svg.querySelectorAll('[id], [data-cell-id]').forEach(el => {
                const hasDots = ScadaAnimationController.dotClasses.some(c => el.classList.contains(c));
                const elId = el.getAttribute('id') || el.getAttribute('data-cell-id');
                if (!elId)
                    return;
                const key = widget.dataset.wid + ':' + elId;
                if (hasDots) {
                    let pathEl = el.tagName === 'path' || el.tagName === 'line' || el.tagName === 'polyline' ? el : el.querySelector('path, line, polyline');
                    if (!pathEl)
                        return;
                    const reverse = el.classList.contains('scada-dots-left'), fast = el.classList.contains('scada-dots-fast');
                    const colorClass = el.classList.contains('scada-dots-green') ? 'green' : el.classList.contains('scada-dots-red') ? 'red' : el.classList.contains('scada-dots-amber') ? 'amber' : '';
                    const existing = ScadaAnimationController.dotAnimations.get(key);
                    if (existing && existing.reverse === reverse && existing.fast === fast && existing.color === colorClass)
                        return;
                    if (existing)
                        ScadaAnimationController.cleanupDots(key);
                    ScadaAnimationController.spawnDots(key, svg, pathEl, reverse, fast, colorClass);
                }
                else if (ScadaAnimationController.dotAnimations.has(key)) {
                    ScadaAnimationController.cleanupDots(key);
                }
            });
        });
        if (ScadaAnimationController.dotAnimations.size > 0 && !ScadaAnimationController.dotRafId) {
            ScadaAnimationController.dotRafId = requestAnimationFrame(ScadaAnimationController.animateDots);
        }
    }
    static spawnDots(key, _svg, pathEl, reverse, fast, colorClass) {
        let totalLen;
        try {
            if (pathEl.tagName === 'line') {
                const x1 = parseFloat(pathEl.getAttribute('x1') || '0') || 0, y1 = parseFloat(pathEl.getAttribute('y1') || '0') || 0, x2 = parseFloat(pathEl.getAttribute('x2') || '0') || 0, y2 = parseFloat(pathEl.getAttribute('y2') || '0') || 0;
                totalLen = Math.sqrt((x2 - x1) ** 2 + (y2 - y1) ** 2);
            }
            else
                totalLen = pathEl.getTotalLength();
        }
        catch (e) {
            return;
        }
        if (totalLen < 10)
            return;
        const DOT_COUNT = Math.max(2, Math.min(6, Math.round(totalLen / 80))), DOT_RADIUS = 3, speed = fast ? totalLen / 0.8 : totalLen / 2.5;
        const dots = [];
        for (let i = 0; i < DOT_COUNT; i++) {
            const circle = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
            circle.setAttribute('r', String(DOT_RADIUS));
            circle.classList.add('scada-dot');
            if (colorClass)
                circle.classList.add(colorClass);
            const insertTarget = pathEl.closest('g') || pathEl;
            insertTarget.parentNode.insertBefore(circle, insertTarget.nextSibling);
            dots.push({ el: circle, offset: (i / DOT_COUNT) * totalLen, totalLen, speed, born: performance.now() });
            requestAnimationFrame(() => circle.classList.add('active'));
        }
        ScadaAnimationController.dotAnimations.set(key, { dots, pathEl, reverse, fast, color: colorClass, lastTime: performance.now() });
    }
    static animateDots = (now) => {
        if (ScadaAnimationController.dotAnimations.size === 0) {
            ScadaAnimationController.dotRafId = null;
            return;
        }
        ScadaAnimationController.dotAnimations.forEach((anim, _key) => {
            const dt = (now - anim.lastTime) / 1000;
            anim.lastTime = now;
            anim.dots.forEach((dot) => {
                if (anim.reverse) {
                    dot.offset -= dot.speed * dt;
                    if (dot.offset < 0)
                        dot.offset += dot.totalLen;
                }
                else {
                    dot.offset += dot.speed * dt;
                    if (dot.offset > dot.totalLen)
                        dot.offset -= dot.totalLen;
                }
                try {
                    let pt;
                    if (anim.pathEl.tagName === 'line') {
                        const x1 = parseFloat(anim.pathEl.getAttribute('x1') || '0') || 0, y1 = parseFloat(anim.pathEl.getAttribute('y1') || '0') || 0, x2 = parseFloat(anim.pathEl.getAttribute('x2') || '0') || 0, y2 = parseFloat(anim.pathEl.getAttribute('y2') || '0') || 0;
                        const t = dot.offset / dot.totalLen;
                        pt = { x: x1 + (x2 - x1) * t, y: y1 + (y2 - y1) * t };
                    }
                    else
                        pt = anim.pathEl.getPointAtLength(dot.offset);
                    dot.el.setAttribute('cx', String(pt.x));
                    dot.el.setAttribute('cy', String(pt.y));
                    const edgeDist = Math.min(dot.offset, dot.totalLen - dot.offset), fadeZone = dot.totalLen * 0.08, edgeOpacity = fadeZone > 0 ? Math.min(1, edgeDist / fadeZone) : 1;
                    dot.el.style.opacity = String(edgeOpacity);
                }
                catch (e) { }
            });
        });
        ScadaAnimationController.dotRafId = requestAnimationFrame(ScadaAnimationController.animateDots);
    };
    static cleanupDots(key) {
        const anim = ScadaAnimationController.dotAnimations.get(key);
        if (!anim)
            return;
        anim.dots.forEach((dot) => {
            dot.el.classList.remove('active');
            setTimeout(() => dot.el.remove(), 350);
        });
        ScadaAnimationController.dotAnimations.delete(key);
        if (ScadaAnimationController.dotAnimations.size === 0 && ScadaAnimationController.dotRafId) {
            cancelAnimationFrame(ScadaAnimationController.dotRafId);
            ScadaAnimationController.dotRafId = null;
        }
    }
}
