// BacnetDriver.cs – BACnet/IP device reader with full object discovery and filtering
//
//  Discovery flow (run on startup and optionally on a timer):
//    1. Unicast Who-Is  →  I-Am  →  resolves BacnetAddress
//    2. Read DEVICE:bacnetDeviceId / PROP_OBJECT_LIST  →  full object list
//       (falls back to segmented index-by-index read if the device doesn't
//        support returning the whole list at once)
//    3. For every object, read PROP_OBJECT_NAME to get the human-readable name
//    4. Apply the filter block from config (objectTypes, instanceRange, namePattern,
//       excludeNamePattern)
//    5. Cache the surviving list.  On every poll, call ReadPropertyMultiple on
//       the cached objects to read TelemetryValues + attribute properties.
//
//  Standalone mode distinction:
//    data point properties  → published as time-series (ts + values)
//    attributes properties  → published to dashboard (key-value, no timestamp)
//
//  NuGet:  BACnet 3.0.2  (ela-compil / System.IO.BACnet)

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO.BACnet;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Pulswerk.Core;
using Pulswerk.Storage;

namespace System.IO.BACnet
{
    // Fallback definitions for Weekly_Schedule support if the linked library version misses them
    public struct BacnetTimeValue
    {
        public BacnetTime Time;
        public BacnetValue Value;
        public BacnetTimeValue(BacnetTime time, BacnetValue value) { Time = time; Value = value; }
    }
    public struct BacnetTime
    {
        public byte Hour; public byte Minute; public byte Second; public byte Hundredths;
        public BacnetTime(byte hour, byte minute, byte second, byte hundredths)
        {
            Hour = hour; Minute = minute; Second = second; Hundredths = hundredths;
        }
        public override string ToString() => $"{Hour:D2}:{Minute:D2}:{Second:D2}";
    }
}

namespace Pulswerk.Drivers.BACnet
{
    using Attributes = Dictionary<string, string>;
    using TelemetryValues = Dictionary<string, object>;

    /// <summary>
    /// Conflates high-frequency data point updates into batches to reduce system load.
    /// </summary>
    public class TelemetryConflator : IDisposable
    {
        private readonly Func<TelemetryValues, Task> _publisher;
        private readonly ConcurrentDictionary<string, object> _buffer = new();
        private readonly System.Threading.Timer _timer;
        private int _isFlushing = 0;

        public TelemetryConflator(Func<TelemetryValues, Task> publisher, int intervalMs = 250)
        {
            _publisher = publisher;
            _timer = new System.Threading.Timer(OnTimer, null, intervalMs, intervalMs);
        }

        public void Add(TelemetryValues data)
        {
            foreach (var kv in data)
                _buffer[kv.Key] = kv.Value;
        }

        private void OnTimer(object? state)
        {
            if (Interlocked.CompareExchange(ref _isFlushing, 1, 0) != 0) return;
            try
            {
                if (_buffer.IsEmpty) return;

                // Fetch all keys currently in buffer
                var keys = _buffer.Keys.ToList();
                var snapshot = new TelemetryValues();

                foreach (var k in keys)
                {
                    if (_buffer.TryRemove(k, out var val))
                        snapshot[k] = val;

                    // Flush in chunks of 200
                    if (snapshot.Count >= 200)
                    {
                        var batch = snapshot;
                        snapshot = new TelemetryValues();
                        PublishBatch(batch);
                    }
                }

                if (snapshot.Count > 0)
                {
                    PublishBatch(snapshot);
                }
            }
            catch (Exception ex)
            {
                Pulswerk.Core.Log.Error($"[Conflator] Flush error: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _isFlushing, 0);
            }
        }

        private void PublishBatch(TelemetryValues batch)
        {
            // Fire-and-forget publish with error handling
            _ = Task.Run(async () =>
            {
                try { await _publisher(batch); }
                catch (Exception ex) { Pulswerk.Core.Log.Error($"[Conflator] Publish failed: {ex.Message}"); }
            });
        }

        public void Dispose() => _timer.Dispose();
    }

    // =========================================================================
    //  Result returned by BacnetReader – extends the base TelemetryValues with
    //  an optional Attributes bag for device metadata.
    // =========================================================================
    public class BacnetReadResult
    {
        public TelemetryValues TelemetryValues { get; } = new();
        public Attributes Attributes { get; } = new();
        /// <summary>True on the first poll after a (re-)discovery. Signals the background job to re-provision the hierarchy.</summary>
        public bool HierarchyDirty { get; set; }
    }

    // =========================================================================
    //  Discovered object descriptor (cached after discovery)
    // =========================================================================
    public record DiscoveryResult(
        List<BacnetObjectInfo> Objects,
        Dictionary<BacnetObjectId, Dictionary<BacnetPropertyIds, List<BacnetValue>>> ExtraProperties
    );

    public record BacnetObjectInfo(
        string TechDeviceId,
        uint DeviceId,
        BacnetObjectId ObjectId,
        string ObjectName,           // technical path (from PROP_OBJECT_NAME)
        List<string> NamingPath,     // friendly path segments (vendor-specific)
        string NameExtension = "",   // friendly alias (vendor-specific)
        string Description = "",     // from PROP_DESCRIPTION
        string Units = "",           // from PROP_UNITS
        string ProfileName = "",     // from PROP_PROFILE_NAME (168)
        bool Commandable = false,    // true when PROP_PRIORITY_ARRAY present
        bool Writeable = false,      // true when it's a config value type AND NOT commandable
        int Category = -1,           // vendor-specific category
        BacnetObjectId? LogObjectId = null,   // associated trend log
        List<string>? StateText = null,       // from PROP_STATE_TEXT (110)
        double? HighLimit = null,    // from PROP_HIGH_LIMIT (45)
        double? LowLimit = null,     // from PROP_LOW_LIMIT (59)
        double? Deadband = null,     // from PROP_DEADBAND (25)
        uint? LimitEnable = null,    // from PROP_LIMIT_ENABLE (52)
        double? Resolution = null,   // from PROP_RESOLUTION (108)
        double? CovIncrement = null  // from PROP_COV_INCREMENT (109)
    )
    {
        /// <summary>Sanitised technical ObjectName used as the key prefix, e.g. "ahu-01_rlt001_t_su".</summary>
        public string KeyPrefix => $"{TechDeviceId}_{Sanitise(ObjectName)}";

        public static string ShortTypeName(BacnetObjectTypes t) => t switch
        {
            BacnetObjectTypes.OBJECT_ANALOG_INPUT => "ai",
            BacnetObjectTypes.OBJECT_ANALOG_OUTPUT => "ao",
            BacnetObjectTypes.OBJECT_ANALOG_VALUE => "av",
            BacnetObjectTypes.OBJECT_BINARY_INPUT => "bi",
            BacnetObjectTypes.OBJECT_BINARY_OUTPUT => "bo",
            BacnetObjectTypes.OBJECT_BINARY_VALUE => "bv",
            BacnetObjectTypes.OBJECT_MULTI_STATE_INPUT => "mi",
            BacnetObjectTypes.OBJECT_MULTI_STATE_OUTPUT => "mo",
            BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE => "mv",
            BacnetObjectTypes.OBJECT_INTEGER_VALUE => "iv",
            BacnetObjectTypes.OBJECT_DEVICE => "dev",
            (BacnetObjectTypes)264 => "sys",
            _ => t.ToString().Replace("OBJECT_", "").ToLower(),
        };

        public static string Sanitise(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            name = name.ToLowerInvariant().Trim()
                .Replace("ä", "a")
                .Replace("ö", "o")
                .Replace("ü", "u")
                .Replace("ß", "ss");
            return Regex.Replace(name, @"[^a-z0-9]+", "_").Trim('_');
        }
    }

    // =========================================================================
    //  COV value snapshot – stored when a COV notification arrives
    // =========================================================================
    public record CovSnapshot(double Value, DateTime ReceivedAt);

    // =========================================================================
    //  The reader + writer
    // =========================================================================
    public class BacnetDriver : IDeviceDriver, IDeviceWriter
    {
        public virtual string DriverName => "BACnet";
        public IEnumerable<string> GetTelemetryKeys() => Array.Empty<string>();

        private int _busyCount = 0;
        public bool IsBusy => _busyCount > 0;

        // Per-device discovery state (keyed by device name so multiple devices work)
        protected readonly Dictionary<string, DiscoveryState> _stateByDevice = new();
        protected readonly object _stateLock = new();


        // ── BACnet Alarm Acknowledgment Registry ─────────────────────────────
        // Key: "{connectionId}:{objType}:{objInstance}"  (matches details["bacnetAckKey"] stored in TB alarm)
        // Stores the live client/address context needed to send AlarmAcknowledgement to the field device.
        // Populated when an alarm is raised; survives until the connector restarts.
        private record BacnetAckContext(
            BacnetClient Client,
            BacnetAddress Address,
            BacnetObjectId ObjectId,
            BacnetEventStates EventState,
            DateTime EventTime);

        private static readonly ConcurrentDictionary<string, BacnetAckContext> _ackRegistry = new();

        /// <summary>Tracks devices that have already logged their hierarchy conversion stats (avoids log spam on every poll).</summary>
        private static readonly HashSet<string> _hierarchyLogged = new();

        private static readonly ConcurrentDictionary<string, BacnetClient> _clientsByConnection = new();
        private static readonly ConcurrentDictionary<(string, int), BacnetClient> _clientsByEndpoint = new();

        public static void RegisterClient(string connId, string addr, int port, BacnetClient client)
        {
            _clientsByConnection[connId] = client;
            string bindAddr = string.IsNullOrWhiteSpace(addr) ? "0.0.0.0" : addr;
            if (port > 0) _clientsByEndpoint[(bindAddr, port)] = client;
        }

        public static void ClearClients()
        {
            _clientsByConnection.Clear();
            _clientsByEndpoint.Clear();
        }

        /// <summary>
        /// Sends a BACnet AcknowledgeAlarm service to the originating field device.
        /// This mirrors the Deziko behaviour: when an operator acknowledges an
        /// alarm in the management UI, Deziko sends AlarmAcknowledgement back to the
        /// BACnet controller so the controller's ACKED_TRANSITIONS property is updated.
        /// </summary>
        /// <param name="ackKey">Value of detail["bacnetAckKey"] from the TB alarm.</param>
        /// <param name="operatorText">Operator comment / acknowledgment source string.</param>
        /// <returns>True if the BACnet service call succeeded, false otherwise.</returns>
        public static bool SendAlarmAcknowledgement(string ackKey, string operatorText)
        {
            if (!_ackRegistry.TryGetValue(ackKey, out var ctx))
            {
                Pulswerk.Core.Log.Warning($"[BACnet-Ack] No live context for key '{ackKey}' — connector may have restarted.");
                return false;
            }

            try
            {
                var evTs = new BacnetGenericTime(ctx.EventTime, BacnetTimestampTags.TIME_STAMP_DATETIME, 0);
                var ackTs = new BacnetGenericTime(DateTime.UtcNow, BacnetTimestampTags.TIME_STAMP_DATETIME, 0);
                bool ok = ctx.Client.AlarmAcknowledgement(
                    ctx.Address, ctx.ObjectId, ctx.EventState,
                    operatorText, evTs, ackTs, 0);
                if (ok)
                    Pulswerk.Core.Log.Info($"[BACnet-Ack] AlarmAcknowledgement sent → {ctx.Address} obj={ctx.ObjectId} state={ctx.EventState}");
                else
                    Pulswerk.Core.Log.Error($"[BACnet-Ack] AlarmAcknowledgement FAILED → {ctx.Address} obj={ctx.ObjectId}");
                return ok;
            }
            catch (Exception ex)
            {
                Pulswerk.Core.Log.Error($"[BACnet-Ack] Exception sending AlarmAcknowledgement: {ex.Message}");
                return false;
            }
        }

        protected const BacnetPropertyIds PropProfileName = BacnetPropertyIds.PROP_PROFILE_NAME;

        // =====================================================================
        //  IDeviceDriver.Read  –  called by the polling loop
        // =====================================================================
        public TelemetryValues Read(ConnectionConfig conn, DeviceConfig device)
        {
            // BacnetReader.Read() only returns TelemetryValues for the base interface.
            // Call ReadFull() from Program.cs to get both TelemetryValues and attributes.
            return ReadFull(conn, device).TelemetryValues;
        }

        /// <summary>Returns both TelemetryValues and attributes. Called directly from Program.cs.</summary>
        public virtual BacnetReadResult ReadFull(ConnectionConfig conn, DeviceConfig device,
            AlarmStore? alarmStore = null, TelemetryStore? dataStore = null,
            bool isRecovery = false)
        {
            if (device.DeviceId is null)
                throw new InvalidOperationException($"Device '{device.Name}' is missing deviceId.");

            var cfg = device;  // BACnet config is flat on DeviceConfig
            var result = new BacnetReadResult();

            var client = OpenClient(conn);
            try
            {

                // ── Resolve BacnetAddress ─────────────────────────────────────────
                // Use device-specific host if provided; otherwise fallback to connection host 
                // (though in the new model, connection host is the local bind IP).
                var address = ResolveAddress(client, device.Address ?? conn.Address ?? "", conn.Port ?? 47808,
                                             device.DeviceId.Value,
                                             cfg.WhoIsTimeoutMs > 0 ? cfg.WhoIsTimeoutMs : 2000);
                // ResolveAddress always returns a non-null address (direct IP fallback)

                // ── Discovery (lazy + periodic) ───────────────────────────────────
                var state = GetOrCreateState(device.Name);
                var disc = cfg.Discovery ?? BacnetDiscoveryConfig.Default;
                bool needsDiscovery = disc.OnStartup && !state.DiscoveryDone
                                   || (disc.RefreshIntervalMinutes > 0
                                       && DateTime.UtcNow - state.LastDiscovery
                                          > TimeSpan.FromMinutes(disc.RefreshIntervalMinutes));

                if (needsDiscovery)
                {
                    Interlocked.Increment(ref _busyCount);
                    Pulswerk.Core.Log.Info($"[BACnet] Discovering objects on {device.Name}…");
                    var all = DiscoverObjects(client, address, device.DeviceId.Value);
                    var resultDict = ApplyFilter(client, address, all, cfg.Filter ?? BacnetFilterConfig.Default, device.Id, device.DeviceId.Value, GetExtraDiscoveryProperties(), disc.ReadDelayMs);
                    var filtered = resultDict.Objects;

                    // Enrich objects with vendor-specific properties (override in subclass)
                    // Now uses the extra properties already fetched in the batch read
                    filtered = filtered.Select(obj => EnrichObjectInfo(client, address, obj, resultDict.ExtraProperties)).ToList();

                    // ── Resolve Trend Log associations ───────────────────────────
                    // Standard BACnet: TrendLog objects reference their monitored
                    // object via PROP_LOG_DEVICE_OBJECT_PROPERTY (132). We scan
                    // the full object list for TrendLogs and build a reverse map,
                    // then wire LogObjectId into each matching data object.
                    var trendLogMap = ResolveTrendLogMap(client, address, all);
                    if (trendLogMap.Count > 0)
                    {
                        Pulswerk.Core.Log.Info($"[BACnet] Found {trendLogMap.Count} Trend Log association(s).");
                        filtered = filtered.Select(obj =>
                            trendLogMap.TryGetValue(obj.ObjectId, out var logObjId) && obj.LogObjectId == null
                                ? obj with { LogObjectId = logObjId }
                                : obj
                        ).ToList();
                    }

                    state.CachedObjects = filtered;
                    state.LastDiscovery = DateTime.UtcNow;
                    state.DiscoveryDone = true;
                    state.AttributesSent = false;   // re-send attributes after rediscovery
                    state.HierarchyDirty = true;    // signal background provisioner
                    state.HierarchyReady = false;   // wait for background job to finish provisioning before alarms
                    _hierarchyLogged.Remove(device.Name);  // re-log hierarchy stats on next conversion

                    Pulswerk.Core.Log.Info($"[BACnet] {device.Name}: {all.Count} objects found, " +
                                      $"{filtered.Count} after filter" +
                                      (trendLogMap.Count > 0 ? $", {trendLogMap.Count} trend logs linked." : "."));

                    // Extension point for subclasses (e.g. DezikoDriver hierarchy walk)
                    OnPostDiscovery(client, address, state, device, cfg);
                    Interlocked.Decrement(ref _busyCount);

                    // ── Sync Trend Logs (Historical Backfill) ─────────────────────
                    // Only on first startup or after device recovery from offline.
                    // Periodic rediscovery does not re-sync trend logs.
                    bool shouldSyncTrends = !state.TrendLogsSynced || isRecovery;
                    if (dataStore != null && shouldSyncTrends)
                    {
                        state.TrendLogsSynced = true;
                        Interlocked.Increment(ref _busyCount);
                        var syncClient = client;
                        var syncAddress = address;
                        Pulswerk.Core.Log.Info($"[BACnet] Trend log sync triggered for {device.Name}" +
                            (isRecovery ? " (recovery after stale)" : " (initial startup)") + ".");
                        _ = Task.Run(() =>
                        {
                            try
                            {
                                SyncTrendLogsAsync(syncClient, syncAddress, state, dataStore, device);
                            }
                            catch (Exception ex)
                            {
                                Pulswerk.Core.Log.Error($"[BACnet] Trend log sync failed for {device.Name}: {ex.Message}");
                            }
                            finally
                            {
                                Interlocked.Decrement(ref _busyCount);
                            }
                        });
                    }
                }

                if (state.CachedObjects.Count == 0)
                    return new BacnetReadResult();

                // ── Read properties ───────────────────────────────────────────────

                var props = cfg.Properties ?? BacnetPropsConfig.Default;
                var telPropIds = ParsePropertyIds(props.EffectiveTelemetries);
                var attrPropIds = !state.AttributesSent
                                  ? ParsePropertyIds(props.EffectiveAttributes)
                                  : Array.Empty<BacnetPropertyIds>();

                var allPropIds = telPropIds.Concat(attrPropIds).Distinct().ToArray();

                // Inter-object read delay to avoid overwhelming the controller
                int readDelayMs = disc.ReadDelayMs;
                bool isFirstObject = true;

                foreach (var obj in state.CachedObjects)
                {
                    // Pace requests: sleep before each read (skip the first to avoid unnecessary delay)
                    if (!isFirstObject && readDelayMs > 0)
                        Thread.Sleep(readDelayMs);
                    isFirstObject = false;

                    var values = ReadObjectProperties(client, address, obj.ObjectId, allPropIds);

                    foreach (var propId in telPropIds)
                    {
                        string key = $"{obj.KeyPrefix}_{PropSuffix(propId)}";
                        if (values.TryGetValue(propId, out var raw))
                            result.TelemetryValues[key] = FormatValue(obj, propId, raw);
                        else if (propId == BacnetPropertyIds.PROP_PRESENT_VALUE)
                            result.TelemetryValues[key] = FormatValue(obj, propId, null);
                    }

                    // Attributes (only on first poll after discovery)
                    if (!state.AttributesSent)
                    {
                        foreach (var propId in attrPropIds)
                        {
                            if (values.TryGetValue(propId, out var raw))
                            {
                                string key = $"{obj.KeyPrefix}_{PropSuffix(propId)}";
                                result.Attributes[key] = FormatValue(obj, propId, raw).ToString() ?? "";
                            }
                        }
                    }

                    // Decode status flags bit-string into separate boolean TelemetryValues
                    if (allPropIds.Contains(BacnetPropertyIds.PROP_STATUS_FLAGS) && values.TryGetValue(BacnetPropertyIds.PROP_STATUS_FLAGS, out var stRaw) && stRaw is BacnetBitString stBs)
                    {
                        ExpandStatusFlags(result.TelemetryValues, obj.KeyPrefix + "_status", stBs);

                        // ── Diagnostic Alarms ─────────────────────────────────────
                        if (alarmStore != null)
                        {
                            HandleObjectAlarms(alarmStore, device, state, obj, stRaw, values, client, address);
                        }
                    }
                }

                if (!state.AttributesSent && attrPropIds.Length > 0)
                    state.AttributesSent = true;

                // Propagate the dirty flag once per discovery cycle
                if (state.HierarchyDirty)
                {
                    result.HierarchyDirty = true;
                    state.HierarchyDirty = false;
                }

            }
            catch (Exception ex) when (IsTransportError(ex))
            {
                // UDP socket died (network hiccup, interface restart, etc.).
                // Purge the cached client so the next poll creates a fresh one.
                Pulswerk.Core.Log.Error(
                    $"[BACnet] Transport error on {device.Name}: {ex.GetType().Name} – {ex.Message}. Resetting client.");
                InvalidateClient(conn);
                throw;  // re-throw so PollAndPublishAsync tracks the failure count
            }
            finally
            {
                if (!_clientsByConnection.Values.Contains(client))
                    client.Dispose();
            }

            return result;
        }

