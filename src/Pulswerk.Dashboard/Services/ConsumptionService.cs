// Services/ConsumptionService.cs
// GetConsumptionHistoryAsync and BackfillConsumptionAsync — gap-fill logic for
// hourly/daily/monthly/yearly aggregated consumption data.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pulswerk.Core;
using Pulswerk.Storage;

namespace Pulswerk.Dashboard
{
    public partial class DashboardDataService
    {
        public async Task<List<TsPoint>> GetConsumptionHistoryAsync(
            string baseKey, string interval, string persistedKey,
            long startTs, long endTs,
            List<AvailableTelemetryDto>? allKeys = null,
            bool isConsumption = true)
        {
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // 1. Query existing pre-calculated points
            var existingPoints = await DataStore.QueryAsync(persistedKey, startTs, endTs, limit: 5000);

            // 2. Determine interval in milliseconds
            long intervalMs = interval switch
            {
                "5m"             => 300_000L,
                "1h"             => 3_600_000L,
                "1d"             => 86_400_000L,
                "1m" or "1mo"   => 30L * 86_400_000L,
                "1y"             => 365L * 86_400_000L,
                _                => 3_600_000L
            };

            // 3. Find gaps in [startTs, endTs]
            var gaps = new List<(long Start, long End)>();

            if (existingPoints.Count == 0)
            {
                gaps.Add((startTs, endTs));
            }
            else
            {
                if (existingPoints[0].Ts - startTs > intervalMs * 1.5)
                    gaps.Add((startTs, existingPoints[0].Ts - 1));

                for (int i = 1; i < existingPoints.Count; i++)
                {
                    if (existingPoints[i].Ts - existingPoints[i - 1].Ts > intervalMs * 1.5)
                    {
                        long gapStart = isConsumption
                            ? existingPoints[i - 1].Ts + intervalMs
                            : existingPoints[i - 1].Ts + 1;
                        gaps.Add((gapStart, existingPoints[i].Ts - 1));
                    }
                }

                long targetEnd = Math.Min(endTs, nowMs);
                if (isConsumption)
                {
                    if (targetEnd > existingPoints.Last().Ts + intervalMs)
                        gaps.Add((existingPoints.Last().Ts + intervalMs, targetEnd));
                }
                else
                {
                    if (targetEnd - existingPoints.Last().Ts > intervalMs * 1.5)
                        gaps.Add((existingPoints.Last().Ts + 1, targetEnd));
                }
            }

            if (gaps.Count == 0)
                return existingPoints;

            // 4. Fill gaps
            long totalGapDurationMs = gaps.Sum(g => g.End - g.Start);
            const long ThirtyDaysMs = 30L * 24 * 60 * 60 * 1000;

            if (totalGapDurationMs > ThirtyDaysMs)
            {
                // Large gap — backfill in the background, serve recent data synchronously
                foreach (var gap in gaps)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await BackfillConsumptionAsync(baseKey, interval, persistedKey, gap.Start, gap.End, allKeys, isConsumption);
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[Dashboard] Lazy backfill failed for {persistedKey} ({gap.Start}-{gap.End}): {ex.Message}");
                        }
                    });
                }

                var lastGap = gaps.Last();
                long recentStart = Math.Max(lastGap.Start, endTs - 7L * 24 * 60 * 60 * 1000);
                if (recentStart < lastGap.End)
                {
                    string cleanInterval = interval == "1m" ? "1mo" : interval;
                    List<TsPoint>? recentPoints = baseKey.StartsWith("pathsum(")
                        ? await DataStore.QuerySumAsync(ResolvePathSumKeys(baseKey, allKeys), recentStart, lastGap.End, cleanInterval, isConsumption: isConsumption)
                        : await DataStore.QueryConsumptionAsync(baseKey, cleanInterval, recentStart, lastGap.End);

                    if (recentPoints?.Count > 0)
                        existingPoints.AddRange(recentPoints);
                }

                return existingPoints.OrderBy(p => p.Ts).ToList();
            }
            else
            {
                // Small gap — calculate synchronously and persist
                var allCalculated = new List<TsPoint>();
                string cleanInterval = interval == "1m" ? "1mo" : interval;
                List<string>? resolvedKeys = baseKey.StartsWith("pathsum(")
                    ? ResolvePathSumKeys(baseKey, allKeys)
                    : null;

                foreach (var gap in gaps)
                {
                    List<TsPoint>? calculated = resolvedKeys != null
                        ? await DataStore.QuerySumAsync(resolvedKeys, gap.Start, gap.End, cleanInterval, isConsumption: isConsumption)
                        : await DataStore.QueryConsumptionAsync(baseKey, cleanInterval, gap.Start, gap.End);

                    if (calculated?.Count > 0)
                    {
                        allCalculated.AddRange(calculated);
                        foreach (var p in calculated)
                        {
                            if (p.Value.HasValue && (!isConsumption || p.Ts + intervalMs <= nowMs))
                                DataStore.Insert(persistedKey, p.Ts, p.Value.Value);
                        }
                    }
                }

                if (allCalculated.Count > 0)
                {
                    DataStore.Flush();
                    existingPoints.AddRange(allCalculated);
                }

                return existingPoints.OrderBy(p => p.Ts).ToList();
            }
        }

        private async Task BackfillConsumptionAsync(
            string baseKey, string interval, string persistedKey,
            long startTs, long endTs,
            List<AvailableTelemetryDto>? allKeys = null,
            bool isConsumption = true)
        {
            Log.Info($"[Dashboard] Starting lazy backfill for {persistedKey} ({startTs} to {endTs})");
            string cleanInterval = interval == "1m" ? "1mo" : interval;

            List<TsPoint>? points = baseKey.StartsWith("pathsum(")
                ? await DataStore.QuerySumAsync(ResolvePathSumKeys(baseKey, allKeys), startTs, endTs, cleanInterval, isConsumption: isConsumption)
                : await DataStore.QueryConsumptionAsync(baseKey, cleanInterval, startTs, endTs);

            if (points?.Count > 0)
            {
                long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                long intervalMs = interval switch
                {
                    "5m"           => 300_000L,
                    "1h"           => 3_600_000L,
                    "1d"           => 86_400_000L,
                    "1m" or "1mo" => 30L * 86_400_000L,
                    "1y"           => 365L * 86_400_000L,
                    _              => 3_600_000L
                };

                foreach (var p in points)
                    if (p.Value.HasValue && (!isConsumption || p.Ts + intervalMs <= nowMs))
                        DataStore.Insert(persistedKey, p.Ts, p.Value.Value);

                DataStore.Flush();
                Log.Info($"[Dashboard] Lazy backfill completed for {persistedKey}: wrote {points.Count} points");
            }
        }
    }
}
