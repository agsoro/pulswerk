// dashboards.js – Custom dashboard engine
const COLORS = ['#38bdf8','#f59e0b','#10b981','#a78bfa','#f472b6','#fb923c','#34d399','#60a5fa','#e879f9','#fbbf24'];
const PRESETS = [
    {label:'Last 5 min',ms:300000},{label:'Last 15 min',ms:900000},{label:'Last 1 hour',ms:3600000},
    {label:'Last 6 hours',ms:21600000},{label:'Last 12 hours',ms:43200000},{label:'Last 24 hours',ms:86400000},
    {label:'Last 7 days',ms:604800000},{label:'Last 30 days',ms:2592000000}
];
let grid=null, dashboard=null, isEditing=false, charts={}, pollTimer=null, allKeys=[], twMode='realtime', twRealtimeMs=3600000, twHistFrom=null, twHistTo=null;
const token=()=>document.querySelector('input[name="__RequestVerificationToken"]')?.value||'';
const api=async(handler,opts)=>{const r=await fetch(`/plswk/Dashboards?handler=${handler}`,opts);return r.json();};

function initDashboards(initial,editMode){
    if(initial){dashboard=initial;showDashboard();if(editMode)enterEditMode();}
    else loadList();
    buildTwPresets();
    // Enter key shortcuts
    document.getElementById('newDashName')?.addEventListener('keydown',e=>{if(e.key==='Enter')confirmCreate();});
    document.getElementById('widgetTitle')?.addEventListener('keydown',e=>{if(e.key==='Enter')confirmAddWidget();});
}

// ── LIST MODE ──────────────────────────────────────────────────
async function loadList(){
    const list=await api('List');
    const g=document.getElementById('dashGrid'), e=document.getElementById('emptyDashboards');
    if(!list||list.length===0){g.style.display='none';e.style.display='flex';return;}
    g.style.display='grid';e.style.display='none';
    g.innerHTML=list.map(d=>{
        const wc=d.widgets?.length||0;
        const ago=timeAgo(d.updatedAt);
        return `<div class="dash-card" onclick="location.href='/plswk/Dashboards/${d.id}/${slugify(d.name)}'">
            <div style="display:flex;align-items:center;gap:0.6rem;margin-bottom:0.75rem">
                <div style="width:36px;height:36px;border-radius:10px;background:rgba(56,189,248,0.1);display:flex;align-items:center;justify-content:center;flex-shrink:0">
                    <i class="fas fa-th-large" style="color:#38bdf8;font-size:1rem"></i>
                </div>
                <div style="flex:1;min-width:0"><div class="dash-card-title">${esc(d.name)}</div></div>
            </div>
            <div class="dash-card-desc">${esc(d.description||'No description')}</div>
            <div class="dash-card-meta">
                <span><i class="fas fa-puzzle-piece" style="margin-right:0.3rem"></i>${wc} widget${wc!==1?'s':''} · ${ago}</span>
                <div class="dash-card-actions" onclick="event.stopPropagation()">
                    <button class="btn-ghost btn-sm" onclick="location.href='/plswk/Dashboards/${d.id}/${slugify(d.name)}?edit=true'"><i class="fas fa-pen"></i></button>
                    <button class="btn-ghost btn-sm btn-danger" onclick="deleteDash('${d.id}')"><i class="fas fa-trash"></i></button>
                </div>
            </div>
        </div>`;}).join('');
}

function createDashboard(){document.getElementById('createModal').style.display='flex';document.getElementById('newDashName').focus();}
async function confirmCreate(){
    const name=document.getElementById('newDashName').value.trim();
    if(!name)return;
    const d=await fetch('/plswk/Dashboards?handler=Create',{method:'POST',headers:{'Content-Type':'application/json','RequestVerificationToken':token()},body:JSON.stringify({name,description:document.getElementById('newDashDesc').value})}).then(r=>r.json());
    location.href=`/plswk/Dashboards/${d.id}/${slugify(d.name)}?edit=true`;
}
async function deleteDash(id){
    if(!confirm('Delete this dashboard?'))return;
    await fetch('/plswk/Dashboards?handler=Delete',{method:'POST',headers:{'Content-Type':'application/json','RequestVerificationToken':token()},body:JSON.stringify({id})});
    loadList();
}

