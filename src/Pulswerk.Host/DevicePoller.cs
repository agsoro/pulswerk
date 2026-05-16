// DevicePoller.cs – per-device poll-and-publish logic
//
//  Extracted from the monolithic PollAndPublishAsync to isolate
//  polling concerns from orchestration.

using System;
using System.Collections.Generic;
using System.Linq;
using Pulswerk.Core;
using Pulswerk.Dashboard;
using Pulswerk.Drivers;
using Pulswerk.Drivers.BACnet;
using Pulswerk.Storage;
using System.Collections.Concurrent;

namespace Pulswerk.Host
{
    using Attributes = Dictionary<string, string>;
    using DataPointValues = Dictionary<string, object>;

    /// <summary>
    /// Reads one device per call, publishes data points + attributes,
    /// and manages failure tracking / alarm lifecycle.
    /// </summary>
    sealed class DevicePoller
    {
        readonly Dictionary<string, IDeviceDriver> _drivers;
        readonly DashboardDataService? _dataService;
        readonly ConcurrentDictionary<string, byte> _offlineDevices;
        readonly ConcurrentDictionary<string, DateTime> _lastPolledAt;

        readonly Dictionary<string, int> _failCounts = new();
        readonly Dictionary<string, DateTime> _lastAttempt = new();

        const int FailThreshold = 10;

        public DevicePoller(
            Dictionary<string, IDeviceDriver> drivers,
            DashboardDataService? dataService,
            ConcurrentDictionary<string, byte> offlineDevices,
            ConcurrentDictionary<string, DateTime> lastPolledAt)
        {
            _drivers = drivers;
            _dataService = dataService;
            _offlineDevices = offlineDevices;
            _lastPolledAt = lastPolledAt;
        }

        // =================================================================
        //  Main entry — called once per loop tick per device
        // =================================================================

        public void PollAndPublish(
            DeviceConfig device, ConnectionConfig conn,
            DataPointStore dataStore, AlarmStore alarmStore,
            int deviceIntervalMs)
        {
            try
            {
                var reader = _drivers[device.Name];

                DataPointValues dataPointValues;
                Attributes attributes = new();

                // ── BACnet COV mode ──────────────────────────────────────
                if (reader is BacnetDriver bacnetDrv && device.EffectiveCov is { Enabled: true })
                {
                    ServiceCovDevice(bacnetDrv, device, conn, dataStore, reader);
                    return;
                }

                // ── Polling mode (non-COV BACnet / Modbus) ───────────────
                if (!ShouldPollNow(device.Name, deviceIntervalMs))
                    return;

                // Check if device is recovering from offline — used to trigger
                // trend log backfill for any missed history during the outage.
                bool wasOffline = _offlineDevices.ContainsKey(device.Name);

                if (reader is BacnetDriver br)
                {
                    var result = br.ReadFull(conn, device, alarmStore, dataStore,
                        isRecovery: wasOffline);
                    dataPointValues = result.DataPointValues;
                    attributes = result.Attributes;
                }
                else
                {
                    dataPointValues = reader.Read(conn, device);
                }

                if (dataPointValues.Count > 0)
                    PublishDataPoints(dataPointValues, dataStore, device, reader);

                if (attributes.Count > 0)
                    _dataService?.UpdateAttributes(attributes);

                // ── Mark device as recently seen ─────────────────────────
                _lastPolledAt[device.Name] = DateTime.UtcNow;
                _failCounts[device.Name] = 0;

                if (_offlineDevices.TryRemove(device.Name, out _))
                {
                    alarmStore.ClearByOriginAndType(device.Name, "Communication Loss");
                    Log.Info(
                        $"[{reader.DriverName,-10}] {device.Name,-38} ✓ back online");
                }
            }
            catch (Exception ex)
            {
                TrackFailure(device, alarmStore, ex);
            }
        }

        // =================================================================
        //  COV service tick (1 s cadence, lightweight)
        // =================================================================