        /// <summary>
        /// Called after object discovery completes. Override in subclasses to add
        /// vendor-specific post-processing (e.g. hierarchy walking, extra metadata).
        /// </summary>
        protected virtual void OnPostDiscovery(
            BacnetClient client, BacnetAddress address,
            DiscoveryState state, DeviceConfig device, DeviceConfig cfg)
        {
            // If the device has an AssetType set, we trigger the hierarchy walker.
            // This allows standard BACnet devices to also build trees if requested.
            if (!string.IsNullOrEmpty(cfg.AssetType))
            {
                Pulswerk.Core.Log.Info($"[BACnet] Walking Structured Views on {device.Name} (assetType={cfg.AssetType})…");
                state.Tree = BacnetHierarchy.Walk(client, address, device.DeviceId!.Value);
                int leafCount = CountTreeLeaves(state.Tree);
                Pulswerk.Core.Log.Info($"[BACnet] Hierarchy walk complete — " +
                                  $"{state.Tree.Roots.Count} root(s), {leafCount} leaf node(s) " +
                                  $"(discovered={state.CachedObjects.Count}).");

                if (state.Tree.Roots.Count == 0)
                {
                    Pulswerk.Core.Log.Warning(
                        $"[BACnet] {device.Name} has {state.CachedObjects.Count} " +
                        $"discovered objects but 0 Structured View roots. " +
                        $"Items will appear under flat fallback hierarchy.");
                }
                else if (leafCount < state.CachedObjects.Count)
                {
                    int orphanEstimate = state.CachedObjects.Count - leafCount;
                    Pulswerk.Core.Log.Info(
                        $"[BACnet] {device.Name} has ~{orphanEstimate} object(s) " +
                        $"not covered by the Structured View tree. " +
                        $"These will appear under 'Uncategorized'.");
                }
            }
        }

        /// <summary>Counts the total non-view (leaf) nodes in a DezikoTree.</summary>
        private static int CountTreeLeaves(DezikoTree tree)
        {
            int count = 0;
            void Walk(DezikoNode n)
            {
                if (!n.IsView) { count++; return; }
                foreach (var c in n.Children) Walk(c);
            }
            foreach (var r in tree.Roots) Walk(r);
            return count;
        }

        /// <summary>
        /// Called for each discovered object to enrich it with vendor-specific
        /// properties (e.g. Deziko NamingPath, Category, TrendLogReference).
        /// Returns the enriched info record.
        /// </summary>
        protected virtual BacnetObjectInfo EnrichObjectInfo(
            BacnetClient client, BacnetAddress address,
            BacnetObjectInfo info)
        {
            return info; // Base: no enrichment
        }

        // =====================================================================
        //  Discovery – reads PROP_OBJECT_LIST from the DEVICE object
        // =====================================================================
        static List<BacnetObjectId> DiscoverObjects(
            BacnetClient client, BacnetAddress address, uint deviceId)
        {
            var deviceObjId = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, deviceId);
            var objectIds = new List<BacnetObjectId>();

            // 1. Try to read the full list at once (best performance)
            try
            {
                var prevTimeout = client.Timeout;
                client.Timeout = 15000; // higher timeout for bulk read
                try
                {
                    if (client.ReadPropertyRequest(address, deviceObjId, BacnetPropertyIds.PROP_OBJECT_LIST, out IList<BacnetValue> fullList))
                    {
                        foreach (var v in fullList)
                            if (v.Value is BacnetObjectId id) objectIds.Add(id);

                        if (objectIds.Count > 0)
                        {
                            Pulswerk.Core.Log.Info($"[BACnet] Successfully read {objectIds.Count} objects in bulk.");
                            return objectIds;
                        }
                    }
                }
                finally { client.Timeout = prevTimeout; }
            }
            catch { /* fallback to index read */ }

            // 2. Fallback: Read the count first (Index 0)
            uint count = 0;
            int retry = 0;
            while (retry++ < 3)
            {
                try
                {
                    if (client.ReadPropertyRequest(address, deviceObjId,
                            BacnetPropertyIds.PROP_OBJECT_LIST, out IList<BacnetValue> countVal,
                            arrayIndex: 0))
                    {
                        if (countVal != null && countVal.Count > 0)
                        {
                            count = Convert.ToUInt32(countVal[0].Value);
                            break;
                        }
                    }
                    if (retry < 3) Thread.Sleep(1000);
                }
                catch
                {
                    if (retry < 3) Thread.Sleep(1000);
                }
            }

            if (count == 0)
            {
                Pulswerk.Core.Log.Error($"[BACnet] Could not read object list from {address}. Aborting discovery.");
                return objectIds;
            }

            // 3. Read the list index-by-index (slow fallback)
            Pulswerk.Core.Log.Info($"[BACnet] Downloading object list index-by-index ({count} items)…");
            for (uint i = 1; i <= count; i++)
            {
                try
                {
                    if (client.ReadPropertyRequest(address, deviceObjId,
                            BacnetPropertyIds.PROP_OBJECT_LIST, out IList<BacnetValue> objVal,
                            arrayIndex: i))
                    {
                        foreach (var v in objVal)
                            if (v.Value is BacnetObjectId id) objectIds.Add(id);
                    }
                }
                catch { /* skip broken index */ }

                if (i % 100 == 0) Pulswerk.Core.Log.Debug($"[BACnet] ... discovered {i}/{count} objects");
            }

            return objectIds;
        }

