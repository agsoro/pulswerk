// @ts-nocheck
// picker.js – Data key selection and ordering logic

async function loadKeyPicker(): Promise<void> {
    if (!allKeys.length) { try { allKeys = await api('AvailableTelemetries'); } catch (e) { allKeys = []; } }
    
    // Default to rendering selected keys in sorted view
    const selected = activeKeyOrder.map(key => allKeys.find(k => k.key === key)).filter(Boolean);
    renderKeyList(selected as ITelemetryMeta[], true);
}

let _initialSelection: string[] = [];
function openKeySelector(): void {
    isKeySelectorOpen = true;
    const wrapper = document.getElementById('keyPickerWrapper')!;
    const searchWrapper = document.getElementById('keySearchWrapper');
    
    wrapper.classList.add('key-picker-expanded');
    if (searchWrapper) searchWrapper.classList.remove('hidden');
    
    document.body.style.overflow = 'hidden';
    
    // Get current checked keys
    _initialSelection = [...document.querySelectorAll('#keyList input')].filter(i => ((i as HTMLInputElement).type === 'hidden' && (i as HTMLInputElement).name === 'wkey') || (i as HTMLInputElement).checked).map(i => (i as HTMLInputElement).value);
    renderKeyList(allKeys, false, _initialSelection);
    filterKeys();
}

function closeKeySelector(e?: Event): void {
    if (e) { e.preventDefault(); e.stopPropagation(); }
    dismissKeySelector();
    
    try {
        // Sync current selections into activeKeyOrder and re-render the small list
        const checkedKeys = [...document.querySelectorAll('#keyList input:checked')].map(c => (c as HTMLInputElement).value);
        activeKeyOrder = checkedKeys.map(key => allKeys.find(k => k.key === key)).filter(Boolean).map((k: any) => k.key);
        // Add any remaining keys at the end
        const others = allKeys.map(k => k.key).filter(k => !activeKeyOrder.includes(k));
        activeKeyOrder = [...activeKeyOrder, ...others];
        
        const selectedMeta = activeKeyOrder.filter(k => checkedKeys.includes(k)).map(k => allKeys.find(x => x.key === k)).filter(Boolean);
        renderKeyList(selectedMeta as ITelemetryMeta[], true);
    } catch (err) {
        console.error('Error syncing key selection:', err);
    }
}

function cancelKeySelector(e?: Event): void {
    if (e) { e.preventDefault(); e.stopPropagation(); }
    dismissKeySelector();
    
    // Restore initial selection in the small list
    const selectedMeta = activeKeyOrder.filter(k => _initialSelection.includes(k)).map(k => allKeys.find(x => x.key === k)).filter(Boolean);
    renderKeyList(selectedMeta as ITelemetryMeta[], true);
}

function dismissKeySelector(): void {
    isKeySelectorOpen = false;
    const wrapper = document.getElementById('keyPickerWrapper');
    const searchWrapper = document.getElementById('keySearchWrapper');
    if (wrapper) wrapper.classList.remove('key-picker-expanded');
    if (searchWrapper) searchWrapper.classList.add('hidden');
    document.body.style.overflow = '';
}

function filterKeys(): void {
    const raw = (document.getElementById('keySearch') as HTMLInputElement).value.toLowerCase();
    const terms = raw.split(/\s+/).filter(t => t.length > 0);
    
    if (isKeySelectorOpen) {
        if (terms.length > 0) {
            document.querySelectorAll('#keyList .key-item').forEach(el => {
                const search = (el as HTMLElement).dataset.search || '';
                const matchesSearch = terms.every(t => search.includes(t));
                (el as HTMLElement).style.display = matchesSearch ? '' : 'none';
            });
        } else {
            document.querySelectorAll('#keyList .key-item').forEach(el => (el as HTMLElement).style.display = '');
        }
    }
}