// ── DASHBOARD VIEW ─────────────────────────────────────────────
async function showDashboard(){
    document.getElementById('listMode').style.display='none';
    document.getElementById('dashMode').style.display='';
    document.getElementById('dashTitleView').textContent=dashboard.name;
    document.getElementById('dashTitle').value=dashboard.name;
    if(dashboard.timewindow){twMode=dashboard.timewindow.mode||'realtime';twRealtimeMs=dashboard.timewindow.realtimeMs||3600000;twHistFrom=dashboard.timewindow.historyFrom;twHistTo=dashboard.timewindow.historyTo;}
    updateTwLabel();
    // Pre-fetch key metadata so widgets can show friendly names
    if(!allKeys.length){try{allKeys=await api('AvailableKeys');}catch(e){allKeys=[];}}
    initGrid();
    if(dashboard.widgets?.length)renderAllWidgets();
    else document.getElementById('emptyDash').style.display='flex';
    startPolling();
}

function initGrid(){
    if(grid)grid.destroy(false);
    grid=GridStack.init({column:12,cellHeight:80,margin:8,disableResize:true,disableDrag:true,float:true},'#dashGrid2');
    grid.on('resizestop', function(event, el) {
        const id = el.getAttribute('gs-id');
        if (charts[id]) {
            setTimeout(() => charts[id].windowResize(), 50);
        }
    });
}

function enterEditMode(){
    isEditing=true;
    grid.enableMove(true);grid.enableResize(true);
    document.getElementById('dashTitle').style.display='';document.getElementById('dashTitleView').style.display='none';
    document.getElementById('btnEdit').style.display='none';
    document.getElementById('btnSave').style.display='';document.getElementById('btnCancel').style.display='';document.getElementById('btnAddWidget').style.display='';
    document.querySelectorAll('.widget-actions').forEach(e=>e.style.display='flex');
    document.getElementById('emptyDash').style.display=dashboard.widgets?.length?'none':'flex';
}

function cancelEdit(){location.href=`/plswk/Dashboards/${dashboard.id}/${slugify(dashboard.name)}`;}

async function saveDashboard(){
    dashboard.name=document.getElementById('dashTitle').value.trim()||dashboard.name;
    dashboard.timewindow={mode:twMode,realtimeMs:twRealtimeMs,historyFrom:twHistFrom,historyTo:twHistTo};
    // Sync widget positions from grid
    const items=grid.getGridItems();
    items.forEach(el=>{
        const wid=el.getAttribute('gs-id');
        const w=dashboard.widgets.find(x=>x.id===wid);
        if(w){w.x=parseInt(el.getAttribute('gs-x'))||0;w.y=parseInt(el.getAttribute('gs-y'))||0;w.w=parseInt(el.getAttribute('gs-w'))||6;w.h=parseInt(el.getAttribute('gs-h'))||4;}
    });
    await fetch('/plswk/Dashboards?handler=Save',{method:'POST',headers:{'Content-Type':'application/json','RequestVerificationToken':token()},body:JSON.stringify(dashboard)});
    location.href=`/plswk/Dashboards/${dashboard.id}/${slugify(dashboard.name)}`;
}

// ── WIDGET RENDERING ───────────────────────────────────────────
function renderAllWidgets(){
    grid.removeAll();charts={};
    dashboard.widgets.forEach(w=>addWidgetToGrid(w));
}

const WTYPE_ICONS={'timeseries':'fa-chart-line','latest-values':'fa-table','single-value':'fa-digital-tachograph'};
function addWidgetToGrid(w){
    document.getElementById('emptyDash').style.display='none';
    const el=document.createElement('div');
    el.className='grid-stack-item';
    const icon=WTYPE_ICONS[w.type]||'fa-puzzle-piece';
    el.innerHTML=`<div class="grid-stack-item-content">
        <div class="widget-header">
            <i class="fas ${icon} widget-type-icon"></i>
            <span class="widget-title">${esc(t(w.title))}</span>
            <div class="widget-actions" style="display:${isEditing?'flex':'none'}">
                <button title="Configure" onclick="editWidget('${w.id}')"><i class="fas fa-cog"></i></button>
                <button title="Remove" onclick="removeWidget('${w.id}')"><i class="fas fa-trash"></i></button>
            </div>
        </div>
        <div class="widget-body" id="wb_${w.id}"></div>
    </div>`;
    grid.addWidget(el,{id:w.id,x:w.x,y:w.y,w:w.w,h:w.h,minW:3,minH:2});
    renderWidgetContent(w);
}