        // =====================================================================
        //  Trend Log reverse-map: TrendLog → monitored object
        //
        //  Standard BACnet: Each TrendLog has PROP_LOG_DEVICE_OBJECT_PROPERTY
        //  (property 132) which is a DeviceObjectPropertyReference containing
        //  the ObjectId of the object it monitors.
        //
        //  We scan the full object list for OBJECT_TREND_LOG entries, read
        //  prop 132, and return a dict: { monitoredObjectId → trendLogObjectId }.
        // =====================================================================
        static Dictionary<BacnetObjectId, BacnetObjectId> ResolveTrendLogMap(
            BacnetClient client, BacnetAddress address, List<BacnetObjectId> allObjects)
        {
            var map = new Dictionary<BacnetObjectId, BacnetObjectId>();

            var trendLogs = allObjects.Where(o =>
                o.type == (BacnetObjectTypes)20 ||   // OBJECT_TREND_LOG
                o.type == (BacnetObjectTypes)27      // OBJECT_TREND_LOG_MULTIPLE
            ).ToList();

            if (trendLogs.Count == 0)
                return map;

            Pulswerk.Core.Log.Info($"[BACnet] Scanning {trendLogs.Count} Trend Log objects for associations...");

            foreach (var tl in trendLogs)
            {
                try
                {
                    // PROP_LOG_DEVICE_OBJECT_PROPERTY (132) returns a DeviceObjectPropertyReference
                    // which encodes: objectId, propertyId, and optionally deviceId
                    if (client.ReadPropertyRequest(address, tl,
                            BacnetPropertyIds.PROP_LOG_DEVICE_OBJECT_PROPERTY,
                            out IList<BacnetValue> vals) && vals.Count > 0)
                    {
                        // The value may be a BacnetDeviceObjectPropertyReference or encoded as
                        // an ObjectId directly depending on stack implementation
                        BacnetObjectId? monitoredId = null;

                        if (vals[0].Value is BacnetDeviceObjectPropertyReference devRef)
                        {
                            monitoredId = devRef.objectIdentifier;
                        }
                        else if (vals[0].Value is BacnetObjectId directId)
                        {
                            monitoredId = directId;
                        }
                        // Some stacks encode it as a list of values [objectId, propertyId, ...]
                        else if (vals.Count >= 2 && vals[0].Value is BacnetObjectId listId)
                        {
                            monitoredId = listId;
                        }

                        if (monitoredId.HasValue && !map.ContainsKey(monitoredId.Value))
                        {
                            map[monitoredId.Value] = tl;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Pulswerk.Core.Log.Debug($"[BACnet] Could not read trend log ref for {tl}: {ex.Message}");
                }
            }

            return map;
        }

        // =====================================================================
        //  Object-type alias table  (short name / numeric string → enum)
        // =====================================================================
        static readonly Dictionary<string, BacnetObjectTypes> _typeAliases =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["AI"] = BacnetObjectTypes.OBJECT_ANALOG_INPUT,
                ["AO"] = BacnetObjectTypes.OBJECT_ANALOG_OUTPUT,
                ["AV"] = BacnetObjectTypes.OBJECT_ANALOG_VALUE,
                ["BI"] = BacnetObjectTypes.OBJECT_BINARY_INPUT,
                ["BO"] = BacnetObjectTypes.OBJECT_BINARY_OUTPUT,
                ["BV"] = BacnetObjectTypes.OBJECT_BINARY_VALUE,
                ["MI"] = BacnetObjectTypes.OBJECT_MULTI_STATE_INPUT,
                ["MO"] = BacnetObjectTypes.OBJECT_MULTI_STATE_OUTPUT,
                ["MV"] = BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE,
                ["IV"] = BacnetObjectTypes.OBJECT_INTEGER_VALUE,
                ["SV"] = BacnetObjectTypes.OBJECT_STRUCTURED_VIEW,
            };

        /// <summary>
        /// Resolves one entry from the objectTypes allowlist.
        /// Accepts (in order):
        ///   1. Short alias     e.g. "AI", "BO"
        ///   2. Full enum name  e.g. "OBJECT_ANALOG_INPUT"
        ///   3. Numeric type ID e.g. "0", "128"
        /// Returns null and logs a warning when the entry cannot be resolved.
        /// </summary>
        static BacnetObjectTypes? ResolveObjectType(string entry)
        {
            // 1. Short alias
            if (_typeAliases.TryGetValue(entry, out var aliased))
                return aliased;

            // 2. Full enum name (case-insensitive)
            if (Enum.TryParse<BacnetObjectTypes>(entry, ignoreCase: true, out var named))
                return named;

            // 3. Numeric ID
            if (uint.TryParse(entry, out uint numericId))
                return (BacnetObjectTypes)numericId;

            Pulswerk.Core.Log.Warning($"[BACnet] Cannot resolve objectType filter entry: '{entry}'");
            return null;
        }

        // =====================================================================
        //  Filter – reads properties, applies rules, caches results
        // =====================================================================
        protected static DiscoveryResult ApplyFilter(
            BacnetClient client, BacnetAddress address,
            List<BacnetObjectId> candidates, BacnetFilterConfig filter,
            string techDeviceId, uint deviceInstanceId,
            List<BacnetPropertyIds>? extraProps = null,
            int readDelayMs = 0)
        {
            extraProps ??= new List<BacnetPropertyIds>();
            if (!extraProps.Contains((BacnetPropertyIds)108)) extraProps.Add((BacnetPropertyIds)108);
            if (!extraProps.Contains((BacnetPropertyIds)109)) extraProps.Add((BacnetPropertyIds)109);

            var extraResults = new Dictionary<BacnetObjectId, Dictionary<BacnetPropertyIds, List<BacnetValue>>>();
            // Build type allowlist (null = accept all)
            HashSet<BacnetObjectTypes>? allowedTypes = null;
            if (filter.ObjectTypes is { Count: > 0 })
            {
                allowedTypes = new();
                foreach (var entry in filter.ObjectTypes)
                {
                    var t = ResolveObjectType(entry);
                    if (t.HasValue) allowedTypes.Add(t.Value);
                }
            }

            // Pre-compile regex objects (null = not active)
            Regex? includeNameRx = filter.NamePattern is not null ? new Regex(filter.NamePattern, RegexOptions.IgnoreCase) : null;
            Regex? excludeNameRx = filter.ExcludeNamePattern is not null ? new Regex(filter.ExcludeNamePattern, RegexOptions.IgnoreCase) : null;
            Regex? includeDescRx = filter.DescriptionPattern is not null ? new Regex(filter.DescriptionPattern, RegexOptions.IgnoreCase) : null;

            int cap = (filter.MaxObjects is > 0) ? filter.MaxObjects.Value : int.MaxValue;

            var allNames = new Dictionary<BacnetObjectId, string>();
            var allDescs = new Dictionary<BacnetObjectId, string>();
            var allUnits = new Dictionary<BacnetObjectId, string>();
            var allCommandable = new HashSet<BacnetObjectId>();
            var allReadOnly = new HashSet<BacnetObjectId>();
            var allStateText = new Dictionary<BacnetObjectId, List<string>>();
            var allNumStates = new Dictionary<BacnetObjectId, uint>();

            var candidatesList = candidates
                .Where(oid => allowedTypes == null || allowedTypes.Contains(oid.type))
                .Where(oid => filter.InstanceRange == null || (oid.instance >= filter.InstanceRange.Min && oid.instance <= filter.InstanceRange.Max))
                .ToList();

            Pulswerk.Core.Log.Info($"[BACnet] Fetching properties for {candidatesList.Count} potential objects in batches...");
            bool rpmSegFault = false;   // set once → skip batch RPM from then on
            for (int i = 0; i < candidatesList.Count; i += 50)
            {
                var batch = candidatesList.Skip(i).Take(50).ToList();

                // Pace between batches (skip first batch)
                if (i > 0 && readDelayMs > 0)
                    Thread.Sleep(readDelayMs);

                // ── Attempt batch RPM (50 objects at a time) ──────────────────
                if (!rpmSegFault)
                {
                    try
                    {
                        var readSpecs = batch.Select(oid =>
                        {
                            var props = new List<BacnetPropertyReference> {
                                new BacnetPropertyReference((uint)BacnetPropertyIds.PROP_OBJECT_NAME, uint.MaxValue),
                                new BacnetPropertyReference((uint)BacnetPropertyIds.PROP_DESCRIPTION, uint.MaxValue),
                                new BacnetPropertyReference((uint)BacnetPropertyIds.PROP_UNITS, uint.MaxValue),
                                new BacnetPropertyReference((uint)BacnetPropertyIds.PROP_PRIORITY_ARRAY, uint.MaxValue),
                                new BacnetPropertyReference((uint)BacnetPropertyIds.PROP_READ_ONLY, uint.MaxValue),
                                new BacnetPropertyReference((uint)BacnetPropertyIds.PROP_STATE_TEXT, uint.MaxValue),
                                new BacnetPropertyReference((uint)BacnetPropertyIds.PROP_NUMBER_OF_STATES, uint.MaxValue)
                            };
                            if (extraProps != null)
                            {
                                foreach (var ep in extraProps)
                                    props.Add(new BacnetPropertyReference((uint)ep, uint.MaxValue));
                            }
                            return new BacnetReadAccessSpecification(oid, props);
                        }).ToList();

                        if (client.ReadPropertyMultipleRequest(address, readSpecs, out IList<BacnetReadAccessResult> batchResults))
                        {
                            ProcessRpmResults(batchResults, allNames, allDescs, allUnits, allCommandable, allReadOnly, allStateText, allNumStates, extraProps, extraResults);
                            if (i > 0 && i % 250 == 0) Pulswerk.Core.Log.Debug($"[BACnet] ... {i}/{candidatesList.Count} objects fetched");
                            continue;  // batch succeeded — next batch
                        }
                    }
                    catch (Exception ex) when (ex.Message.Contains("SEGMENTATION"))
                    {
                        rpmSegFault = true;
                        Pulswerk.Core.Log.Warning($"[BACnet] Device does not support segmentation — switching to single-object reads.");
                    }
                    catch { /* other RPM failure — fall through to single-object reads */ }
                }

                // ── Fallback: single-object RPM or individual reads ───────────
                foreach (var oid in batch)
                {
                    // Pace single-object reads to avoid overwhelming the controller
                    if (readDelayMs > 0)
                        Thread.Sleep(readDelayMs);

                    try
                    {
                        // Try RPM for a single object first (most efficient fallback)
                        var singleSpec = new List<BacnetReadAccessSpecification> {
                            new BacnetReadAccessSpecification(oid, new List<BacnetPropertyReference> {
                                new BacnetPropertyReference((uint)BacnetPropertyIds.PROP_OBJECT_NAME, uint.MaxValue),
                                new BacnetPropertyReference((uint)BacnetPropertyIds.PROP_DESCRIPTION, uint.MaxValue),
                                new BacnetPropertyReference((uint)BacnetPropertyIds.PROP_UNITS, uint.MaxValue),
                                new BacnetPropertyReference((uint)BacnetPropertyIds.PROP_PRIORITY_ARRAY, uint.MaxValue),
                                new BacnetPropertyReference((uint)BacnetPropertyIds.PROP_READ_ONLY, uint.MaxValue),
                                new BacnetPropertyReference((uint)BacnetPropertyIds.PROP_STATE_TEXT, uint.MaxValue),
                                new BacnetPropertyReference((uint)BacnetPropertyIds.PROP_NUMBER_OF_STATES, uint.MaxValue)
                            })
                        };
                        if (extraProps != null)
                        {
                            foreach (var ep in extraProps)
                                singleSpec[0].propertyReferences.Add(new BacnetPropertyReference((uint)ep, uint.MaxValue));
                        }

                        if (client.ReadPropertyMultipleRequest(address, singleSpec, out IList<BacnetReadAccessResult> singleResults))
                        {
                            ProcessRpmResults(singleResults, allNames, allDescs, allUnits, allCommandable, allReadOnly, allStateText, allNumStates, extraProps, extraResults);
                            continue;
                        }
                    }
                    catch { /* single RPM also failed — fall through to individual reads */ }

                    // Ultimate fallback: individual ReadPropertyRequest calls
                    ReadSingleProps(client, address, oid, allNames, allDescs, allUnits, allCommandable, allReadOnly, allStateText, allNumStates, extraProps, extraResults);
                }
                if (i > 0 && i % 250 == 0) Pulswerk.Core.Log.Debug($"[BACnet] ... {i}/{candidatesList.Count} objects fetched (single-object mode)");
            }

            var result = new List<BacnetObjectInfo>();
            int objectsWithLabels = 0;

            foreach (var oid in candidatesList)
            {
                if (result.Count >= cap) break;

                if (!allNames.TryGetValue(oid, out string? objectName)) objectName = oid.ToString();
                if (includeNameRx != null && !includeNameRx.IsMatch(objectName)) continue;
                if (excludeNameRx != null && excludeNameRx.IsMatch(objectName)) continue;

                if (!allDescs.TryGetValue(oid, out string? description)) description = "";
                if (includeDescRx != null && !includeDescRx.IsMatch(description)) continue;

                allUnits.TryGetValue(oid, out string? units);
                bool commandable = allCommandable.Contains(oid);
                bool isConfigValue = IsConfigValueType(oid.type);
                bool isReadOnly = allReadOnly.Contains(oid);
                bool writeable = isConfigValue && !commandable && !isReadOnly;
                allStateText.TryGetValue(oid, out List<string>? stateText);
                allNumStates.TryGetValue(oid, out uint numStates);

                // Fallback for binary state text if not in batch
                if (stateText == null && IsBinary(oid.type))
                {
                    string inactive = ReadStringProp(client, address, oid, BacnetPropertyIds.PROP_INACTIVE_TEXT);
                    string active = ReadStringProp(client, address, oid, BacnetPropertyIds.PROP_ACTIVE_TEXT);
                    if (!string.IsNullOrEmpty(inactive) || !string.IsNullOrEmpty(active))
                        stateText = new List<string> { string.IsNullOrEmpty(inactive) ? "0" : inactive, string.IsNullOrEmpty(active) ? "1" : active };
                }

                if (stateText == null && IsMultiState(oid.type) && numStates > 0)
                {
                    stateText = new List<string>();
                    for (int n = 1; n <= numStates; n++)
                        stateText.Add($"State {n}");
                }

                if (stateText != null && stateText.Count > 0) objectsWithLabels++;

                double? resolution = null;
                double? covIncrement = null;
                if (extraResults.TryGetValue(oid, out var exProps))
                {
                    if (exProps.TryGetValue((BacnetPropertyIds)108, out var resVals) && resVals.Count > 0 && resVals[0].Value != null)
                    {
                        if (BacnetValueConverter.TryToDouble(resVals[0].Value, out double r)) resolution = r;
                    }
                    if (exProps.TryGetValue((BacnetPropertyIds)109, out var covVals) && covVals.Count > 0 && covVals[0].Value != null)
                    {
                        if (BacnetValueConverter.TryToDouble(covVals[0].Value, out double c)) covIncrement = c;
                    }
                }

                result.Add(new BacnetObjectInfo(
                    TechDeviceId: techDeviceId,
                    DeviceId: deviceInstanceId,
                    ObjectId: oid,
                    ObjectName: objectName,
                    NamingPath: new List<string>(),
                    Description: description ?? "",
                    Units: units ?? "",
                    Commandable: commandable,
                    Writeable: writeable,
                    StateText: stateText,
                    Resolution: resolution,
                    CovIncrement: covIncrement
                ));
            }

            if (objectsWithLabels > 0)
                Pulswerk.Core.Log.Info($"[BACnet] Loaded state labels for {objectsWithLabels} objects.");

            return new DiscoveryResult(result, extraResults);
        }

        /// <summary>
        /// Processes the results of a ReadPropertyMultiple response into the lookup dictionaries.
        /// Shared between batch RPM and single-object RPM paths.
        /// </summary>
        private static void ProcessRpmResults(
            IList<BacnetReadAccessResult> results,
            Dictionary<BacnetObjectId, string> allNames,
            Dictionary<BacnetObjectId, string> allDescs,
            Dictionary<BacnetObjectId, string> allUnits,
            HashSet<BacnetObjectId> allCommandable,
            HashSet<BacnetObjectId> allReadOnly,
            Dictionary<BacnetObjectId, List<string>> allStateText,
            Dictionary<BacnetObjectId, uint> allNumStates,
            List<BacnetPropertyIds>? extraProps,
            Dictionary<BacnetObjectId, Dictionary<BacnetPropertyIds, List<BacnetValue>>> extraResults)
        {
            foreach (var res in results)
            {
                var oid = res.objectIdentifier;
                if (res.values == null) continue;

                foreach (var pv in res.values)
                {
                    if (pv.value == null || pv.value.Count == 0) continue;
                    var propId = (BacnetPropertyIds)pv.property.propertyIdentifier;

                    if (propId == BacnetPropertyIds.PROP_OBJECT_NAME)
                        allNames[oid] = pv.value[0].Value?.ToString() ?? "";
                    else if (propId == BacnetPropertyIds.PROP_DESCRIPTION)
                        allDescs[oid] = pv.value[0].Value?.ToString() ?? "";
                    else if (propId == BacnetPropertyIds.PROP_UNITS)
                        allUnits[oid] = UnitMapper.Format(pv.value[0].Value);
                    else if (propId == BacnetPropertyIds.PROP_PRIORITY_ARRAY)
                        allCommandable.Add(oid);
                    else if (propId == BacnetPropertyIds.PROP_READ_ONLY)
                    {
                        if (pv.value[0].Value is bool ro && ro) allReadOnly.Add(oid);
                        else if (pv.value[0].Value is uint rou && rou != 0) allReadOnly.Add(oid);
                    }
                    else if (propId == BacnetPropertyIds.PROP_STATE_TEXT)
                    {
                        var list = new List<string>();
                        foreach (var v in pv.value)
                        {
                            // Skip error responses that sneak into state text lists
                            if (v.Tag == BacnetApplicationTags.BACNET_APPLICATION_TAG_ERROR) continue;
                            var s = v.Value?.ToString() ?? "";
                            if (s.Contains("ERROR_")) continue;
                            list.Add(s);
                        }
                        if (list.Count > 0) allStateText[oid] = list;
                    }
                    else if (propId == BacnetPropertyIds.PROP_NUMBER_OF_STATES)
                    {
                        if (pv.value[0].Value is uint n) allNumStates[oid] = n;
                        else if (BacnetValueConverter.TryToDouble(pv.value[0].Value, out double d)) allNumStates[oid] = (uint)d;
                    }
                    else if (extraProps != null && extraProps.Contains(propId))
                    {
                        if (!extraResults.TryGetValue(oid, out var dict))
                            extraResults[oid] = dict = new();
                        dict[propId] = pv.value.ToList();
                    }
                }
            }
        }

        /// <summary>
        /// Ultimate fallback: reads core properties one-by-one via individual ReadPropertyRequest calls.
        /// Used when both batch and single-object RPM fail (e.g. device doesn't support RPM at all).
        /// </summary>
        private static void ReadSingleProps(
            BacnetClient client, BacnetAddress address, BacnetObjectId oid,
            Dictionary<BacnetObjectId, string> allNames,
            Dictionary<BacnetObjectId, string> allDescs,
            Dictionary<BacnetObjectId, string> allUnits,
            HashSet<BacnetObjectId> allCommandable,
            HashSet<BacnetObjectId> allReadOnly,
            Dictionary<BacnetObjectId, List<string>> allStateText,
            Dictionary<BacnetObjectId, uint> allNumStates,
            List<BacnetPropertyIds>? extraProps,
            Dictionary<BacnetObjectId, Dictionary<BacnetPropertyIds, List<BacnetValue>>> extraResults)
        {
            // PROP_OBJECT_NAME
            try
            {
                if (client.ReadPropertyRequest(address, oid, BacnetPropertyIds.PROP_OBJECT_NAME, out var vals) && vals.Count > 0)
                    allNames[oid] = vals[0].Value?.ToString() ?? "";
            }
            catch { }

            // PROP_DESCRIPTION
            try
            {
                if (client.ReadPropertyRequest(address, oid, BacnetPropertyIds.PROP_DESCRIPTION, out var vals) && vals.Count > 0)
                    allDescs[oid] = vals[0].Value?.ToString() ?? "";
            }
            catch { }

            // PROP_UNITS
            try
            {
                if (client.ReadPropertyRequest(address, oid, BacnetPropertyIds.PROP_UNITS, out var vals) && vals.Count > 0)
                    allUnits[oid] = UnitMapper.Format(vals[0].Value);
            }
            catch { }

            // PROP_PRIORITY_ARRAY (check for commandable)
            try
            {
                if (client.ReadPropertyRequest(address, oid, BacnetPropertyIds.PROP_PRIORITY_ARRAY, out _))
                    allCommandable.Add(oid);
            }
            catch { }

            // PROP_READ_ONLY
            try
            {
                if (client.ReadPropertyRequest(address, oid, BacnetPropertyIds.PROP_READ_ONLY, out var vals) && vals.Count > 0)
                {
                    if (vals[0].Value is bool ro && ro) allReadOnly.Add(oid);
                    else if (vals[0].Value is uint rou && rou != 0) allReadOnly.Add(oid);
                }
            }
            catch { }

            // PROP_STATE_TEXT
            try
            {
                if (client.ReadPropertyRequest(address, oid, BacnetPropertyIds.PROP_STATE_TEXT, out var vals) && vals.Count > 0)
                {
                    var list = new List<string>();
                    foreach (var v in vals)
                    {
                        if (v.Tag == BacnetApplicationTags.BACNET_APPLICATION_TAG_ERROR) continue;
                        var s = v.Value?.ToString() ?? "";
                        if (s.Contains("ERROR_")) continue;
                        list.Add(s);
                    }
                    if (list.Count > 0) allStateText[oid] = list;
                }
            }
            catch { }

            // PROP_NUMBER_OF_STATES
            try
            {
                if (client.ReadPropertyRequest(address, oid, BacnetPropertyIds.PROP_NUMBER_OF_STATES, out var vals) && vals.Count > 0)
                {
                    if (vals[0].Value is uint n) allNumStates[oid] = n;
                    else if (BacnetValueConverter.TryToDouble(vals[0].Value, out double d)) allNumStates[oid] = (uint)d;
                }
            }
            catch { }

            // Extra properties (vendor-specific)
            if (extraProps != null)
            {
                foreach (var ep in extraProps)
                {
                    try
                    {
                        if (client.ReadPropertyRequest(address, oid, ep, out var vals) && vals.Count > 0)
                        {
                            if (!extraResults.TryGetValue(oid, out var dict))
                                extraResults[oid] = dict = new();
                            dict[ep] = vals.ToList();
                        }
                    }
                    catch { }
                }
            }
        }

        protected virtual List<BacnetPropertyIds> GetExtraDiscoveryProperties() => new();

        protected virtual BacnetObjectInfo EnrichObjectInfo(
            BacnetClient client, BacnetAddress address,
            BacnetObjectInfo info,
            Dictionary<BacnetObjectId, Dictionary<BacnetPropertyIds, List<BacnetValue>>> extraProps)
        {
            return info;
        }

        static bool IsAnalog(BacnetObjectTypes t) => t is
            BacnetObjectTypes.OBJECT_ANALOG_INPUT or
            BacnetObjectTypes.OBJECT_ANALOG_OUTPUT or
            BacnetObjectTypes.OBJECT_ANALOG_VALUE or
            BacnetObjectTypes.OBJECT_SCHEDULE;

        static bool IsMultiState(BacnetObjectTypes t) => t is
            BacnetObjectTypes.OBJECT_MULTI_STATE_INPUT or
            BacnetObjectTypes.OBJECT_MULTI_STATE_OUTPUT or
            BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE;

        static bool IsBinary(BacnetObjectTypes t) =>
            BacnetValueConverter.IsBinary(t);

        public static string ReadStringProp(BacnetClient client, BacnetAddress address, BacnetObjectId oid, BacnetPropertyIds propId)
        {
            try
            {
                if (client.ReadPropertyRequest(address, oid, propId, out IList<BacnetValue> vals) && vals.Count > 0)
                    return vals[0].Value?.ToString() ?? "";
            }
            catch { /* ignore */ }
            return "";
        }

        public static List<string> ReadStringListProp(BacnetClient client, BacnetAddress address, BacnetObjectId oid, BacnetPropertyIds propId)
        {
            var result = new List<string>();
            try
            {
                if (client.ReadPropertyRequest(address, oid, propId, out IList<BacnetValue> vals))
                {
                    foreach (var v in vals)
                    {
                        string? s = v.Value?.ToString();
                        if (!string.IsNullOrEmpty(s)) result.Add(s);
                    }
                }
            }
            catch { /* ignore */ }
            return result;
        }

        protected static bool ReadIntProp(BacnetClient client, BacnetAddress address, BacnetObjectId oid, BacnetPropertyIds propId, out int result)
        {
            result = -1;
            try
            {
                if (client.ReadPropertyRequest(address, oid, propId, out IList<BacnetValue> vals) && vals.Count > 0)
                {
                    result = Convert.ToInt32(vals[0].Value);
                    return true;
                }
            }
            catch { /* ignore */ }
            return false;
        }

        protected static bool ReadDoubleProp(BacnetClient client, BacnetAddress address, BacnetObjectId oid, BacnetPropertyIds propId, out double? result)
        {
            result = null;
            try
            {
                if (client.ReadPropertyRequest(address, oid, propId, out IList<BacnetValue> vals) && vals.Count > 0)
                {
                    if (TryToDouble(vals[0].Value, out double d))
                    {
                        result = d;
                        return true;
                    }
                }
            }
            catch { /* ignore */ }
            return false;
        }

        protected static bool ReadUintProp(BacnetClient client, BacnetAddress address, BacnetObjectId oid, BacnetPropertyIds propId, out uint? result)
        {
            result = null;
            try
            {
                if (client.ReadPropertyRequest(address, oid, propId, out IList<BacnetValue> vals) && vals.Count > 0)
                {
                    result = Convert.ToUInt32(vals[0].Value);
                    return true;
                }
            }
            catch { /* ignore */ }
            return false;
        }

        protected static bool ReadObjectIdProp(BacnetClient client, BacnetAddress address, BacnetObjectId oid, BacnetPropertyIds propId, out BacnetObjectId? result)
        {
            result = null;
            try
            {
                if (client.ReadPropertyRequest(address, oid, propId, out IList<BacnetValue> vals) && vals.Count > 0)
                {
                    if (vals[0].Value is BacnetObjectId rid)
                    {
                        result = rid;
                        return true;
                    }
                }
            }
            catch { /* ignore */ }
            return false;
        }

        /// <summary>
        /// Returns true for BACnet Value object types (AV, BV, MV) which are
        /// used as config values or setpoints.
        /// OUTPUT types and Schedules are excluded as they are not config values.
        /// </summary>
        static bool IsConfigValueType(BacnetObjectTypes t) => t is
            BacnetObjectTypes.OBJECT_ANALOG_VALUE or
            BacnetObjectTypes.OBJECT_BINARY_VALUE or
            BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE;


        static string ReadObjectName(BacnetClient client, BacnetAddress address, BacnetObjectId oid)
        {
            try
            {
                if (client.ReadPropertyRequest(address, oid,
                        BacnetPropertyIds.PROP_OBJECT_NAME, out IList<BacnetValue> vals)
                    && vals.Count > 0)
                    return vals[0].Value?.ToString() ?? oid.ToString();
            }
            catch { /* fall through */ }
            return oid.ToString();
        }

        static string ReadDescription(BacnetClient client, BacnetAddress address, BacnetObjectId oid)
        {
            try
            {
                if (client.ReadPropertyRequest(address, oid,
                        BacnetPropertyIds.PROP_DESCRIPTION, out IList<BacnetValue> vals)
                    && vals.Count > 0)
                    return vals[0].Value?.ToString() ?? "";
            }
            catch { /* fall through */ }
            return "";
        }

        // =====================================================================
        //  Read multiple properties from one object
        // =====================================================================
        // --- Public helpers for live lookups ---
        public static Dictionary<BacnetPropertyIds, object?> ReadObjectProperties(
            BacnetClient client, BacnetAddress address,
            BacnetObjectId oid, BacnetPropertyIds[] propIds)
        {
            var result = new Dictionary<BacnetPropertyIds, object?>();

            // Build a ReadPropertyMultiple request for all props at once
            var propRefs = propIds
                .Select(p => new BacnetPropertyReference((uint)p, uint.MaxValue))
                .ToList();

            var readReq = new List<BacnetReadAccessSpecification>
            {
                new BacnetReadAccessSpecification(oid, propRefs)
            };

            if (client.ReadPropertyMultipleRequest(address, readReq, out IList<BacnetReadAccessResult> results))
            {
                foreach (var res in results)
                    foreach (var pv in res.values)
                    {
                        var propId = (BacnetPropertyIds)pv.property.propertyIdentifier;
                        if (pv.value?.Count > 0)
                        {
                            // Skip if any value in the list is a BACnet error
                            bool hasError = false;
                            foreach (var bv in pv.value)
                            {
                                if (bv.Tag == BacnetApplicationTags.BACNET_APPLICATION_TAG_ERROR)
                                { hasError = true; break; }
                                var s = bv.Value?.ToString();
                                if (s != null && s.Contains("ERROR_"))
                                { hasError = true; break; }
                            }

                            if (hasError) continue;

                            // For complex properties (Schedule/Calendar), keep the full list.
                            // For simple properties, take the first value.
                            object? val = (pv.value.Count == 1) ? pv.value[0].Value : pv.value;
                            result[propId] = val;
                        }
                    }
                return result;
            }

            // Fallback: ReadPropertyMultiple not supported – read one by one
            foreach (var propId in propIds)
            {
                try
                {
                    if (client.ReadPropertyRequest(address, oid, propId, out IList<BacnetValue> vals)
                        && vals.Count > 0)
                    {
                        // Skip error responses (same filter as RPM path)
                        bool hasError = vals.Any(v =>
                            v.Tag == BacnetApplicationTags.BACNET_APPLICATION_TAG_ERROR ||
                            (v.Value?.ToString()?.Contains("ERROR_") == true));
                        if (hasError) continue;

                        result[propId] = (vals.Count == 1) ? vals[0].Value : vals;
                    }
                }
                catch { /* property not available on this object */ }
            }

            return result;
        }

        // =====================================================================
        //  Address resolution
        //
        //  When host+port are already known we can construct the BacnetAddress
        //  directly – no need for a broadcast Who-Is.
        //  We still send a local-broadcast Who-Is so the device's I-Am can
        //  confirm it is alive; if no I-Am arrives within the timeout we use
        //  the address we built from config (reliable for unicast IP devices).
        // =====================================================================
        public static BacnetAddress ResolveAddress(
            BacnetClient client, string host, int port, uint deviceId, int timeoutMs)
        {
            // BacnetAddress requires a dotted-decimal IP – resolve hostname if needed
            // (e.g. "bacnet-sim" on the Docker bridge resolves via Docker DNS).
            string ip = host;
            if (!string.IsNullOrEmpty(host) && !System.Net.IPAddress.TryParse(host, out _))
            {
                try
                {
                    var addrs = System.Net.Dns.GetHostAddresses(host);
                    var v4 = addrs.FirstOrDefault(
                        a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    if (v4 != null) { ip = v4.ToString(); Pulswerk.Core.Log.Info($"[BACnet] Resolved '{host}' to {ip}"); }
                }
                catch (Exception ex)
                {
                    Pulswerk.Core.Log.Error(
                        $"[BACnet] DNS resolution failed for '{host}': {ex.Message}");
                }
            }

            // Ensure we have a numeric IP for BacnetAddress
            if (string.IsNullOrEmpty(ip) || !System.Net.IPAddress.TryParse(ip, out _))
            {
                ip = "127.0.0.1";
                Pulswerk.Core.Log.Warning($"[BACnet] Using loopback fallback for '{host}'");
            }

            // Build the direct address from config (works for unicast BACnet/IP)
            byte[] ipBytes = System.Net.IPAddress.Parse(ip).GetAddressBytes();
            byte[] adr = new byte[6];
            Array.Copy(ipBytes, adr, 4);
            adr[4] = (byte)((port >> 8) & 0xFF);
            adr[5] = (byte)(port & 0xFF);

            var directAddress = new BacnetAddress(BacnetAddressTypes.IP, 0, adr);
            Pulswerk.Core.Log.Info($"[BACnet] Attempting Who-Is for device {deviceId} via {ip}:{port}...");

            BacnetAddress? iamAddress = null;
            using var signal = new ManualResetEventSlim(false);

            void OnIam(BacnetClient _, BacnetAddress adr, uint id,
                       uint maxApdu, BacnetSegmentations seg, ushort vendor)
            {
                if (id == deviceId)
                {
                    iamAddress = adr;
                    Pulswerk.Core.Log.Debug($"[BACnet] Got I-Am from {adr} (Net={adr.net}, Adr={BitConverter.ToString(adr.adr)})");
                    signal.Set();
                }
            }

            client.OnIam += OnIam;
            try
            {
                // Range-based Who-Is
                client.WhoIs((int)deviceId, (int)deviceId);
                signal.Wait(timeoutMs);
            }
            finally
            {
                client.OnIam -= OnIam;
            }

            if (iamAddress != null) Pulswerk.Core.Log.Info($"[BACnet] Received I-Am from {iamAddress} for device {deviceId}.");
            else Pulswerk.Core.Log.Warning($"[BACnet] Who-Is timeout for device {deviceId}. Falling back to {directAddress}.");

            // For BACnet/IP, we MUST ensure the address has the correct port.
            // If we got an I-Am, use it. Otherwise use our manually built directAddress.
            return iamAddress ?? directAddress;
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        /// <summary>
        /// Returns an existing client for the given connection if available.
        /// Checks by Connection ID and then by Local Endpoint (IP/Port).
        /// </summary>
        public static BacnetClient? GetSharedClient(ConnectionConfig conn)
        {
            if (_clientsByConnection.TryGetValue(conn.Id, out var shared)) return shared;

            var bindAddr = string.IsNullOrWhiteSpace(conn.LocalAddress) ? "0.0.0.0" : conn.LocalAddress;
            var bindPort = conn.LocalPort ?? 0;
            if (bindPort > 0 && _clientsByEndpoint.TryGetValue((bindAddr, bindPort), out var endpointShared))
                return endpointShared;

            return null;
        }

        protected virtual BacnetClient OpenClient(ConnectionConfig conn)
        {
            // 1. Try by Connection ID
            if (_clientsByConnection.TryGetValue(conn.Id, out var shared)) return shared;

            // 2. Try by Endpoint (Address + Port)
            var bindAddr = conn.LocalAddress ?? "0.0.0.0";
            var bindPort = conn.LocalPort ?? 0;
            if (bindPort > 0 && _clientsByEndpoint.TryGetValue((bindAddr, bindPort), out var endpointShared))
            {
                // Register this connection ID for the existing client
                _clientsByConnection[conn.Id] = endpointShared;
                return endpointShared;
            }

            // 3. Create new
            var transport = new BacnetIpUdpProtocolTransport(bindPort, false, false, 1472, bindAddr);
            var client = new BacnetClient(transport);
            client.Start();

            // Register both ways
            _clientsByConnection[conn.Id] = client;
            if (bindPort > 0) _clientsByEndpoint[(bindAddr, bindPort)] = client;

            return client;
        }

        /// <summary>Dispose and remove a cached BACnet client for a connection,
        /// forcing the next poll to create a fresh UDP socket.</summary>
        protected void InvalidateClient(ConnectionConfig conn)
        {
            if (_clientsByConnection.TryRemove(conn.Id, out var client))
            {
                // Also remove from endpoint cache
                var bindAddr = conn.LocalAddress ?? "0.0.0.0";
                var bindPort = conn.LocalPort ?? 0;
                if (bindPort > 0)
                    _clientsByEndpoint.TryRemove((bindAddr, bindPort), out _);

                try { client.Dispose(); } catch { /* best-effort */ }
                Pulswerk.Core.Log.Info($"[BACnet] Client for connection '{conn.Id}' invalidated and disposed.");
            }
        }

        /// <summary>Checks whether an exception indicates a transport-level failure
        /// (dead UDP socket, network unreachable, I/O error).</summary>
        static bool IsTransportError(Exception ex)
        {
            if (ex is System.Net.Sockets.SocketException) return true;
            if (ex is System.IO.IOException) return true;
            if (ex is ObjectDisposedException) return true;
            // BACnet library wraps some transport failures as generic Exception
            // with socket-related inner exceptions
            if (ex.InnerException is System.Net.Sockets.SocketException) return true;
            return false;
        }

        DiscoveryState GetOrCreateState(string deviceName)
        {
            lock (_stateLock)
            {
                if (!_stateByDevice.TryGetValue(deviceName, out var s))
                {
                    s = new DiscoveryState();
                    _stateByDevice[deviceName] = s;
                }
                return s;
            }
        }

        static BacnetPropertyIds[] ParsePropertyIds(IEnumerable<string> names) =>
            names
                .Select(n => Enum.TryParse<BacnetPropertyIds>(n, true, out var id) ? (BacnetPropertyIds?)id : null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToArray();

        static string PropSuffix(BacnetPropertyIds p) => p switch
        {
            BacnetPropertyIds.PROP_PRESENT_VALUE => "value",
            BacnetPropertyIds.PROP_OBJECT_NAME => "name",
            BacnetPropertyIds.PROP_DESCRIPTION => "desc",
            BacnetPropertyIds.PROP_UNITS => "units",
            BacnetPropertyIds.PROP_STATUS_FLAGS => "status",
            BacnetPropertyIds.PROP_OUT_OF_SERVICE => "oos",
            BacnetPropertyIds.PROP_RELIABILITY => "rel",
            BacnetPropertyIds.PROP_EVENT_STATE => "evt",
            (BacnetPropertyIds)4311 => "subst_value",
            (BacnetPropertyIds)4312 => "subst_active",
            (BacnetPropertyIds)4340 => "last_change",
            (BacnetPropertyIds)5092 => "io_binding",
            (BacnetPropertyIds)5094 => "asset_id",
            (BacnetPropertyIds)5103 => "comm_status",
            _ => p.ToString().Replace("PROP_", "").ToLower(),
        };

        private void ExpandStatusFlags(TelemetryValues tel, string baseKey, BacnetBitString bs)
        {
            // Bit 0: in-alarm, 1: fault, 2: overridden, 3: out-of-service
            tel[$"{baseKey}_alarm"] = bs.GetBit(0) ? 1.0 : 0.0;
            tel[$"{baseKey}_fault"] = bs.GetBit(1) ? 1.0 : 0.0;
            tel[$"{baseKey}_overridden"] = bs.GetBit(2) ? 1.0 : 0.0;
            tel[$"{baseKey}_oos"] = bs.GetBit(3) ? 1.0 : 0.0;
        }

        private string GetFriendlyName(BacnetObjectInfo obj)
        {
            if (!string.IsNullOrWhiteSpace(obj.NameExtension)) return obj.NameExtension;
            if (obj.NamingPath.Count > 0) return obj.NamingPath.Last();
            return obj.ObjectName;
        }

        private string GetReliabilityString(object? raw)
        {
            if (raw is null) return "Unknown";
            string s = raw.ToString() ?? "";
            if (Enum.TryParse<BacnetReliability>(s, out var r))
            {
                return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
                    r.ToString().Replace("_", " ").ToLower());
            }
            return s;
        }

        protected virtual void HandleObjectAlarms(
            AlarmStore alarmStore, DeviceConfig device, DiscoveryState state,
            BacnetObjectInfo obj, object statusRaw,
            Dictionary<BacnetPropertyIds, object?> allValues,
            BacnetClient? client = null, BacnetAddress? address = null)
        {
            if (statusRaw is not BacnetBitString bs) return;

            // BACnet STATUS_FLAGS bit positions (MSB-first string representation):
            //   bit 0 = in-alarm  → "1000"   bit 1 = fault → "0100"
            //   bit 2 = overridden→ "0010"   bit 3 = out-of-service → "0001"
            bool inAlarm = bs.GetBit(0);
            bool isFault = bs.GetBit(1);
            bool outOfService = bs.GetBit(3);

            // Also treat RELIABILITY != NO_FAULT_DETECTED (0) as a fault
            bool hasReliabilityFault = false;
            string relStr = "No Fault";
            if (allValues.TryGetValue(BacnetPropertyIds.PROP_RELIABILITY, out var relRaw) && relRaw is uint relUint && relUint != 0)
            {
                hasReliabilityFault = true;
                relStr = GetReliabilityString(relRaw);
            }

            // ── Alarm type: "In Alarm" or "Communication Fault" ───────────────
            string shortObj = $"{obj.ObjectId.type.ToString().Replace("OBJECT_", "").Replace("_", "").ToUpper()}:{obj.ObjectId.instance}";
            string alarmType = hasReliabilityFault ? "Communication Fault"
                             : isFault ? "Fault"
                             : inAlarm ? "In Alarm"
                                                   : "Out of Service";
            string category = alarmType; // for use in description/message below

            string friendly = GetFriendlyName(obj);
            bool isAlarmed = isFault || inAlarm || hasReliabilityFault || outOfService;

            // Capture last state for potential re-evaluation after hierarchy discovery
            lock (state.ActiveAlarms)
            {
                state.LastAlarmState[obj.ObjectId] = (bs, allValues);
            }

            if (isAlarmed)
            {
                if (ShouldDelayAlarm(device, state)) return;

                string severity = hasReliabilityFault || isFault ? "CRITICAL"
                                : inAlarm ? "MAJOR"
                                                                 : "WARNING";

                string pathStr = obj.NamingPath.Count > 0 ? string.Join(" › ", obj.NamingPath) : obj.ObjectName;
                string message = $"{friendly}: {category}";
                if (hasReliabilityFault) message += $" ({relStr})";

                string descParts = !string.IsNullOrWhiteSpace(obj.Description) ? $" — {obj.Description}" : "";
                string description = $"BACnet object {shortObj} '{pathStr}'{descParts} reported {category.ToLower()}";
                if (hasReliabilityFault) description += $": {relStr}";
                description += $". Device: {device.Name}. StatusFlags: {bs}.";

                // ── Originator: use device name + path for local alarm routing ──
                string originName = device.Name;
                string originType = "DEVICE";
                if (obj.NamingPath.Count > 0)
                {
                    originName = $"{device.Name} / {string.Join(" / ", obj.NamingPath)}";
                    originType = "ASSET";
                }

                // ── Build BACnet ACK registry entry (only when we have a live client) ────────
                string ackKey = $"{device.ConnectionId}:{(int)obj.ObjectId.type}:{obj.ObjectId.instance}";
                if (client != null && address != null)
                {
                    var bacnetEventState = (isFault || hasReliabilityFault)
                        ? BacnetEventStates.EVENT_STATE_FAULT
                        : BacnetEventStates.EVENT_STATE_OFFNORMAL;
                    _ackRegistry[ackKey] = new BacnetAckContext(client, address, obj.ObjectId, bacnetEventState, DateTime.UtcNow);
                }

                var details = new Dictionary<string, object>
                {
                    { "object",       shortObj },
                    { "name",          friendly },
                    { "path",          pathStr },
                    { "description",   description },
                    { "status_flags",  bs.ToString() },
                    { "reliability",   relStr },
                    { "bacnetAckKey",  ackKey },
                    { "telemetryKey",  $"{obj.KeyPrefix}_present_value" }
                };
                if (!string.IsNullOrWhiteSpace(obj.Description))
                    details["bacnet_description"] = obj.Description;
                if (allValues.TryGetValue(BacnetPropertyIds.PROP_EVENT_STATE, out var evt) && evt != null)
                    details["event_state"] = evt.ToString() ?? "";

                alarmStore.CreateOrUpdate(
                    originName, originType,
                    alarmType, severity, message,
                    details, ackKey);

                lock (state.ActiveAlarms) state.ActiveAlarms.Add(alarmType);
            }
            else
            {
                // Clear alarm if it was previously active
                bool wasActive;
                lock (state.ActiveAlarms) wasActive = state.ActiveAlarms.Remove(alarmType);

                if (wasActive)
                {
                    string clearOrigin = device.Name;
                    string clearType = "DEVICE";
                    if (obj.NamingPath.Count > 0)
                    {
                        clearOrigin = $"{device.Name} / {string.Join(" / ", obj.NamingPath)}";
                        clearType = "ASSET";
                    }
                    alarmStore.ClearByOriginAndType(clearOrigin, alarmType, clearType);
                }
            }
        }

        protected virtual bool ShouldDelayAlarm(DeviceConfig device, DiscoveryState state) => false;

        // =====================================================================
        //  Historical Sync – reads PROP_LOG_BUFFER from associated Trend Logs
        // =====================================================================
        private Task SyncTrendLogsAsync(
            BacnetClient client, BacnetAddress address, DiscoveryState state,
            TelemetryStore dataStore, DeviceConfig device)
        {
            List<BacnetObjectInfo> objectsWithLogs;
            lock (_stateLock)
            {
                objectsWithLogs = state.CachedObjects.Where(o => o.LogObjectId != null).ToList();
            }

            if (objectsWithLogs.Count == 0) return Task.CompletedTask;

            Pulswerk.Core.Log.Info($"[BACnet] Starting historical sync for {objectsWithLogs.Count} objects on {device.Name}…");

            int totalSynced = 0;
            int failedCount = 0;

            foreach (var obj in objectsWithLogs)
            {
                if (obj.LogObjectId == null) continue;

                try
                {
                    if (client.ReadPropertyRequest(address, obj.LogObjectId.Value,
                        BacnetPropertyIds.PROP_LOG_BUFFER, out IList<BacnetValue> records))
                    {
                        int synced = 0;

                        // Try nested format: each record is [datetime, value]
                        foreach (var rec in records)
                        {
                            if (rec.Value is IList<BacnetValue> parts && parts.Count >= 2)
                            {
                                if (parts[0].Value is DateTime dt)
                                {
                                    long ts = new DateTimeOffset(dt).ToUnixTimeMilliseconds();
                                    var formatted = FormatValue(obj, BacnetPropertyIds.PROP_PRESENT_VALUE, parts[1].Value);
                                    dataStore.Insert($"{obj.KeyPrefix}_value", ts, formatted);
                                    synced++;
                                }
                            }
                        }

                        // Fallback: flat interleaved format
                        // BACnet stacks may split DATETIME into DATE+TIME, yielding
                        // [date, time, val, date, time, val, ...] (stride 3)
                        // or [datetime, val, datetime, val, ...] (stride 2)
                        if (synced == 0 && records.Count >= 2)
                        {
                            // Detect stride: check if first two values are both DateTime (DATE+TIME split)
                            int stride = 2;
                            if (records.Count >= 3 &&
                                records[0].Value is DateTime &&
                                records[1].Value is DateTime &&
                                !(records[2].Value is DateTime))
                            {
                                stride = 3; // DATE + TIME + VALUE
                            }

                            for (int i = 0; i <= records.Count - stride; i += stride)
                            {
                                DateTime? dt = null;
                                object? val = null;

                                if (stride == 3)
                                {
                                    // Combine DATE + TIME into single DateTime
                                    if (records[i].Value is DateTime date && records[i + 1].Value is DateTime time)
                                    {
                                        dt = new DateTime(date.Year, date.Month, date.Day,
                                                          time.Hour, time.Minute, time.Second, DateTimeKind.Utc);
                                        val = records[i + 2].Value;
                                    }
                                }
                                else
                                {
                                    if (records[i].Value is DateTime dtVal)
                                    {
                                        dt = dtVal;
                                        val = records[i + 1].Value;
                                    }
                                }

                                if (dt.HasValue && val != null)
                                {
                                    long ts = new DateTimeOffset(dt.Value).ToUnixTimeMilliseconds();
                                    var formatted = FormatValue(obj, BacnetPropertyIds.PROP_PRESENT_VALUE, val);
                                    dataStore.Insert($"{obj.KeyPrefix}_value", ts, formatted);
                                    synced++;
                                }
                            }
                        }

                        if (synced > 0)
                        {
                            totalSynced += synced;
                        }
                    }
                }
                catch (Exception ex)
                {
                    failedCount++;
                    if (failedCount == 1)
                        Pulswerk.Core.Log.Warning($"[BACnet] Trend log read failed for {obj.ObjectName ?? obj.ObjectId.ToString()}: {ex.Message}");
                }
            }

            var syncMsg = $"[BACnet] Historical sync for {device.Name} complete.";
            if (totalSynced > 0)
                syncMsg += $" {totalSynced} records synced.";
            if (failedCount > 0)
                syncMsg += $" {failedCount}/{objectsWithLogs.Count} trend logs unavailable.";
            Pulswerk.Core.Log.Info(syncMsg);
            return Task.CompletedTask;
        }

        // ── Value conversion – delegates to BacnetValueConverter ────────────
        //    All TelemetryValues paths (RPM, COV, COV-fallback) use these wrappers
        //    so there is exactly one conversion implementation.

        static bool TryToDouble(object? v, out double result) =>
            BacnetValueConverter.TryToDouble(v, out result);

        static object FormatValue(BacnetObjectInfo obj, BacnetPropertyIds propId, object? raw)
        {
            var result = BacnetValueConverter.FormatValue(obj, propId, raw);

            // Log suppressed errors for diagnostics (keep in driver, not in converter)
            if (raw != null && raw.ToString() is string s && s.Contains("ERROR_"))
                Pulswerk.Core.Log.Debug($"[BACnet] FormatValue suppressed error for {obj.ObjectName} ({obj.ObjectId}): {s}");

            return result;
        }

        // ── Per-device mutable discovery state ───────────────────────────────

        public class DiscoveryState
        {
            // ── Core discovery ────────────────────────────────────────────────
            public bool DiscoveryDone { get; set; }
            public bool AttributesSent { get; set; }  // non-COV only
            public bool HierarchyDirty { get; set; }
            public bool HierarchyReady { get; set; }
            public DateTime LastDiscovery { get; set; } = DateTime.MinValue;
            public List<BacnetObjectInfo> CachedObjects { get; set; } = new();
            /// <summary>Populated after discovery when hierarchy extraction is enabled.</summary>
            public DezikoTree? Tree { get; set; }

            /// <summary>Tracks active alarm types for this device to allow clearing.</summary>
            public HashSet<string> ActiveAlarms { get; set; } = new();

            public TelemetryConflator? Conflator { get; set; }

            public TelemetryConflator GetConflator(Func<TelemetryValues, Task> publisher)
            {
                if (Conflator == null)
                {
                    lock (this)
                    {
                        Conflator ??= new TelemetryConflator(publisher);
                    }
                }
                return Conflator;
            }

            /// <summary>Stores the last known status/values for re-evaluating alarms after hierarchy ready.</summary>
            public Dictionary<BacnetObjectId, (BacnetBitString? Status, Dictionary<BacnetPropertyIds, object?> Values)>
                                          LastAlarmState
            { get; set; } = new();

            // ── COV mode ──────────────────────────────────────────────────────
            /// <summary>Long-lived client kept open for the lifetime of the process (COV mode).</summary>
            public BacnetClient? CovClient { get; set; }
            public BacnetAddress? CovAddress { get; set; }

            /// <summary>Latest value received via COV notification per object.</summary>
            public System.Collections.Concurrent.ConcurrentDictionary<BacnetObjectId, CovSnapshot>
                                          CovValues
            { get; set; } = new();

            /// <summary>When each COV subscription expires (we renew 30 s before).</summary>
            public System.Collections.Concurrent.ConcurrentDictionary<BacnetObjectId, DateTime>
                                          CovSubExpiry
            { get; set; } = new();

            /// <summary>Objects that returned a NAK to SubscribeCOV – polled the old way.</summary>
            public System.Collections.Concurrent.ConcurrentDictionary<BacnetObjectId, byte> CovFallbackPoll { get; set; } = new();

            /// <summary>When fallback-polled objects were last read (throttled to device poll interval).</summary>
            public DateTime LastFallbackPoll { get; set; } = DateTime.MinValue;

            // ── Attribute drip-poll ───────────────────────────────────────────
            /// <summary>Round-robin cursor across (object × attribute-property) slots.</summary>
            public int AttrPollCursor { get; set; } = 0;
            /// <summary>Earliest time the next attribute read is permitted.</summary>
            public DateTime NextAttrPoll { get; set; } = DateTime.MinValue;

            // ── Async publish callbacks (set by Program.cs for COV devices) ───
            public Func<TelemetryValues, Task>? PublishTelemetryValues { get; set; }
            public Func<Attributes, Task>? PublishAttributes { get; set; }

            // ── Trend Log sync ───────────────────────────────────────────────
            /// <summary>True after the initial trend log backfill has been performed.
            /// Only reset on device recovery from stale/offline.</summary>
            public bool TrendLogsSynced { get; set; }

            // Internal tracking for busy state
            public int DiscoveryTaskFinished { get; set; }
            public int SyncTaskFinished { get; set; } = 1; // Default to finished if no sync started
        }

        /// <summary>
        /// Marks hierarchy as ready for a device, enabling alarm routing to use asset paths.
        /// Called by the background hierarchy provisioner in Program.cs after tree extraction.
        /// </summary>
        public void MarkHierarchyReady(string deviceName)
        {
            var state = GetOrCreateState(deviceName);

            lock (state.ActiveAlarms)
            {
                state.HierarchyReady = true;
            }

            Pulswerk.Core.Log.Info($"[BACnet] Hierarchy ready for '{deviceName}'. Alarm routing now uses asset paths.");
        }

        /// <summary>
        /// Returns the list of objects discovered during the last poll cycle for a device.
        /// </summary>
        public List<BacnetObjectInfo> GetDiscoveredObjects(string deviceName)
        {
            lock (_stateLock)
            {
                if (_stateByDevice.TryGetValue(deviceName, out var s))
                    return s.CachedObjects.ToList();
                return new List<BacnetObjectInfo>();
            }
        }

        public DezikoTree? GetDiscoveredTree(string deviceName)
        {
            lock (_stateLock)
                return _stateByDevice.TryGetValue(deviceName, out var s) ? s.Tree : null;
        }

        // =====================================================================
        //  COV mode – setup, subscription management, service loop
        // =====================================================================

        /// <summary>
        /// Initialises COV mode for a device. Called once from Program.cs on startup.
        /// Creates a long-lived BacnetClient, runs discovery, subscribes COV for all
        /// discovered objects, and registers the async publish callbacks that are invoked
        /// whenever a COV notification arrives.
        /// </summary>
        public void InitCovMode(
            ConnectionConfig conn,
            DeviceConfig device,
            AlarmStore alarmStore,
            TelemetryStore tsStore,
            Func<TelemetryValues, Task> publishTelemetryValues,
            Func<Attributes, Task> publishAttributes)
        {
            var cfg = device;  // BACnet config is flat on DeviceConfig
            var state = GetOrCreateState(device.Name);

            state.PublishTelemetryValues = publishTelemetryValues;
            state.PublishAttributes = publishAttributes;

            // Long-lived client (one UDP socket per device)
            state.CovClient = OpenClient(conn);
            state.CovAddress = ResolveAddress(
                state.CovClient, device.Address ?? conn.Address ?? "", conn.Port ?? 47808,
                device.DeviceId ?? 0,
                cfg.WhoIsTimeoutMs > 0 ? cfg.WhoIsTimeoutMs : 2000);

            // Register COV notification handler
            state.CovClient.OnCOVNotification +=
                (sender, adr, invokeId, _, _, monitoredObjId, _, needConfirm, values, _) =>
                {
                    // Security: Verify source address to avoid crosstalk if multiple devices share one client
                    if (state.CovAddress != null && !adr.Equals(state.CovAddress)) return;

                    // ACK confirmed notifications
                    if (needConfirm)
                        try
                        {
                            sender.SimpleAckResponse(adr,
                            BacnetConfirmedServices.SERVICE_CONFIRMED_COV_NOTIFICATION,
                            invokeId);
                        }
                        catch { /* best-effort */ }

                    BacnetObjectInfo? objInfo;
                    lock (_stateLock)
                        objInfo = state.CachedObjects
                                       .FirstOrDefault(o => o.ObjectId == monitoredObjId);
                    if (objInfo is null) return;

                    var tel = new TelemetryValues();
                    foreach (var pv in values)
                    {
                        var propId = (BacnetPropertyIds)pv.property.propertyIdentifier;
                        if (pv.value?.Count > 0)
                        {

                            // Skip error responses in COV notifications
                            bool hasError = pv.value.Any(v =>
                                v.Tag == BacnetApplicationTags.BACNET_APPLICATION_TAG_ERROR ||
                                (v.Value?.ToString()?.Contains("ERROR_") == true));

                            // FormatValue handles null/error → typed default
                            var raw = hasError ? null : pv.value[0].Value;
                            var formatted = FormatValue(objInfo, propId, raw);
                            tel[$"{objInfo.KeyPrefix}_{PropSuffix(propId)}"] = formatted;

                            if (TryToDouble(raw ?? formatted, out double d))
                            {
                                lock (_stateLock)
                                    state.CovValues[monitoredObjId] = new CovSnapshot(d, DateTime.UtcNow);
                            }
                        }
                    }

                    if (tel.Count > 0)
                    {
                        // Use the conflator to batch updates
                        state.GetConflator(publishTelemetryValues).Add(tel);

                        // Store in InfluxDB
                        tsStore.InsertBatch(tel);
                    }

                    // ── Diagnostic Alarms (COV) ───────────────────────────────
                    if (values.Any(pv => (BacnetPropertyIds)pv.property.propertyIdentifier == BacnetPropertyIds.PROP_STATUS_FLAGS))
                    {
                        var statusPv = values.First(pv => (BacnetPropertyIds)pv.property.propertyIdentifier == BacnetPropertyIds.PROP_STATUS_FLAGS);
                        var allProps = values.ToDictionary(
                            pv => (BacnetPropertyIds)pv.property.propertyIdentifier,
                            pv => pv.value?.Count > 0 ? pv.value[0].Value : null);

                        HandleObjectAlarms(alarmStore, device, state, objInfo, statusPv.value[0].Value, allProps,
                            state.CovClient, state.CovAddress);
                    }
                };

            // Discovery + initial subscriptions
            RunDiscoveryInternal(state.CovClient, state.CovAddress, device, cfg, state);
            EnsureCovSubscriptions(state, cfg);

            // ── Sync Trend Logs (Historical Backfill) ─────────────────────
            _ = Task.Run(async () =>
            {
                await Task.Delay(5000);
                await SyncTrendLogsAsync(state.CovClient, state.CovAddress, state, tsStore, device);
            });

            Pulswerk.Core.Log.Info(
                $"[COV] {device.Name}: {state.CachedObjects.Count} subscribed, " +
                $"{state.CovFallbackPoll.Count} fallback-poll.");
        }

        /// <summary>
        /// Runs object discovery and caches results into <paramref name="state"/>.
        /// Shared by InitCovMode and the periodic rediscovery path.
        /// </summary>
        void RunDiscoveryInternal(
            BacnetClient client, BacnetAddress address,
            DeviceConfig device, DeviceConfig cfg, DiscoveryState state)
        {
            Pulswerk.Core.Log.Info($"[BACnet] Discovering objects on {device.Name}…");
            var all = DiscoverObjects(client, address, device.DeviceId!.Value);
            var disc = cfg.Discovery ?? BacnetDiscoveryConfig.Default;
            var resultDict = ApplyFilter(client, address, all, cfg.Filter ?? BacnetFilterConfig.Default, device.Id, device.DeviceId!.Value, GetExtraDiscoveryProperties(), disc.ReadDelayMs);
            var filtered = resultDict.Objects;

            // Enrich objects with vendor-specific properties (override in subclass)
            filtered = filtered.Select(obj => EnrichObjectInfo(client, address, obj, resultDict.ExtraProperties)).ToList();

            // Resolve Trend Log associations (standard BACnet prop 132)
            var trendLogMap = ResolveTrendLogMap(client, address, all);
            if (trendLogMap.Count > 0)
            {
                Pulswerk.Core.Log.Info($"[BACnet] Found {trendLogMap.Count} Trend Log association(s).");
                filtered = filtered.Select(obj =>
                    trendLogMap.TryGetValue(obj.ObjectId, out var logObjId) && obj.LogObjectId == null
                        ? obj with { LogObjectId = logObjId }
                        : obj
                ).ToList();
            }

            lock (_stateLock)
            {
                state.CachedObjects = filtered;
                state.LastDiscovery = DateTime.UtcNow;
                state.DiscoveryDone = true;
                state.HierarchyDirty = true;
                state.HierarchyReady = false;
            }

            // Extension point for subclasses (e.g. DezikoDriver hierarchy walk)
            OnPostDiscovery(client, address, state, device, cfg);

            Pulswerk.Core.Log.Info(
                $"[BACnet] {device.Name}: {all.Count} found, {filtered.Count} after filter" +
                (trendLogMap.Count > 0 ? $", {trendLogMap.Count} trend logs linked." : "."));
        }

        /// <summary>
        /// Subscribes (or re-subscribes) COV for all objects not yet subscribed or whose
        /// subscription expires within 30 seconds. Objects that NAK go into
        /// <see cref="DiscoveryState.CovFallbackPoll"/>.
        /// </summary>
        void EnsureCovSubscriptions(DiscoveryState state, DeviceConfig cfg)
        {
            var covCfg = cfg.EffectiveCov!;
            var now = DateTime.UtcNow;
            var renew = now.AddSeconds(30);   // renew anything expiring in next 30 s

            foreach (var obj in state.CachedObjects)
            {
                // Skip if subscription still has > 30 s left
                if (state.CovSubExpiry.TryGetValue(obj.ObjectId, out var exp) && exp > renew)
                    continue;

                try
                {
                    bool ok = state.CovClient!.SubscribeCOVRequest(
                        state.CovAddress!,
                        obj.ObjectId,
                        1u,    // subscribeId
                        false, // cancel
                        covCfg.ConfirmedNotifications,
                        covCfg.LifetimeSeconds,
                        0);    // maxSegments (0 = unspecified)

                    if (ok)
                    {
                        state.CovSubExpiry[obj.ObjectId] =
                            now.AddSeconds(covCfg.LifetimeSeconds);
                        state.CovFallbackPoll.TryRemove(obj.ObjectId, out _);

                        // Seed value cache on first subscription
                        if (!state.CovValues.ContainsKey(obj.ObjectId))
                        {
                            var seed = ReadObjectProperties(
                                state.CovClient, state.CovAddress!, obj.ObjectId,
                                new[] { BacnetPropertyIds.PROP_PRESENT_VALUE });
                            if (seed.TryGetValue(BacnetPropertyIds.PROP_PRESENT_VALUE, out var raw))
                            {
                                if (TryToDouble(raw, out double d))
                                    state.CovValues[obj.ObjectId] = new CovSnapshot(d, now);

                                // Also publish seed as TelemetryValues so LatestValues is populated
                                // immediately - prevents "---" for objects that haven't changed.
                                var formatted = FormatValue(obj, BacnetPropertyIds.PROP_PRESENT_VALUE, raw);
                                var seedTel = new TelemetryValues { [$"{obj.KeyPrefix}_value"] = formatted };
                                state.GetConflator(state.PublishTelemetryValues!).Add(seedTel);
                            }
                        }
                    }
                    else
                    {
                        Pulswerk.Core.Log.Debug($"[COV] {obj.ObjectName}: fallback-poll (NAK)");
                        state.CovFallbackPoll.TryAdd(obj.ObjectId, 0);
                        state.CovSubExpiry[obj.ObjectId] = now.AddMinutes(10);
                    }
                }
                catch (Exception ex)
                {
                    Pulswerk.Core.Log.Debug($"[COV] {obj.ObjectName}: fallback-poll ({ex.Message})");
                    state.CovFallbackPoll.TryAdd(obj.ObjectId, 0);
                    state.CovSubExpiry[obj.ObjectId] = now.AddMinutes(10);
                }
            }
        }

        /// <summary>
        /// Called every fast tick (1 s) for BACnet-COV devices.
        /// Does NOT publish COV TelemetryValues (that is done event-driven from OnCOVNotification).
        /// Returns a <see cref="BacnetReadResult"/> containing:
        ///   • TelemetryValues for fallback-polled objects (those that could not subscribe COV)
        ///   • At most one attribute key read by the drip-poller this tick
        ///   • HierarchyDirty flag propagated as usual
        /// </summary>
        public BacnetReadResult ServiceCovDevice(ConnectionConfig conn, DeviceConfig device)
        {
            var cfg = device;  // BACnet config is flat on DeviceConfig
            var covCfg = cfg.EffectiveCov!;
            var state = GetOrCreateState(device.Name);
            var result = new BacnetReadResult();

            if (!state.DiscoveryDone || state.CovClient is null)
                return result;

            // Periodic rediscovery
            var disc = cfg.Discovery ?? BacnetDiscoveryConfig.Default;
            if (disc.RefreshIntervalMinutes > 0
                && DateTime.UtcNow - state.LastDiscovery
                   > TimeSpan.FromMinutes(disc.RefreshIntervalMinutes))
            {
                state.CovAddress = ResolveAddress(
                    state.CovClient, device.Address ?? conn.Address ?? "", conn.Port ?? 47808,
                    device.DeviceId ?? 0,
                    cfg.WhoIsTimeoutMs > 0 ? cfg.WhoIsTimeoutMs : 2000);
                RunDiscoveryInternal(state.CovClient, state.CovAddress, device, cfg, state);
                state.CovSubExpiry.Clear();  // force resubscription of everything
            }

            // Renew expiring subscriptions
            EnsureCovSubscriptions(state, cfg);

            // Attribute drip-poll – read one (object × property) slot if rate allows
            var props = cfg.Properties ?? BacnetPropsConfig.Default;
            var attrPropIds = ParsePropertyIds(props.EffectiveAttributes);
            var attrKv = DrainAttributePoll(
                state, state.CovClient, state.CovAddress!, attrPropIds,
                covCfg.AttributePollRatePerMinute);
            foreach (var kv in attrKv)
                result.Attributes[kv.Key] = kv.Value;

            var telPropIds = ParsePropertyIds(props.EffectiveTelemetries);

            // Fallback poll – objects that NAK'd COV: read TelemetryValues at the device
            // poll interval (default 1h for BACnet), not every 1s tick.
            int fallbackIntervalSec = device.PollIntervalSeconds ?? 3600;
            bool shouldFallbackPoll = telPropIds.Length > 0
                && state.CovFallbackPoll.Count > 0
                && (DateTime.UtcNow - state.LastFallbackPoll).TotalSeconds >= fallbackIntervalSec;

            if (shouldFallbackPoll)
            {
                state.LastFallbackPoll = DateTime.UtcNow;
                var fallback = state.CachedObjects
                    .Where(o => state.CovFallbackPoll.ContainsKey(o.ObjectId));
                foreach (var obj in fallback)
                {
                    var vals = ReadObjectProperties(
                        state.CovClient, state.CovAddress!, obj.ObjectId, telPropIds);
                    foreach (var p in telPropIds)
                    {
                        string key = $"{obj.KeyPrefix}_{PropSuffix(p)}";
                        if (vals.TryGetValue(p, out var raw))
                            result.TelemetryValues[key] = FormatValue(obj, p, raw);
                        else if (p == BacnetPropertyIds.PROP_PRESENT_VALUE)
                            result.TelemetryValues[key] = FormatValue(obj, p, null);
                    }
                }
            }

            // Propagate hierarchy dirty flag once per discovery cycle
            if (state.HierarchyDirty)
            {
                result.HierarchyDirty = true;
                state.HierarchyDirty = false;
            }

            return result;
        }

        /// <summary>
        /// Drip-polls exactly one (object × attribute-property) slot per call when the
        /// configured rate allows it. Returns a single-entry Attributes dict (or empty).
        /// The cursor advances round-robin across all cached objects × all attribute properties.
        /// </summary>
        static Attributes DrainAttributePoll(
            DiscoveryState state,
            BacnetClient client,
            BacnetAddress address,
            BacnetPropertyIds[] attrProps,
            int ratePerMinute)
        {
            var result = new Attributes();
            if (attrProps.Length == 0 || state.CachedObjects.Count == 0)
                return result;

            var now = DateTime.UtcNow;
            if (state.NextAttrPoll > now)
                return result;   // rate-limit: not yet

            int totalSlots = state.CachedObjects.Count * attrProps.Length;
            int slot = state.AttrPollCursor % totalSlots;
            int objIdx = slot / attrProps.Length;
            int pIdx = slot % attrProps.Length;

            var obj = state.CachedObjects[objIdx];
            var propId = attrProps[pIdx];

            try
            {
                if (client.ReadPropertyRequest(address, obj.ObjectId, propId,
                        out IList<BacnetValue> vals)
                    && vals.Count > 0)
                {
                    result[$"{obj.KeyPrefix}_{PropSuffix(propId)}"] =
                        vals[0].Value?.ToString() ?? "";
                }
            }
            catch { /* property unavailable on this object */ }

            state.AttrPollCursor = (state.AttrPollCursor + 1) % totalSlots;
            double intervalMs = ratePerMinute > 0 ? 60_000.0 / ratePerMinute : 12_000;
            state.NextAttrPoll = now.AddMilliseconds(intervalMs);

            return result;
        }

        /// <summary>
        /// Unsubscribes all COV subscriptions and disposes the long-lived client for a device.
        /// Called from Program.cs on graceful shutdown.
        /// </summary>
        public void DisposeCovClient(string deviceName)
        {
            lock (_stateLock)
            {
                if (!_stateByDevice.TryGetValue(deviceName, out var s)) return;
                if (s.CovClient is null) return;
                try
                {
                    // Cancel all active subscriptions before closing the socket
                    foreach (var obj in s.CachedObjects
                        .Where(o => !s.CovFallbackPoll.ContainsKey(o.ObjectId)))
                    {
                        try
                        {
                            s.CovClient.SubscribeCOVRequest(
                                s.CovAddress!, obj.ObjectId,
                                1u, true, false, 0u, 0);
                        }
                        catch { /* best-effort */ }
                    }
                }
                finally
                {
                    if (!_clientsByConnection.Values.Contains(s.CovClient))
                        s.CovClient.Dispose();
                    s.CovClient = null;
                }
            }
        }

        // =====================================================================
        //  IDeviceWriter – write a value to a BACnet object's PROP_PRESENT_VALUE
        // =====================================================================
        public bool IsWritable(string key)
        {
            lock (_stateLock)
            {
                foreach (var kvp in _stateByDevice)
                {
                    foreach (var obj in kvp.Value.CachedObjects)
                    {
                        string fullKey = obj.KeyPrefix + "_value";
                        // Match full key OR driver-scoped key (device prefix stripped)
                        if (fullKey == key || fullKey.EndsWith("_" + key) || key == fullKey)
                        {
                            return obj.Writeable || obj.Commandable;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Finds the cached <see cref="BacnetObjectInfo"/> for a given TelemetryValues key.
        /// Returns null if not found. Used by DashboardDataService to resolve
        /// display values through the converter after a write.
        /// </summary>
        public BacnetObjectInfo? FindCachedObject(string key)
        {
            lock (_stateLock)
            {
                foreach (var state in _stateByDevice.Values)
                {
                    var obj = state.CachedObjects.FirstOrDefault(
                        o => (o.KeyPrefix + "_value") == key);
                    if (obj != null) return obj;

                    // Try with device prefix stripped
                    if (state.CachedObjects.Count > 0)
                    {
                        string prefix = state.CachedObjects[0].TechDeviceId + "_";
                        obj = state.CachedObjects.FirstOrDefault(
                            o => (o.KeyPrefix + "_value") == (prefix + key));
                        if (obj != null) return obj;
                    }
                }
            }
            return null;
        }

        public void Write(ConnectionConfig conn, DeviceConfig device, string key, double value)
        {
            if (device.DeviceId is null)
                throw new InvalidOperationException(
                    $"Device '{device.Name}' is missing deviceId.");

            var cfg = device;  // BACnet config is flat on DeviceConfig

            // Resolve target object from the TelemetryValues key
            var state = GetOrCreateState(device.Name);

            var objectId = ResolveObjectIdFromKey(key, state);
            if (objectId == null)
                throw new ArgumentException($"Cannot resolve BACnet object from key '{key}'.");

            var obj = state.CachedObjects.FirstOrDefault(o => o.ObjectId == objectId.Value);

            if (obj is null)
                throw new KeyNotFoundException(
                    $"Object for key '{key}' ({objectId}) not found in discovery cache for '{device.Name}'. " +
                    "Trigger a rediscovery or check the key name.");

            if (!obj.Writeable && !obj.Commandable)
                throw new InvalidOperationException(
                    $"Object '{key}' ({objectId}) is neither writeable nor commandable. " +
                    "Write rejected to protect the device logic.");

            var client = OpenClient(conn);
            var address = ResolveAddress(client, device.Address ?? conn.Address ?? "", conn.Port ?? 47808, device.DeviceId ?? 0, 2000);

            // Convert UI value → internal numeric (reverse state-text lookup if needed)
            double internalVal = BacnetValueConverter.FromDisplayValue(obj, value);

            // Delegate tag selection to the converter (binary → ENUMERATED, analog → REAL)
            BacnetValue bv = BacnetValueConverter.ToWriteValue(objectId.Value.type, internalVal);

            try
            {
                bool ok = client.WritePropertyRequest(
                    address, objectId.Value,
                    BacnetPropertyIds.PROP_PRESENT_VALUE,
                    new List<BacnetValue> { bv });

                if (!ok)
                {
                    string errMsg = $"[BACnet] WriteProperty returned NAK or Timeout. Device: {device.Name} ({address}), Object: {obj.ObjectName} ({objectId.Value}), Target Value: {internalVal} (Raw: {value})";
                    Pulswerk.Core.Log.Warning(errMsg);
                    throw new Exception(errMsg);
                }

                Pulswerk.Core.Log.Info($"[BACnet] Write successful. Device: {device.Name} ({address}), Object: {obj.ObjectName} ({objectId.Value}), Value: {internalVal}");
            }
            catch (Exception ex) when (!ex.Message.StartsWith("[BACnet] WriteProperty"))
            {
                string errMsg = $"[BACnet] Exception during WriteProperty. Device: {device.Name} ({address}), Object: {obj.ObjectName} ({objectId.Value}), Target Value: {internalVal}. Error: {ex.Message}";
                Pulswerk.Core.Log.Error(errMsg);
                throw new Exception(errMsg, ex);
            }
        }
        public void WriteComplex(ConnectionConfig conn, DeviceConfig device, string key, object value)
        {
            if (device.DeviceId is null) throw new InvalidOperationException("Device is missing deviceId.");
            var state = GetOrCreateState(device.Name);
            var objectId = ResolveObjectIdFromKey(key, state);
            if (objectId == null) throw new ArgumentException($"Cannot resolve object from key '{key}'.");

            var client = OpenClient(conn);
            var address = ResolveAddress(client, device.Address ?? conn.Address ?? "", conn.Port ?? 47808, device.DeviceId ?? 0, 2000);

            string json;
            if (value is string s) json = s;
            else if (value is System.Text.Json.JsonElement je)
            {
                // If the JsonElement is a string, it means the JSON was double-encoded 
                // or passed as a string property. We need the inner content.
                json = je.ValueKind == System.Text.Json.JsonValueKind.String ? (je.GetString() ?? "") : je.GetRawText();
            }
            else json = System.Text.Json.JsonSerializer.Serialize(value);

            if (objectId.Value.type == BacnetObjectTypes.OBJECT_SCHEDULE)
            {
                WriteWeeklySchedule(client, address, objectId.Value, json, device.Name);
                return;
            }

            string errMsg = $"[BACnet] Complex write not supported for key '{key}' (type {objectId.Value.type}). Device: {device.Name} ({address}).";
            Pulswerk.Core.Log.Warning(errMsg);
            throw new NotSupportedException(errMsg);
        }

        private void WriteWeeklySchedule(BacnetClient client, BacnetAddress address, BacnetObjectId oid, string json, string deviceName)
        {
            try
            {
                var days = JsonSerializer.Deserialize<List<DailyScheduleDto>>(json);
                if (days == null) return;

                // ── Detect the value tag from PROP_SCHEDULE_DEFAULT ──────────
                // The schedule default tells us whether values are REAL, ENUMERATED, UNSIGNED, etc.
                var valueTag = BacnetApplicationTags.BACNET_APPLICATION_TAG_ENUMERATED; // safe default for boolean schedules
                try
                {
                    if (client.ReadPropertyRequest(address, oid, BacnetPropertyIds.PROP_SCHEDULE_DEFAULT, out var defVals)
                        && defVals.Count > 0)
                    {
                        valueTag = defVals[0].Tag;
                    }
                }
                catch { /* fallback to enumerated */ }

                // If we still couldn't detect, try reading existing schedule to get the tag
                if (valueTag == BacnetApplicationTags.BACNET_APPLICATION_TAG_NULL)
                {
                    try
                    {
                        if (client.ReadPropertyRequest(address, oid, BacnetPropertyIds.PROP_WEEKLY_SCHEDULE, out var schedVals)
                            && schedVals.Count > 0)
                        {
                            // Find first value entry (skip TIME entries)
                            foreach (var sv in schedVals)
                            {
                                if (sv.Tag != BacnetApplicationTags.BACNET_APPLICATION_TAG_TIME
                                    && sv.Tag != BacnetApplicationTags.BACNET_APPLICATION_TAG_NULL)
                                {
                                    valueTag = sv.Tag;
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }

                Pulswerk.Core.Log.Info($"[BACnet] Writing schedule for {oid} on {deviceName} ({address}) — detected value tag: {valueTag}, entries per day: {days.Sum(d => d.Entries?.Count ?? 0)}");

                // BACnet Weekly_Schedule is an array of 7 DailySchedule (SEQUENCE OF TimeValue)
                // arrayIndex 1 = Monday, ..., 7 = Sunday
                for (int i = 0; i < 7; i++)
                {
                    var dayData = days.FirstOrDefault(d => d.DayIndex == i);
                    var dayValues = new List<BacnetValue>();

                    if (dayData?.Entries != null)
                    {
                        // Sort entries by time (mandatory for BACnet schedules)
                        var sortedEntries = dayData.Entries.OrderBy(e => e.Time).ToList();
                        foreach (var entry in sortedEntries)
                        {
                            if (TimeSpan.TryParse(entry.Time, out var ts))
                            {
                                // Use DateTime for the TIME tag, as the library extracts the time part from it.
                                var timeValue = new DateTime(1, 1, 1, ts.Hours, ts.Minutes, ts.Seconds);
                                dayValues.Add(new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_TIME, timeValue));

                                // Encode value with the detected tag
                                object encodedValue = valueTag switch
                                {
                                    BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL => (float)entry.Value,
                                    BacnetApplicationTags.BACNET_APPLICATION_TAG_DOUBLE => entry.Value,
                                    BacnetApplicationTags.BACNET_APPLICATION_TAG_UNSIGNED_INT => (uint)entry.Value,
                                    BacnetApplicationTags.BACNET_APPLICATION_TAG_SIGNED_INT => (int)entry.Value,
                                    BacnetApplicationTags.BACNET_APPLICATION_TAG_ENUMERATED => (uint)entry.Value,
                                    BacnetApplicationTags.BACNET_APPLICATION_TAG_BOOLEAN => entry.Value != 0,
                                    _ => (uint)entry.Value // default to unsigned for unknown tags
                                };
                                dayValues.Add(new BacnetValue(valueTag, encodedValue));
                            }
                        }
                    }

                    // Write each day using WritePropertyMultipleRequest with array index
                    uint arrayIndex = (uint)(i + 1);
                    var propRef = new BacnetPropertyReference((uint)BacnetPropertyIds.PROP_WEEKLY_SCHEDULE, arrayIndex);
                    var propValue = new BacnetPropertyValue { property = propRef, value = dayValues };

                    try
                    {
                        if (!client.WritePropertyMultipleRequest(address, oid, new[] { propValue }))
                        {
                            Pulswerk.Core.Log.Warning($"[BACnet] Failed to write schedule for day index {arrayIndex} (object {oid}) on {deviceName} ({address}). NAK or Timeout.");
                        }
                        else
                        {
                            Pulswerk.Core.Log.Info($"[BACnet] Successfully wrote schedule day index {arrayIndex} to {oid} on {deviceName}.");
                        }
                    }
                    catch (Exception dayEx)
                    {
                        Pulswerk.Core.Log.Error($"[BACnet] Schedule write error day {arrayIndex} on {deviceName} ({address}) for object {oid}: {dayEx.Message}");
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                string errMsg = $"[BACnet] Failed to write weekly schedule on {deviceName} ({address}) for object {oid}. Error: {ex.Message}";
                Pulswerk.Core.Log.Error(errMsg);
                throw new Exception(errMsg, ex);
            }
        }

        private class DailyScheduleDto
        {
            [JsonPropertyName("dayIndex")] public int DayIndex { get; set; }
            [JsonPropertyName("entries")] public List<ScheduleEntryDto>? Entries { get; set; }
        }

        private class ScheduleEntryDto
        {
            [JsonPropertyName("time")] public string Time { get; set; } = "";
            [JsonPropertyName("value")] public double Value { get; set; }
        }

        protected virtual BacnetObjectId? ResolveObjectIdFromKey(string key, DiscoveryState state)
        {
            // 1. Try to find the key in the discovery cache (handles modern/structured keys)
            // TelemetryValues keys are stored as "device.Id_SanitisedName_value"
            // The 'key' passed here might be the full key OR just the suffix after device.Id

            // Check full match first
            var cached = state.CachedObjects.FirstOrDefault(o => (o.KeyPrefix + "_value") == key);
            if (cached == null)
            {
                // Try matching by suffix (common when called from DashboardDataService)
                cached = state.CachedObjects.FirstOrDefault(o => o.ObjectName.EndsWith(key.Replace("_value", ""), StringComparison.OrdinalIgnoreCase));

                // Final attempt: check if the key is the sanitised suffix
                if (cached == null && state.CachedObjects.Count > 0)
                {
                    string devicePrefix = state.CachedObjects[0].TechDeviceId + "_";
                    cached = state.CachedObjects.FirstOrDefault(o => (o.KeyPrefix + "_value") == (devicePrefix + key));
                }
            }

            if (cached != null) return cached.ObjectId;

            // 2. Try legacy parsing (e.g. "ao_3_supply_temp_sp_value")
            if (TryParseKeyToObjectId(key, out var objectId))
                return objectId;

            return null;
        }

        /// <summary>
        /// Legacy fallback: parses a TelemetryValues key such as "ao_3_supply_temp_sp_value" 
        /// into its BacnetObjectId by extracting type and instance.
        /// </summary>
        static bool TryParseKeyToObjectId(string key, out BacnetObjectId result)
        {
            result = default;

            // Split on '_'; first token = type alias, second = instance number
            var parts = key.Split('_');
            if (parts.Length < 2) return false;

            if (!uint.TryParse(parts[1], out uint instance)) return false;

            if (!_typeAliases.TryGetValue(parts[0], out BacnetObjectTypes objType))
                return false;   // only well-known short aliases are accepted for writes

            result = new BacnetObjectId(objType, instance);
            return true;
        }

        // ── IDeviceDriver Hierarchy & Properties ─────────────────────────────

        public AssetNodeDto GetAssetHierarchy(DeviceConfig device)
        {
            var discovered = GetDiscoveredObjects(device.Name);
            if (discovered == null || discovered.Count == 0 || device.DeviceId == null)
                return new AssetNodeDto { Id = device.Name, Name = device.Name, Type = "BACnet Device", IsView = true };

            var telemetries = discovered
                .Where(o => !IsMetaObjectType(o.ObjectId.type))
                .ToList();

            var root = new AssetNodeDto { Id = device.Name, Name = device.Name, IsView = true, Type = "BACnet Device" };

            var tree = GetDiscoveredTree(device.Name);
            bool hasTree = tree != null && tree.Roots.Count > 0;

            if (hasTree)
            {
                // Check if the Structured View tree actually contains data-point leaves
                var lookup = discovered.ToDictionary(o => o.ObjectId, o => o);
                var stats = new TreeConversionStats();
                var referencedIds = new HashSet<BacnetObjectId>();
                var treeChildren = new List<AssetNodeDto>();

                foreach (var rootNode in tree!.Roots)
                {
                    var dto = ConvertNodeToDto(rootNode, device.Id, device.DeviceId!.Value, lookup, new List<PathSegmentDto>(), stats);
                    treeChildren.Add(dto);
                    CollectReferencedIds(rootNode, referencedIds);
                }

                bool shouldLog = !_hierarchyLogged.Contains(device.Name);
                if (shouldLog)
                {
                    _hierarchyLogged.Add(device.Name);
                    Pulswerk.Core.Log.Info(
                        $"[Hierarchy] {device.Name}: tree conversion — " +
                        $"leaves={stats.TotalTreeLeaves}, " +
                        $"resolved={stats.ResolvedFromCache}, " +
                        $"not-in-cache={stats.NotInCache}, " +
                        $"discovered={discovered.Count}, " +
                        $"telemetries={telemetries.Count}");
                }

                if (stats.TotalTreeLeaves > 0)
                {
                    // ── Normal path: tree has data-point leaves ───────────────
                    root.Children.AddRange(treeChildren);

                    // Add orphaned items not referenced by the tree
                    var orphaned = telemetries
                        .Where(o => !referencedIds.Contains(o.ObjectId))
                        .ToList();

                    if (orphaned.Count > 0)
                    {
                        if (shouldLog)
                            Pulswerk.Core.Log.Info(
                                $"[Hierarchy] {device.Name}: {orphaned.Count} orphaned object(s) " +
                                $"not referenced by any Structured View — adding to 'Uncategorized'.");

                        var uncatNode = new AssetNodeDto
                        {
                            Id = $"{device.Id}_uncategorized",
                            Name = "Uncategorized",
                            Description = $"{orphaned.Count} data points not referenced by any Structured View",
                            IsView = true,
                            Type = "Folder"
                        };
                        var uncatPath = new List<PathSegmentDto>
                        {
                            new PathSegmentDto { Id = root.Id, Name = root.Name },
                            new PathSegmentDto { Id = uncatNode.Id, Name = uncatNode.Name }
                        };

                        foreach (var info in orphaned)
                            uncatNode.Telemetries.Add(CreatePointDto(info, device.Id, uncatNode.Id, uncatPath));

                        root.Children.Add(uncatNode);
                    }
                }
                else
                {
                    // ── NamingPath fallback: tree has views but no data-point leaves ──
                    // Deziko controllers encode hierarchy via NamingPath (4397) on each
                    // object rather than listing data points in subordinate lists.
                    int withPath = telemetries.Count(o => o.NamingPath?.Count > 0);
                    if (shouldLog)
                        Pulswerk.Core.Log.Info(
                            $"[Hierarchy] {device.Name}: Structured View tree has 0 data-point leaves — " +
                            $"building hierarchy from NamingPath ({withPath}/{telemetries.Count} objects have paths).");

                    BuildHierarchyFromNamingPaths(root, telemetries, device.Id);
                }
            }
            else
            {
                // ── Flat fallback: no Structured View tree available ──────────
                if (!_hierarchyLogged.Contains(device.Name))
                {
                    _hierarchyLogged.Add(device.Name);
                    Pulswerk.Core.Log.Info(
                        $"[Hierarchy] {device.Name}: no Structured View tree — " +
                        $"building flat hierarchy for {discovered.Count} discovered object(s).");
                }

                // Try NamingPath first, fall back to type-grouping
                int withPath = telemetries.Count(o => o.NamingPath?.Count > 0);
                if (withPath > telemetries.Count / 2)
                {
                    BuildHierarchyFromNamingPaths(root, telemetries, device.Id);
                }
                else
                {
                    BuildFlatHierarchyByType(root, telemetries, device.Id);
                }
            }

            return root;
        }

        /// <summary>
        /// Builds a tree hierarchy from each object's NamingPath segments.
        /// Objects with NamingPath ["Floor1", "AHU01", "SupplyFan"] create
        /// nested folders Floor1 → AHU01, with the point under AHU01.
        /// Objects without a NamingPath go into "Uncategorized".
        /// </summary>
        private static void BuildHierarchyFromNamingPaths(
            AssetNodeDto root, List<BacnetObjectInfo> telemetries, string techDeviceId)
        {
            // Cache of created folder nodes by their full path key
            var folderCache = new Dictionary<string, AssetNodeDto>();
            var uncategorized = new List<BacnetObjectInfo>();

            foreach (var info in telemetries)
            {
                if (info.NamingPath == null || info.NamingPath.Count == 0)
                {
                    uncategorized.Add(info);
                    continue;
                }

                // NamingPath segments define the folder chain
                // e.g. ["Gebäude", "OG1", "RLT001", "Zuluft"] → 3 folders, point under "RLT001"
                // The last segment is the point's friendly name
                var folderSegments = info.NamingPath.Count > 1
                    ? info.NamingPath.Take(info.NamingPath.Count - 1).ToList()
                    : new List<string>(); // single segment = point name only, goes at root

                // Build/find the folder chain
                var parentNode = root;
                var pathAccum = new List<PathSegmentDto> { new PathSegmentDto { Id = root.Id, Name = root.Name } };
                string pathKey = "";

                foreach (var segment in folderSegments)
                {
                    pathKey = string.IsNullOrEmpty(pathKey) ? segment : $"{pathKey}/{segment}";
                    string folderId = $"{techDeviceId}_{AssetNodeDto.PathSegmentId(pathKey)}";

                    if (!folderCache.TryGetValue(pathKey, out var folderNode))
                    {
                        folderNode = new AssetNodeDto
                        {
                            Id = folderId,
                            Name = segment,
                            IsView = true,
                            Type = "Folder"
                        };
                        folderCache[pathKey] = folderNode;
                        parentNode.Children.Add(folderNode);
                    }

                    pathAccum = new List<PathSegmentDto>(pathAccum)
                    {
                        new PathSegmentDto { Id = folderId, Name = segment }
                    };
                    parentNode = folderNode;
                }

                // Create the data point under its folder
                var pointDto = CreatePointDto(info, techDeviceId, parentNode.Id, pathAccum);
                parentNode.Telemetries.Add(pointDto);
            }

            // Handle objects without NamingPath
            if (uncategorized.Count > 0)
            {
                var uncatNode = new AssetNodeDto
                {
                    Id = $"{techDeviceId}_uncategorized",
                    Name = "Uncategorized",
                    Description = $"{uncategorized.Count} data points without NamingPath",
                    IsView = true,
                    Type = "Folder"
                };
                var uncatPath = new List<PathSegmentDto>
                {
                    new PathSegmentDto { Id = root.Id, Name = root.Name },
                    new PathSegmentDto { Id = uncatNode.Id, Name = uncatNode.Name }
                };

                foreach (var info in uncategorized)
                    uncatNode.Telemetries.Add(CreatePointDto(info, techDeviceId, uncatNode.Id, uncatPath));

                root.Children.Add(uncatNode);
            }
        }

        /// <summary>
        /// Groups data points by BACnet object type for a flat hierarchy.
        /// Used when no Structured View tree or NamingPath data is available.
        /// </summary>
        private static void BuildFlatHierarchyByType(
            AssetNodeDto root, List<BacnetObjectInfo> telemetries, string techDeviceId)
        {
            var grouped = telemetries
                .GroupBy(o => GetObjectTypeCategory(o.ObjectId.type))
                .OrderBy(g => g.Key);

            foreach (var group in grouped)
            {
                var folderNode = new AssetNodeDto
                {
                    Id = $"{techDeviceId}_{group.Key.ToLowerInvariant().Replace(" ", "_")}",
                    Name = group.Key,
                    Description = $"{group.Count()} data points",
                    IsView = true,
                    Type = "Folder"
                };
                var folderPath = new List<PathSegmentDto>
                {
                    new PathSegmentDto { Id = root.Id, Name = root.Name },
                    new PathSegmentDto { Id = folderNode.Id, Name = folderNode.Name }
                };

                foreach (var info in group)
                    folderNode.Telemetries.Add(CreatePointDto(info, techDeviceId, folderNode.Id, folderPath));

                root.Children.Add(folderNode);
            }
        }

        /// <summary>
        /// Creates an <see cref="TelemetryDto"/> from a discovered <see cref="BacnetObjectInfo"/>.
        /// Used for orphaned items and the flat-hierarchy fallback.
        /// </summary>
        private static TelemetryDto CreatePointDto(
            BacnetObjectInfo info, string techDeviceId,
            string parentId, List<PathSegmentDto> parentPath)
        {
            string friendlyName;
            if (info.NamingPath?.Count > 0) friendlyName = info.NamingPath.Last();
            else if (!string.IsNullOrEmpty(info.NameExtension)) friendlyName = info.NameExtension;
            else friendlyName = string.IsNullOrWhiteSpace(info.ObjectName)
                ? info.ObjectId.ToString()
                : info.ObjectName.Split(new[] { '.', '\'' }, StringSplitOptions.RemoveEmptyEntries).Last();

            List<string>? enumValues = null;
            bool isMultiState = info.ObjectId.type is
                BacnetObjectTypes.OBJECT_MULTI_STATE_INPUT or
                BacnetObjectTypes.OBJECT_MULTI_STATE_OUTPUT or
                BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE;
            bool isBin = info.ObjectId.type is
                BacnetObjectTypes.OBJECT_BINARY_INPUT or
                BacnetObjectTypes.OBJECT_BINARY_OUTPUT or
                BacnetObjectTypes.OBJECT_BINARY_VALUE;
            if (isMultiState && info.StateText?.Count > 0) enumValues = info.StateText;
            else if (isBin && info.StateText?.Count >= 2) enumValues = info.StateText;

            return new TelemetryDto
            {
                Id = $"{techDeviceId}_{info.ObjectId}",
                Name = friendlyName,
                FullName = info.ObjectName,
                Description = info.Description,
                Units = info.Units,
                Type = info.ObjectId.type.ToString(),
                Key = $"{info.KeyPrefix}_value",
                IsWritable = info.Commandable,
                EnumValues = enumValues,
                ParentId = parentId,
                ParentPath = parentPath
            };
        }

        /// <summary>
        /// Recursively collects all BacnetObjectIds referenced by nodes in a DezikoTree.
        /// </summary>
        private static void CollectReferencedIds(DezikoNode node, HashSet<BacnetObjectId> ids)
        {
            ids.Add(node.ObjectId);
            foreach (var child in node.Children)
                CollectReferencedIds(child, ids);
        }

        /// <summary>
        /// Returns true for BACnet object types that are metadata/infrastructure
        /// objects (Device, Structured View, Notification Class, etc.) rather
        /// than data-point objects that should appear in the asset tree.
        /// </summary>
        private static bool IsMetaObjectType(BacnetObjectTypes t) => t is
            BacnetObjectTypes.OBJECT_DEVICE or
            BacnetObjectTypes.OBJECT_STRUCTURED_VIEW or
            BacnetObjectTypes.OBJECT_NOTIFICATION_CLASS or
            BacnetObjectTypes.OBJECT_NOTIFICATION_FORWARDER or
            BacnetObjectTypes.OBJECT_EVENT_ENROLLMENT or
            (BacnetObjectTypes)20 or   // OBJECT_TREND_LOG
            (BacnetObjectTypes)27 or   // OBJECT_TREND_LOG_MULTIPLE
            BacnetObjectTypes.OBJECT_FILE or
            BacnetObjectTypes.OBJECT_PROGRAM or
            BacnetObjectTypes.OBJECT_GROUP;

        /// <summary>
        /// Returns a human-readable category name for grouping BACnet object types
        /// in the flat-hierarchy fallback.
        /// </summary>
        private static string GetObjectTypeCategory(BacnetObjectTypes t) => t switch
        {
            BacnetObjectTypes.OBJECT_ANALOG_INPUT => "Analog Inputs",
            BacnetObjectTypes.OBJECT_ANALOG_OUTPUT => "Analog Outputs",
            BacnetObjectTypes.OBJECT_ANALOG_VALUE => "Analog Values",
            BacnetObjectTypes.OBJECT_BINARY_INPUT => "Binary Inputs",
            BacnetObjectTypes.OBJECT_BINARY_OUTPUT => "Binary Outputs",
            BacnetObjectTypes.OBJECT_BINARY_VALUE => "Binary Values",
            BacnetObjectTypes.OBJECT_MULTI_STATE_INPUT => "Multi-State Inputs",
            BacnetObjectTypes.OBJECT_MULTI_STATE_OUTPUT => "Multi-State Outputs",
            BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE => "Multi-State Values",
            BacnetObjectTypes.OBJECT_SCHEDULE => "Schedules",
            BacnetObjectTypes.OBJECT_CALENDAR => "Calendars",
            BacnetObjectTypes.OBJECT_LOOP => "Control Loops",
            _ => "Other Objects"
        };

        private AssetNodeDto ConvertNodeToDto(
            DezikoNode node, string techDeviceId, uint deviceInstanceId,
            Dictionary<BacnetObjectId, BacnetObjectInfo> objectLookup,
            List<PathSegmentDto> currentPath,
            TreeConversionStats stats)
        {
            string uniqueId = $"{techDeviceId}_{node.ObjectId}";
            var dto = new AssetNodeDto
            {
                Id = uniqueId,
                Name = node.FriendlyName,
                Description = node.Description,
                IsView = node.IsView,
                Type = node.IsView ? "Folder" : node.ObjectId.type.ToString()
            };

            var newPath = new List<PathSegmentDto>(currentPath);
            newPath.Add(new PathSegmentDto { Id = uniqueId, Name = node.FriendlyName });
            foreach (var child in node.Children)
            {
                if (child.IsView)
                {
                    dto.Children.Add(ConvertNodeToDto(child, techDeviceId, deviceInstanceId, objectLookup, newPath, stats));
                }
                else
                {
                    stats.TotalTreeLeaves++;

                    if (objectLookup.TryGetValue(child.ObjectId, out var info))
                    {
                        // ── Resolved from discovery cache ────────────────────
                        stats.ResolvedFromCache++;

                        string objectName = info.ObjectName;
                        string friendlyName;
                        if (info.NamingPath?.Count > 0) friendlyName = info.NamingPath.Last();
                        else if (!string.IsNullOrEmpty(info.NameExtension)) friendlyName = info.NameExtension;
                        else friendlyName = string.IsNullOrWhiteSpace(info.ObjectName)
                            ? child.ObjectId.ToString()
                            : info.ObjectName.Split(new[] { '.', '\'' }, StringSplitOptions.RemoveEmptyEntries).Last();

                        string description = info.Description;
                        bool isWritable = info.Writeable || info.Commandable;
                        List<string>? enumValues = null;

                        bool isMultiState = child.ObjectId.type is
                            BacnetObjectTypes.OBJECT_MULTI_STATE_INPUT or
                            BacnetObjectTypes.OBJECT_MULTI_STATE_OUTPUT or
                            BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE;

                        bool isBinary = child.ObjectId.type is
                            BacnetObjectTypes.OBJECT_BINARY_INPUT or
                            BacnetObjectTypes.OBJECT_BINARY_OUTPUT or
                            BacnetObjectTypes.OBJECT_BINARY_VALUE;

                        if (isMultiState && info.StateText?.Count > 0)
                            enumValues = info.StateText;
                        else if (isBinary && info.StateText?.Count >= 2)
                            enumValues = info.StateText;

                        var keyPrefix = $"{techDeviceId}_{BacnetObjectInfo.Sanitise(objectName)}";
                        var TelemetryValuesKey = $"{keyPrefix}_value";

                        dto.Telemetries.Add(new TelemetryDto
                        {
                            Id = $"{techDeviceId}_{child.ObjectId}",
                            Name = friendlyName,
                            FullName = objectName,
                            Description = description,
                            Units = info.Units,
                            Type = child.ObjectId.type.ToString(),
                            Key = TelemetryValuesKey,
                            IsWritable = isWritable,
                            EnumValues = enumValues,
                            ParentId = uniqueId,
                            ParentPath = newPath
                        });
                    }
                    else
                    {
                        // ── Not in CachedObjects (filtered out during discovery) ─
                        // The tree references this object but discovery didn't keep it.
                        // Still create a minimal point using the stub's ObjectId
                        // so the item is at least visible (with "---" value).
                        stats.NotInCache++;

                        string stubName = !string.IsNullOrWhiteSpace(child.ObjectName)
                            ? child.ObjectName.Split(new[] { '.', '\'' }, StringSplitOptions.RemoveEmptyEntries).Last()
                            : child.ObjectId.ToString();

                        // Build a fallback key from ObjectId since we have no ObjectName
                        string fallbackKey = $"{techDeviceId}_{BacnetObjectInfo.ShortTypeName(child.ObjectId.type)}_{child.ObjectId.instance}_value";

                        dto.Telemetries.Add(new TelemetryDto
                        {
                            Id = $"{techDeviceId}_{child.ObjectId}",
                            Name = stubName,
                            FullName = child.ObjectId.ToString(),
                            Description = child.Description,
                            Units = "",
                            Type = child.ObjectId.type.ToString(),
                            Key = fallbackKey,
                            IsWritable = false,
                            ParentId = uniqueId,
                            ParentPath = newPath
                        });
                    }
                }
            }

            return dto;
        }

        /// <summary>Tracks conversion stats for diagnostic logging.</summary>
        private class TreeConversionStats
        {
            public int TotalTreeLeaves;
            public int ResolvedFromCache;
            public int NotInCache;
        }

        public Task<List<PropertyDto>> GetExtendedPropertiesAsync(ConnectionConfig connection, DeviceConfig device, string key)
        {
            return Task.Run(() =>
            {
                var props = new List<PropertyDto>();
                var discovered = GetDiscoveredObjects(device.Name);

                // Search by key
                var info = discovered.FirstOrDefault(i => (i.KeyPrefix + "_value") == key);
                if (info == null) return props;

                props.Add(new PropertyDto { Name = "Object ID (75)", Value = info.ObjectId.ToString() });
                props.Add(new PropertyDto { Name = "Object Name (77)", Value = info.ObjectName });
                props.Add(new PropertyDto { Name = "Description (28)", Value = info.Description });
                props.Add(new PropertyDto { Name = "Profile Name (168)", Value = info.ProfileName });
                props.Add(new PropertyDto { Name = "Category (4941)", Value = info.Category.ToString() });
                props.Add(new PropertyDto { Name = "Commandable", Value = info.Commandable ? "Yes" : "No" });
                props.Add(new PropertyDto { Name = "Writable", Value = info.Writeable ? "Yes" : "No" });
                props.Add(new PropertyDto { Name = "Friendly Path (4397)", Value = string.Join(" > ", info.NamingPath) });

                try
                {
                    using var client = new BacnetClient(new BacnetIpUdpProtocolTransport(0));
                    client.Start();
                    var address = ResolveAddress(client, device.Address ?? connection.Address ?? "", connection.Port ?? 47808, device.DeviceId ?? 0, 1000);

                    var extraPropIds = new[] {
                        BacnetPropertyIds.PROP_ALL,
                        BacnetPropertyIds.PROP_PRESENT_VALUE,
                        BacnetPropertyIds.PROP_STATUS_FLAGS,
                        BacnetPropertyIds.PROP_EVENT_STATE,
                        BacnetPropertyIds.PROP_RELIABILITY,
                        BacnetPropertyIds.PROP_OUT_OF_SERVICE,
                        BacnetPropertyIds.PROP_SETPOINT,
                        BacnetPropertyIds.PROP_PRIORITY_ARRAY,
                        BacnetPropertyIds.PROP_RELINQUISH_DEFAULT,
                        BacnetPropertyIds.PROP_WEEKLY_SCHEDULE,
                        BacnetPropertyIds.PROP_EXCEPTION_SCHEDULE,
                        BacnetPropertyIds.PROP_READ_ONLY,
                        (BacnetPropertyIds)4311, // Substitution Value
                        (BacnetPropertyIds)4312  // Substitution Active
                    };

                    var liveValues = ReadObjectProperties(client, address, info.ObjectId, extraPropIds);
                    foreach (var kv in liveValues)
                    {
                        if (kv.Value == null) continue;
                        string name = kv.Key.ToString().Replace("PROP_", "").Replace("_", " ");
                        name = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLower());
                        name = $"{name} ({(int)kv.Key})";

                        string valStr;
                        if (kv.Key == BacnetPropertyIds.PROP_WEEKLY_SCHEDULE
                            || kv.Key == BacnetPropertyIds.PROP_EXCEPTION_SCHEDULE)
                        {
                            valStr = System.Text.Json.JsonSerializer.Serialize(
                                BacnetValueConverter.FormatSchedule(kv.Value));
                        }
                        else if (kv.Key == BacnetPropertyIds.PROP_PRIORITY_ARRAY && kv.Value is System.Collections.Generic.IList<BacnetValue> pArray)
                        {
                            var parts = new List<string>();
                            for (int i = 0; i < pArray.Count; i++)
                            {
                                var v = pArray[i].Value;
                                if (v != null && v.ToString() != "Null")
                                    parts.Add($"P{i + 1}: {v}");
                            }
                            valStr = parts.Count > 0 ? string.Join(", ", parts) : "No active priorities";
                        }
                        else if (kv.Value is System.Collections.IList list)
                        {
                            // Unwrap nested BacnetValue lists (BACnet array properties)
                            var parts = new List<string>();
                            foreach (var item in list)
                            {
                                if (item is BacnetValue bv)
                                    FlattenBacnetValue(bv, parts);
                                else if (item != null)
                                    parts.Add(item.ToString() ?? "");
                            }
                            valStr = parts.Count > 0 ? string.Join(", ", parts) : "";
                        }
                        else
                        {
                            valStr = kv.Value.ToString() ?? "";
                        }

                        if (props.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                        props.Add(new PropertyDto { Name = name, Value = valStr });
                    }
                }
                catch { /* live read failed, return cached properties only */ }

                // ── Schedule metadata for the editor UI ──────────────────────
                if (info.ObjectId.type == BacnetObjectTypes.OBJECT_SCHEDULE)
                {
                    // Detect value type from Schedule Default
                    var defProp = props.FirstOrDefault(p => p.Name == "Schedule Default");
                    string schedType = "real"; // default
                    if (defProp != null)
                    {
                        if (int.TryParse(defProp.Value, out int defVal) && (defVal == 0 || defVal == 1))
                            schedType = "boolean";
                        else if (int.TryParse(defProp.Value, out _))
                            schedType = "enumerated";
                    }

                    // Check state text from the cached object
                    if (info.StateText?.Count > 0)
                    {
                        schedType = "enumerated";
                        props.Add(new PropertyDto
                        {
                            Name = "_scheduleStates",
                            Value = JsonSerializer.Serialize(info.StateText)
                        });
                    }
                    else
                    {
                        var prop4460 = props.FirstOrDefault(p => p.Name == "4460 (4460)");
                        if (prop4460 != null && !string.IsNullOrWhiteSpace(prop4460.Value))
                        {
                            schedType = "enumerated";
                            var states = prop4460.Value.Split(',').Select(s => s.Trim()).ToList();
                            props.Add(new PropertyDto
                            {
                                Name = "_scheduleStates",
                                Value = JsonSerializer.Serialize(states)
                            });
                        }
                    }

                    props.Add(new PropertyDto { Name = "_scheduleValueType", Value = schedType });
                }

                return props;
            });
        }

        /// <summary>
        /// Recursively unwraps a <see cref="BacnetValue"/> to extract displayable strings.
        /// Handles nested List&lt;BacnetValue&gt; wrappers produced by the BACnet library for array properties.
        /// </summary>
        private static void FlattenBacnetValue(BacnetValue v, List<string> result)
        {
            if (v.Value is IList<BacnetValue> nested)
            {
                foreach (var inner in nested)
                    FlattenBacnetValue(inner, result);
            }
            else if (v.Value != null)
            {
                string s = v.Value.ToString() ?? "";
                if (!string.IsNullOrEmpty(s)) result.Add(s);
            }
        }
    }
}