        void ServiceCovDevice(
            BacnetDriver driver, DeviceConfig device,
            ConnectionConfig conn, DataPointStore dataStore,
            IDeviceDriver reader)
        {
            var result = driver.ServiceCovDevice(conn, device);
            var dataPointValues = result.DataPointValues;
            var attributes = result.Attributes;

            if (dataPointValues.Count > 0)
            {
                dataStore.InsertBatch(dataPointValues);
                var persisted = _dataService?.UpdateDataPoints(dataPointValues);
                if (persisted != null)
                {
                    foreach (var p in persisted)
                        dataStore.Insert(p.Key,
                            new DateTimeOffset(p.Value.ts).ToUnixTimeMilliseconds(),
                            p.Value.val);
                }
            }

            if (attributes.Count > 0)
                _dataService?.UpdateAttributes(attributes);

            _lastPolledAt[device.Name] = DateTime.UtcNow;

            if (attributes.Count > 0 || dataPointValues.Count > 0)
                Log.Debug(
                    $"[{reader.DriverName,-10}] {device.Name,-38} " +
                    $"[COV] attrs={attributes.Count} fallback-dp={dataPointValues.Count}");
        }

        // =================================================================
        //  Publish data point to dashboard + InfluxDB
        // =================================================================

        void PublishDataPoints(
            DataPointValues dataPointValues, DataPointStore dataStore,
            DeviceConfig device, IDeviceDriver reader)
        {
            dataStore.InsertBatch(dataPointValues);

            var persisted = _dataService?.UpdateDataPoints(dataPointValues);
            if (persisted != null)
            {
                foreach (var p in persisted)
                    dataStore.Insert(p.Key,
                        new DateTimeOffset(p.Value.ts).ToUnixTimeMilliseconds(),
                        p.Value.val);
            }

            // For non-BACnet readers, also scope keys by device ID
            if (reader is not BacnetDriver)
            {
                var scoped = dataPointValues.ToDictionary(
                    kv => $"{device.Id}_{kv.Key}",
                    kv => kv.Value);
                var persistedScoped = _dataService?.UpdateDataPoints(scoped);
                if (persistedScoped != null)
                {
                    foreach (var p in persistedScoped)
                        dataStore.Insert(p.Key,
                            new DateTimeOffset(p.Value.ts).ToUnixTimeMilliseconds(),
                            p.Value.val);
                }
                dataStore.InsertBatch(scoped);
            }
        }

        // =================================================================
        //  Rate limiting
        // =================================================================

        bool ShouldPollNow(string deviceName, int intervalMs)
        {
            var now = DateTime.UtcNow;
            _lastAttempt.TryGetValue(deviceName, out var last);
            if (now - last < TimeSpan.FromMilliseconds(intervalMs))
                return false;

            _lastAttempt[deviceName] = now;
            return true;
        }

        // =================================================================
        //  Failure tracking & Communication Loss alarms
        // =================================================================

        void TrackFailure(DeviceConfig device, AlarmStore alarmStore, Exception? ex = null)
        {
            _failCounts.TryGetValue(device.Name, out int count);
            _failCounts[device.Name] = ++count;

            // ── First failure: log the actual error for diagnostics ──────
            if (count == 1 && ex != null)
            {
                Log.Warning(
                    $"[{device.DeviceType,-10}] {device.Name,-38} " +
                    $"poll failed: {ex.GetType().Name}: {ex.Message}");
            }

            // ── At threshold: raise Communication Loss alarm ─────────────
            if (count == FailThreshold)
            {
                Log.Warning(
                    $"[{device.DeviceType,-10}] {device.Name,-38} " +
                    $"offline ({count} consecutive failures)");

                if (_offlineDevices.TryAdd(device.Name, 0))
                {
                    alarmStore.CreateOrUpdate(
                        device.Name, "DEVICE",
                        "Communication Loss", "CRITICAL",
                        $"Device {device.Name} is not responding after {count} attempts.");
                }
            }

            // ── Periodic status: re-log every 100 failures ───────────────
            if (count > FailThreshold && count % 100 == 0)
            {
                Log.Warning(
                    $"[{device.DeviceType,-10}] {device.Name,-38} " +
                    $"still offline ({count} consecutive failures)");
            }

            // ── Force connection reset for Modbus at escalation points ───
            // Purge the pooled TCP socket to force a fresh connection attempt.
            // This handles cases where the gateway is back but the old socket is dead.
            bool isModbus = device.ConnectionId != null &&
                !device.DeviceType.Equals("bacnet", StringComparison.OrdinalIgnoreCase) &&
                !device.DeviceType.Equals("deziko", StringComparison.OrdinalIgnoreCase);

            if (isModbus && (count % 50 == 0))
            {
                Log.Warning(
                    $"[{device.DeviceType,-10}] {device.Name,-38} " +
                    $"prolonged outage ({count} fails) — forcing Modbus connection reset for '{device.ConnectionId}'");
                Pulswerk.Drivers.Modbus.ModbusConnection.FullReset(device.ConnectionId!);
            }
        }
    }
}