function renderWidgetContent(w){
    const body=document.getElementById('wb_'+w.id);
    if(!body)return;
    const cfg=w.config||{};
    if(w.type==='timeseries')renderTimeseries(w,body,cfg);
    else if(w.type==='latest-values')renderLatestValues(w,body,cfg);
    else if(w.type==='single-value')renderSingleValue(w,body,cfg);
}

async function renderTimeseries(w,body,cfg){
    const keys=cfg.keys||[];
    if(!keys.length){body.innerHTML='<div class="empty-state" style="padding:1rem"><p style="font-size:0.8rem">No keys configured</p></div>';return;}
    
    const {startTs,endTs}=getTimeRange();
    let data;
    try{data=await api(`WidgetData&keys=${keys.join(',')}&startTs=${startTs}&endTs=${endTs}`);}catch(e){return;}

    const series=[], colors=[], statsHtml=[];
    keys.forEach((key,i)=>{
        const color=COLORS[i%COLORS.length];
        const raw=data?.[key]||[];
        const points=raw.map(p=>({x:new Date(p.ts).getTime(),y:parseFloat(parseFloat(p.value).toFixed(2))})).filter(p=>!isNaN(p.y));
        
        const meta=allKeys.find(k=>k.key===key);
        series.push({name:meta?.fullName||friendlyName(key),data:points});
        colors.push(color);

        if(points.length){
            const vals=points.map(p=>p.y);
            const min=Math.min(...vals),max=Math.max(...vals),avg=vals.reduce((a,b)=>a+b,0)/vals.length;
            const meta=allKeys.find(k=>k.key===key);
            const kn=meta?.fullName||friendlyName(key);
            statsHtml.push(`<span style="font-size:0.68rem;color:${color};display:flex;align-items:center;gap:0.3rem">
                <span style="width:8px;height:8px;border-radius:50%;background:${color};display:inline-block"></span>
                ${esc(kn)}: <span style="color:#94a3b8">min</span> ${formatNumber(min,1)} <span style="color:#94a3b8">avg</span> ${formatNumber(avg,1)} <span style="color:#94a3b8">max</span> ${formatNumber(max,1)}</span>`);
        }
    });

    const existingChart = charts[w.id];
    if(existingChart){
        existingChart.updateSeries(series);
        const statsEl=document.getElementById('stats_'+w.id);
        if(statsEl) statsEl.innerHTML=statsHtml.join('');
        return;
    }

    body.innerHTML='<div style="flex:1;min-height:0;position:relative"><div id="chart_'+w.id+'" style="height:100%"></div></div>'
        +'<div class="ts-stats" id="stats_'+w.id+'" style="display:flex;gap:0.75rem;padding:0.4rem 0.5rem 0;flex-wrap:wrap;flex-shrink:0;"></div>';
    
    const statsEl=document.getElementById('stats_'+w.id);
    if(statsEl) statsEl.innerHTML=statsHtml.join('');

    const options = {
        series: series,
        chart: {
            type: cfg.chartType==='bar'?'bar':'area',
            height: '100%',
            fontFamily: 'Inter, sans-serif',
            animations: { enabled: true, easing: 'easeinout', speed: 400 },
            toolbar: { show: false },
            sparkline: { enabled: false },
            stacked: !!cfg.stacked,
        },
        colors: colors,
        stroke: { curve: 'smooth', width: 2 },
        fill: {
            type: 'gradient',
            gradient: {
                shadeIntensity: 1, opacityFrom: 0.45, opacityTo: 0.05,
                stops: [0, 90, 100]
            }
        },
        dataLabels: { enabled: false },
        grid: {
            show: true, borderColor: 'rgba(255,255,255,0.05)',
            xaxis: { lines: { show: false } },
            yaxis: { lines: { show: true } },
            padding: { top: 0, right: 0, bottom: 0, left: 10 }
        },
        xaxis: {
            type: 'datetime',
            labels: { style: { colors: '#64748b', fontSize: '10px' } },
            axisBorder: { show: false },
            axisTicks: { show: false },
        },
        yaxis: {
            labels: { style: { colors: '#94a3b8', fontSize: '10px' } }
        },
        legend: {
            show: cfg.showLegend!==false, position: 'top', horizontalAlign: 'right',
            labels: { colors: '#94a3b8' }, markers: { width: 8, height: 8, radius: 12 }
        },
        tooltip: {
            theme: 'dark', x: { format: 'dd MMM HH:mm:ss' },
            y: { formatter: (v)=>formatNumber(v, 2) }
        }
    };

    const chart = new ApexCharts(document.getElementById('chart_'+w.id), options);
    chart.render().then(() => {
        // Force a resize event after rendering to ensure layout is correct
        setTimeout(() => window.dispatchEvent(new Event('resize')), 100);
    });
    charts[w.id] = chart;
}

