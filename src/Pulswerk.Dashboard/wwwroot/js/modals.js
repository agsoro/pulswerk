/**
 * Modals logic for History, Edit, and Properties
 */

let currentHistoryKey = null;
let currentEditKey = null;
let currentPropsKey = null;
let historyChart = null;

// --- History Modal ---
async function openHistory(key, name, units, pathEnc) {
    currentHistoryKey = key;
    const path = JSON.parse(decodeURIComponent(pathEnc || '[]'));
    
    document.getElementById('chartTitle').textContent = name;
    document.getElementById('chartUnit').textContent = units;
    document.getElementById('chartMeta').textContent = key;
    renderModalBreadcrumb('chartPath', path);
    
    document.getElementById('historyModal').style.display = 'flex';
    
    // Try to pre-fill live value from any element on the page matching this key
    const valEl = document.querySelector(`[data-key="${key}"]`);
    document.getElementById('chartLiveValue').textContent = valEl ? valEl.textContent : '---';
    
    await reloadHistory();
}

function closeHistory() {
    document.getElementById('historyModal').style.display = 'none';
    if (historyChart) {
        historyChart.destroy();
        historyChart = null;
    }
}

async function reloadHistory() {
    const days = document.getElementById('daysSelector').value;
    const loader = document.getElementById('chartLoading');
    loader.classList.remove('hidden');
    loader.classList.add('flex');
    
    try {
        const response = await fetch(`?handler=History&key=${currentHistoryKey}&days=${days}`);
        const data = await response.json();
        
        renderChart(data);
    } catch (e) {
        console.error("History load failed", e);
    } finally {
        loader.classList.remove('flex');
        loader.classList.add('hidden');
    }
}

function renderChart(data) {
    const options = {
        series: [{
            name: document.getElementById('chartTitle').textContent,
            data: data.map(d => ({ x: new Date(d.ts).getTime(), y: d.value }))
        }],
        chart: {
            type: 'area',
            height: '100%',
            foreColor: '#94a3b8',
            toolbar: { show: false },
            zoom: { enabled: false },
            animations: { enabled: false }
        },
        colors: ['#38bdf8'],
        fill: {
            type: 'gradient',
            gradient: {
                shadeIntensity: 1,
                opacityFrom: 0.45,
                opacityTo: 0.05,
                stops: [20, 100]
            }
        },
        stroke: { curve: 'smooth', width: 2 },
        dataLabels: { enabled: false },
        xaxis: {
            type: 'datetime',
            axisBorder: { show: false },
            axisTicks: { show: false }
        },
        yaxis: {
            labels: {
                formatter: (val) => formatNumber(val, 2)
            }
        },
        tooltip: {
            theme: 'dark',
            x: { format: 'dd MMM HH:mm:ss' }
        },
        grid: {
            borderColor: 'rgba(255,255,255,0.05)',
            strokeDashArray: 4
        }
    };

    const container = document.getElementById('historyChart');
    container.innerHTML = '';
    historyChart = new ApexCharts(container, options);
    historyChart.render();
}

// --- Edit Modal ---
function openEdit(key, name, fullName, units, pathEnc, type, enumValuesEnc) {
    currentEditKey = key;
    const path = JSON.parse(decodeURIComponent(pathEnc || '[]'));
    const enums = JSON.parse(decodeURIComponent(enumValuesEnc || 'null'));
    
    document.getElementById('editTitle').textContent = name;
    document.getElementById('editMeta').textContent = fullName;
    document.getElementById('editUnitLabel').textContent = units;
    renderModalBreadcrumb('editPath', path);
    
    // Hide all groups
    document.getElementById('stepperGroup').style.display = 'none';
    document.getElementById('enumGroup').style.display = 'none';
    document.getElementById('boolGroup').style.display = 'none';
    const status = document.getElementById('editStatus');
    status.textContent = '';
    status.className = 'status-msg';
    
    const valEl = document.querySelector(`.point-value[data-key="${key}"]`);
    const currentVal = valEl ? valEl.textContent : '---';
    document.getElementById('currentVal').textContent = currentVal;

    if (enums && Object.keys(enums).length > 0) {
        const sel = document.getElementById('enumSelect');
        sel.innerHTML = '';
        for (const [v, label] of Object.entries(enums)) {
            const opt = document.createElement('option');
            opt.value = v;
            opt.textContent = label;
            sel.appendChild(opt);
        }
        sel.value = currentVal;
        document.getElementById('enumGroup').style.display = 'block';
    } else if (type && (type.toLowerCase().includes('binary') || type.toLowerCase().includes('bool'))) {
        const input = document.getElementById('boolInput');
        input.checked = currentVal.toLowerCase() === 'on' || currentVal === '1' || currentVal.toLowerCase() === 'true';
        updateBoolLabel();
        document.getElementById('boolGroup').style.display = 'block';
    } else {
        document.getElementById('editValue').value = parseFloat(currentVal) || 0;
        document.getElementById('stepperGroup').style.display = 'block';
    }
    
    document.getElementById('editModal').style.display = 'flex';
}

