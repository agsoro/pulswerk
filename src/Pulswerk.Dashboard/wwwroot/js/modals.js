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
        colors: ['#00d1d1'],
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
    document.getElementById('boolLabel').className = 'bool-toggle-label ' + (input.checked ? 'text-cyan-400' : 'text-slate-500');
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
