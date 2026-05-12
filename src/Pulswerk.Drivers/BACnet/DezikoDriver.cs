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
            if (!extraProps.TryGetValue(info.ObjectId, out var props))
                return info;

            string nameExt = info.NameExtension;
            if (props.TryGetValue(PropNameExtension, out var vals1) && vals1.Count > 0)
                nameExt = vals1[0].Value?.ToString() ?? "";

            var namingPath = info.NamingPath;
            if (props.TryGetValue(PropNamingPath, out var vals2))
            {
                namingPath = new List<string>();
                foreach (var v in vals2)
                {
                    string? s = v.Value?.ToString();
                    if (!string.IsNullOrEmpty(s)) namingPath.Add(s);
                }
            }

            int category = info.Category;
            if (props.TryGetValue(PropCategory4941, out var vals3) && vals3.Count > 0)
                category = Convert.ToInt32(vals3[0].Value);

            BacnetObjectId? logId = info.LogObjectId;
            if (props.TryGetValue(PropTrendLogReference, out var vals4) && vals4.Count > 0)
            {
                if (vals4[0].Value is BacnetObjectId oid) logId = oid;
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
    }
}