function closeEdit() {
    document.getElementById('editModal').style.display = 'none';
    const btn = document.getElementById('saveBtn');
    btn.disabled = false;
    btn.innerHTML = '<i class="fas fa-save"></i> <span data-i18n="btn_save_changes">Save Changes</span>';
}

function step(n) {
    const input = document.getElementById('editValue');
    input.value = (parseFloat(input.value) || 0) + n;
}

function updateBoolLabel() {
    const input = document.getElementById('boolInput');
    document.getElementById('boolLabel').textContent = input.checked ? 'ON' : 'OFF';
    document.getElementById('boolLabel').className = 'bool-toggle-label ' + (input.checked ? 'text-sky-400' : 'text-slate-500');
}

async function submitEdit(e) {
    if (e) e.preventDefault();
    const btn = document.getElementById('saveBtn');
    const status = document.getElementById('editStatus');
    btn.disabled = true;
    btn.innerHTML = '<i class="fas fa-spinner fa-spin"></i> <span>Sending...</span>';
    status.innerHTML = '<i class="fas fa-sync-alt fa-spin"></i> Sending update to device...';
    status.className = 'status-msg info';
    
    let value = 0;
    if (document.getElementById('stepperGroup').style.display !== 'none') {
        value = document.getElementById('editValue').value;
    } else if (document.getElementById('enumGroup').style.display !== 'none') {
        value = document.getElementById('enumSelect').value;
    } else {
        value = document.getElementById('boolInput').checked ? 1 : 0;
    }
    
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    
    try {
        const response = await fetch('?handler=Write', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token
            },
            body: JSON.stringify({ key: currentEditKey, value: parseFloat(value) || 0 })
        });
        
        if (response.ok) {
            const result = await response.json();
            if (result.success) {
                status.innerHTML = '<i class="fas fa-check-circle"></i> Success! Value updated.';
                status.className = 'status-msg success';
                btn.innerHTML = '<i class="fas fa-check"></i> <span>Updated</span>';
                
                // If this is the current history key, reload the chart with a delay
                // to ensure InfluxDB has indexed the new point.
                if (currentHistoryKey === currentEditKey && historyChart) {
                    setTimeout(reloadHistory, 1000);
                }
                
                setTimeout(closeEdit, 1200);
            } else {
                status.innerHTML = '<i class="fas fa-exclamation-triangle"></i> Write failed on device';
                status.className = 'status-msg error';
                btn.disabled = false;
                btn.innerHTML = '<i class="fas fa-save"></i> <span data-i18n="btn_save_changes">Save Changes</span>';
            }
        } else {
            const err = await response.text();
            status.innerHTML = '<i class="fas fa-bug"></i> Error: ' + err;
            status.className = 'status-msg error';
            btn.disabled = false;
            btn.innerHTML = '<i class="fas fa-save"></i> <span data-i18n="btn_save_changes">Save Changes</span>';
        }
    } catch (e) {
        status.innerHTML = '<i class="fas fa-wifi"></i> Failed to connect';
        status.className = 'status-msg error';
        btn.disabled = false;
        btn.innerHTML = '<i class="fas fa-save"></i> <span data-i18n="btn_save_changes">Save Changes</span>';
    }
}

