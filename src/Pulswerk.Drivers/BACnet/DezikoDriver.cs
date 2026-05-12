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

        /// <inheritdoc />
        protected override BacnetObjectInfo EnrichObjectInfo(
            BacnetClient client, BacnetAddress address,
            BacnetObjectInfo info)
        {
            // Read Deziko proprietary properties
            string nameExt = ReadStringProp(client, address, info.ObjectId, PropNameExtension);
            var namingPath = ReadStringListProp(client, address, info.ObjectId, PropNamingPath);

            ReadIntProp(client, address, info.ObjectId, PropCategory4941, out int category);
            ReadObjectIdProp(client, address, info.ObjectId, PropTrendLogReference, out var logId);

            return info with
            {
                NameExtension = nameExt,
                NamingPath = namingPath,
                Category = category,
                LogObjectId = logId ?? info.LogObjectId  // keep standard trend log ref if no proprietary one
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
