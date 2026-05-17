/// <reference path="../types/pulswerk.d.ts" />

let chart: any;
let pushBuffer: number[] = Array(60).fill(0);
let pullBuffer: number[] = Array(60).fill(0);
let lastPush = 0;
let lastPull = 0;

export function initHeartbeatPage(initialStats: { totalPush: number; totalPull: number }): void {
    lastPush = initialStats.totalPush;
    lastPull = initialStats.totalPull;

    initChart();
    setInterval(updateStats, 1000);
}

function initChart(): void {
    const el = document.querySelector("#throughputChart");
    if (!el || typeof ApexCharts === 'undefined') return;

    const options = {
        series: [
            { name: 'Push (COV)', data: pushBuffer },
            { name: 'Pull (Poll)', data: pullBuffer }
        ],
        chart: {
            type: 'area',
            height: '100%',
            stacked: true,
            animations: { enabled: false },
            toolbar: { show: false },
            sparkline: { enabled: false }
        },
        colors: ['#8b5cf6', '#10b981'],
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
        legend: {
            show: true,
            position: 'top',
            horizontalAlign: 'right',
            labels: { colors: '#94a3b8' },
            markers: { width: 8, height: 8, radius: 4 },
            fontSize: '12px',
            fontFamily: 'inherit'
        },
        grid: {
            borderColor: 'rgba(255,255,255,0.1)',
            xaxis: { lines: { show: false } },
            yaxis: { lines: { show: true } },
            padding: { bottom: 20, left: 10, right: 10, top: 0 }
        },
        xaxis: {
            labels: { 
                show: true,
                style: { colors: '#64748b', fontSize: '10px' },
                formatter: (val: number) => val > 0 ? `-${60 - val}s` : ''
            },
            axisBorder: { show: true, color: 'rgba(255,255,255,0.1)' },
            axisTicks: { show: false }
        },
        yaxis: {
            min: 0,
            forceNiceScale: true,
            labels: {
                style: { colors: '#64748b', fontSize: '10px' }
            },
            title: { text: 'updates/s', style: { color: '#475569', fontSize: '11px' } }
        },
        dataLabels: { enabled: false },
        tooltip: {
            theme: 'dark',
            shared: true,
            intersect: false,
            y: { formatter: (val: number) => val.toLocaleString() + ' upd/s' }
        }
    };

    chart = new ApexCharts(el, options);
    chart.render();
}

async function updateStats(): Promise<void> {
    try {
        const res = await fetch('?handler=Stats');
        const data = await res.json();
        
        document.getElementById('upm')!.textContent = data.updatesPerMinute.toFixed(1);
        document.getElementById('points')!.textContent = data.totalDataPoints.toLocaleString();

        const badge = document.getElementById('statusBadge')!;
        if (data.isScanning) {
            badge.innerHTML = '<div class="w-3 h-3 rounded-full bg-amber-500 animate-pulse"></div><span class="text-sm font-medium text-amber-400 uppercase tracking-wider">Scanning Discovery/History...</span>';
        } else {
            badge.innerHTML = '<div class="w-3 h-3 rounded-full bg-emerald-500 animate-pulse"></div><span class="text-sm font-medium text-emerald-400 uppercase tracking-wider">System Operational</span>';
        }
        
        const dbSizeElem = document.getElementById('dbSize')!;
        if (data.databaseSizeBytes > 1024 * 1024 * 1024) {
            dbSizeElem.textContent = (data.databaseSizeBytes / (1024.0 * 1024.0 * 1024.0)).toFixed(2) + ' GB';
        } else {
            dbSizeElem.textContent = (data.databaseSizeBytes / (1024.0 * 1024.0)).toFixed(2) + ' MB';
        }
        
        document.getElementById('totalUpdates')!.textContent = data.totalUpdates.toLocaleString();
        document.getElementById('totalPush')!.textContent = data.totalPushUpdates.toLocaleString();
        document.getElementById('totalPull')!.textContent = data.totalPullUpdates.toLocaleString();
        
        // Memory
        const mem = document.getElementById('memStats');
        if (mem) mem.textContent = `${data.workingSetMb} MB / ${data.gcHeapMb} MB`;

        const tcpEl = document.getElementById('tcpConns');
        if (tcpEl) tcpEl.textContent = data.tcpConnections;

        // Oldest device seen
        const oldest = document.getElementById('oldestSeen');
        if (oldest && data.oldestDeviceSeenUtc) oldest.textContent = data.oldestDeviceSeenUtc + ' UTC';

        // Format uptime
        const s = data.uptimeSeconds;
        const d = Math.floor(s / 86400);
        const h = Math.floor((s % 86400) / 3600);
        const m = Math.floor((s % 3600) / 60);
        const sec = s % 60;
        document.getElementById('uptime')!.textContent = d > 0 ? `${d}d ${h}h ${m}m` : (h > 0 ? `${h}h ${m}m ${sec}s` : `${m}m ${sec}s`);

        // Update stacked chart
        const pushDiff = Math.max(0, data.totalPushUpdates - lastPush);
        const pullDiff = Math.max(0, data.totalPullUpdates - lastPull);
        lastPush = data.totalPushUpdates;
        lastPull = data.totalPullUpdates;

        pushBuffer.shift(); pushBuffer.push(pushDiff);
        pullBuffer.shift(); pullBuffer.push(pullDiff);

        if (chart) {
            chart.updateSeries([
                { data: pushBuffer },
                { data: pullBuffer }
            ]);
        }
    } catch (e) {
        console.error('Failed to update stats:', e);
    }
}

(window as any).initHeartbeatPage = initHeartbeatPage;