// --- Properties Modal ---
async function openProperties(key, name, pathEnc) {
    currentPropsKey = key;
    const path = JSON.parse(decodeURIComponent(pathEnc || '[]'));
    
    document.getElementById('propsTitle').textContent = name;
    document.getElementById('propsMeta').textContent = key;
    renderModalBreadcrumb('propsPath', path);
    
    const loader = document.getElementById('propsLoading');
    const table = document.getElementById('propsTable');
    const empty = document.getElementById('propsEmpty');
    const body = document.getElementById('propsBody');
    
    loader.classList.remove('hidden');
    table.classList.add('hidden');
    empty.classList.add('hidden');
    body.innerHTML = '';
    
    document.getElementById('propsModal').style.display = 'flex';
    
    try {
        const response = await fetch(`?handler=Properties&key=${key}`);
        const props = await response.json();
        
        if (Array.isArray(props) && props.length > 0) {
            props.forEach(p => {
                const tr = document.createElement('tr');
                tr.innerHTML = `<td>${p.name || ''}</td><td>${p.value || ''}</td>`;
                body.appendChild(tr);
            });
            table.classList.remove('hidden');
        } else {
            empty.classList.remove('hidden');
        }
    } catch (e) {
        empty.classList.remove('hidden');
        console.error("Props load failed", e);
    } finally {
        loader.classList.add('hidden');
    }
}

function closeProperties() {
    document.getElementById('propsModal').style.display = 'none';
}

// --- Utils ---
function renderModalBreadcrumb(id, path) {
    const container = document.getElementById(id);
    if (!container) return;
    container.innerHTML = '';
    
    path.forEach((p, index) => {
        const span = document.createElement('span');
        span.textContent = p.name;
        container.appendChild(span);
        
        if (index < path.length - 1) {
            const sep = document.createElement('span');
            sep.className = 'sep';
            sep.textContent = ' / ';
            container.appendChild(sep);
        }
    });
}

// --- Schedule Modal ---
let currentScheduleData = [];
let isEditingSchedule = false;
let currentScheduleKey = null;
let scheduleValueType = 'real';    // 'boolean' | 'enumerated' | 'real'
let scheduleStates = null;         // ['Off','On'] or ['State1','State2',...]

async function openScheduleView(key, name, pathEnc) {
    const modal = document.getElementById('scheduleModal');
    const grid = document.getElementById('scheduleGrid');
    const loading = document.getElementById('scheduleLoading');
    const view = document.getElementById('scheduleView');
    
    currentScheduleKey = key;
    isEditingSchedule = false;
    scheduleValueType = 'real';
    scheduleStates = null;
    toggleScheduleEdit(false);

    let path = [];
    try { path = JSON.parse(decodeURIComponent(pathEnc)); } catch(e) {}
    document.getElementById('schedulePath').textContent = path.map(p => p.name).join(' › ');
    document.getElementById('scheduleMeta').textContent = key;
    
    modal.style.display = 'flex';
    loading.classList.remove('hidden');
    view.classList.add('hidden');
    grid.innerHTML = '';
    
    try {
        const response = await fetch(`?handler=Properties&key=${key}`);
        const props = await response.json();
        const schedProp = props.find(p => p.name === 'Weekly Schedule');
        
        // Read schedule metadata from backend
        const typeProp = props.find(p => p.name === '_scheduleValueType');
        if (typeProp) scheduleValueType = typeProp.value;
        
        const statesProp = props.find(p => p.name === '_scheduleStates');
        if (statesProp) {
            try { scheduleStates = JSON.parse(statesProp.value); } catch(e) {}
        }
        
        // For boolean without explicit states, use defaults
        if (scheduleValueType === 'boolean' && !scheduleStates) {
            scheduleStates = ['Off', 'On'];
        }
        
        loading.classList.add('hidden');
        view.classList.remove('hidden');
        
        currentScheduleData = [0,1,2,3,4,5,6].map(i => ({ dayIndex: i, entries: [] }));
        
        if (schedProp && schedProp.value && schedProp.value !== 'Empty Schedule' && schedProp.value !== 'None') {
            const dayNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
            const days = schedProp.value.split(' | ');
            days.forEach(dayStr => {
                const parts = dayStr.split(': ');
                if (parts.length < 2) return;
                const dayIdx = dayNames.indexOf(parts[0]);
                if (dayIdx === -1) return;
                
                const timesStr = parts[1];
                currentScheduleData[dayIdx].entries = timesStr.split(', ').map(t => {
                    const [time, val] = t.split('➔');
                    return { time, value: parseFloat(val) };
                });
            });
        }
        
        renderSchedule();
        
    } catch (error) {
        console.error("Schedule load error:", error);
        grid.innerHTML = '<div class="text-center p-8 text-red-400">Failed to load schedule from device.</div>';
    }
}

