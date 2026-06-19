// transformers.ts - Pure functions for data transformation
function alignTimestamp(ts, granularity) {
    if (!granularity)
        return ts;
    const d = new Date(ts);
    if (granularity === 'hour') {
        d.setUTCMinutes(0, 0, 0);
    }
    else if (granularity === 'day') {
        d.setUTCHours(0, 0, 0, 0);
    }
    else if (granularity === 'month') {
        d.setUTCDate(1);
        d.setUTCHours(0, 0, 0, 0);
    }
    else if (granularity === 'year') {
        d.setUTCMonth(0, 1);
        d.setUTCHours(0, 0, 0, 0);
    }
    return d.getTime();
}
/**
 * Transforms raw widget data into ApexCharts series format.
 */
export function transformToChartSeries(data, keys, allKeysMeta, colors, isStacked, friendlyNameFn, isBar = false, barGranularity) {
    const series = [];
    const usedColors = [];
    // Pre-calculate unique names for all keys to avoid ApexCharts bugs where series with the same name overlap instead of stacking
    const resolvedNames = keys.map(key => {
        const meta = allKeysMeta.find(k => k.key === key);
        const baseName = meta?.name || friendlyNameFn(key);
        return { key, meta, baseName, finalName: baseName };
    });
    const nameCounts = new Map();
    resolvedNames.forEach(item => {
        nameCounts.set(item.baseName, (nameCounts.get(item.baseName) || 0) + 1);
    });
    resolvedNames.forEach(item => {
        if (nameCounts.get(item.baseName) > 1) {
            let differentiator = '';
            if (item.meta?.parentPath && item.meta.parentPath.length > 0) {
                differentiator = item.meta.parentPath[item.meta.parentPath.length - 1]?.name;
            }
            else if (item.meta?.fullName) {
                const parts = item.meta.fullName.split(/[\/›]/).map((p) => p.trim());
                if (parts.length >= 2) {
                    differentiator = parts[parts.length - 2];
                }
                else {
                    differentiator = item.meta.fullName;
                }
            }
            if (!differentiator) {
                differentiator = item.key;
            }
            item.finalName = `${item.baseName} (${differentiator})`;
        }
    });
    const finalNameCounts = new Map();
    resolvedNames.forEach(item => {
        finalNameCounts.set(item.finalName, (finalNameCounts.get(item.finalName) || 0) + 1);
    });
    const finalSeen = new Map();
    resolvedNames.forEach(item => {
        const count = finalNameCounts.get(item.finalName);
        if (count > 1) {
            const seenIndex = (finalSeen.get(item.finalName) || 0) + 1;
            finalSeen.set(item.finalName, seenIndex);
            item.finalName = `${item.finalName} (${seenIndex})`;
        }
    });
    const uniqueNamesMap = new Map();
    resolvedNames.forEach(item => {
        uniqueNamesMap.set(item.key, item.finalName);
    });
    if (isStacked || isBar) {
        // Collect all unique timestamps across all series
        const allTsSet = new Set();
        const tempSeries = [];
        keys.forEach((key) => {
            const raw = data?.[key] || [];
            const dataMap = new Map();
            raw.forEach((p) => {
                const rawTs = typeof p.ts === 'number' ? p.ts : new Date(p.ts).getTime();
                const ts = alignTimestamp(rawTs, barGranularity);
                const val = p.value != null ? parseFloat(parseFloat(p.value).toFixed(2)) : NaN;
                if (!isNaN(val)) {
                    dataMap.set(ts, val);
                    allTsSet.add(ts);
                }
            });
            tempSeries.push({
                name: uniqueNamesMap.get(key),
                dataMap
            });
        });
        const allTimestamps = Array.from(allTsSet).sort((a, b) => a - b);
        tempSeries.forEach((tsEntry, i) => {
            const color = colors[i % colors.length];
            const points = allTimestamps.map(ts => {
                const val = tsEntry.dataMap.has(ts) ? tsEntry.dataMap.get(ts) : 0;
                return { x: ts, y: val };
            });
            series.push({
                name: tsEntry.name,
                data: points
            });
            usedColors.push(color);
        });
    }
    else {
        keys.forEach((key, i) => {
            const color = colors[i % colors.length];
            const raw = data?.[key] || [];
            const points = raw.map((p) => ({
                x: typeof p.ts === 'number' ? p.ts : new Date(p.ts).getTime(),
                y: p.value != null ? parseFloat(parseFloat(p.value).toFixed(2)) : NaN
            })).filter((p) => !isNaN(p.y));
            series.push({
                name: uniqueNamesMap.get(key),
                data: points
            });
            usedColors.push(color);
        });
    }
    // For stacked charts, trim all series to the earliest endpoint so
    // missing data at the right edge isn't treated as 0 by ApexCharts.
    if (isStacked && series.length > 1) {
        const seriesWithData = series.filter(s => s.data.length > 0);
        if (seriesWithData.length > 1) {
            const commonEnd = Math.min(...seriesWithData.map(s => s.data[s.data.length - 1].x));
            series.forEach(s => {
                s.data = s.data.filter((p) => p.x <= commonEnd);
            });
        }
    }
    return { series, usedColors };
}
/**
 * Calculates Y-axis min/max constraints based on data points to ensure 0 is visible.
 */
export function calculateYAxisConstraints(series) {
    let yDataMin = Infinity, yDataMax = -Infinity;
    series.forEach(s => s.data.forEach((p) => {
        if (p.y < yDataMin)
            yDataMin = p.y;
        if (p.y > yDataMax)
            yDataMax = p.y;
    }));
    const yAxisOpts = { labels: { style: { colors: '#94a3b8', fontSize: '10px' } } };
    if (yDataMin >= 0 && yDataMin !== Infinity)
        yAxisOpts.min = 0; // all positive → pin bottom to 0
    else if (yDataMax <= 0 && yDataMax !== -Infinity)
        yAxisOpts.max = 0; // all negative → pin top to 0
    return yAxisOpts;
}
