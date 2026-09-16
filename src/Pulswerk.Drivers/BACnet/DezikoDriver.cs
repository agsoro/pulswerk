// DezikoDriver.cs – Deziko BACnet driver with proprietary extensions
//
//  Extends BacnetDriver with Deziko-specific features:
//    • Proprietary properties: NamingPath (4397), NameExtension (4438),
//      Category (4941), TrendLogReference (4452)
//    • Structured View hierarchy walking via BacnetHierarchy
//    • Hierarchy-aware alarm routing (waits for tree before emitting alarms)
//    • Technical key-prefix resolution (e.g. g01_asp01...)
//
//  Register this driver for deviceType "deziko" in DeviceDriverFactory.

using System;
using System.IO.BACnet;
using System.Linq;

using Pulswerk.Core;

namespace Pulswerk.Drivers.BACnet
{
    /// <summary>
    /// Deziko BACnet driver.
    /// Adds proprietary property reads and Structured View hierarchy walking
    /// on top of the standard <see cref="BacnetDriver"/>.
    /// </summary>
    public class DezikoDriver : BacnetDriver
    {
        public override string DriverName => "Deziko";

        // ── Proprietary Deziko property IDs ──────────────────────────────────
        protected const BacnetPropertyIds PropNameExtension = (BacnetPropertyIds)4438;
        protected const BacnetPropertyIds PropNamingPath = (BacnetPropertyIds)4397;
        protected const BacnetPropertyIds PropCategory4941 = (BacnetPropertyIds)4941;
        protected const BacnetPropertyIds PropTrendLogReference = (BacnetPropertyIds)4452;

        // ── Proprietary property enrichment ──────────────────────────────────
        protected override List<BacnetPropertyIds> GetExtraDiscoveryProperties() => new()
        {
            PropNameExtension,
            PropNamingPath,
            PropCategory4941,
            PropTrendLogReference
        };

        /// <inheritdoc />
        protected override BacnetObjectInfo EnrichObjectInfo(
            BacnetClient client, BacnetAddress address,
            BacnetObjectInfo info,
            Dictionary<BacnetObjectId, Dictionary<BacnetPropertyIds, List<BacnetValue>>> extraProps)
        {
            // ── 1. Try the batch RPM data first ─────────────────────────────
            bool hasBatchData = extraProps.TryGetValue(info.ObjectId, out var props)
                             && props.ContainsKey(PropNamingPath);

            string nameExt = info.NameExtension;
            var namingPath = info.NamingPath;
            int category = info.Category;
            BacnetObjectId? logId = info.LogObjectId;

            if (hasBatchData)
            {
                // Batch RPM returned the proprietary properties
                if (props!.TryGetValue(PropNameExtension, out var vals1) && vals1.Count > 0)
                    nameExt = vals1[0].Value?.ToString() ?? "";

                if (props.TryGetValue(PropNamingPath, out var vals2))
                {
                    namingPath = new List<string>();
                    foreach (var v in vals2)
                        ExtractStrings(v, namingPath);
                }

                if (props.TryGetValue(PropCategory4941, out var vals3) && vals3.Count > 0)
                {
                    if (Pulswerk.Drivers.BACnet.BacnetValueConverter.TryToDouble(vals3[0].Value, out double d))
                        category = (int)d;
                }

                if (props.TryGetValue(PropTrendLogReference, out var vals4) && vals4.Count > 0)
                {
                    if (vals4[0].Value is BacnetObjectId oid) logId = oid;
                }
            }
            else
            {
                // ── 2. Fallback: individual ReadProperty per proprietary prop ──
                // The batch RPM often doesn't return proprietary Deziko properties;
                // Deziko controllers require individual reads for these.
                try
                {
                    // NamingPath (4397) – most important for hierarchy
                    if (client.ReadPropertyRequest(address, info.ObjectId, PropNamingPath, out var npValues))
                    {
                        namingPath = new List<string>();
                        foreach (var v in npValues)
                            ExtractStrings(v, namingPath);
                    }
                }
                catch { }

                try
                {
                    // NameExtension (4438) – friendly alias
                    if (client.ReadPropertyRequest(address, info.ObjectId, PropNameExtension, out var neValues)
                        && neValues.Count > 0)
                    {
                        nameExt = neValues[0].Value?.ToString() ?? "";
                    }
                }
                catch { }

                try
                {
                    // Category (4941)
                    if (client.ReadPropertyRequest(address, info.ObjectId, PropCategory4941, out var catValues)
                        && catValues.Count > 0)
                    {
                        if (Pulswerk.Drivers.BACnet.BacnetValueConverter.TryToDouble(catValues[0].Value, out double d))
                            category = (int)d;
                    }
                }
                catch { }

                try
                {
                    // TrendLogReference (4452)
                    if (client.ReadPropertyRequest(address, info.ObjectId, PropTrendLogReference, out var tlValues)
                        && tlValues.Count > 0)
                    {
                        if (tlValues[0].Value is BacnetObjectId oid) logId = oid;
                    }
                }
                catch { }
            }

            return info with
            {
                NameExtension = nameExt,
                NamingPath = namingPath,
                Category = category,
                LogObjectId = logId
            };
        }

        // ── Key Resolution ───────────────────────────────────────────────────

        /// <inheritdoc />
        protected override BacnetObjectId? ResolveObjectIdFromKey(string key, DiscoveryState state)
        {
            // 1. Try lookup by technical KeyPrefix (Deziko style: "g01_asp01_rlt001_t_su_value")
            var obj = state.CachedObjects
                .FirstOrDefault(o => key.StartsWith(o.KeyPrefix + "_", StringComparison.OrdinalIgnoreCase));

            if (obj != null) return obj.ObjectId;

            // 2. Fallback to legacy parsing (e.g. "ao_3_supply_temp_sp_value")
            return base.ResolveObjectIdFromKey(key, state);
        }



        /// <inheritdoc />
        protected override bool ShouldDelayAlarm(DeviceConfig device, DiscoveryState state)
        {
            // Deziko devices with hierarchy enabled wait until Structured View tree is built
            // before emitting alarms to ensure correct asset mapping.
            return device.HierarchyEnabled && !state.HierarchyReady;
        }

        /// <summary>
        /// Recursively unwraps BacnetValue wrappers to extract actual string values.
        /// The BACnet library wraps array properties as nested List&lt;BacnetValue&gt;,
        /// so v.Value?.ToString() gives the .NET type name instead of the data.
        /// </summary>
        private static void ExtractStrings(BacnetValue v, List<string> result)
        {
            if (v.Value is IList<BacnetValue> nested)
            {
                foreach (var inner in nested)
                    ExtractStrings(inner, result);
            }
            else
            {
                string? s = v.Value?.ToString();
                if (!string.IsNullOrEmpty(s)) result.Add(s);
            }
        }
    }
}