function closeSchedule() {
    document.getElementById('scheduleModal').style.display = 'none';
}

function formatScheduleValue(val) {
    if (scheduleStates && val >= 0 && val < scheduleStates.length) {
        return scheduleStates[val];
    }
    return val;
}

function renderScheduleValueInput(dayIndex, entryIndex, value) {
    if (scheduleValueType === 'boolean') {
        const checked = value ? 'checked' : '';
        const label = value ? 'ON' : 'OFF';
        const labelColor = value ? 'color:#38bdf8' : 'color:#64748b';
        return `
            <label style="display:inline-flex;align-items:center;gap:6px;cursor:pointer">
                <span class="bool-toggle" style="width:32px;height:16px;position:relative;display:inline-block">
                    <input type="checkbox" ${checked} style="width:0;height:0;opacity:0"
                           onchange="updateScheduleEntry(${dayIndex}, ${entryIndex}, 'value', this.checked ? 1 : 0)">
                    <span class="bool-toggle-slider" style="border-radius:16px"></span>
                </span>
                <span style="font-size:0.65rem;font-weight:700;${labelColor}">${label}</span>
            </label>`;
    }
    
    if (scheduleValueType === 'enumerated' && scheduleStates) {
        const options = scheduleStates.map((s, i) => 
            `<option value="${i}" ${i === value ? 'selected' : ''}>${s}</option>`
        ).join('');
        return `<select style="background:#1e293b;color:#38bdf8;font-weight:700;font-size:0.7rem;border:1px solid #475569;border-radius:4px;padding:2px 4px;outline:none"
                        onchange="updateScheduleEntry(${dayIndex}, ${entryIndex}, 'value', parseInt(this.value))">${options}</select>`;
    }
    
    // Default: number input for real values
    return `<input type="number" step="0.1" class="bg-transparent text-sky-400 font-bold text-[0.7rem] border-none focus:ring-0 w-10 p-0 text-center" 
                   value="${value}" onchange="updateScheduleEntry(${dayIndex}, ${entryIndex}, 'value', parseFloat(this.value))">`;
}

function renderSchedule() {
    const grid = document.getElementById('scheduleGrid');
    grid.innerHTML = '';
    
    currentScheduleData.forEach(day => {
        const dayNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
        const dayRow = document.createElement('div');
        dayRow.className = 'sched-day-row';
        
        let entriesHtml = day.entries.map((e, idx) => {
            if (isEditingSchedule) {
                return `
                    <div class="sched-entry-edit">
                        <input type="time" class="bg-transparent text-slate-300 text-[0.7rem] border-none focus:ring-0 w-[4.5rem] p-0" 
                               value="${e.time}" onchange="updateScheduleEntry(${day.dayIndex}, ${idx}, 'time', this.value)">
                        ${renderScheduleValueInput(day.dayIndex, idx, e.value)}
                        <button class="text-red-400 hover:text-red-300 px-1 text-sm leading-none" onclick="removeScheduleEntry(${day.dayIndex}, ${idx})">&times;</button>
                    </div>
                `;
            } else {
                return `
                    <div class="sched-entry-view">
                        <span class="text-slate-400"><i class="far fa-clock mr-1 opacity-70"></i>${e.time}</span>
                        <span class="font-bold text-sky-400 border-l border-white/10 pl-2">${formatScheduleValue(e.value)}</span>
                    </div>
                `;
            }
        }).join('');
        
        if (isEditingSchedule) {
            entriesHtml += `
                <button class="sched-add-btn" onclick="addScheduleEntry(${day.dayIndex})">
                    <i class="fas fa-plus mr-1"></i> Add
                </button>
            `;
        }

        dayRow.innerHTML = `
            <div class="w-14 font-bold text-slate-300 pt-1.5 border-r border-slate-700 pr-2 shrink-0">${dayNames[day.dayIndex]}</div>
            <div class="flex-1 flex flex-wrap gap-2 items-center">
                ${entriesHtml || (isEditingSchedule ? '' : '<span class="text-slate-600 text-xs italic">No switching points</span>')}
            </div>
        `;
        grid.appendChild(dayRow);
    });
}