async function renderLatestValues(w,body,cfg){
    const keys=cfg.keys||[];
    if(!keys.length){body.innerHTML='<div class="empty-state" style="padding:1rem"><p style="font-size:0.8rem">No keys configured</p></div>';return;}
    body.innerHTML='<div style="overflow:auto;height:100%"><table class="lv-table"><thead><tr><th>Name</th><th>Value</th><th style="width:50px">Trend</th></tr></thead><tbody id="lvt_'+w.id+'"></tbody></table></div>';
    await updateLatestValues(w,cfg);
}

async function updateLatestValues(w,cfg){
    const keys=cfg.keys||[];
    if(!keys.length)return;
    let data;
    try{data=await api(`LatestValues&keys=${keys.join(',')}`);}catch(e){return;}
    const tbody=document.getElementById('lvt_'+w.id);
    if(!tbody)return;
    const keyMeta=allKeys.reduce((m,k)=>{m[k.key]=k;return m;},{});
    tbody.innerHTML=keys.map((key,i)=>{
        const val=data?.[key]||'---';
        const meta=keyMeta[key]||{};
        const color=COLORS[i%COLORS.length];
        const numVal=parseFloat(val);
        const isNum=!isNaN(numVal);
        const displayVal=isNum?formatNumber(numVal,2):val;
        updateHistoryLiveValue(key, displayVal);
        return `<tr>
            <td><span style="display:inline-block;width:8px;height:8px;border-radius:50%;background:${color};margin-right:0.5rem"></span>${esc(friendlyName(key))}</td>
            <td><span class="lv-value" data-key="${key}">${esc(displayVal)}</span><span class="lv-units">${esc(meta.units||'')}</span></td>
            <td>${isNum?miniSparkSvg(numVal,color):''}</td>
        </tr>`;
    }).join('');
}