function renderKeyList(keys: ITelemetryMeta[], isSortedView: boolean = false, checkedKeys: string[] = []): void {
    const list = document.getElementById('keyList');
    if (!list) return;
    
    if (keys.length === 0) {
        if (isSortedView) {
            list.innerHTML = `<div class="p-8 text-center text-slate-500 border-2 border-dashed border-white/5 rounded-lg m-3 bg-white/[0.01]">
                <i class="fas fa-mouse-pointer mb-3 opacity-20 text-3xl block"></i>
                <p class="text-[0.78rem] font-semibold text-slate-400 mb-1">No telemetries selected</p>
                <p class="text-[0.68rem] opacity-60">Use the <strong class="text-sky-400">SELECT KEYS</strong> button above to find and add telemetries to this widget.</p>
            </div>`;
        } else {
            list.innerHTML = `<div class="p-8 text-center text-slate-500">
                <i class="fas fa-search mb-3 opacity-20 text-3xl block"></i>
                <p class="text-[0.78rem] font-semibold text-slate-400">No keys found</p>
            </div>`;
        }
        return;
    }

    const multi = selectedType !== 'single-value';
    list.innerHTML = keys.map((k: any, idx: number) => {
        const isChecked = isSortedView || checkedKeys.includes(k.key);
        return `<div class="key-item ${isChecked ? 'selected' : ''}" data-search="${(k.name + ' ' + k.path + ' ' + k.key).toLowerCase()}" data-key="${k.key}">
            ${isSortedView ? `
                <div class="flex items-center gap-1 mr-3">
                    <div class="flex flex-col gap-0.5">
                        <button class="text-slate-500 hover:text-sky-400 p-0.5" onclick="moveKey('${k.key}', -1)"><i class="fas fa-chevron-up text-[0.6rem]"></i></button>
                        <button class="text-slate-500 hover:text-sky-400 p-0.5" onclick="moveKey('${k.key}', 1)"><i class="fas fa-chevron-down text-[0.6rem]"></i></button>
                    </div>
                    <button class="text-slate-500 hover:text-red-400 p-1.5 ml-0.5" onclick="removeKeyFromSelection('${k.key}')" title="Remove"><i class="fas fa-trash-alt text-[0.75rem]"></i></button>
                </div>
                <input type="hidden" name="wkey" value="${k.key}" class="sort-key">
            ` : `
                <input type="${multi ? 'checkbox' : 'radio'}" name="wkey" value="${k.key}" ${isChecked ? 'checked' : ''} onchange="handleKeyToggle(this)">
            `}
            <div style="min-width:0;flex:1">
                <div class="key-name">${esc(t(k.name))}</div>
                <div class="flex items-center gap-2">
                    <span class="text-[0.6rem] font-mono text-slate-500 bg-white/5 px-1.5 py-0.5 rounded border border-white/10 uppercase tracking-tighter">${esc(k.key)}</span>
                    <div class="key-path">${esc(k.path)}</div>
                </div>
            </div>
            <span class="key-val">${esc(k.value)} ${esc(k.units)}</span>
        </div>`
    }).join('');

    // Re-render animation rules if the editor is open so dropdowns pick up new available keys
    if (typeof (window as any).renderAnimRules === 'function') {
        (window as any).renderAnimRules();
    }
}

function removeKeyFromSelection(key: string): void {
    const items = [...document.querySelectorAll('#keyList .key-item')];
    const el = items.find((el: HTMLElement) => el.dataset.key === key);
    if (el) {
        (el as HTMLElement).style.opacity = '0';
        (el as HTMLElement).style.transform = 'translateX(-20px)';
        setTimeout(() => {
            el.remove();
            // Sync state if needed, although the form submit reads the DOM
        }, 200);
    }
}

function moveKey(key: string, dir: number): void {
    const items = [...document.querySelectorAll('#keyList .key-item')];
    const idx = items.findIndex((el: HTMLElement) => el.dataset.key === key);
    if (idx === -1) return;
    const newIdx = idx + dir;
    if (newIdx < 0 || newIdx >= items.length) return;
    
    const list = document.getElementById('keyList')!;
    if (dir === -1) list.insertBefore(items[idx], items[newIdx]);
    else list.insertBefore(items[idx], items[newIdx].nextSibling);
}

function handleKeyToggle(input: HTMLInputElement): void {
    if (input.type === 'radio') {
        document.querySelectorAll('#keyList .key-item').forEach(el => el.classList.remove('selected'));
        input.closest('.key-item')!.classList.add('selected');
    } else {
        input.closest('.key-item')!.classList.toggle('selected', input.checked);
    }
}

(window as any).loadKeyPicker = loadKeyPicker;
(window as any).openKeySelector = openKeySelector;
(window as any).closeKeySelector = closeKeySelector;
(window as any).cancelKeySelector = cancelKeySelector;
(window as any).dismissKeySelector = dismissKeySelector;
(window as any).filterKeys = filterKeys;
(window as any).renderKeyList = renderKeyList;
(window as any).removeKeyFromSelection = removeKeyFromSelection;
(window as any).moveKey = moveKey;
(window as any).handleKeyToggle = handleKeyToggle;
