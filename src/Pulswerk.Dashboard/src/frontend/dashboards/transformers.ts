// transformers.ts - Pure functions for data transformation

export interface ChartSeries {
    name: string;
    data: { x: number; y: number }[];
}

/**
 * Transforms raw widget data into ApexCharts series format.
 */
export function transformToChartSeries(
    data: any, 
    keys: string[], 
    allKeysMeta: any[], 
    colors: string[], 
    isStacked: boolean,
    friendlyNameFn: (key: string) => string
): { series: ChartSeries[], usedColors: string[] } {
    
    const series: ChartSeries[] = [];
    const usedColors: string[] = [];
    
    keys.forEach((key, i) => {
        const color = colors[i % colors.length];
        const raw = data?.[key] || [];
        const points = raw.map((p: any) => ({
            x: typeof p.ts === 'number' ? p.ts : new Date(p.ts).getTime(),
            y: p.value != null ? parseFloat(parseFloat(p.value).toFixed(2)) : NaN
        })).filter((p: any) => !isNaN(p.y));

        const meta = allKeysMeta.find(k => k.key === key);
        series.push({ 
            name: meta?.fullName || friendlyNameFn(key), 
            data: points 
        });
        usedColors.push(color);
    });

    // For stacked charts, trim all series to the earliest endpoint so
    // missing data at the right edge isn't treated as 0 by ApexCharts.
    if (isStacked && series.length > 1) {
        const seriesWithData = series.filter(s => s.data.length > 0);
        if (seriesWithData.length > 1) {
            const commonEnd = Math.min(...seriesWithData.map(s => s.data[s.data.length - 1].x));
            series.forEach(s => {
                s.data = s.data.filter((p: any) => p.x <= commonEnd);
            });
        }
    }

    return { series, usedColors };
}

/**
 * Calculates Y-axis min/max constraints based on data points to ensure 0 is visible.
 */
export function calculateYAxisConstraints(series: ChartSeries[]): any {
    let yDataMin = Infinity, yDataMax = -Infinity;
    series.forEach(s => s.data.forEach((p: any) => {
        if (p.y < yDataMin) yDataMin = p.y;
        if (p.y > yDataMax) yDataMax = p.y;
    }));
    
    const yAxisOpts: any = { labels: { style: { colors: '#94a3b8', fontSize: '10px' } } };
    if (yDataMin >= 0 && yDataMin !== Infinity) yAxisOpts.min = 0;       // all positive → pin bottom to 0
    else if (yDataMax <= 0 && yDataMax !== -Infinity) yAxisOpts.max = 0;   // all negative → pin top to 0
    
    return yAxisOpts;
}