async function renderSingleValue(w,body,cfg){
    const key=cfg.key||(cfg.keys?.[0])||'';
    if(!key){body.innerHTML=`<div class="empty-state" style="padding:1rem"><p style="font-size:0.8rem">${t('no_key')}</p></div>`;return;}
    const meta=allKeys.find(k=>k.key===key)||{};
    const icon=typeof getPointIcon==='function'?getPointIcon(meta.type||''):'<i class="fas fa-microchip"></i>';
    const safeName=(meta.name||friendlyName(key)).replace(/'/g,"\\'");
    const safeFullName=(meta.fullName||'').replace(/'/g,"\\'");
    const safeUnits=(meta.units||'').replace(/'/g,"\\'");

    // Build clickable breadcrumb path from parentPath array (same as Favorites)
    const pp=meta.parentPath||[];
    const safePathEnc=encodeURIComponent(JSON.stringify(pp));
    const pathHtml=pp.map((p,i)=>{
        const link=`<a href="/plswk/Assets?node=${p.id}" style="color:inherit;text-decoration:none">${esc(p.name)}</a>`;
        const sep=i<pp.length-1?'<i class="fas fa-chevron-right" style="margin:0 0.4rem;font-size:0.55rem;opacity:0.4"></i>':'';
        return link+sep;
    }).join('');

    const safeEnumValues=encodeURIComponent(JSON.stringify(meta.enumValues||null));

    body.innerHTML=`<div class="sv-card">
        <div class="sv-card-path">${pathHtml||'<span style="opacity:0.4">—</span>'}</div>
        <div class="sv-card-body">
            <div class="sv-card-icon">${icon}</div>
            <div class="sv-card-info">
                <a href="/plswk/Assets?node=${meta.parentId||''}" class="sv-card-name" style="text-decoration:none;color:#fff;display:block">${esc(meta.name||friendlyName(key))}</a>
                <div class="sv-card-fullname">${esc(meta.fullName||key)}</div>
            </div>
            <div class="sv-card-valbox">
                <span class="sv-card-val" id="svv_${w.id}" data-key="${key}">---</span>
                <span class="sv-card-units">${esc(meta.units||'')}</span>
            </div>
        </div>
        <div class="sv-card-actions">
            <button class="btn-icon" title="Trend" onclick="openHistory('${key}','${safeName}','${safeUnits}','${safePathEnc}')"><i class="fas fa-chart-area"></i></button>
            ${meta.isWritable?`<button class="btn-icon" title="Edit Value" onclick="openEdit('${key}','${safeName}','${safeFullName}','${safeUnits}','${safePathEnc}','${meta.type||''}','${safeEnumValues}')"><i class="fas fa-pen"></i></button>`:''}
            <button class="btn-icon" title="Properties" onclick="openProperties('${key}','${safeName}','${safePathEnc}')"><i class="fas fa-cog"></i></button>
        </div>
    </div>`;
    await updateSingleValue(w,cfg);
}

async function updateSingleValue(w,cfg){
    const key=cfg.key||(cfg.keys?.[0])||'';
    if(!key)return;
    let data;try{data=await api(`LatestValues&keys=${key}`);}catch(e){return;}
    const el=document.getElementById('svv_'+w.id);
    if(el){
        const val=data?.[key]||'---';
        const display=formatNumber(val,1);
        if(el.textContent!==display) el.textContent=display;
        updateHistoryLiveValue(key, display);
    }
}

function miniSparkSvg(val,color){
    // Tiny inline spark indicator
    const h=16,w=32;
    return `<svg width="${w}" height="${h}" style="vertical-align:middle"><rect x="0" y="${h/4}" width="${w}" height="${h/2}" rx="2" fill="${hexToRgba(color,0.15)}"/><rect x="0" y="${h/4}" width="${Math.min(w,Math.max(4,w*Math.abs(val%100)/100))}" height="${h/2}" rx="2" fill="${color}" opacity="0.6"/></svg>`;
}

// ── TIMEWINDOW ─────────────────────────────────────────────────
function buildTwPresets(){
    const c=document.getElementById('twPresets');if(!c)return;
    c.innerHTML=PRESETS.map(p=>`<button class="tw-preset${p.ms===twRealtimeMs?' active':''}" onclick="selectPreset(${p.ms},this)">${p.label}</button>`).join('');
}
function toggleTwDropdown(e){e.stopPropagation();document.getElementById('twDropdown').classList.toggle('open');}
document.addEventListener('click',()=>document.getElementById('twDropdown')?.classList.remove('open'));
function setTwMode(mode,btn){
    twMode=mode;
    document.querySelectorAll('.tw-tab').forEach(t=>t.classList.toggle('active',t.dataset.twMode===mode));
    document.getElementById('twRealtimePanel').style.display=mode==='realtime'?'':'none';
    document.getElementById('twHistoryPanel').style.display=mode==='history'?'':'none';
    // Pre-populate date inputs with sensible defaults when switching to history
    if(mode==='history'){
        const fromEl=document.getElementById('twHistFrom'),toEl=document.getElementById('twHistTo');
        if(fromEl&&!fromEl.value){
            const now=new Date();
            const from=new Date(now.getTime()-twRealtimeMs);
            fromEl.value=from.toISOString().slice(0,16);
            toEl.value=now.toISOString().slice(0,16);
        }
    }
}
function selectPreset(ms,btn){
    twRealtimeMs=ms;twMode='realtime';
    document.querySelectorAll('.tw-preset').forEach(b=>b.classList.remove('active'));btn.classList.add('active');
    document.getElementById('twDropdown').classList.remove('open');
    updateTwLabel();refreshAllWidgets();
}
function applyHistoryRange(){
    const from=document.getElementById('twHistFrom').value,to=document.getElementById('twHistTo').value;
    if(!from||!to)return;
    twHistFrom=new Date(from).getTime();twHistTo=new Date(to).getTime();twMode='history';
    document.getElementById('twDropdown').classList.remove('open');
    updateTwLabel();refreshAllWidgets();
}
function updateTwLabel(){
    const lbl=document.getElementById('twLabel'),icon=document.querySelector('.tw-selector .mode-icon');
    if(twMode==='realtime'){
        const p=PRESETS.find(x=>x.ms===twRealtimeMs);
        lbl.textContent=p?p.label:`Last ${Math.round(twRealtimeMs/60000)} min`;
        if(icon) icon.className='fas fa-clock text-[0.9rem] mode-icon';
    }else{
        const f=twHistFrom?new Date(twHistFrom).toLocaleDateString():'?',t=twHistTo?new Date(twHistTo).toLocaleDateString():'?';
        lbl.textContent=`${f} → ${t}`;
        if(icon) icon.className='fas fa-calendar-alt text-[0.9rem] mode-icon';
    }
}
function getTimeRange(){
    if(twMode==='history'&&twHistFrom&&twHistTo)return{startTs:twHistFrom,endTs:twHistTo};
    const now=Date.now();return{startTs:now-twRealtimeMs,endTs:now};
}

// ── ADD/EDIT WIDGET ────────────────────────────────────────────
let selectedType='timeseries', editingWidgetId=null;
async function openAddWidget(){
    editingWidgetId=null;
    document.getElementById('widgetModalTitle').textContent='Add Widget';
    document.getElementById('widgetConfirmText').textContent='Add Widget';
    document.getElementById('widgetTitle').value='';
    document.getElementById('optStacked').checked=false;
    document.getElementById('optLegend').checked=true;
    document.getElementById('optChartType').value='line';
    selectWidgetType(document.querySelector('.wtype-card[data-type="timeseries"]'));
    await loadKeyPicker();
    document.getElementById('addWidgetModal').style.display='flex';
}
function editWidget(wid){
    const w=dashboard.widgets.find(x=>x.id===wid);if(!w)return;
    editingWidgetId=wid;
    document.getElementById('widgetModalTitle').textContent='Edit Widget';
    document.getElementById('widgetConfirmText').textContent='Save Changes';
    document.getElementById('widgetTitle').value=w.title;
    selectWidgetType(document.querySelector(`.wtype-card[data-type="${w.type}"]`));
    const cfg=w.config||{};
    document.getElementById('optChartType').value=cfg.chartType||'line';
    document.getElementById('optStacked').checked=!!cfg.stacked;
    document.getElementById('optLegend').checked=cfg.showLegend!==false;
    loadKeyPicker().then(()=>{
        const keys=cfg.keys||[];if(cfg.key)keys.push(cfg.key);
        document.querySelectorAll('#keyList .key-item input').forEach(cb=>{cb.checked=keys.includes(cb.value);cb.closest('.key-item').classList.toggle('selected',cb.checked);});
    });
    document.getElementById('addWidgetModal').style.display='flex';
}
function closeAddWidget(){document.getElementById('addWidgetModal').style.display='none';}
function selectWidgetType(el){
    if(!el)return;
    document.querySelectorAll('.wtype-card').forEach(c=>c.classList.remove('selected'));
    el.classList.add('selected');selectedType=el.dataset.type;
    document.getElementById('tsOptions').style.display=selectedType==='timeseries'?'':'none';
}
async function loadKeyPicker(){
    if(!allKeys.length){try{allKeys=await api('AvailableKeys');}catch(e){allKeys=[];}}
    renderKeyList(allKeys);
}
function renderKeyList(keys){
    const list=document.getElementById('keyList');
    const multi=selectedType!=='single-value';
    list.innerHTML=keys.map(k=>`<label class="key-item" data-search="${(k.name+' '+k.path+' '+k.key).toLowerCase()}">
        <input type="${multi?'checkbox':'radio'}" name="wkey" value="${k.key}" onchange="handleKeyToggle(this)">
        <div style="min-width:0;flex:1"><div class="key-name">${esc(t(k.name))}</div><div class="key-path">${esc(k.path)}</div></div>
        <span class="key-val">${esc(k.value)} ${esc(k.units)}</span>
    </label>`).join('');
}
function handleKeyToggle(input){
    if(input.type==='radio'){
        document.querySelectorAll('#keyList .key-item').forEach(el=>el.classList.remove('selected'));
        input.closest('.key-item').classList.add('selected');
    } else {
        input.closest('.key-item').classList.toggle('selected',input.checked);
    }
}
function filterKeys(){
    const q=document.getElementById('keySearch').value.toLowerCase();
    document.querySelectorAll('#keyList .key-item').forEach(el=>{el.style.display=el.dataset.search.includes(q)?'':'none';});
}
function confirmAddWidget(){
    const title=document.getElementById('widgetTitle').value.trim()||'Untitled';
    const checked=[...document.querySelectorAll('#keyList input:checked')].map(c=>c.value);
    if(!checked.length){alert('Select at least one data key.');return;}
    const cfg={keys:checked,chartType:document.getElementById('optChartType').value,stacked:document.getElementById('optStacked').checked,showLegend:document.getElementById('optLegend').checked};
    if(selectedType==='single-value')cfg.key=checked[0];

    if(editingWidgetId){
        const w=dashboard.widgets.find(x=>x.id===editingWidgetId);
        if(w){w.title=title;w.type=selectedType;w.config=cfg;
            const body=document.getElementById('wb_'+w.id);if(body)renderWidgetContent(w);
        }
    }else{
        const w={id:'w'+Date.now().toString(36),type:selectedType,title,x:0,y:0,w:selectedType==='single-value'?3:6,h:selectedType==='single-value'?3:4,config:cfg};
        dashboard.widgets=dashboard.widgets||[];dashboard.widgets.push(w);
        addWidgetToGrid(w);
    }
    closeAddWidget();
}
function removeWidget(wid){
    if(!confirm(t('confirm_remove_widget')))return;
    dashboard.widgets=dashboard.widgets.filter(w=>w.id!==wid);
    const el=grid.getGridItems().find(e=>e.getAttribute('gs-id')===wid);
    if(el)grid.removeWidget(el);
    if(charts[wid]){charts[wid].destroy();delete charts[wid];}
    if(!dashboard.widgets.length)document.getElementById('emptyDash').style.display='flex';
}

// ── POLLING ────────────────────────────────────────────────────
function startPolling(){if(pollTimer)clearInterval(pollTimer);pollTimer=setInterval(refreshAllWidgets,3000);}
function refreshAllWidgets(){
    if(!dashboard?.widgets)return;
    dashboard.widgets.forEach(w=>{
        if(w.type==='timeseries'&&twMode==='realtime')renderTimeseries(w,document.getElementById('wb_'+w.id),w.config||{});
        else if(w.type==='latest-values')updateLatestValues(w,w.config||{});
        else if(w.type==='single-value')updateSingleValue(w,w.config||{});
    });
}

// ── HELPERS ────────────────────────────────────────────────────
function esc(s){const d=document.createElement('div');d.textContent=s||'';return d.innerHTML;}
function keyName(key){const parts=key.split('_');return parts.length>2?parts.slice(1,-1).join(' '):key;}
function friendlyName(key){
    const meta=allKeys.find(k=>k.key===key);
    return meta?.name || meta?.fullName || keyName(key);
}
function hexToRgba(hex,a){const r=parseInt(hex.slice(1,3),16),g=parseInt(hex.slice(3,5),16),b=parseInt(hex.slice(5,7),16);return `rgba(${r},${g},${b},${a})`;}
function timeAgo(isoStr){
    if(!isoStr)return '';
    const diff=Date.now()-new Date(isoStr).getTime();
    if(diff<60000)return 'just now';
    if(diff<3600000)return Math.floor(diff/60000)+' min ago';
    if(diff<86400000)return Math.floor(diff/3600000)+' hr ago';
    return Math.floor(diff/86400000)+' days ago';
}

function slugify(text) {
    return text.toString().toLowerCase()
        .replace(/\s+/g, '-')           // Replace spaces with -
        .replace(/[^\w\-]+/g, '')       // Remove all non-word chars
        .replace(/\-\-+/g, '-')         // Replace multiple - with single -
        .replace(/^-+/, '')             // Trim - from start of text
        .replace(/-+$/, '');            // Trim - from end of text
}

function updateHistoryLiveValue(key, value) {
    if (typeof currentHistoryKey !== 'undefined' && currentHistoryKey === key) {
        const hlv = document.getElementById('chartLiveValue');
        if (hlv && document.getElementById('historyModal')?.style.display === 'flex') {
            hlv.textContent = value;
        }
    }
}