function toggleScheduleEdit(edit) {
    isEditingSchedule = edit;
    const btnEdit = document.getElementById('btnEditSchedule');
    const btnActions = document.getElementById('editScheduleActions');
    const status = document.getElementById('scheduleStatus');
    const saveBtn = document.getElementById('scheduleSaveBtn');
    if (btnEdit) btnEdit.classList.toggle('hidden', edit);
    if (btnActions) btnActions.classList.toggle('hidden', !edit);
    if (status) { status.textContent = ''; status.className = 'status-msg'; }
    if (saveBtn) { saveBtn.disabled = false; saveBtn.innerHTML = '<i class="fas fa-save mr-1"></i> <span data-i18n="save">Save</span>'; }
    renderSchedule();
}

function updateScheduleEntry(dayIdx, entryIdx, field, value) {
    if (field === 'value') value = (scheduleValueType === 'real') ? parseFloat(value) : parseInt(value);
    currentScheduleData[dayIdx].entries[entryIdx][field] = value;
    if (field === 'value') renderSchedule(); // re-render to update toggle/select state
}

function addScheduleEntry(dayIdx) {
    const lastEntry = currentScheduleData[dayIdx].entries[currentScheduleData[dayIdx].entries.length - 1];
    const defaultValue = (scheduleValueType === 'boolean') ? 1 : (lastEntry ? lastEntry.value : 0);
    const newTime = lastEntry ? lastEntry.time : "08:00";
    currentScheduleData[dayIdx].entries.push({ time: newTime, value: defaultValue });
    renderSchedule();
}

function removeScheduleEntry(dayIdx, entryIdx) {
    currentScheduleData[dayIdx].entries.splice(entryIdx, 1);
    renderSchedule();
}

async function saveSchedule() {
    const btn = document.getElementById('scheduleSaveBtn');
    const status = document.getElementById('scheduleStatus');
    if (!btn) return;

    btn.disabled = true;
    btn.innerHTML = '<i class="fas fa-spinner fa-spin mr-1"></i> <span>Sending...</span>';
    status.innerHTML = '<i class="fas fa-sync-alt fa-spin"></i> Sending schedule to device...';
    status.className = 'status-msg info';

    try {
        currentScheduleData.forEach(day => {
            day.entries.sort((a, b) => a.time.localeCompare(b.time));
        });

        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
        const response = await fetch('?handler=WriteComplex', {
            method: 'POST',
            headers: { 
                'Content-Type': 'application/json',
                'RequestVerificationToken': token
            },
            body: JSON.stringify({
                key: currentScheduleKey,
                value: JSON.stringify(currentScheduleData)
            })
        });

        if (response.ok) {
            const result = await response.json();
            if (result.success) {
                status.innerHTML = '<i class="fas fa-check-circle"></i> Success! Schedule updated.';
                status.className = 'status-msg success';
                btn.innerHTML = '<i class="fas fa-check mr-1"></i> <span>Updated</span>';
                setTimeout(() => {
                    toggleScheduleEdit(false);
                    status.textContent = '';
                    status.className = 'status-msg';
                }, 1200);
            } else {
                status.innerHTML = '<i class="fas fa-exclamation-triangle"></i> Write failed on device';
                status.className = 'status-msg error';
                btn.disabled = false;
                btn.innerHTML = '<i class="fas fa-save mr-1"></i> <span data-i18n="save">Save</span>';
            }
        } else {
            const err = await response.text();
            status.innerHTML = '<i class="fas fa-bug"></i> Error: ' + err;
            status.className = 'status-msg error';
            btn.disabled = false;
            btn.innerHTML = '<i class="fas fa-save mr-1"></i> <span data-i18n="save">Save</span>';
        }
    } catch (error) {
        console.error("Save error:", error);
        status.innerHTML = '<i class="fas fa-wifi"></i> Failed to connect';
        status.className = 'status-msg error';
        btn.disabled = false;
        btn.innerHTML = '<i class="fas fa-save mr-1"></i> <span data-i18n="save">Save</span>';
    }
}
